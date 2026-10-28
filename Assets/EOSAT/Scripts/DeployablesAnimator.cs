using UnityEngine;

namespace Eosat
{
    /// <summary>
    /// Animates solar panel wings and the antenna between stowed / deployed / jammed.
    /// Two ways to drive the wings:
    ///  - SetTargets(state): both wings animate together over panelDeploySeconds (manual buttons, legacy feed).
    ///  - SetWingFractions(py, my): each wing follows the simulator's own deploy fraction (client smo-vr/1 feed).
    /// </summary>
    public class DeployablesAnimator : MonoBehaviour
    {
        [Tooltip("Right hinge = +Y wing, Left hinge = -Y wing.")]
        public Transform panelHingeLeft, panelHingeRight, antennaHinge;
        public Renderer antennaRenderer;

        [Tooltip("Target spec: 30 s solar panel deployment animation.")]
        public float panelDeploySeconds = 30f;
        public float antennaDeploySeconds = 5f;
        [Tooltip("How quickly a wing catches up to the simulator's fraction (1/s). Higher = tighter follow.")]
        public float wingFollowRate = 4f;
        public float panelStowedAngle = 90f;
        public float antennaStowedAngle = 90f;
        [Range(0f, 1f)] public float antennaJammedFraction = 0.4f;
        public Color antennaColor = new Color(0.85f, 0.85f, 0.85f);
        public Color antennaJammedColor = new Color(1f, 0.25f, 0.2f);

        public PanelState Panels { get; private set; } = PanelState.Stowed;
        public AntennaState Antenna { get; private set; } = AntennaState.Stowed;
        public float WingPyFraction { get; private set; }
        public float WingMyFraction { get; private set; }
        public float PanelFraction => (WingPyFraction + WingMyFraction) * 0.5f;
        public float AntennaFraction { get; private set; }
        public bool PanelsMoving => Mathf.Abs(WingPyFraction - pyTarget) > 0.01f || Mathf.Abs(WingMyFraction - myTarget) > 0.01f
                                    || (dataWings && (InBetween(pyTarget) || InBetween(myTarget)));
        public bool AntennaMoving => !Mathf.Approximately(AntennaFraction, AntennaTarget);

        float AntennaTarget => Antenna == AntennaState.Deployed ? 1f : Antenna == AntennaState.Jammed ? antennaJammedFraction : 0f;

        bool dataWings;              // true while the simulator is giving per-wing fractions
        float pyTarget, myTarget;

        MaterialPropertyBlock mpb;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        static bool InBetween(float f) => f > 0.001f && f < 0.999f;

        public void SetTargets(PanelState panels, AntennaState antenna)
        {
            if (panels != PanelState.Unknown)
            {
                Panels = panels;
                dataWings = false;
                pyTarget = myTarget = panels == PanelState.Deployed ? 1f : 0f;
            }
            if (antenna != AntennaState.Unknown) Antenna = antenna;
        }

        /// <summary>Per-wing deploy fraction from the simulator (0 = stowed, 1 = deployed).</summary>
        public void SetWingFractions(float wingPy, float wingMy)
        {
            dataWings = true;
            pyTarget = Mathf.Clamp01(wingPy);
            myTarget = Mathf.Clamp01(wingMy);
            Panels = pyTarget <= 0.001f && myTarget <= 0.001f ? PanelState.Stowed : PanelState.Deployed;
        }

        void Update()
        {
            if (dataWings)
            {
                float k = 1f - Mathf.Exp(-wingFollowRate * Time.deltaTime);
                WingPyFraction = Mathf.Lerp(WingPyFraction, pyTarget, k);
                WingMyFraction = Mathf.Lerp(WingMyFraction, myTarget, k);
            }
            else
            {
                float step = Time.deltaTime / Mathf.Max(0.01f, panelDeploySeconds);
                WingPyFraction = Mathf.MoveTowards(WingPyFraction, pyTarget, step);
                WingMyFraction = Mathf.MoveTowards(WingMyFraction, myTarget, step);
            }
            AntennaFraction = Mathf.MoveTowards(AntennaFraction, AntennaTarget, Time.deltaTime / Mathf.Max(0.01f, antennaDeploySeconds));

            if (panelHingeRight) panelHingeRight.localRotation = Quaternion.Euler(0f, Mathf.Lerp(panelStowedAngle, 0f, WingPyFraction), 0f);
            if (panelHingeLeft) panelHingeLeft.localRotation = Quaternion.Euler(0f, -Mathf.Lerp(panelStowedAngle, 0f, WingMyFraction), 0f);
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
