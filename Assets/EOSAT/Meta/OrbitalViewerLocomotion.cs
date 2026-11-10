// OrbitalViewerLocomotion.cs  -  Meta XR Core SDK (OVRInput / OVRCameraRig)
// VR mode: the player sits on a sphere around a pivot and orbits it with the thumbsticks.
// Put this on the PARENT of OVRCameraRig.
//
//   Right stick      : orbit freely in any direction, a full 360 degrees (no pitch / yaw limits)
//   Left stick Y     : zoom (change the orbit radius; push up = closer)
//   A / X            : recentre (pivot straight ahead at eye level from where you now stand and face)
//   Desktop testing  : arrow keys orbit, PageUp / PageDown zoom, T = switch target
//
// Camera target (ToggleTarget / SetFollowSatellite, wired to the VR console button):
//   Follow Earth      - the pivot is the Earth's centre.
//   Follow Satellite  - the pivot is the satellite's POSITION only. The satellite's rotation is never read:
//                       the view direction changes only when you move the stick, so the satellite spins
//                       and tumbles in front of you while you stay steady (comfort requirement).
//
// The orbit direction is stored as a quaternion and turned about its own axes, so there is no pole,
// no clamp and no gimbal lock: stick right always moves you right, stick up always moves you up.

using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Eosat.Meta
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]            // after SpacecraftController / EarthController have moved things this frame
    public class OrbitalViewerLocomotion : MonoBehaviour
    {
        public enum Target { Earth, Satellite }

        [Header("References")]
        [Tooltip("Centre of the Earth.")]
        [SerializeField] Transform earthCenter;
        [Tooltip("The satellite to follow in 'Follow Satellite' mode. Only its position is used.")]
        [SerializeField] Transform satellite;
        [Tooltip("CenterEyeAnchor of the OVRCameraRig that is a child of this object. Auto-found if empty.")]
        [SerializeField] Transform head;

        [Header("Orbit")]
        [Tooltip("Degrees per second at full stick (scaled down automatically when close).")]
        [SerializeField] float orbitSpeed = 45f;
        [SerializeField] float startYaw = 0f;
        [SerializeField] float startPitch = 15f;
        [SerializeField] bool invertYaw;
        [SerializeField] bool invertPitch;

        [Header("Zoom - Follow Earth (distance from the Earth's centre, scene units)")]
        [Tooltip("Earth radius in scene units (6.378 when 1 unit = 1000 km).")]
        [SerializeField] float earthRadius = 6.378f;
        [SerializeField] float earthStartRadius = 16f;
        [SerializeField] float earthMinRadius = 7.3f;
        [SerializeField] float earthMaxRadius = 60f;

        [Header("Zoom - Follow Satellite (distance from the satellite)")]
        [SerializeField] float satelliteStartRadius = 1.2f;
        [SerializeField] float satelliteMinRadius = 0.25f;
        [Tooltip("Keep below 2.3 so the automatic 'stay above ground' zoom is always smooth.")]
        [SerializeField] float satelliteMaxRadius = 2.2f;
        [Tooltip("Never let the eyes get closer than this to the Earth's surface (scene units). The satellite flies only 0.5 units up, so when the Earth is between you and it the view slides in along your line of sight instead of going underground.")]
        [SerializeField] float surfaceClearance = 0.08f;

        [Tooltip("How fast the distance doubles / halves per second at full stick.")]
        [SerializeField] float zoomSpeed = 1.2f;

        [Header("Feel")]
        [SerializeField, Range(0f, 0.5f)] float deadZone = 0.2f;
        [Tooltip("Seconds of smoothing on orbit and zoom (0 = instant).")]
        [SerializeField, Range(0f, 0.5f)] float smoothing = 0.12f;

        [Header("Input")]
        [SerializeField] OVRInput.Button recenterButton = OVRInput.Button.One;      // A
        [SerializeField] OVRInput.Button alternateButton = OVRInput.Button.Three;   // X
        [SerializeField] Key desktopTargetKey = Key.T;

        public Target CurrentTarget { get; private set; } = Target.Earth;
        public bool FollowingSatellite => CurrentTarget == Target.Satellite;
        public float Radius { get; private set; }
        /// <summary>Direction the orbit looks along (from you toward the pivot), world space.</summary>
        public Quaternion Orbit { get; private set; } = Quaternion.identity;
        public Transform Head => head;
        public event Action<Target> TargetChanged;

        Quaternion targetOrbit = Quaternion.identity;
        float targetRadius;
        float earthRadiusSaved, satelliteRadiusSaved;
        Vector3 anchorLocal;        // head position in this object's space when the view was (re)centred
        float anchorYaw;            // head yaw in this object's space at that moment
        bool needsCapture = true;
        Vector3 roomPos; Quaternion roomRot;

        bool HeadReady => head && (!UnityEngine.XR.XRSettings.isDeviceActive || head.localPosition.sqrMagnitude > 1e-4f);
        Transform Pivot => FollowingSatellite && satellite ? satellite : earthCenter;
        float SurfaceRadius => FollowingSatellite ? 0f : earthRadius;
        float MinRadius => FollowingSatellite ? satelliteMinRadius : earthMinRadius;
        float MaxRadius => FollowingSatellite ? satelliteMaxRadius : earthMaxRadius;

        void Awake()
        {
            if (!head)
            {
                var rig = GetComponentInChildren<OVRCameraRig>(true);
                if (rig) head = rig.centerEyeAnchor ? rig.centerEyeAnchor : rig.transform.Find("TrackingSpace/CenterEyeAnchor");
            }
            if (!head) Debug.LogWarning("[OrbitalViewerLocomotion] Make OVRCameraRig a child of this object.", this);
            roomPos = transform.position;
            roomRot = transform.rotation;
            earthRadiusSaved = earthStartRadius;
            satelliteRadiusSaved = satelliteStartRadius;
            ResetView();
        }

        void OnEnable() { needsCapture = true; }

        /// <summary>Back to the starting view of the current target.</summary>
        public void ResetView()
        {
            targetOrbit = Orbit = Quaternion.Euler(startPitch, startYaw, 0f);
            targetRadius = Radius = Mathf.Clamp(FollowingSatellite ? satelliteStartRadius : earthStartRadius, MinRadius, MaxRadius);
            needsCapture = true;
        }

        /// <summary>Re-align so the pivot is straight ahead at eye level from where you stand and face now.</summary>
        public void Recenter() { needsCapture = true; }

        /// <summary>MR: put the rig back in plain room space (call before disabling this component).</summary>
        public void ResetToRoom() { transform.SetPositionAndRotation(roomPos, roomRot); needsCapture = true; }

        /// <summary>UI button: Follow Earth <-> Follow Satellite.</summary>
        public void ToggleTarget() => SetTarget(FollowingSatellite ? Target.Earth : Target.Satellite);

        /// <summary>UI toggle (on = follow the satellite).</summary>
        public void SetFollowSatellite(bool follow) => SetTarget(follow ? Target.Satellite : Target.Earth);

        public void SetTarget(Target target)
        {
            if (target == CurrentTarget) return;
            if (target == Target.Satellite && !satellite) { Debug.LogWarning("[OrbitalViewerLocomotion] No satellite assigned."); return; }
            // The jump between pivots happens behind the switcher's short fade, never as a visible lurch.
            var sw = EnvironmentSwitcher.Instance;
            if (sw && isActiveAndEnabled) sw.RunBehindFade(() => ApplyTarget(target));
            else ApplyTarget(target);
        }

        void ApplyTarget(Target target)
        {
            if (FollowingSatellite) satelliteRadiusSaved = targetRadius; else earthRadiusSaved = targetRadius;
            CurrentTarget = target;
            // This runs behind the fade, so the view can be re-aimed without a visible lurch.
            if (FollowingSatellite && earthCenter)
            {
                // Start on the space side of the satellite, a little off to one side, with the Earth below it.
                Vector3 up = (satellite.position - earthCenter.position).normalized;
                Vector3 side = Vector3.Cross(up, Vector3.up);
                if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(up, Vector3.right);
                Vector3 back = (up + side.normalized * 0.7f).normalized;
                targetOrbit = Orbit = Quaternion.LookRotation(-back, up);
            }
            else targetOrbit = Orbit = Quaternion.Euler(startPitch, startYaw, 0f);
            targetRadius = Radius = Mathf.Clamp(FollowingSatellite ? satelliteRadiusSaved : earthRadiusSaved, MinRadius, MaxRadius);
            Debug.Log("[OrbitalViewerLocomotion] Camera target: " + (FollowingSatellite ? "Follow Satellite (position only)" : "Follow Earth"));
            TargetChanged?.Invoke(target);
        }

        void CaptureAnchor()
        {
            anchorLocal = transform.InverseTransformPoint(head.position);
            Vector3 f = transform.InverseTransformDirection(head.forward);
            f.y = 0f;
            anchorYaw = f.sqrMagnitude > 1e-4f ? Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg : 0f;
            needsCapture = false;
        }

        void LateUpdate()
        {
            if (!head || !Pivot) return;
            if (needsCapture) { if (!HeadReady) return; CaptureAnchor(); }

            float dt = Time.deltaTime;
            Vector2 orbit = DeadZone(OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick, OVRInput.Controller.Touch));
            float zoom = DeadZone(new Vector2(0f, OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.Touch).y)).y;

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.rightArrowKey.isPressed) orbit.x += 1f;
                if (kb.leftArrowKey.isPressed) orbit.x -= 1f;
                if (kb.upArrowKey.isPressed) orbit.y += 1f;
                if (kb.downArrowKey.isPressed) orbit.y -= 1f;
                if (kb.pageUpKey.isPressed) zoom += 1f;
                if (kb.pageDownKey.isPressed) zoom -= 1f;
                if (kb[desktopTargetKey].wasPressedThisFrame) ToggleTarget();
            }
            if (OVRInput.GetDown(recenterButton, OVRInput.Controller.Touch) || OVRInput.GetDown(alternateButton, OVRInput.Controller.Touch))
                CaptureAnchor();

            // Slower angular speed when close, so the surface doesn't whip past.
            float height = Mathf.Max(0.05f, targetRadius - SurfaceRadius);
            float startHeight = Mathf.Max(0.01f, (FollowingSatellite ? satelliteStartRadius : earthStartRadius) - SurfaceRadius);
            float closeness = FollowingSatellite ? 1f : Mathf.Clamp(height / startHeight, 0.15f, 1f);
            float dYaw = -orbit.x * orbitSpeed * closeness * dt * (invertYaw ? -1f : 1f);     // stick right = you move right
            float dPitch = orbit.y * orbitSpeed * closeness * dt * (invertPitch ? -1f : 1f);  // stick up = you move up
            // Free rotation: turn about the orbit's OWN axes. No Mathf.Clamp, no pole, full 360 in every direction.
            if (dYaw != 0f || dPitch != 0f)
                targetOrbit = (targetOrbit * Quaternion.AngleAxis(dYaw, Vector3.up) * Quaternion.AngleAxis(dPitch, Vector3.right)).normalized;

            // Zoom acts on the height above the surface (or the distance to the satellite), so it feels even near and far.
            if (Mathf.Abs(zoom) > 0f)
                targetRadius = Mathf.Clamp(SurfaceRadius + height * Mathf.Pow(2f, -zoom * zoomSpeed * dt), MinRadius, MaxRadius);

            float k = smoothing <= 0f ? 1f : 1f - Mathf.Exp(-dt / smoothing);
            Orbit = Quaternion.Slerp(Orbit, targetOrbit, k);
            Radius = Mathf.Lerp(Radius, targetRadius, k);
            Apply();
        }

        // Rig pose so that the anchored head position sits on the orbit sphere, looking at the pivot.
        // Only Pivot.position is read: the pivot's rotation (the satellite's spin) never reaches the camera.
        void Apply()
        {
            Quaternion rigRot = Orbit * Quaternion.Euler(0f, -anchorYaw, 0f);
            float dist = Radius;
            if (FollowingSatellite && earthCenter)
            {
                // The line from the satellite back to the eyes must not enter the Earth. If it would, stop just short:
                // a pure slide along the line of sight, so the satellite stays centred and nothing rotates.
                Vector3 back = -(Orbit * Vector3.forward);
                Vector3 m = Pivot.position - earthCenter.position;
                float minR = earthRadius + surfaceClearance;
                float b = Vector3.Dot(m, back);
                float c = m.sqrMagnitude - minR * minR;
                float disc = b * b - c;
                if (c > 0f && b < 0f && disc > 0f) dist = Mathf.Min(dist, Mathf.Max(0.1f, -b - Mathf.Sqrt(disc)));
            }
            Vector3 eye = Pivot.position - Orbit * Vector3.forward * dist;
            transform.SetPositionAndRotation(eye - rigRot * anchorLocal, rigRot);
        }

        Vector2 DeadZone(Vector2 v)
        {
            float m = v.magnitude;
            if (m < deadZone) return Vector2.zero;
            return v.normalized * Mathf.InverseLerp(deadZone, 1f, Mathf.Min(m, 1f));
        }
    }
}
