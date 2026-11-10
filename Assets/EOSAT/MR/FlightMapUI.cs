// FlightMapUI.cs - 2D flat flight map: satellite position + ground track on an equirectangular Earth image.
// Latitude / longitude come from the spacecraft's displayed ECI position and Greenwich sidereal time,
// so the map keeps moving on the PREDICTED path while telemetry is lost (marker turns amber).

using System.Collections.Generic;
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
        [Tooltip("Pre-made dot images used for the ground track (oldest are reused).")]
        public List<Image> trackDots = new List<Image>();

        [Header("Track")]
        [Tooltip("Degrees of travel between two track dots.")]
        public float dotSpacingDeg = 3f;
        public Color liveColor = new Color(1f, 0.85f, 0.2f, 1f);
        public Color predictedColor = new Color(1f, 0.55f, 0.1f, 1f);
        public Color trackColor = new Color(0.3f, 0.85f, 1f, 0.9f);

        public float LatitudeDeg { get; private set; }
        public float LongitudeDeg { get; private set; }

        SpacecraftController lastSc;
        int nextDot, usedDots;
        Vector2 lastDotLatLon = new Vector2(999f, 999f);
        float nextText;

        void OnEnable() { if (hub) hub.SourceChanged += OnSourceChanged; }
        void OnDisable() { if (hub) hub.SourceChanged -= OnSourceChanged; }
        void OnSourceChanged(DataSource _) => ClearTrack();

        public void ClearTrack()
        {
            foreach (var d in trackDots) if (d) d.enabled = false;
            nextDot = usedDots = 0;
            lastDotLatLon = new Vector2(999f, 999f);
        }

        void Start() => ClearTrack();

        void LateUpdate()
        {
            var sc = spacecraft && spacecraft.isActiveAndEnabled ? spacecraft : alternate ? alternate : spacecraft;
            if (sc != lastSc) { lastSc = sc; ClearTrack(); }      // MR <-> VR: start a fresh track
            if (!sc || !mapRect || !marker) return;
            bool has = sc.HasData;
            marker.gameObject.SetActive(has);
            if (!has) { if (readout && Time.unscaledTime >= nextText) { nextText = Time.unscaledTime + 0.5f; readout.text = "Waiting for telemetry..."; } return; }

            // ECI (Unity axes: x = ECI x, y = ECI z / north, z = ECI y) -> latitude, longitude.
            Vector3 u = sc.transform.localPosition;
            float r = u.magnitude;
            if (r < 1e-9f) return;
            LatitudeDeg = Mathf.Asin(Mathf.Clamp(u.y / r, -1f, 1f)) * Mathf.Rad2Deg;
            double gmst = EciMath.GmstDegrees(EciMath.JulianDate(sc.DisplayUnix));
            LongitudeDeg = Mathf.DeltaAngle(0f, Mathf.Atan2(u.z, u.x) * Mathf.Rad2Deg - (float)gmst);

            marker.anchoredPosition = ToMap(LatitudeDeg, LongitudeDeg);
            bool predicted = hub && hub.PredictionMode;
            if (markerImage) markerImage.color = predicted ? predictedColor : liveColor;

            // Drop a track dot every few degrees of travel.
            float moved = Mathf.Max(Mathf.Abs(Mathf.DeltaAngle(lastDotLatLon.y, LongitudeDeg)), Mathf.Abs(LatitudeDeg - lastDotLatLon.x));
            if (trackDots.Count > 0 && moved >= dotSpacingDeg)
            {
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

            if (readout && Time.unscaledTime >= nextText)
            {
                nextText = Time.unscaledTime + 0.25f;
                string ns = LatitudeDeg >= 0 ? "N" : "S", ew = LongitudeDeg >= 0 ? "E" : "W";
                readout.text = $"LAT {Mathf.Abs(LatitudeDeg):00.0}° {ns}     LON {Mathf.Abs(LongitudeDeg):000.0}° {ew}     ALT {sc.AltitudeKm:0} km     " +
                               (predicted ? "<color=#FFB000><b>PREDICTED</b></color>" : "<color=#39FF6A><b>LIVE</b></color>") +
                               $"     {EciMath.FormatUtc(sc.DisplayUnix)}";
            }
        }

        // Map-local position (pivot at the map centre).
        Vector2 ToMap(float latDeg, float lonDeg)
        {
            Vector2 size = mapRect.rect.size;
            return new Vector2(lonDeg / 360f * size.x, latDeg / 180f * size.y);
        }
    }
}
