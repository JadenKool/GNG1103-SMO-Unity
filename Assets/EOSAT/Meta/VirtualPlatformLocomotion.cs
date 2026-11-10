// VirtualPlatformLocomotion.cs  -  Meta XR Core SDK (OVRInput / OVRCameraRig)
// "Magic carpet": OVRCameraRig is a CHILD of this platform. The thumbsticks drive the platform through space;
// you can still physically walk around on top of it (room-scale tracking moves you relative to the platform).
//
//   Left stick      : move forward/back/strafe, relative to where you are looking (horizontal plane)
//   Right stick X   : snap turn 30 deg (or smooth turn), pivoting around your HEAD
//   Right stick Y   : rise / sink
//   Left trigger    : hold for boost
//
// EnvironmentSwitcher enables this component in VR and disables it in MR (where it parks the platform at its
// start pose so the rig is back in plain room space). Optional "ride along": the platform also follows a moving
// target (the satellite) so you can inspect it up close while it orbits.
//
// OVRManager > Tracking Origin Type = Floor Level so you stand ON the platform.

using UnityEngine;

namespace Eosat.Meta
{
    [DisallowMultipleComponent]
    public class VirtualPlatformLocomotion : MonoBehaviour
    {
        [Header("References (auto-found if empty)")]
        [Tooltip("CenterEyeAnchor of the OVRCameraRig that is a child of this platform.")]
        [SerializeField] Transform head;

        [Header("Movement")]
        [SerializeField] float speed = 1.5f;                 // m/s at full stick
        [SerializeField] float boostMultiplier = 4f;
        [SerializeField] float verticalSpeed = 1.0f;         // m/s
        [Tooltip("Seconds to reach full speed / stop. Small easing = more comfortable than instant start/stop.")]
        [SerializeField] float acceleration = 0.25f;
        [SerializeField, Range(0f, 0.5f)] float deadZone = 0.15f;

        [Header("Turning")]
        [SerializeField] bool snapTurn = true;
        [SerializeField] float snapAngle = 30f;
        [SerializeField] float smoothTurnSpeed = 60f;        // deg/s when snapTurn is off

        [Header("Ride along")]
        [Tooltip("If set, the platform also moves with this target (e.g. the orbiting satellite).")]
        [SerializeField] Transform rideAlong;
        [SerializeField] bool rideAlongEnabled = true;

        [Header("Limits")]
        [Tooltip("0 = unlimited. Otherwise the platform can't drift further than this from its start pose.")]
        [SerializeField] float maxDistanceFromStart = 0f;

        public Transform Head => head;
        public bool RideAlongEnabled { get => rideAlongEnabled; set { rideAlongEnabled = value; ResyncRideAlong(); } }

        Vector3 velocity, startPos, lastRidePos;
        Quaternion startRot;
        bool snapReady = true;

        void Awake()
        {
            if (!head)
            {
                var rig = GetComponentInChildren<OVRCameraRig>(true);
                if (rig) head = rig.centerEyeAnchor ? rig.centerEyeAnchor : rig.transform.Find("TrackingSpace/CenterEyeAnchor");
            }
            if (!head) Debug.LogWarning("[VirtualPlatformLocomotion] Make OVRCameraRig a child of this platform.", this);
            startPos = transform.position;
            startRot = transform.rotation;
        }

        void OnEnable() { velocity = Vector3.zero; ResyncRideAlong(); }

        /// <summary>Back to the start pose (MR: rig in plain room space).</summary>
        public void ResetPlatform() { transform.SetPositionAndRotation(startPos, startRot); velocity = Vector3.zero; ResyncRideAlong(); }

        public void SetRideAlong(Transform target) { rideAlong = target; ResyncRideAlong(); }
        public void ResyncRideAlong() { if (rideAlong) lastRidePos = rideAlong.position; }

        /// <summary>Teleport so the HEAD ends up at eyePosition, looking (horizontally) along lookDirection.</summary>
        public void PlaceHead(Vector3 eyePosition, Vector3 lookDirection)
        {
            if (!head) return;
            transform.rotation = startRot;
            Vector3 up = transform.up;
            Vector3 want = Vector3.ProjectOnPlane(lookDirection, up);
            Vector3 have = Vector3.ProjectOnPlane(head.forward, up);
            if (want.sqrMagnitude > 1e-4f && have.sqrMagnitude > 1e-4f)
                transform.RotateAround(head.position, up, Vector3.SignedAngle(have, want, up));
            transform.position += eyePosition - head.position;
            velocity = Vector3.zero;
            ResyncRideAlong();
        }

        void Update()
        {
            if (!head) return;
            float dt = Time.deltaTime;

            // Ride along with the target (before driving, so stick input is relative to the moving frame).
            if (rideAlong && rideAlongEnabled)
            {
                Vector3 d = rideAlong.position - lastRidePos;
                if (d.sqrMagnitude < 25f) transform.position += d;     // ignore jumps of the target (> 5 m in a frame)
                lastRidePos = rideAlong.position;
            }

            Vector2 move = DeadZone(OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.Touch));
            Vector2 look = DeadZone(OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick, OVRInput.Controller.Touch));
            bool boost = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.Touch) > 0.5f;

            // Direction from where the head looks, flattened so looking down doesn't drive into the floor.
            Vector3 fwd = Vector3.ProjectOnPlane(head.forward, transform.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.ProjectOnPlane(transform.forward, transform.up).normalized;
            Vector3 right = Vector3.Cross(transform.up, fwd);

            float s = speed * (boost ? boostMultiplier : 1f);
            Vector3 target = (fwd * move.y + right * move.x) * s + transform.up * (look.y * verticalSpeed);
            float k = acceleration <= 0f ? 1f : 1f - Mathf.Exp(-dt / (acceleration * 0.35f));
            velocity = Vector3.Lerp(velocity, target, k);

            Vector3 next = transform.position + velocity * dt;
            if (maxDistanceFromStart > 0f)
            {
                Vector3 off = next - startPos;
                if (off.magnitude > maxDistanceFromStart) next = startPos + off.normalized * maxDistanceFromStart;
            }
            transform.position = next;

            // Turning: rotate the platform around the head so the world turns around YOU.
            if (snapTurn)
            {
                if (Mathf.Abs(look.x) > 0.7f && snapReady) { RotateAroundHead(Mathf.Sign(look.x) * snapAngle); snapReady = false; }
                else if (Mathf.Abs(look.x) < 0.3f) snapReady = true;
            }
            else if (Mathf.Abs(look.x) > 0f)
            {
                RotateAroundHead(look.x * smoothTurnSpeed * dt);
            }
        }

        void RotateAroundHead(float degrees) => transform.RotateAround(head.position, transform.up, degrees);

        Vector2 DeadZone(Vector2 v)
        {
            float m = v.magnitude;
            if (m < deadZone) return Vector2.zero;
            return v.normalized * Mathf.InverseLerp(deadZone, 1f, Mathf.Min(m, 1f));   // rescale so motion starts smoothly
        }
    }
}
