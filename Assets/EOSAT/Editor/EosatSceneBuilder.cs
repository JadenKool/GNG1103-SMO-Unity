using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Eosat.EditorTools
{
    /// <summary>
    /// EOSAT menu:
    ///  - Build Visualization Scene: (re)creates Assets/Scenes/EOSAT_Visualization.unity from scratch.
    ///  - Use Selected As Spacecraft Model: swaps the placeholder box for your SolidWorks OBJ (auto-centred, scaled to 37 cm).
    ///  - Restore Placeholder Model.
    /// </summary>
    public static class EosatSceneBuilder
    {
        const string Root = "Assets/EOSAT";
        const string ScenePath = "Assets/Scenes/EOSAT_Visualization.unity";
        const float UnitsPerMeter = 0.6f;   // visual exaggeration: 37 cm spacecraft drawn ~220 km long
        const float LongestSideMeters = 0.37f;
        // Unity local axes: x = body X (10 cm), y = body Z (37 cm), z = body Y (23 cm)
        static readonly Vector3 BoxMeters = new Vector3(0.10f, 0.37f, 0.23f);

        static Font font;

        [MenuItem("EOSAT/Build Visualization Scene", priority = 0)]
        public static void BuildMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        public static void Build()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureFolder(Root, "Materials");
            EnsureFolder(Root, "Meshes");
            EnsureFolder(Root, "Models");
            ConfigureTexture($"{Root}/Textures/earth_atmos_2048.jpg");
            ConfigureTexture($"{Root}/Textures/earth_lights_2048.png");

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var earthShader = Shader.Find("EOSAT/EarthDayNight");

            var mEarth = Mat("Earth", earthShader, Color.white, m =>
            {
                m.SetTexture("_DayTex", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/earth_atmos_2048.jpg"));
                m.SetTexture("_NightTex", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/earth_lights_2048.png"));
            });
            var mBody = Mat("SpacecraftBody", lit, new Color(0.72f, 0.72f, 0.75f), m => { m.SetFloat("_Metallic", 0.7f); m.SetFloat("_Smoothness", 0.55f); });
            var mPanel = Mat("SolarPanel", lit, new Color(0.08f, 0.12f, 0.35f), m => { m.SetFloat("_Metallic", 0.3f); m.SetFloat("_Smoothness", 0.85f); });
            var mLens = Mat("Payload", lit, new Color(0.05f, 0.05f, 0.05f), m => m.SetFloat("_Smoothness", 0.9f));
            var mAntenna = Mat("Antenna", lit, new Color(0.85f, 0.85f, 0.85f));
            var mSun = Mat("Sun", unlit, new Color(4f, 3.6f, 2.6f));
            var mTrail = Mat("Trail", unlit, new Color(0.3f, 0.85f, 1f));
            var mSunVec = Mat("SunVector", unlit, new Color(1f, 0.85f, 0.2f));
            var mX = Mat("AxisX", unlit, new Color(1f, 0.25f, 0.25f));
            var mY = Mat("AxisY", unlit, new Color(0.3f, 1f, 0.35f));
            var mZ = Mat("AxisZ", unlit, new Color(0.3f, 0.55f, 1f));
            var mXd = Mat("AxisX_ECI", unlit, new Color(0.7f, 0.25f, 0.25f));
            var mYd = Mat("AxisY_ECI", unlit, new Color(0.25f, 0.65f, 0.3f));
            var mZd = Mat("AxisZ_ECI", unlit, new Color(0.25f, 0.4f, 0.8f));
            var earthMesh = EarthMesh();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Environment
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.1f, 0.1f, 0.12f);

            var sunGo = new GameObject("Sun Light");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.None;
            RenderSettings.sun = sun;
            var sunVisual = Prim(PrimitiveType.Sphere, "Sun", null, Vector3.right * 400f, Vector3.one * 10f, mSun);
            sunVisual.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            var eci = new GameObject("ECI Frame").transform;
            var earthBody = new GameObject("Earth");
            earthBody.transform.SetParent(eci, false);
            earthBody.transform.localScale = Vector3.one * 6.378137f;
            earthBody.AddComponent<MeshFilter>().sharedMesh = earthMesh;
            var er = earthBody.AddComponent<MeshRenderer>();
            er.sharedMaterial = mEarth;
            er.shadowCastingMode = ShadowCastingMode.Off;

            var eciAxes = Child("ECI Axes", eci);
            Axis(eciAxes, "X ECI", Vector3.right, 10f, 0.03f, mXd, 0.05f, new Color(1f, 0.5f, 0.5f));
            Axis(eciAxes, "Y ECI", Vector3.forward, 10f, 0.03f, mYd, 0.05f, new Color(0.5f, 1f, 0.55f));
            Axis(eciAxes, "Z ECI (North)", Vector3.up, 10f, 0.03f, mZd, 0.05f, new Color(0.55f, 0.7f, 1f));

            // Spacecraft
            var sc = new GameObject("Spacecraft");
            var visuals = Child("Visuals", sc.transform);
            var slot = Child("ModelSlot", visuals);
            slot.localScale = Vector3.one * UnitsPerMeter;
            var placeholder = Child("Placeholder", slot);
            Prim(PrimitiveType.Cube, "Body (10x23x37 cm)", placeholder, Vector3.zero, BoxMeters, mBody);
            Prim(PrimitiveType.Cylinder, "Payload lens (nadir, +Z)", placeholder, new Vector3(0, 0.185f, 0), new Vector3(0.05f, 0.006f, 0.05f), mLens);

            var hingeR = Child("Panel Hinge R", placeholder); hingeR.localPosition = new Vector3(0.053f, 0, 0.11f);
            Prim(PrimitiveType.Cube, "Solar Panel R", hingeR, new Vector3(0.112f, 0, 0), new Vector3(0.22f, 0.34f, 0.004f), mPanel);
            var hingeL = Child("Panel Hinge L", placeholder); hingeL.localPosition = new Vector3(-0.053f, 0, 0.11f);
            Prim(PrimitiveType.Cube, "Solar Panel L", hingeL, new Vector3(-0.112f, 0, 0), new Vector3(0.22f, 0.34f, 0.004f), mPanel);
            var hingeA = Child("Antenna Hinge", placeholder); hingeA.localPosition = new Vector3(0, -0.185f, 0.10f);
            var rod = Prim(PrimitiveType.Cube, "Antenna", hingeA, new Vector3(0, -0.1f, 0), new Vector3(0.006f, 0.2f, 0.006f), mAntenna);

            var bodyAxes = Child("Body Axes", visuals);
            Axis(bodyAxes, "X", Vector3.right, 0.35f, 0.006f, mX, 0.009f, new Color(1f, 0.4f, 0.4f));
            Axis(bodyAxes, "Y", Vector3.forward, 0.35f, 0.006f, mY, 0.009f, new Color(0.45f, 1f, 0.5f));
            Axis(bodyAxes, "Z", Vector3.up, 0.35f, 0.006f, mZ, 0.009f, new Color(0.5f, 0.7f, 1f));

            var sunVecGo = Child("Sun Vector", visuals).gameObject;
            var svLine = sunVecGo.AddComponent<LineRenderer>();
            StyleLine(svLine, 0.005f, mSunVec);
            var sunVec = sunVecGo.AddComponent<Eosat.SunVector>();

            var trail = sc.AddComponent<TrailRenderer>();
            trail.time = 90f;
            trail.minVertexDistance = 0.005f;
            trail.startWidth = 0.012f; trail.endWidth = 0.0f;
            trail.sharedMaterial = mTrail;
            trail.shadowCastingMode = ShadowCastingMode.Off;

            var deploy = sc.AddComponent<Eosat.DeployablesAnimator>();
            deploy.panelHingeLeft = hingeL; deploy.panelHingeRight = hingeR; deploy.antennaHinge = hingeA;
            deploy.antennaRenderer = rod.GetComponent<Renderer>();
            hingeR.localRotation = Quaternion.Euler(0, 90, 0);
            hingeL.localRotation = Quaternion.Euler(0, -90, 0);
            hingeA.localRotation = Quaternion.Euler(90, 0, 0);

            var telemetry = new GameObject("Telemetry");
            var hub = telemetry.AddComponent<Eosat.TelemetryHub>();
            telemetry.AddComponent<Eosat.SimulatedSpacecraft>();

            var ctrl = sc.AddComponent<Eosat.SpacecraftController>();
            ctrl.hub = hub; ctrl.deployables = deploy; ctrl.visualRoot = visuals.gameObject; ctrl.trail = trail;

            var envGo = new GameObject("Environment");
            var earthCtrl = envGo.AddComponent<Eosat.EarthController>();
            earthCtrl.spacecraft = ctrl; earthCtrl.earthBody = earthBody.transform; earthCtrl.sunLight = sun; earthCtrl.sunVisual = sunVisual.transform;
            sunVec.earth = earthCtrl;

            // Camera rig: OrbitCamera moves the rig; the camera is a head-tracked child (identity on desktop).
            var rig = new GameObject("Camera Rig");
            rig.transform.position = new Vector3(0, 8, -25);
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(rig.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 1000f;
            cam.fieldOfView = 55f;
            camGo.AddComponent<AudioListener>();
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            AddHeadTracking(camGo);
            var orbit = rig.AddComponent<Eosat.OrbitCamera>();
            orbit.spacecraft = sc.transform; orbit.earth = eci;

            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<Eosat.HudController>();
            hud.spacecraft = ctrl; hud.hub = hub; hud.deployables = deploy;

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = sc;
            Debug.Log("[EOSAT] Built " + ScenePath + ". Press Play to run with the simulated feed.");
        }

        // ---------- Custom model ----------

        [MenuItem("EOSAT/Use Selected As Spacecraft Model", priority = 20)]
        public static void UseSelectedAsModel()
        {
            var sel = Selection.activeObject as GameObject;
            if (sel == null) { Debug.LogWarning("[EOSAT] Select your imported OBJ (in the Project window or the scene) first."); return; }
            var slot = FindSlot();
            if (slot == null) { Debug.LogWarning("[EOSAT] Open the EOSAT_Visualization scene first (no Spacecraft/Visuals/ModelSlot found)."); return; }

            GameObject inst = EditorUtility.IsPersistent(sel) ? (GameObject)PrefabUtility.InstantiatePrefab(sel) : sel;
            if (EditorUtility.IsPersistent(sel)) Undo.RegisterCreatedObjectUndo(inst, "Add spacecraft model");

            var old = slot.Find("CustomModel");
            if (old && old.gameObject != inst) Undo.DestroyObjectImmediate(old.gameObject);

            Undo.SetTransformParent(inst.transform, slot, "Use as spacecraft model");
            Undo.RecordObject(inst.transform, "Fit spacecraft model");
            inst.name = "CustomModel";
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one;
            foreach (var c in inst.GetComponentsInChildren<Collider>()) Undo.DestroyObjectImmediate(c);

            var renderers = inst.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) { Debug.LogWarning("[EOSAT] That object has no meshes."); return; }
            var b = new Bounds();
            bool first = true;
            foreach (var r in renderers)
            {
                var wb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var local = slot.InverseTransformPoint(corner);
                    if (first) { b = new Bounds(local, Vector3.zero); first = false; } else b.Encapsulate(local);
                }
            }
            float longest = Mathf.Max(b.size.x, b.size.y, b.size.z);
            float s = longest > 1e-6f ? LongestSideMeters / longest : 1f;
            inst.transform.localScale = Vector3.one * s;
            inst.transform.localPosition = -b.center * s;

            var placeholder = slot.Find("Placeholder");
            if (placeholder) { Undo.RecordObject(placeholder.gameObject, "Hide placeholder"); placeholder.gameObject.SetActive(false); }

            Selection.activeGameObject = inst;
            EditorSceneManager.MarkSceneDirty(slot.gameObject.scene);
            Debug.Log($"[EOSAT] Using '{sel.name}' as the spacecraft (scaled x{s:G4} so the longest side is 37 cm). " +
                      "Rotate 'CustomModel' so its faces line up with the X/Y/Z body axes, then save the scene.");
        }

        [MenuItem("EOSAT/Restore Placeholder Model", priority = 21)]
        public static void RestorePlaceholder()
        {
            var slot = FindSlot();
            if (slot == null) return;
            var custom = slot.Find("CustomModel");
            if (custom) Undo.DestroyObjectImmediate(custom.gameObject);
            var placeholder = slot.Find("Placeholder");
            if (placeholder) { Undo.RecordObject(placeholder.gameObject, "Show placeholder"); placeholder.gameObject.SetActive(true); }
            EditorSceneManager.MarkSceneDirty(slot.gameObject.scene);
        }

        static Transform FindSlot()
        {
            var ctrl = Object.FindAnyObjectByType<Eosat.SpacecraftController>(FindObjectsInactive.Include);
            return ctrl ? ctrl.transform.Find("Visuals/ModelSlot") : null;
        }

        // ---------- Helpers ----------

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{name}")) AssetDatabase.CreateFolder(parent, name);
        }

        static void ConfigureTexture(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) { Debug.LogWarning("[EOSAT] Missing texture " + path); return; }
            ti.wrapModeU = TextureWrapMode.Repeat;
            ti.wrapModeV = TextureWrapMode.Clamp;
            ti.anisoLevel = 4;
            ti.maxTextureSize = 2048;
            ti.mipmapEnabled = true;
            ti.SaveAndReimport();
        }

        static Material Mat(string name, Shader shader, Color color, System.Action<Material> extra = null)
        {
            string path = $"{Root}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            else m.shader = shader;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            extra?.Invoke(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Mesh EarthMesh()
        {
            const int lonSeg = 128, latSeg = 64;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int j = 0; j <= latSeg; j++)
            {
                float v = (float)j / latSeg;
                float lat = Mathf.Lerp(-90f, 90f, v) * Mathf.Deg2Rad;
                for (int i = 0; i <= lonSeg; i++)
                {
                    float u = (float)i / lonSeg;
                    float lon = Mathf.Lerp(-180f, 180f, u) * Mathf.Deg2Rad;
                    // ECEF (cos lat cos lon, cos lat sin lon, sin lat) mapped to Unity (x, z, y)
                    verts.Add(new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon)));
                    uvs.Add(new Vector2(u, v));
                }
            }
            int row = lonSeg + 1;
            for (int j = 0; j < latSeg; j++)
                for (int i = 0; i < lonSeg; i++)
                {
                    int a = j * row + i, b = a + 1, c = a + row, d = c + 1;
                    tris.AddRange(new[] { a, c, b, b, c, d });
                }
            // Make sure triangles face outward (Unity front face = Cross(v1-v0, v2-v0) direction).
            int t0 = (latSeg / 2 * lonSeg + 3) * 6;
            Vector3 p0 = verts[tris[t0]], p1 = verts[tris[t0 + 1]], p2 = verts[tris[t0 + 2]];
            if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), p0) < 0)
                for (int k = 0; k < tris.Count; k += 3) (tris[k + 1], tris[k + 2]) = (tris[k + 2], tris[k + 1]);

            var mesh = new Mesh { name = "EarthSphere" };
            mesh.SetVertices(verts);
            mesh.SetNormals(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            string path = $"{Root}/Meshes/EarthSphere.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Transform Child(string name, Transform parent)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            return g.transform;
        }

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var g = GameObject.CreatePrimitive(type);
            g.name = name;
            if (parent) g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.GetComponent<Renderer>().sharedMaterial = m;
            return g;
        }

        static void StyleLine(LineRenderer lr, float width, Material m)
        {
            lr.startWidth = lr.endWidth = width;
            lr.sharedMaterial = m;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCapVertices = 2;
        }

        static void Axis(Transform parent, string label, Vector3 dir, float length, float width, Material m, float charSize, Color labelColor)
        {
            var g = Child("Axis " + label, parent).gameObject;
            var lr = g.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = 2;
            lr.SetPosition(0, Vector3.zero);
            lr.SetPosition(1, dir * length);
            StyleLine(lr, width, m);

            var lg = Child("Label " + label, g.transform);
            lg.localPosition = dir * (length * 1.08f);
            var tm = lg.gameObject.AddComponent<TextMesh>();
            tm.text = label;
            tm.font = font;
            tm.fontSize = 100;
            tm.characterSize = charSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = labelColor;
            lg.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            lg.gameObject.AddComponent<Eosat.Billboard>();
        }

        public static void AddHeadTracking(GameObject camGo)
        {
            var tpd = camGo.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            if (tpd == null) tpd = camGo.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            var pos = new UnityEngine.InputSystem.InputAction("Head Position", UnityEngine.InputSystem.InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
            var rot = new UnityEngine.InputSystem.InputAction("Head Rotation", UnityEngine.InputSystem.InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
            tpd.positionInput = new UnityEngine.InputSystem.InputActionProperty(pos);
            tpd.rotationInput = new UnityEngine.InputSystem.InputActionProperty(rot);
            tpd.trackingType = UnityEngine.InputSystem.XR.TrackedPoseDriver.TrackingType.RotationAndPosition;
            tpd.updateType = UnityEngine.InputSystem.XR.TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        }

        static void AddToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == path)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
