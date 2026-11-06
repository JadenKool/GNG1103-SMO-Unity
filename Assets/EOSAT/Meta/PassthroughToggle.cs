// PassthroughToggle.cs  -  Meta XR Core SDK (OVRCameraRig / OVRPassthroughLayer / OVRInput)
// Toggles between MR (full-colour passthrough on Quest 3 / 3S / Pro, greyscale on Quest 2)
// and VR (immersive skybox) with a controller button. Put it on any GameObject in the scene
// (e.g. next to OVRCameraRig) and fill in the references.
//
// Requirements (see setup guide):
//  - OVRManager: Quest Features > General > Passthrough Support = Supported/Required,
//    Insight Passthrough > Enable Passthrough = on.
//  - One OVRPassthroughLayer (Placement = Underlay) in the scene.
//  - CenterEyeAnchor camera: Background Type = Solid Color, colour (0,0,0,0).

using System;
using UnityEngine;

namespace Eosat.Meta
{
    [DisallowMultipleComponent]
    public class PassthroughToggle : MonoBehaviour
    {
        public enum Mode { Passthrough, Virtual }

        [Header("Meta references")]
        [Tooltip("The scene's OVRPassthroughLayer (Placement = Underlay).")]
        [SerializeField] OVRPassthroughLayer passthroughLayer;
        [Tooltip("OVRCameraRig > TrackingSpace > CenterEyeAnchor camera. Auto-found from OVRCameraRig if empty.")]
        [SerializeField] Camera centerEyeCamera;

        [Header("VR look")]
        [Tooltip("Skybox drawn in VR mode (e.g. a space skybox). Lighting > Skybox Material must stay None in the scene.")]
        [SerializeField] Material vrSkybox;
        [Tooltip("Virtual-only scenery (star field, floor...) shown only in VR mode.")]
        [SerializeField] GameObject[] vrOnlyObjects = Array.Empty<GameObject>();
        [Tooltip("Background colour used when no headset is running (desktop preview has no camera feed).")]
        [SerializeField] Color desktopBackground = new Color(0.36f, 0.34f, 0.32f, 1f);

        [Header("Input")]
        [Tooltip("Clicking either thumbstick toggles by default. Hands have no thumbstick: call Toggle() from a tablet button.")]
        [SerializeField] OVRInput.Button toggleButton = OVRInput.Button.PrimaryThumbstick;
        [SerializeField] OVRInput.Button alternateButton = OVRInput.Button.SecondaryThumbstick;
        [SerializeField] OVRInput.Controller controllers = OVRInput.Controller.Touch;   // both Touch controllers

        [Header("Startup")]
        [Tooltip("Start in whatever the user picked system-wide (OVRManager.IsPassthroughRecommended).")]
        [SerializeField] bool useSystemRecommendation;
        [SerializeField] Mode startMode = Mode.Passthrough;

        [Header("Performance")]
        [Tooltip("Off (default): VR mode only HIDES the layer - instant switch back, cameras stay on.\n" +
                 "On: VR mode DISABLES the layer - saves battery/GPU on standalone, but passthrough takes a few hundred ms to come back.")]
        [SerializeField] bool disableLayerInVr;

        /// <summary>True while the real room is visible.</summary>
        public bool IsPassthrough { get; private set; }
        /// <summary>True when a headset is running (otherwise there is no camera feed to show).</summary>
        public bool PassthroughAvailable => UnityEngine.XR.XRSettings.isDeviceActive;
        /// <summary>Raised after every switch (true = passthrough). Hook UI labels/icons here.</summary>
        public event Action<bool> ModeChanged;

        static readonly Color Transparent = new Color(0f, 0f, 0f, 0f);

        void Awake()
        {
            if (!centerEyeCamera)
            {
                var rig = FindAnyObjectByType<OVRCameraRig>();
                if (rig) centerEyeCamera = rig.centerEyeAnchor.GetComponent<Camera>();
            }
            if (!passthroughLayer) passthroughLayer = FindAnyObjectByType<OVRPassthroughLayer>();
            if (!passthroughLayer) Debug.LogWarning("[PassthroughToggle] No OVRPassthroughLayer in the scene.", this);
        }

        System.Collections.IEnumerator Start()
        {
            yield return null;   // let OVRManager / XR finish starting first
            bool mr = startMode == Mode.Passthrough;
            if (useSystemRecommendation && PassthroughAvailable) mr = OVRManager.IsPassthroughRecommended();
            Apply(mr);
        }

        void Update()
        {
            // GetDown = true only on the frame the button goes down (no repeat while held).
            if (OVRInput.GetDown(toggleButton, controllers) || OVRInput.GetDown(alternateButton, controllers))
                Toggle();
        }

        /// <summary>Flip MR <-> VR. Public so a UI button or gesture can call it too.</summary>
        public void Toggle() => Apply(!IsPassthrough);

        public void SetPassthrough(bool on) { if (on != IsPassthrough) Apply(on); }

        void Apply(bool passthrough)
        {
            IsPassthrough = passthrough;

            if (passthroughLayer)
            {
                if (passthrough)
                {
                    // Re-enable first (if it was disabled), then un-hide.
                    if (!passthroughLayer.enabled) passthroughLayer.enabled = true;
                    passthroughLayer.hidden = false;
                }
                else
                {
                    passthroughLayer.hidden = true;           // compositor stops drawing passthrough
                    if (disableLayerInVr) passthroughLayer.enabled = false;  // also stops the camera pipeline
                }
            }

            if (centerEyeCamera)
            {
                if (passthrough)
                {
                    // Alpha 0 everywhere nothing virtual is drawn -> the Underlay passthrough shows through.
                    centerEyeCamera.clearFlags = CameraClearFlags.SolidColor;
                    centerEyeCamera.backgroundColor = PassthroughAvailable ? Transparent : desktopBackground;
                }
                else
                {
                    centerEyeCamera.clearFlags = CameraClearFlags.Skybox;    // opaque skybox covers everything
                }
            }

            // Only set the skybox in VR; in MR it must be null or it would paint over the passthrough.
            RenderSettings.skybox = passthrough ? null : vrSkybox;

            for (int i = 0; i < vrOnlyObjects.Length; i++)
                if (vrOnlyObjects[i]) vrOnlyObjects[i].SetActive(!passthrough);

            Debug.Log("[EOSAT Meta] " + (passthrough ? "Passthrough (MR)" : "Virtual skybox (VR)"));
            ModeChanged?.Invoke(passthrough);
        }
    }
}
