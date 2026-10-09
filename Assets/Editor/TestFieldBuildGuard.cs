using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace LemonBear.Editor
{
    // A player build must carry a compatible offline bundle for its selected platform.
    public sealed class TestFieldBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (!File.Exists(TestFieldTools.ScenePath)) return;
            if (report.summary.platform != EditorUserBuildSettings.activeBuildTarget)
                throw new BuildFailedException("Switch to the player target platform before building TestField bundles.");
            TestFieldTools.BuildBundles();
        }
    }
}
