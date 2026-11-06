using System.Collections.Generic;
using System.Linq;
using Eosat.Meta;
using Eosat.MR;
using Oculus.Interaction;
using Oculus.Interaction.Editor.QuickActions;
using Oculus.Interaction.HandGrab;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Eosat.EditorTools
{
    /// <summary>
    /// EOSAT > Build MR Tabletop Scene (Meta SDK): builds Assets/Scenes/EOSAT_MR_Tabletop.unity on Meta's stack:
    ///  - OVRCameraRig + OVRPassthroughLayer (Underlay) + transparent CenterEyeAnchor  -> colour passthrough
    ///  - Interaction SDK comprehensive rig (hands + controllers: poke, ray, grab, distance grab)
    ///  - floating globe: ISDK grab + distance grab, hovers where released
    ///  - floating tablet: ISDK grab + distance grab + ray grab with ObjectPullProvider (push/pull),
    ///    UI via PointableCanvas (ray + poke) and PointableCanvasModule; summoned by WristSummonUI
    ///  - PassthroughToggle (thumbstick click or the tablet's AR/VR switch)
    /// Content (globe, orbit, telemetry, tablet UI) comes from EosatMrSceneBuilder; only the rig and
    /// interaction layer are swapped from Unity XRI / AR Foundation to Meta.
    /// </summary>
    public static class EosatMetaMrSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/EOSAT_MR_Tabletop.unity";
        const string OvrCameraRigGuid = "126d619cf4daa52469682f85c1378b4a";      // OVRCameraRig.prefab (Meta XR Core SDK)
        const string RigWizardType = "Oculus.Interaction.OVR.Editor.QuickActions.OVRComprehensiveInteractionRigWizard, Oculus.Interaction.OVR.Editor";

        [MenuItem("EOSAT/Build MR Tabletop Scene (Meta SDK)", priority = 0)]
        public static void BuildMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Debug.Log("[EOSAT] " + Build());
        }

        public static string Build()
        {
            var log = new List<string>();

            // 1) Content from the existing builder (globe, orbit, telemetry, overlay, tablet canvas, desktop fallback).
            EosatMrSceneBuilder.Build();
            var scene = EditorSceneManager.GetActiveScene();

            // 2) Remove the Unity XRI / AR Foundation layer.
            foreach (var name in new[] { "XR Origin (MR, hands + controllers)", "AR Session", "Tabletop Placer", "XR Interaction Manager" })
                DestroyAll(name);
            foreach (var m in Object.FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.XRInteractionManager>(FindObjectsInactive.Include))
                Object.DestroyImmediate(m.gameObject);

            var eventSystem = Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
            foreach (var mod in eventSystem.GetComponents<BaseInputModule>()) Object.DestroyImmediate(mod);

            // 3) Meta camera rig with passthrough.
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(OvrCameraRigGuid));
            var rigGo = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, scene);
            rigGo.name = "OVRCameraRig (passthrough)";
            var rig = rigGo.GetComponent<OVRCameraRig>();
            var ovrManager = rigGo.GetComponent<OVRManager>();
            var originField = typeof(OVRManager).GetField("_trackingOriginType",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            originField?.SetValue(ovrManager, OVRManager.TrackingOrigin.FloorLevel);
            ovrManager.isInsightPassthroughEnabled = true;                       // Insight Passthrough > Enable Passthrough
            ovrManager.controllerDrivenHandPosesType = OVRManager.ControllerDrivenHandPosesType.ConformingToController;
            EditorUtility.SetDirty(ovrManager);

            var passthrough = rigGo.AddComponent<OVRPassthroughLayer>();
            // SDK v207: OVRPassthroughLayer always composites as an Underlay (behind all virtual content).
            passthrough.textureOpacity = 1f;

            var cam = rig.centerEyeAnchor.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);                     // alpha 0 = see the passthrough
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;
            cam.allowHDR = false;                                                 // HDR target has no alpha
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = false;
            camData.antialiasing = AntialiasingMode.None;                        // MSAA from the URP asset is cheaper on Quest
            log.Add("OVRCameraRig + OVRPassthroughLayer (Underlay) + transparent CenterEyeAnchor");

            // Project-wide Meta feature flags (Quest Features > General).
            var config = OVRProjectConfig.CachedProjectConfig;
            config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;
            config.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
            OVRProjectConfig.CommitProjectConfig(config);

            // 4) Interaction SDK comprehensive rig (hands, controllers, poke, ray, grab, distance grab).
            var wizardType = System.Type.GetType(RigWizardType);
            if (wizardType == null) throw new System.Exception("OVRComprehensiveInteractionRigWizard not found");
            var wizard = ScriptableObject.CreateInstance(wizardType);
            wizardType.GetMethod("InjectCameraRig").Invoke(wizard, new object[] { rig });
            wizardType.GetMethod("InjectOptionalGenerateAsEditableCopy")?.Invoke(wizard, new object[] { false });
            var interactionRig = (GameObject)wizardType.GetMethod("CreateInteractionRig",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                .Invoke(wizard, null);
            Object.DestroyImmediate(wizard);
            log.Add("ISDK rig: " + interactionRig.name);

            // MR = you walk around the real room: turn off teleport / snap-turn / slide so the virtual
            // globe never shifts relative to the passthrough room.
            int locoOff = 0;
            foreach (var t in interactionRig.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Locomotor" || t.name == "LocomotionControllerInteractorGroup" ||
                    t.name == "MicroGesturesLocomotionHandInteractorGroup")
                { t.gameObject.SetActive(false); locoOff++; }
            }
            log.Add($"Locomotion off ({locoOff} groups) - walk around the globe instead");

            // 5) Floating globe: swap XRI grab for ISDK grab + distance grab.
            var globe = GameObject.Find("Globe Pivot");
            var xriTransformer = globe.GetComponent<UnityEngine.XR.Interaction.Toolkit.Transformers.XRGeneralGrabTransformer>();
            if (xriTransformer) Object.DestroyImmediate(xriTransformer);
            var xriGrab = globe.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            if (xriGrab) Object.DestroyImmediate(xriGrab);
            QuickActionsAPI.AddGrabInteraction(globe);
            QuickActionsAPI.AddDistanceGrabInteraction(globe, DistanceGrabMode.AnchorAtHand);   // grab relative to hand: 6 DoF from afar
            ConfigureHover(globe.GetComponent<Grabbable>());
            var globePlacer = globe.AddComponent<PlaceInFrontOnStart>();
            globePlacer.head = rig.centerEyeAnchor;
            log.Add("Globe: ISDK grab + distance grab (hovers on release)");

            // 6) Floating tablet with push/pull distance grab.
            var tabletRoot = GameObject.Find("Hand Tablet");
            tabletRoot.name = "Floating Tablet";
            var oldHandTablet = tabletRoot.GetComponent<HandTablet>();
            if (oldHandTablet) Object.DestroyImmediate(oldHandTablet);
            var canvasGo = tabletRoot.GetComponentInChildren<Canvas>(true).gameObject;
            canvasGo.SetActive(true);
            var tdgr = canvasGo.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();
            if (tdgr) Object.DestroyImmediate(tdgr);
            var canvasRt = (RectTransform)canvasGo.transform;
            Vector2 size = canvasRt.sizeDelta * canvasRt.localScale.x;          // metres (~0.27 x 0.155)
            tabletRoot.transform.SetPositionAndRotation(new Vector3(-0.25f, 1.2f, 0.55f), Quaternion.identity);

            var tabletBody = tabletRoot.AddComponent<Rigidbody>();
            tabletBody.isKinematic = true;                                       // zero gravity: never falls or drifts
            tabletBody.useGravity = false;
            tabletBody.interpolation = RigidbodyInterpolation.Interpolate;

            var frameMat = MakeLit("META_TabletFrame", new Color(0.12f, 0.13f, 0.15f), 0.3f, 0.5f);
            var handleMat = MakeLit("META_TabletHandle", new Color(0.55f, 0.6f, 0.68f), 0.6f, 0.6f);
            var frame = Box("Frame (visual)", tabletRoot.transform, new Vector3(0f, 0f, 0.006f),
                new Vector3(size.x + 0.016f, size.y + 0.016f, 0.008f), frameMat, false);
            var handle = Box("Grab Handle (pinch here)", tabletRoot.transform, new Vector3(0f, -size.y * 0.5f - 0.026f, 0.004f),
                new Vector3(size.x * 0.6f, 0.022f, 0.016f), handleMat, true);
            Object.DestroyImmediate(frame.GetComponent<Collider>());

            // Grab components first (so the canvas surfaces added later aren't picked up as grab colliders).
            QuickActionsAPI.AddGrabInteraction(tabletRoot);                                          // near pinch/grip
            QuickActionsAPI.AddDistanceGrabInteraction(tabletRoot, DistanceGrabMode.PullToHand);     // far hand pinch
            QuickActionsAPI.AddRayGrabInteraction(tabletRoot);                                       // visible-ray pinch
            var pull = tabletRoot.AddComponent<ObjectPullProvider>();
            pull.Speed = 2.5f;                                // metres of travel per second per metre of arm movement
            pull.DeadZone = 0.03f;                            // ignore hand jitter under 3 cm
            int wired = 0;
            foreach (var dh in tabletRoot.GetComponentsInChildren<DistanceHandGrabInteractable>(true)) { dh.InjectOptionalMovementProvider(pull); EditorUtility.SetDirty(dh); wired++; }
            foreach (var dg in tabletRoot.GetComponentsInChildren<DistanceGrabInteractable>(true)) { dg.InjectOptionalMovementProvider(pull); EditorUtility.SetDirty(dg); wired++; }
            foreach (var ri in tabletRoot.GetComponentsInChildren<RayInteractable>(true)) { ri.InjectOptionalMovementProvider(pull); EditorUtility.SetDirty(ri); wired++; }
            var tabletGrabbable = tabletRoot.GetComponent<Grabbable>();
            ConfigureHover(tabletGrabbable);
            log.Add($"Tablet: grab + distance grab + ray grab, ObjectPullProvider on {wired} interactables");

            // The distance-grab Quick Action also adds a "time-out snap zone" that pulls the object back to its
            // start pose after release. We want it to hover exactly where it is let go, so remove those.
            int snaps = 0;
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
                if (go && go.name.StartsWith("ISDK_SnapZone")) { Object.DestroyImmediate(go); snaps++; }
            foreach (var root in new[] { globe, tabletRoot })
                foreach (var si in root.GetComponentsInChildren<SnapInteractor>(true)) { Object.DestroyImmediate(si); snaps++; }
            log.Add($"Removed {snaps} snap-back zones/interactors (objects hover where released)");

            // Canvas UI: ray + poke through PointableCanvas, PointableCanvasModule on the EventSystem.
            QuickActionsAPI.AddRayCanvasInteraction(canvasGo);
            QuickActionsAPI.AddPokeCanvasInteraction(canvasGo);
            var module = Object.FindAnyObjectByType<PointableCanvasModule>(FindObjectsInactive.Include);
            log.Add("Canvas: ray + poke, " + (module ? "PointableCanvasModule on " + module.gameObject.name : "NO PointableCanvasModule"));

            // 7) Passthrough toggle (thumbstick click) + tablet AR/VR switch.
            var ptGo = new GameObject("Passthrough Toggle (thumbstick click)");
            var pt = ptGo.AddComponent<PassthroughToggle>();
            var so = new SerializedObject(pt);
            so.FindProperty("passthroughLayer").objectReferenceValue = passthrough;
            so.FindProperty("centerEyeCamera").objectReferenceValue = cam;
            so.FindProperty("vrSkybox").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/EOSAT/Materials/MR_VR_SpaceSkybox.mat");
            so.ApplyModifiedPropertiesWithoutUndo();

            var ui = canvasGo.GetComponent<TabletUIController>();
            if (ui.arVrToggle)
            {
                var env = ui.arVrToggle.GetComponent<EnvironmentManager>();
                if (env) Object.DestroyImmediate(env);
            }
            ui.environment = null;
            ui.metaPassthrough = pt;
            EditorUtility.SetDirty(ui);

            // 8) Wrist "check your watch" summon + close button.
            var hand = FindLeftHand(interactionRig);
            var summonGo = new GameObject("Wrist Summon (check your watch)");
            var summon = summonGo.AddComponent<WristSummonUI>();
            so = new SerializedObject(summon);
            so.FindProperty("leftHand").objectReferenceValue = hand;
            so.FindProperty("cameraRig").objectReferenceValue = rig;
            so.FindProperty("tablet").objectReferenceValue = tabletRoot;
            so.FindProperty("tabletGrabbable").objectReferenceValue = tabletGrabbable;
            so.ApplyModifiedPropertiesWithoutUndo();
            log.Add("Wrist summon: left hand = " + (hand ? hand.name : "NOT FOUND"));

            var close = CloseButton(canvasRt);
            UnityEventTools.AddPersistentListener(close.onClick, summon.Dismiss);

            // 9) Desktop fallback (no headset): use the Meta camera, no AR managers / placer.
            var fallback = Object.FindAnyObjectByType<MrDesktopFallback>(FindObjectsInactive.Include);
            fallback.cam = cam;
            fallback.handTablet = null;
            fallback.placer = null;
            fallback.focus = globe.transform;                                   // desktop camera sits facing the globe
            fallback.tabletOffset = new Vector3(-0.15f, -0.08f, 0.36f);         // 27 cm tablet, lower-left of the view
            fallback.arComponents = new Behaviour[0];
            fallback.hideOnDesktop = tabletRoot;                                // frame/handle stay behind when the canvas moves to the camera
            EditorUtility.SetDirty(fallback);

            rigGo.transform.SetAsFirstSibling();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            foreach (var l in log) Debug.Log("[EOSAT Meta] " + l);
            return "Built " + ScenePath + " on Meta SDK: " + string.Join(" | ", log);
        }

        /// <summary>Zero-gravity hover: kinematic while held, no throw on release, so it stops dead where it is let go.</summary>
        static void ConfigureHover(Grabbable g)
        {
            if (!g) return;
            g.InjectOptionalKinematicWhileSelected(true);
            g.InjectOptionalThrowWhenUnselected(false);
            EditorUtility.SetDirty(g);
        }

        static Object FindLeftHand(GameObject interactionRig)
        {
            // ISDK Hand components: pick the tracked LEFT hand (not the controller-driven one).
            var hands = interactionRig.GetComponentsInChildren<Oculus.Interaction.Input.Hand>(true);
            foreach (var h in hands)
                Debug.Log($"[EOSAT Meta] ISDK Hand found: {GetPath(h.transform)}");
            var left = hands.Where(h => GetPath(h.transform).ToLowerInvariant().Contains("left")).ToList();
            var tracked = left.FirstOrDefault(h => !GetPath(h.transform).ToLowerInvariant().Contains("controller"));
            return tracked ? tracked : left.FirstOrDefault();
        }

        static string GetPath(Transform t) => t.parent ? GetPath(t.parent) + "/" + t.name : t.name;

        static void DestroyAll(string name)
        {
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
                if (go && go.name == name) Object.DestroyImmediate(go);
        }

        static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 scale, Material m, bool keepCollider)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = m;
            g.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            if (!keepCollider) Object.DestroyImmediate(g.GetComponent<Collider>());
            return g;
        }

        static Material MakeLit(string name, Color c, float metal, float smooth)
        {
            string path = $"Assets/EOSAT/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Metallic", metal);
            m.SetFloat("_Smoothness", smooth);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Button CloseButton(RectTransform canvas)
        {
            var go = new GameObject("Close (hide tablet)", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-30f, -28f);
            rt.sizeDelta = new Vector2(44f, 44f);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.55f, 0.18f, 0.18f, 1f);
            var b = go.AddComponent<Button>();
            b.targetGraphic = img;
            var label = new GameObject("X", typeof(RectTransform));
            label.transform.SetParent(rt, false);
            var lrt = (RectTransform)label.transform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            var t = label.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = "X"; t.fontSize = 24; t.fontStyle = FontStyle.Bold; t.alignment = TextAnchor.MiddleCenter; t.color = Color.white;
            t.raycastTarget = false;
            return b;
        }
    }
}
