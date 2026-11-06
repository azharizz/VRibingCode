#if UNITY_EDITOR
// Auto-import for generated animals (animalgen/build.py -> Assets/Models/Animals/<id>.fbx).
// On every (re)import of such an FBX:
//   * model: Generic rig, Bake Axis Conversion (Blender Z-up -> upright, facing +Z),
//     file scale, no imported materials
//   * clips: Idle/Working/Done loop, Failed holds its last pose
//   * builds Assets/Models/Animals/<id>.controller: int "State" -> Idle 0, Working 1,
//     Done 2, Failed 3, Party 4 (Any State transitions, 0.15 s blend)
//   * saves Assets/Resources/Skins/<id>.prefab (vertex-color material), which
//     AgentSkin loads by the skin id the backend sends. No manual steps.
// Menu: Loft/Rebuild Animal Skins (re-runs it for every FBX).

using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public class LoftAnimalImport : AssetPostprocessor
{
    public const string Dir = "Assets/Models/Animals";
    public const string SkinDir = "Assets/Resources/Skins";
    public static readonly string[] States = { "Idle", "Working", "Done", "Failed", "Party" }; // = Animator "State" ints

    bool IsAnimal => assetPath.StartsWith(Dir + "/") && assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);

    void OnPreprocessModel()
    {
        if (!IsAnimal) return;
        var mi = (ModelImporter)assetImporter;
        mi.animationType = ModelImporterAnimationType.Generic;
        mi.bakeAxisConversion = true;
        mi.useFileScale = true;
        mi.globalScale = 1f;
        mi.importCameras = false;
        mi.importLights = false;
        mi.materialImportMode = ModelImporterMaterialImportMode.None;
        mi.importAnimation = true;
    }

    void OnPreprocessAnimation()
    {
        if (!IsAnimal) return;
        var mi = (ModelImporter)assetImporter;
        var clips = mi.defaultClipAnimations;
        foreach (var c in clips)
        {
            c.name = ShortName(c.name);
            c.loopTime = c.name != "Failed";
            c.loopPose = false; // loops are authored seamless (animalgen/test.py checks it)
        }
        mi.clipAnimations = clips;
    }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        var fbx = imported.Where(p => p.StartsWith(Dir + "/") && p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)).ToArray();
        if (fbx.Length > 0) EditorApplication.delayCall += () => { foreach (var p in fbx) BuildSkin(p); };
    }

    [MenuItem("Loft/Rebuild Animal Skins")]
    public static void RebuildAll()
    {
        foreach (var p in Directory.GetFiles(Dir, "*.fbx")) BuildSkin(p.Replace('\\', '/'));
    }

    // "Armature|Done" / "Done" -> "Done"
    static string ShortName(string n) => n.Contains("|") ? n.Substring(n.LastIndexOf('|') + 1) : n;

    public static void BuildSkin(string fbxPath)
    {
        var id = Path.GetFileNameWithoutExtension(fbxPath);
        var clips = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__"))
            .GroupBy(c => ShortName(c.name)).ToDictionary(g => g.Key, g => g.First());
        var missing = States.Where(s => !clips.ContainsKey(s)).ToArray();
        if (missing.Length > 0) Debug.LogWarning($"[Animals] {id}: missing clips {string.Join(",", missing)}");

        var ctrlPath = $"{Dir}/{id}.controller";
        AssetDatabase.DeleteAsset(ctrlPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
        ctrl.AddParameter("State", AnimatorControllerParameterType.Int);
        var sm = ctrl.layers[0].stateMachine;
        for (int i = 0; i < States.Length; i++)
        {
            var st = sm.AddState(States[i]);
            st.motion = clips.TryGetValue(States[i], out var clip) ? clip : null;
            if (i == 0) sm.defaultState = st;
            var t = sm.AddAnyStateTransition(st);
            t.AddCondition(AnimatorConditionMode.Equals, i, "State");
            t.hasExitTime = false;
            t.duration = 0.15f;
            t.canTransitionToSelf = false;
        }

        var matPath = $"{Dir}/LoftAnimal.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            var shader = Shader.Find("Loft/VertexColorLit");
            if (shader == null) { Debug.LogWarning("[Animals] shader Loft/VertexColorLit not imported yet — run Loft/Rebuild Animal Skins"); return; }
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, matPath);
        }

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        var anim = go.GetComponent<Animator>();
        if (anim == null) anim = go.AddComponent<Animator>();
        anim.runtimeAnimatorController = ctrl;
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
            r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(SkinDir)) AssetDatabase.CreateFolder("Assets/Resources", "Skins");
        PrefabUtility.SaveAsPrefabAsset(go, $"{SkinDir}/{id}.prefab");
        Object.DestroyImmediate(go);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Animals] {id}: prefab + Animator ready ({clips.Count} clips)");
    }
}
#endif
