using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace Eosat.MR
{
    /// <summary>
    /// Keeps a floating tablet attached to the non-dominant wrist (XR Hands), with smoothing.
    /// Wrist-menu style: shows when the palm faces the user, hides shortly after it turns away.
    /// Falls back to the Touch controller for that hand when hands aren't tracked.
    /// </summary>
    public class HandTablet : MonoBehaviour
    {
        [Header("Tracking")]
        public XROrigin xrOrigin;
        [Tooltip("Non-dominant hand. Left for right-handed users.")]
        public Handedness hand = Handedness.Left;
        public ControllerPose controllerFallback;

        [Header("Tablet")]
        [Tooltip("Child object holding the canvas. Shown/hidden by this script.")]
        public GameObject tabletVisual;
        [Tooltip("Offset in wrist space: y+ = back of hand, y- = palm side, z+ = toward fingers.")]
        public Vector3 wristOffset = new Vector3(0f, -0.07f, 0.06f);
        public Vector3 controllerOffset = new Vector3(0f, 0.08f, 0.05f);
        [Tooltip("Turn the tablet to face the user's eyes (more readable than matching wrist rotation).")]
        public bool faceUser = true;
        [Range(1f, 40f)] public float followSharpness = 16f;

        [Header("Wrist-menu visibility")]
        public bool onlyWhenPalmFacesUser = true;
        [Range(0f, 1f)] public float palmFacingThreshold = 0.4f;
        [Tooltip("Tick if the tablet appears when the palm faces AWAY instead.")]
        public bool invertPalmNormal;
        public float hideDelay = 0.35f;

        static readonly List<XRHandSubsystem> s_Subsystems = new List<XRHandSubsystem>();
        XRHandSubsystem subsystem;
        Transform cam;
        float lastShownTime = -10f;
        bool snapNext = true;

        void Start()
        {
            if (!xrOrigin) xrOrigin = FindAnyObjectByType<XROrigin>();
            cam = xrOrigin ? xrOrigin.Camera.transform : Camera.main.transform;
            if (tabletVisual) tabletVisual.SetActive(false);
        }

        void LateUpdate()
        {
            bool havePose = TryGetTargetPose(out Pose target, out bool palmOk);
            bool show = havePose && palmOk;
            if (show) lastShownTime = Time.time;

            bool visible = show || (tabletVisual && tabletVisual.activeSelf && Time.time - lastShownTime < hideDelay);
            if (tabletVisual && tabletVisual.activeSelf != visible)
            {
                tabletVisual.SetActive(visible);
                if (visible) snapNext = true;
            }
            if (!havePose || !visible) return;

            if (faceUser)
                target.rotation = Quaternion.LookRotation(target.position - cam.position, Vector3.up);

            if (snapNext)
            {
                transform.SetPositionAndRotation(target.position, target.rotation);
                snapNext = false;
                return;
            }

            float t = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, target.position, t),
                Quaternion.Slerp(transform.rotation, target.rotation, t));
        }

        bool TryGetTargetPose(out Pose target, out bool palmFacing)
        {
            target = default;
            palmFacing = !onlyWhenPalmFacesUser;

            if (TryGetHand(out XRHand h) && h.isTracked
                && h.GetJoint(XRHandJointID.Wrist).TryGetPose(out Pose wrist)
                && h.GetJoint(XRHandJointID.Palm).TryGetPose(out Pose palm))
            {
                // Joint poses are relative to the XR Origin; convert to world space.
                Transform origin = xrOrigin.Origin.transform;
                wrist = wrist.GetTransformedBy(origin);
                palm = palm.GetTransformedBy(origin);

                target = new Pose(wrist.position + wrist.rotation * wristOffset, wrist.rotation);

                if (onlyWhenPalmFacesUser)
                {
                    Vector3 palmNormal = invertPalmNormal ? palm.up : -palm.up;
                    Vector3 toEye = (cam.position - palm.position).normalized;
                    palmFacing = Vector3.Dot(palmNormal, toEye) > palmFacingThreshold;
                }
                return true;
            }

            if (controllerFallback && controllerFallback.IsTracked)
            {
                Transform c = controllerFallback.transform;
                target = new Pose(c.TransformPoint(controllerOffset), c.rotation);
                palmFacing = true;
                return true;
            }
            return false;
        }

        bool TryGetHand(out XRHand result)
        {
            result = default;
            if (subsystem == null || !subsystem.running)
            {
                subsystem = null;
                SubsystemManager.GetSubsystems(s_Subsystems);
                foreach (var s in s_Subsystems)
                    if (s.running) { subsystem = s; break; }
                if (subsystem == null) return false;
            }
            result = hand == Handedness.Left ? subsystem.leftHand : subsystem.rightHand;
            return true;
        }
    }
}
