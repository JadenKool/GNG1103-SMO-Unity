// QuaternionAxesGizmo.cs - X (red), Y (green), Z (blue) body axes drawn on the satellite.
// Put it on a child of the satellite model: SpacecraftController writes the telemetry quaternion (body -> ECI,
// smoothed) into the model's rotation every frame, and these local-space lines turn with it in real time.
// Body axes in Unity local space (same convention as EosatSceneBuilder): body X = +x, body Y = +z, body Z = +y.

using UnityEngine;

namespace Eosat
{
    public class QuaternionAxesGizmo : MonoBehaviour
    {
        [Tooltip("Axis length in this object's local units.")]
        public float length = 1f;
        [Tooltip("Line width in metres (world).")]
        public float width = 0.002f;
        public Material material;

        LineRenderer x, y, z;
        static Material fallback;   // one shared vertex-colour material when none is assigned

        void OnEnable()
        {
            x = Line("Axis X (red)", Color.red, Vector3.right);
            y = Line("Axis Y (green)", Color.green, Vector3.forward);
            z = Line("Axis Z (blue)", new Color(0.2f, 0.45f, 1f), Vector3.up);
        }

        void OnValidate() { if (x) { Set(x, Vector3.right); Set(y, Vector3.forward); Set(z, Vector3.up); } }

        LineRenderer Line(string name, Color c, Vector3 dir)
        {
            var t = transform.Find(name);
            var go = t ? t.gameObject : new GameObject(name);
            go.transform.SetParent(transform, false);
            go.hideFlags = HideFlags.DontSave;            // built at runtime on enable; never saved into the scene
            var lr = go.GetComponent<LineRenderer>();
            if (!lr) lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = 2;
            lr.startColor = lr.endColor = c;
            lr.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.85f, 1f), new Keyframe(0.86f, 2.2f), new Keyframe(1f, 0f)); // arrowhead
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (!material && !fallback) fallback = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.DontSave };
            lr.sharedMaterial = material ? material : fallback;
            Set(lr, dir);
            return lr;
        }

        void Set(LineRenderer lr, Vector3 dir)
        {
            lr.widthMultiplier = width;
            lr.SetPosition(0, Vector3.zero);
            lr.SetPosition(1, dir * length);
        }
    }
}
