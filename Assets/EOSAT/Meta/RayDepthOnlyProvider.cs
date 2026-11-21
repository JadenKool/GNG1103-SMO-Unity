// RayDepthOnlyProvider.cs - ISDK movement provider for LASER (ray / distance) grabs of a panel.
// The laser can only:
//   - push / pull the panel along the line it was grabbed on (hand travel x pushPullGain, or either thumbstick up / down)
//   - tilt it: pitch and roll follow the controller's wrist; yaw (which would swing it sideways) is ignored
// It never drags the panel sideways across the room. To reposition a panel freely, grab its grab bar directly
// (near hand / controller grab), which uses the Grabbable's normal free transformer.
// Based on the earlier SensitiveRayPullProvider (fast push / pull), restricted to depth + pitch / roll.

using Oculus.Interaction;
using UnityEngine;

namespace Eosat.Meta
{
    public class RayDepthOnlyProvider : MonoBehaviour, IMovementProvider
    {
        [Tooltip("Hand moves 10 cm along the laser -> the panel moves 10 cm x this.")]
        [Range(1f, 12f)] public float pushPullGain = 5f;
        [Tooltip("Thumbstick up / down while holding: distance changes by this fraction of the current distance per second.")]
        [Range(0f, 4f)] public float stickSpeed = 1.2f;
        [Range(0f, 0.5f)] public float stickDeadZone = 0.2f;
        [Tooltip("Closest / farthest the panel can get from the hand along the laser (m).")]
        public float minDistance = 0.35f;
        public float maxDistance = 4f;
        [Tooltip("Pitch / roll follow the wrist (1 = same angle). 0 = laser never rotates the panel.")]
        [Range(0f, 2f)] public float tiltGain = 1f;

        public IMovement CreateMovement() => new Movement(this);

        class Movement : IMovement
        {
            readonly RayDepthOnlyProvider cfg;
            Pose source = Pose.identity, origin0 = Pose.identity;
            float baseDistance, stickOffset, currentDistance;

            public Movement(RayDepthOnlyProvider cfg) { this.cfg = cfg; }

            public Pose Pose { get; private set; } = Pose.identity;
            public bool Stopped => true;

            public void StopAndSetPose(Pose pose) { Pose = source = pose; }

            public void MoveTo(Pose target)
            {
                origin0 = target;                                   // ray origin when the grab started
                baseDistance = Vector3.Dot(source.position - target.position, target.forward);
                currentDistance = baseDistance;
                stickOffset = 0f;
                UpdateTarget(target);
            }

            public void UpdateTarget(Pose target)
            {
                float push = Vector3.Dot(target.position - origin0.position, origin0.forward);
                float wanted = baseDistance + push * cfg.pushPullGain + stickOffset;
                float clamped = Mathf.Clamp(wanted, Mathf.Min(cfg.minDistance, baseDistance), Mathf.Max(cfg.maxDistance, baseDistance));
                stickOffset += clamped - wanted;                    // don't wind up past the limits
                currentDistance = clamped;

                // Depth only: slide along the ORIGINAL laser direction, never sideways.
                Vector3 pos = source.position + origin0.forward * (clamped - baseDistance);

                // Wrist pitch / roll, measured about the panel's own right / forward axes.
                Quaternion delta = target.rotation * Quaternion.Inverse(origin0.rotation);
                Quaternion pitch = Scale(TwistAbout(delta, source.rotation * Vector3.right), cfg.tiltGain);
                Quaternion roll = Scale(TwistAbout(delta, source.rotation * Vector3.forward), cfg.tiltGain);
                Pose = new Pose(pos, (roll * pitch * source.rotation).normalized);
            }

            public void Tick()
            {
                float a = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.Touch).y;
                float b = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick, OVRInput.Controller.Touch).y;
                float y = Mathf.Abs(a) > Mathf.Abs(b) ? a : b;
                if (Mathf.Abs(y) < cfg.stickDeadZone) return;
                y = Mathf.Sign(y) * Mathf.InverseLerp(cfg.stickDeadZone, 1f, Mathf.Abs(y));
                stickOffset += y * cfg.stickSpeed * Mathf.Max(0.5f, currentDistance) * Time.deltaTime;
            }
        }

        static Quaternion Scale(Quaternion q, float k)
        {
            q.ToAngleAxis(out float ang, out Vector3 axis);
            if (ang > 180f) ang -= 360f;
            return float.IsNaN(axis.x) || axis.sqrMagnitude < 1e-6f ? Quaternion.identity : Quaternion.AngleAxis(ang * k, axis);
        }

        /// <summary>Twist part of q about axis (swing-twist decomposition).</summary>
        public static Quaternion TwistAbout(Quaternion q, Vector3 axis)
        {
            Vector3 proj = Vector3.Project(new Vector3(q.x, q.y, q.z), axis.normalized);
            var tw = new Quaternion(proj.x, proj.y, proj.z, q.w);
            float m = Mathf.Sqrt(tw.x * tw.x + tw.y * tw.y + tw.z * tw.z + tw.w * tw.w);
            return m < 1e-6f ? Quaternion.identity : new Quaternion(tw.x / m, tw.y / m, tw.z / m, tw.w / m);
        }
    }
}
