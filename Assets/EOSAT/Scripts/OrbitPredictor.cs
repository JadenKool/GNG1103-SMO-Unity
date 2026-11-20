using System.Collections.Generic;
using UnityEngine;

namespace Eosat
{
    /// <summary>
    /// Propagates the current orbit forward (circular: rotate the position about the orbit pole at the observed rate)
    /// and derives ground-station passes, eclipse windows and the ground track. Good to a few seconds per orbit for
    /// EOSAT-1's near-circular 500 km orbit, which is plenty for a planning strip and AOS / LOS times.
    /// shortcut: circular two-body propagation, no J2 / drag; swap for SGP4 if passes drift by minutes over 24 h.
    /// All vectors in Unity axes (x = ECI x, y = ECI z / north, z = ECI y), km.
    /// </summary>
    public static class OrbitPredictor
    {
        public struct Station { public string name; public float latDeg, lonDeg; }

        /// <summary>Client ground stations (approximate public locations).</summary>
        public static readonly Station[] Stations =
        {
            new Station { name = "Inuvik", latDeg = 68.36f, lonDeg = -133.72f },
            new Station { name = "Iqaluit", latDeg = 63.75f, lonDeg = -68.52f },
            new Station { name = "Svalbard", latDeg = 78.23f, lonDeg = 15.41f },
            new Station { name = "O'Higgins", latDeg = -63.32f, lonDeg = -57.90f },
            new Station { name = "Troll", latDeg = -72.01f, lonDeg = 2.53f },
        };

        public struct Pass { public int station; public double aos, los; public float maxElDeg; public string Name => Stations[station].name; }
        public struct Window { public double start, end; }

        public class Prediction
        {
            public double t0, step;
            public readonly List<Vector3> positions = new List<Vector3>();   // km, Unity axes, one per step
            public readonly List<bool> eclipse = new List<bool>();
            public readonly List<int> contact = new List<int>();              // station index in contact (-1 none)
            public readonly List<Pass> passes = new List<Pass>();
            public readonly List<Window> eclipses = new List<Window>();
            public double Time(int i) => t0 + i * step;
        }

        /// <summary>Station position (km, Unity axes) at a given time.</summary>
        public static Vector3 StationPosition(int i, double unix)
        {
            var s = Stations[i];
            float lat = s.latDeg * Mathf.Deg2Rad;
            float lon = (s.lonDeg + (float)EciMath.GmstDegrees(EciMath.JulianDate(unix))) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon)) * (float)EciMath.EarthRadiusKm;
        }

        public static float ElevationDeg(Vector3 satKm, Vector3 stationKm) =>
            Mathf.Asin(Mathf.Clamp(Vector3.Dot((satKm - stationKm).normalized, stationKm.normalized), -1f, 1f)) * Mathf.Rad2Deg;

        /// <summary>Latitude / longitude (deg) of a position at a time (Earth-fixed).</summary>
        public static Vector2 LatLon(Vector3 posKm, double unix)
        {
            float r = posKm.magnitude;
            float lat = Mathf.Asin(Mathf.Clamp(posKm.y / r, -1f, 1f)) * Mathf.Rad2Deg;
            float lon = Mathf.DeltaAngle(0f, Mathf.Atan2(posKm.z, posKm.x) * Mathf.Rad2Deg - (float)EciMath.GmstDegrees(EciMath.JulianDate(unix)));
            return new Vector2(lat, lon);
        }

        public static Prediction Predict(Vector3 posKm, Vector3 poleAxis, float degPerSimSecond, double t0, double horizonSeconds, double stepSeconds, float minElevationDeg = 0f)
        {
            var p = new Prediction { t0 = t0, step = stepSeconds };
            int n = Mathf.Max(2, (int)(horizonSeconds / stepSeconds) + 1);
            var open = new bool[Stations.Length];
            var maxEl = new float[Stations.Length];
            var aos = new double[Stations.Length];
            bool inEclipse = false; double eclipseStart = t0;
            Vector3 sun = EciMath.EciToUnity(EciMath.SunDirectionEci(EciMath.JulianDate(t0)));   // ~1 deg/day drift: fixed is fine

            for (int i = 0; i < n; i++)
            {
                double t = t0 + i * stepSeconds;
                Vector3 r = Quaternion.AngleAxis(degPerSimSecond * (float)(i * stepSeconds), poleAxis) * posKm;
                p.positions.Add(r);
                bool ecl = EciMath.InEarthShadow(r, sun);
                p.eclipse.Add(ecl);
                if (ecl && !inEclipse) eclipseStart = t;
                if (!ecl && inEclipse) p.eclipses.Add(new Window { start = eclipseStart, end = t });
                inEclipse = ecl;

                int best = -1; float bestEl = minElevationDeg;
                for (int s = 0; s < Stations.Length; s++)
                {
                    float el = ElevationDeg(r, StationPosition(s, t));
                    bool up = el > minElevationDeg;
                    if (up && !open[s]) { open[s] = true; aos[s] = t; maxEl[s] = el; }
                    if (up) maxEl[s] = Mathf.Max(maxEl[s], el);
                    if (!up && open[s]) { open[s] = false; p.passes.Add(new Pass { station = s, aos = aos[s], los = t, maxElDeg = maxEl[s] }); }
                    if (up && el > bestEl) { bestEl = el; best = s; }
                }
                p.contact.Add(best);
            }
            double end = t0 + (n - 1) * stepSeconds;
            if (inEclipse) p.eclipses.Add(new Window { start = eclipseStart, end = end });
            for (int s = 0; s < Stations.Length; s++)
                if (open[s]) p.passes.Add(new Pass { station = s, aos = aos[s], los = end, maxElDeg = maxEl[s] });
            p.passes.Sort((a, b) => a.aos.CompareTo(b.aos));
            return p;
        }
    }
}
