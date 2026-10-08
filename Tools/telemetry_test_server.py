"""
EOSAT-1 telemetry test server (stand-in until the client's test tool / simulator is available).

Serves newline-delimited JSON at 1 Hz on TCP port 8100, in the format the Unity visualizer expects:
  {"t": <unix s>, "pos_eci_km": [x, y, z], "q": [w, x, y, z], "panels": "...", "antenna": "...", "eclipse": bool}

Usage:
  python telemetry_test_server.py                 # 500 km sun-synchronous orbit, 20x time warp
  python telemetry_test_server.py --warp 1        # real time
  python telemetry_test_server.py --stall-every 30 --stall-for 8   # drop the feed for 8 s every 30 s
  python telemetry_test_server.py --bad-every 15  # send a corrupt packet every 15 s

In Unity: select the "Telemetry" object, set Source = Tcp (port 8100), press Play.
"""
import argparse, json, math, socket, time

MU = 398600.4418
RE = 6378.137


def sun_dir_eci(jd):
    n = jd - 2451545.0
    L = 280.460 + 0.9856474 * n
    g = math.radians(357.528 + 0.9856003 * n)
    lam = math.radians(L + 1.915 * math.sin(g) + 0.020 * math.sin(2 * g))
    eps = math.radians(23.439 - 0.0000004 * n)
    return (math.cos(lam), math.cos(eps) * math.sin(lam), math.sin(eps) * math.sin(lam))


def norm(v):
    m = math.sqrt(sum(c * c for c in v))
    return tuple(c / m for c in v)


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def mat_to_quat(X, Y, Z):
    """Columns X, Y, Z = body axes expressed in ECI -> quaternion (w, x, y, z), body->ECI."""
    m00, m01, m02 = X[0], Y[0], Z[0]
    m10, m11, m12 = X[1], Y[1], Z[1]
    m20, m21, m22 = X[2], Y[2], Z[2]
    tr = m00 + m11 + m22
    if tr > 0:
        s = math.sqrt(tr + 1.0) * 2
        return (0.25 * s, (m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s)
    if m00 > m11 and m00 > m22:
        s = math.sqrt(1.0 + m00 - m11 - m22) * 2
        return ((m21 - m12) / s, 0.25 * s, (m01 + m10) / s, (m02 + m20) / s)
    if m11 > m22:
        s = math.sqrt(1.0 + m11 - m00 - m22) * 2
        return ((m02 - m20) / s, (m01 + m10) / s, 0.25 * s, (m12 + m21) / s)
    s = math.sqrt(1.0 + m22 - m00 - m11) * 2
    return ((m10 - m01) / s, (m02 + m20) / s, (m12 + m21) / s, 0.25 * s)


def state(t_sim, epoch, alt, inc, raan, roll_deg):
    a = RE + alt
    u = math.sqrt(MU / a ** 3) * (t_sim - epoch)
    cO, sO, ci, si, cu, su = math.cos(raan), math.sin(raan), math.cos(inc), math.sin(inc), math.cos(u), math.sin(u)
    pos = (a * (cO * cu - sO * ci * su), a * (sO * cu + cO * ci * su), a * si * su)
    vel = norm((-cO * su - sO * ci * cu, -sO * su + cO * ci * cu, si * cu))
    Z = norm(tuple(-c for c in pos))          # body Z -> nadir
    X = vel                                   # body X -> velocity
    Y = cross(Z, X)
    r = math.radians(roll_deg)                # roll about body Z
    Xr = tuple(math.cos(r) * X[i] + math.sin(r) * Y[i] for i in range(3))
    Yr = cross(Z, Xr)
    return pos, mat_to_quat(Xr, Yr, Z)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=8100)
    ap.add_argument("--warp", type=float, default=20.0)
    ap.add_argument("--alt", type=float, default=500.0)
    ap.add_argument("--stall-every", type=float, default=0)
    ap.add_argument("--stall-for", type=float, default=8)
    ap.add_argument("--bad-every", type=float, default=0)
    args = ap.parse_args()

    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind((args.host, args.port))
    srv.listen(1)
    print(f"Telemetry test server on {args.host}:{args.port} (Ctrl+C to stop)")

    while True:
        conn, addr = srv.accept()
        print("Visualizer connected from", addr)
        start = time.time()
        epoch = start
        roll = 0.0
        try:
            while True:
                now = time.time()
                el = now - start
                t_sim = epoch + el * args.warp
                roll = (roll + 4.0) % 360
                if args.stall_every and (el % args.stall_every) > args.stall_every - args.stall_for:
                    time.sleep(1.0)
                    continue
                if args.bad_every and int(el) % int(args.bad_every) == 0 and el > 1:
                    conn.sendall(b'{"pos_eci_km":[1,2,3],"q":[2,0,0,0]}\n')
                    time.sleep(1.0)
                    continue
                pos, q = state(t_sim, epoch, args.alt, math.radians(97.4), math.radians(30), roll)
                jd = t_sim / 86400.0 + 2440587.5
                s = sun_dir_eci(jd)
                along = sum(pos[i] * s[i] for i in range(3))
                perp = math.sqrt(max(0.0, sum(p * p for p in pos) - along * along))
                msg = {
                    "t": round(t_sim, 3),
                    "pos_eci_km": [round(c, 3) for c in pos],
                    "q": [round(c, 6) for c in q],
                    "panels": "deployed" if el > 8 else "stowed",
                    "antenna": "deployed" if el > 15 else "stowed",
                    "eclipse": along < 0 and perp < RE,
                }
                conn.sendall((json.dumps(msg) + "\n").encode())
                time.sleep(1.0)
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
            print("Visualizer disconnected; waiting for reconnect...")
        finally:
            conn.close()


if __name__ == "__main__":
    main()
