# GNG1103-SMO-Unity

EOSAT-1 spacecraft visualization for the GNG1103 SMO client project (Group 13).
Unity 6 + OpenXR: a VR view of EOSAT-1 orbiting the Earth, and a mixed-reality tabletop view
(globe and satellite on your real desk), both driven by the client's simulator data.

| Scene | What it shows |
|---|---|
| `Assets/Scenes/EOSAT_Visualization.unity` | Main view: Earth (rotating by real sidereal time), EOSAT-1, real Sun direction, HUD. Zoom out and the satellite becomes a **yellow dot** (Concept 4). |
| `Assets/Scenes/EOSAT_MR_Tabletop.unity` | Mixed reality: 24 cm globe on your desk with the true-scale orbit, ground-station links, a hovering EOSAT-1 model, and a **wrist tablet** with Data / Antenna / Solar Panels switches (built on Josh's tabletop concept). |

Both scenes replay the client's recorded orbit (`Assets/StreamingAssets/sample_vr_frames.ndjson`,
1 full orbit: 500.9 km, 98° inclination, RAAN 120°) automatically, so they work with no setup.

---

## Getting started

### 1. Install (one time)
1. Install **Unity Hub**: <https://unity.com/download> and sign in with a free Unity account (Unity Personal is free).
2. Install **Unity 6000.6.2f1** (Unity Hub → Installs → Install Editor → Archive).
   It must be this exact version. If you skip this, Hub offers to install it when you open the project.
3. Install **GitHub Desktop**: <https://desktop.github.com>.

### 2. Get the project
1. GitHub Desktop → **File → Clone repository → URL** → `https://github.com/JadenKool/GNG1103-SMO-Unity`.
2. Unity Hub → **Add → Add project from disk** → pick the cloned `GNG1103-SMO-Unity` folder → open it.
3. The first open takes **5–10 minutes** while Unity downloads the XR packages and builds its `Library` folder. This is normal.

### 3. Run a scene
1. In the **Project** window open `Assets/Scenes` and double-click a scene.
2. Press **Play** (▶).

No headset? Both scenes switch to keyboard + mouse automatically:

| Main scene | MR tabletop scene |
|---|---|
| Drag = orbit camera | Right-drag = look around |
| Scroll = zoom (zoom far out to see the yellow dot) | WASD = move, Q/E = down/up, Shift = faster |
| 1 = spacecraft view, 2 = Earth view | 1 = Data overlay, 2 = Antenna, 3 = Solar panels |
| H = help | Click the tablet switches with the mouse; R = re-place the globe |

If a scene looks broken or empty, rebuild it from code with the **EOSAT** menu:
**EOSAT → Build Visualization Scene** or **EOSAT → Build MR Tabletop Scene**.

---

## Using a Meta Quest (optional)

Runs from the Unity Editor over **Meta Horizon Link** (Windows PC only). No Android build needed.

1. Install the **Meta Horizon Link** app on the PC and connect the headset (USB-C cable or Air Link).
2. Link app → Settings → General → **OpenXR Runtime: Set Meta Horizon Link as active**.
3. Put the headset on, then press **Play** in Unity.
   - VR controls: thumbstick up/down = zoom, left/right = snap turn, A/X = switch spacecraft/Earth.

**Extra for the MR tabletop scene (passthrough):**
- Your Meta account needs a free developer organization (<https://developers.meta.com>); then
  Link app → Settings → **Developer** → turn on *Developer runtime features*, *Pass-through over Meta Horizon Link*
  and *Spatial data over Meta Horizon Link*, and restart Unity.
- In the headset: Settings → Physical Space → **Space Setup** (mark your desk as a *Table*), and
  Settings → Movement Tracking → **Hand tracking** on.
- Turn your **left palm** toward your face to show the tablet; tap the switches with your right index finger.
- Passthrough only shows in the headset (the Game view stays black there). Quest 2 passthrough is black-and-white.

`XR_ERROR_FORM_FACTOR_UNAVAILABLE` in the Console when you press Play just means no headset was found; the scenes still run on desktop.

---

## Live data from the client's simulator

The client package (`GNG1103_F2026`, not in this repo) contains `vr_stub.py` and `sample_vr_frames.ndjson`.

1. Select the **Telemetry** object in the scene → `TelemetryHub` → **Source = Tcp**, host `127.0.0.1`, port `8100`.
2. Press **Play** in Unity first, then start the stub in a terminal in the client folder:
   ```
   python vr_stub.py --replay sample_vr_frames.ndjson --speed 5
   python vr_stub.py --chaos --speed 10 --rate 2      # robustness test
   ```
3. To score the test plan: add the **EosatTestRecorder** component to the Telemetry object, run the feed,
   then right-click the component → **Print Report**.

Format notes: the parser reads the client's `smo-vr/1` messages (quaternion `[x, y, z, w]`, scalar last)
and the older flat format. `Tools/telemetry_test_server.py` is our original stand-in server for the flat format.

---

## Working together (please read before pushing)

- Always **Fetch / Pull** in GitHub Desktop **before** you open Unity and start working.
- Only commit `Assets/`, `Packages/` and `ProjectSettings/` (the `.gitignore` already hides `Library/`, `Logs/`, `Temp/`, `UserSettings/`).
- Always commit a file **together with its `.meta` file**.
- Unity scene files don't merge well: **don't edit the same scene as someone else at the same time.** Say in the group chat which scene you're working on, or work in your own copy of the scene.
- Use **Unity 6000.6.2f1** only. Opening the project in another version changes many files.
- Write a short commit message saying what you changed, then **Push origin**.
- No paid services: the project uses no Unity AI, Unity Version Control or cloud services, and no API keys. Please don't add packages that need a subscription.

## Project layout

```
Assets/
  EOSAT/Scripts/     telemetry pipeline, spacecraft + Earth motion, HUD, yellow dot, replay feed, test recorder
  EOSAT/MR/          mixed-reality tabletop: desk anchor, wrist tablet, overlay, desktop fallback
  EOSAT/Editor/      EOSAT menu: scene builders, VR/MR setup, parser self-test
  EOSAT/Materials, Meshes, Shaders, Textures
  Scenes/            EOSAT_Visualization, EOSAT_MR_Tabletop
  StreamingAssets/   client sample orbit data
Tools/               telemetry_test_server.py (legacy test server)
```
