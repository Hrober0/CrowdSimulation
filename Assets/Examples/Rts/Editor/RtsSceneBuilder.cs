using System.IO;
using Examples.Rts.UI;
using HCore.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Examples.Rts.EditorTools
{
    /// <summary>
    /// Builds the RTS sandbox scene and the two prefabs it needs, from nothing.
    ///
    /// The scene is generated rather than hand-authored so that it can be rebuilt after any change to the
    /// example - and so that what is in it is *readable*, as this file, instead of being a diff of scene
    /// YAML that nobody can review.
    ///
    /// Views are unlit quads tinted per instance. No sprite assets, no lighting setup, nothing to import:
    /// the point of the scene is to watch the simulation, and a coloured box is the fastest way to see one
    /// agent from another.
    /// </summary>
    public static class RtsSceneBuilder
    {
        private const string SCENE_PATH = "Assets/Examples/Rts/RtsSandbox.unity";
        private const string GENERATED_DIR = "Assets/Examples/Rts/Generated";
        private const string AGENT_PREFAB = GENERATED_DIR + "/RtsAgentView.prefab";
        private const string BUILDING_PREFAB = GENERATED_DIR + "/RtsBuildingView.prefab";
        private const string OBJECT_PREFAB = GENERATED_DIR + "/RtsObjectView.prefab";
        private const string AGENT_MATERIAL = GENERATED_DIR + "/RtsAgentView.mat";
        private const string BUILDING_MATERIAL = GENERATED_DIR + "/RtsBuildingView.mat";
        private const string OBJECT_MATERIAL = GENERATED_DIR + "/RtsObjectView.mat";
        private const string CIRCLE_MESH = GENERATED_DIR + "/RtsCircle.mesh";
        private const string PANEL_SETTINGS = GENERATED_DIR + "/RtsPanelSettings.asset";
        private const string RUNTIME_THEME = "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss";

        [MenuItem("Tools/RTS/Build Sandbox Scene")]
        public static void Build()
        {
            Directory.CreateDirectory(GENERATED_DIR);

            BuildQuadPrefab(AGENT_PREFAB, AGENT_MATERIAL, "RtsAgentView", new Color(0.95f, 0.85f, 0.35f), 0.7f);
            BuildQuadPrefab(BUILDING_PREFAB, BUILDING_MATERIAL, "RtsBuildingView", Color.white, 1f);
            BuildCirclePrefab(OBJECT_PREFAB, OBJECT_MATERIAL, "RtsObjectView", new Color(0.28f, 0.62f, 0.32f));
            BuildPanelSettings();

            // Flushed to disk before anything in the scene points at them. A scene can only serialise a
            // reference to an asset that has a file to have a GUID for; referencing one that exists only in
            // memory writes a null and says nothing about it.
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Loaded *after* the new scene, not before. Opening a scene unloads unused assets, which drops
            // the native half of anything nothing is pointing at yet - leaving a managed wrapper that is
            // still non-null to C# but serialises as a null reference, silently.
            GameObject agentPrefab = Load<GameObject>(AGENT_PREFAB);
            GameObject buildingPrefab = Load<GameObject>(BUILDING_PREFAB);
            GameObject objectPrefab = Load<GameObject>(OBJECT_PREFAB);
            var panel = Load<PanelSettings>(PANEL_SETTINGS);

            BuildCamera();
            GameObject sandbox = BuildSandbox(agentPrefab, buildingPrefab, objectPrefab);
            BuildUi(panel, sandbox.GetComponent<RtsToolController>());

            EditorSceneManager.SaveScene(scene, SCENE_PATH);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"RTS sandbox scene written to {SCENE_PATH}");
        }

        private static void BuildCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -20f);

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 22f;
            camera.backgroundColor = new Color(0.13f, 0.15f, 0.17f);
            camera.clearFlags = CameraClearFlags.SolidColor;

            cameraObject.AddComponent<RtsCameraController>();
        }

        private static GameObject BuildSandbox(
            GameObject agentPrefab,
            GameObject buildingPrefab,
            GameObject objectPrefab)
        {
            var sandbox = new GameObject("RTS Sandbox");

            RtsToolController tools = sandbox.AddComponent<RtsToolController>();
            sandbox.AddComponent<RtsStartWorld>();
            sandbox.AddComponent<GridDebugOverlay>();
            sandbox.AddComponent<AgentDebugOverlay>();
            sandbox.AddComponent<SelectedAgentOverlay>();

            AgentViewSettings agents = sandbox.AddComponent<AgentViewSettings>();
            BuildingViewSettings buildings = sandbox.AddComponent<BuildingViewSettings>();
            CellObjectViewSettings objects = sandbox.AddComponent<CellObjectViewSettings>();

            SetPrivate(agents, "_agentPrefab", agentPrefab);
            SetPrivate(buildings, "_buildingPrefab", buildingPrefab);
            SetPrivate(objects, "_objectPrefab", objectPrefab);

            // Nothing to set on the tool controller here: the panel drives it, and the camera resolves to
            // Camera.main when its own field is empty.
            _ = tools;

            return sandbox;
        }

        private static void BuildUi(PanelSettings panel, RtsToolController tools)
        {
            var uiObject = new GameObject("UI");

            UIDocument document = uiObject.AddComponent<UIDocument>();

            // Through the serialized field, not the property. The property setter does re-parenting work that
            // does not mark the component dirty outside play mode, so the assignment is lost on save.
            SetPrivate(document, "m_PanelSettings", panel);

            RtsPanel rtsPanel = uiObject.AddComponent<RtsPanel>();
            SetPrivate(rtsPanel, "_tools", tools);
        }

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                Debug.LogError($"{typeof(T).Name} missing at {path}; the scene will be built incomplete.");
            }

            return asset;
        }

        private static GameObject BuildQuadPrefab(
            string prefabPath,
            string materialPath,
            string name,
            Color colour,
            float size)
        {
            Material material = CreateUnlitMaterial(materialPath, colour);

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.transform.localScale = new Vector3(size, size, 1f);

            // Primitives come with a collider; nothing here is ever raycast against, and thousands of them
            // would be paid for every physics step.
            Object.DestroyImmediate(quad.GetComponent<Collider>());

            quad.GetComponent<MeshRenderer>().sharedMaterial = material;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(quad, prefabPath);
            Object.DestroyImmediate(quad);
            return prefab;
        }

        /// <summary>
        /// A flat disc, for the things on the map that are round: a tree reads as a circle where a building
        /// reads as a box, which is the whole of "what kind of thing is that" at a glance.
        ///
        /// Scale 1 is one cell across, so <see cref="CellObjectViewSystem"/> can size an instance in cells and
        /// not care that the mesh is a fan.
        /// </summary>
        private static GameObject BuildCirclePrefab(
            string prefabPath,
            string materialPath,
            string name,
            Color colour)
        {
            Material material = CreateUnlitMaterial(materialPath, colour);

            // Two-sided. Which way a generated mesh faces depends on the winding *and* on which side of the
            // plane the camera ends up, and a disc that is invisible from one of them is a trap for whoever
            // switches SimToWorld to XZ later. Turning culling off costs nothing on a 24-triangle mesh.
            material.SetFloat("_Cull", 0f);

            var circle = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            circle.GetComponent<MeshFilter>().sharedMesh = CreateCircleMesh(CIRCLE_MESH);
            circle.GetComponent<MeshRenderer>().sharedMaterial = material;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(circle, prefabPath);
            Object.DestroyImmediate(circle);
            return prefab;
        }

        /// <summary>A triangle fan of radius 0.5 in the XY plane, saved as an asset the prefab can point at.</summary>
        private static Mesh CreateCircleMesh(string path)
        {
            const int segments = 24;

            var vertices = new Vector3[segments + 1];
            var triangles = new int[segments * 3];

            vertices[0] = Vector3.zero;
            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * 0.5f, Mathf.Sin(angle) * 0.5f, 0f);

                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % segments + 1;
            }

            var mesh = new Mesh { name = "RtsCircle", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            // CreateAsset turns the instance it is given *into* the asset, so this is the thing to hand back.
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static Material CreateUnlitMaterial(string path, Color colour)
        {
            // Explicit check rather than ??, because Unity objects overload equality and a "null" one is not
            // reliably null to the coalescing operator.
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            var material = new Material(shader);

            // Both names, because the URP and built-in shaders disagree and one of them will be the one in
            // use. Setting a property a shader does not have is harmless.
            material.SetColor("_BaseColor", colour);
            material.SetColor("_Color", colour);

            // CreateAsset turns the instance it is given *into* the asset, so this is the thing to hand back.
            // Re-loading the path instead can return null, because the import has not necessarily run yet.
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static PanelSettings BuildPanelSettings()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(RUNTIME_THEME);

            // Same call the panel makes at runtime, so the asset this writes and what the game does agree,
            // and a default-constructed PanelSettings does not ship a fixed-pixel-size panel.
            UIPanelScale.ScaleWithScreen(settings);

            AssetDatabase.CreateAsset(settings, PANEL_SETTINGS);
            return settings;
        }

        /// <summary>
        /// Sets a <c>[SerializeField]</c> the way the inspector would. The alternative is making every one of
        /// those fields public purely so that a build script can reach it, which would widen the API of every
        /// component for the benefit of one editor tool.
        /// </summary>
        private static void SetPrivate(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);

            if (property == null)
            {
                Debug.LogError($"{target.GetType().Name} has no serialized field '{field}'.");
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
