// TelemetryLoadingScreen.cs - startup loading screen shown while TelemetryHub fills its startup buffer
// (TelemetryHub.startupBufferSeconds, e.g. 60 s). Progress bar + countdown + frames cached; fades out when the
// buffer is released and the simulation starts. Put it on the loading Canvas (needs a CanvasGroup).

using UnityEngine;
using UnityEngine.UI;

namespace Eosat.MR
{
    [RequireComponent(typeof(CanvasGroup))]
    public class TelemetryLoadingScreen : MonoBehaviour
    {
        public TelemetryHub hub;
        [Tooltip("Image Type = Filled, Horizontal.")]
        public Image progressFill;
        public Text statusText;
        public float fadeSeconds = 0.6f;

        CanvasGroup group;
        float nextText;

        void Awake()
        {
            group = GetComponent<CanvasGroup>();
            group.alpha = 1f;
            if (!hub || !hub.Buffering) gameObject.SetActive(false);
        }

        void Update()
        {
            if (progressFill) progressFill.fillAmount = hub.BufferProgress;
            if (!hub.Buffering)
            {
                group.alpha = Mathf.MoveTowards(group.alpha, 0f, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds));
                if (group.alpha <= 0f) gameObject.SetActive(false);
                return;
            }
            if (!statusText || Time.unscaledTime < nextText) return;
            nextText = Time.unscaledTime + 0.25f;
            string data = hub.History.Count > 0
                ? $"{hub.History.Count} frames cached  ({hub.SourceLabel}, {hub.PacketRateHz:0.0} Hz)"
                : $"No data yet  ({hub.SourceLabel})";
            statusText.text = LocalizationManager.Tr(
                "<size=46><b>BUFFERING TELEMETRY</b></size>\n" +
                "Collecting 60 s of telemetry before the simulation starts\n" +
                $"<size=40><color=#4DC8FF><b>{hub.BufferSecondsLeft:0} s</b></color></size> remaining\n{data}");
        }
    }
}
