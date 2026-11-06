// WristSummonUI.cs  -  Meta Interaction SDK (IHand) + Meta XR Core SDK (OVRCameraRig)
// "Check your watch" gesture: turn the back of the LEFT wrist toward your eyes and look at it.
// After a short hold the floating tablet appears at a comfortable reading distance in front of you.
//  - Gesture again while the tablet is far away -> it comes back in front of you.
//  - Dismiss() (wire it to a close button on the tablet) hides it.
// Works with tracked hands (ISDK IHand wrist pose) and falls back to the left Touch controller.
//
// Put it on an always-active object (NOT on the tablet - a disabled object stops updating).

using Oculus.Interaction;          // InterfaceAttribute, Grabbable
using Oculus.Interaction.Input;    // IHand
using UnityEngine;

namespace Eosat.Meta
{
    public class WristSummonUI : MonoBehaviour
    {
        [Header("Meta rig")]
        [Tooltip("Left Hand from the ISDK rig (the component implementing IHand).")]
        [SerializeField, Interface(typeof(IHand))] Object leftHand;
        [Tooltip("OVRCameraRig. Head = centerEyeAnchor, controller fallback = leftControllerAnchor.")]
        [SerializeField] OVRCameraRig cameraRig;

        [Header("Tablet")]
        [Tooltip("Root of the floating tablet (the object with Rigidbody + Grabbable).")]
        [SerializeField] GameObject tablet;
        [Tooltip("Optional: the tablet's Grabbable, so the tablet is never moved while someone is holding it.")]
        [SerializeField] Grabbable tabletGrabbable;
        [Tooltip("Hide the tablet at start (summon it with the gesture). Off = it starts open in front of you.")]
        [SerializeField] bool startHidden;

        [Header("Gesture")]
        [Tooltip("Wrist-local axis that points out of the BACK of the wrist (watch face). " +
                 "OpenXR hand skeleton: +Y is the back of the hand. Flip to (0,-1,0) to summon with the palm instead.")]
        [SerializeField] Vector3 wristWatchAxis = Vector3.up;
        [Tooltip("Controller-local axis that faces your eyes when you 'check the watch' holding the left Touch.")]
        [SerializeField] Vector3 controllerWatchAxis = Vector3.left;
        [Tooltip("How directly the watch face must point at the eyes (cosine; 0.6 ~ within 53 deg).")]
        [Range(0f, 1f)] [SerializeField] float faceHeadThreshold = 0.6f;
        [Tooltip("How directly you must be looking at the wrist (cosine; 0.8 ~ within 37 deg).")]
        [Range(0f, 1f)] [SerializeField] float gazeThreshold = 0.8f;
        [Tooltip("Hold the pose this long before it triggers (avoids accidental summons).")]
        [SerializeField] float holdSeconds = 0.35f;
        [Tooltip("Ignore the gesture for this long after it fires.")]
        [SerializeField] float cooldownSeconds = 1.0f;
        [Tooltip("Gesture checks per second (Quest-friendly; no need to test every frame).")]
        [SerializeField] float checkHz = 30f;

        [Header("Placement")]
        [SerializeField] float readingDistance = 0.55f;
        [SerializeField] float heightBelowEyes = 0.12f;
        [Tooltip("If the tablet is visible but further than this from the head, the gesture brings it back.")]
        [SerializeField] float recallIfFurtherThan = 1.2f;

        IHand hand;
        Transform head, leftController;
        float poseHeldSince = -1f, nextCheck, cooldownUntil;
        bool placedOnce;

        public bool IsShown => tablet && tablet.activeSelf;

        void Awake()
        {
            hand = leftHand as IHand;
            if (!cameraRig) cameraRig = FindAnyObjectByType<OVRCameraRig>();
            head = cameraRig ? cameraRig.centerEyeAnchor : Camera.main.transform;
            leftController = cameraRig ? cameraRig.leftControllerAnchor : null;
            if (tablet && startHidden) tablet.SetActive(false);
        }

        System.Collections.IEnumerator Start()
        {
            // Give the headset a moment to report a real head pose, then put the tablet in front of the user once.
            yield return new WaitForSeconds(0.5f);
            if (!startHidden && !placedOnce && UnityEngine.XR.XRSettings.isDeviceActive) Summon();
        }

        void Update()
        {
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + 1f / Mathf.Max(1f, checkHz);
            if (Time.time < cooldownUntil) return;

            if (!WatchPoseActive()) { poseHeldSince = -1f; return; }
            if (poseHeldSince < 0f) { poseHeldSince = Time.time; return; }
            if (Time.time - poseHeldSince < holdSeconds) return;

            poseHeldSince = -1f;
            cooldownUntil = Time.time + cooldownSeconds;
            OnGesture();
        }

        /// <summary>True while the watch face points at the eyes AND the user is looking at the wrist.</summary>
        bool WatchPoseActive()
        {
            Vector3 wristPos, faceDir;

            // 1) Tracked hand: ISDK root pose = wrist joint, already in world space.
            if (hand != null && hand.IsTrackedDataValid && hand.GetRootPose(out Pose wrist))
            {
                wristPos = wrist.position;
                faceDir = wrist.rotation * wristWatchAxis;
            }
            // 2) Controller fallback: OVRCameraRig keeps leftControllerAnchor in world space for us.
            else if (leftController && OVRInput.IsControllerConnected(OVRInput.Controller.LTouch)
                     && OVRInput.GetControllerPositionTracked(OVRInput.Controller.LTouch))
            {
                wristPos = leftController.position;
                faceDir = leftController.rotation * controllerWatchAxis;
            }
            else return false;

            Vector3 toHead = head.position - wristPos;
            float dist = toHead.magnitude;
            if (dist < 0.05f || dist > 0.9f) return false;   // arm must be raised in front of you
            toHead /= dist;

            // Dot products (no trig): watch face toward eyes, and eyes toward wrist.
            bool facing = Vector3.Dot(faceDir.normalized, toHead) > faceHeadThreshold;
            bool looking = Vector3.Dot(head.forward, -toHead) > gazeThreshold;
            return facing && looking;
        }

        void OnGesture()
        {
            if (!tablet) return;
            if (!IsShown) { Summon(); return; }
            // Already open: bring it back only if it's been left far away (and nobody is holding it).
            if (Vector3.Distance(tablet.transform.position, head.position) > recallIfFurtherThan) Summon();
        }

        /// <summary>Show the tablet in front of the user at reading distance, facing them.</summary>
        public void Summon()
        {
            if (!tablet || IsHeld()) return;
            placedOnce = true;
            Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            Vector3 pos = head.position + fwd * readingDistance + Vector3.down * heightBelowEyes;
            // Canvas faces +Z away from the viewer, so look along (tablet - head).
            Quaternion rot = Quaternion.LookRotation(pos - head.position, Vector3.up);

            var rb = tablet.GetComponent<Rigidbody>();
            if (rb && rb.isKinematic) { rb.position = pos; rb.rotation = rot; }   // keep physics in sync
            tablet.transform.SetPositionAndRotation(pos, rot);
            tablet.SetActive(true);
        }

        /// <summary>Hide the tablet (wire to a close "X" button on the canvas).</summary>
        public void Dismiss()
        {
            if (tablet && !IsHeld()) tablet.SetActive(false);
        }

        public void ToggleTablet() { if (IsShown) Dismiss(); else Summon(); }

        // Grabbable.GrabPoints holds one pose per hand currently grabbing it.
        bool IsHeld() => tabletGrabbable && tabletGrabbable.GrabPoints.Count > 0;
    }
}
