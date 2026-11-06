using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Eosat.MR
{
    /// <summary>
    /// Tabletop globe interaction (XRI 3.7). Two separate handles so the hands never fight:
    ///  - SPIN: this component is itself an XRI interactable on the globe sphere. Grab/pinch the globe and
    ///    drag around it to spin the whole display (Earth + orbit + satellite) about its centre. Lets go with inertia.
    ///  - MOVE: an XRGrabInteractable on the globe root (collider = the desk stand only). Grab the stand to
    ///    carry it with full 6 DoF (position + any tilt); on release it stays exactly as left (or, with
    ///    keepUprightOnRelease, stands upright on the table plane) and is re-anchored so it stays put while you walk around it.
    /// Put this on a child of the globe root that has the sphere collider. Set the move grab's Colliders list
    /// to the stand collider only, and this component's Colliders list to the sphere only.
    /// </summary>
    public class InteractiveGlobe : XRBaseInteractable
    {
        [Header("Spin")]
        [Tooltip("What turns when you spin: the parent of the ECI frame, orbit overlay and Sun marker. NOT the globe root.")]
        public Transform spinPivot;
        [Tooltip("On = turn only about the vertical axis (turntable). Off = free trackball, so you can also tip it to look from below.")]
        public bool yawOnly;
        [Range(0.2f, 3f)] public float spinSensitivity = 1.2f;
        [Tooltip("How fast a flicked globe slows down (1/s). 0 = spins forever.")]
        public float inertiaDamping = 2.5f;
        public float maxSpinDegPerSec = 540f;

        [Header("Move / place")]
        [Tooltip("XRGrabInteractable on the globe root (the stand). Optional - leave empty for spin-only.")]
        public XRGrabInteractable moveGrab;
        [Tooltip("Off = full 6 DoF: the move grab tracks rotation too and the globe stays exactly as released. " +
                 "On = stand it back upright and snap it onto the table plane underneath.")]
        public bool keepUprightOnRelease;
        [Tooltip("Globe centre height above the surface it stands on (same as the stand height).")]
        public float standHeight = 0.2f;
        [Tooltip("On release, drop onto the highest detected table plane under the globe (needs planes from Space Setup).")]
        public bool snapToTablePlanes = true;
        [Tooltip("How far below the stand base a plane may be and still be snapped to (m).")]
        public float snapSearchDepth = 0.35f;
        public ARPlaneManager planeManager;
        [Tooltip("Re-anchor after each move so the globe doesn't drift while you walk around it.")]
        public ARAnchorManager anchorManager;
        [Tooltip("Lock the globe in place (spinning still works).")]
        public bool movementLocked;

        public UnityEvent onPlaced;

        public bool IsSpinning => isSelected || angularVelocity.sqrMagnitude > 1f;
        public bool IsBeingMoved => moveGrab && moveGrab.isSelected;

        Vector3 lastDir;
        bool hasLastDir;
        Vector3 angularVelocity;        // axis * deg/s (world)
        Quaternion homeRotation;
        ARAnchor ownAnchor;

        protected override void Awake()
        {
            base.Awake();
            selectMode = InteractableSelectMode.Single;
            if (!spinPivot) spinPivot = transform;
            homeRotation = spinPivot.localRotation;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (moveGrab)
            {
                moveGrab.selectEntered.AddListener(OnMoveStart);
                moveGrab.selectExited.AddListener(OnMoveEnd);
                moveGrab.enabled = !movementLocked;
            }
        }

        protected override void OnDisable()
        {
            if (moveGrab)
            {
                moveGrab.selectEntered.RemoveListener(OnMoveStart);
                moveGrab.selectExited.RemoveListener(OnMoveEnd);
            }
            base.OnDisable();
        }

        // ---------------- Spin ----------------

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            hasLastDir = TryGetHandDir(args.interactorObject, out lastDir);
            angularVelocity = Vector3.zero;
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            hasLastDir = false;   // keep angularVelocity -> inertia
        }

        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.ProcessInteractable(updatePhase);
            if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic || !spinPivot) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (isSelected)
            {
                if (!TryGetHandDir(firstInteractorSelecting, out Vector3 dir)) return;
                if (!hasLastDir) { lastDir = dir; hasLastDir = true; return; }

                Quaternion delta;
                if (yawOnly)
                {
                    float angle = Vector3.SignedAngle(Flat(lastDir), Flat(dir), Vector3.up) * spinSensitivity;
                    delta = Quaternion.AngleAxis(angle, Vector3.up);
                }
                else
                {
                    delta = Quaternion.SlerpUnclamped(Quaternion.identity, Quaternion.FromToRotation(lastDir, dir), spinSensitivity);
                }
                lastDir = dir;
                Rotate(delta);

                delta.ToAngleAxis(out float deg, out Vector3 axis);
                if (deg > 180f) deg -= 360f;
                Vector3 instant = float.IsFinite(axis.x) ? axis * (deg / dt) : Vector3.zero;
                angularVelocity = Vector3.ClampMagnitude(Vector3.Lerp(angularVelocity, instant, 0.35f), maxSpinDegPerSec);
            }
            else if (angularVelocity.sqrMagnitude > 1f)
            {
                float speed = angularVelocity.magnitude;
                Rotate(Quaternion.AngleAxis(speed * dt, angularVelocity / speed));
                angularVelocity *= Mathf.Exp(-inertiaDamping * dt);
            }
            else angularVelocity = Vector3.zero;
        }

        void Rotate(Quaternion worldDelta) => spinPivot.rotation = worldDelta * spinPivot.rotation;

        bool TryGetHandDir(IXRSelectInteractor interactor, out Vector3 dir)
        {
            dir = default;
            if (interactor == null) return false;
            Transform attach = interactor.GetAttachTransform(this);
            if (!attach) return false;
            dir = attach.position - spinPivot.position;
            if (yawOnly) dir = Flat(dir);
            if (dir.sqrMagnitude < 1e-6f) return false;
            dir.Normalize();
            return true;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>Stop spinning and turn the display back to how it started.</summary>
        public void ResetSpin()
        {
            angularVelocity = Vector3.zero;
            if (spinPivot) spinPivot.localRotation = homeRotation;
        }

        // ---------------- Move / place ----------------

        public void SetMovementLocked(bool locked)
        {
            movementLocked = locked;
            if (moveGrab) moveGrab.enabled = !locked;
        }

        void OnMoveStart(SelectEnterEventArgs _)
        {
            angularVelocity = Vector3.zero;
            if (planeManager && keepUprightOnRelease && snapToTablePlanes && UnityEngine.XR.XRSettings.isDeviceActive) planeManager.enabled = true;
        }

        async void OnMoveEnd(SelectExitEventArgs _)
        {
            Transform root = moveGrab.transform;
            if (keepUprightOnRelease)
            {
                Vector3 p = root.position;
                if (snapToTablePlanes && TryFindSurfaceBelow(p, out float surfaceY)) p.y = surfaceY + standHeight;
                root.SetPositionAndRotation(p, Quaternion.Euler(0f, root.eulerAngles.y, 0f));
            }
            // else 6 DoF: it stays exactly where and how it was let go (any position, any tilt).
            if (planeManager && UnityEngine.XR.XRSettings.isDeviceActive) planeManager.enabled = false;

            await ReAnchor(root);
            Debug.Log($"[EOSAT MR] Globe placed at {root.position:F2}" + (root.parent && root.parent.GetComponent<ARAnchor>() ? " (anchored)" : ""));
            onPlaced?.Invoke();
        }

        bool TryFindSurfaceBelow(Vector3 globeCentre, out float y)
        {
            y = 0f;
            if (!planeManager || planeManager.trackables.count == 0) return false;
            float baseY = globeCentre.y - standHeight;
            bool found = false;
            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane.alignment != PlaneAlignment.HorizontalUp) continue;
                float py = plane.transform.position.y;
                if (py > baseY + 0.05f || py < baseY - snapSearchDepth) continue;
                if (!InsideBoundary(plane, globeCentre)) continue;
                if (!found || py > y) { y = py; found = true; }
            }
            return found;
        }

        static bool InsideBoundary(ARPlane plane, Vector3 world)
        {
            Vector3 l = plane.transform.InverseTransformPoint(world);
            var b = plane.boundary;
            if (b.Length < 3) return Mathf.Abs(l.x) <= plane.extents.x && Mathf.Abs(l.z) <= plane.extents.y;
            bool inside = false;
            for (int i = 0, j = b.Length - 1; i < b.Length; j = i++)
            {
                if ((b[i].y > l.z) != (b[j].y > l.z) &&
                    l.x < (b[j].x - b[i].x) * (l.z - b[i].y) / (b[j].y - b[i].y) + b[i].x)
                    inside = !inside;
            }
            return inside;
        }

        async Task ReAnchor(Transform root)
        {
            if (!anchorManager || anchorManager.subsystem == null || !anchorManager.subsystem.running) return;
            var oldAnchor = root.parent ? root.parent.GetComponent<ARAnchor>() : null;
            var pose = new Pose(root.position, root.rotation);   // anchor exactly where it was left
            var result = await anchorManager.TryAddAnchorAsync(pose);
            if (!result.status.IsSuccess() || !root) return;
            root.SetParent(result.value.transform, true);
            ownAnchor = result.value;
            if (oldAnchor && oldAnchor != ownAnchor) anchorManager.TryRemoveAnchor(oldAnchor);
        }
    }
}
