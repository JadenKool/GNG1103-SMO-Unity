// CommandQueueBanner.cs - banner listing deploy commands queued to the Flight Commander (and Simulation Mode).
// Placed by ComfortPlacement: floats above the MR Earth, or lazily follows the top of the view in VR - never on the iPad.
// The satellite model does not change when a command is sent; the banner is the only feedback until telemetry
// shows the deployment, then the line turns green ("executed") for a few seconds and disappears.

using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Eosat.MR
{
    [RequireComponent(typeof(CanvasGroup))]
    public class CommandQueueBanner : MonoBehaviour
    {
        public SatelliteDeployControl deploy;
        public Text text;
        public float fadeSeconds = 0.3f;

        CanvasGroup group;
        float nextText;
        readonly StringBuilder sb = new StringBuilder(256);

        void Awake() { group = GetComponent<CanvasGroup>(); group.alpha = 0f; group.blocksRaycasts = false; }

        void Update()
        {
            bool any = deploy && (deploy.Commands.Count > 0 || deploy.SimulationMode);
            group.alpha = Mathf.MoveTowards(group.alpha, any ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds));
            if (!any || !text || Time.unscaledTime < nextText) return;
            nextText = Time.unscaledTime + 0.25f;
            sb.Clear();
            if (deploy.SimulationMode) sb.Append("<color=#C88CFF><b>SIMULATION MODE</b></color>  local preview - nothing is sent to the Flight Commander");
            foreach (var c in deploy.Commands)
            {
                if (sb.Length > 0) sb.Append('\n');
                if (c.verified) sb.Append("<color=#39FF6A><b>").Append(c.Label).Append("</b>  executed - telemetry confirms</color>");
                else sb.Append("<color=#FFB000><b>").Append(c.Label).Append("</b></color>  queued to the Flight Commander  ")
                       .Append(c.sentOn8104 ? "<color=#9AA6B8>(sent on 8104, " + Mathf.FloorToInt(Time.time - c.queuedAt) + " s)</color>"
                                            : "<color=#FF6A6A>(command link down - not sent)</color>");
            }
            text.text = LocalizationManager.Tr(sb.ToString());
        }
    }
}
