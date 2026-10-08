using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace Eosat
{
    /// <summary>
    /// Sits on the "Camera Rig" (the camera is a head-tracked child). Orbits a target and keeps the view
    /// aligned to the inertial frame, never to the spacecraft's spin, to avoid motion sickness.
    ///
    /// Desktop: mouse drag orbits, scroll zooms, 1 = spacecraft, 2 = Earth.
    /// VR (Meta Quest via Link): your head looks around; thumbstick left/right snap-turns 30 deg,
    /// up/down zooms; A or X switches spacecraft/Earth. The rig never pitches or rolls in VR, and
    /// target switches are instant (teleport-style) rather than a sliding camera, which is more comfortable.
    /// </summary>
    public class OrbitCamera : MonoBehaviour
    {
        public Transform spacecraft;
        public Transform earth;
        public float spacecraftDistance = 1.2f;
        public float earthDistance = 24f;
        public float minDistance = 0.25f, maxDistance = 80f;
        public float rotateSpeed = 0.2f;
        public float zoomStep = 0.12f;
        public float smoothing = 6f;

        [Header("VR")]
        public float snapTurnDegrees = 30f;
        public float vrZoomSpeed = 1.2f;
        [Tooltip("Earth view distance in VR (metres to the Earth's centre; Earth radius is 6.4 m).")]
        public float vrEarthDistance = 16f;

        public float yaw = 35f, pitch = 15f;
        public bool InVr => XRSettings.isDeviceActive;

        Transform target;
        float distance, currentDistance;
        Vector3 focus;
        bool snapArmed = true;
        InputAction stick, switchTarget;

        void OnEnable()
        {
            stick = new InputAction("EOSAT Stick", InputActionType.Value, expectedControlType: "Vector2");
            stick.AddBinding("<XRController>{RightHand}/{Primary2DAxis}");
            stick.AddBinding("<XRController>{LeftHand}/{Primary2DAxis}");
            stick.AddBinding("<Gamepad>/rightStick");
            switchTarget = new InputAction("EOSAT Switch Target", InputActionType.Button);
            switchTarget.AddBinding("<XRController>{RightHand}/{PrimaryButton}");
            switchTarget.AddBinding("<XRController>{LeftHand}/{PrimaryButton}");
            switchTarget.AddBinding("<Gamepad>/buttonSouth");
            stick.Enable();
            switchTarget.Enable();
        }

        void OnDisable()
        {
            stick?.Disable(); stick?.Dispose();
            switchTarget?.Disable(); switchTarget?.Dispose();
        }

        void Start()
        {
            SetTarget(spacecraft, spacecraftDistance);
            focus = target ? target.position : Vector3.zero;
            currentDistance = distance;
        }

        public void SetTarget(Transform t, float dist)
        {
            target = t;
            distance = dist;
        }

        public void ToggleTarget()
        {
            if (target == spacecraft) SetTarget(earth, InVr ? vrEarthDistance : earthDistance);
            else SetTarget(spacecraft, spacecraftDistance);
            if (InVr) { focus = target ? target.position : Vector3.zero; currentDistance = distance; }
        }

        void LateUpdate()
        {
            bool vr = InVr;
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame && target != spacecraft) ToggleTarget();
                if (kb.digit2Key.wasPressedThisFrame && target != earth) ToggleTarget();
            }
            if (switchTarget.WasPressedThisFrame()) ToggleTarget();

            if (mouse != null && !vr)
            {
                if (mouse.leftButton.isPressed || mouse.rightButton.isPressed)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    yaw += d.x * rotateSpeed;
                    pitch = Mathf.Clamp(pitch - d.y * rotateSpeed, -85f, 85f);
                }
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    distance = Mathf.Clamp(distance * (scroll > 0 ? 1f - zoomStep : 1f + zoomStep), minDistance, maxDistance);
            }

            Vector2 s = stick.ReadValue<Vector2>();
            if (Mathf.Abs(s.x) > 0.7f && snapArmed) { yaw += Mathf.Sign(s.x) * snapTurnDegrees; snapArmed = false; }
            if (Mathf.Abs(s.x) < 0.3f) snapArmed = true;
            if (Mathf.Abs(s.y) > 0.2f)
                distance = Mathf.Clamp(distance * Mathf.Exp(-s.y * vrZoomSpeed * Time.deltaTime), minDistance, maxDistance);

            float k = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            Vector3 targetPos = target ? target.position : Vector3.zero;
            focus = Vector3.Lerp(focus, targetPos, Vector3.Distance(focus, targetPos) > 0.05f ? k : 1f);
            currentDistance = Mathf.Lerp(currentDistance, distance, k);

            // In VR the horizon must stay level: yaw only; the headset supplies pitch and roll.
            Quaternion rot = vr ? Quaternion.Euler(0f, yaw, 0f) : Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(focus + rot * new Vector3(0f, 0f, -currentDistance), rot);
        }
    }
}
