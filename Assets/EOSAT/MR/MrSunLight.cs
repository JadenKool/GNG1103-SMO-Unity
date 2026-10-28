using UnityEngine;

namespace Eosat.MR
{
    /// <summary>
    /// Points the scene's Sun light along the real Sun direction for the displayed sim time
    /// (from EarthController), expressed in the tabletop's ECI frame - so the globe's day/night
    /// side and the spacecraft lighting match the simulator wherever the globe is placed.
    /// Also positions a small Sun marker out from the globe in that direction.
    /// </summary>
    [DefaultExecutionOrder(150)] // after EarthController (100)
    public class MrSunLight : MonoBehaviour
    {
        public EarthController earth;
        [Tooltip("The tabletop ECI frame (globe centre, inertial axes).")]
        public Transform eciFrame;
        public Light sunLight;
        public Transform sunMarker;
        [Tooltip("Distance of the Sun marker from the globe centre (m).")]
        public float markerDistance = 0.42f;

        public Vector3 SunDirWorld { get; private set; } = Vector3.right;

        void LateUpdate()
        {
            if (!earth || !eciFrame) return;
            SunDirWorld = eciFrame.TransformDirection(earth.SunDirUnity).normalized;
            if (sunLight) sunLight.transform.rotation = Quaternion.LookRotation(-SunDirWorld);
            if (sunMarker) sunMarker.position = eciFrame.position + SunDirWorld * markerDistance;
        }
    }
}
