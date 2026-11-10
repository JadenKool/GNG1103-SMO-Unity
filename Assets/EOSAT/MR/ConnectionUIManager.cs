using UnityEngine;
using UnityEngine.UI;

namespace Eosat.MR
{
    /// <summary>
    /// Connection box on the tablet canvas. Hook the three buttons to SelectSimulated / SelectTcp /
    /// SelectWebSocket (done automatically if the Button fields are set). The status Text shows exactly
    /// which feed is active and appends a green CONNECTED when data is arriving or the socket is open.
    /// </summary>
    public class ConnectionUIManager : MonoBehaviour
    {
        public TelemetryHub hub;

        [Header("Source buttons (optional - auto-wired)")]
        public Button simulatedButton;
        public Button tcpButton;
        public Button webSocketButton;
        public Color activeColor = new Color(0.2f, 0.75f, 1f, 1f);
        public Color idleColor = new Color(0.25f, 0.28f, 0.33f, 1f);

        [Header("Status (legacy UI Text, Rich Text on)")]
        public Text statusText;
        [Tooltip("Optional second line for the socket error / packet rate.")]
        public Text detailText;
        [Range(1f, 20f)] public float refreshHz = 5f;
        [Tooltip("Rich-text colour of CONNECTED. Unity's named 'green' is #008000 (too dark on the tablet), so a bright green is used.")]
        public string connectedColor = "#39FF6A";

        [Header("Custom IP address")]
        [Tooltip("Type with a keyboard, or use the on-screen keypad (KeypadPress).")]
        public InputField hostField;
        public Text hostStatus;
        public int maxHostLength = 21;      // 255.255.255.255:8101

        float nextRefresh;
        string lastStatus, lastDetail;

        void Start()
        {
            if (hostField && hub && string.IsNullOrEmpty(hostField.text)) hostField.text = hub.host;
        }

        // ---- On-screen keypad (each key's OnClick passes its own character) ----
        public void KeypadPress(string key)
        {
            if (!hostField || string.IsNullOrEmpty(key)) return;
            if (hostField.text.Length + key.Length > maxHostLength) return;
            hostField.text += key;
        }
        public void KeypadBackspace() { if (hostField && hostField.text.Length > 0) hostField.text = hostField.text.Substring(0, hostField.text.Length - 1); }
        public void KeypadClear() { if (hostField) hostField.text = ""; }
        public void UseLocalhost() { if (hostField) hostField.text = "127.0.0.1"; ApplyCustomHost(); }

        /// <summary>"Set IP" button: point TCP + WebSocket at the typed address.</summary>
        public void ApplyCustomHost()
        {
            if (!hub || !hostField) return;
            bool ok = hub.SetCustomHost(hostField.text);
            if (hostStatus) hostStatus.text = ok
                ? $"<color={connectedColor}>Address set: {hub.host}</color>  (TCP {hub.port}, WebSocket 8101). Now press TCP or WS."
                : "<color=#FF6A6A>" + hub.LastHostError + "</color>";
            nextRefresh = 0f;
        }

        void OnEnable()
        {
            if (simulatedButton) simulatedButton.onClick.AddListener(SelectSimulated);
            if (tcpButton) tcpButton.onClick.AddListener(SelectTcp);
            if (webSocketButton) webSocketButton.onClick.AddListener(SelectWebSocket);
            if (hub) hub.SourceChanged += OnSourceChanged;
            nextRefresh = 0f;
        }

        void OnDisable()
        {
            if (simulatedButton) simulatedButton.onClick.RemoveListener(SelectSimulated);
            if (tcpButton) tcpButton.onClick.RemoveListener(SelectTcp);
            if (webSocketButton) webSocketButton.onClick.RemoveListener(SelectWebSocket);
            if (hub) hub.SourceChanged -= OnSourceChanged;
        }

        // ---- Public hooks for UI Buttons (OnClick) ----
        public void SelectSimulated() => Select(DataSource.SimulatedLocal);
        public void SelectTcp() => Select(DataSource.Tcp8100);
        public void SelectWebSocket() => Select(DataSource.WebSocket8101);

        public void Select(DataSource source)
        {
            if (hub) hub.SwitchDataSource(source);
        }

        void OnSourceChanged(DataSource _) => nextRefresh = 0f;

        void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 1f / refreshHz;
            Refresh();
        }

        void Refresh()
        {
            if (!hub) { SetText(statusText, ref lastStatus, "No TelemetryHub"); return; }

            bool connected = hub.ReceivingData || hub.LinkConnected;
            string status = hub.SourceLabel + "  " + (connected
                ? $"<color={connectedColor}><b>CONNECTED</b></color>"
                : hub.IsNetworkSource ? "<color=#FFB000><b>CONNECTING...</b></color>" : "<color=#FFB000><b>WAITING</b></color>");
            SetText(statusText, ref lastStatus, status);

            string detail;
            if (hub.ReceivingData) detail = $"{hub.PacketRateHz:0.0} Hz  |  {hub.GoodPackets} good, {hub.BadPackets} rejected";
            else if (hub.LinkConnected) detail = "Socket open - waiting for first frame";
            else if (hub.IsNetworkSource && !string.IsNullOrEmpty(hub.LinkError)) detail = "<color=#FF6A6A>" + hub.LinkError + "</color>";
            else detail = hub.IsNetworkSource ? "Retrying every 2 s" : "Built-in simulator / client replay";
            SetText(detailText, ref lastDetail, detail);

            Tint(simulatedButton, hub.source == DataSource.SimulatedLocal);
            Tint(tcpButton, hub.source == DataSource.Tcp8100);
            Tint(webSocketButton, hub.source == DataSource.WebSocket8101);
        }

        void Tint(Button b, bool active)
        {
            if (b && b.targetGraphic) b.targetGraphic.color = active ? activeColor : idleColor;
        }

        static void SetText(Text t, ref string cache, string value)
        {
            if (!t || cache == value) return;   // only touch the Text when it changes (no per-frame mesh rebuilds)
            cache = value;
            t.text = value;
        }
    }
}
