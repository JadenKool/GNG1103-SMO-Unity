using UnityEngine;

namespace Eosat
{
    /// <summary>Animates solar panel wings and the antenna between stowed / deployed / jammed.</summary>
    public class DeployablesAnimator : MonoBehaviour
    {
        public Transform panelHingeLeft, panelHingeRight, antennaHinge;
        public Renderer antennaRenderer;

        [Tooltip("Target spec: 30 s solar panel deployment animation.")]
        public float panelDeploySeconds = 30f;
        public float antennaDeploySeconds = 5f;
        public float panelStowedAngle = 90f;
        public float antennaStowedAngle = 90f;
        [Range(0f, 1f)] public float antennaJammedFraction = 0.4f;
        public Color antennaColor = new Color(0.85f, 0.85f, 0.85f);
        public Color antennaJammedColor = new Color(1f, 0.25f, 0.2f);

        public PanelState Panels { get; private set; } = PanelState.Stowed;
        public AntennaState Antenna { get; private set; } = AntennaState.Stowed;
        public float PanelFraction { get; private set; }
        public float AntennaFraction { get; private set; }
        public bool PanelsMoving => !Mathf.Approximately(PanelFraction, PanelTarget);
        public bool AntennaMoving => !Mathf.Approximately(AntennaFraction, AntennaTarget);

        float PanelTarget => Panels == PanelState.Deployed ? 1f : 0f;
        float AntennaTarget => Antenna == AntennaState.Deployed ? 1f : Antenna == AntennaState.Jammed ? antennaJammedFraction : 0f;

        MaterialPropertyBlock mpb;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public void SetTargets(PanelState panels, AntennaState antenna)
        {
            if (panels != PanelState.Unknown) Panels = panels;
            if (antenna != AntennaState.Unknown) Antenna = antenna;
        }

        void Update()
        {
            PanelFraction = Mathf.MoveTowards(PanelFraction, PanelTarget, Time.deltaTime / Mathf.Max(0.01f, panelDeploySeconds));
            AntennaFraction = Mathf.MoveTowards(AntennaFraction, AntennaTarget, Time.deltaTime / Mathf.Max(0.01f, antennaDeploySeconds));

            float pa = Mathf.Lerp(panelStowedAngle, 0f, PanelFraction);
            if (panelHingeRight) panelHingeRight.localRotation = Quaternion.Euler(0f, pa, 0f);
            if (panelHingeLeft) panelHingeLeft.localRotation = Quaternion.Euler(0f, -pa, 0f);
            if (antennaHinge) antennaHinge.localRotation = Quaternion.Euler(Mathf.Lerp(antennaStowedAngle, 0f, AntennaFraction), 0f, 0f);

            if (antennaRenderer)
            {
                mpb ??= new MaterialPropertyBlock();
                antennaRenderer.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColor, Antenna == AntennaState.Jammed ? antennaJammedColor : antennaColor);
                antennaRenderer.SetPropertyBlock(mpb);
            }
        }
    }
}
