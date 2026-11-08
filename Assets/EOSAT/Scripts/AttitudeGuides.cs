using UnityEngine;

namespace Eosat
{
    /// <summary>
    /// Extra guides around the spacecraft so an operator can read how it is moving and turning,
    /// not just where its body X/Y/Z axes point:
    ///  - DIRECTION OF TRAVEL: velocity relative to the rotating Earth (orange arrow + km/s).
    ///  - QUATERNION: the telemetry attitude quaternion q (body -> ECI) as text, and drawn as what it means:
    ///    "rotate theta degrees about this axis" (magenta axis arrow + arc of theta).
    ///  - SPIN: while the attitude is changing, the instantaneous spin axis and rate (cyan arrow + curved arrow, deg/s).
    /// All lengths are in scene units around the (enlarged) spacecraft model, like the body axes.
    /// </summary>
    [DefaultExecutionOrder(160)]
    public class AttitudeGuides : MonoBehaviour
    {
        public SpacecraftController spacecraft;
        public Material travelMaterial, quatMaterial, spinMaterial;
        public Font font;

        [Header("Sizes (scene units)")]
        public float travelLength = 0.55f;
        public float quatAxisLength = 0.45f;
        public float arcRadius = 0.22f;
        public float lineWidth = 0.006f;
        public float labelSize = 0.0032f;

        [Header("Spin")]
        [Tooltip("Show the spin arrow only above this rate (deg/s of simulator time).")]
        public float spinShowAbove = 0.05f;
        public bool showGroundRelativeVelocity = true;

        public Vector3 TravelDirUnity { get; private set; }
        public float SpeedKmS { get; private set; }
        public float SpinDegPerSec { get; private set; }

        const float EarthRateRadS = 7.2921159e-5f;      // sidereal rotation rate
        const int ArcSegments = 32;

        LineRenderer travelLine, quatAxisLine, quatArc, spinAxisLine, spinArc;
        TextMesh travelLabel, quatLabel, spinLabel;
        Vector3 prevPosKm; double prevT = double.NaN;
        Quaternion prevRot; double prevRotT = double.NaN;
        Vector3 spinAxisUnity = Vector3.up;
        float nextText;

        void Awake()
        {
            if (!spacecraft) spacecraft = GetComponent<SpacecraftController>();
            if (!font) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            travelLine = Line("Direction of Travel", travelMaterial, new Color(1f, 0.55f, 0.1f), true);
            quatAxisLine = Line("Quaternion Axis", quatMaterial, new Color(1f, 0.3f, 0.9f), true);
            quatArc = Line("Quaternion Angle Arc", quatMaterial, new Color(1f, 0.3f, 0.9f, 0.85f), false);
            spinAxisLine = Line("Spin Axis", spinMaterial, new Color(0.2f, 0.95f, 1f), true);
            spinArc = Line("Spin Direction Arc", spinMaterial, new Color(0.2f, 0.95f, 1f, 0.9f), true);
            quatArc.positionCount = ArcSegments + 1;
            spinArc.positionCount = ArcSegments + 1;
            travelLabel = Label("Travel Label", new Color(1f, 0.7f, 0.35f));
            quatLabel = Label("Quaternion Label", new Color(1f, 0.6f, 0.95f));
            spinLabel = Label("Spin Label", new Color(0.55f, 0.97f, 1f));
            SetVisible(false);
        }

        LineRenderer Line(string name, Material m, Color c, bool arrowHead)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.sharedMaterial = m;
            lr.startColor = lr.endColor = c;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCapVertices = 2;
            if (arrowHead)
                lr.widthCurve = new AnimationCurve(new Keyframe(0f, lineWidth), new Keyframe(0.86f, lineWidth),
                                                   new Keyframe(0.87f, lineWidth * 3.2f), new Keyframe(1f, 0f));
            else lr.widthMultiplier = lineWidth * 0.8f;
            return lr;
        }

        TextMesh Label(string name, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var tm = go.AddComponent<TextMesh>();
            tm.font = font; tm.fontSize = 100; tm.characterSize = labelSize;
            tm.anchor = TextAnchor.MiddleLeft; tm.color = c; tm.richText = true;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            go.AddComponent<Billboard>();
            return tm;
        }

        Renderer[] own;
        bool visible = true;

        void SetVisible(bool on)
        {
            if (visible == on && own != null) return;
            visible = on;
            if (own == null)
            {
                var list = new System.Collections.Generic.List<Renderer>();
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                    if (r.transform.parent == transform && (r is LineRenderer || r.GetComponent<TextMesh>())) list.Add(r);
                own = list.ToArray();
            }
            foreach (var r in own) r.enabled = on;
        }

        void LateUpdate()
        {
            if (!spacecraft || !spacecraft.HasData) { SetVisible(false); return; }
            SetVisible(true);
            Vector3 p = transform.position;
            double t = spacecraft.DisplayUnix;
            var cam = Camera.main;
            Vector3 camUp = cam ? cam.transform.up : Vector3.up;     // keeps the three labels from stacking on each other
            Vector3 posKm = transform.localPosition / spacecraft.unitsPerKm;    // Unity axes, km

            // ---- Direction of travel (relative to Earth) ----
            if (!double.IsNaN(prevT) && t - prevT > 0.2)
            {
                Vector3 v = (posKm - prevPosKm) / (float)(t - prevT);          // inertial, km/s
                if (showGroundRelativeVelocity)
                    v -= Vector3.Cross(-EarthRateRadS * Vector3.up, posKm);     // remove Earth's spin (-GMST about Unity +Y)
                if (v.sqrMagnitude > 1e-6f)
                {
                    TravelDirUnity = Vector3.Slerp(TravelDirUnity == Vector3.zero ? v.normalized : TravelDirUnity, v.normalized, 0.5f).normalized;
                    SpeedKmS = Mathf.Lerp(SpeedKmS <= 0f ? v.magnitude : SpeedKmS, v.magnitude, 0.3f);
                }
                prevPosKm = posKm; prevT = t;
            }
            else if (double.IsNaN(prevT) || t < prevT) { prevPosKm = posKm; prevT = t; }

            Vector3 tip = p + TravelDirUnity * travelLength;
            travelLine.SetPosition(0, p); travelLine.SetPosition(1, tip);
            travelLabel.transform.position = tip + TravelDirUnity * 0.03f;

            // ---- Quaternion as axis + angle (exactly the rotation the model is drawn with) ----
            transform.rotation.ToAngleAxis(out float ang, out Vector3 axis);
            if (ang > 180f) { ang = 360f - ang; axis = -axis; }
            if (axis.sqrMagnitude < 1e-6f || float.IsNaN(axis.x)) axis = Vector3.up;
            quatAxisLine.SetPosition(0, p - axis * quatAxisLength * 0.35f);
            quatAxisLine.SetPosition(1, p + axis * quatAxisLength);
            DrawArc(quatArc, p, axis, ang, arcRadius);
            quatLabel.transform.position = p + axis * (quatAxisLength + 0.03f) + camUp * 0.03f;

            // ---- Spin (rate of change of the quaternion) ----
            Quaternion rot = transform.rotation;
            if (!double.IsNaN(prevRotT) && t - prevRotT > 0.2)
            {
                (rot * Quaternion.Inverse(prevRot)).ToAngleAxis(out float d, out Vector3 sAx);
                if (d > 180f) { d = 360f - d; sAx = -sAx; }
                float rate = d / (float)(t - prevRotT);
                if (!float.IsNaN(sAx.x) && sAx.sqrMagnitude > 1e-6f && d > 1e-3f)
                    spinAxisUnity = Vector3.Slerp(spinAxisUnity, sAx.normalized, 0.5f).normalized;
                SpinDegPerSec = Mathf.Lerp(SpinDegPerSec, float.IsNaN(rate) ? 0f : rate, 0.3f);
                prevRot = rot; prevRotT = t;
            }
            else if (double.IsNaN(prevRotT) || t < prevRotT) { prevRot = rot; prevRotT = t; }

            bool spinning = SpinDegPerSec > spinShowAbove;
            spinAxisLine.enabled = spinArc.enabled = spinLabel.GetComponent<Renderer>().enabled = spinning;
            if (spinning)
            {
                spinAxisLine.SetPosition(0, p);
                spinAxisLine.SetPosition(1, p + spinAxisUnity * quatAxisLength * 0.9f);
                DrawArc(spinArc, p + spinAxisUnity * quatAxisLength * 0.55f, spinAxisUnity, 270f, arcRadius * 0.55f);
                spinLabel.transform.position = p + spinAxisUnity * (quatAxisLength * 0.9f + 0.03f) - camUp * 0.06f;
            }

            // Text at 5 Hz (no per-frame mesh rebuilds).
            if (Time.unscaledTime < nextText) return;
            nextText = Time.unscaledTime + 0.2f;
            travelLabel.text = $"<b>Direction of travel</b>\n{SpeedKmS:0.00} km/s {(showGroundRelativeVelocity ? "rel. Earth" : "inertial")}";
            var s = spacecraft.Latest;
            Vector3 eciAxis = EciMath.UnityToEci(axis);
            quatLabel.text = s == null ? "" :
                $"<b>Attitude quaternion</b> (body→ECI)\nq = [{s.qx:+0.000;-0.000}, {s.qy:+0.000;-0.000}, {s.qz:+0.000;-0.000}, {s.qw:+0.000;-0.000}]  (x, y, z, w)\n" +
                $"= turn <b>{ang:0.0}°</b> about axis ({eciAxis.x:+0.00;-0.00}, {eciAxis.y:+0.00;-0.00}, {eciAxis.z:+0.00;-0.00}) ECI";
            spinLabel.text = $"<b>Spinning</b> {SpinDegPerSec:0.00}°/s";
        }

        static void DrawArc(LineRenderer lr, Vector3 centre, Vector3 axis, float degrees, float radius)
        {
            Vector3 perp = Vector3.Cross(axis, Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up).normalized;
            for (int i = 0; i <= ArcSegments; i++)
            {
                float a = degrees * i / ArcSegments;
                lr.SetPosition(i, centre + Quaternion.AngleAxis(a, axis) * perp * radius);
            }
        }
    }
}
