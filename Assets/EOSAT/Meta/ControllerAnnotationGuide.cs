// ControllerAnnotationGuide.cs  -  Meta XR Core SDK (OVRCameraRig)
// Control-layout guide anchored above a controller. Tilt the controller DOWN toward your body and it fades in;
// point it forward again and it fades out. Pitch is measured in the headset's frame, so it works however
// your head or the VR orbit rig is turned. Hysteresis stops it flickering at the threshold.
// Hierarchy: OVRCameraRig > TrackingSpace > RightHandAnchor > Controller Guide (this, CanvasGroup) > Canvas

using UnityEngine;
using UnityEngine.UI;

namespace Eosat.Meta
{
    [RequireComponent(typeof(CanvasGroup))]
    public class ControllerAnnotationGuide : MonoBehaviour
    {
        [Tooltip("Controller / hand anchor to read (RightHandAnchor). Defaults to the parent.")]
        [SerializeField] Transform controller;
        [Tooltip("CenterEyeAnchor. Auto-found if empty.")]
        [SerializeField] Transform head;

        [Header("Tilt (degrees below the headset's horizontal)")]
        [SerializeField] float showBelowPitch = 40f;
        [SerializeField] float hideAbovePitch = 25f;
        [SerializeField] float fadeSeconds = 0.25f;

        [Header("Placement")]
        [Tooltip("Metres above the controller (player's up) and toward the eyes.")]
        [SerializeField] float lift = 0.10f;
        [SerializeField] float towardEyes = 0.04f;

        [Header("Content (English; translated by LocalizationManager)")]
        [SerializeField] Text guideText;
        [SerializeField, TextArea(4, 10)] string mrGuide =
            "<b>MR CONTROLS</b>\n<color=#4DC8FF>GRIP</color>  grab the globe to spin it\n<color=#4DC8FF>TRIGGER</color>  point at a grab bar, then push / pull\n" +
            "<color=#4DC8FF>A  /  X</color>  recentre the layout\n<color=#4DC8FF>B  /  Y</color>  switch MR tabletop / VR orbit";
        [SerializeField, TextArea(4, 10)] string vrGuide =
            "<b>VR CONTROLS</b>\n<color=#4DC8FF>RIGHT STICK</color>  orbit the target - full 360°\n<color=#4DC8FF>LEFT STICK</color>  up / down = zoom\n" +
            "<color=#4DC8FF>A  /  X</color>  recentre on the target\n<color=#4DC8FF>B  /  Y</color>  switch MR tabletop / VR orbit\n<color=#4DC8FF>LEFT WRIST</color>  iPad: telemetry, deploy, connections";

        CanvasGroup group;
        bool showing;
        string shownText;

        /// <summary>How far the controller points below the headset's horizontal, in degrees (negative = above).</summary>
        public float PitchDownDeg { get; private set; }

        void Awake()
        {
            group = GetComponent<CanvasGroup>();
            group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;
            if (!controller) controller = transform.parent;
            if (!head)
            {
                var rig = FindAnyObjectByType<OVRCameraRig>();
                if (rig) head = rig.centerEyeAnchor;
            }
        }

        void OnEnable() => LocalizationManager.LanguageChanged += Relabel;
        void OnDisable() => LocalizationManager.LanguageChanged -= Relabel;
        void Relabel() => shownText = null;

        void LateUpdate()
        {
            if (!controller || !head) return;

            // Controller forward in head space: y < 0 = pointing below the eyes' horizontal.
            Vector3 f = head.InverseTransformDirection(controller.forward);
            PitchDownDeg = Mathf.Asin(Mathf.Clamp(-f.y, -1f, 1f)) * Mathf.Rad2Deg;
            if (!showing && PitchDownDeg > showBelowPitch) showing = true;
            else if (showing && PitchDownDeg < hideAbovePitch) showing = false;
            group.alpha = Mathf.MoveTowards(group.alpha, showing ? 1f : 0f, Time.deltaTime / Mathf.Max(0.01f, fadeSeconds));
            if (group.alpha <= 0f) return;

            // Float above the controller and face the eyes (canvas is read from its -Z side).
            Vector3 up = head.parent ? head.parent.up : Vector3.up;   // player's up (the rig tilts while orbiting in VR)
            Vector3 pos = controller.position + up * lift;
            Vector3 toEyes = head.position - pos;
            if (toEyes.sqrMagnitude > 1e-6f) pos += toEyes.normalized * towardEyes;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - head.position, up));

            if (guideText)
            {
                var sw = EnvironmentSwitcher.Instance;
                string text = sw && !sw.IsMR ? vrGuide : mrGuide;
                if (text != shownText) { shownText = text; guideText.text = LocalizationManager.Tr(text); }
            }
        }
    }
}
