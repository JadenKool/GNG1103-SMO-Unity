using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Eosat
{
    /// <summary>
    /// Plays the client's simulator recording (sample_vr_frames.ndjson: smo-vr/1, one frame every 5 s,
    /// one full orbit - 98 deg inclination, RAAN 120 deg, 500.9 km, 94.5 min) through TelemetryHub.Submit,
    /// i.e. the exact same parse/validate path as the live TCP feed. Runs while the hub source is Simulated;
    /// switch the hub to Tcp for the live vr_stub / real simulator.
    /// </summary>
    [RequireComponent(typeof(TelemetryHub))]
    public class SampleReplayFeed : MonoBehaviour
    {
        [Tooltip("File in Assets/StreamingAssets.")]
        public string fileName = "sample_vr_frames.ndjson";
        [Tooltip("Simulated seconds per real second. 30 = one orbit in ~3 min. 1 = real time.")]
        public float speed = 30f;
        public bool loop = true;
        [Tooltip("Turn off the built-in SimulatedSpacecraft when the client file loads, so only client data is fed.")]
        public bool disableBuiltInSimulator = true;

        public bool Loaded => lines.Count > 0;
        public bool Running => Loaded && enabled && hub && hub.source == DataSource.SimulatedLocal;
        public float MetSeconds => met;
        public float DurationSeconds { get; private set; }
        /// <summary>Every recorded position (ECI, km) - the full orbit, for drawing the path.</summary>
        public IReadOnlyList<Vector3> PathEciKm => path;

        [System.Serializable] class Head { public string type; public float met_s; public SmoOrbit orbit; }

        readonly List<string> lines = new List<string>();
        readonly List<float> mets = new List<float>();
        readonly List<Vector3> path = new List<Vector3>();
        TelemetryHub hub;
        float met;
        int next;

        void Awake()
        {
            hub = GetComponent<TelemetryHub>();
            Load();
            if (Loaded && disableBuiltInSimulator)
            {
                var sim = GetComponent<SimulatedSpacecraft>();
                if (sim) sim.enabled = false;
            }
        }

        void Load()
        {
            string p = Path.Combine(Application.streamingAssetsPath, fileName);
            if (!File.Exists(p)) { Debug.LogWarning("[EOSAT] Replay file not found: " + p + " - using the built-in simulator."); return; }

            foreach (string line in File.ReadLines(p))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                Head h;
                try { h = JsonUtility.FromJson<Head>(line); } catch { continue; }
                if (h == null || h.type != "state") continue;
                lines.Add(line);
                mets.Add(h.met_s);
                var pos = h.orbit != null ? h.orbit.pos_eci_km : null;
                if (pos != null && pos.Length >= 3) path.Add(new Vector3(pos[0], pos[1], pos[2]));
            }
            DurationSeconds = mets.Count > 0 ? mets[mets.Count - 1] : 0f;
            Debug.Log($"[EOSAT] Client replay loaded: {lines.Count} frames, {DurationSeconds / 60f:F1} min of sim time, x{speed} speed.");
        }

        /// <summary>Jump to a mission elapsed time (seconds).</summary>
        public void Seek(float metSeconds)
        {
            met = Mathf.Clamp(metSeconds, 0f, DurationSeconds);
            next = 0;
            while (next < mets.Count - 1 && mets[next + 1] <= met) next++;
        }

        void Update()
        {
            if (!Running) return;
            met += Time.deltaTime * speed;

            int sent = 0;
            while (next < lines.Count && mets[next] <= met && sent < 60)
            {
                hub.Submit(lines[next]);
                next++;
                sent++;
            }
            if (next >= lines.Count && loop) { met = 0f; next = 0; }
        }
    }
}
