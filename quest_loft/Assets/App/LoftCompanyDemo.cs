// VIRTUAL LOFT — LoftCompanyDemo.cs (player-only company shift)
// Runs ONLY inside real player builds (#if !UNITY_EDITOR): drives the
// in-world Quest panel exactly like a company team would — assigns all
// three agents, watches live progress, teleports between pads, completes
// work, screenshots every stage, writes company-result.txt. In the Unity
// editor it does nothing (Play tests stay hermetic).
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VirtualLoft.XR;

public class LoftCompanyDemo : MonoBehaviour
{
    readonly List<string> lines = new List<string>();
    int pass, fail;
    void Check(bool ok, string step)
    {
        if (ok) pass++; else fail++;
        lines.Add($"{(ok ? "PASS" : "FAIL")}: {step}");
        Debug.Log($"[COMPANY] {(ok ? "PASS" : "FAIL")}: {step}");
    }

    IEnumerator Start()
    {
#if !UNITY_EDITOR
        yield return new WaitForSeconds(3f);
        var desk = FindFirstObjectByType<LoftDesk>();
        var panel = FindFirstObjectByType<LoftQuestPanel>();
        var agents = FindFirstObjectByType<AgentManager>();
        var boards = FindFirstObjectByType<BoardManager>();
        Check(desk != null && panel != null && agents != null && boards != null, "company shift: all systems up");
        if (desk == null || panel == null || agents == null || boards == null) { WriteReport(); yield break; }
        agents.WakeAll();

        // 1) Morning burst: CEO taps all 3 agent buttons on the 3D panel.
        panel.PressAgent(0);
        panel.PressAgent(1);
        panel.PressAgent(2);
        yield return new WaitForSeconds(1f);
        var kan = boards.ListCards("kanban");
        Check(kan.FindAll(c => c.column == "Doing" && c.text.Contains("[scout]")).Count == 1, "scout card Doing [scout]");
        Check(kan.FindAll(c => c.column == "Doing" && c.text.Contains("[builder]")).Count == 1, "builder card Doing [builder]");
        Check(kan.FindAll(c => c.column == "Doing" && c.text.Contains("[keeper]")).Count >= 1, "keeper card Doing [keeper]");
        Check(agents.agents.TrueForAll(a => a != null && a.CurrentState == AgentState.Working), "all 3 agents Working");
        yield return new WaitForEndOfFrame();
        Shoot("company-1-assign");

        // 2) Mid-shift: live progress % on labels + panel.
        yield return new WaitForSeconds(5f);
        bool prog = desk.tasks.Exists(t => !t.done && t.progress > 0.05f);
        Check(prog, $"live progress advancing ({desk.tasks.Count} active tasks)");
        Shoot("company-2-progress");

        // 3) Hop to pad 0 like a Quest user teleporting across the loft.
        panel.TeleportTo(0);
        yield return new WaitForSeconds(1f);
        var cam = Camera.main ?? FindFirstObjectByType<Camera>();
        bool teleOk = cam != null && Vector3.Distance(cam.transform.position, new Vector3(-3f, Comfort.SEATED_EYE_HEIGHT, 4.6f)) < 0.1f;
        Check(teleOk, "teleport to pad 0, eye height pinned");
        Shoot("company-3-teleport");

        // 4) Keeper reports done early via DONE button; others auto-finish.
        panel.PressDone(); // last assigned agent = keeper
        yield return new WaitForSeconds(1f);
        Check(desk.tasks.Exists(t => t.agentId == "keeper" && t.done), "keeper DONE via 3D button");
        yield return new WaitForSeconds(9f); // let scout+builder auto-finish (12s work)
        Check(desk.tasks.TrueForAll(t => t.done), $"all tasks done ({desk.tasks.Count}/3)");
        Check(boards.ListCards("kanban").FindAll(c => c.column == "Done").Count >= 3, "kanban Done >= 3 with results");
        Check(boards.ListCards("log").Exists(c => c.text.Contains("AUTO-DONE") || c.text.Contains("DONE")), "log board shows results");
        Shoot("company-4-done");
        WriteReport();
#else
        yield break;
#endif
    }

    void Shoot(string name)
    {
#if !UNITY_EDITOR
        StartCoroutine(ShootCo(name));
#endif
    }

    IEnumerator ShootCo(string name)
    {
#if !UNITY_EDITOR
        yield return new WaitForEndOfFrame();
        try
        {
            var cam = Camera.main ?? FindFirstObjectByType<Camera>();
            int w = 960, h = 600;
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            var px = tex.GetPixels32();
            var sb = new System.Text.StringBuilder(w * h * 8 + 64);
            sb.Append($"P3\n{w} {h}\n255\n");
            for (int y = h - 1; y >= 0; y--)
                for (int x = 0; x < w; x++)
                {
                    var c = px[y * w + x];
                    sb.Append(c.r).Append(' ').Append(c.g).Append(' ').Append(c.b).Append(' ');
                }
            var path = System.IO.Path.Combine(Application.persistentDataPath, name + ".ppm");
            System.IO.File.WriteAllText(path, sb.ToString());
            Debug.Log($"[COMPANY] shot: {path}");
            Destroy(rt);
            Destroy(tex);
        }
        catch (System.Exception e) { Debug.LogWarning($"[COMPANY] shot skipped: {e.Message}"); }
#else
        yield break;
#endif
    }

    void WriteReport()
    {
#if !UNITY_EDITOR
        try
        {
            var body = $"date={System.DateTime.Now:O}\nsummary=[COMPANY] SUMMARY: {pass} pass, {fail} fail — {(fail == 0 ? "COMPANY SHIFT GREEN" : "COMPANY SHIFT RED")}\n" + string.Join("\n", lines) + "\n";
            System.IO.File.WriteAllText(System.IO.Path.Combine(Application.persistentDataPath, "company-result.txt"), body);
            Debug.Log($"[COMPANY] report written ({pass} pass, {fail} fail)");
        }
        catch (System.Exception e) { Debug.LogWarning($"[COMPANY] report skipped: {e.Message}"); }
#endif
    }
}
