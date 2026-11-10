using System.Collections.Generic;
using System.Linq;
using Eosat.Meta;
using Oculus.Interaction;
using Oculus.Interaction.Editor.QuickActions;
using Oculus.Interaction.HandGrab;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Eosat.EditorTools
{
    /// <summary>
    /// EOSAT > Meta > Apply Laser Drag + Camera Fixes To Open Scene
    ///  - Every root Grabbable (globe, tablet) gets ray grab (visible laser) + distance grab, all driven by
    ///    MoveAtSourceProvider: the object stays at its distance and moves like it is on the end of the laser,
    ///    so wrist/pointer movement drags it through space (replaces ObjectPullProvider = pull to hand).
    ///  - Camera hygiene: one MainCamera tag + one AudioListener on OVRCameraRig's CenterEyeAnchor.
    ///  - PassthroughToggle moved off the thumbstick click (thumbsticks now drive the platform) to B / Y.
    /// </summary>
    public static class EosatLaserDragSetup
    {
        [MenuItem("EOSAT/Meta/Apply Laser Drag + Camera Fixes To Open Scene")]
        public static void Menu() => Debug.Log("[EOSAT Laser]\n" + Apply());

        public static string Apply()
        {
            var log = new List<string>();
            var scene = EditorSceneManager.GetActiveScene();

            // 1) Laser drag on every top-level Grabbable.
            var grabbables = Object.FindObjectsByType<Grabbable>(FindObjectsInactive.Include)
                .Where(g => g.GetComponentsInParent<Grabbable>(true).Length == 1).ToList();
            foreach (var g in grabbables)
            {
                var root = g.gameObject;
                if (root.GetComponentsInChildren<RayInteractable>(true).Length == 0)
                {
                    QuickActionsAPI.AddRayGrabInteraction(root);
                    log.Add($"{root.name}: added ray grab (laser)");
                }

                var mover = root.GetComponent<MoveAtSourceProvider>();
                if (!mover) mover = root.AddComponent<MoveAtSourceProvider>();
                int wired = 0;
                foreach (var ri in root.GetComponentsInChildren<RayInteractable>(true)) { ri.InjectOptionalMovementProvider(mover); EditorUtility.SetDirty(ri); wired++; }
                foreach (var dh in root.GetComponentsInChildren<DistanceHandGrabInteractable>(true)) { dh.InjectOptionalMovementProvider(mover); EditorUtility.SetDirty(dh); wired++; }
                foreach (var dg in root.GetComponentsInChildren<DistanceGrabInteractable>(true)) { dg.InjectOptionalMovementProvider(mover); EditorUtility.SetDirty(dg); wired++; }
                foreach (var pull in root.GetComponentsInChildren<ObjectPullProvider>(true)) { Object.DestroyImmediate(pull); log.Add($"{root.name}: removed ObjectPullProvider (pull-to-hand)"); }

                g.InjectOptionalKinematicWhileSelected(true);     // hover where released, no physics drift
                g.InjectOptionalThrowWhenUnselected(false);
                EditorUtility.SetDirty(g);
                log.Add($"{root.name}: MoveAtSourceProvider on {wired} ray/distance interactables");
            }

            // Quick Actions can add snap-back zones; objects should stay where they are released.
            int snaps = 0;
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
                if (go && go.name.StartsWith("ISDK_SnapZone")) { Object.DestroyImmediate(go); snaps++; }
            foreach (var g in grabbables)
                foreach (var si in g.GetComponentsInChildren<SnapInteractor>(true)) { Object.DestroyImmediate(si); snaps++; }
            if (snaps > 0) log.Add($"Removed {snaps} snap-back zones");

            int rays = Object.FindObjectsByType<RayInteractor>(FindObjectsInactive.Include).Length;
            int visuals = Object.FindObjectsByType<ControllerRayVisual>(FindObjectsInactive.Include).Length;
            log.Add($"Rig: {rays} RayInteractors, {visuals} ControllerRayVisuals (laser beams)");

            // 2) Camera hygiene.
            var rig = Object.FindAnyObjectByType<OVRCameraRig>(FindObjectsInactive.Include);
            if (rig)
            {
                var center = rig.centerEyeAnchor ? rig.centerEyeAnchor : rig.transform.Find("TrackingSpace/CenterEyeAnchor");
                foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
                {
                    bool isCenter = center && cam.transform == center;
                    if (!isCenter && cam.CompareTag("MainCamera")) { cam.tag = "Untagged"; EditorUtility.SetDirty(cam.gameObject); log.Add($"Untagged MainCamera on {cam.name}"); }
                    if (isCenter && !cam.CompareTag("MainCamera")) { cam.tag = "MainCamera"; EditorUtility.SetDirty(cam.gameObject); }
                }
                foreach (var al in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include))
                    if (!(center && al.transform == center) && al.enabled) { al.enabled = false; EditorUtility.SetDirty(al); log.Add($"Disabled extra AudioListener on {al.name}"); }
                if (center && !center.GetComponent<AudioListener>()) center.gameObject.AddComponent<AudioListener>();
            }
            else log.Add("No OVRCameraRig in this scene (camera fixes skipped)");

            // 3) Passthrough toggle off the thumbstick click.
            foreach (var pt in Object.FindObjectsByType<PassthroughToggle>(FindObjectsInactive.Include))
            {
                var so = new SerializedObject(pt);
                so.FindProperty("toggleButton").intValue = (int)OVRInput.Button.Two;     // B
                so.FindProperty("alternateButton").intValue = (int)OVRInput.Button.Four; // Y
                so.ApplyModifiedPropertiesWithoutUndo();
                pt.gameObject.name = "Passthrough Toggle (B / Y)";
                log.Add("PassthroughToggle: thumbstick click -> B / Y");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return string.Join("\n", log);
        }
    }
}
