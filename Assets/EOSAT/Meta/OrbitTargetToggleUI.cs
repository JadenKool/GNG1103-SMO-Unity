// OrbitTargetToggleUI.cs - the "Camera target" button on the VR console.
// Press = switch OrbitalViewerLocomotion between Follow Earth and Follow Satellite; the label shows the current one.

using UnityEngine;
using UnityEngine.UI;

namespace Eosat.Meta
{
    public class OrbitTargetToggleUI : MonoBehaviour
    {
        public OrbitalViewerLocomotion orbit;
        public Button button;
        public Text label;
        public Image background;
        public Color earthColor = new Color(0.2f, 0.55f, 1f, 1f);
        public Color satelliteColor = new Color(1f, 0.7f, 0.15f, 1f);

        void OnEnable()
        {
            if (button) button.onClick.AddListener(OnClick);
            if (orbit) orbit.TargetChanged += OnChanged;
            Refresh();
        }

        void OnDisable()
        {
            if (button) button.onClick.RemoveListener(OnClick);
            if (orbit) orbit.TargetChanged -= OnChanged;
        }

        void OnClick() { if (orbit) orbit.ToggleTarget(); }
        void OnChanged(OrbitalViewerLocomotion.Target _) => Refresh();

        void Refresh()
        {
            bool sat = orbit && orbit.FollowingSatellite;
            if (label) label.text = sat ? "CAMERA TARGET:  FOLLOW SATELLITE\n<size=20>position only - it spins, you stay steady. Press for Earth.</size>"
                                        : "CAMERA TARGET:  FOLLOW EARTH\n<size=20>Press to follow the satellite.</size>";
            if (background) background.color = sat ? satelliteColor : earthColor;
        }
    }
}
