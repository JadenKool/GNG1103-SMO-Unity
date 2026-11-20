// ViewpointButtonsUI.cs - the five viewpoint buttons on the wrist iPad (Free, Earth-up/Earth, Earth-up/Sun,
// body nadir, body solar panel). Highlights the active one and greys out the two spacecraft-fixed views while
// the tumble lock is on (spin above OrbitalViewerLocomotion's threshold).

using UnityEngine;
using UnityEngine.UI;

namespace Eosat.Meta
{
    public class ViewpointButtonsUI : MonoBehaviour
    {
        public OrbitalViewerLocomotion orbit;
        [Tooltip("In OrbitalViewerLocomotion.ViewMode order: Free, Earth-up look Earth, Earth-up look Sun, Body nadir, Body solar.")]
        public Button[] buttons = new Button[5];
        public Text status;
        public Color activeColor = new Color(0.2f, 0.75f, 1f, 1f);
        public Color idleColor = new Color(0.25f, 0.28f, 0.33f, 1f);

        void Start()
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                int mode = i;
                if (buttons[i]) buttons[i].onClick.AddListener(() => { if (orbit) orbit.SetViewMode(mode); });
            }
        }

        float next;
        void Update()
        {
            if (!orbit || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.2f;
            bool locked = orbit.BodyViewsLocked;
            for (int i = 0; i < buttons.Length; i++)
            {
                if (!buttons[i]) continue;
                if (i >= 3) buttons[i].interactable = !locked;
                var img = buttons[i].targetGraphic as Image;
                if (img) img.color = (int)orbit.Mode == i ? activeColor : idleColor;
            }
            if (status) status.text = LocalizationManager.Tr(locked
                ? "<color=#FFB000>TUMBLE LOCK</color> - spacecraft-fixed views off while it spins fast"
                : "View: " + Name(orbit.Mode));
        }

        static string Name(OrbitalViewerLocomotion.ViewMode m) => m switch
        {
            OrbitalViewerLocomotion.ViewMode.EarthUpLookEarth => "Earth-up, looking at Earth",
            OrbitalViewerLocomotion.ViewMode.EarthUpLookSun => "Earth-up, looking at the Sun",
            OrbitalViewerLocomotion.ViewMode.BodyNadir => "spacecraft-fixed, nadir",
            OrbitalViewerLocomotion.ViewMode.BodySolar => "spacecraft-fixed, solar panel",
            _ => "free orbit (left stick)"
        };
    }
}
