#if UNITY_EDITOR
// Dev tool (not part of the game): render the seat view of every scene to PNGs
// and print budget numbers. Needs a real GPU context (no -nographics):
//   Unity -batchmode -projectPath <copy> -executeMethod WorldShot.Run -quit
// env: LOFT_SHOT_DIR, LOFT_SHOT_THEMES (a,b), LOFT_SHOT_POSES (seat,left,right,back), LOFT_SHOT_TAG
using System;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WorldShot
{
    public static void ImportTmp()
    {
        TMP_PackageResourceImporter.ImportResources(true, false, false);
        double t0 = EditorApplication.timeSinceStartup;
        EditorApplication.update += () =>
        {
            if (File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset") || EditorApplication.timeSinceStartup - t0 > 90)
            {
                AssetDatabase.SaveAssets();
                Debug.Log("[WorldShot] TMP essentials imported: " + File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset"));
                EditorApplication.Exit(0);
            }
        };
    }

    // Default TMP font -> dynamic atlas with the TTF embedded (works in player builds, any glyph the UI uses).
    public static void FixTmpFont()
    {
        const string path = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        var ttf = AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Fonts/LiberationSans.ttf");
        fa.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        var so = new SerializedObject(fa);
        var src = so.FindProperty("m_SourceFontFile"); if (src != null) src.objectReferenceValue = ttf;
        var clear = so.FindProperty("m_ClearDynamicDataOnBuild");
        if (clear != null) clear.boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(fa);
        AssetDatabase.SaveAssets();
        var miss = new StringBuilder();
        foreach (var ch in "\u25CF\u25CB\u25B6\u25B8\u00B7\u2014\u2026\u2192\u2713\u00D7\u2022\u00E9")
            if (!fa.HasCharacter(ch, true, true)) miss.Append(ch).Append(' ');
        Debug.Log("[WorldShot] font dynamic=" + fa.atlasPopulationMode + " src=" + fa.sourceFontFile + " missing glyphs: " + miss);
        EditorApplication.Exit(0);
    }

    static string Env(string k, string d) { var v = Environment.GetEnvironmentVariable(k); return string.IsNullOrEmpty(v) ? d : v; }

    public static void Run()
    {
        try { RunInner(); }
        catch (Exception e) { Debug.LogError("[WorldShot] " + e); }
        EditorApplication.Exit(0);
    }

    static void RunInner()
    {
        string dir = Env("LOFT_SHOT_DIR", Path.Combine(Directory.GetCurrentDirectory(), "shots"));
        Directory.CreateDirectory(dir);
        string tag = Env("LOFT_SHOT_TAG", "");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        var loft = new GameObject("Loft");
        var world = loft.AddComponent<LoftWorld>();
        var agents = loft.AddComponent<AgentManager>();
        world.Build();
        var ids = Env("LOFT_SHOT_THEMES", string.Join(",", LoftWorld.ThemeIds())).Split(',');
        var poses = Env("LOFT_SHOT_POSES", "seat").Split(',');
        var sb = new StringBuilder();
        int W = int.Parse(Env("LOFT_SHOT_W", "1600")), Hh = int.Parse(Env("LOFT_SHOT_H", "1000"));
        var rt = new RenderTexture(W, Hh, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.fieldOfView = float.Parse(Env("LOFT_SHOT_FOV", "88"));
        foreach (var id in ids)
        {
            if (!world.ApplyTheme(id)) { sb.AppendLine("FAILED theme " + id); continue; }
            agents.Clear();
            Spawn(agents, "a1", "Scout", "owl", "Researcher", AgentState.Idle, "");
            Spawn(agents, "a2", "Builder", "fox", "Engineer", AgentState.Working, "Edit src/login.ts");
            Spawn(agents, "a3", "Keeper", "bear", "QA", AgentState.Done, "");
            foreach (var ag in agents.agents) ag.transform.position = ag.GetComponent<AgentSkin>().home;
            Fill();
            foreach (var pose in poses)
            {
                float yaw = pose == "left" ? 55f : pose == "right" ? -55f : pose == "back" ? 180f : pose == "up" ? 0f : 0f;
                float pitch = pose == "up" ? -35f : pose == "down" ? 35f : 0f;
                world.rig.rotation = Quaternion.Euler(0, 180f + yaw, 0);
                cam.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
                Canvas.ForceUpdateCanvases();
                foreach (var tm in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None)) tm.ForceMeshUpdate();
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(W, Hh, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, Hh), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, $"{id}{(pose == "seat" ? "" : "_" + pose)}{tag}.png"), tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
            }
            sb.AppendLine(Stats(id, world));
        }
        MeasureText(sb, world, cam);
        // leak check: 3 full rounds over every scene must not grow materials/meshes/objects
        world.ApplyTheme(ids[0]);
        int Count<T>() where T : UnityEngine.Object => Resources.FindObjectsOfTypeAll<T>().Length;
        int m0 = Count<Material>(), me0 = Count<Mesh>(), g0 = Count<GameObject>(), t0 = Count<Texture>();
        for (int round = 0; round < 3; round++) foreach (var id in ids) world.ApplyTheme(id);
        world.ApplyTheme(ids[0]);
        sb.AppendLine($"leak check (3 rounds x {ids.Length} scenes): materials {m0}->{Count<Material>()}, meshes {me0}->{Count<Mesh>()}, gameobjects {g0}->{Count<GameObject>()}, textures {t0}->{Count<Texture>()}");
        File.WriteAllText(Path.Combine(dir, "stats.txt"), sb.ToString());
        Debug.Log("[WorldShot]\n" + sb);
    }

    static void Spawn(AgentManager am, string id, string name, string skin, string role, AgentState st, string line)
    {
        var a = am.Upsert(id, name, role, skin, st);
        a.GetComponent<AgentSkin>().SetLine(line);
    }

    static void Fill()
    {
        Set("kanban", "ToDo(2): add login | fix nav\nDoing(1): refactor api layer\nReview(1): landing page copy\nDone(5): ci setup | seed db");
        Set("notes", "Builder — REVIEW · web-app\n3 files changed, +120 -14\nLogin page added with email and\npassword validation. Tests pass 3/3.\nReady for your approval.");
        Set("calendar", "3/20 agents\nidle 1  working 1  done 1\n12k tokens · $0.031\n● live");
        Set("log", "09:14 Builder: add login page\n09:15 Scout: found 4 sources\n09:17 Keeper: PASS 3/3\n09:18 CEO GOAL: ship v1\n09:20 Builder: Edit src/login.ts");
    }
    static void Set(string id, string txt)
    {
        var c = GameObject.Find($"Board_{id}")?.transform.Find("Content")?.GetComponent<TMP_Text>();
        if (c != null) c.text = txt;
    }

    static string Stats(string id, LoftWorld w)
    {
        long tris = 0; int draws = 0; int renderers = 0;
        var all = new System.Collections.Generic.List<Renderer>(UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None));
        all.AddRange(GameObject.Find("ThemeProps").GetComponentsInChildren<Renderer>());
        foreach (var bd in LoftWorld.Boards) all.AddRange(GameObject.Find("Board_" + bd.id).GetComponentsInChildren<Renderer>());
        all.AddRange(Camera.main.GetComponentsInChildren<Renderer>(true).Where(q => false));
        foreach (var r in all.Distinct())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            Mesh mesh = null;
            if (r is SkinnedMeshRenderer sk) mesh = sk.sharedMesh;
            else { var mf = r.GetComponent<MeshFilter>(); if (mf != null) mesh = mf.sharedMesh; }
            if (mesh == null) continue;
            renderers++;
            if (Environment.GetEnvironmentVariable("LOFT_SHOT_LIST") == "1") Debug.Log($"[R] {r.name} tris={mesh.triangles.Length / 3} bounds={r.bounds.center} {r.bounds.size} mat={r.sharedMaterial.name}");
            int subs = Mathf.Min(mesh.subMeshCount, Mathf.Max(1, r.sharedMaterials.Length));
            draws += subs;
            for (int s = 0; s < subs; s++) tris += mesh.GetIndexCount(s) / 3;
        }
        return $"{id}: draw calls (upper bound, no culling/batching) {draws}, renderers {renderers}, triangles {tris}, scene mesh triangles {w.SceneTriangles}";
    }

    static void MeasureText(StringBuilder sb, LoftWorld w, Camera cam)
    {
        var eye = cam.transform.position;
        foreach (var (id, _, pos, size) in LoftWorld.Boards)
        {
            var go = GameObject.Find($"Board_{id}");
            if (go == null) continue;
            var c = go.transform.Find("Content").GetComponent<TMP_Text>();
            var t = go.transform.Find("Title").GetComponent<TMP_Text>();
            float lh = c.GetPreferredValues("Hg").y, th = t.GetPreferredValues("Hg").y;
            float d = Vector3.Distance(eye, c.transform.position);
            float ang(float h) => 2f * Mathf.Atan(h / 2f / d) * Mathf.Rad2Deg;
            sb.AppendLine($"board {id}: dist {d:0.00} m, body line {lh:0.0000} m = {ang(lh):0.00} deg, title line {th:0.000} m = {ang(th):0.00} deg, fontSize {c.fontSize:0.00}, font {c.font?.name}, lines shown {c.textInfo.lineCount}");
        }
        foreach (var tm in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).Where(x => x.transform.parent != null && x.transform.parent.name.StartsWith("Agent_")).Take(2))
        {
            float lh = tm.GetPreferredValues("Hg").y, d = Vector3.Distance(eye, tm.transform.position);
            sb.AppendLine($"agent label {tm.name} ({tm.transform.parent.name}): dist {d:0.0} m, line {lh:0.000} m = {2f * Mathf.Atan(lh / 2f / d) * Mathf.Rad2Deg:0.00} deg");
        }
    }
}
#endif
