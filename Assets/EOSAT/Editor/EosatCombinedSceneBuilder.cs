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
    ///       Screen - Connection / Flight Map / Telemetry Data   (grab bar, laser grab, SnapInteractor)
    ///       Layout Slot 1..5          (SnapInteractable)
    ///       VR Console                (control guide + camera-target button, VR only)
    ///   Environment_MR                White Platform, Globe Pivot (big Earth at eye level, satellite on its orbit)
    ///   Environment_VR                VR Earth, Spacecraft, Sun
    /// </summary>
    public static class EosatCombinedSceneBuilder
    {
        const string MrPath = "Assets/Scenes/EOSAT_MR_Tabletop.unity";
        const string VrPath = "Assets/Scenes/EOSAT_Visualization.unity";
        public const string OutPath = "Assets/Scenes/EOSAT_Combined.unity";

        // Screen size: 1000 x 640 px at 0.9 mm per px = 0.90 x 0.58 m.
        const float ScreenW = 1000f, ScreenH = 640f, Px = 0.0009f;
        const float GlobeScale = 2.5f;          // Earth radius 0.12 m -> 0.30 m (60 cm across)
        const float EarthDistance = 1.2f;       // metres in front of the eyes
        const float ScreenRadius = 1.5f;        // screens / slots sit on an arc this far from the user

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
            var zoom = globe.GetComponent<TabletopZoom>(); if (!zoom) zoom = globe.AddComponent<TabletopZoom>();
            SetProps(zoom, ("minZoom", 0.4f), ("maxZoom", 2.5f));
            foreach (var icon in globe.GetComponentsInChildren<ReticleDataIcon>(true)) Object.DestroyImmediate(icon);
            // Laser / distance grab: back to each interaction's own ISDK provider (object rides rigidly on the ray).
            foreach (var mp in globe.GetComponents<MoveAtSourceProvider>()) Object.DestroyImmediate(mp);
            foreach (var ri in globe.GetComponentsInChildren<RayInteractable>(true)) { var p = ri.GetComponent<IMovementProvider>(); if (p != null) { ri.InjectOptionalMovementProvider(p); EditorUtility.SetDirty(ri); } }
            foreach (var dg in globe.GetComponentsInChildren<DistanceGrabInteractable>(true)) { var p = dg.GetComponent<IMovementProvider>(); if (p != null) { dg.InjectOptionalMovementProvider(p); EditorUtility.SetDirty(dg); } }
            foreach (var dh in globe.GetComponentsInChildren<Oculus.Interaction.HandGrab.DistanceHandGrabInteractable>(true)) { var p = dh.GetComponent<IMovementProvider>(); if (p != null) { dh.InjectOptionalMovementProvider(p); EditorUtility.SetDirty(dh); } }

            var mrSc = globe.GetComponentsInChildren<SpacecraftController>(true).First();
            var vrSc = vrSpacecraftGo.GetComponent<SpacecraftController>();
            var overlay = globe.GetComponentInChildren<TelemetryOverlay>(true);

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
            if (overlay) { overlay.deploy = deploy; EditorUtility.SetDirty(overlay); }
            log.Add("Deployables: one control drives MR model" + (vrAnim ? " + VR model" : "") + "; manual toggles animate in 4 s");

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
            var slotDefs = new (float angle, float up, float tilt)[] { (-84f, 0f, 0f), (-42f, 0f, 0f), (42f, 0f, 0f), (84f, 0f, 0f), (0f, 0.78f, -20f) };
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
            var s2 = BuildScreen("Screen - Flight Map", layout.transform, slots[2], barMat, out var c2);
            BuildFlightMapUI(c2, hub, mrSc, vrSc);
            AddOverlay(c2, hub, mrSc, 0.8f);
            // Screen 3 - Telemetry Data
            var s3 = BuildScreen("Screen - Telemetry Data", layout.transform, slots[3], barMat, out var c3);
            var tabletUi = BuildTelemetryUI(c3, hub, overlay, deploy);
            AddOverlay(c3, hub, mrSc, 0.8f);
            screens.AddRange(new[] { s1, s2, s3 });
            foreach (var c in new[] { c1, c2, c3 })
            {
                QuickActionsAPI.AddRayCanvasInteraction(c.gameObject);
                QuickActionsAPI.AddPokeCanvasInteraction(c.gameObject);
            }
            // Slot outlines appear only while a screen is being dragged.
            var hints = layout.AddComponent<LayoutSlotHints>();
            hints.screens = screens.Select(s => s.transform).ToArray();
            hints.outlines = slots.Select(s => (Renderer)s.GetComponent<LineRenderer>()).ToArray();
            log.Add("Screens: Connection / Flight Map / Telemetry Data, each 0.90 x 0.58 m with grab bar, laser grab, SnapInteractor; " + slots.Count + " layout slots");

            // ---------- VR orbit ----------
            var orbit = orbitRig.AddComponent<OrbitalViewerLocomotion>();
            SetProps(orbit, ("earthCenter", eci.transform), ("satellite", vrSpacecraftGo.transform), ("head", centerT));
            orbit.enabled = false;

            // VR console: static control guide + camera-target button (VR only).
            var console = BuildConsole(layout.transform, orbit);
            QuickActionsAPI.AddRayCanvasInteraction(console.gameObject);
            QuickActionsAPI.AddPokeCanvasInteraction(console.gameObject);
            console.gameObject.SetActive(false);

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
            sso.FindProperty("tabletopZoom").objectReferenceValue = zoom;
            sso.FindProperty("centerEyeCamera").objectReferenceValue = cam;
            sso.FindProperty("passthroughLayer").objectReferenceValue = passLayer;
            // No skybox in VR: the procedural one drew its own sun disc (the "second sun") and a grey horizon. Space is black.
            sso.FindProperty("vrSkybox").objectReferenceValue = null;
            var vrOnly = sso.FindProperty("vrOnlyObjects"); vrOnly.arraySize = 1; vrOnly.GetArrayElementAtIndex(0).objectReferenceValue = console.gameObject;
            sso.ApplyModifiedPropertiesWithoutUndo();
            tabletUi.switcher = sw; EditorUtility.SetDirty(tabletUi);
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

        static GameObject BuildScreen(string name, Transform parent, SnapInteractable startSlot, Material barMat, out Canvas canvas)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = startSlot.transform.localPosition;
            root.transform.localRotation = startSlot.transform.localRotation;
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
            crt.sizeDelta = new Vector2(ScreenW, ScreenH);
            crt.localScale = Vector3.one * Px;
            var bg = Stretch(crt, "Background"); Img(bg, PanelBg, true);

            // Grab bar: transparent pill under the screen (like Meta Home windows). The ONLY collider of the screen.
            float halfH = ScreenH * Px * 0.5f;
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
            QuickActionsAPI.AddGrabInteraction(root);
            QuickActionsAPI.AddRayGrabInteraction(root);
            var grabbable = root.GetComponent<Grabbable>();
            if (grabbable)
            {
                grabbable.InjectOptionalKinematicWhileSelected(true);     // hovers where released
                grabbable.InjectOptionalThrowWhenUnselected(false);
                EditorUtility.SetDirty(grabbable);
            }

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
            var msg = Txt(rt, "Message", "", 32, TextAnchor.MiddleCenter, TextCol, 40, 120, ScreenW - 80, ScreenH - 240);
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

            Txt(root, "IP Label", "Simulator IP address  (type, or use the keypad)", 24, TextAnchor.MiddleLeft, Dim, 36, 290, 560, 34);
            // Input field
            var fieldRt = Box(root, "IP Input Field", 36, 328, 560, 76);
            var fieldImg = Img(fieldRt, new Color(0.02f, 0.02f, 0.03f, 1f), true);
            var field = fieldRt.gameObject.AddComponent<InputField>();
            var fText = Txt(fieldRt, "Text", "", 40, TextAnchor.MiddleLeft, TextCol, 18, 8, 524, 60); fText.supportRichText = false;
            var ph = Txt(fieldRt, "Placeholder", "e.g. 192.168.1.20", 34, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.3f), 18, 8, 524, 60);
            field.targetGraphic = fieldImg; field.textComponent = fText; field.placeholder = ph; field.characterLimit = 21;
            field.text = hub ? hub.host : "127.0.0.1";

            var set = Btn(root, "Button SET IP", "SET IP", 36, 420, 180, 76, Accent, 30);
            var local = Btn(root, "Button Localhost", "127.0.0.1", 228, 420, 190, 76, Idle, 28);
            var clear = Btn(root, "Button Clear", "CLEAR", 430, 420, 166, 76, Idle, 28);
            var hostStatus = Txt(root, "IP Status", "TCP and WebSocket both use this address.", 22, TextAnchor.UpperLeft, Dim, 36, 510, 560, 100);

            var conn = canvas.gameObject.AddComponent<ConnectionUIManager>();
            conn.hub = hub; conn.simulatedButton = sim; conn.tcpButton = tcp; conn.webSocketButton = ws;
            conn.statusText = status; conn.detailText = detail; conn.hostField = field; conn.hostStatus = hostStatus;
            conn.idleColor = Idle; conn.activeColor = Accent;
            UnityEventTools.AddPersistentListener(set.onClick, conn.ApplyCustomHost);
            UnityEventTools.AddPersistentListener(local.onClick, conn.UseLocalhost);
            UnityEventTools.AddPersistentListener(clear.onClick, conn.KeypadClear);

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
            var root = (RectTransform)canvas.transform;
            Txt(root, "Title", "<b>FLIGHT MAP</b>  <size=24><color=#9AA6B8>ground track</color></size>", 44, TextAnchor.MiddleLeft, TextCol, 36, 14, 800, 56);
            var mapRt = Box(root, "Map (equirectangular)", 30, 78, 940, 470);
            var raw = mapRt.gameObject.AddComponent<RawImage>();
            raw.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/EOSAT/Textures/earth_atmos_2048.jpg");
            raw.color = new Color(0.85f, 0.85f, 0.85f, 1f); raw.raycastTarget = false;
            // Graticule: equator + prime meridian
            Img(Center(mapRt, "Equator", 940, 2), new Color(1, 1, 1, 0.25f), false);
            Img(Center(mapRt, "Prime Meridian", 2, 470), new Color(1, 1, 1, 0.25f), false);

            var dots = new List<Image>();
            var dotsRoot = Center(mapRt, "Ground Track", 0, 0);
            for (int i = 0; i < 110; i++) { var d = Img(Center(dotsRoot, "dot", 7, 7), new Color(0.3f, 0.85f, 1f, 0.9f), false); d.enabled = false; dots.Add(d); }
            var marker = Center(mapRt, "Satellite Marker", 22, 22);
            var mImg = Img(marker, new Color(1f, 0.85f, 0.2f, 1f), false);
            Img(Center(marker, "Core", 8, 8), new Color(0.1f, 0.1f, 0.1f, 1f), false);
            Txt(marker, "Name", "EOSAT-1", 22, TextAnchor.MiddleLeft, TextCol, 28, -4, 160, 30);
            var readout = Txt(root, "Readout", "", 26, TextAnchor.MiddleLeft, TextCol, 36, 560, 930, 60);

            var map = canvas.gameObject.AddComponent<FlightMapUI>();
            map.spacecraft = mrSc; map.alternate = vrSc; map.hub = hub; map.mapRect = mapRt; map.marker = marker; map.markerImage = mImg;
            map.readout = readout; map.trackDots = dots;
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

            var ui = canvas.gameObject.AddComponent<TabletUIController>();
            ui.telemetry = overlay; ui.satellite = deploy;
            ui.dataToggle = Row(root, "Row Data Overlay", "Data Overlay", 96, out ui.dataStatus);
            ui.antennaToggle = Row(root, "Row Deploy Antenna", "Deploy Antenna", 226, out ui.antennaStatus);
            ui.panelsToggle = Row(root, "Row Deploy Solar Panels", "Deploy Solar Panels", 356, out ui.panelsStatus);
            ui.arVrToggle = Row(root, "Row MR / VR View", "MR / VR View", 486, out ui.arVrStatus);
            return ui;
        }

        static Toggle Row(RectTransform root, string name, string label, float y, out Text status)
        {
            var rt = Box(root, name, 590, y, 380, 118);
            var bg = Img(rt, BoxBg, true);
            var toggle = rt.gameObject.AddComponent<Toggle>();
            Txt(rt, "Label", "<b>" + label + "</b>", 25, TextAnchor.MiddleLeft, TextCol, 20, 14, 256, 40);
            status = Txt(rt, "Status", "", 20, TextAnchor.MiddleLeft, new Color(0.3f, 0.78f, 1f, 1f), 20, 62, 340, 40);
            var sw = Box(rt, "Switch", 280, 20, 80, 36); Img(sw, Idle, false);
            var on = Stretch(sw, "On"); var onImg = Img(on, Accent, false);
            toggle.targetGraphic = bg; toggle.graphic = onImg; toggle.isOn = false;
            return toggle;
        }

        // ---------------- VR console: control guide + camera target ----------------
        static Canvas BuildConsole(Transform layout, OrbitalViewerLocomotion orbit)
        {
            var cgo = new GameObject("VR Console (control guide + camera target)", typeof(RectTransform));
            cgo.transform.SetParent(layout, false);
            cgo.transform.localPosition = new Vector3(0f, -0.52f, 0.85f);        // below the Earth view, like a dashboard
            cgo.transform.localRotation = Quaternion.Euler(38f, 0f, 0f);         // tilted up toward the eyes
            var canvas = cgo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            var scaler = cgo.AddComponent<CanvasScaler>(); scaler.dynamicPixelsPerUnit = 2f;
            cgo.AddComponent<GraphicRaycaster>();
            var rt = (RectTransform)cgo.transform; rt.sizeDelta = new Vector2(1000, 500); rt.localScale = Vector3.one * 0.0008f;   // 0.80 x 0.40 m
            Img(Stretch(rt, "Background"), PanelBg, true);
            Txt(rt, "Title", "<b>VR CONTROLS</b>", 40, TextAnchor.MiddleLeft, TextCol, 36, 16, 500, 54);
            Txt(rt, "Guide",
                "<color=#4DC8FF>RIGHT STICK</color>   orbit around the target - any direction, full 360°\n" +
                "<color=#4DC8FF>LEFT STICK</color>   push up / down to zoom in / out\n" +
                "<color=#4DC8FF>A  /  X</color>   recentre the view on the target\n" +
                "<color=#4DC8FF>B  /  Y</color>   switch between MR tabletop and VR orbit\n" +
                "<color=#4DC8FF>TRIGGER</color>   point the white laser at a screen's grab bar to move it",
                27, TextAnchor.UpperLeft, TextCol, 36, 84, 930, 250).lineSpacing = 1.3f;
            var b = Btn(rt, "Button Camera Target", "", 36, 352, 928, 124, new Color(0.2f, 0.55f, 1f, 1f), 32);
            var t = cgo.AddComponent<OrbitTargetToggleUI>();
            t.orbit = orbit; t.button = b; t.label = b.GetComponentInChildren<Text>(); t.background = b.GetComponent<Image>();
            return canvas;
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
