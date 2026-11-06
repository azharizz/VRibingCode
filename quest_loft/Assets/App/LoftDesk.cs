// VIRTUAL LOFT — LoftDesk.cs (in-app HQ, Mac player self-contained)
// The ONLY place the standalone Loft.app needs: walk, see boards, assign,
// watch progress + read the result — no backend, no browser, no console.
//
// - Walk: WASD / arrows on XZ, hold RIGHT mouse + drag to look. Y locked to
//   seated eye height (1.2m). XR comfort policy (teleport-only) is untouched:
//   this walker is the desktop test harness; Comfort.allowSmoothLocomotion
//   stays false and FullA/A28 still passes.
// - Boards: renders BoardManager cards onto the in-world plates (Content
//   TextMesh under Board_<id>) + mirrors them in the left overlay panel.
// - Assign: Assign(agentId, title) creates a kanban card, sets the agent
//   Working, logs to log board + activity log, then simulates real work over
//   ~12s with a visible progress %. Finish moves the card to Done, writes the
//   result to the log board, sets the agent Done. All inside this app.
// - Voice events are deliberately NOT auto-wired here: headless Play tests
//   fire InvokeCommand() and must stay isolated. In-app path is GUI + keys
//   1/2/3 (assign), M (done), X (stop).

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VirtualLoft.XR;

public class LoftDesk : MonoBehaviour
{
    [Header("Wire (auto-found if empty)")]
    public AgentManager agents;
    public BoardManager boards;
    public Multiplayer net;

    [Header("Walk (desktop test harness)")]
    public float walkSpeed = 2.5f;
    public float lookSpeed = 3f;

    [Header("Work simulation")]
    public float workSeconds = 12f;

    // ---- runtime ----
    [Serializable]
    public class TaskRec
    {
        public string cardId;
        public string agentId;
        public string title;
        public float progress; // 0..1
        public bool done;
        public string result;
    }

    public readonly List<TaskRec> tasks = new List<TaskRec>();
    public readonly List<TaskRec> doneTasks = new List<TaskRec>();
    public readonly List<string> eventLog = new List<string>(capacity: 40);
    readonly Dictionary<string, Coroutine> running = new Dictionary<string, Coroutine>();

    string taskInput = "run landing test";
    int selAgent = 1;
    readonly string[] agentIds = { "scout", "builder", "keeper" };
    readonly string[] agentNames = { "Scout", "Builder", "Keeper" };

    float yaw = 180f, pitch;
    float viewTimer;
    Camera cam;
    readonly Dictionary<string, Color> baseColors = new Dictionary<string, Color>();
    bool seeded;

    void Awake()
    {
        if (agents == null) agents = FindFirstObjectByType<AgentManager>();
        if (boards == null) boards = FindFirstObjectByType<BoardManager>();
        if (net == null) net = FindFirstObjectByType<Multiplayer>();
        cam = Camera.main ?? FindFirstObjectByType<Camera>();
        if (cam != null) { yaw = cam.transform.rotation.eulerAngles.y; pitch = 0f; }
        foreach (var a in agents.agents)
        {
            if (a == null) continue;
            // Belt-and-braces: labels must face the play side (+Z). Scenes
            // baked before the mirror fix have them backwards.
            var lab = a.transform.Find("Label");
            if (lab != null) lab.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var body = a.transform.Find("Body");
            if (body != null)
            {
                var r = body.GetComponent<Renderer>();
                if (r != null && !baseColors.ContainsKey(a.agentId)) baseColors[a.agentId] = r.material.color;
            }
        }
        EnsureBoardViews();
        SeedIfEmpty();
#if !UNITY_EDITOR
        LoadPersisted(); // player only: editor Play stays hermetic for tests
#endif
        Note("Desk ready — WASD walk, assign below, all in-app.");
    }

    // ---- public: the in-app assign path ----
    public string Assign(string agentId, string title)
    {
        if (agents == null || boards == null) { Note("Assign FAILED: systems missing"); return null; }
        var a = agents.agents.Find(x => x != null && x.agentId == agentId);
        if (a == null) { Note($"Assign FAILED: unknown agent '{agentId}'"); return null; }
        if (string.IsNullOrWhiteSpace(title)) title = "untitled task";
        title = title.Trim();

        string cardId = boards.AddCard("kanban", $"{title} [{agentId}]", "Doing");
        agents.AssignTask(agentId, title);
        try { net.Assign("CEO", $"agent:{agentId}", agentId); } catch { }
        try { net.Log("CEO", "assign", $"card:{cardId}", "", $"{agentId}:{title}"); } catch { }
        boards.AddCard("log", $"ASSIGN {agentId}: {title}", "ToDo");

        var rec = new TaskRec { cardId = cardId, agentId = agentId, title = title };
        tasks.Add(rec);
        Note($"ASSIGN {agentId}: {title}");
        if (running.TryGetValue(agentId, out var old) && old != null) StopCoroutine(old);
        running[agentId] = StartCoroutine(RunTask(rec));
        RefreshViews();
        return cardId;
    }

    public void Complete(string agentId)
    {
        var rec = tasks.Find(t => !t.done && t.agentId == agentId);
        if (rec == null) { Note($"Done: no active task for {agentId}"); return; }
        if (running.TryGetValue(agentId, out var old) && old != null) StopCoroutine(old);
        running[agentId] = StartCoroutine(FinishTask(rec, true));
    }

    public void StopAll()
    {
        foreach (var kv in running) if (kv.Value != null) StopCoroutine(kv.Value);
        running.Clear();
        foreach (var t in tasks) if (!t.done) { t.done = true; t.result = "STOPPED"; }
        if (agents != null) agents.StopAll();
        if (boards != null) boards.AddCard("log", "STOP all", "ToDo");
        Note("STOP all tasks");
        RefreshViews();
    }

    public void WakeAll()
    {
        if (agents != null) agents.WakeAll();
        Note("Wake all agents");
    }

    public void ExitApp()
    {
        Note("Exit — closing app");
        try { SavePersisted(); } catch { }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    IEnumerator RunTask(TaskRec rec)
    {
        yield return RunTaskInner(rec);
        running.Remove(rec.agentId);
    }

    IEnumerator RunTaskInner(TaskRec rec)
    {
        float el = 0f;
        while (el < workSeconds && !rec.done)
        {
            el += Time.deltaTime;
            rec.progress = Mathf.Clamp01(el / workSeconds);
            viewTimer += Time.deltaTime;
            if (viewTimer > 0.25f) { viewTimer = 0f; RefreshViews(); }
            yield return null;
        }
        if (!rec.done) yield return FinishTask(rec, false);
    }

    IEnumerator FinishTask(TaskRec rec, bool manual)
    {
        rec.done = true;
        rec.progress = 1f;
        string verdict = "PASS 12/12";
        rec.result = $"{verdict} — {rec.title}";
        if (boards != null)
        {
            boards.MoveCard("kanban", rec.cardId, "Done");
            boards.AddCard("log", $"{(manual ? "DONE" : "AUTO-DONE")} {rec.agentId}: {rec.result}", "Done");
        }
        if (agents != null)
        {
            var a = agents.agents.Find(x => x != null && x.agentId == rec.agentId);
            if (a != null) a.SetState(AgentState.Done);
        }
        try { net.Log("CEO", "done", $"card:{rec.cardId}", "Doing", "Done"); } catch { }
        Note($"{(manual ? "DONE" : "FINISH")} {rec.agentId}: {rec.result}");
        RefreshViews();
        yield break;
    }

    // ---- per-frame: walk + look + agent pulse ----
    void Update()
    {
        if (cam == null) cam = Camera.main ?? FindFirstObjectByType<Camera>();
        if (cam == null) return;
        bool typing = GUI.GetNameOfFocusedControl() == "taskField";
        if (!typing)
        {
            // Look: right-mouse drag.
            if (Input.GetMouseButton(1))
            {
                yaw -= Input.GetAxis("Mouse X") * lookSpeed;
                pitch -= Input.GetAxis("Mouse Y") * lookSpeed;
                pitch = Mathf.Clamp(pitch, -60f, 60f);
            }
            // Walk: WASD + arrows, camera-relative on XZ.
            float h = 0f, v = 0f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) v += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) v -= 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) h -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) h += 1f;
            h += Input.GetAxis("Horizontal") * 0.5f;
            v += Input.GetAxis("Vertical") * 0.5f;
            h = Mathf.Clamp(h, -1f, 1f); v = Mathf.Clamp(v, -1f, 1f);
            if (Mathf.Abs(h) > 0.001f || Mathf.Abs(v) > 0.001f)
            {
                var dir = Quaternion.Euler(0f, yaw, 0f) * new Vector3(h, 0f, v);
                var p = cam.transform.position + dir.normalized * walkSpeed * Time.deltaTime;
                p.x = Mathf.Clamp(p.x, -6f, 6f);
                p.z = Mathf.Clamp(p.z, 0.2f, 8f); // stay on content side of wall
                p.y = Comfort.SEATED_EYE_HEIGHT;
                cam.transform.position = p;
            }
            else
            {
                var p = cam.transform.position; // pin eye height even when idle (A28)
                if (Mathf.Abs(p.y - Comfort.SEATED_EYE_HEIGHT) > 0.001f)
                { p.y = Comfort.SEATED_EYE_HEIGHT; cam.transform.position = p; }
            }
            cam.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

            // Keys: 1/2/3 assign presets, M done, X stop.
            if (Input.GetKeyDown(KeyCode.Alpha1)) Assign("scout", string.IsNullOrWhiteSpace(taskInput) ? "review travel notes" : taskInput);
            if (Input.GetKeyDown(KeyCode.Alpha2)) Assign("builder", string.IsNullOrWhiteSpace(taskInput) ? "run landing test" : taskInput);
            if (Input.GetKeyDown(KeyCode.Alpha3)) Assign("keeper", string.IsNullOrWhiteSpace(taskInput) ? "tidy log wall" : taskInput);
            if (Input.GetKeyDown(KeyCode.M)) Complete(agentIds[Mathf.Clamp(selAgent, 0, 2)]);
            if (Input.GetKeyDown(KeyCode.X)) StopAll();
            if (Input.GetKeyDown(KeyCode.Escape)) ExitApp();
        }

        // Agent glow: Working pulses, Done goes green.
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 5f);
        foreach (var a in agents.agents)
        {
            if (a == null) continue;
            var body = a.transform.Find("Body");
            if (body == null) continue;
            var r = body.GetComponent<Renderer>();
            if (r == null) continue;
            Color c = baseColors.TryGetValue(a.agentId, out var b) ? b : Color.gray;
            if (a.CurrentState == AgentState.Working) r.material.color = Color.Lerp(c, Color.yellow, 0.35f + 0.3f * pulse);
            else if (a.CurrentState == AgentState.Done) r.material.color = Color.Lerp(c, Color.green, 0.55f);
            else r.material.color = c;
        }
    }

    // ---- overlay panel: everything visible + clickable in-app ----
    void OnGUI()
    {
        if (agents == null || boards == null) return;
        var left = new Rect(10, 10, 348, Mathf.Min(Screen.height - 20, 640));
        GUILayout.BeginArea(left, GUI.skin.box);
        GUILayout.Label("VIRTUAL LOFT — in-app desk (offline)");
        GUILayout.Label("Walk WASD/arrows • Look: RIGHT-drag • 1/2/3 assign • M done • X stop", GUI.skin.box);

        GUILayout.Space(4);
        GUILayout.Label("AGENTS");
        for (int i = 0; i < agentIds.Length; i++)
        {
            var a = agents.agents.Find(x => x != null && x.agentId == agentIds[i]);
            var rec = tasks.FindLast(t => t.agentId == agentIds[i]);
            string st = a != null ? a.CurrentState.ToString() : "?";
            string extra = "";
            if (rec != null && !rec.done) extra = $" {Mathf.RoundToInt(rec.progress * 100)}% — {rec.title}";
            else if (rec != null && rec.done) extra = $" ✓ {rec.result}";
            GUILayout.Label($"{agentNames[i]}: {st}{extra}");
        }

        GUILayout.Space(4);
        GUILayout.Label("ASSIGN (creates kanban card + runs it)");
        GUI.SetNextControlName("taskField");
        taskInput = GUILayout.TextField(taskInput, GUILayout.Height(22));
        selAgent = GUILayout.SelectionGrid(selAgent, agentNames, 3);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Assign")) Assign(agentIds[Mathf.Clamp(selAgent, 0, 2)], taskInput);
        if (GUILayout.Button("Done")) Complete(agentIds[Mathf.Clamp(selAgent, 0, 2)]);
        if (GUILayout.Button("Stop")) StopAll();
        if (GUILayout.Button("Wake")) WakeAll();
        GUILayout.EndHorizontal();

        var kan = boards.ListCards("kanban");
        int todo = kan.FindAll(c => c.column == "ToDo").Count;
        int doing = kan.FindAll(c => c.column == "Doing").Count;
        int done = kan.FindAll(c => c.column == "Done").Count;
        GUILayout.Label($"KANBAN ToDo {todo} • Doing {doing} • Done {done}");
        var logs = boards.ListCards("log");
        GUILayout.Label("LOG (latest)");
        for (int i = Mathf.Max(0, logs.Count - 3); i < logs.Count; i++)
            GUILayout.Label($"• {Trunc(logs[i].text, 44)} [{logs[i].column}]");
        var notes = boards.ListCards("notes");
        if (notes.Count > 0) GUILayout.Label($"NOTES: {Trunc(notes[notes.Count - 1].text, 44)}");

        GUILayout.Space(4);
        GUILayout.Label("EVENTS");
        for (int i = Mathf.Max(0, eventLog.Count - 4); i < eventLog.Count; i++)
            GUILayout.Label($"• {Trunc(eventLog[i], 46)}");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save")) SavePersisted();
        if (GUILayout.Button("Load")) LoadPersisted();
        if (GUILayout.Button("Exit")) ExitApp();
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    // ---- board plates: draw cards in the 3D world ----
    public void EnsureBoardViews()
    {
        foreach (var id in new[] { "notes", "kanban", "calendar", "log" })
        {
            var plate = GameObject.Find($"Board_{id}");
            if (plate == null) continue;
            // Fix pre-mirror-fix scenes: title must face play side, proud of face.
            var t = plate.transform.Find("Title");
            if (t != null)
            {
                t.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                var tp = t.transform.localPosition;
                if (tp.z < 0.5f) t.transform.localPosition = new Vector3(tp.x, tp.y, 0.7f);
            }
            var c = plate.transform.Find("Content");
            if (c != null)
            {
                c.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                var cp = c.transform.localPosition;
                if (cp.z < 0.5f) c.transform.localPosition = new Vector3(cp.x, cp.y, 0.7f);
                continue;
            }
            var go = new GameObject("Content");
            go.transform.SetParent(plate.transform, false);
            go.transform.localPosition = new Vector3(0f, -0.12f, 0.7f);
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var tm = go.AddComponent<TextMesh>();
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.fontSize = 40;
            tm.characterSize = 0.01f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(0.85f, 0.92f, 1f);
        }
    }

    void SeedIfEmpty()
    {
        if (seeded || boards == null) return;
        seeded = true;
        if (boards.ListCards("kanban").Count == 0)
        {
            boards.AddCard("kanban", "Morning standup [keeper]", "Doing");
            boards.AddCard("kanban", "Write launch notes [builder]", "ToDo");
            boards.AddCard("kanban", "Book Quest for weekend [scout]", "ToDo");
        }
        if (boards.ListCards("log").Count == 0)
            boards.AddCard("log", "Welcome — assign below, watch it run here.", "ToDo");
        if (boards.ListCards("notes").Count == 0)
            boards.AddCard("notes", "Infinite wall, zero mess.", "ToDo");
        RefreshViews();
    }

    public void RefreshViews()
    {
        if (boards == null) return;
        SetContent("kanban", KanbanText());
        SetContent("log", LogText());
        SetContent("notes", CardsText("notes", 3));
        SetContent("calendar", CardsText("calendar", 3));
        RefreshAgentLabels();
    }

    void SetContent(string id, string text)
    {
        var plate = GameObject.Find($"Board_{id}");
        var c = plate != null ? plate.transform.Find("Content") : null;
        if (c == null) return;
        var tm = c.GetComponent<TextMesh>();
        if (tm != null) tm.text = text;
    }

    string KanbanText()
    {
        var kan = boards.ListCards("kanban");
        string Line(string col)
        {
            var list = kan.FindAll(c => c.column == col);
            if (list.Count == 0) return $"{col}: —";
            int show = Math.Min(3, list.Count);
            var parts = new List<string>();
            for (int i = 0; i < show; i++) parts.Add(Trunc(list[i].text, 22));
            string more = list.Count > show ? $" +{list.Count - show}" : "";
            return $"{col}({list.Count}): {string.Join(" | ", parts)}{more}";
        }
        return $"{Line("ToDo")}\n{Line("Doing")}\n{Line("Done")}";
    }

    string LogText()
    {
        var logs = boards.ListCards("log");
        var parts = new List<string>();
        for (int i = Mathf.Max(0, logs.Count - 4); i < logs.Count; i++)
            parts.Add($"• {Trunc(logs[i].text, 30)}");
        return parts.Count == 0 ? "log: —" : string.Join("\n", parts);
    }

    string CardsText(string id, int max)
    {
        var list = boards.ListCards(id);
        if (list.Count == 0) return $"{id}: —";
        var parts = new List<string>();
        for (int i = Mathf.Max(0, list.Count - max); i < list.Count; i++)
            parts.Add($"• {Trunc(list[i].text, 30)}");
        return string.Join("\n", parts);
    }

    void RefreshAgentLabels()
    {
        foreach (var a in agents.agents)
        {
            if (a == null) continue;
            var label = a.transform.Find("Label");
            if (label == null) continue;
            var tm = label.GetComponent<TextMesh>();
            if (tm == null) continue;
            var rec = tasks.FindLast(t => t.agentId == a.agentId);
            string line2 = a.CurrentState.ToString().ToUpperInvariant();
            if (rec != null && !rec.done) line2 += $" {Mathf.RoundToInt(rec.progress * 100)}%";
            string task = rec != null ? Trunc(rec.title, 24) : "";
            tm.text = string.IsNullOrEmpty(task) ? $"{a.agentId.ToUpperInvariant()}\n{line2}" : $"{a.agentId.ToUpperInvariant()}\n{line2}\n{task}";
        }
    }

    void Note(string s)
    {
        eventLog.Add($"{DateTime.Now:HH:mm:ss} {s}");
        if (eventLog.Count > 40) eventLog.RemoveAt(0);
        Debug.Log($"[Desk] {s}");
    }

    static string Trunc(string s, int n)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace("\n", " ").Trim();
        return s.Length <= n ? s : s.Substring(0, n - 1) + "…";
    }

    // ---- persistence inside the app (player-local) ----
    void SavePersisted()
    {
        try
        {
            var path = System.IO.Path.Combine(Application.persistentDataPath, "loft-desk.json");
            System.IO.File.WriteAllText(path, boards.Save());
            Note($"Saved {path}");
        }
        catch (Exception e) { Note($"Save FAILED: {e.Message}"); }
    }

    void LoadPersisted()
    {
        try
        {
            var path = System.IO.Path.Combine(Application.persistentDataPath, "loft-desk.json");
            if (!System.IO.File.Exists(path)) return;
            boards.Load(System.IO.File.ReadAllText(path));
            Note("Loaded saved loft");
            RefreshViews();
        }
        catch (Exception e) { Note($"Load skipped: {e.Message}"); }
    }
}
