using System.Text;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace Eosat.EditorTools
{
    /// <summary>
    /// EOSAT > Configure VR: turns on OpenXR for Windows (PC VR, e.g. Meta Quest over Link / Air Link),
    /// starts XR automatically on Play, enables the Quest Touch controller profiles and single-pass rendering.
    /// </summary>
    public static class EosatVrSetup
    {
        const string LoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";

        [MenuItem("EOSAT/Configure VR (OpenXR, Meta Quest Link)", priority = 40)]
        public static void ConfigureMenu() => Debug.Log("[EOSAT] " + Configure());

        public static string Configure()
        {
            var log = new StringBuilder();
            var group = BuildTargetGroup.Standalone;

            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/XR")) AssetDatabase.CreateFolder("Assets", "XR");
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, perTarget, true);
                log.Append("created XR settings; ");
            }
            if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
            if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);

            var settings = perTarget.SettingsForBuildTarget(group);
            settings.InitManagerOnStart = true;
            bool assigned = XRPackageMetadataStore.AssignLoader(settings.Manager, LoaderType, group);
            log.Append($"OpenXR loader assigned={assigned}; init on start; ");

            var ox = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (ox != null)
            {
                foreach (var f in ox.GetFeatures())
                {
                    string n = f.GetType().Name;
                    if (n.Contains("OculusTouch") || n.Contains("MetaQuestTouch"))
                    {
                        f.enabled = true;
                        log.Append(n).Append(" on; ");
                    }
                }
                ox.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
                EditorUtility.SetDirty(ox);
            }
            else log.Append("OpenXR settings not found (reopen Project Settings > XR Plug-in Management); ");

            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(perTarget);
            AssetDatabase.SaveAssets();
            return log.ToString();
        }
    }
}
