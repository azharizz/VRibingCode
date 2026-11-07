#if UNITY_EDITOR
// VIRTUAL LOFT — Android permissions + LAN networking for voice.
//   * RECORD_AUDIO (+ optional microphone feature) in the generated manifest
//   * android:usesCleartextTraffic="true": the backend is plain http:// on the
//     Mac's LAN address, which Android 9+ blocks by default
//   * Player settings: allow http downloads, INTERNET, a microphone description
// Runs on every Gradle project Unity generates (APK and AAB builds), so no
// hand-edited manifest has to be kept in sync. Settings are applied once on
// editor load and are idempotent.
using System.IO;
using System.Linq;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEngine;

[InitializeOnLoad]
public static class LoftAndroidSetup
{
    const string AndroidNs = "http://schemas.android.com/apk/res/android";

    static LoftAndroidSetup() { EditorApplication.delayCall += ApplySettings; }

    public static void ApplySettings()
    {
        bool dirty = false;
        if (PlayerSettings.insecureHttpOption != InsecureHttpOption.AlwaysAllowed)
        { PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed; dirty = true; }
        if (!PlayerSettings.Android.forceInternetPermission) { PlayerSettings.Android.forceInternetPermission = true; dirty = true; }
        if (string.IsNullOrEmpty(PlayerSettings.macOS.microphoneUsageDescription))
        { PlayerSettings.macOS.microphoneUsageDescription = "Virtual Loft listens only while you hold the talk button."; dirty = true; }
        if (dirty) { AssetDatabase.SaveAssets(); Debug.Log("[LoftAndroidSetup] allowed http + microphone description set."); }
    }

    // Patches an AndroidManifest.xml in place. Public so a test can run it on a sample.
    public static bool PatchManifest(string manifestPath)
    {
        if (!File.Exists(manifestPath)) return false;
        var doc = new XmlDocument();
        doc.Load(manifestPath);
        var root = doc.DocumentElement;
        bool changed = false;

        bool Has(string tag, string name)
        {
            foreach (XmlNode n in root.ChildNodes)
                if (n.Name == tag && n.Attributes?["android:name"]?.Value == name) return true;
            return false;
        }
        void Add(string tag, string name, string requiredAttr = null)
        {
            if (Has(tag, name)) return;
            var el = doc.CreateElement(tag);
            el.SetAttribute("name", AndroidNs, name);
            if (requiredAttr != null) el.SetAttribute("required", AndroidNs, requiredAttr);
            root.InsertBefore(el, root.FirstChild);
            changed = true;
        }
        Add("uses-permission", "android.permission.RECORD_AUDIO");
        Add("uses-permission", "android.permission.INTERNET");
        Add("uses-feature", "android.hardware.microphone", "false");
        Add("uses-permission", "com.oculus.permission.HAND_TRACKING");
        Add("uses-feature", "oculus.software.handtracking", "false");

        var app = root["application"];
        if (app != null) foreach (var (key, value) in new[] { ("com.oculus.handtracking.frequency", "HIGH"), ("com.oculus.handtracking.version", "V2.0") })
        {
            if (app.ChildNodes.OfType<XmlElement>().Any(e => e.Name == "meta-data" && e.GetAttribute("name", AndroidNs) == key)) continue;
            var meta = doc.CreateElement("meta-data");
            meta.SetAttribute("name", AndroidNs, key);
            meta.SetAttribute("value", AndroidNs, value);
            app.AppendChild(meta);
            changed = true;
        }
        if (app != null && app.GetAttribute("usesCleartextTraffic", AndroidNs) != "true")
        {
            app.SetAttribute("usesCleartextTraffic", AndroidNs, "true");
            changed = true;
        }
        if (changed) doc.Save(manifestPath);
        return changed;
    }

    class Post : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 0;
        public void OnPostGenerateGradleAndroidProject(string path)
        {
            // `path` = unityLibrary; the app manifest is in the sibling launcher module.
            foreach (var m in new[] { Path.Combine(path, "src/main/AndroidManifest.xml"),
                                      Path.Combine(path, "../launcher/src/main/AndroidManifest.xml") })
                if (PatchManifest(m)) Debug.Log($"[LoftAndroidSetup] patched {m}");
        }
    }
}
#endif
