#if UNITY_EDITOR
// VIRTUAL LOFT — Quest-nearest setup + FocusLoft.apk build (headless).
//   Unity -batchmode -nographics -projectPath quest_loft \
//     -executeMethod LoftQuestBuild.ConfigureQuest -quit -logFile Tools/quest-config.log
//   Unity -batchmode -nographics -projectPath quest_loft \
//     -executeMethod LoftQuestBuild.BuildApk -quit -logFile Tools/build-apk.log
// Mac .app path (LoftBuilder.BuildMac) is untouched and keeps working.
//
// NOTE: XR Management 4.5.4 keeps its per-target singleton internal, and
// OpenXR 1.13's package settings class is internal, so this script reaches
// both via reflection. Only public, version-pinned behavior is used.
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

public static class LoftQuestBuild
{
    const string SettingsDir = "Assets/XR/Settings";

    // Joint tracking for bare hands, on the Mac player (Simulator) and on Quest.
    public static void EnableHands()
    {
        foreach (var g in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })
        {
            FeatureHelpers.RefreshFeatures(g);
            var s = OpenXRSettings.GetSettingsForBuildTargetGroup(g);
            var hands = s.GetFeature<UnityEngine.XR.Hands.OpenXR.HandTracking>();
            if (hands == null) { Debug.LogError($"[Quest] Hand tracking feature not found for {g}."); continue; }
            hands.enabled = true;
            EditorUtility.SetDirty(s);
            Debug.Log($"[Quest] Hand tracking ENABLED for {g}.");
        }
        AssetDatabase.SaveAssets();
    }

    public static void ConfigureQuest()
    {
        EnsureFolder("Assets/XR");
        EnsureFolder(SettingsDir);

        // 1) XR Plug-in Management singleton (internal GetOrCreate) + Android
        //    manager/settings, OpenXRLoader registered.
        var perTargetT = typeof(XRGeneralSettingsPerBuildTarget);
        var getOrCreate = perTargetT.GetMethod("GetOrCreate", BindingFlags.NonPublic | BindingFlags.Static);
        var perTarget = (XRGeneralSettingsPerBuildTarget)getOrCreate.Invoke(null, null);
        perTargetT.GetMethod("CreateDefaultManagerSettingsForBuildTarget")
            .Invoke(perTarget, new object[] { BuildTargetGroup.Android });
        var mgr = (XRManagerSettings)perTargetT.GetMethod("ManagerSettingsForBuildTarget")
            .Invoke(perTarget, new object[] { BuildTargetGroup.Android });
        bool hasLoader = mgr.loaders != null && mgr.loaders.Any(l => l is OpenXRLoader);
        if (!hasLoader)
        {
            var loader = ScriptableObject.CreateInstance<OpenXRLoader>();
            AssetDatabase.CreateAsset(loader, SettingsDir + "/OpenXRLoader.asset");
            AssetDatabase.SaveAssets();
            mgr.TryAddLoader(loader);
            Debug.Log("[Quest] OpenXRLoader created + registered for Android.");
        }
        else Debug.Log("[Quest] OpenXRLoader already registered for Android.");
        perTargetT.GetMethod("CreateDefaultSettingsForBuildTarget")
            .Invoke(perTarget, new object[] { BuildTargetGroup.Android });
        var general = (XRGeneralSettings)perTargetT.GetMethod("SettingsForBuildTarget")
            .Invoke(perTarget, new object[] { BuildTargetGroup.Android });
        if (general.AssignedSettings != mgr) general.AssignedSettings = mgr;
        general.InitManagerOnStart = true;
        EditorUtility.SetDirty(perTarget);
        EditorUtility.SetDirty(mgr);
        EditorUtility.SetDirty(general);
        Debug.Log($"[Quest] Android XR settings ready (manager={(mgr != null ? "ok" : "NULL")}, initOnStart={general.InitManagerOnStart}).");

        // 2) OpenXR package settings (internal class) + feature discovery,
        //    Meta Quest support ON. FeatureHelpers itself is public.
        var pkgT = System.Type.GetType("UnityEditor.XR.OpenXR.OpenXRPackageSettings, Unity.XR.OpenXR.Editor");
        var pkg = pkgT.GetMethod("GetOrCreateInstance", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
        var oxSettings = (OpenXRSettings)pkgT.GetMethod("GetSettingsForBuildTargetGroup").Invoke(pkg, new object[] { BuildTargetGroup.Android });
        Debug.Log($"[Quest] OpenXR settings for Android: {(oxSettings != null ? "present" : "MISSING")}");
        if (oxSettings != null)
        {
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            var feats = oxSettings.GetFeatures();
            Debug.Log($"[Quest] OpenXR features discovered: {feats.Length}");
            var mq = oxSettings.GetFeature<MetaQuestFeature>();
            if (mq == null) Debug.LogWarning("[Quest] MetaQuestFeature not discovered — loader-only build.");
            else { mq.enabled = true; Debug.Log("[Quest] MetaQuestFeature ENABLED."); }
            var hands = oxSettings.GetFeature<UnityEngine.XR.OpenXR.Features.Interactions.HandInteractionProfile>();
            if (hands == null) Debug.LogWarning("[Quest] Hand Interaction profile not discovered.");
            else { hands.enabled = true; Debug.Log("[Quest] Hand Interaction profile ENABLED."); }
            var touch = oxSettings.GetFeature<UnityEngine.XR.OpenXR.Features.Interactions.MetaQuestTouchPlusControllerProfile>();
            if (touch == null) Debug.LogWarning("[Quest] Touch Plus profile not discovered.");
            else { touch.enabled = true; Debug.Log("[Quest] Touch Plus interaction profile ENABLED."); }
            EditorUtility.SetDirty(oxSettings);
        }

        // 3) Android player settings for Quest 3/3S.
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.defaultcompany.focusloft");
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        // Quest requires Linear color space. Graphics: Vulkan first (what
        // Quest 3/3S uses), GLES3 fallback so the same APK also boots on
        // non-Vulkan test devices (emulator); OpenXR validation passes with
        // Linear on either API.
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
            new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
        // Agents live on the backend over Wi-Fi: always request INTERNET.
        PlayerSettings.Android.forceInternetPermission = true;
        // Voice: RECORD_AUDIO + cleartext http to a LAN backend (see LoftAndroidSetup).
        LoftAndroidSetup.ApplySettings();
        // Input: project stays on old Input Manager (LoftDesk uses it); the
        // Input System *package* is present only as an OpenXR dependency.
        AssetDatabase.SaveAssets();

        Debug.Log("[Quest] ConfigureQuest done: loader + MetaQuest + Android (minSdk32/IL2CPP/ARM64).");
        EditorApplication.Exit(0);
    }

    // Simulator wiring (Mac Editor + Mac test player): registers the SAME
    // OpenXRLoader for Standalone so Unity Play Mode / Mac players talk to
    // Meta XR Simulator (the active OpenXR runtime). Android/Quest config is
    // untouched. If no runtime is present at launch, Unity falls back to 2D.
    public static void ConfigureSimulator()
    {
        EnsureFolder("Assets/XR");
        EnsureFolder(SettingsDir);
        var perTargetT = typeof(XRGeneralSettingsPerBuildTarget);
        var getOrCreate = perTargetT.GetMethod("GetOrCreate", BindingFlags.NonPublic | BindingFlags.Static);
        var perTarget = (XRGeneralSettingsPerBuildTarget)getOrCreate.Invoke(null, null);
        perTargetT.GetMethod("CreateDefaultManagerSettingsForBuildTarget")
            .Invoke(perTarget, new object[] { BuildTargetGroup.Standalone });
        var mgr = (XRManagerSettings)perTargetT.GetMethod("ManagerSettingsForBuildTarget")
            .Invoke(perTarget, new object[] { BuildTargetGroup.Standalone });
        bool hasLoader = mgr.loaders != null && mgr.loaders.Any(l => l is OpenXRLoader);
        if (!hasLoader)
        {
            var loader = ScriptableObject.CreateInstance<OpenXRLoader>();
            AssetDatabase.CreateAsset(loader, SettingsDir + "/OpenXRLoader-Standalone.asset");
            AssetDatabase.SaveAssets();
            mgr.TryAddLoader(loader);
            Debug.Log("[Sim] OpenXRLoader created + registered for Standalone.");
        }
        else Debug.Log("[Sim] OpenXRLoader already registered for Standalone.");
        perTargetT.GetMethod("CreateDefaultSettingsForBuildTarget")
            .Invoke(perTarget, new object[] { BuildTargetGroup.Standalone });
        var general = (XRGeneralSettings)perTargetT.GetMethod("SettingsForBuildTarget")
            .Invoke(perTarget, new object[] { BuildTargetGroup.Standalone });
        if (general.AssignedSettings != mgr) general.AssignedSettings = mgr;
        general.InitManagerOnStart = true;
        EditorUtility.SetDirty(perTarget);
        EditorUtility.SetDirty(mgr);
        EditorUtility.SetDirty(general);

        // Interaction profiles for the Simulator (desktop runtime): Oculus
        // Touch controller. Quest Touch Plus stays Android-only.
        var pkgT = System.Type.GetType("UnityEditor.XR.OpenXR.OpenXRPackageSettings, Unity.XR.OpenXR.Editor");
        var pkg = pkgT.GetMethod("GetOrCreateInstance", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
        var oxSettings = (OpenXRSettings)pkgT.GetMethod("GetSettingsForBuildTargetGroup").Invoke(pkg, new object[] { BuildTargetGroup.Standalone });
        if (oxSettings != null)
        {
            UnityEditor.XR.OpenXR.Features.FeatureHelpers.RefreshFeatures(BuildTargetGroup.Standalone);
            var touch = oxSettings.GetFeature<UnityEngine.XR.OpenXR.Features.Interactions.OculusTouchControllerProfile>();
            if (touch == null) Debug.LogWarning("[Sim] OculusTouchControllerProfile not discovered.");
            else { touch.enabled = true; Debug.Log("[Sim] Oculus Touch controller profile ENABLED for Standalone."); }
            EditorUtility.SetDirty(oxSettings);
        }
        else Debug.LogWarning("[Sim] OpenXR settings for Standalone MISSING.");
        AssetDatabase.SaveAssets();
        Debug.Log("[Sim] ConfigureSimulator done: Standalone OpenXR ready for Meta XR Simulator.");
        EditorApplication.Exit(0);
    }

    public static void BuildApk()
    {
        EditorUserBuildSettings.SwitchActiveBuildTarget(NamedBuildTarget.Android, BuildTarget.Android);
        AssetDatabase.SaveAssets();
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { RebuiltScene() },
            locationPathName = "Builds/FocusLoft.apk",
            target = BuildTarget.Android,
            options = BuildOptions.None,
        };
        var report = BuildPipeline.BuildPlayer(opts);
        Debug.Log($"[BUILD] FocusLoft.apk: {report.summary.result} in {report.summary.totalTime.TotalSeconds:F0}s");
        EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    public static void BuildApk2D()
    {
        // 2D-touch sibling for emulator/phones: same scene + same LoftDesk
        // API, but XR does NOT auto-init so it renders to the phone screen
        // and touch works. Quest VR build (BuildApk) is untouched.
        var perTargetT = typeof(XRGeneralSettingsPerBuildTarget);
        var getOrCreate = perTargetT.GetMethod("GetOrCreate", BindingFlags.NonPublic | BindingFlags.Static);
        var perTarget = (XRGeneralSettingsPerBuildTarget)getOrCreate.Invoke(null, null);
        var general = (XRGeneralSettings)perTargetT.GetMethod("SettingsForBuildTarget")
            .Invoke(perTarget, new object[] { BuildTargetGroup.Android });
        bool prevInit = general.InitManagerOnStart;
        general.InitManagerOnStart = false;
        EditorUtility.SetDirty(general);
        AssetDatabase.SaveAssets();
        int code = 1;
        try
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(NamedBuildTarget.Android, BuildTarget.Android);
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { RebuiltScene() },
                locationPathName = "Builds/FocusLoft-2D.apk",
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[BUILD] FocusLoft-2D.apk: {report.summary.result} in {report.summary.totalTime.TotalSeconds:F0}s");
            code = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1;
        }
        finally
        {
            // Exit() kills the process, so restore XR init first or every later VR build ships with XR off.
            general.InitManagerOnStart = prevInit;
            EditorUtility.SetDirty(general);
            AssetDatabase.SaveAssets();
        }
        EditorApplication.Exit(code);
    }

    // Builds ship the scene the code generates, never a stale committed one.
    public static string RebuiltScene()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Loft.unity");
        LoftBootstrap.Build();
        return "Assets/Scenes/Loft.unity";
    }

    static void EnsureFolder(string path)
    {
        var parts = path.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }
}
#endif
