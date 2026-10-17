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

        private const string BATTLE_SCENE_PATH = "Assets/Examples/Rts/RtsBattle.unity";
        private const string BIG_BATTLE_SCENE_PATH = "Assets/Examples/Rts/RtsBigBattle.unity";
        private const string BATTLE_AGENT_PREFAB = GENERATED_DIR + "/RtsBattleAgentView.prefab";
        private const string SWORD_PREFAB = GENERATED_DIR + "/RtsSoldierSword.prefab";
        private const string GUN_PREFAB = GENERATED_DIR + "/RtsSoldierGun.prefab";
        private const string BULLET_PREFAB = GENERATED_DIR + "/RtsBullet.prefab";
        private const string BATTLE_AGENT_MATERIAL = GENERATED_DIR + "/RtsBattleAgentView.mat";
        private const string SOLDIER_BODY_MATERIAL = GENERATED_DIR + "/RtsSoldierBody.mat";
        private const string BLADE_MATERIAL = GENERATED_DIR + "/RtsBlade.mat";
        private const string HILT_MATERIAL = GENERATED_DIR + "/RtsHilt.mat";
        private const string GUN_MATERIAL = GENERATED_DIR + "/RtsGun.mat";
        private const string FLASH_MATERIAL = GENERATED_DIR + "/RtsMuzzleFlash.mat";
        private const string BULLET_MATERIAL = GENERATED_DIR + "/RtsBullet.mat";

        /// <summary>
        /// A soldier's body, in cells across. Wider than the agent view's disc behind it, so that disc never
        /// shows round the edge, and only a little wider than the 0.84 the simulation thinks the body is.
        /// </summary>
        private const float SOLDIER_BODY = 0.9f;

        /// <summary>
        /// Draw order of the combat view. The project renders through the 2D renderer, which orders by sorting
        /// order rather than by depth - so the soldier drawn a little nearer the camera than the agent disc
        /// behind it still loses to it unless it says so here.
        /// </summary>
        private const int SOLDIER_ORDER = 10;

        private const int WEAPON_ORDER = 11;
        private const int FLASH_ORDER = 12;
        private const int BULLET_ORDER = 20;

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

        /// <summary>
        /// The battle scene: two armies of swords and guns on an open field (<see cref="RtsBattleWorld"/>), drawn
        /// by the combat view so that blades, guns and the rounds they fire can be seen.
        ///
        /// Shares the sandbox's generated assets where it can and only builds them if they are missing, because
        /// re-creating one gives it a new GUID and would leave the sandbox scene pointing at nothing.
        /// </summary>
        [MenuItem("Tools/RTS/Build Battle Scene")]
        public static void BuildBattle() =>
            BuildBattle(BATTLE_SCENE_PATH, cameraSize: 10f, agentOverlay: true, configure: null);

        /// <summary>
        /// The same battle at 300 a side: 180 swords in front of 120 guns, in blocks rather than columns, on a
        /// map twice the size. Everything else - the view, the rules, the prefabs - is the small battle's.
        /// </summary>
        /// <remarks>
        /// No per-agent debug overlay: it draws a gizmo trail per agent in the Scene view, which is a debugging
        /// aid at twenty agents and a frame-time cost at six hundred.
        /// </remarks>
        [MenuItem("Tools/RTS/Build Big Battle Scene")]
        public static void BuildBigBattle() => BuildBattle(BIG_BATTLE_SCENE_PATH, cameraSize: 24f,
                                                           agentOverlay: false, world =>
        {
            SetValue(world, "_worldRadius", p => p.intValue = 48);
            SetValue(world, "_swordsPerSide", p => p.intValue = 180);
            SetValue(world, "_gunsPerSide", p => p.intValue = 120);
            SetValue(world, "_gap", p => p.floatValue = 16f);
            SetValue(world, "_depth", p => p.floatValue = 12f);
            SetValue(world, "_rankDepth", p => p.floatValue = 7f);
            SetValue(world, "_cellsPerSoldier", p => p.floatValue = 1.3f);
            SetValue(world, "_treesPerWood", p => p.intValue = 14);
        });

        private static void BuildBattle(string scenePath, float cameraSize, bool agentOverlay,
                                        System.Action<RtsBattleWorld> configure)
        {
            Directory.CreateDirectory(GENERATED_DIR);

            if (!File.Exists(OBJECT_PREFAB))
            {
                BuildCirclePrefab(OBJECT_PREFAB, OBJECT_MATERIAL, "RtsObjectView", new Color(0.28f, 0.62f, 0.32f));
            }

            if (!File.Exists(BUILDING_PREFAB))
            {
                BuildQuadPrefab(BUILDING_PREFAB, BUILDING_MATERIAL, "RtsBuildingView", Color.white, 1f);
            }

            if (!File.Exists(PANEL_SETTINGS))
            {
                BuildPanelSettings();
            }

            Mesh circle = LoadOrCreateCircleMesh(CIRCLE_MESH);

            // One body material for both soldier prefabs. Creating it once per prefab replaces the first
            // asset with the second, and the prefab saved in between is left holding the one that was lost.
            Material body = CreateUnlitMaterial(SOLDIER_BODY_MATERIAL, Color.white, twoSided: true);

            BuildBattleAgentPrefab(circle);
            BuildSwordPrefab(circle, body);
            BuildGunPrefab(circle, body);
            BuildBulletPrefab();

            // Flushed before the scene points at them, for the reason Build gives.
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Camera camera = BuildCamera();
            camera.orthographicSize = cameraSize;

            var battle = new GameObject("RTS Battle");

            RtsToolController tools = battle.AddComponent<RtsToolController>();
            RtsBattleWorld world = battle.AddComponent<RtsBattleWorld>();
            configure?.Invoke(world);
            if (agentOverlay)
            {
                battle.AddComponent<AgentDebugOverlay>();
            }

            battle.AddComponent<SelectedAgentOverlay>();

            AgentViewSettings agents = battle.AddComponent<AgentViewSettings>();
            BuildingViewSettings buildings = battle.AddComponent<BuildingViewSettings>();
            CellObjectViewSettings objects = battle.AddComponent<CellObjectViewSettings>();

            SetPrivate(agents, "_agentPrefab", Load<GameObject>(BATTLE_AGENT_PREFAB));
            SetPrivate(buildings, "_buildingPrefab", Load<GameObject>(BUILDING_PREFAB));
            SetPrivate(objects, "_objectPrefab", Load<GameObject>(OBJECT_PREFAB));

            // Its own object, because its instances are parented under it and would bury the battle object's
            // components in the hierarchy.
            var combatObject = new GameObject("Combat View");
            CombatViewSettings combat = combatObject.AddComponent<CombatViewSettings>();
            SetPrivate(combat, "_prefabs.Sword", Load<GameObject>(SWORD_PREFAB));
            SetPrivate(combat, "_prefabs.Gun", Load<GameObject>(GUN_PREFAB));
            SetPrivate(combat, "_prefabs.Bullet", Load<GameObject>(BULLET_PREFAB));

            BuildUi(Load<PanelSettings>(PANEL_SETTINGS), tools);

            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"RTS battle scene written to {scenePath}");
        }

        private static Camera BuildCamera()
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
            return camera;
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
            circle.GetComponent<MeshFilter>().sharedMesh = LoadOrCreateCircleMesh(CIRCLE_MESH);
            circle.GetComponent<MeshRenderer>().sharedMaterial = material;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(circle, prefabPath);
            Object.DestroyImmediate(circle);
            return prefab;
        }

        /// <summary>
        /// The disc mesh, made once. Both scenes' prefabs point at it, and re-creating an asset replaces it
        /// under a new GUID - which would leave whichever scene was not being rebuilt pointing at nothing.
        /// </summary>
        private static Mesh LoadOrCreateCircleMesh(string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            return existing != null ? existing : CreateCircleMesh(path);
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

        /// <summary>
        /// The agent view for the battle: a grey disc a little smaller than a soldier's body, so it is hidden
        /// behind every armed agent and only an unarmed one shows it.
        /// </summary>
        private static void BuildBattleAgentPrefab(Mesh circle)
        {
            Material material = CreateUnlitMaterial(BATTLE_AGENT_MATERIAL, new Color(0.6f, 0.6f, 0.6f), twoSided: true);

            var agent = new GameObject("RtsBattleAgentView", typeof(MeshFilter), typeof(MeshRenderer));
            agent.GetComponent<MeshFilter>().sharedMesh = circle;
            agent.GetComponent<MeshRenderer>().sharedMaterial = material;
            agent.transform.localScale = new Vector3(0.84f, 0.84f, 1f);

            SavePrefab(agent, BATTLE_AGENT_PREFAB);
        }

        /// <summary>
        /// A body with a sword in its right hand. The blade hangs off a 'Hand' pivot rather than off the body,
        /// because the swing is a turn about the hand - <see cref="CombatView"/> rotates that and nothing else.
        /// Forward is +Y, which is what <see cref="SimToWorld.Rotation"/> turns onto the facing.
        /// </summary>
        private static void BuildSwordPrefab(Mesh circle, Material body)
        {
            var root = new GameObject("RtsSoldierSword");
            AddBody(root.transform, circle, body);

            Transform hand = new GameObject("Hand").transform;
            hand.SetParent(root.transform, false);
            hand.localPosition = new Vector3(0.32f, 0.12f, -0.02f);

            AddQuad(hand, "Blade", new Vector3(0f, 0.44f, 0f), new Vector2(0.1f, 0.74f),
                    CreateUnlitMaterial(BLADE_MATERIAL, new Color(0.92f, 0.94f, 1f)), WEAPON_ORDER);
            AddQuad(hand, "Guard", new Vector3(0f, 0.07f, -0.005f), new Vector2(0.28f, 0.07f),
                    CreateUnlitMaterial(HILT_MATERIAL, new Color(0.85f, 0.68f, 0.25f)), FLASH_ORDER);

            SavePrefab(root, SWORD_PREFAB);
        }

        /// <summary>
        /// A body with a gun held out in front. 'Gun' is an unscaled pivot so that the barrel's scale does not
        /// squash the 'Flash' beside it, and the flash sits at the muzzle because that is where a round leaves.
        /// </summary>
        private static void BuildGunPrefab(Mesh circle, Material body)
        {
            var root = new GameObject("RtsSoldierGun");
            AddBody(root.transform, circle, body);

            Transform gun = new GameObject("Gun").transform;
            gun.SetParent(root.transform, false);
            gun.localPosition = new Vector3(0.2f, 0.3f, -0.02f);

            AddQuad(gun, "Barrel", new Vector3(0f, 0.22f, 0f), new Vector2(0.13f, 0.56f),
                    CreateUnlitMaterial(GUN_MATERIAL, new Color(0.62f, 0.56f, 0.5f)), WEAPON_ORDER);

            GameObject flash = AddQuad(gun, "Flash", new Vector3(0f, 0.6f, -0.005f), new Vector2(0.32f, 0.32f),
                                       CreateUnlitMaterial(FLASH_MATERIAL, new Color(1f, 0.75f, 0.2f)),
                                       FLASH_ORDER);
            flash.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            flash.SetActive(false);

            SavePrefab(root, GUN_PREFAB);
        }

        /// <summary>A round in flight: a short streak along +Y, so it reads as moving the way it points.</summary>
        private static void BuildBulletPrefab()
        {
            var root = new GameObject("RtsBullet");
            AddQuad(root.transform, "Streak", Vector3.zero, new Vector2(0.12f, 0.5f),
                    CreateUnlitMaterial(BULLET_MATERIAL, new Color(1f, 0.95f, 0.55f)), BULLET_ORDER);

            SavePrefab(root, BULLET_PREFAB);
        }

        /// <summary>The disc the faction colour goes on. White, because a tint multiplies it.</summary>
        private static void AddBody(Transform parent, Mesh circle, Material material)
        {
            var body = new GameObject("Body", typeof(MeshFilter), typeof(MeshRenderer));
            body.transform.SetParent(parent, false);
            body.transform.localScale = new Vector3(SOLDIER_BODY, SOLDIER_BODY, 1f);
            body.GetComponent<MeshFilter>().sharedMesh = circle;
            var renderer = body.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = SOLDIER_ORDER;
        }

        private static GameObject AddQuad(Transform parent, string name, Vector3 position, Vector2 size,
                                          Material material, int sortingOrder)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = position;
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);

            // Nothing is ever raycast against, for the reason BuildQuadPrefab gives.
            Object.DestroyImmediate(quad.GetComponent<Collider>());

            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = sortingOrder;
            return quad;
        }

        private static void SavePrefab(GameObject root, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        /// <param name="twoSided">
        /// For discs, which are only visible from one side otherwise (see BuildCirclePrefab). Set before the
        /// asset is written rather than after, so that it is part of what is saved.
        /// </param>
        private static Material CreateUnlitMaterial(string path, Color colour, bool twoSided = false)
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

            if (twoSided)
            {
                material.SetFloat("_Cull", 0f);
            }

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
        /// <summary><see cref="SetPrivate"/> for a plain value rather than an object reference.</summary>
        private static void SetValue(Object target, string field, System.Action<SerializedProperty> set)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);

            if (property == null)
            {
                Debug.LogError($"{target.GetType().Name} has no serialized field '{field}'.");
                return;
            }

            set(property);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

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
