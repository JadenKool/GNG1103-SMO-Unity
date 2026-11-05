using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

namespace Eosat
{
    /// <summary>
    /// Keeps the app smooth on slower laptops: watches the real frame time and, if the GPU can't hold
    /// the target (60 fps on desktop, the headset's refresh rate in VR), lowers URP's render scale
    /// in steps (FSR upscales back to full size). Raises it again when there is headroom.
    /// Created automatically in every scene; the original render scale is restored on exit.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class FrameRateGuard : MonoBehaviour
    {
        public float minScale = 0.6f;
        public float maxScale = 1.0f;
        public float step = 0.1f;
        [Tooltip("Seconds of frames averaged per decision.")]
        public float window = 1f;
        public float desktopTargetFps = 60f;

        public static FrameRateGuard Instance { get; private set; }
        public float CurrentScale { get; private set; } = 1f;
        public float AverageFps { get; private set; }
        public float TargetFps { get; private set; }

        UniversalRenderPipelineAsset urp;
        float originalScale = 1f;
        float acc; int frames, slowWindows, fastWindows, warmup = 2;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance) return;
            var go = new GameObject("FrameRateGuard (auto)");
            DontDestroyOnLoad(go);
            go.AddComponent<FrameRateGuard>();
        }

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp) { originalScale = urp.renderScale; CurrentScale = Mathf.Clamp(originalScale, minScale, maxScale); }
        }

        void Start()
        {
            bool vr = XRSettings.isDeviceActive;
            TargetFps = vr ? Mathf.Max(60f, XRDevice.refreshRate > 1f ? XRDevice.refreshRate : 72f) : desktopTargetFps;
            Debug.Log($"[EOSAT Perf] Frame-rate guard on: target {TargetFps:0} fps ({(vr ? "VR" : "desktop")}), " +
                      $"quality '{QualitySettings.names[QualitySettings.GetQualityLevel()]}', vSync {QualitySettings.vSyncCount}, " +
                      $"render scale {CurrentScale:0.00}, upscaler {(urp ? urp.upscalingFilter.ToString() : "n/a")}");
        }

        void Update()
        {
            if (!urp) return;
            acc += Time.unscaledDeltaTime;
            frames++;
            if (acc < window) return;

            AverageFps = frames / acc;
            acc = 0f; frames = 0;
            if (warmup > 0) { warmup--; return; } // ignore scene-load hitches

            float budget = TargetFps * 0.92f;
            if (AverageFps < budget) { slowWindows++; fastWindows = 0; }
            else if (AverageFps > TargetFps * 0.98f) { fastWindows++; slowWindows = 0; }
            else { slowWindows = 0; fastWindows = 0; }

            if (slowWindows >= 2 && CurrentScale > minScale + 0.001f) { SetScale(CurrentScale - step); slowWindows = 0; }
            else if (fastWindows >= 5 && CurrentScale < maxScale - 0.001f) { SetScale(CurrentScale + step); fastWindows = 0; }
        }

        void SetScale(float s)
        {
            float old = CurrentScale;
            CurrentScale = Mathf.Clamp(Mathf.Round(s * 100f) / 100f, minScale, maxScale);
            urp.renderScale = CurrentScale;
            Debug.Log($"[EOSAT Perf] {AverageFps:0} fps vs target {TargetFps:0} -> render scale {old:0.00} -> {CurrentScale:0.00}");
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (urp) urp.renderScale = originalScale; // never leave a lowered scale saved in the asset
        }
    }
}
