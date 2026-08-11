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
        private const string AGENT_MATERIAL = GENERATED_DIR + "/RtsAgentView.mat";
        private const string BUILDING_MATERIAL = GENERATED_DIR + "/RtsBuildingView.mat";
        private const string PANEL_SETTINGS = GENERATED_DIR + "/RtsPanelSettings.asset";
        private const string RUNTIME_THEME = "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss";

        [MenuItem("Tools/RTS/Build Sandbox Scene")]
        public static void Build()
        {
            Directory.CreateDirectory(GENERATED_DIR);

            BuildQuadPrefab(AGENT_PREFAB, AGENT_MATERIAL, "RtsAgentView", new Color(0.95f, 0.85f, 0.35f), 0.7f);
            BuildQuadPrefab(BUILDING_PREFAB, BUILDING_MATERIAL, "RtsBuildingView", Color.white, 1f);
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
            var panel = Load<PanelSettings>(PANEL_SETTINGS);

            BuildCamera();
            GameObject sandbox = BuildSandbox(agentPrefab, buildingPrefab);
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

        private static GameObject BuildSandbox(GameObject agentPrefab, GameObject buildingPrefab)
        {
            var sandbox = new GameObject("RTS Sandbox");

            RtsToolController tools = sandbox.AddComponent<RtsToolController>();
            sandbox.AddComponent<RtsStartWorld>();
            sandbox.AddComponent<GridDebugOverlay>();

            AgentViewSettings agents = sandbox.AddComponent<AgentViewSettings>();
            BuildingViewSettings buildings = sandbox.AddComponent<BuildingViewSettings>();

            SetPrivate(agents, "_agentPrefab", agentPrefab);
            SetPrivate(buildings, "_buildingPrefab", buildingPrefab);

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
