using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Eosat.EditorTools
{
    /// <summary>
    /// EOSAT > Add Yellow Dot + Client Replay To Open Scene: adds the Concept 4 zoom-out marker to the
    /// spacecraft and the client-data replay feed to the Telemetry object, without rebuilding the scene.
    /// Safe to run more than once. Also used by both scene builders.
    /// </summary>
    public static class EosatMarkerSetup
    {
        const string Root = "Assets/EOSAT";
        public const string ReplayFile = "sample_vr_frames.ndjson";
        const string ClientSamplePath = @"C:\Users\yinja\Downloads\GNG1103_F2026\GNG1103_F2026\sample_vr_frames.ndjson";

        [MenuItem("EOSAT/Add Yellow Dot + Client Replay To Open Scene", priority = 22)]
        public static void Menu() => Debug.Log("[EOSAT] " + ApplyToActiveScene());

        public static string ApplyToActiveScene()
        {
            var sb = new StringBuilder();
            var ctrl = Object.FindAnyObjectByType<SpacecraftController>(FindObjectsInactive.Include);
            if (ctrl == null) return "No SpacecraftController in the open scene.";

            Transform parent = ctrl.visualRoot ? ctrl.visualRoot.transform : ctrl.transform;
            if (parent.Find("Yellow Dot Marker") == null)
            {
                CreateMarker(parent, 0.02f, 5f, 15f, false, 0f);
                sb.Append("yellow dot added; ");
            }
            else sb.Append("yellow dot already present; ");

            if (ctrl.hub && !ctrl.hub.GetComponent<SampleReplayFeed>())
            {
                ctrl.hub.gameObject.AddComponent<SampleReplayFeed>();
                sb.Append("client replay feed added; ");
            }
            sb.Append(EnsureReplayFile());
            EditorSceneManager.MarkSceneDirty(ctrl.gameObject.scene);
            return sb.ToString();
        }

        /// <summary>Builds "Yellow Dot Marker" (SatelliteMarker + Marker/Dot + Marker/Halo) under parent.</summary>
        public static SatelliteMarker CreateMarker(Transform parent, float angularSize, float showFrom, float fullAt, bool alwaysShow, float minWorldSize)
        {
            var root = new GameObject("Yellow Dot Marker");
            root.transform.SetParent(parent, false);
            var marker = new GameObject("Marker").transform;
            marker.SetParent(root.transform, false);

            Sphere("Dot", marker, 1f, DotMaterial());
            var halo = Sphere("Halo", marker, 2.4f, HaloMaterial());

            var m = root.AddComponent<SatelliteMarker>();
            m.marker = marker;
            m.halo = halo;
            m.angularSize = angularSize;
            m.showFrom = showFrom;
            m.fullAt = fullAt;
            m.alwaysShow = alwaysShow;
            m.minWorldSize = minWorldSize;
            return m;
        }

        public static Material DotMaterial() =>
            Mat("MarkerDot", Shader.Find("Universal Render Pipeline/Unlit"), new Color(1.6f, 1.35f, 0.15f, 1f), false);

        public static Material HaloMaterial() =>
            Mat("MarkerHalo", Shader.Find("Universal Render Pipeline/Unlit"), new Color(1f, 0.85f, 0.1f, 0.28f), true);

        /// <summary>Copies the client's sample file into StreamingAssets if it isn't there yet.</summary>
        public static string EnsureReplayFile()
        {
            string dst = Path.Combine(Application.streamingAssetsPath, ReplayFile);
            if (File.Exists(dst)) return "replay file present.";
            if (!File.Exists(ClientSamplePath)) return "replay file MISSING - copy sample_vr_frames.ndjson into Assets/StreamingAssets.";
            Directory.CreateDirectory(Application.streamingAssetsPath);
            File.Copy(ClientSamplePath, dst);
            AssetDatabase.Refresh();
            return "replay file copied to StreamingAssets.";
        }

        static Renderer Sphere(string name, Transform parent, float scale, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localScale = Vector3.one * scale;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            var r = g.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        static Material Mat(string name, Shader shader, Color color, bool transparent)
        {
            string path = $"{Root}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            else m.shader = shader;
            m.SetColor("_BaseColor", color);
            if (transparent)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
