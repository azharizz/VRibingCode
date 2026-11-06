// VIRTUAL LOFT — LoftDesk.cs: the CEO's desk. Glue between the backend and
// the room. It never simulates work:
//   server events (LoftClient.OnEvent) -> animals (AgentManager) + wall boards
//   CEO actions (voice card / wrist menu / keyboard backup) -> LoftClient commands
//   voice answers (LoftIntent) -> ExecuteIntent -> the same commands
//
// Desktop rig (no headset): LoftInteraction runs the same pointing / talk / card /
// wrist code from mouse + keys. WASD walk + right-drag look move the CameraRig.
// F1 shows a small OnGUI debug strip (off by default; OnGUI never renders in a headset).

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
    public LoftInteraction interaction;
    public LoftParty party;
    public LoftPreviewScreen preview;

    [Header("Desktop walk")]
    public float walkSpeed = 2.5f;
    public float lookSpeed = 3f;

    [Serializable]
    public class TaskView { public string id, agentId, title, status, last, result, projectId, diffStat; public JObject raw; }

    public readonly Dictionary<string, TaskView> tasks = new Dictionary<string, TaskView>();
    public readonly List<string> eventLog = new List<string>(40);
    public string selectedAgentId;
    public int maxAgents = AgentManager.MAX_AGENTS;
    public bool Connected => client != null && client.Connected;
    public Rect overlayRect; // LoftInteraction ignores mouse clicks inside the debug strip

    // ---- spawn form (shared by the desktop overlay and the VR console) ----
    // Everything is free: any model id (typed), any saved role or an AI-written
    // one, any animal. Lists come from the server snapshot.
    public readonly List<JObject> roles = new List<JObject>();
    // Projects (git repos the team works on). Goals/tasks go to the current one.
    public readonly List<JObject> projects = new List<JObject>();
    public string projectId;
    public List<string> skins = new List<string> { "fox", "owl", "cat", "bear", "rabbit", "penguin", "frog", "panda" };
    public List<string> writeTools = new List<string> { "write_file", "edit_file", "run_command" };
    public string defaultModel = "";
    public string formName = "", formModel = "", formSkin = "random", formRoleId = "builder", formDescribe = "";
    public JObject formDraft; // AI-written role awaiting CREATE (null = use formRoleId)
    public bool drafting;

    string debugText = "";
    bool showDebug;
    float pitch;
    bool autoCreateDraft; // voice "add a designer": create as soon as the AI-written role is back

    void Awake()
    {
        if (agents == null) agents = FindFirstObjectByType<AgentManager>();
        if (client == null) client = FindFirstObjectByType<LoftClient>();
        if (world == null) world = FindFirstObjectByType<LoftWorld>();
        if (world == null) world = gameObject.AddComponent<LoftWorld>(); // scenes built before themes existed
        if (party == null) party = FindFirstObjectByType<LoftParty>();
        if (party == null) party = gameObject.AddComponent<LoftParty>();
        if (preview == null) preview = FindFirstObjectByType<LoftPreviewScreen>();
        if (preview == null) preview = gameObject.AddComponent<LoftPreviewScreen>();
        projectId = PlayerPrefs.GetString("loft.project", "");
        if (interaction == null) interaction = FindFirstObjectByType<LoftInteraction>();
        if (interaction == null) interaction = gameObject.AddComponent<LoftInteraction>(); // scenes built before the voice card existed
        if (client != null) client.OnEvent += HandleEvent;
        Note($"Connecting to {client?.serverUrl ?? "(no LoftClient)"} …");
    }

    void OnDestroy()
    {
        if (client != null) client.OnEvent -= HandleEvent;
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
                SetProjects(e["projects"] as JArray);
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
            case "projects":
                SetProjects(e["projects"] as JArray);
                break;
            case "party":
                bool on = (bool?)e["on"] ?? false;
                if (party != null && party.On != on) { party.SetParty(on); Note(on ? "PARTY TIME!" : "Party's over — back to work"); }
                break;
            case "goal":
                Note($"GOAL {(string)e["status"]}: {LoftUtil.Trunc((string)e["goal"], 40)} {(string)e["error"]}");
                break;
            case "error":
                Note($"Problem: {(string)e["message"]}");
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
            projectId = (string)t["projectId"],
            diffStat = (string)(t["diff"] as JObject)?["stat"], // JSON null is a JValue, not C# null
            raw = t,
        };
        tasks[id] = v;
        if (log && (old == null || old.status != v.status))
        {
            if (v.status == "approved") LoftAudio.Play("sfx_approve");
            else if (v.status == "rejected") LoftAudio.Play("sfx_reject");
            string who = AgentName(v.agentId);
            string mark = v.status switch
            {
                "queued" => "QUEUED", "running" => "WORKING", "review" => "REVIEW", "approved" => "APPROVED",
                "rejected" => "REDO", "failed" => "FAILED", "stopped" => "STOPPED", "conflict" => "CONFLICT", _ => v.status,
            };
            if (v.status == "approved" && (string)t["merged"] != null) mark = $"MERGED {(string)t["merged"]} ·";
            if (v.status == "conflict") mark += $" ({string.Join(", ", (t["conflicts"] as JArray)?.Select(x => (string)x) ?? new string[0])}) → fixing";
            Note($"{mark} {who}: {LoftUtil.Trunc(FirstLine(v.title), 36)}");
        }
        var skin = agents.Find(v.agentId)?.GetComponent<AgentSkin>();
        if (skin != null && v.status == "running") skin.SetLine(string.IsNullOrEmpty(v.last) ? FirstLine(v.title) : v.last);
        else if (skin != null && old?.status == "running") skin.SetLine(v.status == "review" ? "waiting for CEO review" : v.status);
    }

    void SetProjects(JArray list)
    {
        if (list == null) return;
        projects.Clear();
        foreach (var p in list) if (p is JObject o) projects.Add(o);
        if (!projects.Any(p => (string)p["id"] == projectId) && projects.Count > 0) projectId = (string)projects[0]["id"];
    }

    public string ProjectName => (string)projects.FirstOrDefault(p => (string)p["id"] == projectId)?["name"] ?? projectId ?? "—";

    public void CycleProject()
    {
        if (projects.Count == 0) return;
        int i = projects.FindIndex(p => (string)p["id"] == projectId);
        projectId = (string)projects[(i + 1) % projects.Count]["id"];
        PlayerPrefs.SetString("loft.project", projectId);
        Note($"Project → {ProjectName}");
        RefreshViews();
    }

    public void CreateProject(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        Note($"Creating project {name.Trim()} …");
        client.Then(client.CreateProject(name.Trim()), res =>
        {
            var id = (string)res?["project"]?["id"];
            if (id == null) return;
            projectId = id;
            PlayerPrefs.SetString("loft.project", id);
            Note($"Project {(string)res["project"]["name"]} ready — goals go here now");
        });
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
            bool auto = autoCreateDraft;
            autoCreateDraft = false;
            if (formDraft == null) { if (auto) Note("Could not write that role. Try again."); return; }
            // Voice-created agents only skip the review when the role cannot change files.
            if (auto && !FormTools().Any(writeTools.Contains)) SpawnFromForm();
            else Note($"Role ready: {(string)formDraft["title"]} — open the menu > KEYBOARD to review, then CREATE");
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
               $"{(risky.Count > 0 ? $" (changes files: {string.Join(",", risky)})" : "")} · {formSkin} · " +
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
        Note($"CEO GOAL ({ProjectName}): {LoftUtil.Trunc(text, 34)}");
        _ = client.Goal(text.Trim(), string.IsNullOrEmpty(projectId) ? null : projectId);
    }

    public void Assign(string agentId, string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        if (string.IsNullOrEmpty(agentId) || agents.Find(agentId) == null) { Goal(title); return; }
        _ = client.Assign(agentId, title.Trim(), string.IsNullOrEmpty(projectId) ? null : projectId);
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
        Note(on ? "PARTY TIME!" : "Party's over — back to work");
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

    // ---- voice intents -> the same commands the menu uses --------------------------
    public string LastIntentMessage = "";

    bool Ok(string msg) { LastIntentMessage = msg; Note(msg); return true; }
    bool Fail(string msg) { LastIntentMessage = msg; Note(msg); return false; }

    // Runs one /voice intent. Risky ones (approve, remove, reject, stop-all) refuse
    // unless `confirmed` = a person pressed the separated confirm button.
    public bool ExecuteIntent(JObject intent, bool confirmed = false)
    {
        string action = LoftIntent.Action(intent);
        if (action == null) return Fail("Nothing to do.");
        if (LoftIntent.IsRisky(intent) && !confirmed) return Fail("That needs a button press, not only your voice. Nothing was changed.");
        string S(string key) { var v = (string)intent[key]; return string.IsNullOrWhiteSpace(v) ? null : v.Trim(); }
        switch (action)
        {
            case "goal":
                if (S("goal") == null) return Fail("I did not catch the goal.");
                Goal(S("goal"));
                return Ok($"Goal sent to the team ({ProjectName}).");
            case "task":
            {
                string id = S("agentId") ?? selectedAgentId;
                if (S("title") == null) return Fail("I did not catch the task.");
                if (id == null || agents.Find(id) == null) return Fail("Point at an animal first so I know who gets the task.");
                Assign(id, S("title"));
                return Ok($"Task sent to {AgentName(id)}.");
            }
            case "spawn": return SpawnIntent(intent);
            case "remove":
            {
                string id = S("agentId") ?? selectedAgentId;
                if (id == null || agents.Find(id) == null) return Fail("Point at the animal you want to remove.");
                string who = AgentName(id);
                _ = client.RemoveAgent(id);
                return Ok($"Removing {who}.");
            }
            case "scene": return SetScene(S("sceneId"));
            case "approve":
                if (S("taskId") != null && tasks.ContainsKey(S("taskId"))) { _ = client.Approve(S("taskId")); return Ok("Approved."); }
                if (ReviewTask()?.status != "review") return Fail("Nothing is waiting for approval.");
                Approve();
                return Ok("Approved.");
            case "reject":
                if (S("taskId") != null && tasks.ContainsKey(S("taskId"))) { _ = client.Reject(S("taskId"), S("feedback")); return Ok("Sent back with your feedback."); }
                if (ReviewTask() == null) return Fail("Nothing to reject.");
                Reject(S("feedback"));
                return Ok("Sent back with your feedback.");
            case "stop":
                if (S("agentId") != null && agents.Find(S("agentId")) == null) return Fail("I do not know that animal.");
                Stop(S("agentId"));
                return Ok(S("agentId") == null ? "Stopped everyone." : $"Stopped {AgentName(S("agentId"))}.");
            case "party":
                Party();
                return Ok(party != null && party.On ? "Party time!" : "Party over.");
            case "project": return SetProject(S("projectId"));
            case "selectNext":
                SelectNext();
                return Ok(selectedAgentId == null ? "No animals to select." : $"Selected {AgentName(selectedAgentId)}.");
            case "none":
                return Ok(S("reply") ?? "I did not understand that.");
            default:
                return Fail($"I do not know how to do '{action}'.");
        }
    }

    bool SpawnIntent(JObject intent)
    {
        if (agents.agents.Count >= maxAgents) return Fail($"The team is full ({maxAgents} max).");
        string Opt(string key) { var v = (string)intent[key]; return string.IsNullOrWhiteSpace(v) ? null : v.Trim(); }
        string model = Opt("model") ?? (string.IsNullOrWhiteSpace(formModel) ? null : formModel.Trim());
        string skin = Opt("skin")?.ToLowerInvariant();
        if (skin == null || !skins.Contains(skin)) skin = "random";
        string roleId = Opt("roleId");
        string name = Opt("name"), desc = Opt("description");
        bool known = roleId != null && roles.Any(r => (string)r["id"] == roleId);
        if (known)
        {
            _ = client.Spawn(model, roleId, null, name, skin);
            return Ok($"Adding {name ?? roleId} ({skin}).");
        }
        if (desc != null || roleId != null)
        {
            // No saved role by that name: let the AI write one, then create it (see DraftRole).
            formName = name ?? ""; formModel = model ?? ""; formSkin = skin;
            autoCreateDraft = true;
            DraftRole(desc ?? roleId);
            return Ok($"Writing a role for {LoftUtil.Trunc(desc ?? roleId, 30)}.");
        }
        _ = client.Spawn(model, formRoleId, null, name, skin);
        return Ok($"Adding {name ?? formRoleId} ({skin}).");
    }

    // Scene change with the world's fade.
    public bool SetScene(string id)
    {
        if (world == null) return Fail("The room is not ready.");
        var match = LoftWorld.ThemeIds().FirstOrDefault(t => string.Equals(t, id, StringComparison.OrdinalIgnoreCase));
        if (match == null) return Fail(id == null ? "Which scene?" : $"There is no scene called {id}.");
        world.FadeToTheme(match); // hard ApplyTheme when not playing
        return Ok($"Scene: {match}.");
    }

    public bool SetProject(string id)
    {
        var p = projects.FirstOrDefault(x => string.Equals((string)x["id"], id, StringComparison.OrdinalIgnoreCase)
                                          || string.Equals((string)x["name"], id, StringComparison.OrdinalIgnoreCase));
        if (p == null) return Fail(id == null ? "Which project?" : $"There is no project called {id}.");
        projectId = (string)p["id"];
        PlayerPrefs.SetString("loft.project", projectId);
        RefreshViews();
        return Ok($"Project: {ProjectName}.");
    }

    // ---- wall boards ----
    public void RefreshViews()
    {
        SetBoard("kanban", KanbanText());
        SetBoard("log", string.Join("\n", eventLog.Skip(Math.Max(0, eventLog.Count - 6)).Select(s => LoftUtil.Trunc(s, 34))));
        var r = ReviewTask();
        SetBoard("notes", r == null ? $"Nothing to review yet.\nProject: {ProjectName}"
            : $"{AgentName(r.agentId)} — {r.status.ToUpperInvariant()} · {r.projectId}\n" +
              $"{(string.IsNullOrEmpty(r.diffStat) ? "" : r.diffStat + "\n")}{LoftUtil.Wrap(r.result, 40, string.IsNullOrEmpty(r.diffStat) ? 7 : 6)}");
        if (preview != null) preview.Show(r?.raw);
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
        var tm = c != null ? c.GetComponent<TMPro.TMP_Text>() : null; // TextMeshPro SDF (LoftWorld.BuildBoard)
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

    // ---- desktop rig: walk/look (pointing, talking and turning live in LoftInteraction) ----
    void Update()
    {
        if (XRSettings.isDeviceActive || world == null || world.rig == null) return;
        if (GUIUtility.keyboardControl != 0) return; // typing in the debug strip
        if (Input.GetKeyDown(KeyCode.F1)) showDebug = !showDebug;
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
        if (!showDebug) return; // dev shortcuts only while the strip is up
        if (Input.GetKeyDown(KeyCode.Alpha1)) SpawnAgent("scout");
        if (Input.GetKeyDown(KeyCode.Alpha2)) SpawnAgent("builder");
        if (Input.GetKeyDown(KeyCode.Alpha3)) SpawnAgent("keeper");
        if (Input.GetKeyDown(KeyCode.Tab)) SelectNext();
        if (Input.GetKeyDown(KeyCode.T)) NextTheme();
        if (Input.GetKeyDown(KeyCode.P)) Party();
    }

    void OnGUI()
    {
        if (!showDebug || XRSettings.isDeviceActive || agents == null) { overlayRect = Rect.zero; return; }
        overlayRect = new Rect(8, 8, 560, 128);
        GUILayout.BeginArea(overlayRect, GUI.skin.box);
        GUILayout.Label($"{(Connected ? "● live" : "○ offline")} {client?.serverUrl}  scene {world?.Current?.id}  project {ProjectName}  team {agents.agents.Count}/{maxAgents}   {interaction?.VoiceStatus()}");
        for (int i = Math.Max(0, eventLog.Count - 3); i < eventLog.Count; i++) GUILayout.Label(LoftUtil.Trunc(eventLog[i], 80));
        GUILayout.BeginHorizontal();
        debugText = GUILayout.TextField(debugText);
        if (GUILayout.Button("Say", GUILayout.Width(50)) && interaction != null) { interaction.SendText(debugText); debugText = ""; GUIUtility.keyboardControl = 0; }
        GUILayout.EndHorizontal();
        GUILayout.Label("click=trigger  hold V=A  Esc/C=B  Q/E turn  M menu  right-drag look  WASD  F1 hide");
        GUILayout.EndArea();
        if (Event.current.type == EventType.MouseDown && !overlayRect.Contains(Event.current.mousePosition))
            GUIUtility.keyboardControl = 0; // click in the world = stop typing
    }
}
