using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Eosat.EditorTools
{
    /// <summary>
    /// Puts Unity's standard VR rig (XR Interaction Toolkit sample "XR Origin Hands (XR Rig)": tracked head,
    /// controller and Quest hand models, near-far rays, poke, grab) into EOSAT scenes, configured for them:
    /// locomotion off (orbit view / real walking), left hand can't press the wrist tablet it carries.
    /// </summary>
    public static class EosatXrRig
    {
        public const string PrefabPath =
            "Assets/Samples/XR Interaction Toolkit/3.7.0/Hands Interaction Demo/Prefabs/XR Origin Hands (XR Rig).prefab";

        public enum Mode { OrbitView, MixedReality }

        public static GameObject LoadPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab) return prefab;
            foreach (var guid in AssetDatabase.FindAssets("\"XR Origin Hands (XR Rig)\" t:Prefab"))
            {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab) return prefab;
            }
            throw new System.Exception("XR rig prefab not found. Import the XR Interaction Toolkit samples 'Starter Assets' and " +
                                       "'Hands Interaction Demo' and the XR Hands sample 'HandVisualizer' (Package Manager > Samples).");
        }

        public static XROrigin Create(string name, Mode mode)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(LoadPrefab());
            go.name = name;
            var origin = go.GetComponent<XROrigin>();

            // No artificial locomotion: the orbit camera owns the thumbsticks (zoom / snap turn),
            // and in MR you walk around the real desk.
            var loco = go.transform.Find("Locomotion");
            if (loco) loco.gameObject.SetActive(false);
            var cc = go.GetComponent<CharacterController>();
            if (cc) cc.enabled = false;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.name == "Teleport Interactor" || t.name == "Hand Teleport Interactor")
                    t.gameObject.SetActive(false);

            // The wrist tablet rides on the left hand: only the right hand / controller may press UI.
            foreach (var side in new[] { "Left Controller", "Left Hand" })
            {
                var s = FindDeep(go.transform, side);
                if (!s) continue;
                foreach (var p in s.GetComponentsInChildren<XRPokeInteractor>(true)) p.enableUIInteraction = false;
                foreach (var nf in s.GetComponentsInChildren<NearFarInteractor>(true)) nf.enableUIInteraction = false;
            }

            if (mode == Mode.OrbitView)
            {
                // Head pose relative to where you started, so the orbit camera's maths holds in VR and on desktop.
                origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
                origin.CameraYOffset = 0f;
            }
            else
            {
                origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor; // desk height test needs the floor
            }

            // The prefab stores a 1.36 m head height on Camera Offset / Main Camera. In VR the tracking origin
            // replaces it; without a headset it would stay and push the camera off its orbit -> zero it.
            if (origin.CameraFloorOffsetObject) origin.CameraFloorOffsetObject.transform.localPosition = Vector3.zero;
            if (origin.Camera)
            {
                origin.Camera.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                origin.Camera.gameObject.tag = "MainCamera";
            }
            return origin;
        }

        public static Camera ConfigureCamera(XROrigin origin, Color background, float near, float far, float fov, bool hdr)
        {
            var cam = origin.Camera;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.nearClipPlane = near;
            cam.farClipPlane = far;
            cam.fieldOfView = fov;
            cam.allowHDR = hdr;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            return cam;
        }

        public static void EnsureInteractionManager()
        {
            if (!Object.FindAnyObjectByType<XRInteractionManager>())
                new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        /// <summary>
        /// EOSAT > Use Unity VR Rig In Main Scene: swaps the main scene's custom "Camera Rig" for Unity's rig
        /// in place (keeps every other object and the orbit camera's settings) and saves the scene.
        /// </summary>
        [MenuItem("EOSAT/Use Unity VR Rig In Main Scene", priority = 43)]
        public static void UpgradeMainSceneMenu() => Debug.Log("[EOSAT] " + UpgradeMainScene());

        public static string UpgradeMainScene()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/EOSAT_Visualization.unity");
            var oldOrbit = Object.FindAnyObjectByType<OrbitCamera>(FindObjectsInactive.Include);
            if (!oldOrbit) return "No OrbitCamera in EOSAT_Visualization.";
            if (oldOrbit.GetComponent<XROrigin>()) return "Main scene already uses the Unity VR rig.";

            var oldRig = oldOrbit.gameObject;
            var oldCam = oldRig.GetComponentInChildren<Camera>(true);

            var origin = Create("XR Origin (orbit view)", Mode.OrbitView);
            origin.transform.SetPositionAndRotation(oldRig.transform.position, oldRig.transform.rotation);
            if (oldCam) ConfigureCamera(origin, oldCam.backgroundColor, oldCam.nearClipPlane, oldCam.farClipPlane, oldCam.fieldOfView, oldCam.allowHDR);

            var orbit = origin.gameObject.AddComponent<OrbitCamera>();
            EditorUtility.CopySerialized(oldOrbit, orbit);   // same targets, distances, speeds
            Object.DestroyImmediate(oldRig);
            EnsureInteractionManager();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"Main scene now uses '{origin.name}' (orbit camera on the rig root, locomotion off) and was saved.";
        }
    }
}
