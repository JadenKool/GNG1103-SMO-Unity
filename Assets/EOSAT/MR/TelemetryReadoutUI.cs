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
            // Refresh on new samples, and at least once a second so link / power status updates when data stops.
            if (!dataText || Time.unscaledTime < nextRefresh || (!dirty && Time.unscaledTime < nextRefresh + 1f)) return;
            nextRefresh = Time.unscaledTime + 1f / refreshHz;
            dirty = false;

            if (last == null) { dataText.text = LocalizationManager.Tr("Waiting for telemetry..."); return; }

            // Glanceable only: no raw XYZ / quaternions on the live HUD (those live in the Replay panel).
            float earthR = last.earthRadiusKm > 0f ? last.earthRadiusKm : (float)EciMath.EarthRadiusKm;
            Vector2 ll = OrbitPredictor.LatLon(EciMath.EciToUnity(last.posEciKm), last.unixTime);
            sb.Clear();
            sb.Append("LAT ").Append(ll.x.ToString("0.0")).Append("°  LON ").Append(ll.y.ToString("0.0")).Append("°\n");
            sb.Append("ALT ").Append((last.posEciKm.magnitude - earthR).ToString("0")).Append(" km   ")
              .Append(float.IsNaN(velocityKmS) ? "" : velocityKmS.ToString("0.00") + " km/s   ")
              .Append(!last.hasEclipse ? "" : last.eclipse ? "<color=#FFB000>ECLIPSE</color>" : "SUNLIT").Append('\n');
            if (deploy && deploy.deployables)
            {
                var d = deploy.deployables;
                sb.Append("PANEL A ").Append(d.PanelAAngleDeg.ToString("0")).Append('°');
                if (!d.singlePanel) sb.Append("   B ").Append(d.PanelBAngleDeg.ToString("0")).Append('°');
                if (deploy.PanelAStuck || deploy.PanelBStuck) sb.Append("  <color=#FF6A6A>STUCK</color>");
                sb.Append('\n');
            }
            var p = hub ? hub.LatestPower : null;
            if (p != null)
                sb.Append("BATT ").Append(F(p.BatterySocPct, "0")).Append("%   TEMP ").Append(F(p.TemperatureC, "0.0")).Append(" °C\n");
            sb.Append("LINK ").Append(hub && hub.ReceivingData ? "<color=#39FF6A>LIVE</color>" : "<color=#FFB000>NO DATA</color>")
              .Append(hub && hub.IsNetworkSource ? (hub.CommandLinkConnected ? "   CMD <color=#39FF6A>UP</color>" : "   CMD <color=#FF6A6A>DOWN</color>") : "");
            if (last.inContact) sb.Append("\n<color=green>CONTACT</color> ").Append(Or(last.groundStation));
            dataText.text = LocalizationManager.Tr(sb.ToString());
        }

        float shownPct = -2f;
        void UpdateAntennaBar()
        {
            if (!antennaFill && !antennaText) return;
            // The half-wave dipole is binary: stowed or deployed (jammed shows red).
            bool jammed = deploy && deploy.deployables && deploy.deployables.Antenna == AntennaState.Jammed;
            float pct = deploy ? (deploy.AntennaDeployed ? 100f : 0f) : hub ? hub.AntennaPercent : -1f;
            if (jammed) pct = -3f;
            if (Mathf.Abs(pct - shownPct) < 0.5f) return;
            shownPct = pct;
            if (antennaFill) antennaFill.fillAmount = Mathf.Clamp01(pct / 100f);
            if (antennaText) antennaText.text = LocalizationManager.Tr(
                jammed ? "ANTENNA  <color=#FF6A6A>JAMMED</color>" : pct < 0f ? "ANTENNA  --" : pct >= 99f ? "ANTENNA  DEPLOYED" : "ANTENNA  STOWED");
        }

        static string Or(string s) => string.IsNullOrEmpty(s) ? "--" : s;
        static string F(float f, string fmt) => float.IsNaN(f) ? "--" : f.ToString(fmt);
    }
}
