# GNG1103-SMO-Unity

EOSAT-1 spacecraft visualization for the GNG1103 SMO client project (Group 13).
Unity 6 + OpenXR. A VR view of EOSAT-1 orbiting the Earth, and a mixed-reality tabletop view (globe and
satellite floating in your real room), both driven by the client's simulator data.
- **Main scene**: Unity's VR template rig (XR Interaction Toolkit 3.7).
- **MR scene**: Meta's SDKs - **Meta XR Core SDK** (`OVRCameraRig`, `OVRPassthroughLayer`) and the
  **Meta Interaction SDK** (hands + controllers: poke, ray, grab, distance grab). Both v207, free, from the Unity registry.

| Scene | What it shows |
|---|---|
| `Assets/Scenes/EOSAT_Visualization.unity` | Main view: Earth (rotating by real sidereal time), EOSAT-1, real Sun direction, HUD. Zoom out and the satellite becomes a **yellow dot** (Concept 4). |
| `Assets/Scenes/EOSAT_MR_Tabletop.unity` | Mixed reality (colour passthrough): a 24 cm **free-floating globe** with the true-scale orbit, ground-station links and a hovering EOSAT-1 model, plus a **floating tablet** (Data / AR-VR / Antenna / Solar Panels switches, live Alt / Vel / Eclipse / modes, data-source buttons Sim / TCP 8100 / WebSocket 8101). Both hover wherever you let go of them. |

Both scenes replay the client's recorded orbit (`Assets/StreamingAssets/sample_vr_frames.ndjson`,
1 full orbit: 500.9 km, 98° inclination, RAAN 120°) automatically, so they work with no setup.

---

## Getting started

### 1. Install (one time)
1. Install **Unity Hub**: <https://unity.com/download> and sign in with a free Unity account (Unity Personal is free).
2. Install **Unity 6000.6.2f1** (Unity Hub → Installs → Install Editor → Archive).
   It must be this exact version. If you skip this, Hub offers to install it when you open the project.
3. Install **GitHub Desktop**: <https://desktop.github.com>.
4. Recommended: in Unity Hub → Installs → 6000.6.2f1 → ⚙ → **Add modules** → **Android Build Support**
   (with *Android SDK & NDK Tools* and *OpenJDK*). The Meta SDK looks for the Android tools every time scripts reload;
   without them Unity pops up **"Android SDK not found"** (just click OK - it's harmless). It's also what you need
   to build a standalone Quest app later.

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
| 1 = spacecraft view, 2 = Earth view | 1 = Data overlay, 2 = AR/VR, 3 = Antenna, 4 = Solar panels |
| H = help | 5 = Sim, 6 = TCP 8100, 7 = WebSocket 8101 |
| | Click the tablet with the mouse; R = re-place the globe |

If a scene looks broken or empty, rebuild it from code with the **EOSAT** menu:
**EOSAT → Build Visualization Scene** or **EOSAT → Build MR Tabletop Scene (Meta SDK)**.
(The old XRI / AR Foundation MR builder is kept under *EOSAT → Legacy* - don't use it for the current scene.)

---

## Using a Meta Quest (optional)

Runs from the Unity Editor over **Meta Horizon Link** (Windows PC only). No Android build needed.

1. Install the **Meta Horizon Link** app on the PC and connect the headset (USB-C cable or Air Link).
2. Link app → Settings → General → **OpenXR Runtime: Set Meta Horizon Link as active**.
3. Put the headset on, then press **Play** in Unity.
   - VR controls: thumbstick up/down = zoom, left/right = snap turn, A/X = switch spacecraft/Earth.

**Extra for the MR tabletop scene (Meta SDK, passthrough):**
- Your Meta account needs a free developer organization (<https://developers.meta.com>); then
  Link app → Settings → **Beta / Developer** → turn on *Developer runtime features* and
  *Passthrough over Meta Horizon Link*, and restart Unity.
- In the headset: Settings → Movement Tracking → **Hand tracking** on (controllers work too).
- Passthrough only shows in the headset. It is **full colour on Quest 3 / 3S / Pro** and black-and-white on Quest 2 (hardware limit).

| In the headset | What it does |
|---|---|
| **Click either thumbstick** (or the tablet's AR / VR switch) | Toggle passthrough (your room) ↔ virtual space skybox |
| **Look at the back of your left wrist** like checking a watch (hold ~0.3 s) | Brings the tablet in front of you (it also starts open in front of you) |
| Red **X** on the tablet | Hide the tablet (bring it back with the watch gesture) |
| **Point a hand ray at the bar under the tablet and pinch** | Grab it from a distance. While pinching, **pull your arm back** = tablet slides closer, **push forward** = it slides away. Let go = it stops and hovers there |
| Tap the tablet with your finger, or point + pinch | Press its switches and buttons |
| **Grab the globe** (hand, controller grip, or from a distance) | Move and turn it in any direction; let go and it hovers. Walk around it to see every side |

Teleport / snap-turn are turned off in this scene on purpose: in mixed reality you walk around the real room.

`[MetaXRFeature] ErrorFormFactorUnavailable` (or `XR_ERROR_FORM_FACTOR_UNAVAILABLE`) in the Console when you press Play
just means no headset was found; the scenes still run on desktop.

---

## Live data from the client's simulator

The client package (`GNG1103_F2026`, not in this repo) contains `vr_stub.py` and `sample_vr_frames.ndjson`.

Three sources, switchable while playing (`TelemetryHub.SwitchDataSource`):

| Source | Where | Switch with |
|---|---|---|
| Simulated (local) | built-in replay of the client's orbit | tablet **SIM** button / key 5 |
| TCP | `127.0.0.1:8100` | tablet **TCP 8100** / key 6 |
| WebSocket | `ws://127.0.0.1:8101/vr` | tablet **WS 8101** / key 7 |

The tablet's Connection box shows the active feed (e.g. `WebSocket: 8101`) and a green **CONNECTED** while data arrives.
To start on a network source, set **Telemetry → TelemetryHub → Source** before pressing Play.

1. Start the stub in a terminal in the client folder (it serves TCP 8100 and WebSocket 8101 at the same time):
   ```
   python vr_stub.py --speed 30
   python vr_stub.py --replay sample_vr_frames.ndjson --speed 5
   python vr_stub.py --chaos --speed 10 --rate 2      # robustness test
   ```
2. Press **Play** in Unity and pick TCP or WS on the tablet.
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
  EOSAT/Scripts/     telemetry pipeline (TelemetryHub, TCP + WebSocket clients), spacecraft + Earth motion, HUD, yellow dot, replay feed, test recorder
  EOSAT/MR/          mixed-reality tabletop content: tablet UI, connection UI, telemetry readout, overlay, desktop fallback
  EOSAT/Meta/        Meta SDK scripts: PassthroughToggle (thumbstick), WristSummonUI (watch gesture), PlaceInFrontOnStart
  Samples/           XR Interaction Toolkit + XR Hands samples (the VR rig and hand visuals)
  VRTemplateAssets/  Unity VR template content (sample scene, tutorial)
  EOSAT/Editor/      EOSAT menu: scene builders, VR/MR setup, parser self-test
  EOSAT/Materials, Meshes, Shaders, Textures
  Scenes/            EOSAT_Visualization, EOSAT_MR_Tabletop
  StreamingAssets/   client sample orbit data
Tools/               telemetry_test_server.py (legacy test server)
```
