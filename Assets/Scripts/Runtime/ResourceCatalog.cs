using System;
using UnityEngine;

namespace LemonBear
{
    [Serializable]
    public sealed class ResourceCatalog
    {
        public int schema = 1;
        public string platform;
        public string unityVersion;
        public string bundle;
        public string hash;
        public uint crc;
        public string playerAsset = "Assets/GameContent/Prefabs/Player.prefab";

        public void Validate(string expectedPlatform)
        {
            if (schema != 1 || platform != expectedPlatform || unityVersion != Application.unityVersion)
                throw new InvalidOperationException("Resource platform or Unity version is incompatible.");
            if (string.IsNullOrEmpty(hash) || !System.Text.RegularExpressions.Regex.IsMatch(hash, "^[a-f0-9]{32}$") ||
                bundle != "gamecontent-" + hash + ".bundle" || crc == 0 ||
                playerAsset != "Assets/GameContent/Prefabs/Player.prefab")
                throw new InvalidOperationException("Invalid resource catalog.");
        }

        public static string Platform
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.EditorUserBuildSettings.activeBuildTarget.ToString();
#elif UNITY_STANDALONE_WIN
                return "StandaloneWindows64";
#elif UNITY_ANDROID
                return "Android";
#elif UNITY_IOS
                return "iOS";
#elif UNITY_STANDALONE_OSX
                return "StandaloneOSX";
#elif UNITY_STANDALONE_LINUX
                return "StandaloneLinux64";
#else
                return Application.platform.ToString();
#endif
            }
        }
    }
}
