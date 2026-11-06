// VIRTUAL LOFT — LoftPanels.cs: the boards in the room. Open and close them, keep
// their layout per scene on the backend, and feed them live data. Boards far away
// or behind the CEO stop redrawing; live previews are capped.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VirtualLoft.Panels;
using VirtualLoft.XR;

public class LoftPanels : MonoBehaviour
{
    public const int MAX_PREVIEWS = 3;
    public const float ACTIVE_RANGE = 12f;
    public static LoftPanels Instance { get; private set; }

    public LoftDesk desk;
    public LoftClient client;
    public LoftWorld world;
    public readonly PanelState state = new PanelState();
    public readonly List<LoftBoard> boards = new List<LoftBoard>();

    string scene, signature = "", reviewId, shownProject;
    int shownMerged = -1;
    float tick, saveAt = -1f, lastFrame, lastInfo;
    bool loading, layoutLoaded, preparing;
    float retryLoadAt = -1f;
    int nextId = 1;

    public void Adopt() { Instance = this; Wire(); } // tests run outside Play, where Awake doesn't

    void Awake()
    {
        Adopt();
        state.project = "";
        if (client != null) client.OnEvent += OnServer;
    }

    void OnDestroy()
    {
        if (client != null) client.OnEvent -= OnServer;
        foreach (var b in boards) ClosePreview(b);
        if (Instance == this) Instance = null;
    }

    void Wire()
    {
        if (desk == null) desk = FindFirstObjectByType<LoftDesk>();
        if (client == null) client = FindFirstObjectByType<LoftClient>();
        if (world == null) world = FindFirstObjectByType<LoftWorld>();
    }

    // ---- what voice and the wrist menu call ----
    public static void Open(string type) { if (Instance != null) Instance.OpenBoard(type); }
    public static void Close(string type)
    {
        if (Instance == null) return;
        foreach (var b in Instance.boards.Where(x => x.type == type).ToArray()) Instance.Remove(b);
        Instance.Save();
    }
    public static void CloseAll()
    {
        if (Instance == null) return;
        foreach (var b in Instance.boards.ToArray()) Instance.Remove(b);
        Instance.Save();
    }
    public static void CloseBoard(LoftBoard b) { if (Instance != null) { Instance.Remove(b); Instance.Save(); } }
    public static void SaveSoon() { if (Instance != null) Instance.saveAt = Time.unscaledTime + 1f; }

    public static bool TryPointer(out Ray r)
    {
        r = default;
        var li = LoftInteraction.Current;
        if (li == null) return false;
        return li.TryRay(out r);
    }

    public static void FaceViewer(Transform t)
    {
        var cam = Instance != null && Instance.world != null ? Instance.world.cam : Camera.main;
        if (cam == null) return;
        var away = t.position - cam.transform.position;
        away.y = 0f;
        if (away.sqrMagnitude > 1e-4f) t.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
    }

    void OpenBoard(string type)
    {
        var existing = boards.FirstOrDefault(b => b.type == type);
        if (type == "preview" && boards.Count(b => b.type == "preview") >= MAX_PREVIEWS) { desk?.Note($"Up to {MAX_PREVIEWS} live previews at once."); return; }
        if (existing != null && type != "notes" && type != "files" && type != "preview" && InView(existing))
        {
            existing.builtVersion = -1; // already in front of you: just refresh
            return;
        }
        var b2 = Spawn($"b{nextId++}_{DateTime.Now.Ticks % 100000}", type);
        Place(b2);
        if (type == "preview") StartPreview(b2);
        if (type == "files") Browse("");
        if (type == "project") LoadProject();
        Save();
    }

    LoftBoard Spawn(string id, string type)
    {
        var b = LoftBoard.Create(id, type, state);
        boards.Add(b);
        return b;
    }

    // First free spot on an arc around the CEO, about 3.2 m away.
    void Place(LoftBoard b)
    {
        var head = world != null && world.cam != null ? world.cam.transform : b.transform;
        var fwd = head.forward; fwd.y = 0f;
        fwd = fwd.sqrMagnitude < 1e-4f ? Vector3.back : fwd.normalized;
        Vector3 spot = head.position + fwd * 3.2f;
        foreach (float a in new[] { 0f, -42f, 42f, -84f, 84f, -126f, 126f, 180f })
        {
            var p = head.position + Quaternion.Euler(0, a, 0) * fwd * 3.2f;
            if (boards.All(o => o == b || Vector3.Distance(o.transform.position, p) > 1.8f)) { spot = p; break; }
        }
        spot.y = 1.0f + b.H * 0.0005f;
        b.transform.position = spot;
        FaceViewer(b.transform);
    }

    void Remove(LoftBoard b)
    {
        ClosePreview(b);
        boards.Remove(b);
        if (b == null) return;
        b.gameObject.SetActive(false);
        LoftUtil.Kill(b.gameObject);
    }

    bool InView(LoftBoard b)
    {
        var cam = world != null ? world.cam : null;
        if (cam == null) return true;
        var to = b.transform.position - cam.transform.position;
        return to.magnitude < ACTIVE_RANGE && Vector3.Dot(cam.transform.forward, to.normalized) > 0.05f;
    }

    // ---- layout per scene ----
    void Save()
    {
        saveAt = -1f;
        if (client == null || string.IsNullOrEmpty(scene) || !layoutLoaded) return; // never overwrite a layout we failed to read
        var list = new JArray();
        foreach (var b in boards)
        {
            var p = b.transform.position; var r = b.transform.eulerAngles; float s = b.transform.localScale.x;
            list.Add(new JObject { ["id"] = b.id, ["type"] = b.type, ["pos"] = new JArray(p.x, p.y, p.z), ["rot"] = new JArray(r.x, r.y, r.z), ["size"] = new JArray(s, s, 1f), ["args"] = string.IsNullOrEmpty(b.startUrl) ? new JObject() : new JObject { ["url"] = b.startUrl } });
        }
        _ = client.SaveBoards(scene, list);
    }

    void LoadScene(string id)
    {
        scene = id;
        layoutLoaded = false;
        foreach (var b in boards.ToArray()) Remove(b); // the old scene's layout is already saved
        if (client == null) return;
        loading = true;
        client.Then(client.GetBoards(id), res =>
        {
            loading = false;
            if (scene != id) return;
            if (!(res?["boards"] is JArray list)) { retryLoadAt = Time.unscaledTime + 3f; return; }
            layoutLoaded = true;
            foreach (var o in list.OfType<JObject>())
            {
                var type = (string)o["type"];
                if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty((string)o["id"])) continue;
                var b = Spawn((string)o["id"], type);
                b.transform.position = V(o["pos"], b.transform.position);
                b.transform.rotation = Quaternion.Euler(V(o["rot"], Vector3.zero));
                float s = o["size"] is JArray a && a.Count > 0 ? Mathf.Clamp((float?)a[0] ?? 1f, LoftBoard.MIN_SCALE, LoftBoard.MAX_SCALE) : 1f;
                b.transform.localScale = Vector3.one * s;
                b.startUrl = (string)(o["args"] as JObject)?["url"];
                if (type == "preview") StartPreview(b);
                if (type == "files") Browse(state.treePath ?? "");
                if (type == "project") LoadProject();
            }
        });
    }

    static Vector3 V(JToken t, Vector3 d) => t is JArray a && a.Count >= 3 ? new Vector3((float?)a[0] ?? d.x, (float?)a[1] ?? d.y, (float?)a[2] ?? d.z) : d;

    // ---- live data ----
    void OnServer(JObject e)
    {
        var type = (string)e["type"];
        if (type == "notes" || type == "snapshot") LoadNotes();
    }

    void Update()
    {
        if (desk == null || world == null) { Wire(); return; }
        if (world.Current != null && world.Current.id != scene && client != null && client.Connected) LoadScene(world.Current.id);
        if (retryLoadAt > 0f && Time.unscaledTime >= retryLoadAt && client.Connected) { retryLoadAt = -1f; LoadScene(scene); }
        if (saveAt > 0f && Time.unscaledTime >= saveAt && !loading) Save();
        tick += Time.unscaledDeltaTime;
        if (tick >= 0.5f) { tick = 0f; Pull(); }
        foreach (var b in boards) if (b.builtVersion != state.version && InView(b)) b.Refresh();
        if (Time.unscaledTime - lastFrame >= 0.08f) { lastFrame = Time.unscaledTime; PullFrames(); } // one request in flight per board
        if (Time.unscaledTime - lastInfo >= 1f) { lastInfo = Time.unscaledTime; PullAddresses(); }
    }

    // LoftDesk state -> PanelState, bumping the version only when something changed.
    public void Pull()
    {
        var sig = new StringBuilder();
        state.connected = desk.Connected;
        state.autoApprove = desk.autoApprove;
        state.workspaceId = desk.projectId;
        state.workspaces.Clear();
        foreach (var w in desk.projects) state.workspaces.Add(new KeyValuePair<string, string>((string)w["id"], (string)w["name"] ?? (string)w["id"]));
        sig.Append(desk.projectId).Append(desk.projects.Count);
        sig.Append(desk.autoApprove ? 'a' : 'm');
        var project = desk.ProjectName;
        if (project != state.project) { state.project = project; state.tree.Clear(); state.treePath = ""; state.openFilePath = null; state.openFileText = null; sig.Append('p'); }
        if (desk.projectId != shownProject) { shownProject = desk.projectId; if (boards.Any(b => b.type == "files")) Browse(""); if (boards.Any(b => b.type == "project")) LoadProject(); }

        state.tasks.Clear();
        foreach (var t in desk.tasks.Values)
        {
            var skin = desk.agents != null ? desk.agents.Find(t.agentId)?.GetComponent<AgentSkin>() : null;
            var diff = t.raw?["diff"] as JObject;
            ParseStat(t.diffStat, out int ins, out int del);
            var row = new PTask
            {
                id = t.id, title = FirstLine(t.title), brief = t.title, plain = (string)t.raw?["plain"], status = t.status, agentId = t.agentId, agentName = skin != null ? skin.displayName : t.agentId, role = skin != null ? skin.role : "",
                last = t.last, result = t.result, projectId = t.projectId, stat = t.diffStat,
                files = ((diff?["files"] as JArray)?.Count ?? 0) + ((diff?["ignored"] as JArray)?.Count ?? 0), added = ins, removed = del, progress = t.status == "running" ? Mathf.Repeat(Time.time / 40f, 0.9f) + 0.05f : 0f,
            };
            state.tasks.Add(row);
            sig.Append(t.id).Append(t.status).Append(t.result?.Length ?? 0).Append(t.diffStat).Append(row.plain?.Length ?? 0).Append('|');
        }

        state.log.Clear();
        for (int i = desk.eventLog.Count - 1; i >= 0 && state.log.Count < 14; i--) state.log.Add(ToLog(desk.eventLog[i]));
        sig.Append(desk.eventLog.Count).Append(desk.eventLog.Count > 0 ? desk.eventLog[desk.eventLog.Count - 1] : "");

        var s = sig.ToString();
        if (s != signature)
        {
            signature = s; state.Touch();
            if (boards.Any(b => b.type == "project")) LoadProject();
        }
        int merged = state.tasks.Count(t => t.status == "approved");
        if (merged != shownMerged) // merged work shows up without reopening
        {
            shownMerged = merged;
            if (boards.Any(b => b.type == "files")) Browse(state.treePath ?? "");
        }

        var review = state.tasks.FirstOrDefault(t => t.status == "review" && state.Here(t));
        if (review?.id != reviewId)
        {
            reviewId = review?.id;
            if (reviewId != null)
            {
                LoadDiff(reviewId);
                if (!desk.autoApprove && review.files > 0 && !boards.Any(b => b.type == "review")) OpenBoard("review"); // finished work comes to you
            }
            else { state.diff.Clear(); state.Touch(); }
        }
    }

    PLog ToLog(string line)
    {
        string time = line.Length > 5 && line[2] == ':' ? line.Substring(0, 5) : "";
        string text = time.Length > 0 ? line.Substring(6) : line;
        string who = "Loft", kind = "info";
        if (desk.agents != null)
            foreach (var a in desk.agents.agents)
            {
                if (a == null) continue;
                var name = a.GetComponent<AgentSkin>()?.displayName;
                if (!string.IsNullOrEmpty(name) && text.Contains(name)) { who = name; break; }
            }
        if (text.StartsWith("CEO") || text.StartsWith("GOAL")) who = "You";
        if (text.StartsWith("WORKING")) kind = "running";
        else if (text.StartsWith("REVIEW")) kind = "review";
        else if (text.StartsWith("APPROVED") || text.StartsWith("MERGED")) kind = "approved";
        else if (text.StartsWith("FAILED") || text.StartsWith("Problem")) kind = "failed";
        return new PLog { time = time, who = who, text = text, kind = kind };
    }

    // "2 files changed, 12 insertions(+), 3 deletions(-)"
    static void ParseStat(string s, out int ins, out int del)
    {
        ins = del = 0;
        if (string.IsNullOrEmpty(s)) return;
        foreach (var part in s.Split(','))
        {
            var bits = part.Trim().Split(' ');
            if (bits.Length < 2 || !int.TryParse(bits[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) continue;
            if (bits[1].StartsWith("insertion")) ins = n;
            else if (bits[1].StartsWith("deletion")) del = n;
        }
    }

    static string FirstLine(string s) => string.IsNullOrEmpty(s) ? "" : s.Split('\n')[0];

    void LoadNotes()
    {
        if (client == null) return;
        client.Then(client.GetNotes(), res =>
        {
            if (!(res?["notes"] is JArray list)) return;
            state.notes.Clear();
            string[] tones = { "yellow", "pink", "mint", "blue" };
            int i = 0;
            foreach (var n in list.Reverse().OfType<JObject>())
            {
                var text = (string)n["text"]; if (string.IsNullOrEmpty(text)) text = (string)n["title"];
                var who = (string)n["agentId"];
                var skin = who != null && desk?.agents != null ? desk.agents.Find(who)?.GetComponent<AgentSkin>() : null;
                var col = (string)n["color"];
                state.notes.Add(new PNote { id = (string)n["id"], text = text, agentName = skin != null ? skin.displayName : "You", color = string.IsNullOrEmpty(col) ? tones[i % 4] : col });
                i++;
            }
            state.Touch();
        });
    }

    public static void AddNote()
    {
        var p = Instance; if (p == null) return;
        var entry = FindFirstObjectByType<LoftTextEntry>();
        if (entry == null || !entry.Request("New note", "", t => { if (!string.IsNullOrWhiteSpace(t)) p.desk.Report(p.client.AddNote(t.Trim()), "That note was not saved."); }, out string why))
            p.desk?.Note("Can't open the keyboard here.");
    }
    public static void SwitchWorkspace(string id) => Instance?.desk.SetProject(id);
    public static void OpenFolder() => AskText("Folder path, like ~/code/site", "~/", t => Instance.desk.OpenFolder(t));
    public static void NewWorkspace() => AskText("New workspace name", "", t => Instance.desk.CreateProject(t));

    static void AskText(string hint, string start, Action<string> done)
    {
        var entry = FindFirstObjectByType<LoftTextEntry>();
        if (Instance == null || entry == null) return;
        if (!entry.Request(hint, start, t => { if (!string.IsNullOrWhiteSpace(t)) done(t); }, out string why)) Instance.desk?.Note(why);
    }

    // A finished task's whole report, on the Review board.
    public static void ReadReport(string taskId)
    {
        Open("review");
        var b = Instance?.boards.LastOrDefault(x => x.type == "review");
        if (b == null) return;
        b.readingId = taskId; b.scroll = 0; b.Refresh();
    }

    void LoadProject()
    {
        if (string.IsNullOrEmpty(desk?.projectId)) return;
        client.Then(client.GetProject(desk.projectId), res =>
        {
            if (res == null) return;
            state.projectSite = (bool?)res["site"] ?? false;
            state.latestChange = (string)res["latest"]?["title"];
            state.latestWhen = (string)res["latest"]?["when"];
            state.Touch();
        });
    }

    public static void DeleteNote(string id) { if (Instance != null) _ = Instance.client.DeleteNote(id); }
    public static void Approve(string taskId) { if (Instance != null) { Instance.desk.Report(Instance.client.Approve(taskId), "That could not be approved."); LoftAudio.Play("sfx_approve"); } }
    public static void SetAutoApprove(bool on) { if (Instance != null) Instance.desk.Report(Instance.client.AutoApprove(on), "Auto-approve could not be changed."); }
    public static void Reject(string taskId) { if (Instance != null) Instance.desk.Report(Instance.client.Reject(taskId, null), "That could not be sent back."); }

    // The changes of the task on the Review board's current page.
    public static void ShowChanges(string taskId)
    {
        if (Instance == null) return;
        Instance.LoadDiff(taskId);
        Open("diff");
    }

    string diffId;
    void LoadDiff(string taskId)
    {
        diffId = taskId;
        client.Then(client.GetDiff(taskId), res =>
        {
            if (taskId != diffId || !(res?["files"] is JArray files)) return;
            state.diff.Clear();
            foreach (var f in files.OfType<JObject>())
            {
                var d = new PDiffFile { path = (string)f["path"], added = (int?)f["added"] ?? 0, removed = (int?)f["removed"] ?? 0 };
                foreach (var h in (f["hunks"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    d.lines.Add(new PHunkLine { type = '@', text = (string)h["header"] });
                    foreach (var l in (h["lines"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        var ty = (string)l["type"];
                        d.lines.Add(new PHunkLine { type = ty == "add" ? '+' : ty == "del" ? '-' : ' ', text = (string)l["text"] });
                    }
                }
                state.diff.Add(d);
            }
            state.Touch();
        });
    }

    public static void Browse(string path)
    {
        var p = Instance; if (p == null || p.client == null || string.IsNullOrEmpty(p.desk?.projectId)) return;
        p.client.Then(p.client.GetTree(p.desk.projectId, path), res =>
        {
            if (!(res?["entries"] is JArray list)) return;
            p.state.treePath = (string)res["path"] ?? "";
            p.state.tree.Clear();
            foreach (var e in list.OfType<JObject>())
            {
                var name = (string)e["name"];
                p.state.tree.Add(new PFile { name = name, dir = (string)e["type"] == "dir", path = string.IsNullOrEmpty(p.state.treePath) ? name : p.state.treePath + "/" + name });
            }
            p.state.Touch();
        });
    }
    public static void BrowseUp()
    {
        var p = Instance; if (p == null) return;
        var t = p.state.treePath ?? "";
        int cut = t.LastIndexOf('/');
        Browse(cut < 0 ? "" : t.Substring(0, cut));
    }
    public static void OpenFile(string path)
    {
        var p = Instance; if (p == null) return;
        p.client.Then(p.client.GetFile(p.desk.projectId, path), res =>
        {
            if (res?["content"] == null) { p.desk?.Note("Couldn't open that file."); return; }
            p.state.openFilePath = path;
            p.state.openFileText = (bool?)res["binary"] == true ? "(binary file)" : (string)res["content"] ?? "";
            p.state.Touch();
        });
    }

    // ---- browser ----
    // A saved address first, then the task in review, else the board waits for an address.
    void StartPreview(LoftBoard b)
    {
        if (!string.IsNullOrEmpty(b.startUrl)) { Connect(b, client.OpenWeb(b.startUrl), b.startUrl); return; }
        if (reviewId == null) { b.previewError = null; b.Refresh(); return; }
        Connect(b, client.OpenPreview(reviewId), "Task preview");
    }

    void Connect(LoftBoard b, System.Threading.Tasks.Task<JObject> open, string label)
    {
        client.Then(open, res =>
        {
            if (b == null) return;
            ClosePreview(b);
            b.previewId = (string)res?["id"];
            b.previewUrl = label;
            b.previewError = b.previewId == null ? "The page did not open. Check the address, and that the backend has Chromium." : null;
            if (b.previewId != null) previewSize[b] = new Vector2((float?)res["width"] ?? 1360f, (float?)res["height"] ?? 800f);
            b.Refresh();
        });
    }

    // Voice "open youtube.com": reuse an open browser, else open one.
    public static void OpenWeb(string url)
    {
        var p = Instance; if (p == null || string.IsNullOrWhiteSpace(url)) return;
        var b = p.boards.FirstOrDefault(x => x.type == "preview");
        if (b != null && !string.IsNullOrEmpty(b.previewId)) { Go(b, url); return; }
        if (b == null)
        {
            if (p.boards.Count(x => x.type == "preview") >= MAX_PREVIEWS) return;
            b = p.Spawn($"b{p.nextId++}_{DateTime.Now.Ticks % 100000}", "preview");
            p.Place(b);
        }
        b.startUrl = url;
        p.StartPreview(b);
        p.Save();
    }

    // Voice "preview our latest work": the task in review here if there is one, else what is merged.
    public static void OpenLatest() => Show(Instance?.reviewId);
    public static void OpenSite() => Show(null);
    public static void TryTask(string taskId) => Show(taskId);

    static void Show(string taskId)
    {
        var p = Instance; if (p == null) return;
        var b = p.boards.FirstOrDefault(x => x.type == "preview");
        if (b == null)
        {
            b = p.Spawn($"b{p.nextId++}_{DateTime.Now.Ticks % 100000}", "preview");
            p.Place(b);
        }
        b.startUrl = null;
        if (taskId != null) p.Connect(b, p.client.OpenPreview(taskId), "Task preview");
        else p.Connect(b, p.client.OpenLatest(p.desk?.projectId), "Latest work");
        p.Save();
    }

    static void Go(LoftBoard b, string url)
    {
        var p = Instance; if (p == null) return;
        bool locked = string.IsNullOrEmpty(b.startUrl); // a task or latest preview can't leave its page: new session
        b.startUrl = url;
        if (locked || string.IsNullOrEmpty(b.previewId)) { p.StartPreview(b); return; }
        _ = p.client.PreviewInput(b.previewId, new JObject { ["type"] = "goto", ["url"] = url });
        SaveSoon();
    }

    public static void PreviewGo(LoftBoard b)
    {
        var entry = FindFirstObjectByType<LoftTextEntry>();
        if (entry != null) entry.Request("Address or search", "", t => { if (!string.IsNullOrWhiteSpace(t)) Go(b, t.Trim()); }, out _);
    }

    public static void PreviewNav(LoftBoard b, string type)
    {
        var p = Instance; if (p == null || string.IsNullOrEmpty(b.previewId)) return;
        _ = p.client.PreviewInput(b.previewId, new JObject { ["type"] = type });
    }

    readonly Dictionary<LoftBoard, Vector2> previewSize = new Dictionary<LoftBoard, Vector2>();
    readonly HashSet<LoftBoard> fetching = new HashSet<LoftBoard>();

    void ClosePreview(LoftBoard b)
    {
        if (b == null || string.IsNullOrEmpty(b.previewId)) return;
        if (client != null) _ = client.ClosePreview(b.previewId);
        if (b.previewTex != null) Destroy(b.previewTex);
        b.previewTex = null;
        b.previewId = null;
        previewSize.Remove(b);
    }

    void PullFrames()
    {
        foreach (var b in boards)
        {
            if (b.type != "preview" || string.IsNullOrEmpty(b.previewId) || b.frame == null || !InView(b) || fetching.Contains(b)) continue;
            fetching.Add(b);
            var board = b;
            client.ThenBytes(client.GetBytes($"/preview/sessions/{board.previewId}/frame"), bytes =>
            {
                fetching.Remove(board);
                if (board == null || board.frame == null || bytes == null) return;
                if (board.previewTex == null) board.previewTex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (bytes.Length == board.frameBytes) return; // same picture: skip the decode
                board.frameBytes = bytes.Length;
                if (!board.previewTex.LoadImage(bytes)) return;
                board.frame.texture = board.previewTex;
                board.frame.color = Color.white;
            });
        }
    }

    void PullAddresses()
    {
        foreach (var b in boards)
        {
            if (b.type != "preview" || string.IsNullOrEmpty(b.previewId) || !InView(b)) continue;
            var board = b;
            client.Then(client.PreviewInfo(board.previewId), res =>
            {
                var video = (res?["videos"] as JArray)?.FirstOrDefault()?.ToString();
                if (board != null && video != board.videoUrl) { board.videoUrl = video; board.Refresh(); }
                var url = (string)res?["url"];
                if (board == null || string.IsNullOrEmpty(url) || url == board.previewUrl) return;
                board.previewUrl = url;
                if (url.StartsWith("http") && !url.Contains("127.0.0.1")) { board.startUrl = url; SaveSoon(); }
                if (board.address != null) board.address.text = url;
            });
        }
    }

    // ---- watch in VR ----
    public static bool WatchCurrent()
    {
        var b = Instance?.boards.FirstOrDefault(x => x.type == "preview" && !string.IsNullOrEmpty(x.videoUrl));
        if (b == null) return false;
        Watch(b);
        return true;
    }

    public static void Watch(LoftBoard b)
    {
        var p = Instance; if (p == null || string.IsNullOrEmpty(b.videoUrl) || p.preparing) return;
        p.preparing = true;
        p.client.Then(p.client.PrepareVideo(b.videoUrl), res => p.StartCoroutine(p.WaitForVideo(b, res)));
    }

    IEnumerator WaitForVideo(LoftBoard b, JObject job)
    {
        void Say(string s) { if (b != null && b.watchLabel != null) b.watchLabel.text = s; }
        var id = (string)job?["id"];
        while (job != null && (string)job["status"] != "ready" && (string)job["status"] != "error")
        {
            Say($"Preparing {Mathf.RoundToInt(((float?)job["progress"] ?? 0f) * 100f)}%");
            yield return new WaitForSecondsRealtime(1f);
            JObject next = null; bool done = false;
            client.Then(client.VideoStatus(id), r => { next = r; done = true; });
            while (!done) yield return null;
            job = next;
        }
        preparing = false;
        if ((string)job?["status"] != "ready")
        {
            Say("Could not prepare it");
            desk?.Note("Video failed: " + ((string)job?["error"] ?? "the backend did not answer"));
            yield break;
        }
        Say("Watch in VR");
        LoftInteraction.Current?.cinema.Enter(client.serverUrl.TrimEnd('/') + (string)job["video"]);
    }

    public static void PreviewClick(LoftBoard b, Vector2 uv)
    {
        var p = Instance; if (p == null || !p.previewSize.TryGetValue(b, out var size)) return;
        _ = p.client.PreviewInput(b.previewId, new JObject { ["type"] = "click", ["x"] = uv.x * size.x, ["y"] = uv.y * size.y });
    }
    public static void PreviewScroll(LoftBoard b, int dy)
    {
        var p = Instance; if (p == null || string.IsNullOrEmpty(b.previewId)) return;
        _ = p.client.PreviewInput(b.previewId, new JObject { ["type"] = "scroll", ["dy"] = dy });
    }
    public static void PreviewKey(LoftBoard b, string key)
    {
        var p = Instance; if (p == null || string.IsNullOrEmpty(b.previewId)) return;
        _ = p.client.PreviewInput(b.previewId, new JObject { ["type"] = "key", ["key"] = key });
    }
    public static void PreviewType(LoftBoard b)
    {
        var p = Instance; if (p == null || string.IsNullOrEmpty(b.previewId)) return;
        var entry = FindFirstObjectByType<LoftTextEntry>();
        if (entry != null) entry.Request("Type into the page", "", t =>
        {
            if (string.IsNullOrEmpty(t)) return;
            _ = p.client.PreviewInput(b.previewId, new JObject { ["type"] = "text", ["text"] = t });
            PreviewKey(b, "Enter");
        }, out _);
    }
}
