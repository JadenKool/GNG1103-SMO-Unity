using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace Eosat.MR
{
    /// <summary>
    /// Snaps this transform to one hand joint (e.g. the right index tip, for poking the tablet).
    /// Falls back to a controller pose (plus an offset to the controller tip) when hands aren't tracked.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class HandJointFollower : MonoBehaviour
    {
        public XROrigin xrOrigin;
        public Handedness handedness = Handedness.Right;
        public XRHandJointID joint = XRHandJointID.IndexTip;
        public ControllerPose controllerFallback;
        public Vector3 controllerTipOffset = new Vector3(0f, -0.01f, 0.05f);
        [Tooltip("Optional object shown only while a pose is available (e.g. a fingertip dot).")]
        public GameObject visual;

        public bool HasPose { get; private set; }

        static readonly List<XRHandSubsystem> s_Subsystems = new List<XRHandSubsystem>();
        XRHandSubsystem subsystem;

        void Start()
        {
            if (!xrOrigin) xrOrigin = FindAnyObjectByType<XROrigin>();
        }

        void Update()
        {
            HasPose = false;
            if (TryGetHand(out XRHand hand) && hand.isTracked && hand.GetJoint(joint).TryGetPose(out Pose pose))
            {
                pose = pose.GetTransformedBy(xrOrigin.Origin.transform);
                transform.SetPositionAndRotation(pose.position, pose.rotation);
                HasPose = true;
            }
            else if (controllerFallback && controllerFallback.IsTracked)
            {
                Transform c = controllerFallback.transform;
                transform.SetPositionAndRotation(c.TransformPoint(controllerTipOffset), c.rotation);
                HasPose = true;
            }
            if (visual && visual.activeSelf != HasPose) visual.SetActive(HasPose);
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
            result = handedness == Handedness.Left ? subsystem.leftHand : subsystem.rightHand;
            return true;
        }
    }
}
