using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Eosat
{
    /// <summary>Where telemetry comes from. Values 0/1 match the old Simulated/Tcp so saved scenes keep their choice.</summary>
    public enum DataSource { SimulatedLocal = 0, Tcp8100 = 1, WebSocket8101 = 2 }
    public enum DataState { Waiting, Live, Delayed, Ghost, Lost }
    public enum LogKind { Sample, Power, Reject, Gap, Command, CommandReply, Anomaly }

    /// <summary>A background network feed that hands complete JSON strings to the main thread.</summary>
    public interface ITelemetryClient
    {
        bool Connected { get; }
        string LastError { get; }
        string Label { get; }
        void Start();
        void Disconnect();
        bool TryDequeue(out string json);
    }

    /// <summary>One entry of the session log that Replay Mode scrubs through.</summary>
    public class LogEntry
    {
        public float t;                 // Time.time when it happened
        public LogKind kind;
        public TelemetrySample sample;  // kind == Sample
        public PowerSample power;       // kind == Power
        public string text;             // reject reason, command, anomaly...
        /// <summary>Shown as a blue marker on the replay timeline.</summary>
        public bool IsError => kind == LogKind.Reject || kind == LogKind.Gap || kind == LogKind.Anomaly;
    }

    /// <summary>
    /// Single entry point for telemetry and traffic controller for the sources and ports:
    ///   8100 TCP / 8101 WebSocket  main VR state stream (position, attitude, deployables)
    ///   8103 WebSocket             power stream (battery, temperature, bus voltage, panel power...) - network sources only
    ///   8104 WebSocket             command back-channel (deploy commands queued to the Flight Commander)
    /// SimulatedLocal = SimulatedSpacecraft / SampleReplayFeed call Submit() themselves (they check `source`).
    /// No startup buffer: the scene boots live. At most 7 s of frames are kept for smoothing; SpacecraftController
    /// extrapolates between packets at frame rate, so the model never trails the feed by more than that.
    /// Catch system: dropouts / a slow feed set Degraded (screens dim) while propagation continues.
    /// </summary>
    public class TelemetryHub : MonoBehaviour
    {
        public const float MaxBufferSeconds = 7f;
        public const int PowerPort = 8103, CommandPort = 8104;

        [Tooltip("Active source at start. Switch at runtime with SwitchDataSource().")]
        public DataSource source = DataSource.SimulatedLocal;

        [Header("TCP feed")]
        public string host = "127.0.0.1";
        [Tooltip("Main TCP stream: 8100")]
        public int port = 8100;

        [Header("WebSocket feed (live simulation, 8101 / ops server)")]
        public string webSocketUri = "wss://ops.cgo.live/VR";
        [Tooltip("Power / battery / thermal stream. Empty = same host and path as the main feed on port 8103.")]
        public string powerUriOverride = "";
        [Tooltip("Command back-channel. Empty = same host and path as the main feed on port 8104.")]
        public string commandUriOverride = "";

        [Header("Parsing")]
        [Tooltip("Legacy flat format only: true if q arrives as [w, x, y, z]; false for [x, y, z, w]. " +
                 "The client's smo-vr/1 format is always [x, y, z, w] and is detected automatically.")]
        public bool quaternionScalarFirst = true;
        [Tooltip("Most queued messages handled per frame (stops a backlog from causing a hitch).")]
        public int maxMessagesPerFrame = 100;
        [Tooltip("No sample for this long = not receiving (Prediction Mode label).")]
        public float staleAfterSeconds = 3f;

        [Header("Buffer (7 s maximum)")]
        [Range(1f, MaxBufferSeconds)]
        [Tooltip("Rolling cache of received frames used for smoothing. Hard cap 7 s.")]
        public float historySeconds = MaxBufferSeconds;

        [Header("Catch system (dims the screens for comfort)")]
        public float minRateHz = 1f;
        [Tooltip("Jitter allowance on minRateHz: a nominal 1 Hz feed measures 0.95-1.0 Hz over 20 s.")]
        [Range(0f, 0.5f)] public float rateTolerance = 0.15f;
        public float dropoutWindowSeconds = 20f;
        [Range(0f, 1f)] public float maxDropoutFraction = 0.6f;
        public float maxGapSeconds = MaxBufferSeconds;
        [Tooltip("Seconds of healthy feed before the screens brighten again (no flicker).")]
        public float recoverSeconds = 3f;

        [Header("Session log (Replay Mode)")]
        public int maxLogEntries = 20000;

        public event Action<TelemetrySample> SampleReceived;
        public event Action<PowerSample> PowerReceived;
        public event Action<DataSource> SourceChanged;
        /// <summary>Raised once when the catch system trips (screens dim, propagation continues).</summary>
        public event Action DataLost;
        /// <summary>Raised when the feed has been healthy again for recoverSeconds.</summary>
        public event Action DataRestored;
        /// <summary>Raised after SetCustomHost changed the address.</summary>
        public event Action<string> HostChanged;
        /// <summary>Replay jumped / live resumed: followers must snap to the next sample instead of gliding.</summary>
        public event Action Resynced;

        /// <summary>Frames received in the last historySeconds (max 7 s), oldest first.</summary>
        public IReadOnlyCollection<TelemetrySample> History => history;
        public IReadOnlyList<LogEntry> SessionLog => log;
        /// <summary>Antenna deployment 0-100 % from the feed (its fraction if sent, else 0 / 100 from the state); -1 = unknown.</summary>
        public float AntennaPercent { get; private set; } = -1f;
        public PowerSample LatestPower { get; private set; }
        public TelemetrySample LatestLive { get; private set; }

        /// <summary>True once any sample has ever arrived (so "lost" is not shown before the first connection).</summary>
        public bool HasEverReceived { get; private set; }
        /// <summary>Data was flowing and has stopped: the spacecraft is being shown from prediction.</summary>
        public bool PredictionMode => HasEverReceived && !ReceivingData && !ReplayMode;
        /// <summary>Seconds since the last valid sample (0 before the first one).</summary>
        public float SecondsSinceData => HasEverReceived ? Mathf.Max(0f, Time.time - LastSampleRealtime) : 0f;

        /// <summary>Catch system tripped: rate below minRateHz, &gt;60 % dropouts in 20 s, or &gt;7 s without data.</summary>
        public bool Degraded { get; private set; }
        public string DegradedReason { get; private set; }
        /// <summary>Share of the last 20 one-second slots with no packet.</summary>
        public float DropoutFraction { get; private set; }

        /// <summary>Data states from the Deliverable D/E design: LIVE &lt; 2 s, DELAYED 2-5 s, GHOST &gt; 5 s, LOST &gt; 60 s.</summary>
        public DataState State
        {
            get
            {
                if (!HasEverReceived) return DataState.Waiting;
                float s = SecondsSinceData;
                return s < 2f ? DataState.Live : s <= 5f ? DataState.Delayed : s <= 60f ? DataState.Ghost : DataState.Lost;
            }
        }
        /// <summary>Why the last SetCustomHost call was refused (null if it was accepted).</summary>
        public string LastHostError { get; private set; }

        public int GoodPackets { get; private set; }
        public int BadPackets { get; private set; }
        public int IgnoredPackets { get; private set; }
        public int DuplicatePackets { get; private set; }
        public int PowerPackets { get; private set; }
        public string LastRejectReason { get; private set; }
        public float LastRejectRealtime { get; private set; } = -100f;
        public float LastSampleRealtime { get; private set; } = -100f;
        /// <summary>Packets per second over the catch window (up to 20 s).</summary>
        public float PacketRateHz { get; private set; }

        /// <summary>Socket is open (TCP connected / WebSocket state Open). Always false for SimulatedLocal.</summary>
        public bool LinkConnected => client != null && client.Connected;
        public string LinkError => client?.LastError;
        public bool PowerLinkConnected => powerClient != null && powerClient.Connected;
        public bool CommandLinkConnected => commandClient != null && commandClient.Connected;
        /// <summary>Valid samples arrived within staleAfterSeconds.</summary>
        public bool ReceivingData => Time.time - LastSampleRealtime <= staleAfterSeconds;
        public bool IsNetworkSource => source != DataSource.SimulatedLocal;
        public bool ReplayMode { get; private set; }

        // Older names kept so existing scripts compile unchanged.
        public bool TcpConnected => LinkConnected;
        public string TcpError => LinkError;

        /// <summary>Exactly what is active, e.g. "TCP: 127.0.0.1:8100", "WebSocket: ops.cgo.live/VR", "Simulated (local)".</summary>
        public string SourceLabel => source switch
        {
            DataSource.Tcp8100 => $"TCP: {host}:{port}",
            DataSource.WebSocket8101 => "WebSocket: " + ShortUri(webSocketUri),
            _ => "Simulated (local)"
        };

        public string PowerUri => string.IsNullOrEmpty(powerUriOverride) ? Derived(PowerPort) : powerUriOverride;
        public string CommandUri => string.IsNullOrEmpty(commandUriOverride) ? Derived(CommandPort) : commandUriOverride;

        ITelemetryClient client;
        WebSocketJsonClient powerClient, commandClient;
        readonly Queue<TelemetrySample> history = new Queue<TelemetrySample>();
        readonly List<LogEntry> log = new List<LogEntry>();
        readonly Queue<float> stamps = new Queue<float>();
        bool[] slots = new bool[20];
        float windowStart = -1f, healthySince = -1f, lastGoodRealtime = -100f;
        long lastSeq = -1;
        int commandSeq;

        void OnEnable() => StartClient();
        void OnDisable() => StopClient();
        void OnApplicationQuit() => StopClient();
        void OnValidate() => historySeconds = Mathf.Clamp(historySeconds, 1f, MaxBufferSeconds);

        /// <summary>
        /// Safely disconnects the current network clients (if any), resets per-feed state and starts the new source.
        /// Simulated feeds pause/resume by themselves because they check `source` before every Submit.
        /// </summary>
        public void SwitchDataSource(DataSource newSource)
        {
            if (newSource == source && (newSource == DataSource.SimulatedLocal || client != null)) return;
            StopClient();
            source = newSource;
            ResetFeedState();
            if (isActiveAndEnabled) StartClient();
            Debug.Log("[EOSAT] Telemetry source -> " + SourceLabel);
            SourceChanged?.Invoke(newSource);
        }

        /// <summary>For UI events that pass an int (Dropdown etc.).</summary>
        public void SwitchDataSource(int newSource) => SwitchDataSource((DataSource)Mathf.Clamp(newSource, 0, 2));

        /// <summary>
        /// Point the feeds at another server. Accepts "192.168.1.20", "192.168.1.20:8101", "8.232.20.196/VR",
        /// "ws://host:8101/VR" or "wss://ops.cgo.live/VR". A path keeps (or replaces) the WebSocket path.
        /// Main-stream ports: 8100 (TCP) or 8101 (WebSocket); wss may omit the port. If a network source is active
        /// it reconnects straight away. Returns false (see LastHostError) for bad input.
        /// </summary>
        public bool SetCustomHost(string address)
        {
            LastHostError = null;
            if (!TryParseAddress(address, webSocketUri, out string newHost, out int tcpPort, out string wsUri, out string error))
            { LastHostError = error; return false; }
            host = newHost;
            if (tcpPort > 0) port = tcpPort;
            webSocketUri = wsUri;

            if (IsNetworkSource)
            {
                StopClient();
                ResetFeedState();
                if (isActiveAndEnabled) StartClient();
            }
            Debug.Log("[EOSAT] Telemetry host -> " + host + " (" + SourceLabel + ")");
            HostChanged?.Invoke(host);
            return true;
        }

        /// <summary>Pure parser behind SetCustomHost (tested by EosatParserTest). tcpPort = 8100 when given, else -1.</summary>
        public static bool TryParseAddress(string address, string currentWsUri, out string host, out int tcpPort, out string wsUri, out string error)
        {
            host = null; tcpPort = -1; wsUri = null; error = null;
            address = (address ?? "").Trim();
            string scheme = "ws";
            if (address.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)) { scheme = "wss"; address = address.Substring(6); }
            else if (address.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)) address = address.Substring(5);

            string path = "/VR";
            try { path = new Uri(currentWsUri).AbsolutePath; } catch { }
            int slash = address.IndexOf('/');
            if (slash >= 0) { if (slash < address.Length - 1) path = address.Substring(slash); address = address.Substring(0, slash); }

            string newHost = address; int newPort = -1;
            int colon = address.LastIndexOf(':');
            if (colon > 0)
            {
                newHost = address.Substring(0, colon);
                if (!int.TryParse(address.Substring(colon + 1), out newPort)) { error = "Port must be a number"; return false; }
                bool okPort = newPort == 8100 || newPort == 8101 || (scheme == "wss" && newPort == 443);
                if (!okPort) { error = "Port must be 8100 (TCP) or 8101 (WebSocket)"; return false; }
            }
            if (newHost.Length == 0) { error = "Type an IP address first"; return false; }
            if (Uri.CheckHostName(newHost) == UriHostNameType.Unknown) { error = "Not a valid IP address or host name"; return false; }
            // Four dot-separated numbers must each be 0-255 (Uri accepts things like "300.1.1.1" as a DNS name).
            var parts = newHost.Split('.');
            if (parts.Length == 4 && Array.TrueForAll(parts, p => int.TryParse(p, out _)) && !Array.TrueForAll(parts, p => int.Parse(p) <= 255))
            { error = "Each IP number must be 0-255"; return false; }

            host = newHost;
            if (newPort == 8100) tcpPort = 8100;
            int wsPort = newPort == 8101 || newPort == 443 ? newPort : scheme == "wss" ? -1 : 8101;
            wsUri = wsPort > 0 ? $"{scheme}://{host}:{wsPort}{path}" : $"{scheme}://{host}{path}";
            return true;
        }

        string Derived(int p)
        {
            try
            {
                var u = new Uri(webSocketUri);
                string h = source == DataSource.Tcp8100 ? host : u.Host;
                return $"{u.Scheme}://{h}:{p}{u.AbsolutePath}";
            }
            catch { return $"ws://{host}:{p}/VR"; }
        }

        static string ShortUri(string uri)
        {
            try { var u = new Uri(uri); return u.IsDefaultPort ? u.Host + u.AbsolutePath : $"{u.Host}:{u.Port}{u.AbsolutePath}"; }
            catch { return uri; }
        }

        void StartClient()
        {
            if (client != null) return;
            switch (source)
            {
                case DataSource.Tcp8100: client = new TcpJsonClient(host, port); break;
                case DataSource.WebSocket8101: client = new WebSocketJsonClient(webSocketUri); break;
                default: return;   // SimulatedLocal: no client
            }
            client.Start();
            try { powerClient = new WebSocketJsonClient(PowerUri); powerClient.Start(); }
            catch (Exception e) { powerClient = null; Debug.LogWarning("[EOSAT] Power stream (8103) not started: " + e.Message); }
            try { commandClient = new WebSocketJsonClient(CommandUri); commandClient.Start(); }
            catch (Exception e) { commandClient = null; Debug.LogWarning("[EOSAT] Command link (8104) not started: " + e.Message); }
        }

        void StopClient()
        {
            var c = client; var p = powerClient; var k = commandClient;
            client = null; powerClient = null; commandClient = null;   // Update() stops reading from them immediately
            c?.Disconnect(); p?.Disconnect(); k?.Disconnect();       // close sockets, end loops, clear queues
        }

        void ResetFeedState()
        {
            lastSeq = -1;           // a new feed restarts its seq counter; don't flag its first frame as a duplicate
            stamps.Clear();
            windowStart = -1f;
            PacketRateHz = 0f;
            LastSampleRealtime = -100f;
        }

        void Update()
        {
            int n = 0;
            while (client != null && n++ < maxMessagesPerFrame && client.TryDequeue(out var json)) Submit(json);
            n = 0;
            while (powerClient != null && n++ < maxMessagesPerFrame && powerClient.TryDequeue(out var json)) SubmitPower(json);
            n = 0;
            while (commandClient != null && n++ < maxMessagesPerFrame && commandClient.TryDequeue(out var json)) Log(LogKind.CommandReply, "8104: " + json);
            UpdateCatch(Time.time);
        }

        // ---------------- Catch system ----------------
        void UpdateCatch(float now)
        {
            while (stamps.Count > 0 && now - stamps.Peek() > dropoutWindowSeconds) stamps.Dequeue();
            float window = windowStart < 0f ? 0f : Mathf.Min(dropoutWindowSeconds, now - windowStart);
            PacketRateHz = window >= 1f ? stamps.Count / window : stamps.Count;

            int buckets = Mathf.Clamp(Mathf.FloorToInt(window), 1, Mathf.CeilToInt(dropoutWindowSeconds));
            if (slots.Length < buckets) slots = new bool[buckets];
            Array.Clear(slots, 0, slots.Length);
            foreach (float t in stamps) { int i = (int)(now - t); if (i < buckets) slots[i] = true; }
            int empty = 0;
            for (int i = 0; i < buckets; i++) if (!slots[i]) empty++;
            DropoutFraction = empty / (float)buckets;

            string reason = null;
            if (HasEverReceived && !ReplayMode)
            {
                float gap = now - lastGoodRealtime;     // survives a source switch, so a dead new source still trips it
                bool fullWindow = window >= dropoutWindowSeconds - 0.01f;
                if (gap > maxGapSeconds) reason = $"no telemetry for {gap:0} s";
                else if (fullWindow && DropoutFraction > maxDropoutFraction) reason = $"dropouts in {DropoutFraction * 100f:0}% of the last {dropoutWindowSeconds:0} s";
                else if (fullWindow && PacketRateHz < minRateHz * (1f - rateTolerance)) reason = $"feed at {PacketRateHz:0.00} Hz (below {minRateHz:0} Hz)";
            }

            if (reason != null)
            {
                healthySince = -1f;
                DegradedReason = reason;
                if (!Degraded)
                {
                    Degraded = true;
                    Log(LogKind.Gap, "Catch system: " + reason);
                    Debug.LogWarning("[EOSAT] Telemetry degraded - " + reason + " (" + SourceLabel + "); screens dimmed, propagating");
                    DataLost?.Invoke();
                }
            }
            else if (Degraded)
            {
                if (healthySince < 0f) healthySince = now;
                if (now - healthySince >= recoverSeconds)
                {
                    Degraded = false; DegradedReason = null;
                    Debug.Log("[EOSAT] Telemetry restored (" + SourceLabel + ")");
                    DataRestored?.Invoke();
                }
            }
        }

        // ---------------- Main stream ----------------
        /// <summary>Feed one raw JSON message into the pipeline.</summary>
        public void Submit(string json)
        {
            if (TelemetryParser.TryParse(json, quaternionScalarFirst, out var sample, out var error, out bool ignored))
            {
                // Same seq twice in a row = duplicate frame (the stub's --chaos mode sends these). Drop it.
                if (sample.seq >= 0 && sample.seq == lastSeq) { DuplicatePackets++; return; }
                lastSeq = sample.seq;
                float now = Time.time;
                sample.receivedRealtime = now;
                LastSampleRealtime = now;
                lastGoodRealtime = now;
                if (windowStart < 0f) windowStart = now;
                HasEverReceived = true;
                GoodPackets++;
                stamps.Enqueue(now);
                history.Enqueue(sample);
                while (history.Count > 0 && now - history.Peek().receivedRealtime > Mathf.Min(historySeconds, MaxBufferSeconds)) history.Dequeue();
                if (sample.hasAntennaFraction) AntennaPercent = sample.antennaFraction * 100f;
                else if (sample.antenna == AntennaState.Deployed) AntennaPercent = 100f;
                else if (sample.antenna == AntennaState.Stowed || sample.antenna == AntennaState.Jammed) AntennaPercent = 0f;
                LatestLive = sample;
                AddLog(new LogEntry { t = now, kind = LogKind.Sample, sample = sample });
                if (!ReplayMode) SampleReceived?.Invoke(sample);
            }
            else if (ignored)
            {
                // Valid message with no state (e.g. smo-vr/1 "hello"): not an error.
                IgnoredPackets++;
                if (json.Contains("\"hello\"")) Debug.Log($"[EOSAT] Connected to simulator ({SourceLabel}): " + json);
            }
            else
            {
                BadPackets++;
                LastRejectReason = error;
                LastRejectRealtime = Time.time;
                Log(LogKind.Reject, "Rejected frame: " + error);
                Debug.LogWarning("[EOSAT] Rejected telemetry: " + error);
            }
        }

        // ---------------- Power stream (8103) ----------------
        public void SubmitPower(string json)
        {
            var p = new PowerSample { receivedRealtime = Time.time };
            if (!FlatJson.TryFlatten(json, p.fields, out var error)) { Log(LogKind.Reject, "Rejected power frame: " + error); return; }
            PowerPackets++;
            LatestPower = p;
            AddLog(new LogEntry { t = p.receivedRealtime, kind = LogKind.Power, power = p });
            if (!ReplayMode) PowerReceived?.Invoke(p);
        }

        // ---------------- Command back-channel (8104) ----------------
        /// <summary>
        /// Sends a command to the 8104 back-channel, addressed to the Flight Commander for approval.
        /// Nothing in the scene changes here: the model only moves once telemetry shows the result.
        /// shortcut: message shape is ours (no 8104 schema yet) - {"type":"command","command":...,"approval":"FLIGHT_COMMANDER"}.
        /// Returns false if the link is not open (local simulation, or the socket is down).
        /// </summary>
        public bool SendCommand(string command)
        {
            int id = ++commandSeq;
            string utc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            string json = $"{{\"type\":\"command\",\"schema\":\"smo-vr/1\",\"id\":{id},\"command\":\"{command}\",\"approval\":\"FLIGHT_COMMANDER\",\"source\":\"VR\",\"utc\":\"{utc}\"}}";
            bool sent = commandClient != null && commandClient.Send(json);
            Log(LogKind.Command, $"{command} -> Flight Commander " + (sent ? "(sent on 8104)" : "(NOT sent: command link down)"));
            return sent;
        }

        /// <summary>Anything the scene wants a blue replay marker for (stuck panel, jammed antenna...).</summary>
        public void LogAnomaly(string text) => Log(LogKind.Anomaly, text);

        void Log(LogKind kind, string text) => AddLog(new LogEntry { t = Time.time, kind = kind, text = text });

        void AddLog(LogEntry e)
        {
            log.Add(e);
            if (log.Count > maxLogEntries) log.RemoveRange(0, log.Count - maxLogEntries);
        }

        // ---------------- Replay Mode ----------------
        /// <summary>Replay on: live frames are still logged but no longer drive the scene. Off: snap back to live.</summary>
        public void SetReplayMode(bool on)
        {
            if (on == ReplayMode) return;
            ReplayMode = on;
            Resynced?.Invoke();
            if (!on && LatestLive != null) SampleReceived?.Invoke(LatestLive);
        }

        /// <summary>Replay: show a logged frame. jump = scrubbed (snap), false = normal playback (glide).</summary>
        public void EmitReplay(TelemetrySample s, bool jump)
        {
            if (!ReplayMode || s == null) return;
            if (jump) Resynced?.Invoke();
            SampleReceived?.Invoke(s);
        }

        public void EmitReplayPower(PowerSample p) { if (ReplayMode && p != null) PowerReceived?.Invoke(p); }
    }
}
