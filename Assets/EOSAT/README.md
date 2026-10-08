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

## Credits
- Earth day and night textures: NASA Blue Marble / Earth at Night derived maps, via the three.js examples repository (MIT).
- Code drafted with Claude (Anthropic) AI assistance. Cite per course policy.
