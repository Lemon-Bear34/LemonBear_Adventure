using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace LemonBear.Editor
{
    public static class TestFieldTools
    {
        public const string ScenePath = "Assets/Scenes/TestField.unity";
        private const string ContentPath = "Assets/GameContent";

        [MenuItem("LemonBear/Create TestField")]
        public static void CreateScene()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(ContentPath + "/Materials");
            Directory.CreateDirectory(ContentPath + "/Prefabs");
            AssetDatabase.Refresh();
            var blue = Material("PlayerBlue", new Color(0.04f, 0.32f, 0.95f));
            var ground = Material("Ground", new Color(0.3f, 0.4f, 0.36f));
            var obstacle = Material("Obstacle", new Color(0.7f, 0.55f, 0.3f));
            var marker = Material("Marker", new Color(0.72f, 0.78f, 0.75f));

            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "Player";
            UnityEngine.Object.DestroyImmediate(capsule.GetComponent<CapsuleCollider>());
            capsule.GetComponent<Renderer>().sharedMaterial = blue;
            var controller = capsule.AddComponent<CharacterController>();
            controller.height = 2; controller.radius = 0.5f;
            controller.center = Vector3.zero; controller.stepOffset = 0.3f;
            controller.skinWidth = 0.05f; controller.minMoveDistance = 0;
            var prefab = PrefabUtility.SaveAsPrefabAsset(capsule, ContentPath + "/Prefabs/Player.prefab");
            UnityEngine.Object.DestroyImmediate(capsule);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.6f, 0.7f);
            RenderSettings.fog = false;
            Cube("Ground", new Vector3(0, -0.25f, 0), new Vector3(40, 0.5f, 40), ground);
            Cube("North Wall", new Vector3(0, 1, 20), new Vector3(40, 2, 0.5f), obstacle);
            Cube("South Wall", new Vector3(0, 1, -20), new Vector3(40, 2, 0.5f), obstacle);
            Cube("East Wall", new Vector3(20, 1, 0), new Vector3(0.5f, 2, 40), obstacle);
            Cube("West Wall", new Vector3(-20, 1, 0), new Vector3(0.5f, 2, 40), obstacle);
            Cube("Obstacle A", new Vector3(4, 0.75f, 5), new Vector3(3, 1.5f, 3), obstacle);
            Cube("Obstacle B", new Vector3(-5, 0.5f, 8), new Vector3(4, 1, 2), obstacle);
            for (int i = -3; i <= 3; i++)
            {
                Cube("Grid X " + i, new Vector3(i * 5, 0.006f, 0), new Vector3(0.045f, 0.01f, 39), marker, false);
                Cube("Grid Z " + i, new Vector3(0, 0.006f, i * 5), new Vector3(39, 0.01f, 0.045f), marker, false);
            }
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.2f; light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);
            RenderSettings.sun = light;
            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.gameObject.AddComponent<AudioListener>();
            camera.transform.position = new Vector3(0, 7.1f, -8);
            camera.transform.LookAt(new Vector3(0, 1.7f, 0));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.45f, 0.65f, 0.82f);
            camera.nearClipPlane = 0.1f; camera.farClipPlane = 100;
            var preview = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            preview.name = "Player (preview)";
            preview.transform.position = new Vector3(0, 1.1f, 0);
            var bootstrap = new GameObject("Game Bootstrap").AddComponent<TestFieldBootstrap>();
            bootstrap.followCamera = camera; bootstrap.previewPlayer = preview;
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("Created TestField with blue capsule, camera, obstacles and Lua bootstrap.");
        }

        [MenuItem("LemonBear/Build AssetBundles")]
        public static void BuildBundles()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            if (target == BuildTarget.StandaloneWindows) throw new InvalidOperationException("Select Windows x86_64 for TestField.");
            var platform = target.ToString();
            var output = Path.GetFullPath("Build/AssetBundles/" + platform);
            Directory.CreateDirectory(output);
            var assets = Directory.GetFiles(ContentPath + "/Lua", "*.lua.txt").Select(p => p.Replace('\\', '/')).ToList();
            assets.Add(ContentPath + "/Prefabs/Player.prefab");
            var manifest = BuildPipeline.BuildAssetBundles(output, new[] {
                new AssetBundleBuild { assetBundleName = "gamecontent", assetNames = assets.ToArray() }
            }, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, target);
            if (manifest == null) throw new InvalidOperationException("AssetBundle build failed.");
            var hash = manifest.GetAssetBundleHash("gamecontent").ToString();
            if (!BuildPipeline.GetCRCForAssetBundle(output + "/gamecontent", out var crc))
                throw new InvalidOperationException("Cannot compute bundle CRC.");
            var catalog = new ResourceCatalog { platform = platform, unityVersion = Application.unityVersion,
                bundle = "gamecontent-" + hash + ".bundle", hash = hash, crc = crc };
            File.Copy(output + "/gamecontent", output + "/" + catalog.bundle, true);
            File.WriteAllText(output + "/catalog.json", JsonUtility.ToJson(catalog, true));
            var packaged = "Assets/StreamingAssets/AssetBundles/" + platform;
            Directory.CreateDirectory(packaged);
            // Remove only this tool's obsolete local bundles; published versions remain immutable.
            foreach (var old in Directory.GetFiles(packaged, "gamecontent-*.bundle"))
                if (Path.GetFileName(old) != catalog.bundle) AssetDatabase.DeleteAsset(old.Replace('\\', '/'));
            File.Copy(output + "/" + catalog.bundle, packaged + "/" + catalog.bundle, true);
            File.Copy(output + "/catalog.json", packaged + "/catalog.json", true);
            AssetDatabase.Refresh();
            AssetDatabase.SaveAssets();
            Debug.Log("Built resources: " + output + "/" + catalog.bundle);
        }

        public static void CreateAndBuild() { CreateScene(); BuildBundles(); }

        private static Material Material(string name, Color color)
        {
            var path = ContentPath + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.25f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void Cube(string name, Vector3 position, Vector3 scale, Material material, bool collision = true)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name; cube.transform.position = position; cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            if (!collision) UnityEngine.Object.DestroyImmediate(cube.GetComponent<BoxCollider>());
        }
    }
}
