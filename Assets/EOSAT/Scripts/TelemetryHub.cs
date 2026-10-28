using System;
using System.Collections.Generic;
using UnityEngine;

namespace Eosat
{
    public enum TelemetrySourceMode { Simulated, Tcp }

    /// <summary>
    /// Single entry point for telemetry. Every message (simulated or TCP) goes through the same
    /// parser and validator, so bad data is rejected the same way in testing and on the real feed.
    /// </summary>
    public class TelemetryHub : MonoBehaviour
    {
        [Tooltip("Simulated = built-in test orbit. Tcp = connect to the client's test tool / simulator.")]
        public TelemetrySourceMode source = TelemetrySourceMode.Simulated;

        [Header("TCP feed")]
        public string host = "127.0.0.1";
        [Tooltip("Allowed ports per target specs: 8100-8101")]
        public int port = 8100;
        [Tooltip("Legacy flat format only: true if q arrives as [w, x, y, z]; false for [x, y, z, w]. " +
                 "The client's smo-vr/1 format is always [x, y, z, w] and is detected automatically.")]
        public bool quaternionScalarFirst = true;

        public event Action<TelemetrySample> SampleReceived;

        public int GoodPackets { get; private set; }
        public int BadPackets { get; private set; }
        public int IgnoredPackets { get; private set; }
        public int DuplicatePackets { get; private set; }
        public string LastRejectReason { get; private set; }
        public float LastRejectRealtime { get; private set; } = -100f;
        public float PacketRateHz { get; private set; }
        public bool TcpConnected => tcp != null && tcp.Connected;
        public string TcpError => tcp?.LastError;
        public string SourceLabel => source == TelemetrySourceMode.Simulated ? "SIMULATED" : $"TCP {host}:{port}";

        TcpJsonClient tcp;
        readonly Queue<float> stamps = new Queue<float>();
        long lastSeq = -1;

        void OnEnable()
        {
            if (source == TelemetrySourceMode.Tcp)
            {
                tcp = new TcpJsonClient(host, port);
                tcp.Start();
            }
        }

        void OnDisable()
        {
            tcp?.Stop();
            tcp = null;
        }

        void Update()
        {
            if (tcp != null)
                while (tcp.TryDequeue(out var json)) Submit(json);

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
                GoodPackets++;
                stamps.Enqueue(Time.time);
                SampleReceived?.Invoke(sample);
            }
            else if (ignored)
            {
                // Valid message with no state (e.g. smo-vr/1 "hello"): not an error.
                IgnoredPackets++;
                if (json.Contains("\"hello\"")) Debug.Log("[EOSAT] Connected to simulator: " + json);
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
