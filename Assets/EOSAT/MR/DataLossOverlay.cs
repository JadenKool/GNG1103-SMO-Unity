// DataLossOverlay.cs - dims a screen and shows "PREDICTION MODE" while telemetry is lost.
// Put it on a full-size child of the screen's Canvas that has a CanvasGroup, a dark Image and a Text.
// It never blocks clicks (so the Connection screen can still be used to reconnect).

using UnityEngine;
using UnityEngine.UI;

namespace Eosat.MR
{
    [RequireComponent(typeof(CanvasGroup))]
    public class DataLossOverlay : MonoBehaviour
    {
        public TelemetryHub hub;
        [Tooltip("Used for the 'predicted / frozen' wording. Optional.")]
        public SpacecraftController spacecraft;
        public Text message;
        [Tooltip("How dark the screen gets (0-1).")]
        [Range(0f, 1f)] public float dimAlpha = 1f;
        [Tooltip("Seconds to fade in / out (gentle, never a flash).")]
        public float fadeSeconds = 0.6f;

        CanvasGroup group;
        float nextText;

        void Awake()
        {
            group = GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
        }

        void Update()
        {
            bool lost = hub && hub.PredictionMode;
            group.alpha = Mathf.MoveTowards(group.alpha, lost ? dimAlpha : 0f, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds));
            if (!lost || !message || Time.unscaledTime < nextText) return;
            nextText = Time.unscaledTime + 0.25f;

            float s = hub.SecondsSinceData;
            float limit = spacecraft ? spacecraft.maxPredictionSec : 60f;
            string state = s < limit
                ? "Showing <b>predicted</b> position from the last known orbit and spin"
                : "Prediction limit reached - position <b>frozen</b>";
            message.text = "<size=54><color=#FFB000><b>PREDICTION MODE</b></color></size>\n" +
                           $"Telemetry lost {s:0} s ago  ({hub.SourceLabel})\n{state}\n" +
                           "<size=24>Reconnects automatically - or pick a source on the Connection screen</size>";
        }
    }
}
