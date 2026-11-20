using System.Collections.Generic;
using UnityEngine;

namespace Eosat
{
    /// <summary>
    /// Draws the next orbit's ground track on the (rotating) 3D Earth, colour-coded: blue = ground-station pass,
    /// grey-violet = eclipse, yellow = sunlit. Put it on a child of EarthController.earthBody (unit-radius sphere):
    /// points are converted to Earth-fixed coordinates, so the track stays glued to the continents as the Earth turns.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class GroundTrackOverlay : MonoBehaviour
    {
        public SpacecraftController spacecraft;
        [Tooltip("Surface lift of the line, in Earth radii (the Earth mesh is a unit sphere).")]
        public float lift = 1.004f;
        public float refreshSeconds = 20f;
        public float stepSeconds = 30f;
        public Color sunlit = new Color(1f, 0.85f, 0.25f, 0.9f);
        public Color eclipse = new Color(0.55f, 0.55f, 0.9f, 0.9f);
        public Color pass = new Color(0.25f, 0.9f, 1f, 1f);

        LineRenderer line;
        float next;

        void Awake() { line = GetComponent<LineRenderer>(); line.useWorldSpace = false; line.positionCount = 0; }

        void Update()
        {
            if (!spacecraft || !spacecraft.HasData || spacecraft.OrbitRateDegPerSec < 1e-4f || Time.unscaledTime < next) return;
            next = Time.unscaledTime + refreshSeconds;
            Vector3 posKm = spacecraft.transform.localPosition / spacecraft.unitsPerKm;
            double period = 360.0 / spacecraft.OrbitRateDegPerSec;
            var p = OrbitPredictor.Predict(posKm, spacecraft.OrbitAxis, spacecraft.OrbitRateDegPerSec, spacecraft.DisplayUnix, period, stepSeconds);

            int n = p.positions.Count;
            line.positionCount = n;
            var keys = new List<GradientColorKey>();
            Color last = default;
            for (int i = 0; i < n; i++)
            {
                // ECI (Unity axes) -> Earth-fixed: undo the -GMST rotation EarthController applies to the Earth body.
                float gmst = (float)EciMath.GmstDegrees(EciMath.JulianDate(p.Time(i)));
                line.SetPosition(i, Quaternion.AngleAxis(gmst, Vector3.up) * p.positions[i].normalized * lift);
                // Fixed-mode gradient: a key colours everything up to its time, so keys go at the END of each run.
                Color c = p.contact[i] >= 0 ? pass : p.eclipse[i] ? eclipse : sunlit;
                if (i > 0 && c != last) keys.Add(new GradientColorKey(last, (i - 1) / (float)(n - 1)));
                last = c;
            }
            keys.Add(new GradientColorKey(last, 1f));
            // shortcut: Unity gradients hold 8 keys; an orbit rarely has more than ~6 colour changes. Extra ones are dropped.
            if (keys.Count > 8) keys.RemoveRange(8, keys.Count - 8);
            var g = new Gradient { mode = GradientMode.Fixed };
            g.SetKeys(keys.ToArray(), new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            line.colorGradient = g;
        }
    }
}
