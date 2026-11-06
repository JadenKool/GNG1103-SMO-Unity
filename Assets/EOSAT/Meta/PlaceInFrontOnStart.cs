// PlaceInFrontOnStart.cs - puts an object (the floating globe) in front of the user once the headset
// reports a head pose. Replaces the AR Foundation desk search: with the Meta rig the globe simply
// floats where you can reach it, and you grab it to move it anywhere (it then hovers there).

using System.Collections;
using UnityEngine;

namespace Eosat.Meta
{
    public class PlaceInFrontOnStart : MonoBehaviour
    {
        [Tooltip("Object to place (defaults to this object).")]
        public Transform target;
        [Tooltip("OVRCameraRig.centerEyeAnchor. Auto-found if empty.")]
        public Transform head;
        public float forwardDistance = 0.6f;
        public float rightOffset = 0.15f;
        [Tooltip("Height relative to the eyes (negative = below).")]
        public float heightOffset = -0.35f;

        public bool IsPlaced { get; private set; }

        IEnumerator Start()
        {
            if (!target) target = transform;
            if (!head)
            {
                var rig = FindAnyObjectByType<OVRCameraRig>();
                head = rig ? rig.centerEyeAnchor : Camera.main.transform;
            }
            yield return new WaitForSeconds(0.5f);       // first frames: head pose may still be zero
            if (UnityEngine.XR.XRSettings.isDeviceActive) Place();
            IsPlaced = true;                             // desktop: keep the scene's saved pose
        }

        /// <summary>Move the target in front of the user, upright, facing them.</summary>
        public void Place()
        {
            Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 pos = head.position + fwd * forwardDistance + right * rightOffset + Vector3.up * heightOffset;
            var rb = target.GetComponent<Rigidbody>();
            Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
            if (rb && rb.isKinematic) { rb.position = pos; rb.rotation = rot; }
            target.SetPositionAndRotation(pos, rot);
            Debug.Log($"[EOSAT Meta] Globe placed in front of the user at {pos:F2}");
        }
    }
}
