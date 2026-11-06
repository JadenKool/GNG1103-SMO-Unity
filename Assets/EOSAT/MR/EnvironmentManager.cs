using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

namespace Eosat.MR
{
    /// <summary>
    /// AR / VR switch. AR = Meta passthrough (full colour on Quest 3 / 3S / Pro; Quest 2 hardware is greyscale).
    /// VR = passthrough off, Skybox material drawn behind everything.
    /// Put it on the AR/VR Toggle: it listens to that Toggle (isOn = AR) by itself.
    /// Passthrough is only switched on while a headset is running, so desktop Play stays warning-free.
    /// </summary>
    [DefaultExecutionOrder(200)]   // after MrDesktopFallback has decided headset vs desktop
    public class EnvironmentManager : MonoBehaviour
    {
        public enum Mode { Passthrough, Virtual }

        [Header("References")]
        public Camera xrCamera;
        [Tooltip("ARCameraManager on the XR camera. Enabling it starts Meta passthrough.")]
        public ARCameraManager cameraManager;
        [Tooltip("Optional - only used on platforms that draw the camera feed with ARCameraBackground (not needed on Quest).")]
        public ARCameraBackground cameraBackground;
        [Tooltip("Optional. If empty, a Toggle on this GameObject is used. isOn = Passthrough (AR).")]
        public Toggle toggle;

        [Header("Virtual (VR) look")]
        public Material skyboxMaterial;
        [Tooltip("Optional extra virtual scenery shown only in VR mode (floor, stars...).")]
        public GameObject virtualEnvironment;
        public Color virtualAmbient = new Color(0.2f, 0.22f, 0.27f);
        [Tooltip("Desktop (no headset) background in AR mode, since there is no camera feed to show.")]
        public Color desktopPassthroughStandIn = new Color(0.36f, 0.34f, 0.32f, 1f);

        [Header("State")]
        public Mode startMode = Mode.Passthrough;
        public UnityEvent<bool> onPassthroughChanged;

        public Mode Current { get; private set; }
        public bool IsPassthrough => Current == Mode.Passthrough;
        public bool PassthroughAvailable => UnityEngine.XR.XRSettings.isDeviceActive && cameraManager;

        AmbientMode arAmbientMode;
        Color arAmbient;

        void Awake()
        {
            if (!toggle) toggle = GetComponent<Toggle>();
            if (!xrCamera) xrCamera = Camera.main;
            if (!cameraManager && xrCamera) cameraManager = xrCamera.GetComponent<ARCameraManager>();
            if (!cameraBackground && xrCamera) cameraBackground = xrCamera.GetComponent<ARCameraBackground>();
            arAmbientMode = RenderSettings.ambientMode;
            arAmbient = RenderSettings.ambientLight;
        }

        void OnEnable() { if (toggle) toggle.onValueChanged.AddListener(OnToggle); }
        void OnDisable() { if (toggle) toggle.onValueChanged.RemoveListener(OnToggle); }

        System.Collections.IEnumerator Start()
        {
            yield return null; // let XR start and MrDesktopFallback pick headset vs desktop first
            Apply(startMode);
            if (toggle) toggle.SetIsOnWithoutNotify(IsPassthrough);
        }

        void OnToggle(bool passthrough) => Apply(passthrough ? Mode.Passthrough : Mode.Virtual);

        public void SetPassthrough(bool on) => Apply(on ? Mode.Passthrough : Mode.Virtual);
        public void TogglePassthrough() => Apply(IsPassthrough ? Mode.Virtual : Mode.Passthrough);

        public void Apply(Mode mode)
        {
            Current = mode;
            bool ar = mode == Mode.Passthrough;
            bool headset = UnityEngine.XR.XRSettings.isDeviceActive;

            // Camera feed: only on a running headset (no XR subsystem on desktop).
            if (cameraManager) cameraManager.enabled = ar && headset;
            if (cameraBackground) cameraBackground.enabled = ar && headset;

            if (xrCamera)
            {
                if (ar)
                {
                    xrCamera.clearFlags = CameraClearFlags.SolidColor;
                    // Alpha 0 is what lets the compositor show passthrough behind the rendered layer.
                    xrCamera.backgroundColor = headset ? new Color(0f, 0f, 0f, 0f) : desktopPassthroughStandIn;
                }
                else xrCamera.clearFlags = CameraClearFlags.Skybox;
            }

            RenderSettings.skybox = ar ? null : skyboxMaterial;
            RenderSettings.ambientMode = ar ? arAmbientMode : AmbientMode.Flat;
            RenderSettings.ambientLight = ar ? arAmbient : virtualAmbient;
            if (virtualEnvironment) virtualEnvironment.SetActive(!ar);

            if (toggle && toggle.isOn != ar) toggle.SetIsOnWithoutNotify(ar);
            onPassthroughChanged?.Invoke(ar);
            Debug.Log($"[EOSAT MR] Environment: {(ar ? (headset ? "PASSTHROUGH" : "PASSTHROUGH (desktop stand-in colour)") : "VIRTUAL SKYBOX")}");
        }
    }
}
