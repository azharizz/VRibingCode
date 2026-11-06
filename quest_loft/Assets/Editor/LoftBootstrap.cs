#if UNITY_EDITOR
// Menu: Loft/Build Loft Scene — (re)creates Assets/Scenes/Loft.unity.
// The room itself is built by LoftWorld (also at runtime), agents come from
// the backend at runtime, so this only wires the components together.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VirtualLoft.XR;

public static class LoftBootstrap
{
    [MenuItem("Loft/Build Loft Scene")]
    public static void Build()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects);

        // Idempotent rebuild: drop everything this tool (or older versions) made.
        foreach (var name in new[] { "Loft", "Scout", "Builder", "Keeper", "MainWall", "Floor", "QuestPanel", "ConfirmCard", "WristMenu", "ThemeProps",
            "Board_notes", "Board_kanban", "Board_calendar", "Board_log", "TelePad_0", "TelePad_1", "TelePad_2", "PartyMode", "PreviewScreen" })
        {
            GameObject old;
            while ((old = GameObject.Find(name)) != null) Object.DestroyImmediate(old);
        }

        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t != null && t.name == "PartyMode" && t.parent == null) Object.DestroyImmediate(t.gameObject); // hidden between parties

        var loft = new GameObject("Loft");
        var boards = loft.AddComponent<BoardManager>();
        loft.AddComponent<LoftClient>().boards = boards;
        loft.AddComponent<AgentManager>();
        loft.AddComponent<Multiplayer>();
        loft.AddComponent<Comfort>();
        var world = loft.AddComponent<LoftWorld>();
        loft.AddComponent<LoftParty>();
        loft.AddComponent<LoftPreviewScreen>();
        loft.AddComponent<LoftDesk>();
        loft.AddComponent<LoftInteraction>();

        // Keep Standard's _EMISSION variant in player builds (party floor, ball, theme glow):
        // a material that uses it, inside Resources, is always shipped.
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/LoftGlowKeep.mat") == null)
        {
            var glow = new Material(Shader.Find("Standard"));
            glow.EnableKeyword("_EMISSION");
            glow.SetColor("_EmissionColor", Color.white);
            glow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            AssetDatabase.CreateAsset(glow, "Assets/Resources/LoftGlowKeep.mat");
        }

        // Same for the voice card + wrist menu (Unlit/Color).
        if (AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/LoftUiKeep.mat") == null)
            AssetDatabase.CreateAsset(new Material(Shader.Find("Unlit/Color")), "Assets/Resources/LoftUiKeep.mat");

        // Same for the preview screen's screenshots (Unlit/Texture).
        if (AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/LoftUnlitKeep.mat") == null)
            AssetDatabase.CreateAsset(new Material(Shader.Find("Unlit/Texture")), "Assets/Resources/LoftUnlitKeep.mat");

        world.Build();               // floor, wall, 4 boards, CameraRig
        world.ApplyTheme(world.Resolve(world.defaultTheme));
        loft.GetComponent<LoftInteraction>().EnsureBuilt();
        loft.GetComponent<LoftPreviewScreen>().EnsureBuilt();

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Loft.unity");
        Debug.Log("[LoftBootstrap] Saved Assets/Scenes/Loft.unity — start backend/ (npm run serve), then press Play.");
    }
}
#endif
