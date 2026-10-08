using UnityEngine;

namespace Eosat
{
    /// <summary>Yellow line from the spacecraft toward the Sun.</summary>
    [DefaultExecutionOrder(150)]
    [RequireComponent(typeof(LineRenderer))]
    public class SunVector : MonoBehaviour
    {
        public EarthController earth;
        public float length = 0.5f;
        LineRenderer line;

        void Awake() { line = GetComponent<LineRenderer>(); line.useWorldSpace = true; line.positionCount = 2; }

        void LateUpdate()
        {
            if (!earth) return;
            Vector3 p = transform.position;
            line.SetPosition(0, p);
            line.SetPosition(1, p + earth.SunDirUnity * length);
        }
    }
}
