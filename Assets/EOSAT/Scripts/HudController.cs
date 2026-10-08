using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Eosat
{
    /// <summary>
    /// Minimal HUD: two status lines (target spec: fewer than 3 lines of text), one warning banner
    /// that appears only when needed, a full-screen dim when the feed stalls, and a first-run guide (H).
    /// Built at runtime so it needs no prefab or extra fonts.
    /// </summary>
    public class HudController : MonoBehaviour
    {
        public SpacecraftController spacecraft;
        public TelemetryHub hub;
        public DeployablesAnimator deployables;
        [Tooltip("Pixels at 1080p. 22 px is about 16 pt, above the 12 pt legibility target.")]
        public int fontSize = 22;
        public bool showGuideOnStart = true;
        [Range(0f, 1f)] public float stalledDim = 0.45f;

        [Header("VR panel")]
        [Tooltip("Distance of the floating HUD panel in front of the headset (m).")]
        public float vrPanelDistance = 2f;
        [Tooltip("World size of one UI pixel in VR (m). 0.0016 makes the 1300 px panel ~2 m wide.")]
        public float vrPixelSize = 0.0016f;
        public float vrFollowSpeed = 2.5f;

        Text status, banner, hint;
        Image dim;
        GameObject guide, bannerRoot;
        float dimAlpha;
        Canvas canvas;
        CanvasScaler scaler;
        bool? vrMode;
        bool snapPanel;
        InputAction guideButton;

        const string DesktopHint = "H  guide     1  spacecraft view     2  Earth view     drag  orbit     scroll  zoom";
        const string VrHint = "B / Y  guide     A / X  switch view     thumbstick  turn & zoom";

        void Awake() => Build();

        void OnEnable()
        {
            guideButton = new InputAction("EOSAT Guide", InputActionType.Button);
            guideButton.AddBinding("<XRController>{RightHand}/{SecondaryButton}");
            guideButton.AddBinding("<XRController>{LeftHand}/{SecondaryButton}");
            guideButton.AddBinding("<Gamepad>/buttonNorth");
            guideButton.Enable();
        }

        void OnDisable() { guideButton?.Disable(); guideButton?.Dispose(); }

        void Update()
        {
            bool vr = XRSettings.isDeviceActive;
            if (vrMode != vr) ApplyMode(vr);

            var kb = Keyboard.current;
            if ((kb != null && kb.hKey.wasPressedThisFrame) || guideButton.WasPressedThisFrame()) guide.SetActive(!guide.activeSelf);

            if (!spacecraft || !hub) return;

            // Graceful degradation: fade the dim in/out slowly instead of snapping.
            float targetDim = spacecraft.Stalled ? stalledDim : 0f;
            dimAlpha = Mathf.MoveTowards(dimAlpha, targetDim, Time.deltaTime * 0.3f);
            dim.color = new Color(0.05f, 0.05f, 0.07f, dimAlpha);

            status.text = spacecraft.HasData ? Line1() + "\n" + Line2() : $"Waiting for telemetry ({hub.SourceLabel}) {TcpNote()}";

            string warn = null;
            Color warnColor = new Color(1f, 0.75f, 0.2f);
            if (spacecraft.Stalled)
                warn = $"TELEMETRY STALLED {spacecraft.SecondsSinceData:F0} s  -  " +
                       (spacecraft.SecondsSinceData < spacecraft.maxPredictionSec ? "showing predicted position" : "position frozen");
            else if (spacecraft.SpinWarning)
            {
                warn = $"HIGH SPIN RATE {spacecraft.VisualSpinDegPerSec:F0} deg/s  -  motion limited for comfort";
                warnColor = new Color(1f, 0.35f, 0.3f);
            }
            else if (Time.time - hub.LastRejectRealtime < 3f)
                warn = "Bad packet ignored: " + hub.LastRejectReason;

            bannerRoot.SetActive(warn != null);
            if (warn != null) { banner.text = warn; banner.color = warnColor; }
        }

        string Line1()
        {
            string link = spacecraft.Stalled ? "<color=#ffbf33>STALLED</color>"
                        : spacecraft.Predicting ? "<color=#ffbf33>LATE</color>"
                        : "<color=#66ff88>LIVE</color>";
            return $"{link}  {hub.SourceLabel}  {hub.PacketRateHz:F1} Hz     {EciMath.FormatUtc(spacecraft.DisplayUnix)}     Alt {spacecraft.AltitudeKm:F0} km";
        }

        string Line2()
        {
            string panels = deployables ? Describe(deployables.Panels.ToString(), deployables.PanelsMoving, deployables.PanelFraction) : "?";
            string antenna = deployables
                ? (deployables.Antenna == AntennaState.Jammed ? "<color=#ff5544>JAMMED</color>"
                   : Describe(deployables.Antenna.ToString(), deployables.AntennaMoving, deployables.AntennaFraction))
                : "?";
            string power = spacecraft.InEclipse ? "<color=#8899ff>ECLIPSE</color>" : "<color=#ffe066>SUNLIGHT</color>";
            return $"Panels {panels}     Antenna {antenna}     {power}     Spin {spacecraft.PhysicalSpinDegPerSec:F1} deg/s";
        }

        static string Describe(string state, bool moving, float fraction) =>
            moving ? $"<color=#ffbf33>MOVING {fraction * 100f:F0}%</color>" : state.ToUpperInvariant();

        string TcpNote() =>
            hub.source == TelemetrySourceMode.Tcp && !hub.TcpConnected && !string.IsNullOrEmpty(hub.TcpError) ? "- " + hub.TcpError : "";

        // ---------- Desktop vs VR ----------

        void ApplyMode(bool vr)
        {
            vrMode = vr;
            var rt = (RectTransform)canvas.transform;
            if (vr)
            {
                // Screen-space overlays are invisible in a headset, so the HUD becomes a floating panel.
                scaler.enabled = false;
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = Camera.main;
                rt.sizeDelta = new Vector2(1300, 760);
                rt.localScale = Vector3.one * vrPixelSize;
                snapPanel = true;
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                rt.localScale = Vector3.one;
                scaler.enabled = true;
            }
            hint.text = vr ? VrHint : DesktopHint;
        }

        void LateUpdate()
        {
            if (vrMode != true) return;
            var cam = Camera.main;
            if (!cam) return;
            var c = cam.transform;
            var t = canvas.transform;
            Vector3 goal = c.position + c.forward * vrPanelDistance;
            // Lazy follow: the panel drifts after your gaze instead of being glued to your face.
            t.position = snapPanel ? goal : Vector3.Lerp(t.position, goal, 1f - Mathf.Exp(-vrFollowSpeed * Time.deltaTime));
            t.rotation = Quaternion.LookRotation(t.position - c.position, Vector3.up);
            snapPanel = false;
        }

        // ---------- UI construction ----------

        void Build()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGo = new GameObject("HUD Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var root = canvasGo.transform;

            dim = MakeImage(root, "Stall Dim", new Color(0, 0, 0, 0));
            Stretch(dim.rectTransform, 0);

            var panel = MakeImage(root, "Status Panel", new Color(0, 0, 0, 0.55f));
            Place(panel.rectTransform, new Vector2(0, 1), new Vector2(20, -20), new Vector2(1100, fontSize * 2 * 1.35f + 24));
            status = MakeText(panel.transform, "Status", font, fontSize, TextAnchor.UpperLeft);
            Stretch(status.rectTransform, 12);

            var bannerBg = MakeImage(root, "Banner", new Color(0, 0, 0, 0.6f));
            Place(bannerBg.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -110), new Vector2(1100, fontSize + 30));
            banner = MakeText(bannerBg.transform, "Text", font, fontSize + 4, TextAnchor.MiddleCenter);
            banner.fontStyle = FontStyle.Bold;
            Stretch(banner.rectTransform, 6);
            bannerRoot = bannerBg.gameObject;
            bannerRoot.SetActive(false);

            hint = MakeText(root, "Hint", font, fontSize - 2, TextAnchor.LowerLeft);
            Place(hint.rectTransform, new Vector2(0, 0), new Vector2(20, 16), new Vector2(1200, fontSize + 8));
            hint.text = DesktopHint;
            hint.color = new Color(1, 1, 1, 0.75f);

            var guideBg = MakeImage(root, "Guide", new Color(0.02f, 0.03f, 0.06f, 0.88f));
            Place(guideBg.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 560));
            var guideText = MakeText(guideBg.transform, "Text", font, fontSize, TextAnchor.UpperLeft);
            Stretch(guideText.rectTransform, 28);
            guideText.text =
                "<b><size=30>EOSAT-1 Visualizer - quick guide</size></b>\n\n" +
                "<b>Desktop</b>   drag to orbit, scroll to zoom, <b>1</b> spacecraft, <b>2</b> Earth\n" +
                "<b>VR</b>   look around, thumbstick turns & zooms, <b>A/X</b> switch view\n\n" +
                "<b>Spacecraft axes</b>   <color=#ff5555>X</color>  <color=#55ff66>Y</color>  <color=#5599ff>Z</color>  (body frame)\n" +
                "<b>Long axes through Earth</b>   ECI frame (Z = North Pole)\n" +
                "<b>Yellow line</b>   direction to the Sun\n" +
                "<b>Screen greys out</b>   telemetry has stalled; position is predicted\n\n" +
                "<b>Test keys (simulated feed)</b>\n" +
                "P panels    A antenna    F pause feed    B bad packet    G fast spin\n\n" +
                "Press <b>H</b> (or <b>B/Y</b> in VR) to close or reopen this guide";
            guide = guideBg.gameObject;
            guide.SetActive(showGuideOnStart);
        }

        static Image MakeImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static Text MakeText(Transform parent, string name, Font font, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = Color.white;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        static void Stretch(RectTransform rt, float pad)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, pad); rt.offsetMax = new Vector2(-pad, -pad);
        }

        static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }
    }
}
