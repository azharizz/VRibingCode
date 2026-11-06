// VIRTUAL LOFT — StandupFlowTest.cs
// Play-Mode self-test, XR-Simulator compatible. No Test Framework dependency.
// Attach to any GameObject in the Focus Loft scene, press Play in XR Simulator
// (Quest 3/3S profile): it runs the full standup flow and logs PASS/FAIL.
// Gate: every step PASS, zero console errors, total < 5 min (runs in seconds).
//
// Flow: WakeAll -> AssignTask(builder, run) -> VoiceCommands route "run" ->

using System.Collections;
using UnityEngine;
using VirtualLoft.XR;

public class StandupFlowTest : MonoBehaviour
{
    [Header("Wire in scene (or auto-found at runtime)")]
    public AgentManager agents;
    public BoardManager boards;
    public VoiceCommands voice;
    public Multiplayer net;

    int pass, fail;
    readonly System.Collections.Generic.List<string> lines = new System.Collections.Generic.List<string>();

    void Check(bool ok, string step)
    {
        if (ok) { pass++; Debug.Log($"[STANDUP-TEST] PASS: {step}"); }
        else { fail++; Debug.LogError($"[STANDUP-TEST] FAIL: {step}"); }
        lines.Add($"{(ok ? "PASS" : "FAIL")}: {step}");
    }

    void AutoWire()
    {
        if (agents == null) agents = FindFirstObjectByType<AgentManager>();
        if (boards == null) boards = FindFirstObjectByType<BoardManager>();
        if (voice == null) voice = FindFirstObjectByType<VoiceCommands>();
        if (net == null) net = FindFirstObjectByType<Multiplayer>();
    }

    IEnumerator Start()
    {
        AutoWire();
        Check(agents != null, "AgentManager present");
        Check(boards != null, "BoardManager present");
        Check(voice != null, "VoiceCommands present");
        Check(net != null, "Multiplayer present");
        if (fail > 0) { Finish(); yield break; }

        // 1. Wake all agents -> all Idle.
        bool doneFired = false;
        agents.OnAgentDone += _ => doneFired = true;
        agents.WakeAll();
        yield return null;
        bool allIdle = true;
        foreach (var a in agents.agents) allIdle &= a != null && a.CurrentState == AgentState.Idle;
        Check(allIdle, "wake 3 agents -> all Idle");

        // 2. Voice "run landing test" routes to Run (PTT held), then assign Builder.
        voice.testMode = true;
        voice.pushToTalkHeld = true;
        bool runHeard = voice.HandleTranscription("jalankan tes");
        Check(runHeard && voice.lastCommand == VoiceCommandId.Run, "voice ID 'jalankan tes' -> Run");
        voice.SimulateCommand("run landing test");
        Check(voice.lastCommand == VoiceCommandId.Run, "voice EN 'run landing test' -> Run");
        agents.AssignTask("builder", "run landing test");
        yield return null;
        var builder = agents.agents.Find(a => a != null && a.agentId == "builder");
        Check(builder != null && builder.CurrentState == AgentState.Working, "Builder Working after assign");

        // 3. Log board records PASS 12/12.
        string logId = boards.AddCard("log", "PASS 12/12", "Done");
        Check(!string.IsNullOrEmpty(logId), "log board AddCard PASS 12/12");

        // 4. Kanban card moves to Done via voice path + button path.
        string cardId = boards.AddCard("kanban", "Morning standup", "Doing");
        Check(boards.VoiceMove("kanban", cardId, "move that to done"), "VoiceMove -> Done");
        var card = boards.ListCards("kanban").Find(c => c.id == cardId);
        Check(card != null && card.column == "Done", "card column == Done");
        voice.InvokeCommand(VoiceCommandId.Done);
        Check(voice.lastCommand == VoiceCommandId.Done, "button fallback InvokeCommand(Done)");

        // 5. Builder done -> OnAgentDone broadcast fires.
        builder.SetState(AgentState.Done);
        yield return null;
        Check(doneFired, "OnAgentDone broadcast on Done");

        // 6. Multiplayer rules: CEO allowed, member denied (V2 simulator logic).
        net.JoinRoom("focus-01", "CEO", LoftRole.CEO);
        bool ceoAssign = net.Assign("CEO", "agent:builder", "Member-1");
        bool memberAssign = net.Assign("Member-1", "agent:builder", "Member-1");
        Check(ceoAssign && !memberAssign, "CEO assign allowed, member assign denied");
        bool memberOwn = net.MoveCard("Member-1", cardId, "Member-1", "Doing", "Done");
        bool memberOther = net.MoveCard("Member-1", cardId, "CEO", "Doing", "Done");
        Check(memberOwn && !memberOther, "member moves own card, denied others'");
        Check(net.activityLog.Count >= 2, $"activity log has entries ({net.activityLog.Count})");

        // 7. Travel test: save -> load roundtrip keeps boards.
        string saved = boards.Save();
        boards.Load(saved);
        Check(boards.ListCards("kanban").Exists(c => c.id == cardId && c.column == "Done"),
            "save/load roundtrip keeps Done card");

        Finish();
    }

    void Finish()
    {
        var summary = $"[STANDUP-TEST] SUMMARY: {pass} pass, {fail} fail — {(fail == 0 ? "STANDUP FLOW GREEN" : "STANDUP FLOW RED")}";
        Debug.Log(summary);
        // File proof for computer-use runs: GUI Play writes results to disk
        // so the harness can collect them without watching the Console.
        // Player builds: persistentDataPath (CWD inside an .app is unreliable).
        try
        {
            var fps = (1f / Mathf.Max(Time.unscaledDeltaTime, 1e-5f)).ToString("F0");
            var body = $"date={System.DateTime.Now:O}\nsummary={summary}\nplayFps~{fps}\n" + string.Join("\n", lines) + "\n";
#if UNITY_EDITOR
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Tools/play-result.txt"), body);
#else
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, "play-result.txt"), body);
            StartCoroutine(ShootGameView());
            Debug.Log($"[STANDUP-TEST] wrote play-result.txt to {UnityEngine.Application.persistentDataPath}");
#endif
        }
        catch (System.Exception e) { Debug.LogWarning($"[STANDUP-TEST] result file skipped: {e.Message}"); }
    }

    // Player-only: capture the GAME camera to a PPM file (pure C# + core
    // engine only — no screencapture/ImageConversion module needed in
    // stripped builds). Proves pixels, not just logs.
    System.Collections.IEnumerator ShootGameView()
    {
#if !UNITY_EDITOR
        yield return new WaitForEndOfFrame();
        try
        {
            var cam = Camera.main ?? FindFirstObjectByType<Camera>();
            Debug.Log($"[STANDUP-TEST] shoot: cam={(cam ? cam.name : "NULL")} pos={cam?.transform.position} rot={cam?.transform.rotation.eulerAngles} bg={cam?.backgroundColor} clear={cam?.clearFlags} fog={RenderSettings.fog} skybox={(RenderSettings.skybox ? RenderSettings.skybox.name : "none")}");
            var renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            Debug.Log($"[STANDUP-TEST] shoot: {renderers.Length} renderers, first: {(renderers.Length > 0 ? $"{renderers[0].name}@{renderers[0].transform.position}" : "NONE")}");
            foreach (var n in new[] { "Scout", "Builder", "Keeper", "Board_kanban", "MainWall", "Floor" })
            {
                var go = GameObject.Find(n);
                Debug.Log($"[STANDUP-TEST] shoot: {n}={(go ? go.transform.position.ToString() : "MISSING")}");
            }
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
            var path = System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, "gameview.ppm");
            System.IO.File.WriteAllText(path, sb.ToString());
            Debug.Log($"[STANDUP-TEST] game pixels saved: {path} ({w}x{h}, {px.Length} px)");
            Destroy(rt);
            Destroy(tex);
        }
        catch (System.Exception e) { Debug.LogWarning($"[STANDUP-TEST] gameview skipped: {e.Message}"); }
#else
        yield break;
#endif
    }
}
