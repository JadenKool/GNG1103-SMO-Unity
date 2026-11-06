using System.Collections.Generic;
using Eosat.MR;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Eosat.EditorTools
{
    /// <summary>
    /// EOSAT > Build MR Tabletop Scene: (re)creates Assets/Scenes/EOSAT_MR_Tabletop.unity -
    /// passthrough rig; desk-anchored globe driven by the client's simulator data (true-scale ECI orbit,
    /// GMST Earth rotation, real Sun direction, eclipse, ground-station contacts); yellow dot at the
    /// true position; full-size EOSAT-1 hovering above the globe mirroring attitude + deployables;
    /// left-wrist tablet (Data / Antenna / Solar Panels) poked with the right index finger;
    /// keyboard + mouse fallback when no headset is connected.
    /// </summary>
    public static class EosatMrSceneBuilder
    {
        const string Root = "Assets/EOSAT";
        const string ScenePath = "Assets/Scenes/EOSAT_MR_Tabletop.unity";
        const float GlobeRadius = 0.12f;          // 24 cm globe
        const float ClientEarthRadiusKm = 6371f;  // client simulator's Earth radius
        const float SatelliteScale = 0.15f;       // hovering model: 37 cm spacecraft drawn ~5.5 cm
        const float StandHeight = 0.25f;          // globe centre above the desk: free-floating, 13 cm gap under the 12 cm globe
        static readonly Vector3 BoxMeters = new Vector3(0.10f, 0.37f, 0.23f);

        // Ground stations the client simulator reports contact with (approximate public locations).
        static readonly (string name, float lat, float lon)[] Stations =
        {
            ("Iqaluit", 63.75f, -68.52f),
            ("Inuvik", 68.36f, -133.72f),
            ("Troll", -72.01f, 2.53f),
            ("O'Higgins", -63.32f, -57.90f),
        };

        static Font font;

        [MenuItem("EOSAT/Build MR Tabletop Scene", priority = 1)]
        public static void BuildMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Debug.Log("[EOSAT] " + Build());
        }

        public static string Build()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");

            var mEarth = Load<Material>("Materials/Earth.mat");
            var mBody = Load<Material>("Materials/SpacecraftBody.mat");
            var mPanel = Load<Material>("Materials/SolarPanel.mat");
            var mLens = Load<Material>("Materials/Payload.mat");
            var mAntenna = Load<Material>("Materials/Antenna.mat");
            var earthMesh = Load<Mesh>("Meshes/EarthSphere.asset");
            var mLine = Mat("MR_Line", Shader.Find("Sprites/Default"), Color.white);
            var mDot = Mat("MR_FingerDot", unlit, new Color(0.3f, 0.9f, 1f));
            var mStation = Mat("MR_GroundStation", unlit, new Color(0.3f, 1f, 0.55f));
            var mSun = Mat("MR_Sun", unlit, new Color(2.2f, 1.7f, 0.8f));
            string replayNote = EosatMarkerSetup.EnsureReplayFile();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.28f, 0.28f, 0.31f);

            var sunGo = new GameObject("Sun Light");
            var sunLight = sunGo.AddComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.intensity = 1.5f;
            sunLight.shadows = LightShadows.None;
            RenderSettings.sun = sunLight;

            // ---------- XR rig with passthrough ----------
            var arSession = new GameObject("AR Session").AddComponent<ARSession>();

            // Unity's standard rig: tracked head, controller + Quest hand models, near-far rays, poke, grab.
            var origin = EosatXrRig.Create("XR Origin (MR, hands + controllers)", EosatXrRig.Mode.MixedReality);
            var originGo = origin.gameObject;
            var offset = origin.CameraFloorOffsetObject.transform;
            var cam = EosatXrRig.ConfigureCamera(origin, new Color(0f, 0f, 0f, 0f), 0.01f, 100f, 60f, false); // alpha 0 = passthrough; HDR target has no alpha
            var camGo = cam.gameObject;
            camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f); // desktop height; head tracking overrides in VR
            var arCamera = camGo.AddComponent<ARCameraManager>();

            var planes = originGo.AddComponent<ARPlaneManager>();
            planes.planePrefab = null;
            planes.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            var anchors = originGo.AddComponent<ARAnchorManager>();

            // Left controller pose for the wrist tablet when hands aren't tracked (the rig's own controller objects drive the visuals).
            var leftCtrl = Child("Tablet Fallback (Left Controller Pose)", offset).gameObject.AddComponent<ControllerPose>();
            leftCtrl.side = ControllerPose.Side.Left;

            // ---------- Interaction: the rig's right poke / near-far interactors press the tablet ----------
            EosatXrRig.EnsureInteractionManager();
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            var uiModule = esGo.AddComponent<XRUIInputModule>();
            uiModule.activeInputMode = XRUIInputModule.ActiveInputMode.InputSystemActions; // project is Input System only
            uiModule.enableMouseInput = true;
            uiModule.enableBuiltinActionsAsFallback = true;



            // ---------- Telemetry: client data replay (same path as the live TCP feed) ----------
            var telGo = new GameObject("Telemetry");
            var hub = telGo.AddComponent<TelemetryHub>();
            hub.source = DataSource.SimulatedLocal;      // switch to Tcp for vr_stub.py / the real simulator
            telGo.AddComponent<SimulatedSpacecraft>();        // fallback if the replay file is missing
            var replay = telGo.AddComponent<SampleReplayFeed>();
            replay.speed = 30f;

            // ---------- Tabletop globe: true-scale orbit in an ECI frame ----------
            var globe = new GameObject("Globe Pivot").transform;
            globe.position = new Vector3(0.15f, 1.15f, 0.6f); // edit-time preview; the placer moves it at runtime
            var spin = Child("Spin Pivot (grab the globe to turn)", globe); // InteractiveGlobe turns this, never the root
            var eci = Child("ECI Frame", spin);                // inertial axes: X vernal equinox, Unity +Y = north

            var earth = Child("Earth", eci);
            earth.localScale = Vector3.one * GlobeRadius;
            earth.gameObject.AddComponent<MeshFilter>().sharedMesh = earthMesh;
            var er = earth.gameObject.AddComponent<MeshRenderer>();
            er.sharedMaterial = mEarth;
            er.shadowCastingMode = ShadowCastingMode.Off;

            var stations = Child("Ground Stations", earth);
            foreach (var s in Stations)
                Prim(PrimitiveType.Sphere, s.name, stations, LatLon(s.lat, s.lon), Vector3.one * 0.03f, mStation);

            float unitsPerKm = GlobeRadius / ClientEarthRadiusKm;
            var sc = Child("Spacecraft (true position)", eci);
            var visuals = Child("Visuals", sc);
            EosatMarkerSetup.CreateMarker(visuals, 0.012f, 0f, 0f, true, 0.005f);
            var trail = sc.gameObject.AddComponent<TrailRenderer>();
            trail.time = 60f;
            trail.minVertexDistance = 0.0015f;
            trail.startWidth = 0.0018f; trail.endWidth = 0f;
            trail.sharedMaterial = mLine;
            trail.startColor = new Color(1f, 0.85f, 0.2f, 0.8f);
            trail.endColor = new Color(1f, 0.85f, 0.2f, 0f);
            trail.shadowCastingMode = ShadowCastingMode.Off;

            var ctrl = sc.gameObject.AddComponent<SpacecraftController>();
            ctrl.hub = hub;
            ctrl.deployables = null;                          // the hovering model's deployables follow SatelliteDeployControl
            ctrl.visualRoot = visuals.gameObject;
            ctrl.trail = trail;
            ctrl.unitsPerKm = unitsPerKm;
            ctrl.catchUpDistanceUnits = 300f * unitsPerKm;    // same 300 km threshold as the main scene

            var envGo = new GameObject("Environment (Earth rotation + Sun)");
            var earthCtrl = envGo.AddComponent<EarthController>();
            earthCtrl.spacecraft = ctrl;
            earthCtrl.earthBody = earth;                      // GMST rotation for the displayed sim time

            var sunMarker = Prim(PrimitiveType.Sphere, "Sun", spin, Vector3.zero, Vector3.one * 0.035f, mSun).transform;
            var sunLabel = Child("Label", sunMarker);
            sunLabel.localPosition = new Vector3(0f, 1.1f, 0f);
            sunLabel.localScale = Vector3.one / 0.035f;
            var sunText = sunLabel.gameObject.AddComponent<TextMesh>();
            sunText.font = font; sunText.text = "SUN"; sunText.fontSize = 110; sunText.characterSize = 0.0011f;
            sunText.anchor = TextAnchor.LowerCenter; sunText.color = new Color(1f, 0.85f, 0.5f);
            sunLabel.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            sunLabel.gameObject.AddComponent<Billboard>();

            var mrSun = envGo.AddComponent<MrSunLight>();
            mrSun.earth = earthCtrl;
            mrSun.eciFrame = eci;
            mrSun.sunLight = sunLight;
            mrSun.sunMarker = sunMarker;

            // ---------- Hovering EOSAT-1 (same placeholder model as the main scene) ----------
            var detail = Child("Hovering EOSAT-1 (attitude + deployables)", globe);
            detail.localPosition = new Vector3(0f, 0.21f, 0f);
            var model = Child("Model", detail);
            model.localScale = Vector3.one * SatelliteScale;
            Prim(PrimitiveType.Cube, "Body (10x23x37 cm)", model, Vector3.zero, BoxMeters, mBody);
            Prim(PrimitiveType.Cylinder, "Payload lens (nadir, +Z)", model, new Vector3(0, 0.185f, 0), new Vector3(0.05f, 0.006f, 0.05f), mLens);
            var hingeR = Child("Panel Hinge R", model); hingeR.localPosition = new Vector3(0.053f, 0, 0.11f);
            Prim(PrimitiveType.Cube, "Solar Panel R", hingeR, new Vector3(0.112f, 0, 0), new Vector3(0.22f, 0.34f, 0.004f), mPanel);
            var hingeL = Child("Panel Hinge L", model); hingeL.localPosition = new Vector3(-0.053f, 0, 0.11f);
            Prim(PrimitiveType.Cube, "Solar Panel L", hingeL, new Vector3(-0.112f, 0, 0), new Vector3(0.22f, 0.34f, 0.004f), mPanel);
            var hingeA = Child("Antenna Hinge", model); hingeA.localPosition = new Vector3(0, -0.185f, 0.10f);
            var rod = Prim(PrimitiveType.Cube, "Antenna", hingeA, new Vector3(0, -0.1f, 0), new Vector3(0.006f, 0.2f, 0.006f), mAntenna);
            hingeR.localRotation = Quaternion.Euler(0, 90, 0);
            hingeL.localRotation = Quaternion.Euler(0, -90, 0);
            hingeA.localRotation = Quaternion.Euler(90, 0, 0);

            var deploy = detail.gameObject.AddComponent<DeployablesAnimator>();
            deploy.panelHingeLeft = hingeL; deploy.panelHingeRight = hingeR; deploy.antennaHinge = hingeA;
            deploy.antennaRenderer = rod.GetComponent<Renderer>();
            var deployCtrl = detail.gameObject.AddComponent<SatelliteDeployControl>();
            deployCtrl.deployables = deploy;
            deployCtrl.hub = hub;
            var mirror = detail.gameObject.AddComponent<DetailViewMirror>();
            mirror.spacecraft = ctrl;
            mirror.model = model.gameObject;

            // ---------- Free-floating globe (no stand): grab it = 6 DoF move + turn, zero gravity ----------
            var grabCol = globe.gameObject.AddComponent<SphereCollider>();
            grabCol.radius = GlobeRadius * 1.05f;
            var body = globe.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;             // never falls, never drifts
            body.useGravity = false;
            var move = globe.gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            move.colliders.Clear();
            move.colliders.Add(grabCol);
            move.movementType = UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable.MovementType.Kinematic;
            move.trackPosition = true;
            move.trackRotation = true;           // 6 DoF: turn it in your hand to see any side
            move.throwOnDetach = false;          // stops dead and hovers where it is let go
            move.useDynamicAttach = true;        // holds where you grabbed it (no snapping to the hand)
            move.retainTransformParent = true;   // stays under its anchor
            move.smoothPosition = true;
            move.smoothRotation = true;
            move.selectMode = UnityEngine.XR.Interaction.Toolkit.Interactables.InteractableSelectMode.Multiple; // two hands = turn it between them
            var twoHand = globe.gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.Transformers.XRGeneralGrabTransformer>();
            twoHand.allowTwoHandedScaling = false;

            // ---------- Telemetry overlay ----------
            var tel = Child("Telemetry Overlay", spin);
            var overlay = tel.gameObject.AddComponent<TelemetryOverlay>();
            overlay.orbitPath = Line("Orbit Path (client data)", tel, 0.0012f, mLine, new Color(0.3f, 0.85f, 1f, 0.6f));
            overlay.signalLink = Line("Ground Station Link", tel, 0.0015f, mLine, new Color(0.3f, 1f, 0.55f, 0.9f));
            overlay.sunArrow = Line("Sun Direction", tel, 0.002f, mLine, new Color(1f, 0.75f, 0.2f, 1f));
            overlay.earthArrow = Line("Earth Direction (nadir)", tel, 0.002f, mLine, new Color(0.35f, 0.6f, 1f, 1f));
            overlay.leaderLine = Line("Leader (dot to model)", globe, 0.0008f, mLine, new Color(1f, 0.9f, 0.3f, 0.45f));
            var readout = Child("Status Readout", tel);
            var tm = readout.gameObject.AddComponent<TextMesh>();
            tm.font = font; tm.fontSize = 110; tm.characterSize = 0.0009f;
            tm.anchor = TextAnchor.MiddleLeft; tm.alignment = TextAlignment.Left;
            tm.richText = true; tm.color = Color.white; tm.text = "Waiting for telemetry";
            readout.GetComponent<MeshRenderer>().sharedMaterial = font.material;

            overlay.spacecraft = ctrl;
            overlay.hub = hub;
            overlay.replay = replay;
            overlay.sun = mrSun;
            overlay.eciFrame = eci;
            overlay.earth = earth;
            overlay.detailView = detail;
            overlay.deploy = deployCtrl;
            overlay.stationsRoot = stations;
            overlay.readoutRoot = readout;
            overlay.readoutText = tm;
            overlay.startVisible = false;                       // default = just the model on the desk (reference look)
            overlay.extraDataObjects = new[] { sunMarker.gameObject };

            // ---------- Desk placement ----------
            var placer = new GameObject("Tabletop Placer").AddComponent<TabletopAnchorPlacer>();
            placer.xrOrigin = origin;
            placer.planeManager = planes;
            placer.anchorManager = anchors;
            placer.content = globe;
            placer.forwardDistance = 0.55f;
            placer.lateralOffset = 0.12f;
            placer.hoverHeight = StandHeight;

            // ---------- Hand tablet ----------
            var tabletRoot = new GameObject("Hand Tablet");
            var hand = tabletRoot.AddComponent<HandTablet>();
            hand.xrOrigin = origin;
            hand.hand = Handedness.Left;
            hand.controllerFallback = leftCtrl;

            var canvasGo = new GameObject("Tablet Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(tabletRoot.transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = cam;
            var crt = (RectTransform)canvasGo.transform;
            crt.sizeDelta = new Vector2(820f, 470f);
            crt.localScale = Vector3.one * 0.00033f;              // ~27 x 15.5 cm
            canvasGo.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            canvasGo.AddComponent<TrackedDeviceGraphicRaycaster>();
            hand.tabletVisual = canvasGo;

            var bg = UI("Background", crt);
            Stretch(bg);
            bg.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.07f, 0.11f, 0.88f);

            var title = UIText("Title", crt, "EOSAT-1  CONTROL", 28, FontStyle.Bold, new Color(0.85f, 0.93f, 1f), TextAnchor.MiddleCenter);
            Place(title.rectTransform, new Vector2(0f, 200f), new Vector2(780f, 44f));

            // Left column: toggles
            const float L = -200f, R = 205f, RowW = 380f;
            var (dataT, dataS) = Row(crt, "Data Overlay", 128f, L, RowW);
            var (envT, envS) = Row(crt, "AR / VR View", 48f, L, RowW);
            var (antT, antS) = Row(crt, "Deploy Antenna", -32f, L, RowW);
            var (solT, solS) = Row(crt, "Deploy Solar Panels", -112f, L, RowW);

            // AR/VR switch: EnvironmentManager sits on the toggle and listens to it (on = passthrough).
            var env = envT.gameObject.AddComponent<EnvironmentManager>();
            env.xrCamera = cam;
            env.cameraManager = arCamera;
            env.toggle = envT;
            env.skyboxMaterial = SpaceSkybox();
            env.startMode = EnvironmentManager.Mode.Passthrough;

            // Right column: live data + connection box
            var dataBox = UI("Satellite Data", crt);
            Place(dataBox, new Vector2(R, 92f), new Vector2(380f, 176f));
            dataBox.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.07f);
            var dataHead = UIText("Header", dataBox, "SATELLITE DATA", 15, FontStyle.Bold, new Color(0.55f, 0.8f, 1f), TextAnchor.UpperLeft);
            Place(dataHead.rectTransform, new Vector2(0f, 70f), new Vector2(356f, 22f));
            var dataText = UIText("Readout", dataBox, "Waiting for telemetry...", 19, FontStyle.Normal, Color.white, TextAnchor.UpperLeft);
            Place(dataText.rectTransform, new Vector2(0f, -14f), new Vector2(356f, 140f));
            var readoutUi = canvasGo.AddComponent<TelemetryReadoutUI>();
            readoutUi.hub = hub;
            readoutUi.dataText = dataText;

            var connBox = UI("Connection", crt);
            Place(connBox, new Vector2(R, -102f), new Vector2(380f, 196f));
            connBox.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.07f);
            var connHead = UIText("Header", connBox, "CONNECTION", 15, FontStyle.Bold, new Color(0.55f, 0.8f, 1f), TextAnchor.UpperLeft);
            Place(connHead.rectTransform, new Vector2(0f, 80f), new Vector2(356f, 22f));
            var connStatus = UIText("Status", connBox, "--", 19, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft);
            Place(connStatus.rectTransform, new Vector2(0f, 46f), new Vector2(356f, 30f));
            var connDetail = UIText("Detail", connBox, "", 14, FontStyle.Normal, new Color(0.7f, 0.76f, 0.82f), TextAnchor.MiddleLeft);
            Place(connDetail.rectTransform, new Vector2(0f, 16f), new Vector2(356f, 26f));
            var conn = canvasGo.AddComponent<ConnectionUIManager>();
            conn.hub = hub;
            conn.statusText = connStatus;
            conn.detailText = connDetail;
            conn.simulatedButton = Btn(connBox, "SIM", new Vector2(-122f, -50f), new Vector2(112f, 64f));
            conn.tcpButton = Btn(connBox, "TCP 8100", new Vector2(0f, -50f), new Vector2(112f, 64f));
            conn.webSocketButton = Btn(connBox, "WS 8101", new Vector2(122f, -50f), new Vector2(112f, 64f));

            var ui = canvasGo.AddComponent<TabletUIController>();
            ui.dataToggle = dataT; ui.dataStatus = dataS;
            ui.arVrToggle = envT; ui.arVrStatus = envS; ui.environment = env;
            ui.antennaToggle = antT; ui.antennaStatus = antS;
            ui.panelsToggle = solT; ui.panelsStatus = solS;
            ui.telemetry = overlay;
            ui.satellite = deployCtrl;

            // ---------- Desktop fallback (no headset): keyboard + mouse ----------
            var hint = UIText("Desktop Hint", crt, "Desktop: right-drag look | WASD/QE move | 1 Data  2 AR/VR  3 Antenna  4 Panels | 5 Sim  6 TCP  7 WS | R re-place", 13, FontStyle.Normal, new Color(0.7f, 0.76f, 0.82f), TextAnchor.MiddleCenter);
            Place(hint.rectTransform, new Vector2(0f, -218f), new Vector2(800f, 24f));
            hint.gameObject.SetActive(false);

            var fallback = new GameObject("Desktop Fallback").AddComponent<MrDesktopFallback>();
            fallback.cam = cam;
            fallback.handTablet = hand;
            fallback.tabletCanvas = canvas;
            fallback.desktopHint = hint.gameObject;
            fallback.tablet = ui;
            fallback.connection = conn;
            fallback.placer = placer;
            fallback.arComponents = new Behaviour[] { arSession, arCamera, planes, anchors };
            foreach (var b in fallback.arComponents) b.enabled = false; // switched on at runtime only with a headset

            fallback.deskOffset = StandHeight;   // no virtual room: in the headset the real room is passthrough

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = globe.gameObject;
            return "Built " + ScenePath + " (" + replayNote + ")";
        }

        // ---------- Tabletop look helpers ----------

        /// <summary>Soft round shadow under the stand so the globe looks grounded on the desk (also darkens passthrough).</summary>
        static Material ContactShadowMaterial()
        {
            const string texPath = Root + "/Textures/MR_ContactShadow.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null)
            {
                const int n = 64;
                tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "MR_ContactShadow" };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Pow(Mathf.Clamp01(1f - r), 1.6f) * 0.75f;
                        tex.SetPixel(x, y, new Color(0f, 0f, 0f, a));
                    }
                tex.Apply();
                AssetDatabase.CreateAsset(tex, texPath);
            }
            var m = Mat("MR_ContactShadow", Shader.Find("Universal Render Pipeline/Unlit"), Color.black);
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent - 10;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------- UI helpers ----------

        /// <summary>Black space sky for VR mode; its sun disk follows RenderSettings.sun (the real Sun direction).</summary>
        static Material SpaceSkybox()
        {
            var m = Mat("MR_VR_SpaceSkybox", Shader.Find("Skybox/Procedural"), Color.black);
            m.SetFloat("_SunDisk", 2f);
            m.SetFloat("_SunSize", 0.025f);
            m.SetFloat("_AtmosphereThickness", 0f);
            m.SetColor("_SkyTint", Color.black);
            m.SetColor("_GroundColor", new Color(0.015f, 0.015f, 0.02f));
            m.SetFloat("_Exposure", 1f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Button Btn(RectTransform parent, string label, Vector2 pos, Vector2 size)
        {
            var rt = UI("Button " + label, parent);
            Place(rt, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(0.25f, 0.28f, 0.33f, 1f);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var c = b.colors;
            c.highlightedColor = new Color(1.3f, 1.3f, 1.3f);
            c.pressedColor = new Color(1.8f, 1.8f, 1.8f);
            c.fadeDuration = 0.08f;
            b.colors = c;
            var t = UIText("Label", rt, label, 17, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform);
            return b;
        }

        static (Toggle, Text) Row(RectTransform parent, string label, float y, float x = 0f, float width = 414f)
        {
            var row = UI("Row " + label, parent);
            Place(row, new Vector2(x, y), new Vector2(width, 72f));
            var bg = row.gameObject.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.07f);

            var toggle = row.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = bg;
            var colors = toggle.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.6f, 1.8f, 2.2f);
            colors.pressedColor = new Color(2.2f, 2.6f, 3f);
            colors.fadeDuration = 0.08f;
            toggle.colors = colors;

            var track = UI("Switch", row);
            Place(track, new Vector2(width * 0.5f - 47f, 0f), new Vector2(72f, 36f));
            track.gameObject.AddComponent<Image>().color = new Color(0.25f, 0.28f, 0.33f, 1f);
            var on = UI("On", track);
            Stretch(on, 4f);
            var onImg = on.gameObject.AddComponent<Image>();
            onImg.color = new Color(0.2f, 0.75f, 1f, 1f);
            toggle.graphic = onImg;
            toggle.isOn = false;

            var name = UIText("Label", row, label, 24, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft);
            float labelX = -width * 0.5f + 157f;   // 12 px left margin, 290 px wide
            Place(name.rectTransform, new Vector2(labelX, 12f), new Vector2(290f, 32f));
            var status = UIText("Status", row, "--", 16, FontStyle.Normal, new Color(0.55f, 0.8f, 1f), TextAnchor.MiddleLeft);
            Place(status.rectTransform, new Vector2(labelX, -16f), new Vector2(290f, 26f));
            return (toggle, status);
        }

        static RectTransform UI(string name, Transform parent)
        {
            var g = new GameObject(name, typeof(RectTransform));
            g.transform.SetParent(parent, false);
            return (RectTransform)g.transform;
        }

        static Text UIText(string name, Transform parent, string text, int size, FontStyle style, Color color, TextAnchor anchor)
        {
            var t = UI(name, parent).gameObject.AddComponent<Text>();
            t.font = font; t.text = text; t.fontSize = size; t.fontStyle = style; t.color = color; t.alignment = anchor;
            t.raycastTarget = false;
            return t;
        }

        static void Place(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        static void Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
        }

        // ---------- Scene helpers ----------

        static Vector3 LatLon(float latDeg, float lonDeg)
        {
            float lat = latDeg * Mathf.Deg2Rad, lon = lonDeg * Mathf.Deg2Rad;
            // Same mapping as EosatSceneBuilder.EarthMesh: (cos lat cos lon, sin lat, cos lat sin lon)
            return new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
        }

        static T Load<T>(string rel) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>($"{Root}/{rel}");
            if (!a) Debug.LogWarning($"[EOSAT] Missing {Root}/{rel} - run EOSAT > Build Visualization Scene once to create it.");
            return a;
        }

        static Material Mat(string name, Shader shader, Color color)
        {
            string path = $"{Root}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            else m.shader = shader;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Transform Child(string name, Transform parent)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            return g.transform;
        }

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var g = GameObject.CreatePrimitive(type);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            var r = g.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return g;
        }

        static LineRenderer Line(string name, Transform parent, float width, Material m, Color color)
        {
            var lr = Child(name, parent).gameObject.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.startWidth = lr.endWidth = width;
            lr.sharedMaterial = m;
            lr.startColor = lr.endColor = color;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCapVertices = 2;
            return lr;
        }

        static void AddToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == path)) return;
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
