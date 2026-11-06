using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Eosat.MR
{
    /// <summary>
    /// Live satellite readout on the tablet: Alt, Vel, Sun/Eclipse, modes, ground contact.
    /// Reads TelemetryHub.SampleReceived only (no parsing of its own). VEL = distance between the
    /// last two positions / simulator time between them.
    /// </summary>
    public class TelemetryReadoutUI : MonoBehaviour
    {
        public TelemetryHub hub;
        public Text dataText;
        [Range(1f, 20f)] public float refreshHz = 5f;

        TelemetrySample last, prev;
        float velocityKmS = float.NaN;
        float nextRefresh;
        bool dirty = true;
        readonly StringBuilder sb = new StringBuilder(256);

        void OnEnable() { if (hub) { hub.SampleReceived += OnSample; hub.SourceChanged += OnSourceChanged; } }
        void OnDisable() { if (hub) { hub.SampleReceived -= OnSample; hub.SourceChanged -= OnSourceChanged; } }

        void OnSourceChanged(DataSource _) { last = prev = null; velocityKmS = float.NaN; dirty = true; }

        void OnSample(TelemetrySample s)
        {
            prev = last;
            last = s;
            if (prev != null && prev.hasTime && s.hasTime)
            {
                double dt = s.unixTime - prev.unixTime;
                if (dt > 0.01 && dt < 600) velocityKmS = (float)((s.posEciKm - prev.posEciKm).magnitude / dt);
            }
            dirty = true;
        }

        void Update()
        {
            if (!dirty || !dataText || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 1f / refreshHz;
            dirty = false;

            if (last == null) { dataText.text = "Waiting for telemetry..."; return; }

            float earthR = last.earthRadiusKm > 0f ? last.earthRadiusKm : (float)EciMath.EarthRadiusKm;
            sb.Clear();
            sb.Append("ALT   ").Append((last.posEciKm.magnitude - earthR).ToString("0.0")).Append(" km\n");
            sb.Append("VEL   ").Append(float.IsNaN(velocityKmS) ? "--" : velocityKmS.ToString("0.00") + " km/s").Append('\n');
            sb.Append("SUN   ").Append(!last.hasEclipse ? "--" : last.eclipse ? "<color=#FFB000>ECLIPSE</color>" : "SUNLIT").Append('\n');
            sb.Append("MODE  ").Append(Or(last.scMode)).Append("   AOCS ").Append(Or(last.aocsMode)).Append('\n');
            sb.Append("POINT ").Append(Or(last.pointing)).Append("   ").Append(Or(last.phase));
            if (last.inContact) sb.Append("\n<color=green>CONTACT</color> ").Append(Or(last.groundStation));
            dataText.text = sb.ToString();
        }

        static string Or(string s) => string.IsNullOrEmpty(s) ? "--" : s;
    }
}
