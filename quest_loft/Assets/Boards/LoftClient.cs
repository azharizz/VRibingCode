// VIRTUAL LOFT — LoftClient.cs: Unity <-> backend/src/server.js.
//
// Commands go out as small HTTP calls (spawn/assign/goal/approve/...); ALL
// state comes back through the /events Server-Sent-Events stream and is
// raised as OnEvent on the main thread. Unity never fakes progress.
//
// Server address: inspector `serverUrl`/`token`, or on a headset push
// {"url":"http://192.168.1.20:4101","token":"..."} to
// <Application.persistentDataPath>/loft-server.json (see backend/README.md).
// System.Net.Http only — no engine-module dependency.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public partial class LoftClient : MonoBehaviour
{
    public string serverUrl = "http://127.0.0.1:4101";
    [Tooltip("LOFT_TOKEN from backend/.env (required when the server listens on the LAN).")]
    public string token = "";
    [Tooltip("Room id for the board layout store.")]
    public string roomId = "focus-loft";
    public BoardManager boards;

    // Per-call budget for commands (goal planning can take a while).
    public static int timeoutSeconds = 120;

    public bool Connected => connected;
    public event Action<JObject> OnEvent;

    // No HttpClient timeout: the SSE stream is open forever; commands use their own CTS.
    static readonly HttpClient http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    readonly ConcurrentQueue<JObject> inbox = new ConcurrentQueue<JObject>();
    readonly ConcurrentQueue<Action> mainThread = new ConcurrentQueue<Action>();
    CancellationTokenSource streamCts;
    volatile bool connected;

    void Awake()
    {
        if (boards == null) boards = FindFirstObjectByType<BoardManager>();
        LoadOverride();
    }

    void OnEnable() => StartEvents();
    void OnDisable() => StopEvents();

    // Same for any result type (voice replies).
    public void ThenAny<T>(Task<T> task, Action<T> onMain)
    {
        task.ContinueWith(t => mainThread.Enqueue(() => onMain(t.Status == TaskStatus.RanToCompletion ? t.Result : default)));
    }

    // Run onMain(result) on Unity's main thread once the request finishes (null on failure).
    public void Then(Task<JObject> task, Action<JObject> onMain)
    {
        task.ContinueWith(t => mainThread.Enqueue(() => onMain(t.Status == TaskStatus.RanToCompletion ? t.Result : null)));
    }

    void Update()
    {
        while (mainThread.TryDequeue(out var act))
        {
            try { act(); }
            catch (Exception ex) { Debug.LogException(ex); }
        }
        while (inbox.TryDequeue(out var e))
        {
            try { OnEvent?.Invoke(e); }
            catch (Exception ex) { Debug.LogException(ex); }
        }
    }

    void LoadOverride()
    {
        try
        {
            var p = Path.Combine(Application.persistentDataPath, "loft-server.json");
            if (!File.Exists(p)) return;
            var o = JObject.Parse(File.ReadAllText(p));
            serverUrl = (string)o["url"] ?? serverUrl;
            token = (string)o["token"] ?? token;
            Debug.Log($"[LoftClient] server from loft-server.json: {serverUrl}");
        }
        catch (Exception e) { Debug.LogWarning($"[LoftClient] loft-server.json ignored: {e.Message}"); }
    }

    // ---- live events (SSE) ----

    public void StartEvents()
    {
        StopEvents();
        streamCts = new CancellationTokenSource();
        _ = EventLoop(streamCts.Token);
    }

    public void StopEvents()
    {
        streamCts?.Cancel();
        streamCts = null;
        connected = false;
    }

    async Task EventLoop(CancellationToken ct)
    {
        int backoff = 1;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var req = Request(HttpMethod.Get, "/events");
                using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                res.EnsureSuccessStatusCode();
                using var stream = await res.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                using var reg = ct.Register(() => res.Dispose()); // unblocks ReadLineAsync on stop
                connected = true;
                backoff = 1;
                string line;
                while (!ct.IsCancellationRequested && (line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    if (!line.StartsWith("data: ")) continue; // ": ping" keep-alives, blank separators
                    try { inbox.Enqueue(JObject.Parse(line.Substring(6))); }
                    catch (JsonException) { }
                }
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                Debug.LogWarning($"[LoftClient] event stream: {e.Message}");
            }
            catch (Exception) { }
            if (connected) inbox.Enqueue(new JObject { ["type"] = "disconnected" });
            connected = false;
            if (ct.IsCancellationRequested) break;
            try { await Task.Delay(backoff * 1000, ct).ConfigureAwait(false); }
            catch (Exception) { break; }
            backoff = Math.Min(backoff * 2, 10);
        }
    }

    // ---- commands (fire-and-forget friendly: never throw, null on failure) ----

    // Free-form agent: any model id, a saved role (roleId) OR a role object, any animal ("random" ok).
    public Task<JObject> Spawn(string model, string roleId, JObject role = null, string name = null, string skin = null)
    {
        var body = Body("model", model, "roleId", role == null ? roleId : null, "name", name, "skin", skin);
        if (role != null) body["role"] = role;
        return Send(HttpMethod.Post, "/agents", body);
    }
    public Task<JObject> DraftRole(string description) =>
        Send(HttpMethod.Post, "/roles/draft", Body("description", description));
    public Task<JObject> SaveRole(JObject role) => Send(HttpMethod.Post, "/roles", role);
    public Task<JObject> Party(bool on) => Send(HttpMethod.Post, "/party", new JObject { ["on"] = on });
    public Task<JObject> RemoveAgent(string agentId) =>
        Send(HttpMethod.Delete, "/agents/" + Uri.EscapeDataString(agentId ?? ""), null);
    public Task<JObject> Assign(string agentId, string title, string projectId = null) =>
        Send(HttpMethod.Post, "/tasks", Body("agentId", agentId, "title", title, "projectId", projectId));
    public Task<JObject> Goal(string goal, string projectId = null) =>
        Send(HttpMethod.Post, "/goal", Body("goal", goal, "projectId", projectId));
    public Task<JObject> Chat(string agentId, string text) =>
        Send(HttpMethod.Post, $"/agents/{Uri.EscapeDataString(agentId ?? "")}/chat", Body("text", text));
    public Task<JObject> AnswerSuggestion(string id, bool accept) =>
        Send(HttpMethod.Post, $"/suggestions/{Uri.EscapeDataString(id ?? "")}/{(accept ? "accept" : "dismiss")}", null);
    public Task<JObject> CreateProject(string name, string template = "static") =>
        Send(HttpMethod.Post, "/projects/new", Body("name", name, "template", template));

    // Raw bytes (preview screenshots). null on failure.
    public async Task<byte[]> GetBytes(string path)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
            using var req = Request(HttpMethod.Get, path);
            using var res = await http.SendAsync(req, cts.Token).ConfigureAwait(false);
            return res.IsSuccessStatusCode ? await res.Content.ReadAsByteArrayAsync().ConfigureAwait(false) : null;
        }
        catch (Exception e) { Debug.LogWarning($"[LoftClient] GET {path} failed: {e.Message}"); return null; }
    }

    // Run onMain(bytes) on the main thread (Texture2D.LoadImage must be main-thread).
    public void ThenBytes(Task<byte[]> task, Action<byte[]> onMain) =>
        task.ContinueWith(t => mainThread.Enqueue(() => onMain(t.Status == TaskStatus.RanToCompletion ? t.Result : null)));
    public Task<JObject> Approve(string taskId) =>
        Send(HttpMethod.Post, $"/tasks/{Uri.EscapeDataString(taskId ?? "")}/approve", null);
    public Task<JObject> Reject(string taskId, string feedback) =>
        Send(HttpMethod.Post, $"/tasks/{Uri.EscapeDataString(taskId ?? "")}/reject", Body("feedback", feedback));
    public Task<JObject> Stop(string agentId = null) =>
        Send(HttpMethod.Post, "/stop", Body("agentId", agentId));

    // ---- voice (POST /voice with a WAV, POST /voice/text for typed phrases) ----
    // Never throws: either `json` (the contract's {transcript, summary, needsConfirm, risky, intent}) or `error`
    // in words the CEO can act on.
    public class VoiceReply { public JObject json; public string error; }
    public static int voiceTimeoutSeconds = 60; // speech-to-text + intent can take a while

    public Task<VoiceReply> Voice(byte[] wav, string agentId, string projectId, string scene) =>
        PostVoice("/voice" + Query(agentId, projectId, scene), new ByteArrayContent(wav ?? new byte[0]) { Headers = { ContentType = new MediaTypeHeaderValue("audio/wav") } });

    public Task<VoiceReply> VoiceText(string text, string agentId, string projectId, string scene)
    {
        var body = new JObject { ["text"] = text };
        if (!string.IsNullOrEmpty(agentId)) body["agentId"] = agentId;
        if (!string.IsNullOrEmpty(projectId)) body["projectId"] = projectId;
        if (!string.IsNullOrEmpty(scene)) body["scene"] = scene;
        return PostVoice("/voice/text", new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json"));
    }

    static string Query(string agentId, string projectId, string scene)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrEmpty(agentId)) parts.Add("agentId=" + Uri.EscapeDataString(agentId));
        if (!string.IsNullOrEmpty(projectId)) parts.Add("projectId=" + Uri.EscapeDataString(projectId));
        if (!string.IsNullOrEmpty(scene)) parts.Add("scene=" + Uri.EscapeDataString(scene));
        return parts.Count == 0 ? "" : "?" + string.Join("&", parts);
    }

    async Task<VoiceReply> PostVoice(string path, HttpContent content)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, voiceTimeoutSeconds)));
            using var req = Request(HttpMethod.Post, path);
            req.Content = content;
            using var res = await http.SendAsync(req, cts.Token).ConfigureAwait(false);
            var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            JObject json = null;
            try { json = string.IsNullOrEmpty(text) ? null : JObject.Parse(text); } catch (JsonException) { }
            if (res.IsSuccessStatusCode)
                return json != null ? new VoiceReply { json = json } : new VoiceReply { error = "The backend answered with something I could not read." };
            return new VoiceReply { error = VoiceHttpError((int)res.StatusCode, (string)json?["error"] ?? text) };
        }
        catch (OperationCanceledException) { return new VoiceReply { error = "The backend took too long to understand that. Try again." }; }
        catch (Exception e)
        {
            Debug.LogWarning($"[LoftClient] POST {path} failed: {e.Message}");
            return new VoiceReply { error = $"Can't reach the backend at {serverUrl}. Is it running, and is this the Mac's Wi-Fi address?" };
        }
    }

    public static string VoiceHttpError(int status, string detail)
    {
        string d = string.IsNullOrWhiteSpace(detail) ? "" : " (" + detail.Trim() + ")";
        if (status == 401 || status == 403) return "The backend refused the token. Check LOFT_TOKEN in loft-server.json.";
        if (status == 404) return "This backend has no voice endpoint yet. Update and restart it.";
        if (status == 413) return "That recording was too long. Say it in a shorter piece.";
        if (status == 501 || status == 503) return "The backend can't transcribe speech right now" + d + ".";
        return $"The backend had a problem (HTTP {status}){d}.";
    }

    public async Task<bool> CheckHealthAsync() => await Send(HttpMethod.Get, "/health", null, quiet: true).ConfigureAwait(false) != null;

    public async Task<bool> LoadFromServerAsync(string id)
    {
        var res = await Send(HttpMethod.Get, $"/loft/{Uri.EscapeDataString(id)}/boards", null, quiet: true).ConfigureAwait(false);
        if (res == null || boards == null) return false;
        boards.Load(res.ToString(Formatting.None));
        return true;
    }
    public Task<bool> LoadFromServerAsync() => LoadFromServerAsync(roomId);

    public async Task<bool> SaveToServerAsync()
    {
        if (boards == null) return false;
        var body = new JObject { ["roomId"] = roomId, ["snapshot"] = JObject.Parse(boards.Save()) };
        return await Send(HttpMethod.Post, "/loft", body, quiet: true).ConfigureAwait(false) != null;
    }

    public async Task<JObject> Send(HttpMethod method, string path, JObject body, bool quiet = false)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
            using var req = Request(method, path);
            if (body != null) req.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
            using var res = await http.SendAsync(req, cts.Token).ConfigureAwait(false);
            var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            JObject json;
            try { json = string.IsNullOrEmpty(text) ? new JObject() : JObject.Parse(text); }
            catch (JsonException) { json = new JObject { ["error"] = text }; }
            if (res.IsSuccessStatusCode) return json;
            Fail(method, path, $"HTTP {(int)res.StatusCode}: {json["error"]}", quiet);
        }
        catch (Exception e) { Fail(method, path, e.Message, quiet); }
        return null;
    }

    void Fail(HttpMethod method, string path, string why, bool quiet)
    {
        Debug.LogWarning($"[LoftClient] {method} {path} failed: {why}");
        if (!quiet) inbox.Enqueue(new JObject { ["type"] = "error", ["message"] = why });
    }

    HttpRequestMessage Request(HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, serverUrl.TrimEnd('/') + path);
        if (!string.IsNullOrEmpty(token)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    // Body("k1", v1, "k2", v2...) — skips null values so the server applies its defaults.
    static JObject Body(params string[] kv)
    {
        var o = new JObject();
        for (int i = 0; i + 1 < kv.Length; i += 2) if (kv[i + 1] != null) o[kv[i]] = kv[i + 1];
        return o;
    }
}
