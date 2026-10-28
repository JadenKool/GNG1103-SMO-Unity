using System;
using System.Globalization;
using UnityEngine;

namespace Eosat
{
    public enum PanelState { Unknown, Stowed, Deployed }
    public enum AntennaState { Unknown, Stowed, Deployed, Jammed }

    /// <summary>One validated telemetry update.</summary>
    public class TelemetrySample
    {
        public bool hasTime;
        public double unixTime;
        public Vector3 posEciKm;
        public float qw, qx, qy, qz;      // body -> ECI attitude quaternion
        public PanelState panels;
        public AntennaState antenna;
        public bool hasEclipse;
        public bool eclipse;
        public float receivedRealtime;

        // Extra fields filled by the client's smo-vr/1 format (defaults when not sent).
        public long seq = -1;
        public bool hasWings;                     // true -> use per-wing fractions below
        public float wingPyFraction, wingMyFraction;
        public float earthRadiusKm;               // 0 = not sent, use EciMath.EarthRadiusKm
        public string aocsMode = "", pointing = "", scMode = "", phase = "", groundStation = "";
        public bool inContact;
        public bool hasEarthDirBody; public Vector3 earthDirBody;   // simulator's Earth direction in body axes (used by the self-test)
    }

    /// <summary>
    /// Legacy/test wire format (built-in SimulatedSpacecraft):
    /// {"t": 1791460000.0, "pos_eci_km": [x, y, z], "q": [w, x, y, z],
    ///  "panels": "deployed", "antenna": "stowed", "eclipse": false}
    /// </summary>
    [Serializable]
    public class TelemetryJson
    {
        public double t;
        public float[] pos_eci_km;
        public float[] q;
        public string panels;
        public string antenna;
        public bool eclipse;
    }

    // ---------- Client simulator format: smo-vr/1 (VR_INTERFACE_ICD) ----------
    // {"type":"state","schema":"smo-vr/1","seq":1,"sim_time":"2026-08-20T12:00:00.000Z","met_s":0.0,
    //  "orbit":{"pos_eci_km":[..],"alt_km":500.9,...},
    //  "attitude":{"q_body_to_eci":[x,y,z,w],"aocs_mode":"OFF","pointing":"NADIR",...},   <- scalar LAST
    //  "sun":{"in_eclipse":false,...}, "earth":{"gmst_deg":..,"radius_km":6371.0},
    //  "deploy":{"wing_py":{"state":"STOWED","fraction":0.0},"wing_my":{...},"antenna":{"state":"STOWED",...}},
    //  "mission":{"sc_mode":"SAFE","phase":"SEPARATION_TIMER","in_contact":false,"ground_station":""}}
    // Other message types ("hello", future ones) are ignored, not counted as errors. Unknown fields are ignored.

    [Serializable] public class SmoEnvelope { public string type; public string schema; }
    [Serializable] public class SmoOrbit { public float[] pos_eci_km; public float alt_km; }
    [Serializable] public class SmoAttitude { public float[] q_body_to_eci; public string aocs_mode; public string pointing; }
    [Serializable] public class SmoSun { public bool in_eclipse; }
    [Serializable] public class SmoEarth { public float radius_km; public float[] dir_body; }
    [Serializable] public class SmoWing { public string state; public float fraction; }
    [Serializable] public class SmoAntenna { public string state; public bool deployed; }
    [Serializable] public class SmoDeploy { public SmoWing wing_py; public SmoWing wing_my; public SmoAntenna antenna; }
    [Serializable] public class SmoMission { public string sc_mode; public string phase; public bool in_contact; public string ground_station; }

    [Serializable]
    public class SmoState
    {
        public string type;
        public long seq;
        public string sim_time;
        public SmoOrbit orbit;
        public SmoAttitude attitude;
        public SmoSun sun;
        public SmoEarth earth;
        public SmoDeploy deploy;
        public SmoMission mission;
    }

    public static class TelemetryParser
    {
        static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>Backwards-compatible overload (no "ignored" flag).</summary>
        public static bool TryParse(string json, bool quaternionScalarFirst, out TelemetrySample sample, out string error)
            => TryParse(json, quaternionScalarFirst, out sample, out error, out _);

        /// <summary>
        /// Parses either the client's smo-vr/1 messages or the legacy flat format.
        /// ignored = true for messages that are valid but carry no state (e.g. "hello").
        /// </summary>
        public static bool TryParse(string json, bool quaternionScalarFirst, out TelemetrySample sample, out string error, out bool ignored)
        {
            sample = null; error = null; ignored = false;
            if (string.IsNullOrWhiteSpace(json)) { error = "empty message"; return false; }

            SmoEnvelope env;
            try { env = JsonUtility.FromJson<SmoEnvelope>(json); }
            catch (Exception e) { error = "unreadable JSON (" + e.Message + ")"; return false; }
            if (env == null) { error = "empty message"; return false; }

            if (!string.IsNullOrEmpty(env.type))
            {
                if (env.type != "state") { ignored = true; error = "ignored '" + env.type + "' message"; return false; }
                return TryParseSmo(json, out sample, out error);
            }
            return TryParseLegacy(json, quaternionScalarFirst, out sample, out error);
        }

        static bool TryParseSmo(string json, out TelemetrySample sample, out string error)
        {
            sample = null; error = null;
            SmoState j;
            try { j = JsonUtility.FromJson<SmoState>(json); }
            catch (Exception e) { error = "unreadable JSON (" + e.Message + ")"; return false; }

            if (j.orbit == null || j.orbit.pos_eci_km == null || j.orbit.pos_eci_km.Length < 3) { error = "missing orbit.pos_eci_km"; return false; }
            if (!CheckPosition(j.orbit.pos_eci_km, out var p, out error)) return false;

            if (j.attitude == null || j.attitude.q_body_to_eci == null || j.attitude.q_body_to_eci.Length < 4) { error = "missing attitude.q_body_to_eci"; return false; }
            float[] q = j.attitude.q_body_to_eci;                       // [x, y, z, w] scalar LAST
            if (!CheckQuat(q[3], q[0], q[1], q[2], out float w, out float x, out float y, out float z, out error)) return false;

            sample = new TelemetrySample { posEciKm = p, qw = w, qx = x, qy = y, qz = z, seq = j.seq };

            if (!string.IsNullOrEmpty(j.sim_time) &&
                DateTime.TryParse(j.sim_time, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            {
                sample.hasTime = true;
                sample.unixTime = (dt - UnixEpoch).TotalSeconds;
            }

            if (j.deploy != null)
            {
                if (j.deploy.wing_py != null && j.deploy.wing_my != null && json.Contains("\"wing_py\""))
                {
                    sample.hasWings = true;
                    sample.wingPyFraction = WingFraction(j.deploy.wing_py);
                    sample.wingMyFraction = WingFraction(j.deploy.wing_my);
                    sample.panels = sample.wingPyFraction >= 0.999f && sample.wingMyFraction >= 0.999f ? PanelState.Deployed
                                  : sample.wingPyFraction <= 0.001f && sample.wingMyFraction <= 0.001f ? PanelState.Stowed
                                  : PanelState.Deployed;   // partly open = on its way out
                }
                if (j.deploy.antenna != null) sample.antenna = ParseAntenna(j.deploy.antenna.state);
            }

            if (j.sun != null && json.Contains("\"in_eclipse\"")) { sample.hasEclipse = true; sample.eclipse = j.sun.in_eclipse; }
            if (j.earth != null && j.earth.radius_km > 1000f) sample.earthRadiusKm = j.earth.radius_km;
            if (j.earth != null && j.earth.dir_body != null && j.earth.dir_body.Length >= 3)
            { sample.hasEarthDirBody = true; sample.earthDirBody = new Vector3(j.earth.dir_body[0], j.earth.dir_body[1], j.earth.dir_body[2]); }
            if (j.attitude != null) { sample.aocsMode = j.attitude.aocs_mode ?? ""; sample.pointing = j.attitude.pointing ?? ""; }
            if (j.mission != null)
            {
                sample.scMode = j.mission.sc_mode ?? ""; sample.phase = j.mission.phase ?? "";
                sample.inContact = j.mission.in_contact; sample.groundStation = j.mission.ground_station ?? "";
            }
            return true;
        }

        static float WingFraction(SmoWing w)
        {
            float f = Mathf.Clamp01(w.fraction);
            string s = (w.state ?? "").ToUpperInvariant();
            if (s == "DEPLOYED" && f <= 0f) f = 1f;     // state wins if fraction missing
            if (s == "STOWED") f = 0f;
            return f;
        }

        static bool TryParseLegacy(string json, bool quaternionScalarFirst, out TelemetrySample sample, out string error)
        {
            sample = null; error = null;
            TelemetryJson j;
            try { j = JsonUtility.FromJson<TelemetryJson>(json); }
            catch (Exception e) { error = "unreadable JSON (" + e.Message + ")"; return false; }
            if (j == null) { error = "empty message"; return false; }

            if (j.pos_eci_km == null || j.pos_eci_km.Length < 3) { error = "missing pos_eci_km"; return false; }
            if (!CheckPosition(j.pos_eci_km, out var p, out error)) return false;

            if (j.q == null || j.q.Length < 4) { error = "missing q"; return false; }
            float iw, ix, iy, iz;
            if (quaternionScalarFirst) { iw = j.q[0]; ix = j.q[1]; iy = j.q[2]; iz = j.q[3]; }
            else { ix = j.q[0]; iy = j.q[1]; iz = j.q[2]; iw = j.q[3]; }
            if (!CheckQuat(iw, ix, iy, iz, out float w, out float x, out float y, out float z, out error)) return false;

            sample = new TelemetrySample
            {
                hasTime = j.t > 0,
                unixTime = j.t,
                posEciKm = p,
                qw = w, qx = x, qy = y, qz = z,
                panels = ParsePanels(j.panels),
                antenna = ParseAntenna(j.antenna),
                hasEclipse = json.Contains("\"eclipse\""),
                eclipse = j.eclipse
            };
            return true;
        }

        static bool CheckPosition(float[] a, out Vector3 p, out string error)
        {
            error = null;
            p = new Vector3(a[0], a[1], a[2]);
            if (!EciMath.IsFinite(p)) { error = "position is not a number"; return false; }
            float r = p.magnitude;
            // 0.98 x 6378 km = 6250 km, so the client's 6371 km Earth (alt 500 km -> r ~6872 km) passes.
            if (r < EciMath.EarthRadiusKm * 0.98 || r > 100000f) { error = $"position radius {r:F0} km out of range"; return false; }
            return true;
        }

        static bool CheckQuat(float iw, float ix, float iy, float iz, out float w, out float x, out float y, out float z, out string error)
        {
            error = null; w = x = y = z = 0f;
            float n = Mathf.Sqrt(iw * iw + ix * ix + iy * iy + iz * iz);
            if (float.IsNaN(n) || n < 0.9f || n > 1.1f) { error = $"quaternion norm {n:F3} is not ~1"; return false; }
            w = iw / n; x = ix / n; y = iy / n; z = iz / n;
            return true;
        }

        static PanelState ParsePanels(string s)
        {
            if (string.IsNullOrEmpty(s)) return PanelState.Unknown;
            s = s.ToLowerInvariant();
            if (s.Contains("deploy")) return PanelState.Deployed;
            if (s.Contains("stow")) return PanelState.Stowed;
            return PanelState.Unknown;
        }

        static AntennaState ParseAntenna(string s)
        {
            if (string.IsNullOrEmpty(s)) return AntennaState.Unknown;
            s = s.ToLowerInvariant();
            if (s.Contains("jam") || s.Contains("fail") || s.Contains("stuck")) return AntennaState.Jammed;
            if (s.Contains("deploy")) return AntennaState.Deployed;
            if (s.Contains("stow")) return AntennaState.Stowed;
            return AntennaState.Unknown;
        }
    }
}
