// EnvironmentSwitcher.cs  -  Meta XR Core SDK (OVRCameraRig / OVRPassthroughLayer / OVRInput)
// One scene, one OVRCameraRig, two modes. B or Y switches (V on the keyboard for desktop testing):
//
//   MR Tabletop (free space)                      VR Orbit (immersive)
//   Environment_MR on, Environment_VR off         Environment_VR on, Environment_MR off
//   Passthrough ON (CenterEye alpha 0, no sky)    Passthrough OFF (Skybox)
//   Rig back in plain room space, orbit OFF       OrbitalViewerLocomotion ON (rig locked to a sphere round Earth)
//   Thumbsticks -> TabletopZoom (globe scale)     Right stick orbit, left stick zoom
//
// Everything changes inside one fade-to-dark (0.15 s), so the rig move and the passthrough/skybox swap
// are never seen as a hard black<->white cut.

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace Eosat.Meta
{
    [DisallowMultipleComponent]
    public class EnvironmentSwitcher : MonoBehaviour
    {
        public enum Mode { VR, MR }

        [Header("Environment roots")]
        [SerializeField] GameObject environmentVR;
        [SerializeField] GameObject environmentMR;
        [SerializeField] Mode startMode = Mode.MR;

        [Header("Thumbstick owners (exactly one is enabled at a time)")]
        [Tooltip("VR: orbits the rig around the Earth (on the parent of OVRCameraRig).")]
        [SerializeField] OrbitalViewerLocomotion orbitalLocomotion;
        [Tooltip("MR: zooms the tabletop globe.")]
        [SerializeField] TabletopZoom tabletopZoom;
        [Tooltip("Shown only in VR (e.g. the VR control console).")]
        [SerializeField] GameObject[] vrOnlyObjects = Array.Empty<GameObject>();
        [Tooltip("Shown only in MR (e.g. the white platform), for objects that live outside Environment_MR.")]
        [SerializeField] GameObject[] mrOnlyObjects = Array.Empty<GameObject>();

        [Header("Camera / passthrough (auto-found if empty)")]
        [SerializeField] Camera centerEyeCamera;
        [SerializeField] OVRPassthroughLayer passthroughLayer;
        [Tooltip("Skybox used in VR. Lighting > Environment > Skybox Material must be None in the scene.")]
        [SerializeField] Material vrSkybox;
        [Tooltip("Shown in MR when there is no camera feed (desktop, or Link with passthrough off) instead of pure black.")]
        [SerializeField] Color noPassthroughBackground = new Color(0.30f, 0.30f, 0.32f, 1f);

        [Header("Input")]
        [Tooltip("B on the right controller.")]
        [SerializeField] OVRInput.Button switchButton = OVRInput.Button.Two;
        [Tooltip("Y on the left controller.")]
        [SerializeField] OVRInput.Button alternateButton = OVRInput.Button.Four;
        [SerializeField] OVRInput.Controller controllers = OVRInput.Controller.Touch;
        [SerializeField] Key desktopKey = Key.V;
        [SerializeField] float cooldownSeconds = 0.6f;

        [Header("Transition")]
        [SerializeField, Range(0f, 0.5f)] float fadeSeconds = 0.15f;
        [SerializeField] Color fadeColor = new Color(0.05f, 0.05f, 0.07f, 1f);

        [Header("Events")]
        public UnityEvent<bool> onSwitched;   // true = MR

        public Mode Current { get; private set; }
        public bool IsMR => Current == Mode.MR;
        public bool PassthroughRunning => UnityEngine.XR.XRSettings.isDeviceActive && OVRManager.IsInsightPassthroughInitialized();
        public static event Action<Mode> ModeChanged;
        public static EnvironmentSwitcher Instance { get; private set; }

        static readonly Color Transparent = new Color(0f, 0f, 0f, 0f);
        float lastSwitch = -10f;
        bool busy, started;
        Renderer fadeQuad;
        Material fadeMat;

        void Awake()
        {
            Instance = this;
            if (!centerEyeCamera)
            {
                var rig = FindAnyObjectByType<OVRCameraRig>();
                if (rig) centerEyeCamera = rig.centerEyeAnchor.GetComponent<Camera>();
            }
            if (!passthroughLayer) passthroughLayer = FindAnyObjectByType<OVRPassthroughLayer>(FindObjectsInactive.Include);
            if (!orbitalLocomotion) orbitalLocomotion = FindAnyObjectByType<OrbitalViewerLocomotion>(FindObjectsInactive.Include);
            if (!tabletopZoom) tabletopZoom = FindAnyObjectByType<TabletopZoom>(FindObjectsInactive.Include);

            // Only one script may own clear flags / skybox, otherwise they fight on every switch.
            foreach (var pt in FindObjectsByType<PassthroughToggle>(FindObjectsInactive.Include)) pt.enabled = false;

            // Nothing moves the player until Start has picked the mode.
            if (orbitalLocomotion) orbitalLocomotion.enabled = false;
            if (tabletopZoom) tabletopZoom.enabled = false;
            BuildFadeQuad();
        }

        IEnumerator Start()
        {
            yield return null;               // let OVRManager / XR start first
            Apply(startMode);
            started = true;
        }

        void OnDestroy() { if (Instance == this) Instance = null; if (fadeMat) Destroy(fadeMat); }

        void Update()
        {
            if (!started) return;

            // Passthrough may finish starting a moment later: grey -> transparent once it is up.
            if (Current == Mode.MR && centerEyeCamera && !busy)
            {
                var want = PassthroughRunning ? Transparent : noPassthroughBackground;
                if (centerEyeCamera.backgroundColor != want) centerEyeCamera.backgroundColor = want;
            }

            bool pressed = OVRInput.GetDown(switchButton, controllers) || OVRInput.GetDown(alternateButton, controllers);
            var kb = Keyboard.current;
            if (kb != null && kb[desktopKey].wasPressedThisFrame) pressed = true;
            if (pressed) Switch();
        }

        /// <summary>Flip VR <-> MR. Public so a tablet button can call it.</summary>
        public void Switch() => SetMode(Current == Mode.VR ? Mode.MR : Mode.VR);

        /// <summary>For a UI Toggle (on = MR).</summary>
        public void SetMR(bool mr) => SetMode(mr ? Mode.MR : Mode.VR);

        public void SetMode(Mode mode)
        {
            if (!started || busy || mode == Current) return;
            if (Time.unscaledTime - lastSwitch < cooldownSeconds) return;
            lastSwitch = Time.unscaledTime;
            StartCoroutine(FadeAction(() => Apply(mode)));
        }

        /// <summary>Run something that moves the view (e.g. changing the orbit target) hidden behind the short fade.</summary>
        public void RunBehindFade(Action action)
        {
            if (action == null) return;
            if (busy || !isActiveAndEnabled) { action(); return; }
            StartCoroutine(FadeAction(action));
        }

        IEnumerator FadeAction(Action action)
        {
            busy = true;
            if (fadeSeconds > 0f && fadeQuad) yield return Fade(0f, 1f);
            action();
            yield return null;                // one fully covered frame while the new environment appears
            if (fadeSeconds > 0f && fadeQuad) yield return Fade(1f, 0f);
            busy = false;
        }

        IEnumerator Fade(float from, float to)
        {
            fadeQuad.enabled = true;
            for (float t = 0f; t < fadeSeconds; t += Time.unscaledDeltaTime)
            {
                SetFadeAlpha(Mathf.Lerp(from, to, t / fadeSeconds));
                yield return null;
            }
            SetFadeAlpha(to);
            fadeQuad.enabled = to > 0.001f;
        }

        void Apply(Mode mode)
        {
            Current = mode;
            bool mr = mode == Mode.MR;

            // 1) Environments.
            if (environmentVR) environmentVR.SetActive(!mr);
            if (environmentMR) environmentMR.SetActive(mr);
            foreach (var go in vrOnlyObjects) if (go) go.SetActive(!mr);
            foreach (var go in mrOnlyObjects) if (go) go.SetActive(mr);

            // 2) Thumbstick mapping + rig.
            //    MR: orbit OFF, rig back at its room pose (free space), sticks zoom the globe.
            //    VR: zoom OFF, orbit ON (it places the rig on the sphere around the Earth).
            if (tabletopZoom) tabletopZoom.enabled = mr;
            if (orbitalLocomotion)
            {
                if (mr)
                {
                    orbitalLocomotion.enabled = false;
                    orbitalLocomotion.ResetToRoom();
                }
                else
                {
                    orbitalLocomotion.ResetView();      // Earth straight ahead at eye level, starting distance
                    orbitalLocomotion.enabled = true;
                }
            }

            // 3) Passthrough + camera background.
            if (passthroughLayer)
            {
                if (mr && !passthroughLayer.enabled) passthroughLayer.enabled = true;
                passthroughLayer.hidden = !mr;
            }
            if (centerEyeCamera)
            {
                if (mr)
                {
                    centerEyeCamera.clearFlags = CameraClearFlags.SolidColor;   // alpha 0 -> Underlay passthrough shows
                    centerEyeCamera.backgroundColor = PassthroughRunning ? Transparent : noPassthroughBackground;
                }
                else
                {
                    centerEyeCamera.clearFlags = vrSkybox ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
                    if (!vrSkybox) centerEyeCamera.backgroundColor = Color.black;
                }
            }
            RenderSettings.skybox = mr ? null : vrSkybox;   // a skybox in MR would paint over the passthrough

            Debug.Log("[EnvironmentSwitcher] " + (mr
                ? "MR tabletop: passthrough " + (PassthroughRunning ? "on" : "not running (grey stand-in)") + ", sticks = zoom globe"
                : "VR orbit: skybox, right stick = orbit, left stick = zoom"));
            onSwitched?.Invoke(mr);
            ModeChanged?.Invoke(mode);
        }

        // A small dark quad just in front of the eyes; drawn last, ignores depth.
        void BuildFadeQuad()
        {
            if (!centerEyeCamera) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "EnvironmentSwitcher Fade";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(centerEyeCamera.transform, false);
            float d = Mathf.Max(centerEyeCamera.nearClipPlane * 1.5f, 0.06f);
            go.transform.localPosition = new Vector3(0f, 0f, d);
            go.transform.localScale = Vector3.one * d * 6f;           // covers the whole field of view
            fadeMat = new Material(Shader.Find("UI/Default"));         // alpha-blended, stereo-safe, ZTest settable
            fadeMat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);   // draw over everything
            fadeMat.renderQueue = 5000;
            fadeQuad = go.GetComponent<Renderer>();
            fadeQuad.sharedMaterial = fadeMat;
            fadeQuad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            fadeQuad.receiveShadows = false;
            SetFadeAlpha(0f);
            fadeQuad.enabled = false;
        }

        void SetFadeAlpha(float a)
        {
            if (!fadeMat) return;
            var c = fadeColor; c.a = a;
            fadeMat.color = c;
        }
    }
}
