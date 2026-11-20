using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Eosat.MR
{
    public enum DeployCommand { PanelA, PanelB, PanelsAB, Antenna }

    /// <summary>A deploy command waiting for the Flight Commander, then for telemetry to show it happened.</summary>
    public class PendingCommand
    {
        public DeployCommand command;
        public float queuedAt;
        public bool sentOn8104;
        public bool verified;
        public float verifiedAt;
        public string Label => command switch
        {
            DeployCommand.PanelA => "DEPLOY PANEL A",
            DeployCommand.PanelB => "DEPLOY PANEL B",
            DeployCommand.PanelsAB => "DEPLOY PANELS A & B",
            _ => "DEPLOY ANTENNA"
        };
    }

    /// <summary>
    /// The satellite model's deployables follow telemetry ONLY. Deploy buttons do not move anything: they queue a
    /// command to the Flight Commander (sent on 8104) and a banner says so. When telemetry shows the deployment has
    /// started (Panel A / B fraction rising, antenna DEPLOYED) the command is marked executed and the model follows.
    /// Also flags a panel stuck at a partial angle and a jammed antenna (blue replay markers via LogAnomaly).
    /// </summary>
    public class SatelliteDeployControl : MonoBehaviour
    {
        public DeployablesAnimator deployables;
        [Tooltip("Other models that must show the same state (e.g. the VR spacecraft). Driven together with the main one.")]
        public DeployablesAnimator[] mirrors = System.Array.Empty<DeployablesAnimator>();
        public TelemetryHub hub;
        [Tooltip("Follow the simulator's deploy states. Off = model holds its start state.")]
        public bool followTelemetry = true;
        public bool antennaStartsDeployed;
        public bool panelsStartDeployed;

        [Header("Stuck detection")]
        [Tooltip("A panel between 2 % and 98 % that has not moved for this long is reported STUCK.")]
        public float stuckAfterSeconds = 10f;
        [Tooltip("Executed commands stay on the banner this long.")]
        public float executedBannerSeconds = 6f;

        public UnityEvent<bool> onAntennaStateChanged;
        public UnityEvent<bool> onPanelsStateChanged;

        public bool AntennaDeployed => deployables && deployables.Antenna == AntennaState.Deployed;
        public bool PanelsDeployed => deployables && deployables.Panels == PanelState.Deployed;
        public float AntennaProgress => deployables ? deployables.AntennaFraction : 0f;
        public float PanelsProgress => deployables ? deployables.PanelFraction : 0f;
        public bool PanelAStuck { get; private set; }
        public bool PanelBStuck { get; private set; }
        public IReadOnlyList<PendingCommand> Commands => commands;

        public string AntennaStatus =>
            (deployables && deployables.Antenna == AntennaState.Jammed ? "<color=#FF6A6A>JAMMED</color>" : AntennaDeployed ? "DEPLOYED" : "STOWED")
            + Queued(DeployCommand.Antenna);
        public string PanelAStatus => PanelText(deployables ? deployables.WingPyFraction : 0f, PanelAStuck) + Queued(DeployCommand.PanelA);
        public string PanelBStatus => PanelText(deployables ? deployables.WingMyFraction : 0f, PanelBStuck) + Queued(DeployCommand.PanelB);
        public string PanelsStatus => deployables && deployables.singlePanel ? PanelAStatus
            : $"A {PanelText(deployables ? deployables.WingPyFraction : 0f, PanelAStuck)}   B {PanelText(deployables ? deployables.WingMyFraction : 0f, PanelBStuck)}"
              + Queued(DeployCommand.PanelsAB);

        readonly List<PendingCommand> commands = new List<PendingCommand>();
        float lastA = -1f, lastB = -1f, movedA, movedB;
        bool jamLogged;

        void Awake()
        {
            if (!deployables) deployables = GetComponentInChildren<DeployablesAnimator>();
            // Main model missing (e.g. renamed in the scene): drive the mirrors instead of silently doing nothing.
            if (!deployables && mirrors != null) deployables = System.Array.Find(mirrors, m => m);
            if (!deployables) Debug.LogWarning("[EOSAT] SatelliteDeployControl has no DeployablesAnimator - antenna / panels will not animate.", this);
        }

        // Apply the same telemetry to the main model and every mirror.
        void All(System.Action<DeployablesAnimator> act)
        {
            if (deployables) act(deployables);
            if (mirrors != null) foreach (var m in mirrors) if (m && m != deployables) act(m);
        }

        void OnEnable()
        {
            if (!hub) hub = FindAnyObjectByType<TelemetryHub>();   // unwired hub = deployables never follow the data
            if (hub) hub.SampleReceived += OnSample;
        }
        void OnDisable() { if (hub) hub.SampleReceived -= OnSample; }

        void Start()
        {
            if (!deployables || (followTelemetry && hub)) return;
            All(d => d.SetTargets(
                panelsStartDeployed ? PanelState.Deployed : PanelState.Stowed,
                antennaStartsDeployed ? AntennaState.Deployed : AntennaState.Stowed));
        }

        void OnSample(TelemetrySample s)
        {
            if (!followTelemetry || !deployables) return;
            bool wasAntenna = AntennaDeployed, wasPanels = PanelsDeployed;

            if (s.antenna != AntennaState.Unknown) All(d => d.SetTargets(PanelState.Unknown, s.antenna));
            if (s.hasWings) All(d => d.SetWingFractions(s.wingPyFraction, s.wingMyFraction));
            else if (s.panels != PanelState.Unknown) All(d => d.SetTargets(s.panels, AntennaState.Unknown));

            float a = s.hasWings ? s.wingPyFraction : s.panels == PanelState.Deployed ? 1f : 0f;
            float b = s.hasWings ? s.wingMyFraction : a;
            Verify(a, b, s.antenna);
            CheckStuck(a, ref lastA, ref movedA, PanelAStuck, v => PanelAStuck = v, "Panel A");
            CheckStuck(b, ref lastB, ref movedB, PanelBStuck, v => PanelBStuck = v, "Panel B");
            if (s.antenna == AntennaState.Jammed && !jamLogged) { jamLogged = true; hub.LogAnomaly("Antenna JAMMED (telemetry)"); }
            if (s.antenna != AntennaState.Jammed) jamLogged = false;

            if (AntennaDeployed != wasAntenna) onAntennaStateChanged?.Invoke(AntennaDeployed);
            if (PanelsDeployed != wasPanels) onPanelsStateChanged?.Invoke(PanelsDeployed);
        }

        void Verify(float a, float b, AntennaState antenna)
        {
            const float started = 0.02f;
            foreach (var c in commands)
            {
                if (c.verified) continue;
                bool done = c.command switch
                {
                    DeployCommand.PanelA => a > started,
                    DeployCommand.PanelB => b > started,
                    DeployCommand.PanelsAB => a > started && b > started,
                    _ => antenna == AntennaState.Deployed
                };
                if (done) { c.verified = true; c.verifiedAt = Time.time; }
            }
        }

        void CheckStuck(float f, ref float last, ref float movedAt, bool stuck, System.Action<bool> set, string name)
        {
            if (last < 0f || Mathf.Abs(f - last) > 0.005f) { last = f; movedAt = Time.time; }
            bool now = f > 0.02f && f < 0.98f && Time.time - movedAt > stuckAfterSeconds;
            if (now && !stuck) hub.LogAnomaly($"{name} STUCK at {f * 90f:0}° ({f * 100f:0}%)");
            set(now);
        }

        void Update()
        {
            commands.RemoveAll(c => c.verified && Time.time - c.verifiedAt > executedBannerSeconds);
        }

        // ---------------- Commands (UI buttons) ----------------
        public void DeployPanelA() => Request(DeployCommand.PanelA);
        public void DeployPanelB() => Request(DeployCommand.PanelB);
        public void DeployPanelsAB() => Request(DeployCommand.PanelsAB);
        public void DeployAntenna() => Request(DeployCommand.Antenna);

        /// <summary>Queue a command to the Flight Commander. The 3D model does not change here.</summary>
        public void Request(DeployCommand command)
        {
            if (commands.Exists(c => c.command == command && !c.verified)) return;   // already waiting
            var p = new PendingCommand { command = command, queuedAt = Time.time };
            string wire = command switch
            {
                DeployCommand.PanelA => "DEPLOY_PANEL_A",
                DeployCommand.PanelB => "DEPLOY_PANEL_B",
                DeployCommand.PanelsAB => "DEPLOY_PANELS_AB",
                _ => "DEPLOY_ANTENNA"
            };
            p.sentOn8104 = hub && hub.SendCommand(wire);
            commands.Add(p);
        }

        // Older toggle API (tablet toggles): "on" requests the deployment; springs cannot be re-stowed, so "off" does nothing.
        public void SetAntennaDeployed(bool deployed) { if (deployed && !AntennaDeployed) DeployAntenna(); }
        public void SetPanelsDeployed(bool deployed) { if (deployed && !PanelsDeployed) DeployPanelsAB(); }
        public void ToggleAntenna() => SetAntennaDeployed(!AntennaDeployed);
        public void TogglePanels() => SetPanelsDeployed(!PanelsDeployed);

        string Queued(DeployCommand c) =>
            commands.Exists(p => p.command == c && !p.verified) ? "  |  <color=#FFB000>QUEUED TO FLIGHT COMMANDER</color>" : "";

        static string PanelText(float f, bool stuck) =>
            f >= 0.999f ? "DEPLOYED 90°" :
            f <= 0.001f ? "STOWED" :
            (stuck ? "<color=#FF6A6A>STUCK</color> " : "DEPLOYING ") + $"{f * 90f:0}° ({f * 100f:0}%)";

        /// <summary>Human-readable state, e.g. "DEPLOYING 45%".</summary>
        public static string Describe(bool target, float progress) =>
            progress >= 0.999f ? "DEPLOYED" :
            progress <= 0.001f ? "STOWED" :
            $"{(target ? "DEPLOYING" : "STOWING")} {progress * 100f:0}%";
    }
}
