using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Eosat
{
    /// <summary>
    /// Scores the GNG1103 SMO test plan "pass checks" from the telemetry Unity actually accepted.
    /// Add to the Telemetry object (or let the test harness add it), run the feed, then call Report()
    /// (or right-click the component > Print Report). Orbit/attitude checks use simulator time,
    /// so they are valid at any --speed; the spin-warning check reads what the viewer would see.
    /// </summary>
    public class EosatTestRecorder : MonoBehaviour
    {
        public TelemetryHub hub;
        public SpacecraftController spacecraft;

        // ---- recorded ----
        int frames;
        double firstUnix = -1, prevUnix;
        Vector3 prevPos;
        Quaternion prevQ;
        bool hasPrev, runComplete;
        float altMin = float.MaxValue, altMax = float.MinValue;
        float hudAltMin = float.MaxValue, hudAltMax = float.MinValue;
        double incSum, raanSinSum, raanCosSum, angleSum, timeSum;
        int geomCount;
        float maxBodyRate, maxBodyRateMet;
        float metAntenna = -1, metPyStart = -1, metPyDone = -1, metMyStart = -1, metMyDone = -1, metEclIn = -1, metEclOut = -1;
        bool lastEclipse; bool eclipseKnown;
        string lastStation = "";
        readonly List<string> contacts = new List<string>();
        float maxVisualSpin; bool sawSpinWarning;
        int stalls, recoveries; bool wasStalled; float longestStall;
        int good0, bad0, dup0, ign0, errors, exceptions;
        float startRealtime;

        public bool RunComplete => runComplete;
        public int Frames => frames;

        void Reset() { hub = GetComponent<TelemetryHub>(); spacecraft = FindAnyObjectByType<SpacecraftController>(); }

        void OnEnable()
        {
            if (!hub) hub = GetComponent<TelemetryHub>();
            if (!spacecraft) spacecraft = FindAnyObjectByType<SpacecraftController>();
            if (hub) hub.SampleReceived += OnSample;
            Application.logMessageReceived += OnLog;
            good0 = hub ? hub.GoodPackets : 0; bad0 = hub ? hub.BadPackets : 0;
            dup0 = hub ? hub.DuplicatePackets : 0; ign0 = hub ? hub.IgnoredPackets : 0;
            startRealtime = Time.time;
        }

        void OnDisable()
        {
            if (hub) hub.SampleReceived -= OnSample;
            Application.logMessageReceived -= OnLog;
        }

        void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Exception) exceptions++;
            else if (type == LogType.Error) errors++;
        }

        void Update()
        {
            if (!spacecraft || !spacecraft.HasData) return;
            maxVisualSpin = Mathf.Max(maxVisualSpin, spacecraft.VisualSpinDegPerSec);
            if (spacecraft.SpinWarning) sawSpinWarning = true;
            if (!runComplete)
            {
                float a = spacecraft.AltitudeKm;
                hudAltMin = Mathf.Min(hudAltMin, a); hudAltMax = Mathf.Max(hudAltMax, a);
            }
            bool st = spacecraft.Stalled;
            if (st) longestStall = Mathf.Max(longestStall, spacecraft.SecondsSinceData);
            if (st && !wasStalled) stalls++;
            if (!st && wasStalled) recoveries++;
            wasStalled = st;
        }

        void OnSample(TelemetrySample s)
        {
            if (runComplete || !s.hasTime) return;
            if (hasPrev && s.unixTime < prevUnix)          // stub looped back to the start
            {
                if (frames > 100) { runComplete = true; return; }
                hasPrev = false; firstUnix = -1;
            }
            if (firstUnix < 0) firstUnix = s.unixTime;
            frames++;
            float met = (float)(s.unixTime - firstUnix);

            float rE = s.earthRadiusKm > 0f ? s.earthRadiusKm : (float)EciMath.EarthRadiusKm;
            float alt = s.posEciKm.magnitude - rE;
            altMin = Mathf.Min(altMin, alt); altMax = Mathf.Max(altMax, alt);
            var q = new Quaternion(s.qx, s.qy, s.qz, s.qw);

            if (hasPrev)
            {
                double dt = s.unixTime - prevUnix;
                if (dt > 0 && dt < 120)
                {
                    Vector3 h = Vector3.Cross(prevPos, s.posEciKm);
                    if (h.sqrMagnitude > 1e-6f)
                    {
                        h.Normalize();
                        incSum += Mathf.Acos(Mathf.Clamp(h.z, -1f, 1f)) * Mathf.Rad2Deg;
                        float raan = Mathf.Atan2(h.x, -h.y);
                        raanSinSum += Mathf.Sin(raan); raanCosSum += Mathf.Cos(raan);
                        geomCount++;
                    }
                    angleSum += Vector3.Angle(prevPos, s.posEciKm);
                    timeSum += dt;
                    float rate = Quaternion.Angle(prevQ, q) / (float)dt;
                    if (rate > maxBodyRate) { maxBodyRate = rate; maxBodyRateMet = met; }
                }
            }

            if (metAntenna < 0 && s.antenna == AntennaState.Deployed) metAntenna = met;
            if (s.hasWings)
            {
                if (metPyStart < 0 && s.wingPyFraction > 0.001f) metPyStart = met;
                if (metPyDone < 0 && s.wingPyFraction >= 0.999f) metPyDone = met;
                if (metMyStart < 0 && s.wingMyFraction > 0.001f) metMyStart = met;
                if (metMyDone < 0 && s.wingMyFraction >= 0.999f) metMyDone = met;
            }
            if (s.hasEclipse)
            {
                if (eclipseKnown && s.eclipse && !lastEclipse && metEclIn < 0) metEclIn = met;
                if (eclipseKnown && !s.eclipse && lastEclipse && metEclOut < 0) metEclOut = met;
                lastEclipse = s.eclipse; eclipseKnown = true;
            }
            string station = s.inContact ? s.groundStation : "";
            if (station != lastStation)
            {
                contacts.Add($"{met:0} s {(station.Length > 0 ? station : "lost")}");
                lastStation = station;
            }

            prevPos = s.posEciKm; prevUnix = s.unixTime; prevQ = q; hasPrev = true;
        }

        static string Mark(bool pass) => pass ? "PASS" : "FAIL";

        [ContextMenu("Print Report")]
        public void PrintReport() => Debug.Log(Report());

        public string Report()
        {
            var sb = new StringBuilder();
            float inc = geomCount > 0 ? (float)(incSum / geomCount) : float.NaN;
            float raan = (float)(System.Math.Atan2(raanSinSum, raanCosSum) * Mathf.Rad2Deg); if (raan < 0) raan += 360f;
            float degPerSec = timeSum > 0 ? (float)(angleSum / timeSum) : 0f;
            float periodMin = degPerSec > 0 ? 360f / degPerSec / 60f : float.NaN;
            float revPerDay = degPerSec > 0 ? degPerSec * 86400f / 360f : float.NaN;
            float simSpan = (float)(prevUnix - firstUnix);

            sb.AppendLine($"Frames scored: {frames} ({simSpan / 60f:0.0} min of sim time){(runComplete ? ", full orbit" : "")} | accepted {hub.GoodPackets - good0}, rejected {hub.BadPackets - bad0}, duplicates dropped {hub.DuplicatePackets - dup0}, ignored {hub.IgnoredPackets - ign0}");
            sb.AppendLine($"1 Altitude: data {altMin:0.000}-{altMax:0.000} km, HUD {hudAltMin:0.0}-{hudAltMax:0.0} km -> {Mark(altMin > 500.4f && altMax < 501.4f && hudAltMin > 500.4f && hudAltMax < 501.4f)} (expect ~500.9, constant)");
            sb.AppendLine($"2 Orbit: inclination {inc:0.00} deg, RAAN {raan:0.0} deg, {revPerDay:0.00} rev/day, period {periodMin:0.0} min -> {Mark(Mathf.Abs(inc - 98f) < 0.5f && Mathf.Abs(Mathf.DeltaAngle(raan, 120f)) < 1.5f && Mathf.Abs(revPerDay - 15.24f) < 0.05f)} (expect 98 / 120 / 15.24 / 94.5)");
            sb.AppendLine($"3 Body rate: max {maxBodyRate:0.00} deg/s at MET {maxBodyRateMet:0} s -> {Mark(maxBodyRate > 2.4f && maxBodyRate < 3.0f)} (expect ~2.7, tumble only)");
            sb.AppendLine($"  Spin warning (>30 deg/s on screen): {(sawSpinWarning ? "TRIGGERED" : "never")}, max on-screen rate {maxVisualSpin:0.0} deg/s -> {Mark(!sawSpinWarning)}");
            bool order = metAntenna >= 0 && metPyStart > metAntenna && metMyStart > metAntenna;
            bool oneAtATime = metPyDone >= 0 && metMyStart >= metPyDone;
            float gap = metMyStart - metPyStart;
            sb.AppendLine($"5 Deploy: antenna {metAntenna:0} s, +Y wing {metPyStart:0}-{metPyDone:0} s, -Y wing {metMyStart:0}-{metMyDone:0} s (gap {gap:0} s) -> {Mark(order && oneAtATime && Mathf.Abs(gap - 100f) <= 10f)} (expect antenna first, one wing at a time, 100 s apart)");
            sb.AppendLine($"  Eclipse in {metEclIn:0} s / out {metEclOut:0} s (expect 1980 / 4000)");
            sb.AppendLine($"  Contacts: {string.Join(", ", contacts)}");
            sb.AppendLine($"6 Robustness: stalls flagged {stalls}, recovered {recoveries}, longest {longestStall:0.0} s, Unity errors {errors}, exceptions {exceptions}");
            return sb.ToString();
        }
    }
}
