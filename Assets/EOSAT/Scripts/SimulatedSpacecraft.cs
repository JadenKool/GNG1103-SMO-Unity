using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Eosat
{
    /// <summary>
    /// Stand-in for the client's test tool. Emits JSON at 1 Hz through TelemetryHub.Submit, so it exercises
    /// the exact same parsing/validation path as the TCP feed. The orbit maths lives ONLY here (test tool side);
    /// the visualizer itself does no orbit propagation, per the constraints.
    ///
    /// Test keys: P panels, A antenna (stowed > deployed > jammed), F pause feed, B send bad packet, G fast spin.
    /// </summary>
    [RequireComponent(typeof(TelemetryHub))]
    public class SimulatedSpacecraft : MonoBehaviour
    {
        const double Mu = 398600.4418; // km^3/s^2

        [Header("Orbit")]
        public float altitudeKm = 500f;
        public float inclinationDeg = 97.4f;
        public float raanDeg = 30f;
        [Tooltip("Simulated seconds per real second. 1 = real time (an orbit takes ~95 min).")]
        public float timeWarp = 20f;
        public float sampleIntervalSec = 1f;

        [Header("Attitude (nadir pointing + roll)")]
        public float rollDegPerSample = 4f;
        public float fastSpinDegPerSample = 70f;

        [Header("Scenario")]
        public float autoDeployPanelsAfterSec = 8f;
        public float autoDeployAntennaAfterSec = 15f;
        public bool feedPaused;
        public bool fastSpin;

        PanelState panels = PanelState.Stowed;
        AntennaState antenna = AntennaState.Stowed;
        bool autoPanelsDone, autoAntennaDone, sendBadNext;
        double simUnix, epochUnix;
        float timer, elapsed, roll;
        TelemetryHub hub;

        void Start()
        {
            hub = GetComponent<TelemetryHub>();
            simUnix = epochUnix = EciMath.UnixNow();
            timer = sampleIntervalSec; // send the first packet immediately
        }

        void Update()
        {
            if (hub.source != TelemetrySourceMode.Simulated) return;
            HandleKeys();

            elapsed += Time.deltaTime;
            simUnix += Time.deltaTime * timeWarp;

            if (!autoPanelsDone && elapsed > autoDeployPanelsAfterSec) { panels = PanelState.Deployed; autoPanelsDone = true; }
            if (!autoAntennaDone && elapsed > autoDeployAntennaAfterSec) { antenna = AntennaState.Deployed; autoAntennaDone = true; }

            timer += Time.deltaTime;
            if (timer < sampleIntervalSec) return;
            timer = Mathf.Min(timer - sampleIntervalSec, sampleIntervalSec);
            roll = (roll + (fastSpin ? fastSpinDegPerSample : rollDegPerSample)) % 360f;

            if (feedPaused) return;
            if (sendBadNext)
            {
                sendBadNext = false;
                hub.Submit("{\"pos_eci_km\":[12.0,3.0,4.0],\"q\":[2.0,0.0,0.0,0.0]}");
                return;
            }
            hub.Submit(BuildJson());
        }

        void HandleKeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.pKey.wasPressedThisFrame) { panels = panels == PanelState.Deployed ? PanelState.Stowed : PanelState.Deployed; autoPanelsDone = true; }
            if (kb.aKey.wasPressedThisFrame)
            {
                antenna = antenna == AntennaState.Stowed ? AntennaState.Deployed
                        : antenna == AntennaState.Deployed ? AntennaState.Jammed : AntennaState.Stowed;
                autoAntennaDone = true;
            }
            if (kb.fKey.wasPressedThisFrame) feedPaused = !feedPaused;
            if (kb.bKey.wasPressedThisFrame) sendBadNext = true;
            if (kb.gKey.wasPressedThisFrame) fastSpin = !fastSpin;
        }

        string BuildJson()
        {
            double a = EciMath.EarthRadiusKm + altitudeKm;
            double n = System.Math.Sqrt(Mu / (a * a * a));
            double u = n * (simUnix - epochUnix);
            double inc = inclinationDeg * Mathf.Deg2Rad, raan = raanDeg * Mathf.Deg2Rad;
            double cu = System.Math.Cos(u), su = System.Math.Sin(u);
            double cO = System.Math.Cos(raan), sO = System.Math.Sin(raan);
            double ci = System.Math.Cos(inc), si = System.Math.Sin(inc);

            var pos = new Vector3(
                (float)(a * (cO * cu - sO * ci * su)),
                (float)(a * (sO * cu + cO * ci * su)),
                (float)(a * si * su));
            var vel = new Vector3(
                (float)(-cO * su - sO * ci * cu),
                (float)(-sO * su + cO * ci * cu),
                (float)(si * cu));

            // Attitude built in Unity space: body Z (local +Y) to nadir, body X (local +X) along velocity.
            Vector3 nadirU = -EciMath.EciToUnity(pos).normalized;
            Vector3 velU = EciMath.EciToUnity(vel).normalized;
            Quaternion qU = Quaternion.LookRotation(Vector3.Cross(velU, nadirU), nadirU) * Quaternion.AngleAxis(roll, Vector3.up);
            EciMath.UnityQuatToEci(qU, out float w, out float x, out float y, out float z);

            Vector3 sun = EciMath.SunDirectionEci(EciMath.JulianDate(simUnix));
            bool eclipse = EciMath.InEarthShadow(pos, sun);

            string q = hub.quaternionScalarFirst ? F("{0:F6},{1:F6},{2:F6},{3:F6}", w, x, y, z)
                                                 : F("{0:F6},{1:F6},{2:F6},{3:F6}", x, y, z, w);
            return F("{{\"t\":{0:F3},\"pos_eci_km\":[{1:F3},{2:F3},{3:F3}],\"q\":[{4}],\"panels\":\"{5}\",\"antenna\":\"{6}\",\"eclipse\":{7}}}",
                simUnix, pos.x, pos.y, pos.z, q,
                panels.ToString().ToLowerInvariant(), antenna.ToString().ToLowerInvariant(), eclipse ? "true" : "false");
        }

        static string F(string format, params object[] args) => string.Format(CultureInfo.InvariantCulture, format, args);
    }
}
