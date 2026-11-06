using UnityEngine;
using UnityEngine.UI;

namespace Eosat.MR
{
    /// <summary>
    /// Wires the hand tablet's three toggles to the telemetry overlay and the satellite's
    /// deployables, and keeps the toggles/labels in sync with the scene (incl. simulator-driven changes).
    /// </summary>
    public class TabletUIController : MonoBehaviour
    {
        [Header("Toggles on the tablet canvas")]
        public Toggle dataToggle;
        public Toggle antennaToggle;
        public Toggle panelsToggle;
        [Tooltip("isOn = passthrough (AR), off = virtual skybox (VR). EnvironmentManager listens to it.")]
        public Toggle arVrToggle;

        [Header("Status labels")]
        public Text dataStatus;
        public Text antennaStatus;
        public Text panelsStatus;
        public Text arVrStatus;

        [Header("Targets")]
        public TelemetryOverlay telemetry;
        public SatelliteDeployControl satellite;
        public EnvironmentManager environment;

        [Header("Feedback")]
        public AudioSource clickSound;

        void Start()
        {
            if (dataToggle) dataToggle.onValueChanged.AddListener(OnDataToggled);
            if (antennaToggle) antennaToggle.onValueChanged.AddListener(OnAntennaToggled);
            if (panelsToggle) panelsToggle.onValueChanged.AddListener(OnPanelsToggled);
            SyncFromScene();
        }

        void OnDestroy()
        {
            if (dataToggle) dataToggle.onValueChanged.RemoveListener(OnDataToggled);
            if (antennaToggle) antennaToggle.onValueChanged.RemoveListener(OnAntennaToggled);
            if (panelsToggle) panelsToggle.onValueChanged.RemoveListener(OnPanelsToggled);
        }

        // Keeps the UI honest when the simulator changes states, and animates the % labels.
        float nextSync;
        void Update()
        {
            if (Time.unscaledTime < nextSync) return;   // perf: labels at 10 Hz instead of every frame
            nextSync = Time.unscaledTime + 0.1f;
            SyncFromScene();
        }

        public void OnDataToggled(bool on)
        {
            if (telemetry) telemetry.SetVisible(on);
            Click();
        }

        public void OnAntennaToggled(bool on)
        {
            if (satellite) satellite.SetAntennaDeployed(on);
            Click();
        }

        public void OnPanelsToggled(bool on)
        {
            if (satellite) satellite.SetPanelsDeployed(on);
            Click();
        }

        void SyncFromScene()
        {
            if (telemetry)
            {
                if (dataToggle) dataToggle.SetIsOnWithoutNotify(telemetry.Visible);
                SetLabel(dataStatus, telemetry.Visible ? "ON" : "OFF");
            }
            if (environment)
            {
                if (arVrToggle) arVrToggle.SetIsOnWithoutNotify(environment.IsPassthrough);
                SetLabel(arVrStatus, environment.IsPassthrough
                    ? (environment.PassthroughAvailable ? "AR  |  PASSTHROUGH" : "AR  |  no headset (preview colour)")
                    : "VR  |  VIRTUAL SKYBOX");
            }
            if (satellite)
            {
                if (antennaToggle) antennaToggle.SetIsOnWithoutNotify(satellite.AntennaDeployed);
                if (panelsToggle) panelsToggle.SetIsOnWithoutNotify(satellite.PanelsDeployed);
                SetLabel(antennaStatus, satellite.AntennaStatus);
                SetLabel(panelsStatus, satellite.PanelsStatus);
            }
        }

        static void SetLabel(Text label, string value)
        {
            if (label && label.text != value) label.text = value;
        }

        void Click()
        {
            if (clickSound) clickSound.Play();
            nextSync = 0f; // show the new state on the next frame
        }
    }
}
