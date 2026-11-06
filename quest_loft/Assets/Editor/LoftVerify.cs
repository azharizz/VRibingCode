#if UNITY_EDITOR
// Headless verifier: mirrors StandupFlowTest's checks synchronously in
// batchmode (no Play needed). Run:
//   Unity -batchmode -nographics -projectPath quest_loft -openScene Assets/Scenes/Loft.unity -executeMethod LoftVerify.RunAll -quit -logFile Tools/verify.log
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VirtualLoft.XR;

public static class LoftVerify
{
    static int pass, fail;
    static void Check(bool ok, string step)
    {
        if (ok) { pass++; Debug.Log($"[VERIFY] PASS: {step}"); }
        else { fail++; Debug.LogError($"[VERIFY] FAIL: {step}"); }
    }

    public static void RunAll()
    {
        pass = fail = 0;
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Loft.unity", OpenSceneMode.Single);

        var loft = GameObject.Find("Loft");
        Check(loft != null, "Loft root present");
        var agents = loft != null ? loft.GetComponent<AgentManager>() : Object.FindFirstObjectByType<AgentManager>();
        var boards = loft != null ? loft.GetComponent<BoardManager>() : Object.FindFirstObjectByType<BoardManager>();
        var voice = loft != null ? loft.GetComponent<VoiceCommands>() : Object.FindFirstObjectByType<VoiceCommands>();
        var net = loft != null ? loft.GetComponent<Multiplayer>() : Object.FindFirstObjectByType<Multiplayer>();
        var client = loft != null ? loft.GetComponent<LoftClient>() : Object.FindFirstObjectByType<LoftClient>();
        var test = loft != null ? loft.GetComponent<StandupFlowTest>() : Object.FindFirstObjectByType<StandupFlowTest>();

        Check(agents != null, "AgentManager present");
        Check(boards != null, "BoardManager present");
        Check(voice != null, "VoiceCommands present");
        Check(net != null, "Multiplayer present");
        Check(client != null, "LoftClient present");
        Check(test != null, "StandupFlowTest attached");
        Check(client == null || client.boards != null, "LoftClient wired to boards");
        if (agents == null || boards == null || voice == null || net == null)
        {
            Finish();
            return;
        }

        Check(agents.agents.Count == 3, $"3 agents wired (found {agents.agents.Count})");
        bool idsOk = agents.agents.TrueForAll(a => a != null)
            && agents.agents.Exists(a => a.agentId == "scout")
            && agents.agents.Exists(a => a.agentId == "builder")
            && agents.agents.Exists(a => a.agentId == "keeper");
        Check(idsOk, "scout/builder/keeper ids");

        // 1. Wake
        bool doneFired = false;
        System.Action<string> h = _ => doneFired = true;
        agents.OnAgentDone += h;
        agents.WakeAll();
        bool allIdle = true;
        foreach (var a in agents.agents) allIdle &= a != null && a.CurrentState == AgentState.Idle;
        Check(allIdle, "wake 3 agents -> all Idle");

        // 2. Voice routing
        voice.testMode = true;
        voice.pushToTalkHeld = true;
        bool runHeard = voice.HandleTranscription("jalankan tes");
        Check(runHeard && voice.lastCommand == VoiceCommandId.Run, "voice ID 'jalankan tes' -> Run");
        voice.SimulateCommand("run landing test");
        Check(voice.lastCommand == VoiceCommandId.Run, "voice EN 'run landing test' -> Run");

        // 3. Assign builder
        agents.AssignTask("builder", "run landing test");
        var builder = agents.agents.Find(a => a != null && a.agentId == "builder");
        Check(builder != null && builder.CurrentState == AgentState.Working, "Builder Working after assign");

        // 4. Boards
        string logId = boards.AddCard("log", "PASS 12/12", "Done");
        Check(!string.IsNullOrEmpty(logId), "log board AddCard PASS 12/12");
        string cardId = boards.AddCard("kanban", "Morning standup", "Doing");
        Check(boards.VoiceMove("kanban", cardId, "move that to done"), "VoiceMove -> Done");
        var card = boards.ListCards("kanban").Find(c => c.id == cardId);
        Check(card != null && card.column == "Done", "card column == Done");
        voice.InvokeCommand(VoiceCommandId.Done);
        Check(voice.lastCommand == VoiceCommandId.Done, "button fallback InvokeCommand(Done)");

        // 5. Done broadcast
        builder.SetState(AgentState.Done);
        Check(doneFired, "OnAgentDone broadcast on Done");

        // 6. Multiplayer rules
        net.JoinRoom("focus-01", "CEO", LoftRole.CEO);
        bool ceoAssign = net.Assign("CEO", "agent:builder", "Member-1");
        bool memberAssign = net.Assign("Member-1", "agent:builder", "Member-1");
        Check(ceoAssign && !memberAssign, "CEO assign allowed, member assign denied");
        bool memberOwn = net.MoveCard("Member-1", cardId, "Member-1", "Doing", "Done");
        bool memberOther = net.MoveCard("Member-1", cardId, "CEO", "Doing", "Done");
        Check(memberOwn && !memberOther, "member moves own card, denied others'");
        Check(net.activityLog.Count >= 2, $"activity log has entries ({net.activityLog.Count})");

        // 7. Travel test
        string saved = boards.Save();
        boards.Load(saved);
        Check(boards.ListCards("kanban").Exists(c => c.id == cardId && c.column == "Done"),
            "save/load roundtrip keeps Done card");

        agents.OnAgentDone -= h;
        Finish();
    }

    static void Finish()
    {
        Debug.Log($"[VERIFY] SUMMARY: {pass} pass, {fail} fail — {(fail == 0 ? "GREEN" : "RED")}");
        EditorApplication.Exit(fail == 0 ? 0 : 1);
    }

    // E2EApply: consume the LIVE keeper output (Tools/e2e-result.json, written
    // by backend/tools/e2e-proof.mjs after a real Spark run) into the real
    // BoardManager object, proving Unity objects apply AI-made edits.
    public static void E2EApply()
    {
        pass = fail = 0;
        EditorSceneManager.OpenScene("Assets/Scenes/Loft.unity", OpenSceneMode.Single);
        var boards = Object.FindFirstObjectByType<BoardManager>();
        Check(boards != null, "BoardManager present for E2E");
        if (boards == null) { Finish(); return; }

        var p = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Tools/e2e-result.json");
        Check(System.IO.File.Exists(p), $"live keeper result exists ({p})");
        if (!System.IO.File.Exists(p)) { Finish(); return; }

        boards.Load(System.IO.File.ReadAllText(p));
        var card = boards.ListCards("kanban").Find(c => c.id == "standup-01");
        Check(card != null, "AI-touched card standup-01 exists on Unity board object");
        Check(card != null && card.column == "Done",
            $"Unity board shows AI edit: standup-01 column == Done (got '{card?.column}')");
        Debug.Log($"[E2E] Unity BoardManager now holds {boards.ListCards("kanban").Count} kanban cards from live AI output");
        Finish();
    }

    // FullA: the expanded Phase-A gate that runs headless (A5-A12, A19-A24,
    // A26, A28). GUI-Play rows (A2/A4/A25-shot) run via LoftAutoPlay; live
    // server rows (A13-A17) run from backend; A1/A3/A29/A30 wrap the batch.
    public static void FullA()
    {
        pass = fail = 0;
        EditorSceneManager.OpenScene("Assets/Scenes/Loft.unity", OpenSceneMode.Single);
        var agents = Object.FindFirstObjectByType<AgentManager>();
        var boards = Object.FindFirstObjectByType<BoardManager>();
        var voice = Object.FindFirstObjectByType<VoiceCommands>();
        var net = Object.FindFirstObjectByType<Multiplayer>();
        var comfort = Object.FindFirstObjectByType<VirtualLoft.XR.Comfort>();
        Check(agents != null && boards != null && voice != null && net != null, "A-core systems present");

        // A5: all 3 agents lively, Done broadcast x3
        int doneCount = 0;
        System.Action<string> h = _ => doneCount++;
        agents.OnAgentDone += h;
        agents.WakeAll();
        foreach (var id in new[] { "scout", "builder", "keeper" }) agents.AssignTask(id, "shakedown");
        bool allWorking = agents.agents.TrueForAll(a => a != null && a.CurrentState == AgentState.Working);
        Check(allWorking, "A5: all 3 agents Working after assign");
        foreach (var a in agents.agents) a.SetState(AgentState.Done);
        Check(doneCount == 3, $"A5: OnAgentDone fired x3 (got {doneCount})");

        // A6: stop/reset
        agents.AssignTask("builder", "busywork");
        agents.StopAll();
        Check(agents.agents.TrueForAll(a => a.CurrentState == AgentState.Idle), "A6: StopAll -> all Idle");

        // A7: unknown agent
        int beforeCards = boards.ListCards("kanban").Count;
        agents.AssignTask("nope", "ghost task");
        Check(boards.ListCards("kanban").Count == beforeCards, "A7: unknown agent -> warning, boards untouched");

        // A8/A9: all 10 voice commands EN + ID
        voice.testMode = true;
        voice.pushToTalkHeld = true;
        var en = new[] { "wake up", "run this", "stop that", "move that to wall", "summarize this", "assign to builder", "remind me at 5", "next item", "mark done", "focus mode" };
        var want = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        bool enOk = true;
        for (int i = 0; i < 10; i++) enOk &= voice.HandleTranscription(en[i]) && (int)voice.lastCommand == want[i];
        Check(enOk, "A8: voice EN 10/10 route");
        var idPhrases = new[] { "bangun semua", "jalankan tes", "berhenti", "pindah itu", "ringkas ini", "kasih ke", "ingatkan jam 5", "lanjut", "selesai", "mode fokus" };
        bool idOk = true;
        for (int i = 0; i < 10; i++) idOk &= voice.HandleTranscription(idPhrases[i]) && (int)voice.lastCommand == want[i];
        Check(idOk, "A9: voice ID 10/10 route");

        // A10: PTT gate
        voice.testMode = false;
        voice.pushToTalkHeld = false;
        bool gated = !voice.HandleTranscription("run this");
        Check(gated, "A10: PTT released + live mode -> transcription ignored");
        voice.testMode = true;
        voice.pushToTalkHeld = true;

        // A11: button fallback all 10
        bool btnOk = true;
        for (int i = 0; i < 10; i++) { voice.InvokeCommand((VirtualLoft.XR.VoiceCommandId)i); btnOk &= (int)voice.lastCommand == i; }
        Check(btnOk, "A11: button fallback 10/10 fire");

        // A12: garbage safe
        var cmdBefore = voice.lastCommand;
        bool g1 = !voice.HandleTranscription("asdfgh jkl");
        bool g2 = !voice.HandleTranscription("");
        Check(g1 && g2 && voice.lastCommand.Equals(cmdBefore), "A12: garbage/empty -> unrecognized, no state change");

        // A19/A20/A21: roles + ownership + log
        net.JoinRoom("focus-01", "CEO", VirtualLoft.XR.LoftRole.CEO);
        bool ceoOk = net.Assign("CEO", "agent:builder", "Member-1");
        bool memDenied = !net.Assign("Member-1", "agent:builder", "Member-1");
        Check(ceoOk && memDenied, "A19: CEO assign allowed, member assign denied");
        string c1 = boards.AddCard("kanban", "own card", "Doing");
        bool ownOk = net.MoveCard("Member-1", c1, "Member-1", "Doing", "Done");
        bool otherDenied = !net.MoveCard("Member-1", c1, "CEO", "Doing", "Done");
        Check(ownOk && otherDenied, "A20: member moves own, denied others'");
        net.MarkDone("Member-1", c1, "Doing");
        net.Assign("CEO", "agent:scout", "Member-1");
        Check(net.activityLog.Count >= 4, $"A21: activity log has entries ({net.activityLog.Count})");
        foreach (var e in net.activityLog)
            if (string.IsNullOrEmpty(e.userName) || string.IsNullOrEmpty(e.action)) { Check(false, "A21: log entries complete"); goto logDone; }
        Check(true, "A21: log entries complete (who/what/from/to)");
    logDone:;

        // A22: leave/rejoin preserves state
        int cardsBefore = boards.ListCards("kanban").Count;
        net.LeaveRoom();
        net.JoinRoom("focus-01", "CEO", VirtualLoft.XR.LoftRole.CEO);
        Check(boards.ListCards("kanban").Count == cardsBefore, "A22: leave/rejoin preserves boards");

        // A23: save/kill/reload roundtrip (simulated: Save -> mutate -> Load)
        string saved = boards.Save();
        boards.AddCard("kanban", "temp noise", "ToDo");
        boards.Load(saved);
        Check(!boards.ListCards("kanban").Exists(c => c.text == "temp noise"), "A23: save/mutate/load roundtrip restores");

        // A24: corrupt JSON never bricks
        int n24 = boards.ListCards("kanban").Count;
        boards.Load("{this is not json!!!");
        boards.Load("{\"boards\":null}");
        Check(boards.ListCards("kanban").Count == n24, "A24: corrupt/foreign JSON ignored, boards unchanged");

        // A26: desks match spec +/-0.1m
        var want26 = new System.Collections.Generic.Dictionary<string, Vector3>
        {
            { "scout", new Vector3(-2f, 0, 1.5f) }, { "builder", new Vector3(0f, 0, 1.5f) }, { "keeper", new Vector3(2f, 0, 1.5f) },
        };
        bool posOk = true;
        foreach (var a in agents.agents)
        {
            var go = a.gameObject;
            if (!want26.TryGetValue(a.agentId, out var wp)) { posOk = false; break; }
            if (Vector3.Distance(go.transform.position, wp) > 0.11f) { posOk = false; break; }
        }
        Check(posOk, "A26: desks at spec positions (+/-0.1m)");
        bool wallOk = GameObject.Find("Board_notes") != null && GameObject.Find("Board_kanban") != null
            && GameObject.Find("Board_calendar") != null && GameObject.Find("Board_log") != null;
        Check(wallOk, "A26: 4 board plates on main wall");

        // A28: comfort policy enforced on the scene object        Check(comfort != null, "A28: Comfort policy object in scene");
        if (comfort != null)
        {
            Check(comfort.allowSmoothLocomotion == false, "A28: teleport-only (smooth locomotion off)");
            Check(comfort.seatedMode == true, "A28: seated default");
            var cam = GameObject.Find("Main Camera");
            bool eyeOk = cam != null && Mathf.Abs(cam.transform.position.y - VirtualLoft.XR.Comfort.SEATED_EYE_HEIGHT) < 0.05f;
            Check(eyeOk, $"A28: camera at seated eye height ({VirtualLoft.XR.Comfort.SEATED_EYE_HEIGHT}m)");
        }

        agents.OnAgentDone -= h;
        Finish();
    }

    // DeskCheck: the in-app HQ path (Loft.app self-contained). Assign must
    // create a kanban card + set Working + write log + register task, all
    // with zero backend. Unknown agent must change nothing. Views must fill.
    public static void DeskCheck()
    {
        pass = fail = 0;
        EditorSceneManager.OpenScene("Assets/Scenes/Loft.unity", OpenSceneMode.Single);
        var desk = Object.FindFirstObjectByType<LoftDesk>();
        var agents = Object.FindFirstObjectByType<AgentManager>();
        var boards = Object.FindFirstObjectByType<BoardManager>();
        Check(desk != null, "Desk: LoftDesk present in scene");
        Check(agents != null && boards != null, "Desk: agents+boards present");
        if (desk == null || agents == null || boards == null) { Finish(); return; }
        Check(desk.agents != null && desk.boards != null && desk.net != null, "Desk: wired to agents/boards/net");

        int n0 = boards.ListCards("kanban").Count;
        string bad = desk.Assign("nope", "ghost");
        Check(bad == null && boards.ListCards("kanban").Count == n0, "Desk: unknown agent -> null, boards untouched");

        agents.WakeAll();
        string cardId = desk.Assign("builder", "desk-check task");
        Check(!string.IsNullOrEmpty(cardId), "Desk: Assign returns card id");
        var card = boards.ListCards("kanban").Find(c => c.id == cardId);
        Check(card != null && card.column == "Doing" && card.text.Contains("builder"), "Desk: kanban card Doing tagged builder");
        var builder = agents.agents.Find(a => a != null && a.agentId == "builder");
        Check(builder != null && builder.CurrentState == AgentState.Working, "Desk: builder Working after assign");
        Check(boards.ListCards("log").Exists(c => c.text.Contains("ASSIGN builder")), "Desk: log board shows the assign");
        Check(desk.tasks.Exists(t => t.cardId == cardId && !t.done), "Desk: task registered active with progress");
        Check(desk.eventLog.Count > 0, $"Desk: in-app event log has entries ({desk.eventLog.Count})");

        try
        {
            desk.EnsureBoardViews();
            desk.RefreshViews();
            bool viewsOk = true;
            foreach (var id in new[] { "kanban", "log", "notes" })
            {
                var plate = GameObject.Find($"Board_{id}");
                var c = plate != null ? plate.transform.Find("Content") : null;
                var tm = c != null ? c.GetComponent<TextMesh>() : null;
                if (tm == null || string.IsNullOrEmpty(tm.text)) { viewsOk = false; break; }
            }
            Check(viewsOk, "Desk: in-world board Content texts filled");
        }
        catch (System.Exception e) { Check(false, $"Desk: RefreshViews threw {e.Message}"); }

        var label = GameObject.Find("Builder")?.transform.Find("Label")?.GetComponent<TextMesh>();
        Check(label != null && label.text.Contains("BUILDER"), "Desk: agent floating label shows state/task");

        desk.StopAll();
        Check(agents.agents.TrueForAll(a => a.CurrentState == AgentState.Idle), "Desk: StopAll -> all Idle");
        Finish();
    }
    // QuestCheck: the Quest-shaped in-world path. Same LoftDesk API, but
    // driven through LoftQuestPanel's 3D buttons + teleport pads (the exact
    // methods the XR controller ray will call later).
    public static void QuestCheck()
    {
        pass = fail = 0;
        EditorSceneManager.OpenScene("Assets/Scenes/Loft.unity", OpenSceneMode.Single);
        var desk = Object.FindFirstObjectByType<LoftDesk>();
        var panel = Object.FindFirstObjectByType<LoftQuestPanel>();
        var agents = Object.FindFirstObjectByType<AgentManager>();
        var boards = Object.FindFirstObjectByType<BoardManager>();
        var comfort = Object.FindFirstObjectByType<VirtualLoft.XR.Comfort>();
        Check(panel != null, "Quest: LoftQuestPanel in scene");
        Check(desk != null && agents != null && boards != null, "Quest: desk/agents/boards present");
        if (panel == null || desk == null || agents == null || boards == null) { Finish(); return; }
        panel.EnsureBuilt();

        var root = GameObject.Find("QuestPanel");
        bool btns = root != null;
        foreach (var b in new[] { "Btn_scout", "Btn_builder", "Btn_keeper", "Btn_done", "Btn_stop" })
        {
            var t = root != null ? root.transform.Find(b) : null;
            var col = t != null ? t.GetComponent<Collider>() : null;
            if (t == null || col == null) { btns = false; break; }
        }
        Check(btns, "Quest: 5 pointable buttons with colliders (3 agents + done + stop)");
        bool pads = true;
        for (int i = 0; i < 3; i++) if (GameObject.Find($"TelePad_{i}") == null) { pads = false; break; }
        Check(pads, "Quest: 3 teleport pads on floor");
        var status = root != null ? root.transform.Find("Status") : null;
        Check(status != null && status.GetComponent<TextMesh>() != null, "Quest: status readout on panel");

        agents.WakeAll();
        int n0 = boards.ListCards("kanban").Count;
        panel.PressAgent(0); // scout via 3D button path
        var card = boards.ListCards("kanban").Find(c => c.column == "Doing" && c.text.Contains("[scout]"));
        Check(card != null && boards.ListCards("kanban").Count == n0 + 1, "Quest: PressAgent creates Doing card tagged [scout]");
        var scout = agents.agents.Find(a => a != null && a.agentId == "scout");
        Check(scout != null && scout.CurrentState == AgentState.Working, "Quest: scout Working after PressAgent");
        Check(boards.ListCards("log").Exists(c => c.text.Contains("ASSIGN scout")), "Quest: log board shows the 3D assign");

        panel.PressDone();
        Check(desk.tasks.Exists(t => t.agentId == "scout" && t.done), "Quest: PressDone finishes the task");
        var doneCard = card != null ? boards.ListCards("kanban").Find(c => c.id == card.id) : null;
        Check(doneCard != null && doneCard.column == "Done", "Quest: card moved to Done with result");

        panel.PressAgent(1);
        panel.PressStop();
        Check(agents.agents.TrueForAll(a => a.CurrentState == AgentState.Idle), "Quest: PressStop -> all Idle");

        var cam = GameObject.Find("Main Camera");
        panel.TeleportTo(1);
        bool teleOk = cam != null && Vector3.Distance(cam.transform.position, new Vector3(0f, VirtualLoft.XR.Comfort.SEATED_EYE_HEIGHT, 5.2f)) < 0.05f;
        Check(teleOk, "Quest: TeleportTo(1) lands camera at pad 1, eye height pinned");
        Check(comfort != null && comfort.allowSmoothLocomotion == false, "Quest: teleport-only policy untouched");
        Finish();
    }
    // listening): every call must degrade to warning+false/null, game alive.
    public static void ServerDown()
    {
        pass = fail = 0;
        EditorSceneManager.OpenScene("Assets/Scenes/Loft.unity", OpenSceneMode.Single);
        var boards = Object.FindFirstObjectByType<BoardManager>();
        var client = Object.FindFirstObjectByType<LoftClient>();
        Check(boards != null && client != null, "A18: systems present");
        client.serverUrl = "http://127.0.0.1:49999"; // dead port: connection refused
        LoftClient.timeoutSeconds = 4; // fast-fail budget for the resilience probe
        int n0 = boards.ListCards("kanban").Count;
        bool ok = true;
        try
        {
            bool h = client.CheckHealthAsync().GetAwaiter().GetResult();
            bool l = client.LoadFromServerAsync().GetAwaiter().GetResult();
            bool s = client.SaveToServerAsync().GetAwaiter().GetResult();
            string r = client.RunAgentAsync("builder", "run landing test").GetAwaiter().GetResult();
            ok = !h && !l && !s && r == null;
        }
        catch (System.Exception e) { Debug.LogError($"[VERIFY] A18 threw: {e.Message}"); ok = false; }
        Check(ok, "A18: health/load/save/agent all degrade to false/null, zero exceptions");
        Check(boards.ListCards("kanban").Count == n0, "A18: boards untouched, game fully playable offline");
        Finish();
    }
}
#endif
