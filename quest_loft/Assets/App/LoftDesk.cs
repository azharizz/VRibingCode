// VIRTUAL LOFT — LoftDesk.cs: the CEO's desk. Glue between the backend and
// the room. It never simulates work:
//   server events (LoftClient.OnEvent) -> animals (AgentManager) + wall boards
//   CEO actions (panel / voice / keys / desktop overlay) -> LoftClient commands
//
// Desktop harness (no headset): WASD walk + right-drag look move the
// CameraRig; the OnGUI overlay only draws when no XR device is active
// (OnGUI never renders inside a headset — LoftQuestPanel is the VR UI).

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.XR;
using VirtualLoft.XR;

public class LoftDesk : MonoBehaviour
{
    [Header("Wire (auto-found if empty)")]
    public AgentManager agents;
    public LoftClient client;
    public LoftWorld world;
    public VoiceCommands voice;
    public LoftParty party;

    [Header("Desktop walk")]
    public float walkSpeed = 2.5f;
    public float lookSpeed = 3f;

    [Serializable]
    public class TaskView { public string id, agentId, title, status, last, result; }

    public readonly Dictionary<string, TaskView> tasks = new Dictionary<string, TaskView>();
    public readonly List<string> eventLog = new List<string>(40);
    public string selectedAgentId;
    public int maxAgents = AgentManager.MAX_AGENTS;
    public bool Connected => client != null && client.Connected;
    public Rect overlayRect; // LoftQuestPanel ignores mouse clicks inside it

    // ---- spawn form (shared by the desktop overlay and the VR console) ----
    // Everything is free: any model id (typed), any saved role or an AI-written
    // one, any animal. Lists come from the server snapshot.
    public readonly List<JObject> roles = new List<JObject>();
    public List<string> skins = new List<string> { "fox", "owl", "cat", "bear", "rabbit", "penguin", "frog", "panda" };
    public List<string> writeTools = new List<string> { "write_file", "edit_file", "run_command" };
    public string defaultModel = "";
    public string formName = "", formModel = "", formSkin = "random", formRoleId = "builder", formDescribe = "";
    public JObject formDraft; // AI-written role awaiting CREATE (null = use formRoleId)
    public bool drafting;
    bool showForm;

    string input = "Add a /health endpoint with a test";
    string feedback = "";
    float pitch;

    void Awake()
    {
        if (agents == null) agents = FindFirstObjectByType<AgentManager>();
        if (client == null) client = FindFirstObjectByType<LoftClient>();
        if (world == null) world = FindFirstObjectByType<LoftWorld>();
        if (world == null) world = gameObject.AddComponent<LoftWorld>(); // scenes built before themes existed
        if (party == null) party = FindFirstObjectByType<LoftParty>();
        if (party == null) party = gameObject.AddComponent<LoftParty>();
        if (voice == null) voice = FindFirstObjectByType<VoiceCommands>();
        if (client != null) client.OnEvent += HandleEvent;
        if (voice != null) voice.OnCommand += HandleVoice;
        Note($"Connecting to {client?.serverUrl ?? "(no LoftClient)"} …");
    }

    void OnDestroy()
    {
        if (client != null) client.OnEvent -= HandleEvent;
        if (voice != null) voice.OnCommand -= HandleVoice;
    }

    // ---- server events -> room (the only place agent/task state changes) ----
    public void HandleEvent(JObject e)
    {
        switch ((string)e["type"])
        {
            case "snapshot":
                maxAgents = (int?)e["maxAgents"] ?? maxAgents;
                defaultModel = (string)e["defaultModel"] ?? defaultModel;
                if (e["skins"] is JArray sk) skins = sk.Select(x => (string)x).ToList();
                if (e["writeTools"] is JArray wt) writeTools = wt.Select(x => (string)x).ToList();
                SetRoles(e["roles"] as JArray);
                if (party != null) party.SetParty((bool?)e["party"] ?? false); // join a party already going
                var ids = new HashSet<string>();
                foreach (var a in e["agents"] ?? new JArray()) { UpsertAgent((JObject)a); ids.Add((string)a["id"]); }
                foreach (var a in agents.agents.ToArray())
                    if (a != null && !ids.Contains(a.agentId)) agents.Remove(a.agentId);
                if (selectedAgentId != null && !ids.Contains(selectedAgentId)) selectedAgentId = null;
                tasks.Clear();
                foreach (var t in e["tasks"] ?? new JArray()) UpsertTask((JObject)t, false);
                Note($"Connected — {ids.Count}/{maxAgents} agents");
                break;
            case "agent":
                bool fresh = agents.Find((string)e["agent"]?["id"]) == null;
                UpsertAgent((JObject)e["agent"]);
                var born = agents.Find((string)e["agent"]?["id"]);
                if (fresh && born != null) LoftAudio.PlayAt("sfx_spawn", born.GetComponent<AgentSkin>().home + Vector3.up * 0.6f);
                break;
            case "agent.removed":
                var gone = (string)e["id"];
                var leaving = agents.Find(gone);
                if (leaving != null) LoftAudio.PlayAt("sfx_remove", leaving.transform.position + Vector3.up * 0.6f);
                agents.Remove(gone);
                if (selectedAgentId == gone) selectedAgentId = null;
                Note($"– {gone} left the team");
                break;
            case "task":
                UpsertTask((JObject)e["task"], true);
                break;
            case "roles":
                SetRoles(e["roles"] as JArray);
                break;
            case "party":
                bool on = (bool?)e["on"] ?? false;
                if (party != null && party.On != on) { party.SetParty(on); Note(on ? "★ PARTY TIME! ★" : "Party's over — back to work"); }
                break;
            case "goal":
                Note($"GOAL {(string)e["status"]}: {LoftUtil.Trunc((string)e["goal"], 40)} {(string)e["error"]}");
                break;
            case "error":
                Note($"⚠ {(string)e["message"]}");
                break;
            case "disconnected":
                Note("Backend offline — retrying…");
                break;
        }
        RefreshViews();
    }

    void UpsertAgent(JObject a)
    {
        if (a == null) return;
        var id = (string)a["id"];
        bool isNew = agents.Find(id) == null;
        var role = RoleTitle(a);
        var sm = agents.Upsert(id, (string)a["name"], role, (string)a["skin"], AgentManager.ParseState((string)a["state"]));
        if (sm == null) return;
        var model = (string)a["model"] ?? "";
        if (isNew) Note($"+ {(string)a["name"]} ({role}, {ShortModel(model)}, {(string)a["skin"]}) joined");
        var skin = sm.GetComponent<AgentSkin>();
        skin.SetMeta(model, (double?)a["usage"]?["costUsd"], (long?)a["usage"]?["inputTokens"] + (long?)a["usage"]?["outputTokens"]);
        skin.SetSelected(id == selectedAgentId);
    }

    void UpsertTask(JObject t, bool log)
    {
        if (t == null) return;
        var id = (string)t["id"];
        tasks.TryGetValue(id, out var old);
        var v = new TaskView
        {
            id = id,
            agentId = (string)t["agentId"],
            title = (string)t["title"],
            status = (string)t["status"],
            last = (string)t["last"],
            result = (string)t["result"],
        };
        tasks[id] = v;
        if (log && (old == null || old.status != v.status))
        {
            if (v.status == "approved") LoftAudio.Play("sfx_approve");
            else if (v.status == "rejected") LoftAudio.Play("sfx_reject");
            string who = AgentName(v.agentId);
            string mark = v.status switch
            {
                "queued" => "QUEUED", "running" => "▶", "review" => "✓ REVIEW", "approved" => "★ APPROVED",
                "rejected" => "↺ REDO", "failed" => "✗ FAILED", "stopped" => "■ STOPPED", _ => v.status,
            };
            Note($"{mark} {who}: {LoftUtil.Trunc(FirstLine(v.title), 36)}");
        }
        var skin = agents.Find(v.agentId)?.GetComponent<AgentSkin>();
        if (skin != null && v.status == "running") skin.SetLine(string.IsNullOrEmpty(v.last) ? FirstLine(v.title) : v.last);
        else if (skin != null && old?.status == "running") skin.SetLine(v.status == "review" ? "waiting for CEO review" : v.status);
    }

    static string RoleTitle(JObject a) => a["role"] is JObject ro ? (string)ro["title"] : (string)a["role"];
    public static string ShortModel(string m) => string.IsNullOrEmpty(m) ? "default" : m.Substring(m.LastIndexOf('/') + 1);

    void SetRoles(JArray list)
    {
        if (list == null) return;
        roles.Clear();
        foreach (var r in list) if (r is JObject o) roles.Add(o);
        if (!roles.Any(r => (string)r["id"] == formRoleId) && roles.Count > 0) formRoleId = (string)roles[0]["id"];
    }

    // ---- spawn form actions ----
    public void CycleRole()
    {
        formDraft = null;
        if (roles.Count == 0) return;
        int i = roles.FindIndex(r => (string)r["id"] == formRoleId);
        formRoleId = (string)roles[(i + 1) % roles.Count]["id"];
    }

    public void CycleSkin()
    {
        var all = new List<string> { "random" };
        all.AddRange(skins);
        formSkin = all[(all.IndexOf(formSkin) + 1) % all.Count];
    }

    // Plain-language need -> AI writes the role; shown in the form until CREATE.
    public void DraftRole(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return;
        formDescribe = description.Trim();
        drafting = true;
        Note($"Writing a role for: {LoftUtil.Trunc(formDescribe, 30)} …");
        client.Then(client.DraftRole(formDescribe), res =>
        {
            drafting = false;
            formDraft = res?["draft"] as JObject;
            if (formDraft != null) Note($"Role ready: {(string)formDraft["title"]} — review, then CREATE");
        });
    }

    JObject FormRole() => formDraft ?? roles.FirstOrDefault(r => (string)r["id"] == formRoleId);

    public List<string> FormTools() => (FormRole()?["tools"] as JArray)?.Select(t => (string)t).ToList() ?? new List<string>();

    // CEO can take a risky tool away from an AI-written role before creating it.
    public void ToggleDraftTool(string tool)
    {
        if (formDraft == null) return;
        var tools = FormTools();
        if (!tools.Remove(tool)) tools.Add(tool);
        formDraft["tools"] = new JArray(tools);
    }

    public string FormSummary()
    {
        var role = FormRole();
        var risky = FormTools().Where(writeTools.Contains).ToList();
        return $"{(formDraft != null ? "NEW " : "")}{(string)role?["title"] ?? "?"}" +
               $"{(risky.Count > 0 ? $" ⚠ {string.Join(",", risky)}" : "")} · {formSkin} · " +
               $"{(string.IsNullOrWhiteSpace(formModel) ? $"default ({ShortModel(defaultModel)})" : formModel)}" +
               $"{(string.IsNullOrWhiteSpace(formName) ? "" : $" · \"{formName}\"")}";
    }

    public void SpawnFromForm()
    {
        if (agents.agents.Count >= maxAgents) { Note($"Team is full ({maxAgents} max)"); return; }
        if (drafting) { Note("Still writing the role…"); return; }
        string model = string.IsNullOrWhiteSpace(formModel) ? null : formModel.Trim();
        string name = string.IsNullOrWhiteSpace(formName) ? null : formName.Trim();
        if (formDraft != null)
        {
            var role = new JObject { ["title"] = formDraft["title"], ["instructions"] = formDraft["instructions"], ["tools"] = formDraft["tools"] };
            _ = client.SaveRole(role); // keep it in the library for next time
            _ = client.Spawn(model, null, role, name, formSkin);
            formDraft = null;
        }
        else _ = client.Spawn(model, formRoleId, null, name, formSkin);
        formName = "";
        Note($"Spawning {FormSummaryShort(name)} …");
    }

    string FormSummaryShort(string name) => name ?? (string)FormRole()?["title"] ?? "agent";

    // ---- CEO actions (panel, voice, keys and overlay all land here) ----
    public void Goal(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Note($"CEO GOAL: {LoftUtil.Trunc(text, 40)}");
        _ = client.Goal(text.Trim());
    }

    public void Assign(string agentId, string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        if (string.IsNullOrEmpty(agentId) || agents.Find(agentId) == null) { Goal(title); return; }
        _ = client.Assign(agentId, title.Trim());
    }

    public TaskView ReviewTask()
    {
        var waiting = tasks.Values.Where(t => t.status == "review" || t.status == "failed").ToList();
        return waiting.LastOrDefault(t => t.agentId == selectedAgentId) ?? waiting.FirstOrDefault();
    }

    public void Approve()
    {
        var t = ReviewTask();
        if (t == null || t.status != "review") { Note("Nothing waiting for approval"); return; }
        _ = client.Approve(t.id);
    }

    public void Reject(string why)
    {
        var t = ReviewTask();
        if (t == null) { Note("Nothing to reject"); return; }
        _ = client.Reject(t.id, why);
    }

    public void Stop(string agentId = null)
    {
        Note(agentId == null ? "STOP everyone" : $"STOP {AgentName(agentId)}");
        _ = client.Stop(agentId);
    }

    // Quick spawn: a saved role with the form's model, random animal.
    public void SpawnAgent(string roleId)
    {
        if (agents.agents.Count >= maxAgents) { Note($"Team is full ({maxAgents} max)"); return; }
        _ = client.Spawn(string.IsNullOrWhiteSpace(formModel) ? null : formModel.Trim(), roleId, null, null, "random");
    }

    public void RemoveSelected()
    {
        if (selectedAgentId == null) { Note("Select an agent first (point at it)"); return; }
        _ = client.RemoveAgent(selectedAgentId);
    }

    public void Select(string agentId)
    {
        selectedAgentId = agentId;
        foreach (var a in agents.agents)
            if (a != null) a.GetComponent<AgentSkin>().SetSelected(a.agentId == agentId);
        if (agentId != null) Note($"Selected {AgentName(agentId)}");
    }

    public void SelectNext()
    {
        var list = agents.agents.Where(a => a != null).ToList();
        if (list.Count == 0) return;
        int i = list.FindIndex(a => a.agentId == selectedAgentId);
        Select(list[(i + 1) % list.Count].agentId);
    }

    // PARTY button / P key / voice: shared through the server so every headset
    // parties together; works locally too when the backend is offline.
    public void Party()
    {
        bool on = party == null || !party.On;
        if (party != null) party.SetParty(on);
        Note(on ? "★ PARTY TIME! ★" : "Party's over — back to work");
        if (client != null) _ = client.Party(on);
    }

    public void NextTheme()
    {
        var id = world != null ? world.NextTheme() : null;
        Note(id != null ? $"Scene → {world.Current.name ?? id}" : "No themes in Resources/Themes");
    }

    public void ExitApp()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void HandleVoice(VoiceCommandId id, string words)
    {
        switch (id)
        {
            case VoiceCommandId.Run: if (words.Length > 0) Goal(words); else Note("Say: run <goal>"); break;
            case VoiceCommandId.Assign: Assign(selectedAgentId, words); break;
            case VoiceCommandId.Stop: Stop(); break;
            case VoiceCommandId.Done: Approve(); break;
            case VoiceCommandId.Next: SelectNext(); break;
            case VoiceCommandId.FocusMode: NextTheme(); break;
            case VoiceCommandId.Party: Party(); break;
            case VoiceCommandId.Wake: Note("Team is listening"); break;
            default: Note($"Voice '{id}' has no action yet"); break;
        }
    }

    // ---- wall boards ----
    public void RefreshViews()
    {
        SetBoard("kanban", KanbanText());
        SetBoard("log", string.Join("\n", eventLog.Skip(Math.Max(0, eventLog.Count - 6)).Select(s => LoftUtil.Trunc(s, 34))));
        var r = ReviewTask();
        SetBoard("notes", r == null ? "Nothing to review yet."
            : $"{AgentName(r.agentId)} — {r.status.ToUpperInvariant()}\n{LoftUtil.Wrap(r.result, 40, 7)}");
        var counts = agents.agents.Where(a => a != null).GroupBy(a => a.CurrentState).Select(g => $"{g.Key} {g.Count()}");
        var skinsNow = agents.agents.Where(a => a != null).Select(a => a.GetComponent<AgentSkin>()).ToList();
        double cost = skinsNow.Sum(k => k.costUsd ?? 0);
        long tokens = skinsNow.Sum(k => k.tokens);
        SetBoard("calendar", $"{agents.agents.Count}/{maxAgents} agents\n{string.Join("  ", counts)}\n" +
                             $"{tokens / 1000}k tokens{(cost > 0 ? $" · ${cost:0.000}" : "")}\n{(Connected ? "● live" : "○ offline")}");
    }

    string KanbanText()
    {
        string Col(string label, params string[] statuses)
        {
            var list = tasks.Values.Where(t => statuses.Contains(t.status)).ToList();
            var shown = list.Skip(Math.Max(0, list.Count - 2)).Select(t => LoftUtil.Trunc(FirstLine(t.title), 18));
            return $"{label}({list.Count}): {string.Join(" | ", shown)}";
        }
        return $"{Col("ToDo", "queued")}\n{Col("Doing", "running")}\n{Col("Review", "review", "failed")}\n{Col("Done", "approved")}";
    }

    static void SetBoard(string id, string text)
    {
        var c = GameObject.Find($"Board_{id}")?.transform.Find("Content");
        var tm = c != null ? c.GetComponent<TextMesh>() : null;
        if (tm != null) tm.text = text;
    }

    string AgentName(string id) => agents.Find(id)?.GetComponent<AgentSkin>()?.displayName ?? id;
    static string FirstLine(string s) => string.IsNullOrEmpty(s) ? "" : s.Split('\n')[0];

    public void Note(string s)
    {
        eventLog.Add($"{DateTime.Now:HH:mm} {s}");
        if (eventLog.Count > 40) eventLog.RemoveAt(0);
        Debug.Log($"[Desk] {s}");
    }

    // ---- desktop harness: walk/look + shortcut keys ----
    void Update()
    {
        if (XRSettings.isDeviceActive || world == null || world.rig == null) return;
        if (GUIUtility.keyboardControl != 0) return; // typing in the overlay
        var rig = world.rig;
        if (Input.GetMouseButton(1))
        {
            rig.Rotate(0f, Input.GetAxis("Mouse X") * lookSpeed, 0f, Space.World);
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * lookSpeed, -60f, 60f);
            world.cam.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
        var move = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
        if (move.sqrMagnitude > 0.01f)
        {
            var p = rig.position + rig.rotation * move.normalized * walkSpeed * Time.deltaTime;
            rig.position = new Vector3(Mathf.Clamp(p.x, -6f, 6f), rig.position.y, Mathf.Clamp(p.z, 0.2f, 8f));
        }
        if (Input.GetKeyDown(KeyCode.Alpha1)) SpawnAgent("scout");
        if (Input.GetKeyDown(KeyCode.Alpha2)) SpawnAgent("builder");
        if (Input.GetKeyDown(KeyCode.Alpha3)) SpawnAgent("keeper");
        if (showForm && Input.GetKeyDown(KeyCode.Return)) SpawnFromForm();
        if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace)) RemoveSelected();
        if (Input.GetKeyDown(KeyCode.Tab)) SelectNext();
        if (Input.GetKeyDown(KeyCode.T)) NextTheme();
        if (Input.GetKeyDown(KeyCode.P)) Party();
        if (Input.GetKeyDown(KeyCode.Y)) Approve();
        if (Input.GetKeyDown(KeyCode.N)) Reject(null);
        if (Input.GetKeyDown(KeyCode.X)) Stop();
        if (Input.GetKeyDown(KeyCode.Escape)) ExitApp();
    }

    void SpawnForm()
    {
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Name", GUILayout.Width(52));
        formName = GUILayout.TextField(formName);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("Model", GUILayout.Width(52));
        formModel = GUILayout.TextField(formModel);
        GUILayout.EndHorizontal();
        if (string.IsNullOrWhiteSpace(formModel)) GUILayout.Label($"   empty = default: {defaultModel}");
        GUILayout.Label("Animal");
        var animals = new List<string> { "random" };
        animals.AddRange(skins);
        formSkin = animals[Mathf.Max(0, GUILayout.SelectionGrid(Mathf.Max(0, animals.IndexOf(formSkin)), animals.ToArray(), 5))];
        GUILayout.Label("Role (saved)");
        if (roles.Count > 0)
        {
            int cur = formDraft != null ? -1 : roles.FindIndex(r => (string)r["id"] == formRoleId);
            int pick = GUILayout.SelectionGrid(cur, roles.Select(r => (string)r["title"]).ToArray(), 3);
            if (pick != cur && pick >= 0) { formRoleId = (string)roles[pick]["id"]; formDraft = null; }
        }
        GUILayout.Label("…or describe a new role, AI writes it:");
        GUILayout.BeginHorizontal();
        formDescribe = GUILayout.TextField(formDescribe);
        GUI.enabled = !drafting;
        if (GUILayout.Button(drafting ? "writing…" : "Write role", GUILayout.Width(90))) DraftRole(formDescribe);
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        if (formDraft != null)
        {
            GUILayout.Label($"NEW ROLE: {(string)formDraft["title"]}", GUI.skin.box);
            GUILayout.Label(LoftUtil.Trunc((string)formDraft["instructions"], 300));
            GUILayout.Label("Tools (click to grant/remove; ⚠ = changes your project):");
            GUILayout.BeginHorizontal();
            var granted = FormTools();
            foreach (var t in new[] { "read_file", "list_files", "search_files", "fetch_url", "write_file", "edit_file", "run_command" })
            {
                bool on = granted.Contains(t);
                if (GUILayout.Toggle(on, $"{(writeTools.Contains(t) ? "⚠" : "")}{t}") != on) ToggleDraftTool(t);
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.Label(FormSummary());
        if (GUILayout.Button("CREATE AGENT")) SpawnFromForm();
        GUILayout.EndVertical();
    }

    void OnGUI()
    {
        if (XRSettings.isDeviceActive || agents == null) return;
        overlayRect = new Rect(10, 10, 440, Screen.height - 20);
        GUILayout.BeginArea(overlayRect, GUI.skin.box);
        GUILayout.Label($"VIRTUAL LOFT — CEO desk   {(Connected ? "● live" : "○ offline")}   scene: {world?.Current?.name}");
        GUILayout.Label("WASD walk · right-drag look · click animal = select · 1/2/3 quick spawn · Tab next · T scene · P party · Y/N approve/reject · X stop", GUI.skin.box);

        input = GUILayout.TextArea(input, GUILayout.Height(44));
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Send as TEAM GOAL")) Goal(input);
        GUI.enabled = selectedAgentId != null;
        if (GUILayout.Button($"Give to {(selectedAgentId != null ? AgentName(selectedAgentId) : "selected")}")) Assign(selectedAgentId, input);
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.Label($"TEAM {agents.agents.Count}/{maxAgents}");
        foreach (var a in agents.agents.ToArray())
        {
            if (a == null) continue;
            var skin = a.GetComponent<AgentSkin>();
            var label = $"{(a.agentId == selectedAgentId ? "▶ " : "")}{skin.displayName} · {skin.role} · {ShortModel(skin.model)}{(skin.costUsd > 0 ? $" ${skin.costUsd:0.000}" : "")} — {a.CurrentState} {LoftUtil.Trunc(skin.line, 24)}";
            if (GUILayout.Button(label, GUI.skin.label)) Select(a.agentId);
        }
        GUILayout.BeginHorizontal();
        GUI.enabled = agents.agents.Count < maxAgents;
        if (GUILayout.Button(showForm ? "▾ New agent" : "＋ New agent…")) showForm = !showForm;
        GUI.enabled = selectedAgentId != null;
        if (GUILayout.Button("Remove selected")) RemoveSelected();
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        if (showForm) SpawnForm();

        var review = ReviewTask();
        if (review != null)
        {
            GUILayout.Label($"REVIEW — {AgentName(review.agentId)}: {LoftUtil.Trunc(FirstLine(review.title), 40)} [{review.status}]", GUI.skin.box);
            GUILayout.Label(LoftUtil.Trunc(review.result, 400));
            feedback = GUILayout.TextField(feedback);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Approve")) Approve();
            if (GUILayout.Button("Reject + feedback")) { Reject(feedback); feedback = ""; }
            GUILayout.EndHorizontal();
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Stop all")) Stop();
        if (GUILayout.Button("Next scene")) NextTheme();
        if (GUILayout.Button(party != null && party.On ? "End party" : "★ PARTY")) Party();
        if (GUILayout.Button("Exit")) ExitApp();
        GUILayout.EndHorizontal();

        GUILayout.Label("EVENTS");
        for (int i = Math.Max(0, eventLog.Count - 6); i < eventLog.Count; i++) GUILayout.Label(LoftUtil.Trunc(eventLog[i], 56));
        GUILayout.EndArea();
        if (Event.current.type == EventType.MouseDown && !overlayRect.Contains(Event.current.mousePosition))
            GUIUtility.keyboardControl = 0; // click in the world = stop typing
    }
}
