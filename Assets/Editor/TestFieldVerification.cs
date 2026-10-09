using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using XLua;

namespace LemonBear.Editor
{
    // Real Play Mode integration check, including downloaded Lua and CharacterController collisions.
    [InitializeOnLoad]
    public static class TestFieldVerification
    {
        private const string Key = "LemonBear.Verification";
        private static double started;
        private static int readyFrames;
        private static bool checkedGame;
        private static int exitCode;

        static TestFieldVerification()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (!string.IsNullOrEmpty(SessionState.GetString(Key, "")))
            { started = EditorApplication.timeSinceStartup; EditorApplication.update += Poll; }
        }

        [MenuItem("LemonBear/Verify Offline Fallback")]
        public static void RunOffline() { Run("offline"); }

        [MenuItem("LemonBear/Verify GitHub Resources")]
        public static void RunOnline() { Run("online"); }

        public static void RunCache() { Run("cache"); }

        private static void Run(string mode)
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(TestFieldTools.ScenePath);
            checkedGame = false; readyFrames = 0;
            var bootstrap = UnityEngine.Object.FindObjectOfType<TestFieldBootstrap>();
            SessionState.SetString(Key + ".url", bootstrap.resourceBaseUrl);
            if (mode == "offline") bootstrap.resourceBaseUrl = "http://127.0.0.1:1";
            SessionState.SetString(Key, mode);
            started = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Poll; EditorApplication.update += Poll;
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayMode(PlayModeStateChange state)
        {
            var mode = SessionState.GetString(Key, "");
            if (mode == "") return;
            if (state == PlayModeStateChange.EnteredEditMode && checkedGame)
            {
                UnityEngine.Object.FindObjectOfType<TestFieldBootstrap>().resourceBaseUrl = SessionState.GetString(Key + ".url", "");
                SessionState.EraseString(Key + ".url");
                SessionState.EraseString(Key);
                EditorApplication.update -= Poll;
                if (Application.isBatchMode) EditorApplication.Exit(exitCode);
            }
        }

        private static void Poll()
        {
            if (checkedGame) return;
            var mode = SessionState.GetString(Key, "");
            if (mode == "") return;
            if (EditorApplication.timeSinceStartup - started > 120) { Finish(false, "Timed out loading TestField."); return; }
            if (!EditorApplication.isPlaying) return;
            var bootstrap = UnityEngine.Object.FindObjectOfType<TestFieldBootstrap>();
            if (bootstrap == null || !bootstrap.IsReady) return;
            if (++readyFrames < 20) return;
            try
            {
                if (mode == "offline" && bootstrap.ResourceSource != "Packaged (offline)") throw new Exception("Offline fallback was not used.");
                if (mode != "offline" && !bootstrap.ResourceSource.StartsWith("GitHub")) throw new Exception("GitHub resources were not used.");
                if (mode != "offline" && !File.Exists(Path.Combine(Application.persistentDataPath, "AssetBundles", ResourceCatalog.Platform, "gamecontent-" + bootstrap.ResourceHash + ".bundle")))
                    throw new Exception("Downloaded bundle was not cached.");
                if (mode == "cache" && !bootstrap.ResourceWasCached) throw new Exception("Second launch did not reuse the cached bundle.");
                bootstrap.enabled = false;
                var lua = (LuaEnv)typeof(TestFieldBootstrap).GetField("lua", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bootstrap);
                lua.DoString(@"
local U = CS.UnityEngine
local V = U.Vector3
local P = require('player')
local C = require('camera')
local object = U.GameObject.Find('Player')
local camera = U.GameObject('Test Camera'):AddComponent(typeof(U.Camera))
camera.transform.rotation = U.Quaternion.identity
local s = P.create(object)
local function reset()
    s.controller.enabled = false
    s.transform.position = V(0, 1.1, 0)
    s.controller.enabled = true
    s.vertical_speed = 0
    for i = 1, 30 do P.update(s, camera, 0.02, 0, 0, false, false) end
end
local function distance(h, v, run)
    reset()
    local before = s.transform.position
    for i = 1, 50 do P.update(s, camera, 0.02, h, v, run, false) end
    local delta = s.transform.position - before
    delta.y = 0
    return delta.magnitude
end
local straight = distance(1, 0, false)
local diagonal = distance(1, -1, false)
local sprint = distance(1, 0, true)
assert(math.abs(straight - 5) < 0.15, 'walk speed')
assert(math.abs(straight - diagonal) < 0.15, 'diagonal normalization')
assert(math.abs(sprint - 8) < 0.15, 'run speed')
reset()
assert(s.controller.isGrounded, 'ground contact')
local base = s.transform.position.y
local highest = base
for i = 1, 100 do
    P.update(s, camera, 0.02, 0, 0, false, i == 1)
    highest = math.max(highest, s.transform.position.y)
end
assert(highest - base > 1.2 and highest - base < 1.7, 'jump height')
assert(s.controller.isGrounded and math.abs(s.transform.position.y - base) < 0.1, 'land on ground')
reset()
for i = 1, 350 do P.update(s, camera, 0.02, 1, 0, true, false) end
assert(s.transform.position.x < 19.5 and s.transform.position.x > 18, 'wall collision')
reset()
local follow = C.create(camera, s.transform)
local old = camera.transform.position
s.controller.enabled = false
s.transform.position = s.transform.position + V(4, 0, 0)
s.controller.enabled = true
C.update(follow, 0.02)
assert(camera.transform.position.x > old.x and camera.transform.position.x < old.x + 4, 'camera damping')
for i = 1, 100 do C.update(follow, 0.02) end
assert((camera.transform.position - (s.transform.position + follow.offset)).magnitude < 0.01, 'camera follows target')
s.controller.enabled = false
s.transform.position = V(0, -12, 0)
s.controller.enabled = true
P.update(s, camera, 0.02, 0, 0, false, false)
assert((s.transform.position - s.spawn).magnitude < 0.01, 'fall respawn')
U.Object.Destroy(camera.gameObject)
reset()
require('main').late_update(1)
print('Lua gameplay assertions passed: walk, diagonal, sprint, jump, landing, walls, camera, respawn')
", "TestField gameplay verification");
                if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                {
                    var camera = bootstrap.followCamera;
                    var rt = new RenderTexture(960, 600, 24);
                    var previous = RenderTexture.active;
                    camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                    var image = new Texture2D(960, 600, TextureFormat.RGB24, false);
                    image.ReadPixels(new Rect(0, 0, 960, 600), 0, 0); image.Apply();
                    Directory.CreateDirectory("Build");
                    File.WriteAllBytes("Build/TestFieldPreview.png", image.EncodeToPNG());
                    camera.targetTexture = null; RenderTexture.active = previous;
                    UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(rt);
                }
                Finish(true, mode + ": " + bootstrap.ResourceSource + " / " + bootstrap.ResourceHash);
            }
            catch (Exception ex) { Finish(false, ex.ToString()); }
        }

        private static void Finish(bool success, string details)
        {
            checkedGame = true; exitCode = success ? 0 : 1;
            Directory.CreateDirectory("Build");
            var mode = SessionState.GetString(Key, "unknown");
            File.WriteAllText("Build/Verification-" + mode + ".txt", (success ? "PASS " : "FAIL ") + details);
            if (success) Debug.Log("TestField verification PASS: " + details);
            else Debug.LogError("TestField verification FAIL: " + details);
            EditorApplication.ExitPlaymode();
        }
    }
}
