// RotateInPlaceTransformer.cs  -  Meta Interaction SDK (ITransformer)
// MR globe: grab (hand, distance or laser) to SPIN the Earth about its own polar axis. Nothing else moves:
// the globe parent, the orbit, the spacecraft and the miniature Sun stay exactly where they are.
// The grabbed point's angle around the polar axis (x swingGain) is added to EarthController.userSpinDeg.
// Without an EarthController it falls back to turning the grabbed object about its own up axis (still never moving it).
// Set as both the One Grab and Two Grab Transformer on the globe's Grabbable (the builder does this).

using Oculus.Interaction;
using UnityEngine;

namespace Eosat.Meta
{
    public class RotateInPlaceTransformer : MonoBehaviour, ITransformer
    {
        [Tooltip("1 = the grabbed point follows the hand exactly; higher spins further per hand movement.")]
        [SerializeField, Range(0.5f, 6f)] float swingGain = 1.5f;
        [Tooltip("Earth whose body spins (userSpinDeg). Empty = spin the grabbed object about its own up axis.")]
        [SerializeField] EarthController earth;

        IGrabbable grabbable;
        Vector3 lockedPos, lockedScale, startDir;
        Quaternion lockedRot;
        float startSpin;

        public void Initialize(IGrabbable g) => grabbable = g;

        Vector3 PolarAxis => earth && earth.earthBody && earth.earthBody.parent ? earth.earthBody.parent.up : lockedRot * Vector3.up;

        public void BeginTransform()
        {
            var t = grabbable.Transform;
            lockedPos = t.position; lockedScale = t.localScale; lockedRot = t.rotation;
            startSpin = earth ? earth.userSpinDeg : 0f;
            startDir = grabbable.GrabPoints.Count > 0 ? grabbable.GrabPoints[0].position - lockedPos : Vector3.zero;
        }

        public void UpdateTransform()
        {
            var t = grabbable.Transform;
            float deg = 0f;
            if (grabbable.GrabPoints.Count > 0)
                deg = SpinAngle(startDir, grabbable.GrabPoints[0].position - lockedPos, PolarAxis) * swingGain;

            if (earth) { earth.userSpinDeg = Mathf.Repeat(startSpin + deg + 180f, 360f) - 180f; t.SetPositionAndRotation(lockedPos, lockedRot); }
            else t.SetPositionAndRotation(lockedPos, Quaternion.AngleAxis(deg, PolarAxis) * lockedRot);
            t.localScale = lockedScale;    // fixed size, fixed place
        }

        public void EndTransform() { }

        /// <summary>Signed angle (deg) from a to b measured around axis, ignoring the components along the axis.</summary>
        public static float SpinAngle(Vector3 a, Vector3 b, Vector3 axis)
        {
            Vector3 pa = Vector3.ProjectOnPlane(a, axis), pb = Vector3.ProjectOnPlane(b, axis);
            if (pa.sqrMagnitude < 1e-8f || pb.sqrMagnitude < 1e-8f) return 0f;   // grabbed right at a pole
            return Vector3.SignedAngle(pa, pb, axis);
        }
    }
}
