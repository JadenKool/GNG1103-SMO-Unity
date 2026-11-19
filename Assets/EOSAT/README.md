# EOSAT-1 Visualizer (GNG1103 Group 13 prototype)

Open `Assets/Scenes/EOSAT_Visualization.unity` and press **Play**. A built-in simulated feed starts immediately.
Rebuild the scene any time with **EOSAT > Build Visualization Scene** (this overwrites that scene file).

## Controls
| Input | Action |
|---|---|
| Mouse drag | Orbit camera |
| Scroll | Zoom |
| 1 / 2 | Follow spacecraft / Earth view |
| H | Show/hide the quick guide |
| P / A | (simulated feed) toggle panels / cycle antenna stowed > deployed > jammed |
| F / B / G | (simulated feed) pause feed / send bad packet / fast spin |

## Using your SolidWorks model
1. In SolidWorks: **File > Save As > OBJ** (`*.obj`). Tick "save as one file" if asked; any units are fine.
2. Drag the `.obj` into `Assets/EOSAT/Models/` in Unity's Project window.
3. Click the imported model in the Project window, then **EOSAT > Use Selected As Spacecraft Model**.
   It is centred, scaled so the longest side is 37 cm, and the placeholder box is hidden.
4. Rotate the `CustomModel` object so its faces match the X/Y/Z arrows (OBJ files often arrive Y-up), then save the scene.
5. **EOSAT > Restore Placeholder Model** brings the box back.

The placeholder's solar panels and antenna animate. A single-mesh OBJ can't, so for animated deployables export
the panels/antenna as separate parts and assign their pivots to `DeployablesAnimator` on the Spacecraft object.

## Live telemetry (TCP)
Select **Telemetry**, set **Source = Tcp**, host/port (8100), then Play. Messages are JSON objects, newline-separated or back-to-back:
```json
{"t": 1791460000.0, "pos_eci_km": [6878.1, 0.0, 0.0], "q": [0.86, -0.48, 0.10, 0.14],
 "panels": "deployed", "antenna": "stowed", "eclipse": false}
```
Only `pos_eci_km` and `q` are required. `q` is body-to-ECI; untick *Quaternion Scalar First* if the tool sends `[x, y, z, w]`.
When the client's test tool format is known, adjust the field names in `Scripts/Telemetry.cs`.

To test the network path now: `python Tools/telemetry_test_server.py` (options: `--stall-every 30 --stall-for 8`, `--bad-every 15`, `--warp 1`).

## How it meets Deliverable B (Table 1 / target specs)
| # | Criterion | Implementation |
|---|---|---|
| 1 | ECI position over TCP into a scaled 3D scene | `TelemetryHub` + `TcpJsonClient`; 1 unit = 1000 km, ECI mapped to Unity with Z = north |
| 2 | Quaternion attitude on 10x23x37 cm model | `SpacecraftController` converts body-to-ECI quaternion; placeholder box is 10x23x37 cm |
| 3-4 | Body axes and frames of reference | Labelled X/Y/Z body axes on the spacecraft, ECI axes through Earth, yellow Sun vector |
| 5 | Smooth, no jumps, spin warning | Interpolation between 1 Hz samples at full frame rate; spin limited to 45 deg/s, warning above 30 deg/s |
| 6 | Stalled / bad data | Rejects malformed packets; after 3 s of silence the screen dims slowly, motion is extrapolated up to 60 s, then re-joins smoothly |
| 7-8 | Panels / antenna state | Animated (30 s panel deploy, antenna stowed/deployed/jammed) with text labels |
| 9 | Eclipse / sunlight | From telemetry if sent, otherwise cylindrical Earth-shadow check |
| 10 | Recognisable Earth, day/night | Textured Earth rotated by Greenwich sidereal time; custom day/night shader with city lights |
| 11 | Both visible despite scale | Spacecraft drawn ~600,000x true size (~220 km long), Earth at true scale |
| 12-13 | Uncluttered HUD, quick guide | Two status lines, 22 px (~16 pt) text, one banner only when needed, first-run guide (H) |

The visualizer does no orbit propagation (constraint): orbit maths exists only in the simulated feed / test server.

## Combined scene setup (Nov 10 playtest changes)

Everything below is built by **EOSAT > Build Combined Scene (MR tabletop + VR orbit)**. Rerun that menu, open `EOSAT_Combined`, then check or tune these in the Inspector.

**1. VR wrist iPad** (`OVRCameraRig/TrackingSpace/LeftHandAnchor/Wrist iPad (VR, left wrist)`)
- `WristTablet`: Face User **on** floats the iPad 13 cm above the wrist, turned toward your eyes. Turn it **off** for a rigid watch-face mount, then tune Rigid Local Position / Euler in the headset.
- Canvas: `TelemetryReadoutUI` (data), `TabletUIController` (Antenna / Solar Panels / MR-VR toggles), `OrbitTargetToggleUI` (camera target), `ConnectionUIManager` (Source Drop-down, IP field, keypad).
- **Connections** row = toggle: it shows or hides `Connections Panel (toggle)` through its On Value Changed → `GameObject.SetActive`.
- **Controls guide**: `CenterEyeAnchor/VR Controls Guide (pinned top-left of view)`. Move it with its Local Position (pivot = top-left corner; default -0.40, 0.24, 0.9 m).
- `Environment Switcher`: **VR Only Objects** = guide + iPad; **MR Only Objects** = the Connection / Flight Map / Telemetry screens, so there is no Flight Map in VR.

**2. MR Earth + mini Sun** (`Environment_MR/Globe Pivot`)
- No `TabletopZoom` component = fixed size. `Environment Switcher > Tabletop Zoom` stays empty.
- `Grabbable`: One Grab Transformer **and** Two Grab Transformer = `RotateInPlaceTransformer`. The globe never moves or scales. Swing Gain 1.5 is how fast it spins per hand movement. Wrist Twist adds the wrist roll.
- `MrSunLight`: Marker Distance 0.9 m. The `Sun` sphere is no longer in `TelemetryOverlay > Extra Data Objects`, so it is always visible.

**3. Push / pull** (each `Screen - …` root): `ObjectPullProvider` Speed **7** (was 2.5), Dead Zone **0.008** (was 0.03), wired as the Movement Provider of the screen's `RayInteractable`. Raise Speed for faster push/pull; raise Dead Zone if screens drift while held. Defaults are `PullSpeed` / `PullDeadZone` in the builder.

**4. Data + deployables**
- `Systems/Deployables Control`: Deployables = MR model, Mirrors = VR spacecraft, Hub = Telemetry. A missing hub or animator is found or warned about at runtime. Manual overrides clear on a source switch or when data returns.
- Data states (Deliverable D/E): **LIVE** < 2 s, **DELAYED** 2–5 s, **GHOST** > 5 s, **LOST** > 60 s (`TelemetryHub.State`). The PREDICTION MODE veil fades in after 3 s (`Stale After Seconds`) on every screen and the iPad, and dims harder at LOST. The satellite keeps moving on its last orbit and spin for 60 s (`SpacecraftController > Max Prediction Sec`), then re-joins the real position over 3 s with smoothstep + Slerp (`Catch Up Duration Sec`).
- Tabletop overlay: ALT and VEL come from the received frames (same as the Telemetry screen). The orbit path only shows while the replay file is the live source. The status line shows the data state.

## Nov 11 features: setup steps

The builder does all of this too. These are the manual steps, or what to check after a rebuild.

**Loading screen (60 s startup buffer)**
1. `Telemetry` object > `TelemetryHub`: set **Startup Buffer Seconds** to `60`, **History Seconds** to `60`. `0` turns the buffer off and the loading screen hides itself.
2. Under `CenterEyeAnchor` add a world-space Canvas (`Loading Screen`), local position (0, 0, 1.2), scale 0.0009, size 1000 × 420. Add **CanvasGroup** and **TelemetryLoadingScreen**.
3. Children: a Text (status) and an Image with Source Image `UISprite`, **Image Type = Filled, Fill Method = Horizontal** (progress bar).
4. On `TelemetryLoadingScreen` drag in Hub, Progress Fill and Status Text.
While buffering, the hub caches frames but does not send them to the scene. At 60 s it sends the latest frame and the loading screen fades out. `TelemetryHub.History` holds the rolling last 60 s.

**Single solar panel**
1. On each `DeployablesAnimator` (MR model and VR spacecraft) tick **Single Panel**.
2. Disable or delete the left wing GameObject (`panelHingeLeft`). Keep `panelHingeRight` (+Y) as the only panel hinge.
3. `PanelFraction`, the iPad "Solar Panels" toggle and the data table now track that one panel. The status text no longer splits "+Y / −Y".

**Quaternion axis visualizer**
1. Under the satellite model (the object `SpacecraftController` rotates), add an empty child `Body Axes`, position 0, rotation 0.
2. Add **QuaternionAxesGizmo**. Length is in the model's local units (MR model: 0.03; scale up for larger models). Width is in metres.
3. Press Play. X = red, Y = green, Z = blue arrows follow the telemetry quaternion every frame (they are created at runtime and not saved in the scene).

**Antenna deployment %**: the feed may send `"antenna": {"fraction": 0.45}` or `{"percent": 45}`. If it does, the bar under the telemetry readout shows it. If it doesn't, it shows the animated state with "(state)". `TelemetryReadoutUI` has the Antenna Fill (filled Image) and Antenna Text fields.

**Data table**: `Screen - Data Table` (MR) > `DataTableUI` with Keys / Values Text. Refresh Hz is 1. Errors show red and warnings orange.

**EN / FR**: there is a "FRANÇAIS" toggle (`LanguageToggle`) on the Connection screen and the iPad. Code-set text goes through `LocalizationManager.Tr()`. Static labels have `LocalizedText`. Add new translations to `LocalizationManager.Fr` (English phrase → French). The choice is saved in PlayerPrefs.

**Controller guide**: `RightHandAnchor/Controller Guide` > `ControllerAnnotationGuide` + CanvasGroup. It shows when the controller points more than 40° below eye level and hides above 25°. The MR or VR text is picked from the current mode.

## Credits
- Earth day and night textures: NASA Blue Marble / Earth at Night derived maps, via the three.js examples repository (MIT).
- Code drafted with Claude (Anthropic) AI assistance. Cite per course policy.
