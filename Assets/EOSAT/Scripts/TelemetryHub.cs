using System;
using System.Collections.Generic;
using UnityEngine;

namespace Eosat
{
    /// <summary>Where telemetry comes from. Values 0/1 match the old Simulated/Tcp so saved scenes keep their choice.</summary>
    public enum DataSource { SimulatedLocal = 0, Tcp8100 = 1, WebSocket8101 = 2 }

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

    /// <summary>
    /// Single entry point for telemetry and traffic controller for the three sources.
    /// SimulatedLocal = SimulatedSpacecraft / SampleReplayFeed call Submit() themselves (they check `source`).
    /// Tcp8100 / WebSocket8101 = one network client at a time; Update() drains only that client's queue.
    /// Every message goes through the same parser + validator whatever the source.
    /// </summary>
    public class TelemetryHub : MonoBehaviour
    {
        [Tooltip("Active source at start. Switch at runtime with SwitchDataSource().")]
        public DataSource source = DataSource.SimulatedLocal;

        [Header("TCP feed")]
        public string host = "127.0.0.1";
        [Tooltip("Allowed ports per target specs: 8100-8101")]
        public int port = 8100;

        [Header("WebSocket feed")]
        public string webSocketUri = "ws://127.0.0.1:8101/vr";

        [Header("Parsing")]
        [Tooltip("Legacy flat format only: true if q arrives as [w, x, y, z]; false for [x, y, z, w]. " +
                 "The client's smo-vr/1 format is always [x, y, z, w] and is detected automatically.")]
        public bool quaternionScalarFirst = true;
        [Tooltip("Most queued messages handled per frame (stops a backlog from causing a hitch).")]
        public int maxMessagesPerFrame = 100;
        [Tooltip("No sample for this long = not receiving.")]
        public float staleAfterSeconds = 3f;

        public event Action<TelemetrySample> SampleReceived;
        public event Action<DataSource> SourceChanged;

        public int GoodPackets { get; private set; }
        public int BadPackets { get; private set; }
        public int IgnoredPackets { get; private set; }
        public int DuplicatePackets { get; private set; }
        public string LastRejectReason { get; private set; }
        public float LastRejectRealtime { get; private set; } = -100f;
        public float LastSampleRealtime { get; private set; } = -100f;
        public float PacketRateHz { get; private set; }

        /// <summary>Socket is open (TCP connected / WebSocket state Open). Always false for SimulatedLocal.</summary>
        public bool LinkConnected => client != null && client.Connected;
        public string LinkError => client?.LastError;
        /// <summary>Valid samples arrived within staleAfterSeconds.</summary>
        public bool ReceivingData => Time.time - LastSampleRealtime <= staleAfterSeconds;
        public bool IsNetworkSource => source != DataSource.SimulatedLocal;

        // Older names kept so existing scripts compile unchanged.
        public bool TcpConnected => LinkConnected;
        public string TcpError => LinkError;

        /// <summary>Exactly what is active, e.g. "TCP: 127.0.0.1:8100", "WebSocket: 8101", "Simulated (local)".</summary>
        public string SourceLabel => source switch
        {
            DataSource.Tcp8100 => $"TCP: {host}:{port}",
            DataSource.WebSocket8101 => $"WebSocket: {WebSocketPort}",
            _ => "Simulated (local)"
        };

        int WebSocketPort { get { try { return new Uri(webSocketUri).Port; } catch { return 8101; } } }

        ITelemetryClient client;
        readonly Queue<float> stamps = new Queue<float>();
        long lastSeq = -1;

        void OnEnable() => StartClient();
        void OnDisable() => StopClient();
        void OnApplicationQuit() => StopClient();

        /// <summary>
        /// Safely disconnects the current network client (if any), resets per-feed state and starts the new source.
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
        }

        void StopClient()
        {
            var c = client;
            client = null;          // Update() stops reading from it immediately
            c?.Disconnect();        // closes the socket, ends the background loop, clears its queue
        }

        void ResetFeedState()
        {
            lastSeq = -1;           // a new feed restarts its seq counter; don't flag its first frame as a duplicate
            stamps.Clear();
            PacketRateHz = 0f;
            LastSampleRealtime = -100f;
        }

        void Update()
        {
            if (client != null)
            {
                int n = 0;
                while (n++ < maxMessagesPerFrame && client != null && client.TryDequeue(out var json)) Submit(json);
            }

            while (stamps.Count > 0 && Time.time - stamps.Peek() > 5f) stamps.Dequeue();
            PacketRateHz = stamps.Count / 5f;
        }

        /// <summary>Feed one raw JSON message into the pipeline.</summary>
        public void Submit(string json)
        {
            if (TelemetryParser.TryParse(json, quaternionScalarFirst, out var sample, out var error, out bool ignored))
            {
                // Same seq twice in a row = duplicate frame (the stub's --chaos mode sends these). Drop it.
                if (sample.seq >= 0 && sample.seq == lastSeq) { DuplicatePackets++; return; }
                lastSeq = sample.seq;
                sample.receivedRealtime = Time.time;
                LastSampleRealtime = Time.time;
                GoodPackets++;
                stamps.Enqueue(Time.time);
                SampleReceived?.Invoke(sample);
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
                Debug.LogWarning("[EOSAT] Rejected telemetry: " + error);
            }
        }
    }
}
