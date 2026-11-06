using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR;

namespace Eosat.EditorTools
{
    /// <summary>
    /// EOSAT > Configure MR (Passthrough over Link): enables the Meta Quest feature group on the
    /// Windows/Standalone OpenXR settings (used in Play mode over Quest Link) plus hand tracking.
    /// Run "EOSAT > Configure VR" first if XR isn't set up at all.
    /// </summary>
    public static class EosatMrSetup
    {
        const string MetaFeatureSet = "com.unity.openxr.featureset.meta";

        static readonly HashSet<string> Wanted = new HashSet<string>
        {
            "ARSessionFeature",              // Meta Quest: Session
            "ARCameraFeature",               // Meta Quest: Camera (Passthrough)
            "ARPlaneFeature",                // Meta Quest: Planes (desk detection)
            "ARAnchorFeature",               // Meta Quest: Anchors
            "OpenXRCompositionLayersFeature",// passthrough is composited as a layer
            "HandTracking",                  // XR Hands joint data
            "MetaHandTrackingAim",
            "HandInteractionProfile",
            "OculusTouchControllerProfile",
        };

        // Legacy: enables Unity's AR Foundation passthrough features. The Meta SDK scene uses OVRPassthroughLayer instead,
        // and running both can conflict, so this is only for the old XRI MR scene.
        [MenuItem("EOSAT/Legacy (XRI + AR Foundation)/Configure MR (AR Foundation passthrough)", priority = 101)]
        public static void ConfigureMenu() => UnityEngine.Debug.Log("[EOSAT] " + Configure());

        public static string Configure()
        {
            var log = new StringBuilder();
            var group = BuildTargetGroup.Standalone;

            foreach (var fs in OpenXRFeatureSetManager.FeatureSetsForBuildTarget(group))
            {
                if (fs.featureSetId != MetaFeatureSet) continue;
                fs.isEnabled = true;
                log.Append("Meta Quest feature group on; ");
            }
            OpenXRFeatureSetManager.SetFeaturesFromEnabledFeatureSets(group);

            var ox = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (ox == null) return "OpenXR settings not found - run EOSAT > Configure VR first.";

            foreach (var f in ox.GetFeatures())
            {
                if (!Wanted.Contains(f.GetType().Name)) continue;
                // Other vendors (e.g. Android XR) reuse names like "ARCameraFeature": only Meta's AR features here.
                string ns = f.GetType().Namespace ?? "";
                if (f.GetType().Name.StartsWith("AR") && !ns.Contains(".Meta")) continue;
                f.enabled = true;
                log.Append(f.GetType().Name).Append(" on; ");
            }
            EditorUtility.SetDirty(ox);
            AssetDatabase.SaveAssets();
            return log.ToString();
        }
    }
}
