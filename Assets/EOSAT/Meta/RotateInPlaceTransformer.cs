// RotateInPlaceTransformer.cs  -  Meta Interaction SDK (ITransformer)
// MR globe: grab (hand, distance or laser) to SPIN it, never to move or scale it.
// Swing: the grabbed surface point follows your hand / laser around the globe centre (x swingGain).
// Twist: rolling your wrist turns the globe about the grab direction. Regrab for unlimited 360 deg turns.
// Set as both the One Grab and Two Grab Transformer on the globe's Grabbable (the builder does this).

using Oculus.Interaction;
using UnityEngine;

namespace Eosat.Meta
{
    public class RotateInPlaceTransformer : MonoBehaviour, ITransformer
    {
        [Tooltip("1 = the grabbed point follows the hand exactly; higher spins further per hand movement.")]
        [SerializeField, Range(0.5f, 4f)] float swingGain = 1.5f;
        [Tooltip("Rolling the wrist also turns the globe about the grab direction.")]
        [SerializeField] bool wristTwist = true;

        IGrabbable grabbable;
        Vector3 lockedPos, startDir, lockedScale;
        Quaternion startRot, startHand;

        public void Initialize(IGrabbable g) => grabbable = g;

        public void BeginTransform()
        {
            var t = grabbable.Transform;
            lockedPos = t.position; lockedScale = t.localScale; startRot = t.rotation;
            if (grabbable.GrabPoints.Count == 0) return;
            Pose p = grabbable.GrabPoints[0];
            startDir = p.position - lockedPos;
            startHand = p.rotation;
        }

        public void UpdateTransform()
        {
            if (grabbable.GrabPoints.Count == 0) return;
            Pose p = grabbable.GrabPoints[0];
            Vector3 dir = p.position - lockedPos;

            Quaternion swing = Quaternion.identity;
            if (startDir.sqrMagnitude > 1e-6f && dir.sqrMagnitude > 1e-6f)
            {
                Quaternion.FromToRotation(startDir, dir).ToAngleAxis(out float ang, out Vector3 axis);
                if (ang > 180f) ang -= 360f;
                if (axis.sqrMagnitude > 1e-6f && !float.IsNaN(axis.x)) swing = Quaternion.AngleAxis(ang * swingGain, axis);
            }
            Quaternion twist = wristTwist && dir.sqrMagnitude > 1e-6f
                ? TwistAbout(p.rotation * Quaternion.Inverse(startHand), dir.normalized) : Quaternion.identity;

            var t = grabbable.Transform;
            t.SetPositionAndRotation(lockedPos, (twist * swing * startRot).normalized);
            t.localScale = lockedScale;    // fixed size
        }

        public void EndTransform() { }

        // Twist part of q around axis (swing-twist decomposition).
        static Quaternion TwistAbout(Quaternion q, Vector3 axis)
        {
            Vector3 proj = Vector3.Project(new Vector3(q.x, q.y, q.z), axis);
            var tw = new Quaternion(proj.x, proj.y, proj.z, q.w);
            float m = Mathf.Sqrt(tw.x * tw.x + tw.y * tw.y + tw.z * tw.z + tw.w * tw.w);
            return m < 1e-6f ? Quaternion.identity : new Quaternion(tw.x / m, tw.y / m, tw.z / m, tw.w / m);
        }
    }
}
