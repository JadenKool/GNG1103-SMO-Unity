using UnityEngine;
using UnityEngine.Events;

namespace Eosat.MR
{
    /// <summary>
    /// Drives the hovering model's DeployablesAnimator. Follows the simulator's deploy data
    /// (per-wing fractions + antenna state). A tablet toggle overrides that part ("MANUAL") until the
    /// simulator reports a new state for it, then it follows the data again.
    /// </summary>
    public class SatelliteDeployControl : MonoBehaviour
    {
        public DeployablesAnimator deployables;
        public TelemetryHub hub;
        [Tooltip("Follow the simulator's deploy states. Off = tablet only.")]
        public bool followTelemetry = true;
        public bool antennaStartsDeployed;
        public bool panelsStartDeployed;

        public UnityEvent<bool> onAntennaStateChanged;
        public UnityEvent<bool> onPanelsStateChanged;

        public bool AntennaDeployed => deployables && deployables.Antenna == AntennaState.Deployed;
        public bool PanelsDeployed => deployables && deployables.Panels == PanelState.Deployed;
        public float AntennaProgress => deployables ? deployables.AntennaFraction : 0f;
        public float PanelsProgress => deployables ? deployables.PanelFraction : 0f;
        public bool AntennaManual { get; private set; }
        public bool PanelsManual { get; private set; }

        public string AntennaStatus => Describe(AntennaDeployed, AntennaProgress) + Tag(AntennaManual);
        public string PanelsStatus =>
            (deployables && Mathf.Abs(deployables.WingPyFraction - deployables.WingMyFraction) > 0.01f
                ? $"+Y {deployables.WingPyFraction * 100f:0}%  -Y {deployables.WingMyFraction * 100f:0}%"
                : Describe(PanelsDeployed, PanelsProgress)) + Tag(PanelsManual);

        AntennaState lastDataAntenna = AntennaState.Unknown;
        int lastDataPanels = -1;

        string Tag(bool manual) => manual ? "  |  MANUAL" : (followTelemetry && hub ? "  |  SIM" : "");

        void Awake()
        {
            if (!deployables) deployables = GetComponentInChildren<DeployablesAnimator>();
        }

        void OnEnable() { if (hub) hub.SampleReceived += OnSample; }
        void OnDisable() { if (hub) hub.SampleReceived -= OnSample; }

        void Start()
        {
            if (!deployables || (followTelemetry && hub)) return;
            deployables.SetTargets(
                panelsStartDeployed ? PanelState.Deployed : PanelState.Stowed,
                antennaStartsDeployed ? AntennaState.Deployed : AntennaState.Stowed);
        }

        void OnSample(TelemetrySample s)
        {
            if (!followTelemetry || !deployables) return;

            if (s.antenna != AntennaState.Unknown)
            {
                if (s.antenna != lastDataAntenna) { lastDataAntenna = s.antenna; AntennaManual = false; }
                if (!AntennaManual) deployables.SetTargets(PanelState.Unknown, s.antenna);
            }

            int panelsKey = s.hasWings ? (s.wingPyFraction + s.wingMyFraction > 0.002f ? 1 : 0)
                          : s.panels == PanelState.Deployed ? 1 : s.panels == PanelState.Stowed ? 0 : -1;
            if (panelsKey >= 0)
            {
                if (panelsKey != lastDataPanels) { lastDataPanels = panelsKey; PanelsManual = false; }
                if (!PanelsManual)
                {
                    if (s.hasWings) deployables.SetWingFractions(s.wingPyFraction, s.wingMyFraction);
                    else deployables.SetTargets(s.panels, AntennaState.Unknown);
                }
            }
        }

        public void SetAntennaDeployed(bool deployed)
        {
            if (!deployables || AntennaDeployed == deployed) return;
            AntennaManual = true;
            deployables.SetTargets(PanelState.Unknown, deployed ? AntennaState.Deployed : AntennaState.Stowed);
            onAntennaStateChanged?.Invoke(deployed);
        }

        public void SetPanelsDeployed(bool deployed)
        {
            if (!deployables || PanelsDeployed == deployed) return;
            PanelsManual = true;
            deployables.SetTargets(deployed ? PanelState.Deployed : PanelState.Stowed, AntennaState.Unknown);
            onPanelsStateChanged?.Invoke(deployed);
        }

        public void ToggleAntenna() => SetAntennaDeployed(!AntennaDeployed);
        public void TogglePanels() => SetPanelsDeployed(!PanelsDeployed);

        /// <summary>Human-readable state, e.g. "DEPLOYING 45%".</summary>
        public static string Describe(bool target, float progress) =>
            progress >= 0.999f ? "DEPLOYED" :
            progress <= 0.001f ? "STOWED" :
            $"{(target ? "DEPLOYING" : "STOWING")} {progress * 100f:0}%";
    }
}
