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
        [Tooltip("True if q arrives as [w, x, y, z]; false for [x, y, z, w].")]
        public bool quaternionScalarFirst = true;

        public event Action<TelemetrySample> SampleReceived;

        public int GoodPackets { get; private set; }
        public int BadPackets { get; private set; }
        public string LastRejectReason { get; private set; }
        public float LastRejectRealtime { get; private set; } = -100f;
        public float PacketRateHz { get; private set; }
        public bool TcpConnected => tcp != null && tcp.Connected;
        public string TcpError => tcp?.LastError;
        public string SourceLabel => source == TelemetrySourceMode.Simulated ? "SIMULATED" : $"TCP {host}:{port}";

        TcpJsonClient tcp;
        readonly Queue<float> stamps = new Queue<float>();

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
            if (TelemetryParser.TryParse(json, quaternionScalarFirst, out var sample, out var error))
            {
                sample.receivedRealtime = Time.time;
                GoodPackets++;
                stamps.Enqueue(Time.time);
                SampleReceived?.Invoke(sample);
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
