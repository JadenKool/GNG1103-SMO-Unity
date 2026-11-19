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

        [Header("Antenna deployment bar (optional)")]
        [Tooltip("Image Type = Filled, Horizontal: fillAmount = antenna deployment.")]
        public Image antennaFill;
        public Text antennaText;
        [Tooltip("Used while the feed sends no antenna percentage: shows the animated deploy progress instead.")]
        public SatelliteDeployControl deploy;

        TelemetrySample last, prev;
        float velocityKmS = float.NaN;
        float nextRefresh;
        bool dirty = true;
        readonly StringBuilder sb = new StringBuilder(256);

        void OnEnable() { LocalizationManager.LanguageChanged += Relabel; if (hub) { hub.SampleReceived += OnSample; hub.SourceChanged += OnSourceChanged; } }
        void OnDisable() { LocalizationManager.LanguageChanged -= Relabel; if (hub) { hub.SampleReceived -= OnSample; hub.SourceChanged -= OnSourceChanged; } }
        void Relabel() { shownPct = -2f; dirty = true; nextRefresh = 0f; }

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
            UpdateAntennaBar();
            if (!dirty || !dataText || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 1f / refreshHz;
            dirty = false;

            if (last == null) { dataText.text = LocalizationManager.Tr("Waiting for telemetry..."); return; }

            float earthR = last.earthRadiusKm > 0f ? last.earthRadiusKm : (float)EciMath.EarthRadiusKm;
            sb.Clear();
            sb.Append("ALT   ").Append((last.posEciKm.magnitude - earthR).ToString("0.0")).Append(" km\n");
            sb.Append("VEL   ").Append(float.IsNaN(velocityKmS) ? "--" : velocityKmS.ToString("0.00") + " km/s").Append('\n');
            sb.Append("SUN   ").Append(!last.hasEclipse ? "--" : last.eclipse ? "<color=#FFB000>ECLIPSE</color>" : "SUNLIT").Append('\n');
            sb.Append("MODE  ").Append(Or(last.scMode)).Append("   AOCS ").Append(Or(last.aocsMode)).Append('\n');
            sb.Append("POINT ").Append(Or(last.pointing)).Append("   ").Append(Or(last.phase));
            if (last.inContact) sb.Append("\n<color=green>CONTACT</color> ").Append(Or(last.groundStation));
            dataText.text = LocalizationManager.Tr(sb.ToString());
        }

        float shownPct = -2f;
        void UpdateAntennaBar()
        {
            if (!antennaFill && !antennaText) return;
            // Feed's own percentage when it sends one (last != null means the startup buffer is over), else the animated model.
            bool fromFeed = last != null && last.hasAntennaFraction;
            float pct = fromFeed ? hub.AntennaPercent : deploy ? deploy.AntennaProgress * 100f : hub ? hub.AntennaPercent : -1f;
            if (Mathf.Abs(pct - shownPct) < 0.5f) return;
            shownPct = pct;
            if (antennaFill) antennaFill.fillAmount = Mathf.Clamp01(pct / 100f);
            if (antennaText) antennaText.text = LocalizationManager.Tr(pct < 0f ? "ANTENNA  --" : $"ANTENNA  {pct:0}%" + (fromFeed ? "" : "  <size=18><color=#9AA6B8>(state)</color></size>"));
        }

        static string Or(string s) => string.IsNullOrEmpty(s) ? "--" : s;
    }
}
