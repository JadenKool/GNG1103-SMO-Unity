using System;
using UnityEngine;

namespace Eosat.MR
{
    /// <summary>
    /// Data overlay on the tabletop (toggled from the tablet): the full recorded orbit path, the
    /// ground-station link while the simulator reports contact, Sun / Earth direction arrows on the
    /// hovering model, ground-station markers, and a live readout. The leader line from the yellow dot
    /// (true position) to the hovering model is always shown.
    /// </summary>
    [DefaultExecutionOrder(160)]
    public class TelemetryOverlay : MonoBehaviour
    {
        [Header("Sources")]
        public SpacecraftController spacecraft;
        public TelemetryHub hub;
        public SampleReplayFeed replay;
        public MrSunLight sun;
        [Tooltip("Tabletop ECI frame (globe centre).")]
        public Transform eciFrame;
        public Transform earth;
        public Transform detailView;
        public SatelliteDeployControl deploy;
        [Tooltip("Parent of the ground-station markers (children named after the stations).")]
        public Transform stationsRoot;

        [Header("Lines")]
        public LineRenderer orbitPath;
        public LineRenderer signalLink;
        public LineRenderer sunArrow;
        public LineRenderer earthArrow;
        public LineRenderer leaderLine;
        public float arrowLength = 0.07f;

        [Header("Readout")]
        public Transform readoutRoot;
        public TextMesh readoutText;
        public Vector3 readoutOffset = new Vector3(0.09f, 0.02f, 0f);
        public float readoutRefreshSeconds = 0.25f;

        public bool startVisible = true;
        public bool Visible { get; private set; }

        Transform cam;
        Vector3[] pathPoints;
        float nextRefresh;
        string cachedStationName = "";
        Transform cachedStation;

        void Awake() => SetVisible(startVisible);

        void Start()
        {
            if (Camera.main) cam = Camera.main.transform;
            foreach (var lr in new[] { orbitPath, signalLink, sunArrow, earthArrow, leaderLine })
                if (lr) lr.useWorldSpace = true;
        }

        public void SetVisible(bool visible)
        {
            Visible = visible;
            foreach (var lr in new[] { orbitPath, signalLink, sunArrow, earthArrow })
                if (lr) lr.enabled = visible;
            if (stationsRoot) stationsRoot.gameObject.SetActive(visible);
            if (readoutRoot) readoutRoot.gameObject.SetActive(visible);
            nextRefresh = 0f;
        }

        public void Toggle() => SetVisible(!Visible);

        void LateUpdate()
        {
            bool has = spacecraft && spacecraft.HasData;
            if (leaderLine)
            {
                leaderLine.enabled = has && detailView;
                if (leaderLine.enabled) { leaderLine.SetPosition(0, spacecraft.transform.position); leaderLine.SetPosition(1, detailView.position); }
            }
            if (!Visible) return;

            UpdateOrbitPath();
            if (!has)
            {
                if (signalLink) signalLink.enabled = false;
                return;
            }
            var latest = spacecraft.Latest;
            UpdateLink(latest);
            UpdateArrows();
            UpdateReadout(latest);
        }

        void UpdateOrbitPath()
        {
            if (!orbitPath) return;
            if (replay == null || !replay.Loaded || !spacecraft) { orbitPath.enabled = false; return; }
            var p = replay.PathEciKm;
            const int step = 2; // 1134 frames -> 567 points is plenty
            int n = (p.Count + step - 1) / step;
            if (pathPoints == null || pathPoints.Length != n) { pathPoints = new Vector3[n]; orbitPath.positionCount = n; }
            for (int i = 0; i < n; i++)
                pathPoints[i] = eciFrame.TransformPoint(EciMath.EciToUnity(p[i * step]) * spacecraft.unitsPerKm);
            orbitPath.SetPositions(pathPoints);
            orbitPath.enabled = true;
        }

        void UpdateLink(TelemetrySample s)
        {
            if (!signalLink) return;
            Transform station = s != null && s.inContact ? FindStation(s.groundStation) : null;
            signalLink.enabled = station;
            if (!station) return;
            signalLink.SetPosition(0, station.position);
            signalLink.SetPosition(1, spacecraft.transform.position);
        }

        Transform FindStation(string name)
        {
            if (string.IsNullOrEmpty(name) || !stationsRoot) return null;
            if (name == cachedStationName) return cachedStation;
            cachedStationName = name;
            cachedStation = null;
            foreach (Transform t in stationsRoot)
                if (t.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf(t.name, StringComparison.OrdinalIgnoreCase) >= 0)
                { cachedStation = t; break; }
            return cachedStation;
        }

        void UpdateArrows()
        {
            if (!detailView) return;
            Vector3 o = detailView.position;
            if (sunArrow && sun) { sunArrow.SetPosition(0, o); sunArrow.SetPosition(1, o + sun.SunDirWorld * arrowLength); }
            if (earthArrow && earth)
            {
                Vector3 toEarth = (earth.position - spacecraft.transform.position).normalized;
                earthArrow.SetPosition(0, o); earthArrow.SetPosition(1, o + toEarth * arrowLength);
            }
        }

        void UpdateReadout(TelemetrySample s)
        {
            if (!readoutRoot) return;
            Vector3 anchor = detailView ? detailView.parent.TransformPoint(detailView.localPosition) : spacecraft.transform.position;
            readoutRoot.position = anchor + (cam ? cam.right : Vector3.right) * readoutOffset.x + Vector3.up * readoutOffset.y;
            if (cam) readoutRoot.rotation = Quaternion.LookRotation(readoutRoot.position - cam.position, Vector3.up);

            if (!readoutText || Time.time < nextRefresh) return;
            nextRefresh = Time.time + readoutRefreshSeconds;

            float rE = s != null && s.earthRadiusKm > 0f ? s.earthRadiusKm : (float)EciMath.EarthRadiusKm;
            float alt = spacecraft.AltitudeKm;
            float a = rE + alt;
            const float mu = 398600.4418f;
            float vel = Mathf.Sqrt(mu / a);
            float periodMin = 2f * Mathf.PI * Mathf.Sqrt(a * a * a / mu) / 60f;

            string clock = EciMath.FormatUtc(spacecraft.DisplayUnix);
            if (replay && replay.Running) clock += "   MET " + TimeSpan.FromSeconds(replay.MetSeconds).ToString(@"hh\:mm\:ss");

            string text = "<b>EOSAT-1</b>   " + clock + "\n" +
                          $"ALT {alt:0.0} km   VEL {vel:0.00} km/s   PER {periodMin:0.0} min\n";
            if (s != null && (s.scMode.Length > 0 || s.aocsMode.Length > 0))
                text += $"MODE {s.scMode}   AOCS {s.aocsMode}   {s.pointing}\n";
            text += "SUN " + (spacecraft.InEclipse ? "<color=#8899ff>ECLIPSE</color>" : "<color=#ffd34d>SUNLIT</color>");
            text += "   LINK " + (s != null && s.inContact ? $"<color=#44ff88>{s.groundStation.ToUpperInvariant()}</color>" : "<color=#888888>NO CONTACT</color>") + "\n";
            if (deploy) text += "ANT " + deploy.AntennaStatus + "\nWINGS " + deploy.PanelsStatus + "\n";
            text += "<color=#888888>" + (hub ? (replay && replay.Running ? $"CLIENT DATA REPLAY x{replay.speed:0}" : hub.SourceLabel) : "") + "</color>";
            if (spacecraft.Stalled) text += $"\n<color=#ff6666>DATA STALLED {spacecraft.SecondsSinceData:0}s</color>";
            readoutText.text = text;
        }
    }
}
