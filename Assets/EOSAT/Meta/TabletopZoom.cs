// TabletopZoom.cs  -  Meta XR Core SDK (OVRInput)
// MR mode thumbstick mapping: push either stick up/down to zoom the tabletop globe in/out
// (uniform scale around the globe's centre, so the satellite and its orbit grow with it).
// EnvironmentSwitcher enables this in MR and disables it in VR (where the sticks drive the platform).

using UnityEngine;

namespace Eosat.Meta
{
    [DisallowMultipleComponent]
    public class TabletopZoom : MonoBehaviour
    {
        [Tooltip("What to scale. Defaults to this transform (put it on Globe Pivot).")]
        [SerializeField] Transform target;
        [Tooltip("Zoom limits as multiples of the starting size.")]
        [SerializeField] float minZoom = 0.4f;
        [SerializeField] float maxZoom = 5f;
        [Tooltip("Doublings per second at full stick.")]
        [SerializeField] float zoomSpeed = 1.2f;
        [SerializeField, Range(0f, 0.6f)] float deadZone = 0.25f;

        public float Zoom { get; private set; } = 1f;
        Vector3 baseScale;

        void Awake()
        {
            if (!target) target = transform;
            baseScale = target.localScale;
        }

        void Update()
        {
            // Strongest vertical push of either stick, so it works with whichever hand is free.
            float l = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.Touch).y;
            float r = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick, OVRInput.Controller.Touch).y;
            float y = Mathf.Abs(l) > Mathf.Abs(r) ? l : r;
            if (Mathf.Abs(y) < deadZone) return;
            float push = Mathf.Sign(y) * Mathf.InverseLerp(deadZone, 1f, Mathf.Abs(y));
            SetZoom(Zoom * Mathf.Pow(2f, push * zoomSpeed * Time.deltaTime));
        }

        public void SetZoom(float z)
        {
            Zoom = Mathf.Clamp(z, minZoom, maxZoom);
            target.localScale = baseScale * Zoom;
        }

        public void ResetZoom() => SetZoom(1f);
    }
}
