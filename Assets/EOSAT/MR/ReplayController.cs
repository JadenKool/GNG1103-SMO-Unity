// ReplayController.cs - Replay Mode: scrub back through this session's telemetry log.
// While replaying, the hub stops driving the scene with live frames (they are still logged) and this script
// feeds logged frames through hub.EmitReplay instead, so every model / panel shows the replayed moment.
// The timeline gets one clickable blue marker per error / anomaly (rejected frame, data gap, stuck panel, jam...);
// clicking it jumps a little before the event. "Live" returns to the live feed.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Eosat.MR
{
    public class ReplayController : MonoBehaviour
    {
        public TelemetryHub hub;
        [Tooltip("Head-locked replay panel (raw data list + timeline). Shown only while replaying.")]
        public GameObject panel;
        public Slider timeline;
        [Tooltip("Markers are created here, stretched over the timeline's width.")]
        public RectTransform markerRow;
        public Text timeText;
        public Text playLabel;
        public Color markerColor = new Color(0.25f, 0.55f, 1f, 1f);
        [Tooltip("Seconds before an anomaly a marker jumps to.")]
        public float markerLeadSeconds = 2f;
        public float[] speeds = { 1f, 2f, 5f, 10f };

        public bool Replaying { get; private set; }
        public event System.Action<bool> ReplayChanged;

        float t0, t1, cursor;
        bool playing, settingSlider;
        int speedIndex, lastSampleIdx = -1, lastPowerIdx = -1, errorCount = -1;
        readonly List<GameObject> markers = new List<GameObject>();

        void Awake()
        {
            if (timeline) { timeline.minValue = 0f; timeline.maxValue = 1f; timeline.onValueChanged.AddListener(OnScrub); }
            if (panel) panel.SetActive(false);
        }

        public void ToggleReplay() => SetReplay(!Replaying);

        public void SetReplay(bool on)
        {
            if (!hub || on == Replaying) return;
            var log = hub.SessionLog;
            if (on && log.Count == 0) { Debug.Log("[Replay] Nothing logged yet."); return; }
            Replaying = on;
            hub.SetReplayMode(on);
            if (panel) panel.SetActive(on);
            playing = false;
            if (on)
            {
                t0 = log[0].t; t1 = log[log.Count - 1].t;
                errorCount = -1;
                Seek(Mathf.Max(t0, t1 - 60f));        // start one minute back
            }
            ReplayChanged?.Invoke(on);
        }

        public void TogglePlay() { playing = !playing; Label(); }
        public void NextSpeed() { speedIndex = (speedIndex + 1) % speeds.Length; Label(); }
        public void GoLive() => SetReplay(false);

        void Update()
        {
            if (!Replaying) return;
            var log = hub.SessionLog;
            if (log.Count == 0) return;
            t0 = log[0].t; t1 = log[log.Count - 1].t;   // live frames keep being logged meanwhile
            RebuildMarkersIfNeeded(log);

            if (playing)
            {
                cursor += Time.deltaTime * speeds[speedIndex];
                if (cursor >= t1) { cursor = t1; playing = false; Label(); }
                Emit(false);
            }
            if (timeline) { settingSlider = true; timeline.value = t1 > t0 ? (cursor - t0) / (t1 - t0) : 1f; settingSlider = false; }
            if (timeText)
            {
                var s = lastSampleIdx >= 0 && lastSampleIdx < log.Count ? log[lastSampleIdx].sample : null;
                string sim = s != null && s.hasTime ? EciMath.FormatUtc(s.unixTime) : "--";
                timeText.text = LocalizationManager.Tr($"REPLAY  {sim}   -{t1 - cursor:0} s from live   x{speeds[speedIndex]:0}");
            }
        }

        void OnScrub(float v) { if (!settingSlider && Replaying) Seek(Mathf.Lerp(t0, t1, v)); }

        public void Seek(float t)
        {
            cursor = Mathf.Clamp(t, t0, t1);
            lastSampleIdx = lastPowerIdx = -1;
            Emit(true);
        }

        // Show the newest logged frame (and power reading) at or before the cursor.
        void Emit(bool jump)
        {
            var log = hub.SessionLog;
            int i = LastAtOrBefore(log, cursor);
            int sIdx = -1, pIdx = -1;
            for (int j = i; j >= 0 && (sIdx < 0 || pIdx < 0); j--)
            {
                if (sIdx < 0 && log[j].kind == LogKind.Sample) sIdx = j;
                if (pIdx < 0 && log[j].kind == LogKind.Power) pIdx = j;
                if (i - j > 5000) break;                 // bounded search
            }
            if (sIdx >= 0 && sIdx != lastSampleIdx) { lastSampleIdx = sIdx; hub.EmitReplay(log[sIdx].sample, jump); }
            if (pIdx >= 0 && pIdx != lastPowerIdx) { lastPowerIdx = pIdx; hub.EmitReplayPower(log[pIdx].power); }
        }

        /// <summary>Index of the last entry with t <= time (log is in time order); -1 if none.</summary>
        public static int LastAtOrBefore(IReadOnlyList<LogEntry> log, float time)
        {
            int lo = 0, hi = log.Count - 1, ans = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (log[mid].t <= time) { ans = mid; lo = mid + 1; } else hi = mid - 1;
            }
            return ans;
        }

        void RebuildMarkersIfNeeded(IReadOnlyList<LogEntry> log)
        {
            if (!markerRow) return;
            int count = 0;
            for (int i = 0; i < log.Count; i++) if (log[i].IsError) count++;
            if (count == errorCount && Time.frameCount % 120 != 0) return;   // positions also slide as the log grows
            errorCount = count;
            foreach (var m in markers) if (m) Destroy(m);
            markers.Clear();
            float span = Mathf.Max(0.001f, t1 - t0);
            for (int i = 0; i < log.Count; i++)
            {
                var e = log[i];
                if (!e.IsError) continue;
                float x = (e.t - t0) / span;
                var go = new GameObject("Marker " + e.kind, typeof(RectTransform), typeof(Image), typeof(Button));
                var rt = (RectTransform)go.transform;
                rt.SetParent(markerRow, false);
                rt.anchorMin = new Vector2(x, 0f); rt.anchorMax = new Vector2(x, 1f);
                rt.sizeDelta = new Vector2(14f, 0f);
                go.GetComponent<Image>().color = markerColor;
                float when = e.t;
                go.GetComponent<Button>().onClick.AddListener(() => { playing = false; Label(); Seek(when - markerLeadSeconds); });
                markers.Add(go);
            }
        }

        void Label() { if (playLabel) playLabel.text = LocalizationManager.Tr(playing ? "PAUSE" : "PLAY"); }
    }
}
