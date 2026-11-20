using UnityEngine;

namespace Eosat
{
    /// <summary>Yellow line from the spacecraft toward the Sun, and (optional) a second line toward the Earth's centre.</summary>
    [DefaultExecutionOrder(150)]
    [RequireComponent(typeof(LineRenderer))]
    public class SunVector : MonoBehaviour
    {
        public EarthController earth;
        public float length = 0.5f;
        [Tooltip("Optional: line from the spacecraft toward the Earth's centre (nadir), same length.")]
        public LineRenderer earthLine;
        LineRenderer line;

        void Awake()
        {
            line = GetComponent<LineRenderer>(); line.useWorldSpace = true; line.positionCount = 2;
            if (earthLine) { earthLine.useWorldSpace = true; earthLine.positionCount = 2; }
        }

        void LateUpdate()
        {
            if (!earth) return;
            Vector3 p = transform.position;
            line.SetPosition(0, p);
            line.SetPosition(1, p + earth.SunDirUnity * length);
            if (earthLine)
            {
                earthLine.SetPosition(0, p);
                Vector3 centre = earth.earthBody ? earth.earthBody.position : earth.transform.position;
                earthLine.SetPosition(1, p + (centre - p).normalized * length);
            }
        }
    }
}
