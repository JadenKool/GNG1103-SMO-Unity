// DataTableUI.cs - live key / value table of the telemetry link, refreshed at 1 Hz.
// Two Text columns (keys, values) whose lines line up. Problems (no data, socket errors, rejected frames) show in red.

using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Eosat.MR
{
    public class DataTableUI : MonoBehaviour
    {
        public TelemetryHub hub;
        [Tooltip("Optional: antenna / panel state of the model.")]
        public SatelliteDeployControl deploy;
        public Text keysText;
        public Text valuesText;
        [Range(0.2f, 5f)] public float refreshHz = 1f;

        TelemetrySample last;
        float next;
        readonly StringBuilder k = new StringBuilder(512), v = new StringBuilder(1024);
        const string Bad = "#FF6A6A", Warn = "#FFB000", Ok = "#39FF6A";

        void OnEnable()
        {
            if (hub) { hub.SampleReceived += OnSample; hub.SourceChanged += OnSource; }
            LocalizationManager.LanguageChanged += Now;
            next = 0f;
        }
        void OnDisable()
        {
            if (hub) { hub.SampleReceived -= OnSample; hub.SourceChanged -= OnSource; }
            LocalizationManager.LanguageChanged -= Now;
        }
        void OnSample(TelemetrySample s) => last = s;
        void OnSource(DataSource _) { last = null; next = 0f; }
        void Now() => next = 0f;

        void Update()
        {
            if (Time.unscaledTime < next || !keysText || !valuesText) return;
            next = Time.unscaledTime + 1f / refreshHz;
            k.Clear(); v.Clear();
            if (!hub) { Row("Status", "No TelemetryHub", Bad); Flush(); return; }

            // ---- Connection ----
            string port = hub.source == DataSource.Tcp8100 ? $"TCP {hub.port}" : hub.source == DataSource.WebSocket8101 ? "WebSocket 8101" : "Simulated";
            Row("Source", port);
            Row("Host", hub.IsNetworkSource ? hub.host : "local");
            bool connected = hub.ReceivingData || hub.LinkConnected;
            Row("Status", connected ? "CONNECTED" : hub.IsNetworkSource ? "CONNECTING" : "WAITING", connected ? Ok : Warn);
            Row("Data state", hub.State.ToString().ToUpperInvariant(), DataLossOverlay.StateColor(hub.State));
            Row("Rate", $"{hub.PacketRateHz:0.0} Hz");
            Row("Packets", $"{hub.GoodPackets} good, {hub.BadPackets} rejected, {hub.DuplicatePackets} dup");
            Row("Buffer", hub.Buffering ? $"{hub.History.Count} frames, {hub.BufferSecondsLeft:0} s remaining" : $"{hub.History.Count} frames (last {hub.historySeconds:0} s)");
            string err = hub.IsNetworkSource ? hub.LinkError?.Replace("\0", "").Trim() : null;
            Row("Last error", string.IsNullOrEmpty(err) ? "none" : err, string.IsNullOrEmpty(err) ? null : Bad);
            bool recentReject = Time.time - hub.LastRejectRealtime < 30f;
            Row("Last reject", recentReject ? hub.LastRejectReason : "none", recentReject ? Warn : null);

            // ---- Latest frame ----
            if (last == null) Row("Sim time", hub.Buffering ? "buffering..." : "--");
            else
            {
                float rE = last.earthRadiusKm > 0f ? last.earthRadiusKm : (float)EciMath.EarthRadiusKm;
                Row("Sim time", last.hasTime ? EciMath.FormatUtc(last.unixTime) : "seq " + last.seq);
                Row("Position ECI", $"{last.posEciKm.x:0} {last.posEciKm.y:0} {last.posEciKm.z:0} km");
                Row("Altitude", $"{last.posEciKm.magnitude - rE:0.0} km");
                Row("Quaternion", $"w {last.qw:0.000}  x {last.qx:0.000}  y {last.qy:0.000}  z {last.qz:0.000}");
                Row("Eclipse", !last.hasEclipse ? "--" : last.eclipse ? "yes" : "no");
                Row("Mode", $"{Or(last.scMode)} / AOCS {Or(last.aocsMode)}");
                Row("Pointing", Or(last.pointing));
                Row("Phase", Or(last.phase));
                Row("Ground station", last.inContact ? Or(last.groundStation) : "none");
            }
            Row("Antenna deploy", hub.AntennaPercent < 0f ? "--" : $"{hub.AntennaPercent:0}%" + (last != null && last.hasAntennaFraction ? "" : " (state)"));
            if (deploy) Row("Solar panel", deploy.PanelsStatus);
            Flush();
        }

        void Row(string key, string value, string color = null)
        {
            k.Append(key).Append('\n');
            if (color != null) v.Append("<color=").Append(color).Append('>').Append(value).Append("</color>\n");
            else v.Append(value).Append('\n');
        }

        void Flush()
        {
            keysText.text = LocalizationManager.Tr(k.ToString());
            valuesText.text = LocalizationManager.Tr(v.ToString());
        }

        static string Or(string s) => string.IsNullOrEmpty(s) ? "--" : s;
    }
}
