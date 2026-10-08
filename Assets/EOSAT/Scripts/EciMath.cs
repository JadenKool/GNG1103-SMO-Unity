using System;
using UnityEngine;

namespace Eosat
{
    /// <summary>
    /// Frame conversions and low-precision astronomy helpers.
    ///
    /// ECI (Earth-Centred Inertial, J2000-ish) is right-handed with +Z toward the North Pole.
    /// Unity is left-handed with +Y up. We map ECI (x, y, z) -> Unity (x, z, y), so ECI +Z (north) is Unity +Y.
    /// Because that swap is a reflection, rotations keep their axis (swapped) but flip their sign.
    /// </summary>
    public static class EciMath
    {
        public const double EarthRadiusKm = 6378.137;
        const double Deg = Math.PI / 180.0;
        static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static Vector3 EciToUnity(Vector3 v) => new Vector3(v.x, v.z, v.y);
        public static Vector3 UnityToEci(Vector3 v) => new Vector3(v.x, v.z, v.y);

        /// <summary>ECI quaternion (scalar w + vector x,y,z) to a Unity rotation.</summary>
        public static Quaternion EciQuatToUnity(float w, float x, float y, float z) => new Quaternion(-x, -z, -y, w);

        public static void UnityQuatToEci(Quaternion q, out float w, out float x, out float y, out float z)
        {
            w = q.w; x = -q.x; y = -q.z; z = -q.y;
        }

        public static double UnixNow() => (DateTime.UtcNow - UnixEpoch).TotalSeconds;
        public static double JulianDate(double unixSeconds) => unixSeconds / 86400.0 + 2440587.5;

        /// <summary>Greenwich Mean Sidereal Time in degrees (IAU 1982, adequate for visualization).</summary>
        public static double GmstDegrees(double jd)
        {
            double d = jd - 2451545.0;
            double g = (280.46061837 + 360.98564736629 * d) % 360.0;
            return g < 0 ? g + 360.0 : g;
        }

        /// <summary>Unit vector from Earth to Sun in ECI (Astronomical Almanac low-precision formula, ~0.01 deg).</summary>
        public static Vector3 SunDirectionEci(double jd)
        {
            double n = jd - 2451545.0;
            double L = 280.460 + 0.9856474 * n;
            double g = (357.528 + 0.9856003 * n) * Deg;
            double lambda = (L + 1.915 * Math.Sin(g) + 0.020 * Math.Sin(2 * g)) * Deg;
            double eps = (23.439 - 0.0000004 * n) * Deg;
            return new Vector3(
                (float)Math.Cos(lambda),
                (float)(Math.Cos(eps) * Math.Sin(lambda)),
                (float)(Math.Sin(eps) * Math.Sin(lambda))).normalized;
        }

        /// <summary>Cylindrical Earth-shadow test. Works in any frame as long as both vectors use it.</summary>
        public static bool InEarthShadow(Vector3 positionKm, Vector3 sunDirection)
        {
            float along = Vector3.Dot(positionKm, sunDirection);
            if (along >= 0f) return false;
            Vector3 perpendicular = positionKm - along * sunDirection;
            return perpendicular.magnitude < EarthRadiusKm;
        }

        public static string FormatUtc(double unixSeconds) =>
            UnixEpoch.AddSeconds(unixSeconds).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + " UTC";

        public static bool IsFinite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
              float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    }
}
