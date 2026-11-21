using System.Collections.Generic;
using System.Linq;
using Eosat.Meta;
using Eosat.MR;
using Oculus.Interaction;
using Oculus.Interaction.DistanceReticles;
using Oculus.Interaction.Editor.QuickActions;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Eosat.EditorTools
{
    /// <summary>
    /// EOSAT > Build Combined Scene (MR tabletop + VR orbit)
    /// Builds Assets/Scenes/EOSAT_Combined.unity from the two existing scenes (both are left untouched):
    ///
    ///   Systems                       Telemetry (ONE shared hub), EventSystem, Deployables Control, Environment Switcher (B / Y)
    ///   VR Orbit Rig                  OrbitalViewerLocomotion (off in MR, rig at room origin)
    ///     OVRCameraRig (passthrough)  the ONLY camera rig
    ///     Play Space Layout           placed on the user at start (EyeLevelSpawner); stays with the player in both modes
    ///       Screen - Connection / Flight Map / Telemetry Data   (MR only; grab bar, laser grab + fast push/pull, SnapInteractor)
    ///       Layout Slot 1..5          (SnapInteractable)
    ///     CenterEyeAnchor > VR Controls Guide   (VR only, pinned top-left of the view)
    ///     LeftHandAnchor > Wrist iPad           (VR only: telemetry, deploy toggles, camera target, Connections drop-down)
    ///   Environment_MR                White Platform, Globe Pivot (fixed-size Earth, rotate-in-place grab, mini Sun)
    ///   Environment_VR                VR Earth, Spacecraft, Sun
    /// </summary>
    public static class EosatCombinedSceneBuilder
    {
        const string MrPath = "Assets/Scenes/EOSAT_MR_Tabletop.unity";
        const string VrPath = "Assets/Scenes/EOSAT_Visualization.unity";
        public const string OutPath = "Assets/Scenes/EOSAT_Combined.unity";

        // Comfort: screens sit 0.9 m away (inside the 0.65-0.95 m focal range) and are scaled so every font keeps
        // a slightly LARGER angular size than the old 1.05 m-wide screens at 1.5 m.
        // Screen size: 1000 x 640 px at 0.72 mm per px = 0.72 x 0.46 m.
        const float ScreenW = 1000f, ScreenH = 640f, Px = 0.00072f;
        // Flight Map: wider canvas for the detailed layout (time, passes, link, map, plan strip, budgets).
        const float MapW = 1600f, MapH = 940f, MapPx = 0.00052f;                 // 0.83 x 0.49 m
        const float GlobeScale = 3.5f;          // Earth radius 0.12 m -> 0.42 m (84 cm across)
        const float GlobeSwingGain = 3.5f;      // RotateInPlaceTransformer: degrees of Earth spin per degree of hand swing
        const float EarthDistance = 1.2f;       // metres in front of the eyes
        const float ScreenRadius = 0.9f;        // screens / slots sit on an arc this far from the user (comfortable focus)
        const float PopupDistance = 0.8f;       // controls guide / replay window spawn this far in front

        static readonly Color PanelBg = new Color(0.07f, 0.08f, 0.10f, 0.97f);
        static readonly Color BoxBg = new Color(0.13f, 0.15f, 0.18f, 1f);
        static readonly Color Idle = new Color(0.25f, 0.28f, 0.33f, 1f);
        static readonly Color Accent = new Color(0.2f, 0.75f, 1f, 1f);
        static readonly Color TextCol = new Color(0.92f, 0.94f, 0.97f, 1f);
        static readonly Color Dim = new Color(0.62f, 0.67f, 0.74f, 1f);

        [MenuItem("EOSAT/Build Combined Scene (MR tabletop + VR orbit)")]
        public static void Menu() => Debug.Log("[EOSAT Combined]\n" + Build());

        public static string Build()
        {
            var log = new List<string>();

            // Start from the MR scene, immediately re-pointed at the new path so the original is never saved over.
            var scene = EditorSceneManager.OpenScene(MrPath, OpenSceneMode.Single);
            EditorSceneManager.SaveScene(scene, OutPath);
            var vrScene = EditorSceneManager.OpenScene(VrPath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            GameObject Find(Scene s, string name) => s.GetRootGameObjects().FirstOrDefault(g => g.name == name);
            GameObject Root(string name) { var g = new GameObject(name); SceneManager.MoveGameObjectToScene(g, scene); return g; }
            void Put(GameObject go, GameObject parent, string rename = null)
            {
                if (!go) { log.Add("MISSING: " + (rename ?? "?")); return; }
                if (go.scene != scene) SceneManager.MoveGameObjectToScene(go, scene);
                go.transform.SetParent(parent.transform, true);
                if (rename != null) go.name = rename;
            }

            var systems = Root("Systems");
            var orbitRig = Root("VR Orbit Rig");
            var envMR = Root("Environment_MR");
            var envVR = Root("Environment_VR");

            // ---------- MR side ----------
            var rigGo = Find(scene, "OVRCameraRig (passthrough)");
            Put(rigGo, orbitRig);
            rigGo.transform.localPosition = Vector3.zero; rigGo.transform.localRotation = Quaternion.identity;
            var mrTelemetry = Find(scene, "Telemetry");
            Put(mrTelemetry, systems);
            var hub = mrTelemetry.GetComponent<TelemetryHub>();
            hub.historySeconds = TelemetryHub.MaxBufferSeconds;   // 7 s smoothing buffer only; no loading screen, straight to live
            EditorUtility.SetDirty(hub);
            Put(Find(scene, "EventSystem"), systems);
            Put(Find(scene, "Sun Light"), envMR, "Sun Light (MR)");
            var globe = Find(scene, "Globe Pivot");
            Put(globe, envMR);
            Put(Find(scene, "Environment (Earth rotation + Sun)"), envMR, "Environment (MR Earth rotation + Sun)");
            var fallbackGo = Find(scene, "Desktop Fallback");
            Put(fallbackGo, envMR, "Desktop Fallback (MR, no headset)");
            foreach (var go in scene.GetRootGameObjects().Where(g => g.name.StartsWith("Passthrough Toggle") || g.name.StartsWith("Wrist Summon") || g.name.StartsWith("Floating Tablet")).ToList())
            { log.Add("Removed " + go.name); Object.DestroyImmediate(go); }

            // ---------- VR side ----------
            var vrTelemetry = Find(vrScene, "Telemetry");
            var xrOrigin = Find(vrScene, "XR Origin (orbit view)");
            Put(Find(vrScene, "Sun Light"), envVR, "Sun Light (VR)");
            Put(Find(vrScene, "Sun"), envVR, "Sun (VR)");
            var eci = Find(vrScene, "ECI Frame");
            Put(eci, envVR, "ECI Frame (VR Earth, 1 unit = 1000 km)");
            var vrSpacecraftGo = Find(vrScene, "Spacecraft");
            Put(vrSpacecraftGo, envVR, "Spacecraft (VR)");
            Put(Find(vrScene, "Environment"), envVR, "Environment (VR Earth rotation)");
            var hudGo = Find(vrScene, "HUD");
            Put(hudGo, envVR, "HUD (VR, replaced by the screens)");
            if (hudGo) hudGo.SetActive(false);

            // Re-point every reference from the VR scene's Telemetry to the shared one; drop refs into the old XRI rig.
            int remapped = 0, dropped = 0;
            foreach (var comp in envVR.GetComponentsInChildren<Component>(true))
            {
                if (!comp || comp is Transform) continue;
                var so = new SerializedObject(comp);
                var it = so.GetIterator();
                bool changed = false;
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || !it.objectReferenceValue) continue;
                    var obj = it.objectReferenceValue;
                    var go = obj is Component c ? c.gameObject : obj as GameObject;
                    if (!go) continue;
                    if (go == vrTelemetry)
                    {
                        it.objectReferenceValue = obj is Component cc ? mrTelemetry.GetComponent(cc.GetType()) : (Object)mrTelemetry;
                        changed = true; remapped++;
                    }
                    else if (xrOrigin && go.transform.IsChildOf(xrOrigin.transform))
                    {
                        it.objectReferenceValue = null; changed = true; dropped++;
                    }
                }
                if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            }
            log.Add($"VR environment: {remapped} telemetry refs -> shared hub, {dropped} refs to the old XRI rig cleared");
            EditorSceneManager.CloseScene(vrScene, true);

            // ---------- Rig settings shared by both modes ----------
            var ovrManager = rigGo.GetComponent<OVRManager>();
            if (ovrManager) { ovrManager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel; EditorUtility.SetDirty(ovrManager); }
            var centerT = rigGo.transform.Find("TrackingSpace/CenterEyeAnchor");
            var cam = centerT.GetComponent<Camera>();
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 1000f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            EditorUtility.SetDirty(cam);
            var passLayer = rigGo.GetComponent<OVRPassthroughLayer>();
            RenderSettings.skybox = null;

            // ================= MR: big Earth at eye level, satellite ON its orbit =================
            foreach (var old in globe.GetComponents<PlaceInFrontOnStart>()) Object.DestroyImmediate(old);
            foreach (var old in globe.GetComponents<EyeLevelSpawner>()) Object.DestroyImmediate(old);
            globe.transform.localScale = Vector3.one * GlobeScale;
            globe.transform.SetPositionAndRotation(new Vector3(0f, 1.5f, EarthDistance), Quaternion.identity);
            var spawner = globe.AddComponent<EyeLevelSpawner>();
            SetProps(spawner, ("target", globe.transform), ("head", centerT), ("forwardDistance", EarthDistance), ("heightOffset", 0f), ("rightOffset", 0f));
            // Fixed size: no thumbstick zoom (EnvironmentSwitcher auto-finds a TabletopZoom, so none may exist).
            foreach (var z in Object.FindObjectsByType<TabletopZoom>(FindObjectsInactive.Include)) Object.DestroyImmediate(z);
            foreach (var icon in globe.GetComponentsInChildren<ReticleDataIcon>(true)) Object.DestroyImmediate(icon);
            // Rotate in place only: one transformer for one- and two-hand grabs; never moves or scales the globe.
            var globeGrab = globe.GetComponent<Grabbable>();
            foreach (var old in globe.GetComponents<ITransformer>().OfType<MonoBehaviour>().ToList()) Object.DestroyImmediate(old);
            var spin = globe.AddComponent<RotateInPlaceTransformer>();
            // The MR EarthController sits on the MR environment root; its earthBody is the globe's Earth.
            var mrEarth = envMR.GetComponentsInChildren<EarthController>(true).FirstOrDefault(e => e.earthBody && e.earthBody.IsChildOf(globe.transform));
            SetProps(spin, ("swingGain", GlobeSwingGain), ("earth", mrEarth));
            // Serialized fields (warns in the Console if ISDK renames them) - Grabbable picks these up in Start.
            if (globeGrab) SetProps(globeGrab, ("_oneGrabTransformer", spin), ("_twoGrabTransformer", spin));
            log.Add("MR globe: fixed size and place; grab spins ONLY the Earth about its polar axis (x" + GlobeSwingGain + "); globe, orbit and Sun stay put"
                    + (mrEarth ? "" : "  (WARNING: no EarthController drives the globe's Earth - falls back to turning the globe about its axis)"));
            // Laser / distance grab: back to each interaction's own ISDK provider (object rides rigidly on the ray).
            foreach (var mp in globe.GetComponents<MoveAtSourceProvider>()) Object.DestroyImmediate(mp);
            foreach (var ri in globe.GetComponentsInChildren<RayInteractable>(true)) { var p = ri.GetComponent<IMovementProvider>(); if (p != null) { ri.InjectOptionalMovementProvider(p); EditorUtility.SetDirty(ri); } }
            foreach (var dg in globe.GetComponentsInChildren<DistanceGrabInteractable>(true)) { var p = dg.GetComponent<IMovementProvider>(); if (p != null) { dg.InjectOptionalMovementProvider(p); EditorUtility.SetDirty(dg); } }
            foreach (var dh in globe.GetComponentsInChildren<Oculus.Interaction.HandGrab.DistanceHandGrabInteractable>(true)) { var p = dh.GetComponent<IMovementProvider>(); if (p != null) { dh.InjectOptionalMovementProvider(p); EditorUtility.SetDirty(dh); } }

            var mrSc = globe.GetComponentsInChildren<SpacecraftController>(true).First();
            var vrSc = vrSpacecraftGo.GetComponent<SpacecraftController>();
            var overlay = globe.GetComponentInChildren<TelemetryOverlay>(true);

            // Miniature Sun: always visible (was only shown with the Data overlay), 3 Earth radii out along the real Sun direction.
            foreach (var ms in envMR.GetComponentsInChildren<MrSunLight>(true))
            {
                ms.markerDistance = 0.9f;
                if (ms.sunMarker) ms.sunMarker.gameObject.SetActive(true);
                if (overlay && overlay.extraDataObjects != null && ms.sunMarker)
                    overlay.extraDataObjects = overlay.extraDataObjects.Where(g => g != ms.sunMarker.gameObject).ToArray();
                EditorUtility.SetDirty(ms);
                log.Add("MR miniature Sun: always on, " + ms.markerDistance + " m from the globe centre");
            }

            // Satellite model: off the "hovering above the globe" stand and onto the true orbital position.
            var hover = globe.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("Hovering EOSAT-1"));
            DeployablesAnimator mrAnim = null;
            if (hover)
            {
                foreach (var m in hover.GetComponents<DetailViewMirror>()) Object.DestroyImmediate(m);
                foreach (var c in hover.GetComponents<SatelliteDeployControl>()) Object.DestroyImmediate(c);
                mrAnim = hover.GetComponent<DeployablesAnimator>();
                Transform visuals = mrSc.visualRoot ? mrSc.visualRoot.transform : mrSc.transform;
                hover.SetParent(visuals, false);
                hover.localPosition = Vector3.zero; hover.localRotation = Quaternion.identity; hover.localScale = Vector3.one;
                hover.name = "EOSAT-1 model (on orbit, attitude + deployables)";
                var model = hover.Find("Model");
                if (model) { model.localScale = Vector3.one * 0.04f; model.gameObject.SetActive(true); }   // ~3.7 cm body: clears the surface at true altitude
                // Body X / Y / Z axes (red / green / blue); they turn with the telemetry quaternion the controller writes.
                var axesGo = new GameObject("Body Axes (quaternion, X red / Y green / Z blue)");
                axesGo.transform.SetParent(hover, false);
                var axes = axesGo.AddComponent<QuaternionAxesGizmo>();
                axes.length = 0.03f; axes.width = 0.0015f;
                axesGo.AddComponent<AxesToggle>();
                log.Add("Satellite model moved from the hover stand onto the orbiting spacecraft (true altitude)");
            }
            var leader = globe.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("Leader"));
            if (leader) { if (overlay) overlay.leaderLine = null; Object.DestroyImmediate(leader.gameObject); }

            // Orbit trail: world-space TrailRenderer -> local-space, clamped trail.
            var oldTrail = mrSc.GetComponent<TrailRenderer>();
            var trailGo = new GameObject("Orbit Trail (local space, clamped)");
            trailGo.transform.SetParent(mrSc.transform.parent, false);
            var line = trailGo.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.sharedMaterial = oldTrail ? oldTrail.sharedMaterial : AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));
            line.widthMultiplier = 0.004f;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.2f), 0f), new GradientColorKey(new Color(1f, 0.85f, 0.2f), 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 1f) });
            line.colorGradient = grad;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.numCapVertices = 2;
            var localTrail = trailGo.AddComponent<LocalSpaceTrail>();
            localTrail.target = mrSc.transform; localTrail.frame = mrSc.transform.parent;
            if (oldTrail) { mrSc.trail = null; Object.DestroyImmediate(oldTrail); EditorUtility.SetDirty(mrSc); }
            log.Add("MR trail: TrailRenderer (world space, 60 s) replaced by LocalSpaceTrail (40 s / 200 points, moves with the globe)");

            // White platform to walk on (MR only).
            var platform = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            platform.name = "White Platform (MR floor)";
            Object.DestroyImmediate(platform.GetComponent<Collider>());
            platform.transform.SetParent(envMR.transform, false);
            platform.transform.localPosition = new Vector3(0f, -0.01f, 0.6f);
            platform.transform.localScale = new Vector3(4.4f, 0.01f, 4.4f);               // 2.2 m radius
            platform.GetComponent<Renderer>().sharedMaterial = MakeLit("COMBINED_WhitePlatform", new Color(0.95f, 0.95f, 0.95f), 0f, 0.2f);
            platform.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var platSpawner = platform.AddComponent<EyeLevelSpawner>();
            SetProps(platSpawner, ("target", platform.transform), ("head", centerT), ("forwardDistance", 0.6f), ("heightOffset", -0.01f), ("placeOnFloor", true), ("recenterInput", false));

            // ================= Deployables: one control for the MR and the VR model =================
            var vrAnim = vrSpacecraftGo.GetComponent<DeployablesAnimator>();
            if (vrSc) { vrSc.deployables = null; EditorUtility.SetDirty(vrSc); }     // was written every frame from telemetry, wiping manual toggles
            var deployGo = new GameObject("Deployables Control (antenna + solar panels)");
            deployGo.transform.SetParent(systems.transform, false);
            var deploy = deployGo.AddComponent<SatelliteDeployControl>();
            deploy.deployables = mrAnim; deploy.mirrors = vrAnim ? new[] { vrAnim } : new DeployablesAnimator[0];
            deploy.hub = hub; deploy.followTelemetry = true;
            // Two solar panels: Panel A = right hinge (wing +Y), Panel B = left hinge (wing -Y), each driven 0-90 deg by its telemetry fraction.
            foreach (var a in new[] { mrAnim, vrAnim })
            {
                if (!a) continue;
                a.singlePanel = false;
                if (a.panelHingeLeft) a.panelHingeLeft.gameObject.SetActive(true);
                EditorUtility.SetDirty(a);
            }
            if (overlay) { overlay.deploy = deploy; EditorUtility.SetDirty(overlay); }
            log.Add("Deployables: one control drives MR model" + (vrAnim ? " + VR model" : "") + "; Panel A / B follow telemetry only, commands queue to the Flight Commander");

            // ================= Play-space layout: screens, slots, VR console =================
            var layout = new GameObject("Play Space Layout (placed on the user at start)");
            layout.transform.SetParent(orbitRig.transform, false);
            layout.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            var layoutSpawner = layout.AddComponent<EyeLevelSpawner>();
            SetProps(layoutSpawner, ("target", layout.transform), ("head", centerT), ("forwardDistance", 0f), ("heightOffset", 0f), ("recenterInput", false));

            var slotLineMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/EOSAT/Materials/MR_Line.mat");
            if (!slotLineMat) slotLineMat = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            var barMat = MakeTransparent("COMBINED_GrabBar", new Color(1f, 1f, 1f, 0.45f));

            // Slots on an arc around the user: angle (deg, + = right), height above eye level, tilt.
            // Spread a little wider than before: the screens grew (bigger fonts) and the Flight Map is 1.2 m wide.
            var slotDefs = new (float angle, float up, float tilt)[] { (-96f, 0f, 0f), (-48f, 0f, 0f), (48f, 0f, 0f), (96f, 0f, 0f), (0f, 0.5f, -25f) };
            var slots = new List<SnapInteractable>();
            for (int i = 0; i < slotDefs.Length; i++)
            {
                var d = slotDefs[i];
                var slot = BuildSlot("Layout Slot " + (i + 1), layout.transform, slotLineMat);
                Quaternion yaw = Quaternion.Euler(0f, d.angle, 0f);
                slot.transform.localPosition = yaw * Vector3.forward * ScreenRadius + Vector3.up * d.up;
                slot.transform.localRotation = yaw * Quaternion.Euler(d.tilt, 0f, 0f);
                slots.Add(slot);
            }

            var screens = new List<GameObject>();
            // Screen 1 - Connection
            var s1 = BuildScreen("Screen - Connection", layout.transform, slots[1], barMat, out var c1);
            var conn = BuildConnectionUI(c1, hub);
            AddOverlay(c1, hub, mrSc, 0.55f);
            // Screen 2 - Flight Map
            var s2 = BuildScreen("Screen - Flight Map", layout.transform, slots[2], barMat, out var c2, MapW, MapH, MapPx);
            BuildFlightMapUI(c2, hub, mrSc, vrSc);
            AddOverlay(c2, hub, mrSc, 0.8f);
            // Replay (session log scrubber) - grabbable pop-up window, opened from the Data Table / iPad.
            var trackingSpace = rigGo.transform.Find("TrackingSpace");
            var replay = BuildReplayPanel(centerT, trackingSpace, systems.transform, hub, deploy, barMat);
            // Screen 3 - Telemetry Data
            var s3 = BuildScreen("Screen - Telemetry Data", layout.transform, slots[3], barMat, out var c3);
            var tabletUi = BuildTelemetryUI(c3, hub, overlay, deploy);
            AddOverlay(c3, hub, mrSc, 0.8f);
            LanguageRow(c1, 724, 18, 240, 64);
            // Screen 0 - Data Table (connection + live key / value pairs at 1 Hz), taller for the extra rows
            var s0 = BuildScreen("Screen - Data Table", layout.transform, slots[0], barMat, out var c0, ScreenW, 820f, Px);
            BuildDataTableUI(c0, hub, deploy, replay);
            AddOverlay(c0, hub, mrSc, 0.55f);
            screens.AddRange(new[] { s0, s1, s2, s3 });
            foreach (var c in new[] { c0, c1, c2, c3 })
            {
                QuickActionsAPI.AddRayCanvasInteraction(c.gameObject);
                QuickActionsAPI.AddPokeCanvasInteraction(c.gameObject);
            }
            // Slot outlines appear only while a screen is being dragged.
            var hints = layout.AddComponent<LayoutSlotHints>();
            hints.screens = screens.Select(s => s.transform).ToArray();
            hints.outlines = slots.Select(s => (Renderer)s.GetComponent<LineRenderer>()).ToArray();
            log.Add($"Screens: Data Table / Connection / Flight Map ({MapW * MapPx:0.00} x {MapH * MapPx:0.00} m) / Telemetry Data ({ScreenW * Px:0.00} x {ScreenH * Px:0.00} m) at {ScreenRadius} m; " +
                    "laser = push / pull + pitch / roll only, grab bar (direct grab) = free move, SnapInteractor; " + slots.Count + " layout slots");

            // ---------- VR scene extras: translucent Earth, ground track, Earth vector, axes toggle ----------
            var vrEarth = envVR.GetComponentInChildren<EarthController>(true);
            VrSceneExtras(vrEarth, vrSc, vrSpacecraftGo, log);
            var axesUi = new GameObject("Axes Toggle (UI target)").AddComponent<AxesToggle>();
            axesUi.transform.SetParent(systems.transform, false);

            // ---------- VR orbit ----------
            var orbit = orbitRig.AddComponent<OrbitalViewerLocomotion>();
            SetProps(orbit, ("earthCenter", eci.transform), ("satellite", vrSpacecraftGo.transform), ("head", centerT),
                            ("spacecraft", vrSc), ("earth", vrEarth));
            orbit.enabled = false;

            // Controller navigation of any panel the ray hovers (sticks move between buttons / IP octets, A / X click).
            new GameObject("UI Hover Navigation (sticks + A / X on panels)").AddComponent<UiHoverNav>().transform.SetParent(systems.transform, false);

            // ================= VR: controls guide (centred pop-up, iPad toggle) + left-wrist iPad =================
            var guide = BuildVrGuide(centerT, trackingSpace, envVR);
            guide.gameObject.SetActive(false);
            var leftAnchor = rigGo.transform.Find("TrackingSpace/LeftHandAnchor");
            var ipad = BuildWristIpad(leftAnchor, hub, deploy, orbit, mrSc, replay, axesUi, guide.gameObject, out var ipadUi);
            QuickActionsAPI.AddRayCanvasInteraction(ipad.gameObject);
            QuickActionsAPI.AddPokeCanvasInteraction(ipad.gameObject);
            ipad.transform.parent.gameObject.SetActive(false);

            // Command banner (both modes): floats above the MR Earth, lazily follows the top of the view in VR. Never on the iPad.
            BuildCommandBanner(centerT, trackingSpace, globe.transform, deploy);
            // Controller guide on the right controller: fades in when it is tilted down toward the body.
            BuildControllerGuide(rigGo.transform.Find("TrackingSpace/RightHandAnchor"), centerT);
            log.Add("Added: 7 s smoothing buffer (no loading screen), Data Table, command banner, grabbable Replay window + timeline, EN/FR toggles, two solar panels, Simulation Mode, body axes toggle, UI stick navigation, controller tilt guide");
            log.Add("VR: controls guide = centred pop-up from the iPad CONTROLS toggle; wrist iPad on LeftHandAnchor (telemetry, deploy + Simulation Mode, viewpoints, axes, replay, connections, controls)");

            // Any snap-back zones the Quick Actions may have added: screens must stay where they are released.
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
                if (go && go.name.StartsWith("ISDK_SnapZone")) Object.DestroyImmediate(go);

            // ---------- White rays ----------
            string rayMatPath = "Assets/EOSAT/Materials/EOSAT_WhiteRay.mat";
            var rayMat = AssetDatabase.LoadAssetAtPath<Material>(rayMatPath);
            if (!rayMat) { rayMat = new Material(Shader.Find("Interaction/OculusRayVisual")); AssetDatabase.CreateAsset(rayMat, rayMatPath); }
            rayMat.shader = Shader.Find("Interaction/OculusRayVisual");
            rayMat.SetColor("_Color", Color.white);
            rayMat.SetFloat("_Feather", 0f);
            EditorUtility.SetDirty(rayMat);
            int rays = 0;
            foreach (var rv in rigGo.GetComponentsInChildren<RayInteractorRayVisual>(true))
            {
                var so = new SerializedObject(rv);
                so.FindProperty("_hoverColor").colorValue = Color.white;
                so.FindProperty("_selectColor").colorValue = Color.white;
                so.FindProperty("_maxRayVisualLength").floatValue = 3f;
                so.FindProperty("_hideWhenNoInteractable").boolValue = false;
                so.FindProperty("_minRayVisualStartOffset").floatValue = 0.02f;
                so.ApplyModifiedPropertiesWithoutUndo();
                rv.transform.localScale = new Vector3(0.002f, 0.002f, rv.transform.localScale.z);
                var rend = so.FindProperty("_renderer").objectReferenceValue as Renderer;
                if (rend) { rend.sharedMaterial = rayMat; EditorUtility.SetDirty(rend); }
                EditorUtility.SetDirty(rv);
                rays++;
            }
            log.Add($"Rays: {rays} solid white lasers (2 mm, up to 3 m, always visible)");

            // ---------- Switcher ----------
            var swGo = new GameObject("Environment Switcher (B / Y)");
            swGo.transform.SetParent(systems.transform, false);
            var sw = swGo.AddComponent<EnvironmentSwitcher>();
            var sso = new SerializedObject(sw);
            sso.FindProperty("environmentVR").objectReferenceValue = envVR;
            sso.FindProperty("environmentMR").objectReferenceValue = envMR;
            sso.FindProperty("startMode").enumValueIndex = (int)EnvironmentSwitcher.Mode.MR;
            sso.FindProperty("orbitalLocomotion").objectReferenceValue = orbit;
            sso.FindProperty("tabletopZoom").objectReferenceValue = null;   // fixed-size Earth
            sso.FindProperty("centerEyeCamera").objectReferenceValue = cam;
            sso.FindProperty("passthroughLayer").objectReferenceValue = passLayer;
            // No skybox in VR: the procedural one drew its own sun disc (the "second sun") and a grey horizon. Space is black.
            sso.FindProperty("vrSkybox").objectReferenceValue = null;
            // VR: wrist iPad only (the guide is shown from the iPad toggle and hides itself in MR). MR: the floating screens.
            SetArray(sso.FindProperty("vrOnlyObjects"), ipad.transform.parent.gameObject);
            SetArray(sso.FindProperty("mrOnlyObjects"), s0, s1, s2, s3);
            sso.ApplyModifiedPropertiesWithoutUndo();
            tabletUi.switcher = sw; EditorUtility.SetDirty(tabletUi);
            ipadUi.switcher = sw; EditorUtility.SetDirty(ipadUi);
            log.Add("Switcher: start MR; VR background black (procedural skybox with its own sun disc removed)");

            // Desktop preview (no headset): nothing to pin to the view any more; screens are clickable with the mouse.
            var fb = fallbackGo ? fallbackGo.GetComponent<MrDesktopFallback>() : null;
            if (fb)
            {
                fb.cam = cam; fb.handTablet = null; fb.tabletCanvas = null; fb.desktopHint = null; fb.tablet = tabletUi; fb.connection = conn;
                fb.placer = null; fb.focus = null; fb.hideOnDesktop = null; fb.previewRoom = null;
                EditorUtility.SetDirty(fb);
            }

            envMR.SetActive(true);
            envVR.SetActive(false);

            int rigs = Object.FindObjectsByType<OVRCameraRig>(FindObjectsInactive.Include).Length;
            int mains = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include).Count(c => c.CompareTag("MainCamera"));
            int listeners = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include).Count(a => a.enabled);
            int dirLights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include).Count(l => l.type == LightType.Directional);
            int snapI = Object.FindObjectsByType<SnapInteractor>(FindObjectsInactive.Include).Length;
            log.Add($"Check: {rigs} OVRCameraRig, {mains} MainCamera, {listeners} AudioListener, {dirLights} directional lights (1 per environment), {snapI} SnapInteractors, {slots.Count} SnapInteractables");

            log.Add($"Localization: LocalizedText on {LocalizeStaticTexts()} static labels");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            var list = EditorBuildSettings.scenes.Where(s => s.path != OutPath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(OutPath, true));
            EditorBuildSettings.scenes = list.ToArray();
            log.Add("Saved " + OutPath);
            return string.Join("\n", log);
        }

        // =====================================================================================
        // Screens, grab bars, slots
        // =====================================================================================

        static GameObject BuildScreen(string name, Transform parent, SnapInteractable startSlot, Material barMat, out Canvas canvas,
                                      float w = ScreenW, float h = ScreenH, float px = Px)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            if (startSlot) root.transform.SetLocalPositionAndRotation(startSlot.transform.localPosition, startSlot.transform.localRotation);
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false; rb.interpolation = RigidbodyInterpolation.Interpolate;

            // Canvas
            var cgo = new GameObject("Canvas", typeof(RectTransform));
            cgo.transform.SetParent(root.transform, false);
            canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = cgo.AddComponent<CanvasScaler>(); scaler.dynamicPixelsPerUnit = 2f;
            cgo.AddComponent<GraphicRaycaster>();
            var crt = (RectTransform)cgo.transform;
            crt.sizeDelta = new Vector2(w, h);
            crt.localScale = Vector3.one * px;
            var bg = Stretch(crt, "Background"); Img(bg, PanelBg, true);

            // Grab bar: transparent pill under the screen (like Meta Home windows). The ONLY collider of the screen.
            float halfH = h * px * 0.5f;
            var bar = new GameObject("Grab Bar (point + trigger / pinch to move)");
            bar.transform.SetParent(root.transform, false);
            bar.transform.localPosition = new Vector3(0f, -halfH - 0.05f, 0f);
            var col = bar.AddComponent<BoxCollider>(); col.size = new Vector3(0.40f, 0.07f, 0.05f);   // generous target for the laser
            var pill = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            pill.name = "Pill (visual)";
            Object.DestroyImmediate(pill.GetComponent<Collider>());
            pill.transform.SetParent(bar.transform, false);
            pill.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            pill.transform.localScale = new Vector3(0.022f, 0.15f, 0.022f);                         // 30 cm long, 2.2 cm thick
            var pr = pill.GetComponent<Renderer>(); pr.sharedMaterial = barMat; pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // ISDK: near grab + laser (ray) grab. Added before the canvas surfaces so only the bar is grabbable.
            // Direct (near) grab of the bar = free move. Laser = push / pull along the ray + pitch / roll, never sideways.
            QuickActionsAPI.AddGrabInteraction(root);
            QuickActionsAPI.AddRayGrabInteraction(root);
            var depthOnly = root.AddComponent<RayDepthOnlyProvider>();
            foreach (var ri in root.GetComponentsInChildren<RayInteractable>(true)) { ri.InjectOptionalMovementProvider(depthOnly); EditorUtility.SetDirty(ri); }
            foreach (var dg in root.GetComponentsInChildren<DistanceGrabInteractable>(true)) { dg.InjectOptionalMovementProvider(depthOnly); EditorUtility.SetDirty(dg); }
            var grabbable = root.GetComponent<Grabbable>();
            if (grabbable)
            {
                grabbable.InjectOptionalKinematicWhileSelected(true);     // hovers where released
                grabbable.InjectOptionalThrowWhenUnselected(false);
                EditorUtility.SetDirty(grabbable);
            }

            if (!startSlot) return root;                                    // free window (Replay): no layout slots
            // Drag & drop: this screen can be dropped into any Layout Slot (SnapInteractable).
            var snapGo = new GameObject("ISDK_SnapInteractor");
            snapGo.transform.SetParent(root.transform, false);
            var snap = snapGo.AddComponent<SnapInteractor>();
            snap.InjectAllSnapInteractor(grabbable, rb);
            snap.InjectOptionalSnapPoseTransform(root.transform);
            var sso = new SerializedObject(snap);
            var def = sso.FindProperty("_defaultInteractable"); if (def != null) def.objectReferenceValue = startSlot;
            sso.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }

        static SnapInteractable BuildSlot(string name, Transform parent, Material lineMat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
            float w = ScreenW * Px, h = ScreenH * Px;
            // Trigger where a screen's grab bar ends up: dropping the bar inside it snaps the screen to this slot.
            var col = go.AddComponent<BoxCollider>(); col.isTrigger = true;
            col.center = new Vector3(0f, -h * 0.5f - 0.05f, 0f); col.size = new Vector3(0.55f, 0.28f, 0.45f);
            var snap = go.AddComponent<SnapInteractable>();
            snap.InjectAllSnapInteractable(rb);
            // Faint outline so you can see where the slots are.
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false; lr.loop = true; lr.positionCount = 4;
            lr.SetPositions(new[] { new Vector3(-w / 2, -h / 2, 0), new Vector3(-w / 2, h / 2, 0), new Vector3(w / 2, h / 2, 0), new Vector3(w / 2, -h / 2, 0) });
            lr.widthMultiplier = 0.004f; lr.sharedMaterial = lineMat;
            lr.startColor = lr.endColor = new Color(1f, 1f, 1f, 0.22f);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return snap;
        }

        static void AddOverlay(Canvas canvas, TelemetryHub hub, SpacecraftController sc, float dim)
        {
            var rt = Stretch((RectTransform)canvas.transform, "Prediction Mode Overlay (data lost)");
            rt.gameObject.AddComponent<CanvasGroup>();
            Img(rt, new Color(0.02f, 0.03f, 0.05f, 0.82f), false);
            Vector2 size = ((RectTransform)canvas.transform).sizeDelta;
            var msg = Txt(rt, "Message", "", 32, TextAnchor.MiddleCenter, TextCol, 40, 120, size.x - 80, size.y - 240);
            var ov = rt.gameObject.AddComponent<DataLossOverlay>();
            ov.hub = hub; ov.spacecraft = sc; ov.message = msg; ov.dimAlpha = dim;
        }

        // ---------------- Screen 1: Connection ----------------
        static ConnectionUIManager BuildConnectionUI(Canvas canvas, TelemetryHub hub)
        {
            var root = (RectTransform)canvas.transform;
            Txt(root, "Title", "<b>CONNECTION</b>", 44, TextAnchor.MiddleLeft, TextCol, 36, 22, 600, 60);
            var status = Txt(root, "Status", "", 32, TextAnchor.MiddleLeft, TextCol, 36, 90, 930, 44);
            var detail = Txt(root, "Detail", "", 24, TextAnchor.MiddleLeft, Dim, 36, 134, 930, 36);

            var sim = Btn(root, "Button SIM", "SIMULATION", 36, 185, 296, 84, Idle, 30);
            var tcp = Btn(root, "Button TCP 8100", "TCP  8100", 352, 185, 296, 84, Idle, 30);
            var ws = Btn(root, "Button WS 8101", "WEBSOCKET  8101", 668, 185, 296, 84, Idle, 28);

            Txt(root, "IP Label", "Simulator address: IP, IP/path or wss:// URL", 24, TextAnchor.MiddleLeft, Dim, 36, 290, 560, 34);
            // Input field
            var fieldRt = Box(root, "IP Input Field", 36, 328, 560, 76);
            var fieldImg = Img(fieldRt, new Color(0.02f, 0.02f, 0.03f, 1f), true);
            var field = fieldRt.gameObject.AddComponent<InputField>();
            var fText = Txt(fieldRt, "Text", "", 40, TextAnchor.MiddleLeft, TextCol, 18, 8, 524, 60); fText.supportRichText = false;
            var ph = Txt(fieldRt, "Placeholder", "e.g. 8.232.20.196/VR", 34, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.3f), 18, 8, 524, 60);
            field.targetGraphic = fieldImg; field.textComponent = fText; field.placeholder = ph; field.characterLimit = 48;
            field.text = hub ? hub.host : "127.0.0.1";

            var set = Btn(root, "Button SET IP", "SET IP", 36, 420, 180, 76, Accent, 30);
            var local = Btn(root, "Button Localhost", "127.0.0.1", 228, 420, 190, 76, Idle, 28);
            var clear = Btn(root, "Button Clear", "CLEAR", 430, 420, 166, 76, Idle, 28);
            var hostStatus = Txt(root, "IP Status", "Live 8101, power 8103 and commands 8104 all use this address.", 20, TextAnchor.UpperLeft, Dim, 36, 504, 560, 62);
            var ops = Btn(root, "Button Ops Server", "OPS  wss://ops.cgo.live/VR", 36, 570, 380, 58, Accent, 22);
            var vrPath = Btn(root, "Button /VR", "/VR", 426, 570, 170, 58, Idle, 28);

            var conn = canvas.gameObject.AddComponent<ConnectionUIManager>();
            conn.hub = hub; conn.simulatedButton = sim; conn.tcpButton = tcp; conn.webSocketButton = ws;
            conn.statusText = status; conn.detailText = detail; conn.hostField = field; conn.hostStatus = hostStatus;
            conn.idleColor = Idle; conn.activeColor = Accent;
            UnityEventTools.AddPersistentListener(set.onClick, conn.ApplyCustomHost);
            UnityEventTools.AddPersistentListener(local.onClick, conn.UseLocalhost);
            UnityEventTools.AddPersistentListener(clear.onClick, conn.KeypadClear);
            if (ops) UnityEventTools.AddPersistentListener(ops.onClick, conn.UseOpsServer);
            if (vrPath) UnityEventTools.AddStringPersistentListener(vrPath.onClick, conn.KeypadPress, "/VR");

            // Keypad (a headset over Link has no system keyboard)
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", ".", "0", "DEL" };
            for (int i = 0; i < keys.Length; i++)
            {
                float x = 632 + (i % 3) * 112, y = 290 + (i / 3) * 82;
                var b = Btn(root, "Key " + keys[i], keys[i], x, y, 104, 74, BoxBg, keys[i] == "DEL" ? 24 : 34);
                if (keys[i] == "DEL") UnityEventTools.AddPersistentListener(b.onClick, conn.KeypadBackspace);
                else UnityEventTools.AddStringPersistentListener(b.onClick, conn.KeypadPress, keys[i]);
            }
            return conn;
        }

        // ---------------- Screen 2: Flight Map ----------------
        static void BuildFlightMapUI(Canvas canvas, TelemetryHub hub, SpacecraftController mrSc, SpacecraftController vrSc)
        {
            // 1600 x 940 canvas. Left column: Time, Next passes, Link, Budgets. Right: map, then the plan strip.
            var root = (RectTransform)canvas.transform;
            var map = canvas.gameObject.AddComponent<FlightMapUI>();
            map.spacecraft = mrSc; map.alternate = vrSc; map.hub = hub;
            Txt(root, "Title", "<b>FLIGHT MAP</b>", 42, TextAnchor.MiddleLeft, TextCol, 24, 8, 400, 54);
            map.simChip = Chip(root, "Chip Sim", 1180, 14, 180);
            map.rateChip = Chip(root, "Chip Rate", 1376, 14, 200);

            // ---- Left column ----
            const float lx = 20, lw = 400;
            var time = Section(root, "Time", "TIME", lx, 70, lw, 200);
            map.dateText = Txt(time, "Date", "--", 26, TextAnchor.MiddleLeft, Dim, 16, 40, lw - 32, 34);
            map.clockText = Txt(time, "Clock", "--:--:--", 60, TextAnchor.MiddleLeft, TextCol, 16, 74, lw - 32, 70);
            map.metText = Txt(time, "MET", "MET --", 24, TextAnchor.MiddleLeft, Dim, 16, 150, lw - 32, 34);

            var passes = Section(root, "Next passes", "NEXT PASSES", lx, 280, lw, 330);
            map.passHeadline = Txt(passes, "Headline", "--", 28, TextAnchor.MiddleLeft, TextCol, 16, 40, lw - 32, 38);
            map.passCountdown = Txt(passes, "Countdown", "", 40, TextAnchor.MiddleLeft, new Color(0.3f, 0.85f, 1f), 16, 80, lw - 32, 50);
            Txt(passes, "Table Header", "STATION        AOS      LOS     MAX EL", 18, TextAnchor.MiddleLeft, Dim, 16, 136, lw - 32, 26);
            map.passTable = Txt(passes, "Table", "", 20, TextAnchor.UpperLeft, TextCol, 16, 164, lw - 32, 160);
            map.passTable.lineSpacing = 1.15f; map.passTable.horizontalOverflow = HorizontalWrapMode.Overflow;

            var link = Section(root, "Link", "LINK", lx, 620, lw, 130);
            map.linkText = Txt(link, "Link", "", 20, TextAnchor.UpperLeft, TextCol, 16, 40, lw - 32, 86);

            var budgets = Section(root, "Budgets", "BUDGETS", lx, 760, lw, 170);
            map.budgetsText = Txt(budgets, "Budgets", "", 19, TextAnchor.UpperLeft, TextCol, 16, 38, lw - 32, 96);
            map.socFill = FilledBar(budgets, "SoC Bar", 16, 138, lw - 32, 18, new Color(0.3f, 0.9f, 0.45f, 1f));

            // ---- Map (2:1 equirectangular) ----
            var mapRt = Box(root, "Map (equirectangular)", 440, 70, 1140, 570);
            var raw = mapRt.gameObject.AddComponent<RawImage>();
            raw.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/EOSAT/Textures/earth_atmos_2048.jpg");
            raw.color = new Color(0.8f, 0.8f, 0.8f, 1f); raw.raycastTarget = false;
            Img(Center(mapRt, "Equator", 1140, 2), new Color(1, 1, 1, 0.25f), false);
            Img(Center(mapRt, "Prime Meridian", 2, 570), new Color(1, 1, 1, 0.25f), false);
            var size = new Vector2(1140, 570);
            // Ground stations
            for (int s = 0; s < OrbitPredictor.Stations.Length; s++)
            {
                var st = OrbitPredictor.Stations[s];
                var m = Center(mapRt, "Station " + st.name, 14, 14);
                m.anchoredPosition = FlightMapUI.MapPosition(st.latDeg, st.lonDeg, size);
                m.localRotation = Quaternion.Euler(0, 0, 45);
                Img(m, new Color(0.3f, 1f, 0.55f, 1f), false);
                var lbl = Txt(mapRt, "Station Label " + st.name, st.name.ToUpperInvariant(), 17, TextAnchor.MiddleLeft, new Color(0.6f, 1f, 0.7f), 0, 0, 150, 24);
                lbl.rectTransform.anchorMin = lbl.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                lbl.rectTransform.pivot = new Vector2(0f, 0.5f);
                lbl.rectTransform.anchoredPosition = m.anchoredPosition + new Vector2(12f, 0f);
            }
            var dotsRoot = Center(mapRt, "Ground Track", 0, 0);
            map.trackDots = Dots(dotsRoot, "past", 110, 7, new Color(0.3f, 0.85f, 1f, 0.9f));
            map.nextOrbitDots = Dots(dotsRoot, "next orbit (solid)", 190, 5, Color.white);
            map.laterDots = Dots(dotsRoot, "later orbits (dotted)", 150, 4, new Color(1, 1, 1, 0.45f));
            for (int i = 0; i < 6; i++)
            {
                var c = Txt(dotsRoot, "AOS Callout " + i, "", 17, TextAnchor.MiddleLeft, new Color(0.3f, 1f, 0.55f), 0, 0, 260, 24);
                c.rectTransform.anchorMin = c.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                c.rectTransform.pivot = new Vector2(0f, 0.5f);
                c.enabled = false; map.passCallouts.Add(c);
            }
            var marker = Center(mapRt, "Satellite Marker", 24, 24);
            map.markerImage = Img(marker, new Color(1f, 0.85f, 0.2f, 1f), false);
            Img(Center(marker, "Core", 8, 8), new Color(0.1f, 0.1f, 0.1f, 1f), false);
            Txt(marker, "Name", "EOSAT-1", 22, TextAnchor.MiddleLeft, TextCol, 28, -4, 160, 30);
            map.mapRect = mapRt; map.marker = marker;
            map.readout = Txt(root, "Readout", "", 22, TextAnchor.MiddleLeft, TextCol, 440, 644, 1140, 32);

            // ---- Plan strip: next orbit, then 24 h ----
            const float px0 = 440, labelW = 170, barX = px0 + labelW, barW = 1580 - barX;
            map.nextOrbitLabel = Txt(root, "Next Orbit Label", "NEXT ORBIT", 20, TextAnchor.MiddleLeft, Accent, px0, 680, 1140, 28);
            map.nextOrbitContacts = PlanRow(root, "CONTACTS", px0, 710, labelW, barX, barW);
            map.nextOrbitEclipse = PlanRow(root, "ECLIPSE", px0, 738, labelW, barX, barW);
            Txt(root, "Day Label", "NEXT 24 H", 20, TextAnchor.MiddleLeft, Accent, px0, 774, 300, 28);
            map.dayContacts = PlanRow(root, "CONTACTS", px0, 804, labelW, barX, barW);
            var activities = PlanRow(root, "ACTIVITIES", px0, 832, labelW, barX, barW);
            Txt(activities, "None", "no activity plan (no MCS URL configured)", 16, TextAnchor.MiddleLeft, Dim, 8, 0, barW - 16, 24);
            map.dayEclipse = PlanRow(root, "ECLIPSE", px0, 860, labelW, barX, barW);
            for (int hr = 0; hr <= 24; hr += 4)
            {
                var tick = Txt(root, "Tick +" + hr + "h", "+" + hr + "h", 16, TextAnchor.UpperCenter, Dim, barX + barW * hr / 24f - 30, 890, 60, 24);
                tick.raycastTarget = false;
            }
        }

        static Text Chip(RectTransform root, string name, float x, float y, float w)
        {
            var rt = Box(root, name, x, y, w, 44); Img(rt, BoxBg, false);
            return Txt(rt, "Label", "--", 20, TextAnchor.MiddleCenter, TextCol, 0, 0, w, 44);
        }

        static RectTransform Section(RectTransform root, string name, string title, float x, float y, float w, float h)
        {
            var rt = Box(root, name, x, y, w, h); Img(rt, BoxBg, false);
            Txt(rt, "Header", "<color=#4DC8FF>" + title + "</color>", 20, TextAnchor.MiddleLeft, TextCol, 16, 6, w - 32, 30);
            return rt;
        }

        static List<Image> Dots(RectTransform parent, string name, int count, float size, Color c)
        {
            var list = new List<Image>();
            var group = Center(parent, name, 0, 0);
            for (int i = 0; i < count; i++) { var d = Img(Center(group, "dot", size, size), c, false); d.enabled = false; list.Add(d); }
            return list;
        }

        static RectTransform PlanRow(RectTransform root, string label, float x, float y, float labelW, float barX, float barW)
        {
            Txt(root, "Plan Label " + label + " " + y, label, 17, TextAnchor.MiddleLeft, Dim, x, y, labelW, 24);
            var row = Box(root, "Plan Row " + label + " " + y, barX, y, barW, 24);
            Img(row, new Color(0.02f, 0.02f, 0.03f, 1f), false);
            return row;
        }

        // ---------------- Screen 3: Telemetry Data ----------------
        static TabletUIController BuildTelemetryUI(Canvas canvas, TelemetryHub hub, TelemetryOverlay overlay, SatelliteDeployControl deploy)
        {
            var root = (RectTransform)canvas.transform;
            Txt(root, "Title", "<b>TELEMETRY DATA</b>", 44, TextAnchor.MiddleLeft, TextCol, 36, 22, 600, 60);
            var box = Box(root, "Satellite Data", 30, 96, 540, 514); Img(box, BoxBg, false);
            Txt(box, "Header", "<color=#4DC8FF>SATELLITE DATA</color>", 24, TextAnchor.MiddleLeft, TextCol, 22, 12, 480, 34);
            var data = Txt(box, "Readout", "Waiting for telemetry...", 34, TextAnchor.UpperLeft, TextCol, 22, 60, 500, 440);
            data.lineSpacing = 1.25f;
            var readout = canvas.gameObject.AddComponent<TelemetryReadoutUI>();
            readout.hub = hub; readout.dataText = data;
            AntennaBar(box, 22, 430, 496, readout, deploy);

            var ui = canvas.gameObject.AddComponent<TabletUIController>();
            ui.telemetry = overlay; ui.satellite = deploy;
            ui.dataToggle = Row(root, "Row Data Overlay", "Data Overlay", 96, out ui.dataStatus, 590, 380, 84);
            ui.simToggle = DeployButtons(root, deploy, 590, 192, 380, 66, 24, out ui.panelsStatus, out ui.antennaStatus);   // y 192 .. 506
            ui.arVrToggle = Row(root, "Row MR / VR View", "MR / VR View", 532, out ui.arVrStatus, 590, 380, 84);
            return ui;
        }

        // Deploy commands: A, B, A&B, Antenna. Live: they only queue a command to the Flight Commander (8104) and the
        // model moves when telemetry shows the deployment (status on the CommandQueueBanner, not here).
        // Simulation Mode switch: the same buttons preview the deployment locally (nothing sent).
        // Height used: 116 + 3 h.
        static Toggle DeployButtons(RectTransform root, SatelliteDeployControl deploy, float x, float y, float w, float h, int font,
                                    out Text panelsStatus, out Text antennaStatus)
        {
            Txt(root, "Deploy Header", "<color=#4DC8FF>DEPLOY  (Flight Commander approval)</color>", font - 4, TextAnchor.MiddleLeft, TextCol, x, y, w, 30);
            float bw = (w - 10) / 2f;
            var a = Btn(root, "Button Deploy Panel A", "PANEL A", x, y + 34, bw, h, Idle, font);
            var b = Btn(root, "Button Deploy Panel B", "PANEL B", x + bw + 10, y + 34, bw, h, Idle, font);
            var ab = Btn(root, "Button Deploy Panels A+B", "PANELS A + B", x, y + 44 + h, bw, h, Idle, font);
            var ant = Btn(root, "Button Deploy Antenna", "ANTENNA", x + bw + 10, y + 44 + h, bw, h, Idle, font);
            UnityEventTools.AddPersistentListener(a.onClick, deploy.DeployPanelA);
            UnityEventTools.AddPersistentListener(b.onClick, deploy.DeployPanelB);
            UnityEventTools.AddPersistentListener(ab.onClick, deploy.DeployPanelsAB);
            UnityEventTools.AddPersistentListener(ant.onClick, deploy.DeployAntenna);
            var sim = Row(root, "Row Simulation Mode", "Simulation Mode", y + 54 + 2 * h, out var simStatus, x, w, h);
            simStatus.text = "";
            UnityEventTools.AddPersistentListener(sim.onValueChanged, deploy.SetSimulationMode);
            panelsStatus = Txt(root, "Panels Status", "", font - 6, TextAnchor.MiddleLeft, new Color(0.3f, 0.78f, 1f, 1f), x, y + 60 + 3 * h, w, 28);
            antennaStatus = Txt(root, "Antenna Status", "", font - 6, TextAnchor.MiddleLeft, new Color(0.3f, 0.78f, 1f, 1f), x, y + 88 + 3 * h, w, 28);
            return sim;
        }

        static Toggle Row(RectTransform root, string name, string label, float y, out Text status, float x = 590, float w = 380, float h = 118)
        {
            var rt = Box(root, name, x, y, w, h);
            var bg = Img(rt, BoxBg, true);
            var toggle = rt.gameObject.AddComponent<Toggle>();
            Txt(rt, "Label", "<b>" + label + "</b>", 25, TextAnchor.MiddleLeft, TextCol, 20, 10, w - 120, 40);
            status = Txt(rt, "Status", "", 20, TextAnchor.MiddleLeft, new Color(0.3f, 0.78f, 1f, 1f), 20, Mathf.Max(48f, h - 56f), w - 40, 40);
            var sw = Box(rt, "Switch", w - 100, 16, 80, 36); Img(sw, Idle, false);
            var on = Stretch(sw, "On"); var onImg = Img(on, Accent, false);
            toggle.targetGraphic = bg; toggle.graphic = onImg; toggle.isOn = false;
            return toggle;
        }

        static void SetArray(SerializedProperty p, params GameObject[] items)
        {
            p.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        // ---------------- VR: controls guide - centred pop-up toggled from the iPad ----------------
        static Canvas BuildVrGuide(Transform centerEye, Transform trackingSpace, GameObject envVR)
        {
            var cgo = new GameObject("VR Controls Guide (centred pop-up, iPad CONTROLS toggle)", typeof(RectTransform));
            cgo.transform.SetParent(trackingSpace, false);                     // moves with the rig in the orbit view, not with the head
            var place = cgo.AddComponent<ComfortPlacement>();                   // appears straight ahead each time it is opened
            place.head = centerEye; place.distance = PopupDistance; place.up = -0.05f; place.follow = false;
            place.onlyWhileActive = envVR;                                      // hidden in MR mode
            var rt = (RectTransform)cgo.transform;
            var canvas = cgo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 10;
            var scaler = cgo.AddComponent<CanvasScaler>(); scaler.dynamicPixelsPerUnit = 2f;
            rt.sizeDelta = new Vector2(760, 380); rt.localScale = Vector3.one * 0.0005f;    // 0.38 x 0.19 m at 0.8 m
            Img(Stretch(rt, "Background"), new Color(0.05f, 0.06f, 0.08f, 0.92f), false);
            Txt(rt, "Title", "<b>VR CONTROLS</b>", 34, TextAnchor.MiddleLeft, TextCol, 24, 10, 500, 46);
            Txt(rt, "Guide",
                "<color=#4DC8FF>LEFT STICK</color>   orbit the spacecraft - full 360°\n" +
                "<color=#4DC8FF>RIGHT STICK</color>   up / down = zoom\n" +
                "<color=#4DC8FF>A  /  X</color>   recentre  (on a panel: click)\n" +
                "<color=#4DC8FF>B  /  Y</color>   switch MR tabletop / VR orbit\n" +
                "<color=#4DC8FF>RAY ON A PANEL</color>   sticks move between buttons\n" +
                "<color=#4DC8FF>LEFT WRIST</color>   iPad: deploy, viewpoints, axes, replay, this guide",
                24, TextAnchor.UpperLeft, TextCol, 24, 60, 720, 310).lineSpacing = 1.25f;
            return canvas;
        }

        // ---------------- VR: iPad on the left wrist ----------------
        const float IpadW = 1100f, IpadH = 820f, IpadPx = 0.0003f;          // 0.33 x 0.25 m

        static Canvas BuildWristIpad(Transform leftHand, TelemetryHub hub, SatelliteDeployControl deploy, OrbitalViewerLocomotion orbit,
                                     SpacecraftController sc, ReplayController replay, AxesToggle axes, GameObject guide, out TabletUIController ui)
        {
            var mount = new GameObject("Wrist iPad (VR, left wrist)");
            mount.transform.SetParent(leftHand, false);
            var cgo = new GameObject("Canvas", typeof(RectTransform));
            cgo.transform.SetParent(mount.transform, false);
            var wrist = mount.AddComponent<WristTablet>();
            SetProps(wrist, ("wristAnchor", leftHand), ("faceUser", true), ("liftAboveWrist", 0.15f), ("towardEyes", 0.03f));
            var wso = new SerializedObject(wrist); SetArray(wso.FindProperty("content"), cgo); wso.ApplyModifiedPropertiesWithoutUndo();

            var canvas = cgo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            var scaler = cgo.AddComponent<CanvasScaler>(); scaler.dynamicPixelsPerUnit = 2f;
            cgo.AddComponent<GraphicRaycaster>();
            var root = (RectTransform)cgo.transform;
            root.sizeDelta = new Vector2(IpadW, IpadH); root.localScale = Vector3.one * IpadPx;
            Img(Stretch(root, "Bezel"), new Color(0.02f, 0.02f, 0.025f, 1f), true);
            var screen = Box(root, "Screen", 14, 14, IpadW - 28, IpadH - 28); Img(screen, PanelBg, true);

            // Left: glanceable telemetry (lat / lon / alt, panels, antenna, battery / temp, link)
            Txt(screen, "Title", "<b>EOSAT-1</b>", 36, TextAnchor.MiddleLeft, TextCol, 22, 8, 260, 50);
            LanguageRow(screen, 290, 6, 200, 52);
            var box = Box(screen, "Telemetry Data", 18, 66, 480, 700); Img(box, BoxBg, false);
            var data = Txt(box, "Readout", "Waiting for telemetry...", 30, TextAnchor.UpperLeft, TextCol, 18, 14, 450, 480);
            data.lineSpacing = 1.25f;
            var readout = cgo.AddComponent<TelemetryReadoutUI>(); readout.hub = hub; readout.dataText = data;
            AntennaBar(box, 18, 512, 444, readout, deploy);

            // Right column
            ui = cgo.AddComponent<TabletUIController>();
            ui.satellite = deploy;
            const float rx = 512, rw = 546;
            // No command status here: queued / executed commands show on the banner (top of view), not on the wrist.
            ui.simToggle = DeployButtons(screen, deploy, rx, 6, rw, 56, 24, out ui.panelsStatus, out ui.antennaStatus);   // y 6 .. 290

            // Viewpoints (tumble lock greys out the two spacecraft-fixed ones)
            Txt(screen, "Views Header", "<color=#4DC8FF>VIEWPOINT</color>", 20, TextAnchor.MiddleLeft, TextCol, rx, 296, rw, 28);
            var vp = cgo.AddComponent<ViewpointButtonsUI>();
            vp.orbit = orbit; vp.idleColor = Idle; vp.activeColor = Accent;
            string[] viewNames = { "FREE", "EARTH", "SUN", "NADIR", "PANEL" };
            float vw = (rw - 4 * 8) / 5f;
            for (int i = 0; i < 5; i++) vp.buttons[i] = Btn(screen, "Button View " + viewNames[i], viewNames[i], rx + i * (vw + 8), 326, vw, 62, Idle, 21);
            vp.status = Txt(screen, "View Status", "", 18, TextAnchor.MiddleLeft, Dim, rx, 390, rw, 26);

            // Toggles: MR / VR, connections, axes, replay, controls guide
            float tw = (rw - 10) / 2f;
            ui.arVrToggle = Row(screen, "Row MR / VR View", "MR / VR", 422, out ui.arVrStatus, rx, tw, 84);
            var connToggle = Row(screen, "Row Connections", "Connections", 422, out var connRowStatus, rx + tw + 10, tw, 84);
            connRowStatus.text = "source, address";
            var axesToggle = Row(screen, "Row Axes", "Quaternion axes", 514, out var axesStatus, rx, tw, 84);
            axesStatus.text = "shown by default";
            axesToggle.isOn = true;
            UnityEventTools.AddPersistentListener(axesToggle.onValueChanged, axes.SetVisible);
            var replayToggle = Row(screen, "Row Replay", "Replay", 514, out var replayStatus, rx + tw + 10, tw, 84);
            replayStatus.text = "pop-up window";
            UnityEventTools.AddPersistentListener(replayToggle.onValueChanged, replay.SetReplay);
            var guideToggle = Row(screen, "Row Controls Guide", "Controls", 606, out var guideStatus, rx, tw, 84);
            guideStatus.text = "guide in front of you";
            UnityEventTools.AddPersistentListener(guideToggle.onValueChanged, guide.SetActive);
            var cam = Btn(screen, "Button Camera Target", "", rx + tw + 10, 606, tw, 84, new Color(0.2f, 0.55f, 1f, 1f), 20);
            var t = cgo.AddComponent<OrbitTargetToggleUI>();
            t.orbit = orbit; t.button = cam; t.label = cam.GetComponentInChildren<Text>(); t.background = cam.GetComponent<Image>();

            // Connections panel: covers the telemetry box while the Connections toggle is on.
            var panel = Box(screen, "Connections Panel (toggle)", 18, 66, 480, 700); Img(panel, new Color(0.10f, 0.12f, 0.15f, 1f), true);
            Txt(panel, "Title", "<color=#4DC8FF><b>CONNECTIONS</b></color>", 24, TextAnchor.MiddleLeft, TextCol, 16, 6, 300, 36);
            var dd = SourceDropdown(panel, 16, 46, 448, 58);
            var status = Txt(panel, "Status", "", 22, TextAnchor.MiddleLeft, TextCol, 16, 110, 450, 32);
            var detail = Txt(panel, "Detail", "", 18, TextAnchor.MiddleLeft, Dim, 16, 142, 450, 28);
            var fieldRt = Box(panel, "IP Input Field", 16, 176, 310, 56);
            var fieldImg = Img(fieldRt, new Color(0.02f, 0.02f, 0.03f, 1f), true);
            var field = fieldRt.gameObject.AddComponent<InputField>();
            var fText = Txt(fieldRt, "Text", "", 26, TextAnchor.MiddleLeft, TextCol, 12, 4, 290, 48); fText.supportRichText = false;
            var ph = Txt(fieldRt, "Placeholder", "IP or IP/VR", 24, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.3f), 12, 4, 290, 48);
            field.targetGraphic = fieldImg; field.textComponent = fText; field.placeholder = ph; field.characterLimit = 48;
            field.text = hub ? hub.host : "127.0.0.1";
            var set = Btn(panel, "Button SET IP", "SET IP", 336, 176, 128, 56, Accent, 24);
            var hostStatus = Txt(panel, "IP Status", "", 16, TextAnchor.UpperLeft, Dim, 16, 236, 450, 40);
            var conn = cgo.AddComponent<ConnectionUIManager>();
            conn.hub = hub; conn.sourceDropdown = dd; conn.statusText = status; conn.detailText = detail;
            conn.hostField = field; conn.hostStatus = hostStatus; conn.idleColor = Idle; conn.activeColor = Accent;
            UnityEventTools.AddPersistentListener(set.onClick, conn.ApplyCustomHost);
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", ".", "0", "DEL" };
            for (int i = 0; i < keys.Length; i++)
            {
                var b = Btn(panel, "Key " + keys[i], keys[i], 16 + (i % 6) * 75, 282 + (i / 6) * 64, 69, 58, BoxBg, keys[i] == "DEL" ? 18 : 26);
                if (keys[i] == "DEL") UnityEventTools.AddPersistentListener(b.onClick, conn.KeypadBackspace);
                else UnityEventTools.AddStringPersistentListener(b.onClick, conn.KeypadPress, keys[i]);
            }
            var local = Btn(panel, "Button Localhost", "127.0.0.1", 16, 414, 219, 58, Idle, 22);
            var clear = Btn(panel, "Button Clear", "CLEAR", 245, 414, 219, 58, Idle, 22);
            var ops = Btn(panel, "Button Ops Server", "OPS SERVER", 16, 480, 219, 58, Accent, 22);
            var vrPath = Btn(panel, "Button /VR", "/VR", 245, 480, 219, 58, Idle, 24);
            UnityEventTools.AddPersistentListener(local.onClick, conn.UseLocalhost);
            UnityEventTools.AddPersistentListener(clear.onClick, conn.KeypadClear);
            UnityEventTools.AddPersistentListener(ops.onClick, conn.UseOpsServer);
            UnityEventTools.AddStringPersistentListener(vrPath.onClick, conn.KeypadPress, "/VR");
            UnityEventTools.AddPersistentListener(connToggle.onValueChanged, panel.gameObject.SetActive);
            panel.gameObject.SetActive(false);

            AddOverlay(canvas, hub, sc, 0.7f);
            return canvas;
        }

        // ---------------- EN / FR toggle (on = French) ----------------
        static void LanguageRow(Component parent, float x, float y, float w, float h)
        {
            var t = Row((RectTransform)parent.transform, "Row Language (EN / FR)", "FRANÇAIS", y, out _, x, w, h);
            t.gameObject.AddComponent<LanguageToggle>();
        }

        // ---------------- Antenna deployment bar bound to TelemetryReadoutUI ----------------
        static Sprite FillSprite => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        static Image FilledBar(RectTransform parent, string name, float x, float y, float w, float h, Color fill)
        {
            var bg = Box(parent, name, x, y, w, h); Img(bg, new Color(0.02f, 0.02f, 0.03f, 1f), false);
            var f = Img(Stretch(bg, "Fill"), fill, false);
            f.sprite = FillSprite; f.type = Image.Type.Filled; f.fillMethod = Image.FillMethod.Horizontal; f.fillAmount = 0f;
            return f;
        }

        static void AntennaBar(RectTransform parent, float x, float y, float w, TelemetryReadoutUI readout, SatelliteDeployControl deploy)
        {
            readout.antennaText = Txt(parent, "Antenna %", "ANTENNA  --", 26, TextAnchor.MiddleLeft, TextCol, x, y, w, 34);
            readout.antennaFill = FilledBar(parent, "Antenna Bar", x, y + 38, w, 22, Accent);
            readout.deploy = deploy;
        }

        // ---------------- Screen 0: Data Table ----------------
        static void BuildDataTableUI(Canvas canvas, TelemetryHub hub, SatelliteDeployControl deploy, ReplayController replay)
        {
            var root = (RectTransform)canvas.transform;
            Txt(root, "Title", "<b>DATA TABLE</b>  <size=24><color=#9AA6B8>live values, 1 Hz</color></size>", 44, TextAnchor.MiddleLeft, TextCol, 36, 14, 700, 56);
            var rb = Btn(root, "Button Replay", "REPLAY", 790, 16, 180, 56, Idle, 26);
            UnityEventTools.AddPersistentListener(rb.onClick, replay.ToggleReplay);
            var box = Box(root, "Table", 30, 80, 940, 720); Img(box, BoxBg, false);
            Img(Box(box, "Column Divider", 300, 10, 2, 700), new Color(1, 1, 1, 0.12f), false);
            var keys = Txt(box, "Keys", "", 24, TextAnchor.UpperLeft, Dim, 18, 12, 270, 700);
            var vals = Txt(box, "Values", "", 24, TextAnchor.UpperLeft, TextCol, 318, 12, 610, 700);
            keys.lineSpacing = vals.lineSpacing = 1.05f;
            keys.horizontalOverflow = vals.horizontalOverflow = HorizontalWrapMode.Overflow;   // one row per line, keeps columns aligned
            var t = canvas.gameObject.AddComponent<DataTableUI>();
            t.hub = hub; t.deploy = deploy; t.keysText = keys; t.valuesText = vals; t.refreshHz = 1f; t.showRaw = false;
        }

        // ---------------- Command banner: above the MR Earth, or lazily following the top of the view in VR ----------------
        static void BuildCommandBanner(Transform centerEye, Transform trackingSpace, Transform mrGlobe, SatelliteDeployControl deploy)
        {
            var cgo = new GameObject("Command Queue Banner (above the Earth / top of view)", typeof(RectTransform));
            cgo.transform.SetParent(trackingSpace, false);
            var place = cgo.AddComponent<ComfortPlacement>();
            place.head = centerEye; place.distance = 0.95f; place.up = 0.32f; place.follow = true;   // ~19 deg above the eye line
            place.anchorAbove = mrGlobe; place.aboveHeight = 0.42f + 0.22f;                         // just over the MR Earth's north pole
            var canvas = cgo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 15;
            var scaler = cgo.AddComponent<CanvasScaler>(); scaler.dynamicPixelsPerUnit = 2f;
            var rt = (RectTransform)cgo.transform; rt.sizeDelta = new Vector2(1300, 150); rt.localScale = Vector3.one * 0.0006f;   // 0.78 x 0.09 m
            Img(Stretch(rt, "Background"), new Color(0.06f, 0.06f, 0.08f, 0.9f), false);
            var text = Txt(rt, "Text", "", 28, TextAnchor.MiddleCenter, TextCol, 20, 8, 1260, 134);
            var banner = cgo.AddComponent<CommandQueueBanner>();
            banner.deploy = deploy; banner.text = text;
        }

        // ---------------- Replay: grabbable pop-up window (grab bar) with the raw data list + timeline ----------------
        static ReplayController BuildReplayPanel(Transform centerEye, Transform trackingSpace, Transform systems, TelemetryHub hub,
                                                 SatelliteDeployControl deploy, Material barMat)
        {
            // Same window as the MR screens (grab bar, laser push / pull + tilt, direct grab moves it), but no layout slot.
            // Lives in the rig's TrackingSpace so it stays with the user in the VR orbit view; opens straight ahead, slightly right.
            var root = BuildScreen("Replay Window (grab bar)", trackingSpace, null, barMat, out var canvas, 760f, 1000f, 0.00048f);   // 0.36 x 0.48 m
            var place = root.AddComponent<ComfortPlacement>();
            place.head = centerEye; place.distance = PopupDistance; place.right = 0.12f; place.up = -0.06f; place.follow = false;
            var cgo = canvas.gameObject;
            canvas.sortingOrder = 12;
            var rt = (RectTransform)cgo.transform;
            Txt(rt, "Title", "<b>REPLAY</b>  <size=22><color=#9AA6B8>grab the bar to move, stick scrolls</color></size>", 36, TextAnchor.MiddleLeft, TextCol, 20, 8, 720, 52);
            var timeText = Txt(rt, "Time", "", 22, TextAnchor.MiddleLeft, new Color(1f, 0.75f, 0.3f), 20, 62, 720, 30);

            // Scrollable key / value list (DataTableUI with raw values + power fields + command log)
            var scrollRt = Box(rt, "Scroll View", 16, 98, 728, 700);
            Img(scrollRt, BoxBg, true);
            var sr = scrollRt.gameObject.AddComponent<ScrollRect>();
            var viewport = Stretch(scrollRt, "Viewport");
            Img(viewport, new Color(1, 1, 1, 0.01f), true);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f); content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            var hl = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(14, 14, 10, 10); hl.spacing = 16;
            hl.childControlWidth = hl.childControlHeight = true; hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var keys = Txt(content, "Keys", "", 22, TextAnchor.UpperLeft, Dim, 0, 0, 250, 10);
            var vals = Txt(content, "Values", "", 22, TextAnchor.UpperLeft, TextCol, 0, 0, 420, 10);
            keys.gameObject.AddComponent<LayoutElement>().preferredWidth = 250;
            vals.gameObject.AddComponent<LayoutElement>().preferredWidth = 420;
            keys.lineSpacing = vals.lineSpacing = 1.05f;
            sr.content = content; sr.viewport = viewport; sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 30f;
            var table = cgo.AddComponent<DataTableUI>();
            table.hub = hub; table.deploy = deploy; table.keysText = keys; table.valuesText = vals; table.refreshHz = 2f; table.showRaw = true;

            // Timeline: clickable blue anomaly markers above the slider (tall row = easy laser target)
            var markers = Box(rt, "Anomaly Markers (click to jump)", 36, 800, 688, 44);
            Img(markers, new Color(0.02f, 0.02f, 0.03f, 1f), false);
            var sliderGo = DefaultControls.CreateSlider(new DefaultControls.Resources());
            sliderGo.name = "Timeline";
            var srt = (RectTransform)sliderGo.transform;
            srt.SetParent(rt, false);
            srt.anchorMin = srt.anchorMax = new Vector2(0f, 1f); srt.pivot = new Vector2(0f, 1f);
            srt.anchoredPosition = new Vector2(36, -846); srt.sizeDelta = new Vector2(688, 40);
            var play = Btn(rt, "Button Play", "PLAY", 36, 906, 210, 70, Accent, 28);
            var speed = Btn(rt, "Button Speed", "SPEED", 270, 906, 210, 70, Idle, 26);
            var live = Btn(rt, "Button Live", "LIVE", 504, 906, 220, 70, new Color(0.15f, 0.6f, 0.3f, 1f), 28);
            QuickActionsAPI.AddRayCanvasInteraction(cgo);
            QuickActionsAPI.AddPokeCanvasInteraction(cgo);

            var ctrlGo = new GameObject("Replay Controller");
            ctrlGo.transform.SetParent(systems, false);
            var ctrl = ctrlGo.AddComponent<ReplayController>();
            ctrl.hub = hub; ctrl.panel = root; ctrl.timeline = sliderGo.GetComponent<Slider>(); ctrl.markerRow = markers;
            ctrl.timeText = timeText; ctrl.playLabel = play.GetComponentInChildren<Text>();
            UnityEventTools.AddPersistentListener(play.onClick, ctrl.TogglePlay);
            UnityEventTools.AddPersistentListener(speed.onClick, ctrl.NextSpeed);
            UnityEventTools.AddPersistentListener(live.onClick, ctrl.GoLive);
            root.SetActive(false);
            return ctrl;
        }

        // ---------------- VR scene: translucent Earth, colour-coded ground track, Earth vector, axes toggle ----------------
        static void VrSceneExtras(EarthController earth, SpacecraftController sc, GameObject spacecraftGo, List<string> log)
        {
            // Sun a little brighter and bigger so it is easy to find (Look at Sun / Earth-up views) without washing out the scene.
            var sunR = earth && earth.sunVisual ? earth.sunVisual.GetComponent<Renderer>() : null;
            if (sunR && sunR.sharedMaterial)
            {
                const string sunPath = "Assets/EOSAT/Materials/COMBINED_SunVR_Bright.mat";
                const float glow = 1.5f, grow = 1.3f;
                if (AssetDatabase.GetAssetPath(sunR.sharedMaterial) != sunPath)   // never brighten twice
                {
                    var m = AssetDatabase.LoadAssetAtPath<Material>(sunPath);
                    if (!m) { m = new Material(sunR.sharedMaterial); AssetDatabase.CreateAsset(m, sunPath); }
                    else m.CopyPropertiesFromMaterial(sunR.sharedMaterial);
                    foreach (var prop in new[] { "_BaseColor", "_Color", "_EmissionColor" })
                        if (m.HasProperty(prop)) { var c = m.GetColor(prop); m.SetColor(prop, new Color(c.r * glow, c.g * glow, c.b * glow, c.a)); }
                    EditorUtility.SetDirty(m);
                    sunR.sharedMaterial = m;
                    earth.sunVisual.localScale *= grow;
                    log.Add($"VR Sun: colour / glow x{glow}, size x{grow}");
                }
            }

            if (earth && earth.earthBody)
            {
                var r = earth.earthBody.GetComponent<Renderer>();
                if (r && r.sharedMaterial && r.sharedMaterial.HasProperty("_Alpha"))
                {
                    const string path = "Assets/EOSAT/Materials/COMBINED_EarthVR_Translucent.mat";
                    var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (!m) { m = new Material(r.sharedMaterial); AssetDatabase.CreateAsset(m, path); }
                    else m.CopyPropertiesFromMaterial(r.sharedMaterial);
                    m.SetFloat("_Alpha", 0.8f);
                    EditorUtility.SetDirty(m);
                    r.sharedMaterial = m;
                    log.Add("VR Earth: translucent copy of its material (alpha 0.8); MR globe stays solid");
                }
                var gt = new GameObject("Ground Track (next orbit, colour-coded)");
                gt.transform.SetParent(earth.earthBody, false);
                var lr = gt.AddComponent<LineRenderer>();
                lr.widthMultiplier = 0.035f; lr.numCapVertices = 2;
                lr.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                gt.AddComponent<GroundTrackOverlay>().spacecraft = sc;
                log.Add("VR ground track: next orbit on the Earth, yellow sunlit / violet eclipse / cyan station pass");
            }
            if (!spacecraftGo) return;
            var sunVec = spacecraftGo.GetComponentInChildren<SunVector>(true);
            if (sunVec)
            {
                var ev = new GameObject("Earth Vector (to Earth centre)");
                ev.transform.SetParent(sunVec.transform, false);
                var lr = ev.AddComponent<LineRenderer>();
                lr.widthMultiplier = 0.005f; lr.numCapVertices = 2;
                lr.sharedMaterial = MakeTransparent("COMBINED_EarthVector", new Color(0.3f, 1f, 0.6f, 1f));
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                sunVec.earthLine = lr;
                EditorUtility.SetDirty(sunVec);
                log.Add("VR: Earth-centre vector (green) next to the Sun vector (yellow)");
            }
            foreach (var t in spacecraftGo.GetComponentsInChildren<Transform>(true))
                if (t.name == "Body Axes" && !t.GetComponent<AxesToggle>()) t.gameObject.AddComponent<AxesToggle>();
        }

        // ---------------- Controller tilt guide (right controller) ----------------
        static void BuildControllerGuide(Transform rightHand, Transform head)
        {
            if (!rightHand) { Debug.LogWarning("[EOSAT Combined] RightHandAnchor not found - no controller guide"); return; }
            var go = new GameObject("Controller Guide (tilt down to show)");
            go.transform.SetParent(rightHand, false);
            go.AddComponent<CanvasGroup>();
            var cgo = new GameObject("Canvas", typeof(RectTransform));
            cgo.transform.SetParent(go.transform, false);
            var canvas = cgo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            var scaler = cgo.AddComponent<CanvasScaler>(); scaler.dynamicPixelsPerUnit = 2f;
            var rt = (RectTransform)cgo.transform; rt.sizeDelta = new Vector2(640, 300); rt.localScale = Vector3.one * 0.00035f;   // 22 x 10.5 cm
            Img(Stretch(rt, "Background"), new Color(0.05f, 0.06f, 0.08f, 0.9f), false);
            var text = Txt(rt, "Guide", "", 26, TextAnchor.UpperLeft, TextCol, 22, 16, 600, 270);
            text.lineSpacing = 1.2f;
            var guide = go.AddComponent<ControllerAnnotationGuide>();
            SetProps(guide, ("controller", rightHand), ("head", head), ("guideText", text));
        }

        // ---------------- Localization: LocalizedText on every Text no script writes to ----------------
        static int LocalizeStaticTexts()
        {
            var written = new HashSet<Object>();
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!mb || mb is Graphic) continue;
                var it = new SerializedObject(mb).GetIterator();
                while (it.Next(true))
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue is Text t) written.Add(t);
            }
            int n = 0;
            foreach (var t in Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (written.Contains(t) || string.IsNullOrEmpty(t.text) || t.GetComponent<LocalizedText>()
                    || t.GetComponentInParent<InputField>(true) || t.GetComponentInParent<Dropdown>(true)) continue;
                t.gameObject.AddComponent<LocalizedText>();
                n++;
            }
            return n;
        }

        static Dropdown SourceDropdown(RectTransform parent, float x, float y, float w, float h)
        {
            var go = DefaultControls.CreateDropdown(new DefaultControls.Resources());
            go.name = "Source Drop-down";
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
            var dd = go.GetComponent<Dropdown>();
            dd.options = new List<Dropdown.OptionData> { new("SIMULATION (local)"), new("TCP  8100"), new("WEBSOCKET  8101") };
            dd.SetValueWithoutNotify(0);
            ((RectTransform)dd.template).sizeDelta = new Vector2(0f, h * 3.4f);       // room for all three options
            var item = dd.template.GetComponentInChildren<Toggle>(true);
            if (item) { ((RectTransform)item.transform).sizeDelta = new Vector2(0f, h); ((RectTransform)item.transform.parent).sizeDelta = new Vector2(0f, h); }
            foreach (var txt in go.GetComponentsInChildren<Text>(true)) { txt.font = UiFont; txt.fontSize = 26; txt.color = Color.black; }
            dd.RefreshShownValue();
            return dd;
        }

        // =====================================================================================
        // Small UI helpers (legacy uGUI, pixel coordinates from the top-left corner)
        // =====================================================================================
        static Font UiFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        static RectTransform Box(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        static RectTransform Center(Transform parent, string name, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        static RectTransform Stretch(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static Image Img(RectTransform rt, Color c, bool raycast)
        {
            var i = rt.gameObject.AddComponent<Image>(); i.color = c; i.raycastTarget = raycast; return i;
        }

        static Text Txt(Transform parent, string name, string text, int size, TextAnchor align, Color c, float x, float y, float w, float h)
        {
            var rt = Box(parent, name, x, y, w, h);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = UiFont; t.fontSize = size; t.alignment = align; t.color = c; t.text = text;
            t.supportRichText = true; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        static Button Btn(Transform parent, string name, string label, float x, float y, float w, float h, Color bg, int fontSize)
        {
            var rt = Box(parent, name, x, y, w, h);
            var img = Img(rt, bg, true);
            var b = rt.gameObject.AddComponent<Button>(); b.targetGraphic = img;
            var colors = b.colors; colors.highlightedColor = new Color(0.85f, 0.93f, 1f, 1f); colors.pressedColor = new Color(0.6f, 0.75f, 0.9f, 1f); colors.fadeDuration = 0.08f; b.colors = colors;
            var t = Txt(rt, "Label", "<b>" + label + "</b>", fontSize, TextAnchor.MiddleCenter, TextCol, 0, 0, w, h);
            return b;
        }

        static void SetProps(Object target, params (string name, object value)[] props)
        {
            var so = new SerializedObject(target);
            foreach (var (name, value) in props)
            {
                var p = so.FindProperty(name);
                if (p == null) { Debug.LogWarning($"[EOSAT Combined] {target.GetType().Name} has no property '{name}'"); continue; }
                switch (value)
                {
                    case float f: p.floatValue = f; break;
                    case bool b: p.boolValue = b; break;
                    case int i: p.intValue = i; break;
                    case Object o: p.objectReferenceValue = o; break;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Material MakeLit(string name, Color c, float metal, float smooth)
        {
            string path = $"Assets/EOSAT/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material MakeTransparent(string name, Color c)
        {
            string path = $"Assets/EOSAT/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find("Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
