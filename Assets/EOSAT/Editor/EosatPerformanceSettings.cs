using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Eosat.EditorTools
{
    /// <summary>
    /// EOSAT > Apply Laptop Performance Settings: render settings that keep both scenes smooth on older
    /// laptops (integrated graphics). Turns off rendering features the scenes don't use, so the look
    /// stays the same. Safe to run again; logs every value it saved.
    /// </summary>
    public static class EosatPerformanceSettings
    {
        [MenuItem("EOSAT/Apply Laptop Performance Settings", priority = 42)]
        public static void Menu() => Debug.Log("[EOSAT Perf] " + Apply());

        public static string Apply()
        {
            var log = new StringBuilder();

            // --- Quality level (the project has one level, "PC") ---
            QualitySettings.vSyncCount = 1;                                   // steady pacing, no wasted frames -> less heat/throttling on laptops (ignored in VR)
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable; // per-texture instead of forced on every texture
            QualitySettings.pixelLightCount = 1;                              // scenes use a single Sun light

            // --- URP asset used by that level ---
            var urp = QualitySettings.renderPipeline as UniversalRenderPipelineAsset
                      ?? GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp)
            {
                urp.supportsHDR = false;                 // no tonemapping/bloom in use; LDR halves colour-buffer bandwidth
                urp.supportsCameraDepthTexture = false;  // nothing samples the depth texture
                urp.supportsCameraOpaqueTexture = false; // nothing samples the opaque texture
                urp.mainLightShadowmapResolution = 1024; // Sun light casts no shadows; keep the budget small anyway
                urp.shadowCascadeCount = 1;
                var so = new SerializedObject(urp);     // soft shadows has no public setter
                var soft = so.FindProperty("m_SoftShadowsSupported");
                if (soft != null) { soft.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); }
                urp.shadowDistance = 20f;
                urp.upscalingFilter = UpscalingFilterSelection.Auto; // OpenXR validation: custom upscalers are slow in XR
                urp.renderScale = 1f;
                EditorUtility.SetDirty(urp);

                foreach (var data in urp.rendererDataList)
                {
                    if (!data) continue;
                    foreach (var f in data.rendererFeatures)
                    {
                        if (f && f.GetType().Name == "ScreenSpaceAmbientOcclusion" && f.isActive)
                        {
                            f.SetActive(false);          // SSAO = full-screen depth-normals + blur passes every frame
                            EditorUtility.SetDirty(f);
                            EditorUtility.SetDirty(data);
                        }
                    }
                }
            }
            else log.Append("No URP asset found! ");

            AssetDatabase.SaveAssets();

            // --- Read back what is stored ---
            log.Append($"vSync={QualitySettings.vSyncCount}, aniso={QualitySettings.anisotropicFiltering}, pixelLights={QualitySettings.pixelLightCount}");
            if (urp)
            {
                log.Append($" | {urp.name}: HDR={urp.supportsHDR}, depthTex={urp.supportsCameraDepthTexture}, opaqueTex={urp.supportsCameraOpaqueTexture}, " +
                           $"shadows {urp.mainLightShadowmapResolution}px x{urp.shadowCascadeCount} soft={urp.supportsSoftShadows} dist={urp.shadowDistance}, " +
                           $"upscaler={urp.upscalingFilter}, renderScale={urp.renderScale}");
                foreach (var data in urp.rendererDataList)
                    if (data) foreach (var f in data.rendererFeatures) if (f) log.Append($" | {data.name}.{f.GetType().Name} active={f.isActive}");
            }
            return log.ToString();
        }

        /// <summary>Turns off the (neutral) post-processing pass on the open scene's cameras and saves the scene.</summary>
        public static string DisableNeutralPostOnOpenScene()
        {
            int n = 0;
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var d = cam.GetUniversalAdditionalCameraData();
                if (d && d.renderPostProcessing) { d.renderPostProcessing = false; EditorUtility.SetDirty(d); n++; }
            }
            var scene = EditorSceneManager.GetActiveScene();
            if (n > 0) EditorSceneManager.SaveScene(scene);
            return $"{scene.name}: post-processing turned off on {n} camera(s)";
        }
    }
}
