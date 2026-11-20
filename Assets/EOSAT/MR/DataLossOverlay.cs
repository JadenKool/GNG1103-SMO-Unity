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
            // Catch system: < 1 Hz, > 60 % dropouts in 20 s, or > 7 s without data -> dim for comfort.
            bool lost = hub && hub.Degraded;
            // LOST (> 60 s) dims a little harder than DELAYED / GHOST.
            float target = !lost ? 0f : hub.State == DataState.Lost ? Mathf.Min(1f, dimAlpha + 0.1f) : dimAlpha;
            group.alpha = Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds));
            if (!lost || !message || Time.unscaledTime < nextText) return;
            nextText = Time.unscaledTime + 0.25f;

            var st = hub.State;
            message.text = LocalizationManager.Tr("<size=54><color=#FFB000><b>PREDICTION MODE</b></color></size>\n" +
                           $"<color={StateColor(st)}><b>{st.ToString().ToUpperInvariant()}</b></color>   {hub.DegradedReason}  ({hub.SourceLabel})\n" +
                           "Showing <b>predicted</b> position from the last known orbit and spin\n" +
                           "<size=24>Smooth 3 s re-join when data returns - reconnects automatically</size>");
        }

        /// <summary>Data-state colours from the Deliverable D/E design (LIVE green, DELAYED orange, GHOST purple, LOST red).</summary>
        public static string StateColor(DataState s) => s switch
        {
            DataState.Live => "#16A34A",
            DataState.Delayed => "#EA8A0C",
            DataState.Ghost => "#7C5CF0",
            DataState.Lost => "#E11D48",
            _ => "#9AA6B8"
        };
    }
}
