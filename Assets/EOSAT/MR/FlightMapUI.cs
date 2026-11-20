// FlightMapUI.cs - the Flight Map screen: ground track on an equirectangular Earth, plus the operator panels
// around it (Time, Next passes, Link, Plan strip, Budgets). Latitude / longitude come from the spacecraft's
// displayed ECI position and Greenwich sidereal time, so everything keeps moving on the PREDICTED path while
// telemetry is lost (marker turns amber). Passes / eclipses / the future track come from OrbitPredictor.
// Every panel field is optional: the old map-only screen still works.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Eosat.MR
{
    public class FlightMapUI : MonoBehaviour
    {
        public SpacecraftController spacecraft;
        [Tooltip("Used while the first one is inactive (the MR and VR spacecraft are never active together).")]
        public SpacecraftController alternate;
        public TelemetryHub hub;
        [Tooltip("RectTransform of the map image (equirectangular: left edge = 180 W, centre = 0, top = 90 N).")]
        public RectTransform mapRect;
        public RectTransform marker;
        public Image markerImage;
        public Text readout;
        [Tooltip("Pre-made dot images used for the past ground track (oldest are reused).")]
        public List<Image> trackDots = new List<Image>();

        [Header("Track")]
        [Tooltip("Degrees of travel between two track dots.")]
        public float dotSpacingDeg = 3f;
        public Color liveColor = new Color(1f, 0.85f, 0.2f, 1f);
        public Color predictedColor = new Color(1f, 0.55f, 0.1f, 1f);
        public Color trackColor = new Color(0.3f, 0.85f, 1f, 0.9f);

        [Header("Predicted track (solid = next orbit, dotted = later)")]
        public List<Image> nextOrbitDots = new List<Image>();
        public List<Image> laterDots = new List<Image>();
        public List<Text> passCallouts = new List<Text>();

        [Header("Time")]
        public Text dateText, clockText, metText, simChip, rateChip;
        [Header("Next passes")]
        public Text passHeadline, passCountdown, passTable;
        [Header("Link")]
        public Text linkText;
        [Header("Plan: next orbit, then 24 h")]
        public Text nextOrbitLabel;
        public RectTransform nextOrbitContacts, nextOrbitEclipse;
        public RectTransform dayEclipse, dayContacts;
        [Header("Budgets")]
        public Text budgetsText;
        public Image socFill;

        [Header("Prediction")]
        public float predictEverySeconds = 20f;
        public float horizonHours = 24f;
        public float stepSeconds = 30f;

        public float LatitudeDeg { get; private set; }
        public float LongitudeDeg { get; private set; }
        public OrbitPredictor.Prediction Prediction { get; private set; }

        SpacecraftController lastSc;
        int nextDot, usedDots;
        Vector2 lastDotLatLon = new Vector2(999f, 999f);
        float nextText, nextPredict;
        readonly Dictionary<RectTransform, List<Image>> bars = new Dictionary<RectTransform, List<Image>>();
        readonly StringBuilder sb = new StringBuilder(512);
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        static readonly Color ContactColor = new Color(0.3f, 0.85f, 1f, 0.95f), EclipseColor = new Color(0.55f, 0.6f, 0.75f, 0.9f);

        void OnEnable() { if (hub) { hub.SourceChanged += OnSourceChanged; hub.Resynced += Repredict; } }
        void OnDisable() { if (hub) { hub.SourceChanged -= OnSourceChanged; hub.Resynced -= Repredict; } }
        void OnSourceChanged(DataSource _) { ClearTrack(); Repredict(); }
        void Repredict() => nextPredict = 0f;

        public void ClearTrack()
        {
            foreach (var d in trackDots) if (d) d.enabled = false;
            nextDot = usedDots = 0;
            lastDotLatLon = new Vector2(999f, 999f);
        }

        void Start() => ClearTrack();

        /// <summary>Map-local position (pivot at the map centre) of a latitude / longitude.</summary>
        public static Vector2 MapPosition(float latDeg, float lonDeg, Vector2 mapSize) =>
            new Vector2(lonDeg / 360f * mapSize.x, latDeg / 180f * mapSize.y);

        void LateUpdate()
        {
            var sc = spacecraft && spacecraft.isActiveAndEnabled ? spacecraft : alternate ? alternate : spacecraft;
            if (sc != lastSc) { lastSc = sc; ClearTrack(); Repredict(); }      // MR <-> VR: start a fresh track
            if (!sc || !mapRect || !marker) return;
            bool has = sc.HasData;
            marker.gameObject.SetActive(has);
            if (!has)
            {
                if (readout && Time.unscaledTime >= nextText) { nextText = Time.unscaledTime + 0.5f; readout.text = LocalizationManager.Tr("Waiting for telemetry..."); }
                return;
            }

            // ECI (Unity axes: x = ECI x, y = ECI z / north, z = ECI y) -> latitude, longitude.
            Vector3 posKm = sc.transform.localPosition / sc.unitsPerKm;
            if (posKm.sqrMagnitude < 1f) return;
            Vector2 ll = OrbitPredictor.LatLon(posKm, sc.DisplayUnix);
            LatitudeDeg = ll.x; LongitudeDeg = ll.y;

            marker.anchoredPosition = ToMap(LatitudeDeg, LongitudeDeg);
            bool predicted = hub && hub.PredictionMode;
            if (markerImage) markerImage.color = predicted ? predictedColor : liveColor;
            DropTrackDot(predicted);

            if (Time.unscaledTime >= nextPredict && sc.OrbitRateDegPerSec > 1e-4f)
            {
                nextPredict = Time.unscaledTime + predictEverySeconds;
                Prediction = OrbitPredictor.Predict(posKm, sc.OrbitAxis, sc.OrbitRateDegPerSec, sc.DisplayUnix, horizonHours * 3600.0, stepSeconds);
                DrawPrediction(sc);
            }

            if (Time.unscaledTime < nextText) return;
            nextText = Time.unscaledTime + 0.25f;
            if (readout)
            {
                string ns = LatitudeDeg >= 0 ? "N" : "S", ew = LongitudeDeg >= 0 ? "E" : "W";
                readout.text = LocalizationManager.Tr($"LAT {Mathf.Abs(LatitudeDeg):00.0}° {ns}   LON {Mathf.Abs(LongitudeDeg):000.0}° {ew}   ALT {sc.AltitudeKm:0} km   " +
                               (predicted ? "<color=#FFB000><b>PREDICTED</b></color>" : "<color=#39FF6A><b>LIVE</b></color>"));
            }
            UpdateTime(sc);
            UpdatePasses(sc, posKm);
            UpdateBudgets();
        }

        void DropTrackDot(bool predicted)
        {
            float moved = Mathf.Max(Mathf.Abs(Mathf.DeltaAngle(lastDotLatLon.y, LongitudeDeg)), Mathf.Abs(LatitudeDeg - lastDotLatLon.x));
            if (trackDots.Count == 0 || moved < dotSpacingDeg) return;
            var d = trackDots[nextDot];
            if (d)
            {
                d.enabled = true;
                d.rectTransform.anchoredPosition = marker.anchoredPosition;
                d.color = predicted ? predictedColor : trackColor;
            }
            nextDot = (nextDot + 1) % trackDots.Count;
            usedDots = Mathf.Min(usedDots + 1, trackDots.Count);
            lastDotLatLon = new Vector2(LatitudeDeg, LongitudeDeg);
            // Fade from newest (opaque) to oldest (faint).
            for (int i = 0; i < usedDots; i++)
            {
                var dot = trackDots[(nextDot - 1 - i + trackDots.Count * 2) % trackDots.Count];
                if (!dot) continue;
                var c = dot.color; c.a = Mathf.Lerp(0.9f, 0.12f, i / (float)trackDots.Count); dot.color = c;
            }
        }

        // ---------------- Prediction: future track, AOS callouts, plan strip ----------------
        void DrawPrediction(SpacecraftController sc)
        {
            var p = Prediction;
            double period = 360.0 / sc.OrbitRateDegPerSec;
            Place(nextOrbitDots, p, 0, period, 1f);
            Place(laterDots, p, period, period * 4, 0.45f);

            int c = 0;
            foreach (var pass in p.passes)
            {
                if (c >= passCallouts.Count || pass.aos - p.t0 > period) break;
                var label = passCallouts[c++];
                int i = Mathf.Clamp((int)((pass.aos - p.t0) / p.step), 0, p.positions.Count - 1);
                Vector2 ll = OrbitPredictor.LatLon(p.positions[i], p.Time(i));
                label.enabled = true;
                label.rectTransform.anchoredPosition = ToMap(ll.x, ll.y) + new Vector2(10f, 6f);
                label.text = $"AOS {pass.Name.ToUpperInvariant()} {Hm(pass.aos)}";
            }
            for (; c < passCallouts.Count; c++) passCallouts[c].enabled = false;

            if (nextOrbitLabel) nextOrbitLabel.text = LocalizationManager.Tr($"NEXT ORBIT  {period / 60.0:0.0} MIN  -  from the observed orbit rate");
            Windows(nextOrbitContacts, p.passes.ConvertAll(x => (x.aos, x.los)), p.t0, period, ContactColor);
            Windows(nextOrbitEclipse, p.eclipses.ConvertAll(x => (x.start, x.end)), p.t0, period, EclipseColor);
            Windows(dayContacts, p.passes.ConvertAll(x => (x.aos, x.los)), p.t0, horizonHours * 3600.0, ContactColor);
            Windows(dayEclipse, p.eclipses.ConvertAll(x => (x.start, x.end)), p.t0, horizonHours * 3600.0, EclipseColor);
        }

        void Place(List<Image> dots, OrbitPredictor.Prediction p, double from, double to, float alpha)
        {
            if (dots.Count == 0) return;
            int i0 = (int)(from / p.step), i1 = Mathf.Min(p.positions.Count - 1, (int)(to / p.step));
            int every = Mathf.Max(1, (i1 - i0) / dots.Count + 1);
            int d = 0;
            for (int i = i0; i <= i1 && d < dots.Count; i += every, d++)
            {
                Vector2 ll = OrbitPredictor.LatLon(p.positions[i], p.Time(i));
                var dot = dots[d];
                dot.enabled = true;
                dot.rectTransform.anchoredPosition = ToMap(ll.x, ll.y);
                dot.color = p.eclipse[i] ? new Color(0.55f, 0.6f, 0.9f, alpha) : new Color(1f, 1f, 1f, alpha);
            }
            for (; d < dots.Count; d++) dots[d].enabled = false;
        }

        /// <summary>Bars inside a row RectTransform for each (start, end) window between t0 and t0 + span.</summary>
        void Windows(RectTransform row, List<(double a, double b)> windows, double t0, double span, Color color)
        {
            if (!row) return;
            if (!bars.TryGetValue(row, out var list)) bars[row] = list = new List<Image>();
            float w = row.rect.width;
            int n = 0;
            foreach (var (a, b) in windows)
            {
                double s = Math.Max(a, t0), e = Math.Min(b, t0 + span);
                if (e <= s) continue;
                if (n == list.Count)
                {
                    var go = new GameObject("Bar", typeof(RectTransform), typeof(Image));
                    var rt = (RectTransform)go.transform; rt.SetParent(row, false);
                    rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 0.5f);
                    var img = go.GetComponent<Image>(); img.raycastTarget = false;
                    list.Add(img);
                }
                var bar = list[n++];
                bar.enabled = true; bar.color = color;
                bar.rectTransform.anchoredPosition = new Vector2((float)((s - t0) / span) * w, 0f);
                bar.rectTransform.sizeDelta = new Vector2(Mathf.Max(3f, (float)((e - s) / span) * w), 0f);
            }
            for (; n < list.Count; n++) list[n].enabled = false;
        }

        // ---------------- Left column ----------------
        void UpdateTime(SpacecraftController sc)
        {
            var t = Epoch.AddSeconds(sc.DisplayUnix);
            if (dateText) dateText.text = t.ToString("dd MMM yyyy", Inv).ToUpperInvariant() + " UTC";
            if (clockText) clockText.text = t.ToString("HH:mm:ss", Inv);
            var s = sc.Latest;
            if (metText)
                metText.text = s != null && s.hasMet && s.hasTime
                    ? "MET " + Met(s.metSeconds + (sc.DisplayUnix - s.unixTime))
                    : "MET --";
            if (simChip) simChip.text = LocalizationManager.Tr(hub && hub.ReplayMode ? "Replay" : hub && hub.PredictionMode ? "Predicted" : "Sim time");
            if (rateChip) rateChip.text = LocalizationManager.Tr($"Training x{Mathf.Max(1f, sc.TimeRate):0}");
        }

        static string Met(double seconds)
        {
            if (seconds < 0) seconds = 0;
            var ts = TimeSpan.FromSeconds(seconds);
            return $"{(int)ts.TotalDays:000}/{ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
        }

        static string Hm(double unix) => Epoch.AddSeconds(unix).ToString("HH:mm", Inv);
        static string Mmss(double s) { s = Math.Max(0, s); return $"{(int)(s / 60):00}:{(int)(s % 60):00}"; }

        void UpdatePasses(SpacecraftController sc, Vector3 posKm)
        {
            var p = Prediction;
            double now = sc.DisplayUnix;
            int inContact = -1;
            float bestEl = 0f;
            for (int s = 0; s < OrbitPredictor.Stations.Length; s++)
            {
                float el = OrbitPredictor.ElevationDeg(posKm, OrbitPredictor.StationPosition(s, now));
                if (el > bestEl) { bestEl = el; inContact = s; }
            }

            if (p != null && (passHeadline || passTable))
            {
                OrbitPredictor.Pass? current = null, next = null;
                foreach (var pass in p.passes)
                {
                    if (pass.los <= now) continue;
                    if (pass.aos <= now && pass.station == inContact) { current ??= pass; continue; }
                    if (pass.aos > now) { next ??= pass; }
                }
                if (passHeadline) passHeadline.text = LocalizationManager.Tr(current != null ? "In contact: LOS in" : next != null ? "Next AOS in" : "No pass in 24 h");
                if (passCountdown)
                {
                    var shown = current ?? next;
                    passCountdown.text = shown == null ? "--" :
                        $"<size=34><b>{shown.Value.Name}</b></size>\n<color=#4DC8FF><b>{Mmss(current != null ? shown.Value.los - now : shown.Value.aos - now)}</b></color>";
                }
                if (passTable)
                {
                    sb.Clear();
                    sb.Append("<color=#9AA6B8>Station        AOS     LOS    Max el</color>\n");
                    int rows = 0;
                    foreach (var pass in p.passes)
                    {
                        if (pass.los <= now) continue;
                        if (rows++ >= 4) break;
                        sb.Append(pass.Name.PadRight(12)).Append(' ').Append(Hm(Math.Max(pass.aos, p.t0))).Append("   ").Append(Hm(pass.los))
                          .Append("   ").Append(pass.maxElDeg.ToString("0", Inv)).Append("°\n");
                    }
                    passTable.text = LocalizationManager.Tr(sb.ToString());
                }
            }

            if (linkText)
            {
                var s = sc.Latest;
                bool contact = inContact >= 0 || (s != null && s.inContact);
                string station = inContact >= 0 ? OrbitPredictor.Stations[inContact].name : s != null && s.inContact ? s.groundStation : "--";
                float range = inContact >= 0 ? (posKm - OrbitPredictor.StationPosition(inContact, now)).magnitude : float.NaN;
                linkText.text = LocalizationManager.Tr(
                    (contact ? "<color=#4DC8FF><size=40><b>In contact</b></size></color>" : "<color=#9AA6B8><size=40><b>NO CONTACT</b></size></color>") +
                    $"\nStation    {station}\nElevation  {(inContact >= 0 ? bestEl.ToString("0.0", Inv) + "°" : "--")}\nRange      {(float.IsNaN(range) ? "--" : range.ToString("0", Inv) + " km")}");
            }
        }

        void UpdateBudgets()
        {
            if (!budgetsText && !socFill) return;
            var pw = hub ? hub.LatestPower : null;
            float soc = pw != null ? pw.BatterySocPct : float.NaN;
            if (socFill) socFill.fillAmount = float.IsNaN(soc) ? 0f : Mathf.Clamp01(soc / 100f);
            if (budgetsText)
                budgetsText.text = LocalizationManager.Tr(
                    $"Power: SoC <b>{(float.IsNaN(soc) ? "no data" : soc.ToString("0.0", Inv) + "%")}</b>" +
                    (pw != null && !float.IsNaN(pw.TemperatureC) ? $"   Temp {pw.TemperatureC:0} °C" : "") +
                    "      Data: peak fill <b>no data</b>      Pass fit: <b>no data</b>      SoC at eclipse exit: <b>no data</b>\n" +
                    "<color=#9AA6B8>" + (pw != null ? "power stream 8103 live" : "no power stream (8103)") + "  -  no MCS URL configured</color>");
        }

        Vector2 ToMap(float latDeg, float lonDeg) => MapPosition(latDeg, lonDeg, mapRect.rect.size);
    }
}
