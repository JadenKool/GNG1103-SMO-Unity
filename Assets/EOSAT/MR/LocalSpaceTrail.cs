// LocalSpaceTrail.cs - orbit trail that lives in the globe's own space.
// Unity's TrailRenderer draws in WORLD space, so every time the globe is recentred, grabbed or zoomed it
// leaves a long straight streak behind that takes a minute to fade (the "endless yellow trail").
// This trail stores points relative to a frame (the globe's ECI Frame), so it moves and scales with the globe,
// and it is clamped by age, by point count and against sudden jumps.

using System.Collections.Generic;
using UnityEngine;

namespace Eosat.MR
{
    [RequireComponent(typeof(LineRenderer))]
    public class LocalSpaceTrail : MonoBehaviour
    {
        [Tooltip("Object being traced (the orbiting spacecraft).")]
        public Transform target;
        [Tooltip("Frame the trail is stored in (the globe's ECI Frame). This object should be a child of it.")]
        public Transform frame;
        [Tooltip("Points older than this are removed (seconds).")]
        public float maxAgeSeconds = 40f;
        public int maxPoints = 200;
        [Tooltip("Minimum travel between points, in frame-local units.")]
        public float minDistance = 0.0015f;
        [Tooltip("A move bigger than this in one frame (frame-local units) is a jump, not travel: the trail restarts.")]
        public float jumpDistance = 0.05f;

        LineRenderer line;
        readonly List<Vector3> points = new List<Vector3>();
        readonly List<float> times = new List<float>();
        Vector3[] buffer;

        void Awake()
        {
            line = GetComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 0;
            if (!frame) frame = transform.parent;
            transform.localPosition = Vector3.zero; transform.localRotation = Quaternion.identity; transform.localScale = Vector3.one;
        }

        void OnEnable() => Clear();

        public void Clear()
        {
            points.Clear(); times.Clear();
            if (line) line.positionCount = 0;
        }

        void LateUpdate()
        {
            if (!target || !frame || !target.gameObject.activeInHierarchy) { if (points.Count > 0) Clear(); return; }
            Vector3 p = frame.InverseTransformPoint(target.position);
            float now = Time.time;

            if (points.Count > 0)
            {
                float d = (p - points[points.Count - 1]).magnitude;
                if (d > jumpDistance) Clear();                       // source switch / reconnect jump
                else if (d < minDistance) p = Vector3.positiveInfinity;
            }
            if (!float.IsInfinity(p.x)) { points.Add(p); times.Add(now); }

            // Clamp by age and by length.
            int drop = 0;
            while (drop < points.Count && (now - times[drop] > maxAgeSeconds || points.Count - drop > maxPoints)) drop++;
            if (drop > 0) { points.RemoveRange(0, drop); times.RemoveRange(0, drop); }

            int n = points.Count;
            if (buffer == null || buffer.Length < n) buffer = new Vector3[Mathf.Max(n, maxPoints)];
            for (int i = 0; i < n; i++) buffer[i] = points[i];      // index 0 = oldest (faded end of the gradient)
            line.positionCount = n;
            line.SetPositions(buffer);
        }
    }
}
