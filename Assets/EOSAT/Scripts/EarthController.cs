using UnityEngine;

namespace Eosat
{
    /// <summary>
    /// Rotates the Earth by Greenwich sidereal time (so continents sit correctly under the ECI frame)
    /// and points the Sun light along the real Sun direction for the displayed time.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class EarthController : MonoBehaviour
    {
        public SpacecraftController spacecraft;
        public Transform earthBody;
        public Light sunLight;
        public Transform sunVisual;
        public float sunVisualDistance = 400f;

        public Vector3 SunDirUnity { get; private set; } = Vector3.right;
        public double CurrentUnix { get; private set; }

        void Update()
        {
            CurrentUnix = spacecraft && spacecraft.HasData ? spacecraft.DisplayUnix : EciMath.UnixNow();
            double jd = EciMath.JulianDate(CurrentUnix);

            // Earth rotates +GMST about ECI +Z; in Unity's reflected frame that is -GMST about +Y.
            if (earthBody) earthBody.localRotation = Quaternion.AngleAxis(-(float)EciMath.GmstDegrees(jd), Vector3.up);

            SunDirUnity = EciMath.EciToUnity(EciMath.SunDirectionEci(jd));
            if (sunLight) sunLight.transform.rotation = Quaternion.LookRotation(-SunDirUnity);
            if (sunVisual) sunVisual.position = SunDirUnity * sunVisualDistance;
        }
    }
}
