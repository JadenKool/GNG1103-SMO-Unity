using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Eosat.EditorTools
{
    /// <summary>EOSAT > Add Attitude Guides To Open Scene: direction-of-travel arrow + quaternion axis/angle + spin arrow.</summary>
    public static class EosatAttitudeSetup
    {
        [MenuItem("EOSAT/Add Attitude Guides (travel + quaternion) To Open Scene", priority = 23)]
        public static void Menu() => Debug.Log("[EOSAT] " + ApplyToActiveScene());

        public static string ApplyToActiveScene()
        {
            var sc = Object.FindAnyObjectByType<SpacecraftController>(FindObjectsInactive.Include);
            if (!sc) return "No SpacecraftController in the open scene.";
            var g = sc.GetComponent<AttitudeGuides>();
            if (!g) g = sc.gameObject.AddComponent<AttitudeGuides>();
            g.spacecraft = sc;
            g.travelMaterial = Mat("Guide_Travel");
            g.quatMaterial = Mat("Guide_Quaternion");
            g.spinMaterial = Mat("Guide_Spin");
            g.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            g.labelSize = 0.0032f;
            EditorUtility.SetDirty(g);
            EditorSceneManager.MarkSceneDirty(sc.gameObject.scene);
            EditorSceneManager.SaveScene(sc.gameObject.scene);
            return "Attitude guides added to " + sc.gameObject.scene.path;
        }

        static Material Mat(string name)
        {
            string path = $"Assets/EOSAT/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Sprites/Default")); AssetDatabase.CreateAsset(m, path); } // vertex colours from the LineRenderer
            return m;
        }
    }
}
