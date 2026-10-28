using UnityEngine;

namespace Eosat.MR
{
    /// <summary>
    /// The hovering EOSAT-1 model above the globe. At true scale the spacecraft orbits only ~9 mm above
    /// the 24 cm globe (shown there as the yellow dot), so this full-size copy mirrors the real
    /// spacecraft's attitude (same rotation, incl. tumble and slews) and shows the deployables.
    /// </summary>
    [DefaultExecutionOrder(120)] // after SpacecraftController
    public class DetailViewMirror : MonoBehaviour
    {
        public SpacecraftController spacecraft;
        [Tooltip("Model root, hidden until the first telemetry frame.")]
        public GameObject model;
        [Tooltip("Slow idle bob so it reads as hovering (m).")]
        public float bobAmplitude = 0.004f;
        public float bobPeriod = 4f;

        Vector3 basePos;

        void Start()
        {
            basePos = transform.localPosition;
            if (model) model.SetActive(false);
        }

        void LateUpdate()
        {
            bool has = spacecraft && spacecraft.HasData;
            if (model && model.activeSelf != has) model.SetActive(has);
            if (!has) return;
            transform.rotation = spacecraft.transform.rotation;
            transform.localPosition = basePos + Vector3.up * bobAmplitude * Mathf.Sin(Time.time * 2f * Mathf.PI / bobPeriod);
        }
    }
}
