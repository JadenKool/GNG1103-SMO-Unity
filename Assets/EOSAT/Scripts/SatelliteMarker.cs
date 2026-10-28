using UnityEngine;

namespace Eosat
{
    /// <summary>
    /// Concept 4 (Adjustable-Scale Space View): as the camera zooms out (mouse scroll or VR thumbstick),
    /// a glowing yellow dot fades in on the satellite and keeps a constant on-screen size, so the
    /// spacecraft can still be followed when the model itself is too small to see.
    /// </summary>
    [DefaultExecutionOrder(200)] // after the camera has moved this frame
    public class SatelliteMarker : MonoBehaviour
    {
        public Camera cam;
        [Tooltip("Child holding the dot + halo. Scaled by this script.")]
        public Transform marker;

        [Tooltip("Dot diameter as a fraction of the camera distance (constant on-screen size). 0.02 = ~1.1 deg.")]
        public float angularSize = 0.02f;
        [Tooltip("Never smaller than this (world units).")]
        public float minWorldSize = 0f;

        [Header("Fade with zoom (camera distance, scene units)")]
        public float showFrom = 5f;
        public float fullAt = 15f;
        [Tooltip("Show the dot at every distance (used on the MR tabletop globe).")]
        public bool alwaysShow;

        [Header("Glow")]
        public Renderer halo;
        public float pulseSpeed = 2f;
        [Range(0f, 0.5f)] public float pulseAmount = 0.15f;

        public float Visibility { get; private set; }

        void LateUpdate()
        {
            if (!marker) return;
            if (!cam) cam = Camera.main;
            if (!cam) return;

            float d = Vector3.Distance(cam.transform.position, transform.position);
            float t = alwaysShow ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(showFrom, fullAt, d));
            Visibility = t;

            bool on = t > 0.001f;
            if (marker.gameObject.activeSelf != on) marker.gameObject.SetActive(on);
            if (!on) return;

            float size = Mathf.Max(minWorldSize, d * angularSize) * t;
            float parentScale = marker.parent ? marker.parent.lossyScale.x : 1f;
            marker.localScale = Vector3.one * (size / Mathf.Max(1e-6f, parentScale));

            if (halo)
            {
                float pulse = 1f + pulseAmount * Mathf.Sin(Time.time * pulseSpeed * 2f * Mathf.PI);
                halo.transform.localScale = Vector3.one * 2.4f * pulse;
            }
        }
    }
}
