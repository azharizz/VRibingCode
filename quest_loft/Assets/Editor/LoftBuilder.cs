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
            options = BuildOptions.None,
        };
        var report = BuildPipeline.BuildPlayer(opts);
        Debug.Log($"[BUILD] macOS player: {report.summary.result} in {report.summary.totalTime.TotalSeconds:F0}s -> Builds/Loft.app");
        EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }
}
#endif
