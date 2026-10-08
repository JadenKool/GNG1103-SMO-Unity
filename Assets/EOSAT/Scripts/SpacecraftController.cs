using UnityEngine;

namespace Eosat
{
    /// <summary>
    /// Turns 1 Hz telemetry into smooth 60+ fps motion.
    /// - Each new sample starts a segment from where the model IS to where the data says it should be,
    ///   so the model never teleports (no visible jumps, even after a long dropout).
    /// - If data is late, it keeps moving using the last observed rates (simple extrapolation, not orbit propagation).
    /// - After stallTimeoutSec without data the feed is flagged as stalled (HUD dims and warns).
    /// - Visual spin is rate-limited and flagged when it exceeds a comfort threshold.
    /// </summary>
    public class SpacecraftController : MonoBehaviour
    {
        public TelemetryHub hub;
        public DeployablesAnimator deployables;
        [Tooltip("Everything visible on the spacecraft; hidden until the first packet arrives.")]
        public GameObject visualRoot;
        public TrailRenderer trail;

        [Tooltip("Scene scale: 1 Unity unit = 1000 km.")]
        public float unitsPerKm = 0.001f;

        [Header("Data loss handling")]
        public float stallTimeoutSec = 3f;
        [Tooltip("Max time to keep predicting motion after the last packet (target spec: tolerate up to 60 s lag).")]
        public float maxPredictionSec = 60f;
        [Tooltip("If the new sample is this far (units) from where the model is, blend over catchUpDurationSec instead of 1 s.")]
        public float catchUpDistanceUnits = 0.3f;
        public float catchUpDurationSec = 3f;

        [Header("Motion sickness")]
        public float spinWarningDegPerSec = 30f;
        public bool limitVisualSpin = true;
        public float maxVisualSpinDegPerSec = 45f;

        public bool HasData { get; private set; }
        public bool Stalled => HasData && SecondsSinceData > stallTimeoutSec;
        public bool Predicting { get; private set; }
        public float SecondsSinceData => HasData ? Time.time - lastSampleReal : 0f;
        public double DisplayUnix { get; private set; }
        public float AltitudeKm => dispPos.magnitude / unitsPerKm - (float)EciMath.EarthRadiusKm;
        public float VisualSpinDegPerSec { get; private set; }
        public float PhysicalSpinDegPerSec { get; private set; }
        public bool SpinWarning => VisualSpinDegPerSec > spinWarningDegPerSec;
        public bool InEclipse { get; private set; }

        TelemetrySample latest;
        Vector3 startPos, endPos, dispPos;
        Quaternion startRot, endRot, dispRot = Quaternion.identity;
        double startT, endT;
        float segStart, segDur = 1f, lastSampleReal, avgInterval = 1f;
        Vector3 posAxis = Vector3.up, rotAxis = Vector3.up;
        float posRateDeg, rotRateDeg, timeRate = 1f;

        void Awake()
        {
            if (visualRoot) visualRoot.SetActive(false);
            if (trail) trail.emitting = false;
        }

        void OnEnable() { if (hub) hub.SampleReceived += OnSample; }
        void OnDisable() { if (hub) hub.SampleReceived -= OnSample; }

        void OnSample(TelemetrySample s)
        {
            float now = Time.time;
            Vector3 p = EciMath.EciToUnity(s.posEciKm) * unitsPerKm;
            Quaternion q = EciMath.EciQuatToUnity(s.qw, s.qx, s.qy, s.qz);
            double t = s.hasTime ? s.unixTime : EciMath.UnixNow();
            latest = s;

            if (!HasData)
            {
                HasData = true;
                startPos = endPos = dispPos = p;
                startRot = endRot = dispRot = q;
                startT = endT = t;
                DisplayUnix = t;
                segStart = now; segDur = 1f; lastSampleReal = now;
                if (visualRoot) visualRoot.SetActive(true);
                Apply();
                if (trail) { trail.Clear(); trail.emitting = true; }
                return;
            }

            float interval = Mathf.Clamp(now - lastSampleReal, 0.05f, 10f);
            ComputeRates(endPos, p, endRot, q, endT, t, interval);
            if (interval < 3f) avgInterval = Mathf.Lerp(avgInterval, interval, 0.3f);

            startPos = dispPos; startRot = dispRot; startT = DisplayUnix;
            endPos = p; endRot = q; endT = t;
            segStart = now;
            segDur = Mathf.Clamp(avgInterval, 0.2f, 2f);
            float expectedStep = posRateDeg * Mathf.Deg2Rad * p.magnitude * segDur;
            if (Vector3.Distance(startPos, endPos) > catchUpDistanceUnits + expectedStep * 1.5f)
                segDur = Mathf.Max(segDur, catchUpDurationSec);
            lastSampleReal = now;
        }

        void ComputeRates(Vector3 a, Vector3 b, Quaternion qa, Quaternion qb, double ta, double tb, float interval)
        {
            Quaternion.FromToRotation(a, b).ToAngleAxis(out float ang, out Vector3 ax);
            Normalize(ref ang, ref ax);
            posAxis = ax; posRateDeg = ang / interval;

            (qb * Quaternion.Inverse(qa)).ToAngleAxis(out float rang, out Vector3 rax);
            Normalize(ref rang, ref rax);
            rotAxis = rax; rotRateDeg = rang / interval;
            VisualSpinDegPerSec = rotRateDeg;

            double dt = tb - ta;
            PhysicalSpinDegPerSec = dt > 1e-3 ? (float)(rang / dt) : rotRateDeg;
            timeRate = dt > 0 ? (float)(dt / interval) : 1f;
        }

        static void Normalize(ref float angle, ref Vector3 axis)
        {
            if (float.IsNaN(axis.x) || float.IsInfinity(axis.x) || axis.sqrMagnitude < 1e-6f) { angle = 0f; axis = Vector3.up; return; }
            if (angle > 180f) { angle = 360f - angle; axis = -axis; }
        }

        void Update()
        {
            if (!HasData) return;
            float now = Time.time;
            float a = (now - segStart) / segDur;
            Vector3 p; Quaternion r; double t;

            if (a <= 1f)
            {
                p = Vector3.Slerp(startPos, endPos, a);
                r = Quaternion.Slerp(startRot, endRot, a);
                t = startT + (endT - startT) * a;
                Predicting = false;
            }
            else
            {
                float extra = Mathf.Min(now - segStart - segDur, maxPredictionSec);
                p = Quaternion.AngleAxis(posRateDeg * extra, posAxis) * endPos;
                r = Quaternion.AngleAxis(rotRateDeg * extra, rotAxis) * endRot;
                t = endT + timeRate * extra;
                Predicting = SecondsSinceData > segDur * 1.5f;
            }

            dispPos = p;
            dispRot = limitVisualSpin ? Quaternion.RotateTowards(dispRot, r, maxVisualSpinDegPerSec * Time.deltaTime) : r;
            DisplayUnix = t;
            Apply();
        }

        void Apply()
        {
            transform.localPosition = dispPos;
            transform.localRotation = dispRot;

            if (latest != null)
            {
                if (latest.hasEclipse) InEclipse = latest.eclipse;
                else
                {
                    Vector3 sunU = EciMath.EciToUnity(EciMath.SunDirectionEci(EciMath.JulianDate(DisplayUnix)));
                    InEclipse = EciMath.InEarthShadow(dispPos / unitsPerKm, sunU);
                }
                if (deployables) deployables.SetTargets(latest.panels, latest.antenna);
            }
        }
    }
}
