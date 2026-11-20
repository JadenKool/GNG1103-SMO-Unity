// DataTableUI.cs - key / value table of the telemetry link, refreshed at 1 Hz.
// Two Text columns (keys, values) whose lines line up. Problems (no data, socket errors, rejected frames) show in red.
// Live (showRaw off): only glanceable values - lat / lon / alt, deployments, power, link and command status.
// Replay panel (showRaw on): adds raw ECI position, quaternion, every 8103 power field and the command log.

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
        [Tooltip("Show raw XYZ, quaternions, all power fields and the command log (Replay panel). Off for the live panels.")]
        public bool showRaw;
        [Tooltip("Command / anomaly log lines shown when showRaw is on.")]
        public int logLines = 12;

        TelemetrySample last;
        PowerSample lastPower;
        float next;
        readonly StringBuilder k = new StringBuilder(512), v = new StringBuilder(1024);
        const string Bad = "#FF6A6A", Warn = "#FFB000", Ok = "#39FF6A";

        void OnEnable()
        {
            if (hub) { hub.SampleReceived += OnSample; hub.SourceChanged += OnSource; hub.PowerReceived += OnPower; }
            LocalizationManager.LanguageChanged += Now;
            next = 0f;
        }
        void OnDisable()
        {
            if (hub) { hub.SampleReceived -= OnSample; hub.SourceChanged -= OnSource; hub.PowerReceived -= OnPower; }
            LocalizationManager.LanguageChanged -= Now;
        }
        void OnSample(TelemetrySample s) => last = s;
        void OnPower(PowerSample p) => lastPower = p;   // live or replayed reading
        void OnSource(DataSource _) { last = null; lastPower = null; next = 0f; }
        void Now() => next = 0f;

        void Update()
        {
            if (Time.unscaledTime < next || !keysText || !valuesText) return;
            next = Time.unscaledTime + 1f / refreshHz;
            k.Clear(); v.Clear();
            if (!hub) { Row("Status", "No TelemetryHub", Bad); Flush(); return; }

            // ---- Connection ----
            string port = hub.source == DataSource.Tcp8100 ? $"TCP {hub.port}" : hub.source == DataSource.WebSocket8101 ? "WebSocket 8101" : "Simulated";
            Row("Source", hub.ReplayMode ? "REPLAY" : port, hub.ReplayMode ? Warn : null);
            Row("Host", hub.IsNetworkSource ? hub.host : "local");
            bool connected = hub.ReceivingData || hub.LinkConnected;
            Row("Status", connected ? "CONNECTED" : hub.IsNetworkSource ? "CONNECTING" : "WAITING", connected ? Ok : Warn);
            Row("Data state", hub.State.ToString().ToUpperInvariant(), DataLossOverlay.StateColor(hub.State));
            if (hub.Degraded) Row("Data loss", hub.DegradedReason, Bad);
            Row("Rate", $"{hub.PacketRateHz:0.0} Hz   dropouts {hub.DropoutFraction * 100f:0}% / {hub.dropoutWindowSeconds:0} s");
            if (hub.IsNetworkSource)
            {
                Row("Power 8103", hub.PowerLinkConnected ? "CONNECTED" : "DOWN", hub.PowerLinkConnected ? Ok : Warn);
                Row("Commands 8104", hub.CommandLinkConnected ? "CONNECTED" : "DOWN", hub.CommandLinkConnected ? Ok : Warn);
            }
            if (showRaw)
            {
                Row("Packets", $"{hub.GoodPackets} good, {hub.BadPackets} rejected, {hub.DuplicatePackets} dup, {hub.PowerPackets} power");
                Row("Smoothing buffer", $"{hub.History.Count} frames (max {hub.historySeconds:0} s)");
            }
            string err = hub.IsNetworkSource ? hub.LinkError?.Replace("\0", "").Trim() : null;
            if (!string.IsNullOrEmpty(err)) Row("Last error", err, Bad);
            if (Time.time - hub.LastRejectRealtime < 30f) Row("Last reject", hub.LastRejectReason, Warn);

            // ---- Latest frame ----
            if (last == null) Row("Sim time", "--");
            else
            {
                float rE = last.earthRadiusKm > 0f ? last.earthRadiusKm : (float)EciMath.EarthRadiusKm;
                Row("Sim time", last.hasTime ? EciMath.FormatUtc(last.unixTime) : "seq " + last.seq);
                if (last.hasMet) Row("MET", FormatMet(last.metSeconds));
                Vector2 ll = OrbitPredictor.LatLon(EciMath.EciToUnity(last.posEciKm), last.unixTime);
                Row("Lat / Lon", $"{ll.x:0.00}°  {ll.y:0.00}°");
                Row("Altitude", $"{last.posEciKm.magnitude - rE:0.0} km");
                if (showRaw)
                {
                    Row("Position ECI", $"{last.posEciKm.x:0.0} {last.posEciKm.y:0.0} {last.posEciKm.z:0.0} km");
                    Row("Quaternion", $"w {last.qw:0.0000}  x {last.qx:0.0000}  y {last.qy:0.0000}  z {last.qz:0.0000}");
                    Row("Mode", $"{Or(last.scMode)} / AOCS {Or(last.aocsMode)}");
                    Row("Pointing", Or(last.pointing));
                    Row("Phase", Or(last.phase));
                }
                Row("Eclipse", !last.hasEclipse ? "--" : last.eclipse ? "yes" : "no");
                Row("Ground station", last.inContact ? Or(last.groundStation) : "none");
            }

            // ---- Deployables ----
            if (deploy)
            {
                Row("Panel A", deploy.PanelAStatus, deploy.PanelAStuck ? Bad : null);
                if (deploy.deployables && !deploy.deployables.singlePanel) Row("Panel B", deploy.PanelBStatus, deploy.PanelBStuck ? Bad : null);
                Row("Antenna", deploy.AntennaStatus);
            }
            else Row("Antenna", hub.AntennaPercent < 0f ? "--" : hub.AntennaPercent >= 99f ? "DEPLOYED" : "STOWED");

            // ---- Power (8103) ----
            var p = lastPower;
            if (p == null) Row("Power", hub.IsNetworkSource ? "no data on 8103" : "--");
            else
            {
                Row("Battery", $"{Num(p.BatterySocPct, "0")} %   {Num(p.BatteryVoltage, "0.00")} V");
                Row("Temperature", $"{Num(p.TemperatureC, "0.0")} °C");
                Row("Bus voltage", $"{Num(p.BusVoltage, "0.00")} V");
                Row("Power gen / use", $"{Num(p.PowerGeneratedW, "0.0")} W / {Num(p.PowerConsumedW, "0.0")} W");
                string shed = p.LoadShed;
                if (shed != null) Row("Load shed", shed, shed == "0" || shed.ToLowerInvariant() == "false" || shed.ToLowerInvariant() == "none" ? null : Warn);
                if (showRaw)
                {
                    Row("Panel A / B power", $"{Num(p.PanelPowerW("a"), "0.0")} W / {Num(p.PanelPowerW("b"), "0.0")} W");
                    foreach (var kv in p.fields) Row("  " + kv.Key, kv.Value);
                }
            }

            // ---- Command / anomaly log ----
            if (showRaw)
            {
                int shown = 0;
                for (int i = hub.SessionLog.Count - 1; i >= 0 && shown < logLines; i--)
                {
                    var e = hub.SessionLog[i];
                    if (e.kind == LogKind.Sample || e.kind == LogKind.Power) continue;
                    Row($"{e.kind} -{Time.time - e.t:0}s", e.text, e.IsError ? Bad : e.kind == LogKind.Command ? Warn : null);
                    shown++;
                }
                if (shown == 0) Row("Log", "no commands or anomalies");
            }
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
        static string Num(float f, string fmt) => float.IsNaN(f) ? "--" : f.ToString(fmt);

        public static string FormatMet(double s)
        {
            if (s < 0) return "--";
            long t = (long)s;
            return $"{t / 86400}d {t / 3600 % 24:00}:{t / 60 % 60:00}:{t % 60:00}";
        }
    }
}
