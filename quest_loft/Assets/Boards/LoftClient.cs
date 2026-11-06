// VIRTUAL LOFT — LoftClient.cs
// Runtime link Unity <-> backend. Talks to backend/src/server.js
// (default http://127.0.0.1:4101) via System.Net.Http (pure .NET — no
// engine-module dependency, compiles in stripped/batch contexts).
// Attach next to BoardManager. In XR Simulator (Play on the dev Mac) the
// server runs on the same machine, so localhost just works.
//
// Flow: BoardManager.Save() -> POST /loft -> cloud/local fallback.
// Agent voice command ("run X") -> POST /agent/run -> live Spark text ->
// Log board. Server speaks BoardManager's native {"boards":[...]} shape:
// GET .../boards feeds Load() directly; Save() output nests under snapshot.

using System;
using System.Collections;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class LoftClient : MonoBehaviour
{
    [Tooltip("Backend bridge: run `npm run serve -- --env-file=.env` in backend/ (or: node --env-file=.env src/server.js)")]
    public string serverUrl = "http://127.0.0.1:4101";

    [Tooltip("Room id shared by Unity + backend (same loft everywhere).")]
    public string roomId = "focus-loft";

    [Tooltip("Optional BoardManager to push server state into after load.")]
    public BoardManager boards;

    static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

    // Test hook: per-call timeout budget. Production stays 120s (reasoning
    // model streams); headless resilience tests (A18) set it to seconds.
    public static int timeoutSeconds = 120;
    static CancellationTokenSource LinkedTimeout()
    {
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
        return cts;
    }

    void Awake()
    {
        if (boards == null) boards = FindFirstObjectByType<BoardManager>();
    }

    // ---- async core (Task) ----

    public async Task<bool> CheckHealthAsync()
    {
        try
        {
            using var cts = LinkedTimeout();
            var res = await http.GetAsync(serverUrl + "/health", cts.Token).ConfigureAwait(false);
            Debug.Log(res.IsSuccessStatusCode ? "[LoftClient] backend reachable" : $"[LoftClient] backend HTTP {(int)res.StatusCode}");
            return res.IsSuccessStatusCode;
        }
        catch (Exception e) { Debug.LogWarning($"[LoftClient] backend DOWN: {e.Message}"); return false; }
    }

    public async Task<bool> LoadFromServerAsync(string id)
    {
        try
        {
            using var cts = LinkedTimeout();
            var resp = await http.GetAsync($"{serverUrl}/loft/{Uri.EscapeDataString(id)}/boards", cts.Token).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (boards != null) boards.Load(text);
            Debug.Log($"[LoftClient] loaded loft '{id}' from server");
            return true;
        }
        catch (Exception e) { Debug.LogWarning($"[LoftClient] load failed: {e.Message}"); return false; }
    }

    public Task<bool> LoadFromServerAsync() => LoadFromServerAsync(roomId);

    public async Task<bool> SaveToServerAsync()
    {
        if (boards == null) return false;
        try
        {
            using var cts = LinkedTimeout();
            var body = "{\"roomId\":" + JsonEscape(roomId) + ",\"snapshot\":" + boards.Save() + "}";
            var res = await http.PostAsync(serverUrl + "/loft", new StringContent(body, Encoding.UTF8, "application/json"), cts.Token).ConfigureAwait(false);
            Debug.Log(res.IsSuccessStatusCode ? "[LoftClient] saved loft to server" : $"[LoftClient] save HTTP {(int)res.StatusCode}");
            return res.IsSuccessStatusCode;
        }
        catch (Exception e) { Debug.LogWarning($"[LoftClient] save failed: {e.Message}"); return false; }
    }

    public async Task<string> RunAgentAsync(string role, string task)
    {
        try
        {
            using var cts = LinkedTimeout();
            var body = "{\"role\":" + JsonEscape(role) + ",\"task\":" + JsonEscape(task) + "}";
            var res = await http.PostAsync(serverUrl + "/agent/run", new StringContent(body, Encoding.UTF8, "application/json"), cts.Token).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) { Debug.LogWarning($"[LoftClient] agent HTTP {(int)res.StatusCode}"); return null; }
            return await res.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch (Exception e) { Debug.LogWarning($"[LoftClient] agent failed: {e.Message}"); return null; }
    }

    // ---- coroutine wrappers (for Play-Mode tests / UI buttons) ----

    public IEnumerator CheckHealth(Action<bool> done) => AwaitToCoroutine(CheckHealthAsync(), done);
    public IEnumerator LoadFromServer(Action<bool> done) => AwaitToCoroutine(LoadFromServerAsync(), done);
    public IEnumerator LoadFromServer(string id, Action<bool> done) => AwaitToCoroutine(LoadFromServerAsync(id), done);
    public IEnumerator SaveToServer(Action<bool> done) => AwaitToCoroutine(SaveToServerAsync(), done);
    public IEnumerator RunAgent(string role, string task, Action<string> done) => AwaitToCoroutine(RunAgentAsync(role, task), done);

    static IEnumerator AwaitToCoroutine<T>(Task<T> task, Action<T> done)
    {
        while (!task.IsCompleted) yield return null;
        T result = default;
        if (task.Status == TaskStatus.RanToCompletion) result = task.Result;
        else Debug.LogWarning($"[LoftClient] task faulted: {task.Exception?.InnerException?.Message}");
        done?.Invoke(result);
    }

    static string JsonEscape(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
}
