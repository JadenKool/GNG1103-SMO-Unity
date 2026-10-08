using System;
using UnityEngine;

namespace Eosat
{
    public enum PanelState { Unknown, Stowed, Deployed }
    public enum AntennaState { Unknown, Stowed, Deployed, Jammed }

    /// <summary>One validated telemetry update.</summary>
    public class TelemetrySample
    {
        public bool hasTime;
        public double unixTime;
        public Vector3 posEciKm;
        public float qw, qx, qy, qz;      // body -> ECI attitude quaternion
        public PanelState panels;
        public AntennaState antenna;
        public bool hasEclipse;
        public bool eclipse;
        public float receivedRealtime;
    }

    /// <summary>
    /// Wire format (one JSON object per message, newline-delimited or back-to-back):
    /// {"t": 1791460000.0, "pos_eci_km": [x, y, z], "q": [w, x, y, z],
    ///  "panels": "deployed", "antenna": "stowed", "eclipse": false}
    /// Only pos_eci_km and q are required. Change field names here once the client's test tool format is known.
    /// </summary>
    [Serializable]
    public class TelemetryJson
    {
        public double t;
        public float[] pos_eci_km;
        public float[] q;
        public string panels;
        public string antenna;
        public bool eclipse;
    }

    public static class TelemetryParser
    {
        public static bool TryParse(string json, bool quaternionScalarFirst, out TelemetrySample sample, out string error)
        {
            sample = null;
            error = null;

            TelemetryJson j;
            try { j = JsonUtility.FromJson<TelemetryJson>(json); }
            catch (Exception e) { error = "unreadable JSON (" + e.Message + ")"; return false; }
            if (j == null) { error = "empty message"; return false; }

            if (j.pos_eci_km == null || j.pos_eci_km.Length < 3) { error = "missing pos_eci_km"; return false; }
            var p = new Vector3(j.pos_eci_km[0], j.pos_eci_km[1], j.pos_eci_km[2]);
            if (!EciMath.IsFinite(p)) { error = "position is not a number"; return false; }
            float r = p.magnitude;
            if (r < EciMath.EarthRadiusKm * 0.98 || r > 100000f) { error = $"position radius {r:F0} km out of range"; return false; }

            if (j.q == null || j.q.Length < 4) { error = "missing q"; return false; }
            float w, x, y, z;
            if (quaternionScalarFirst) { w = j.q[0]; x = j.q[1]; y = j.q[2]; z = j.q[3]; }
            else { x = j.q[0]; y = j.q[1]; z = j.q[2]; w = j.q[3]; }
            float n = Mathf.Sqrt(w * w + x * x + y * y + z * z);
            if (float.IsNaN(n) || n < 0.9f || n > 1.1f) { error = $"quaternion norm {n:F3} is not ~1"; return false; }

            sample = new TelemetrySample
            {
                hasTime = j.t > 0,
                unixTime = j.t,
                posEciKm = p,
                qw = w / n, qx = x / n, qy = y / n, qz = z / n,
                panels = ParsePanels(j.panels),
                antenna = ParseAntenna(j.antenna),
                hasEclipse = json.Contains("\"eclipse\""),
                eclipse = j.eclipse
            };
            return true;
        }

        static PanelState ParsePanels(string s)
        {
            if (string.IsNullOrEmpty(s)) return PanelState.Unknown;
            s = s.ToLowerInvariant();
            if (s.Contains("deploy")) return PanelState.Deployed;
            if (s.Contains("stow")) return PanelState.Stowed;
            return PanelState.Unknown;
        }

        static AntennaState ParseAntenna(string s)
        {
            if (string.IsNullOrEmpty(s)) return AntennaState.Unknown;
            s = s.ToLowerInvariant();
            if (s.Contains("jam")) return AntennaState.Jammed;
            if (s.Contains("deploy")) return AntennaState.Deployed;
            if (s.Contains("stow")) return AntennaState.Stowed;
            return AntennaState.Unknown;
        }
    }
}
