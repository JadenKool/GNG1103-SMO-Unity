using UnityEngine;

namespace Eosat
{
    /// <summary>Keeps axis labels facing the camera.</summary>
    [DefaultExecutionOrder(200)]
    public class Billboard : MonoBehaviour
    {
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam) transform.rotation = cam.transform.rotation;
        }
    }
}
