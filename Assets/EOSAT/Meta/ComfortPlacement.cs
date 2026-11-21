// ComfortPlacement.cs - puts a panel at a comfortable reading distance instead of pinning it to the head.
//   - On enable it appears straight ahead (yaw only, level), distance / height / side offset as set, facing the eyes.
//   - follow = true : lazy follow. It stays put until it drifts more than followAngle from where you look, then glides back.
//   - follow = false: placed once; after that it stays where it is (grab its bar to move it).
//   - anchorAbove   : if that object is visible (e.g. the MR globe), float above it instead, facing you.
//   - onlyWhileActive: hide the canvas while that object is inactive (e.g. VR guide in MR mode).
// Parent it under the rig's TrackingSpace so it moves with the user in the VR orbit view.

using UnityEngine;

namespace Eosat.Meta
{
    public class ComfortPlacement : MonoBehaviour
    {
        public Transform head;
        [Range(0.4f, 1.5f)] public float distance = 0.8f;
        [Tooltip("Metres above (+) / below (-) eye level.")]
        public float up;
        [Tooltip("Metres to the right (+) / left (-).")]
        public float right;
        public bool follow;
        [Range(5f, 60f)] public float followAngle = 22f;
        [Range(0.5f, 10f)] public float followSpeed = 3f;
        public Transform anchorAbove;
        public float aboveHeight = 0.6f;
        public GameObject onlyWhileActive;

        Canvas[] canvases;
        bool gliding;

        void OnEnable()
        {
            canvases = GetComponentsInChildren<Canvas>(true);
            if (!head && Camera.main) head = Camera.main.transform;
            Snap();
        }

        public void Snap() { if (head) { Target(out var p, out var r); transform.SetPositionAndRotation(p, r); } }

        void Target(out Vector3 pos, out Quaternion rot)
        {
            if (anchorAbove && anchorAbove.gameObject.activeInHierarchy)
            {
                pos = anchorAbove.position + Vector3.up * aboveHeight;
            }
            else
            {
                Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(head.up, Vector3.up);
                fwd.Normalize();
                pos = head.position + fwd * distance + Vector3.up * up + Vector3.Cross(Vector3.up, fwd) * right;
            }
            rot = Quaternion.LookRotation(pos - head.position, Vector3.up);
        }

        void LateUpdate()
        {
            if (onlyWhileActive && canvases != null)
                foreach (var c in canvases) if (c) c.enabled = onlyWhileActive.activeInHierarchy;
            if (!follow || !head) return;

            Target(out var p, out var r);
            float off = Vector3.Angle(transform.position - head.position, p - head.position);
            if (off > followAngle) gliding = true;
            if (!gliding) return;
            float k = 1f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime);
            transform.SetPositionAndRotation(Vector3.Lerp(transform.position, p, k), Quaternion.Slerp(transform.rotation, r, k));
            if (off < 1.5f) gliding = false;
        }
    }
}
