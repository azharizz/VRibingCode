#if UNITY_EDITOR
// Headless macOS player build: batchmode
//   Unity -batchmode -nographics -projectPath quest_loft \
//     -executeMethod LoftBuilder.BuildMac -quit -logFile Tools/build-mac.log
using UnityEditor;
using UnityEngine;

public static class LoftBuilder
{
    public static void BuildMac()
    {
        // Active target may be Android after an APK build; rest it here so
        // the Mac artifact + later editor Play both use the desktop path.
        EditorUserBuildSettings.SwitchActiveBuildTarget(UnityEditor.Build.NamedBuildTarget.Standalone, BuildTarget.StandaloneOSX);
        var scenes = new[] { LoftQuestBuild.RebuiltScene() };
        var opts = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = "Builds/Loft.app",
            target = BuildTarget.StandaloneOSX,
            options = System.Environment.GetEnvironmentVariable("LOFT_DEV_BUILD") == "1" ? BuildOptions.Development : BuildOptions.None,
        };
        var report = BuildPipeline.BuildPlayer(opts);
        // OpenXR looks for openxr_loader next to its plugin; the build only ships ARM64/libopenxr_loader.
        var plugins = "Builds/Loft.app/Contents/PlugIns";
        if (System.IO.File.Exists(plugins + "/ARM64/libopenxr_loader.dylib"))
            System.IO.File.Copy(plugins + "/ARM64/libopenxr_loader.dylib", plugins + "/openxr_loader.dylib", true);
        Debug.Log($"[BUILD] macOS player: {report.summary.result} in {report.summary.totalTime.TotalSeconds:F0}s -> Builds/Loft.app");
        EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }
}
#endif
