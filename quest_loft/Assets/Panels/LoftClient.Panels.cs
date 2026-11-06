// VIRTUAL LOFT — LoftClient.Panels.cs: the calls boards make.

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public partial class LoftClient
{
    static string E(string s) => Uri.EscapeDataString(s ?? "");

    public Task<JObject> GetBoards(string scene) => Send(HttpMethod.Get, $"/boards?scene={E(scene)}", null, true);
    public Task<JObject> SaveBoards(string scene, JArray boards) =>
        Send(HttpMethod.Put, $"/boards?scene={E(scene)}", new JObject { ["boards"] = boards }, true);

    public Task<JObject> GetNotes() => Send(HttpMethod.Get, "/notes", null, true);
    public Task<JObject> AddNote(string text) => Send(HttpMethod.Post, "/notes", new JObject { ["text"] = text });
    public Task<JObject> DeleteNote(string id) => Send(HttpMethod.Delete, $"/notes/{E(id)}", null);

    public Task<JObject> GetTree(string project, string path) => Send(HttpMethod.Get, $"/projects/{E(project)}/tree?path={E(path)}", null, true);
    public Task<JObject> GetFile(string project, string path) => Send(HttpMethod.Get, $"/projects/{E(project)}/file?path={E(path)}", null, true);
    public Task<JObject> GetDiff(string taskId) => Send(HttpMethod.Get, $"/tasks/{E(taskId)}/diff", null, true);

    public Task<JObject> PrepareVideo(string url) => Send(HttpMethod.Post, "/cinema", new JObject { ["url"] = url }, true);
    public Task<JObject> VideoStatus(string id) => Send(HttpMethod.Get, $"/cinema/{E(id)}", null, true);

    public Task<JObject> OpenPreview(string taskId) => Send(HttpMethod.Post, "/preview/sessions", new JObject { ["taskId"] = taskId, ["width"] = 1360, ["height"] = 800 }, true);
    public Task<JObject> OpenWeb(string url) => Send(HttpMethod.Post, "/preview/sessions", new JObject { ["url"] = url, ["width"] = 1360, ["height"] = 800 }, true);
    public Task<JObject> PreviewInfo(string id) => Send(HttpMethod.Get, $"/preview/sessions/{E(id)}", null, true);
    public Task<JObject> PreviewInput(string id, JObject input) => Send(HttpMethod.Post, $"/preview/sessions/{E(id)}/input", input, true);
    public Task<JObject> ClosePreview(string id) => Send(HttpMethod.Delete, $"/preview/sessions/{E(id)}", null, true);
}
