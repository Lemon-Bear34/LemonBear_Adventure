using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using XLua;

namespace LemonBear
{
    // C# owns loading/lifecycle only. Input, movement, gravity and camera behavior live in Lua.
    public sealed class TestFieldBootstrap : MonoBehaviour
    {
        public string resourceBaseUrl = "https://raw.githubusercontent.com/Lemon-Bear34/LemonBear_Adventure/ab-resources";
        public Camera followCamera;
        public GameObject previewPlayer;
        public bool checkRemote = true;
        public bool IsReady { get; private set; }
        public string Status { get; private set; } = "Starting...";
        public string ResourceSource { get; private set; }
        public string ResourceHash { get; private set; }
        public bool ResourceWasCached { get; private set; }
        private LuaEnv lua;
        private LuaTable game;
        private LuaFunction update;
        private LuaFunction lateUpdate;
        private AssetBundle content;
        private GameObject player;
        private bool loading;

        private IEnumerator Start() { return Load(); }

        private IEnumerator Load()
        {
            loading = true;
            var platform = ResourceCatalog.Platform;
            ResourceCatalog catalog = null;
            string remoteError = null;
            if (checkRemote)
            {
                Status = "Checking GitHub resources...";
                using (var request = UnityWebRequest.Get(resourceBaseUrl.TrimEnd('/') + "/" + platform + "/catalog.json?t=" + DateTime.UtcNow.Ticks))
                {
                    request.timeout = 12;
                    yield return request.SendWebRequest();
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        try { catalog = JsonUtility.FromJson<ResourceCatalog>(request.downloadHandler.text); catalog.Validate(platform); }
                        catch (Exception ex) { remoteError = ex.Message; catalog = null; }
                    }
                    else remoteError = request.error;
                }
                if (catalog != null)
                {
                    Status = "Downloading / loading cached resources...";
                    yield return LoadRemote(catalog);
                    if (content == null) remoteError = "Bundle download, disk cache or CRC validation failed.";
                }
            }
            if (content == null)
            {
                if (checkRemote) Debug.LogWarning("GitHub resources unavailable; using packaged resources. " + remoteError);
                Status = "Loading packaged resources...";
                string path = Application.streamingAssetsPath + "/AssetBundles/" + platform + "/";
                if (!path.Contains("://")) path = new Uri(path).AbsoluteUri;
                using (var request = UnityWebRequest.Get(path + "catalog.json"))
                {
                    request.timeout = 10;
                    yield return request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Fail("No packaged resources. Run LemonBear > Build AssetBundles. " + request.error); yield break;
                    }
                    try { catalog = JsonUtility.FromJson<ResourceCatalog>(request.downloadHandler.text); catalog.Validate(platform); }
                    catch (Exception ex) { Fail(ex.Message); yield break; }
                }
                using (var request = UnityWebRequestAssetBundle.GetAssetBundle(path + catalog.bundle, catalog.crc))
                {
                    request.timeout = 30;
                    yield return request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success) { Fail(request.error); yield break; }
                    content = DownloadHandlerAssetBundle.GetContent(request);
                }
                ResourceSource = "Packaged (offline)";
            }
            try
            {
                var prefab = content.LoadAsset<GameObject>(catalog.playerAsset);
                if (prefab == null || followCamera == null) throw new InvalidOperationException("Player prefab or camera missing.");
                var scripts = new Dictionary<string, byte[]>();
                foreach (var asset in content.LoadAllAssets<TextAsset>())
                    scripts.Add(asset.name.Replace(".lua", ""), Encoding.UTF8.GetBytes(asset.text));
                if (previewPlayer != null) previewPlayer.SetActive(false);
                player = Instantiate(prefab, new Vector3(0, 1.1f, 0), Quaternion.identity);
                player.name = "Player";
                lua = new LuaEnv();
                lua.AddLoader((ref string module) => scripts.TryGetValue(module.Replace('.', '/'), out var bytes) ? bytes : null);
                game = (LuaTable)lua.DoString("return require('main')", "TestField/main")[0];
                using (var initialize = game.Get<LuaFunction>("initialize")) initialize.Action(player, followCamera);
                update = game.Get<LuaFunction>("update");
                lateUpdate = game.Get<LuaFunction>("late_update");
                ResourceHash = catalog.hash;
                Status = "WASD / Arrows: move   Shift: run   Space: jump";
                IsReady = true;
                Debug.Log("TestField ready: " + ResourceSource + " / " + ResourceHash);
            }
            catch (Exception ex) { Release(); Fail(ex.ToString()); }
            loading = false;
        }

        private IEnumerator LoadRemote(ResourceCatalog catalog)
        {
            // Own versioned cache avoids the Editor's shared native cache rename failures.
            string directory = Path.Combine(Application.persistentDataPath, "AssetBundles", catalog.platform);
            string file = Path.Combine(directory, catalog.bundle);
            ResourceWasCached = false;
            try { Directory.CreateDirectory(directory); }
            catch (Exception ex) { Debug.LogWarning("Cannot create resource cache: " + ex.Message); yield break; }
            if (File.Exists(file))
            {
                var cached = AssetBundle.LoadFromFileAsync(file, catalog.crc);
                yield return cached;
                content = cached.assetBundle;
                if (content != null)
                {
                    ResourceWasCached = true; ResourceSource = "GitHub version cache"; yield break;
                }
                try { File.Delete(file); }
                catch (Exception ex) { Debug.LogWarning("Cannot remove invalid cached bundle: " + ex.Message); yield break; }
            }
            string temporary = file + ".download-" + Guid.NewGuid().ToString("N");
            using (var request = UnityWebRequest.Get(resourceBaseUrl.TrimEnd('/') + "/" + catalog.platform + "/" + catalog.bundle))
            {
                request.downloadHandler = new DownloadHandlerFile(temporary) { removeFileOnAbort = true };
                request.timeout = 30;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning("Resource download failed: " + request.error);
                    TryDelete(temporary); yield break;
                }
            }
            // This Unity/Windows combination can fail renames with ERROR_INVALID_HANDLE.
            // Copy a finished download; CRC rejects any interrupted copy on the next launch.
            try { File.Copy(temporary, file, false); TryDelete(temporary); }
            catch (Exception ex) { Debug.LogWarning("Cannot save resource cache: " + ex.Message); TryDelete(temporary); yield break; }
            var downloaded = AssetBundle.LoadFromFileAsync(file, catalog.crc);
            yield return downloaded;
            content = downloaded.assetBundle;
            if (content != null) ResourceSource = "GitHub download";
            else TryDelete(file);
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { Debug.LogWarning("Cannot remove partial resource file: " + ex.Message); }
        }

        private void Update()
        {
            if (!IsReady) return;
            try { update.Action(Time.deltaTime); lua.Tick(); }
            catch (Exception ex) { IsReady = false; Fail(ex.ToString()); }
        }

        private void LateUpdate()
        {
            if (!IsReady) return;
            try { lateUpdate.Action(Time.deltaTime); }
            catch (Exception ex) { IsReady = false; Fail(ex.ToString()); }
        }

        private void Fail(string message) { loading = false; Status = message; Debug.LogError(message); }

        private void OnGUI()
        {
            GUI.Box(new Rect(12, 12, 620, 100), "TestField - Lua + AssetBundle");
            GUI.Label(new Rect(26, 38, 590, 40), Status);
            GUI.Label(new Rect(26, 78, 590, 24), ResourceSource ?? "Waiting for resources");
            if (!IsReady && !loading && GUI.Button(new Rect(26, 122, 180, 32), "Retry loading resources"))
            { Release(); StartCoroutine(Load()); }
        }

        private void OnDestroy() { Release(); }

        private void Release()
        {
            IsReady = false;
            update?.Dispose(); update = null;
            lateUpdate?.Dispose(); lateUpdate = null;
            game?.Dispose(); game = null;
            lua?.Dispose(); lua = null;
            if (player != null) Destroy(player);
            if (content != null) content.Unload(true);
            content = null;
        }
    }
}
