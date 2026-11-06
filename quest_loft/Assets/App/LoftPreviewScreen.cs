// VIRTUAL LOFT — the PREVIEW screen: see what the team built before you approve.
// A big monitor to the CEO's right, angled toward the seat. When the task in
// review has screenshots (backend previews.js), it shows the desktop shot full
// size with the phone shot as an inset, plus project / change size / live URL.

using Newtonsoft.Json.Linq;
using UnityEngine;

public class LoftPreviewScreen : MonoBehaviour
{
    public static readonly Vector3 Position = LoftWorld.Polar(86f, 3.6f, 1.55f);
    const float W = 2.0f, H = 1.25f;

    public LoftClient client;
    public string shownTaskId, shownDesktop;

    Transform root;
    Renderer desktopQuad, mobileQuad;
    TMPro.TMP_Text caption, placeholder;
    Texture2D desktopTex, mobileTex;

    void Awake()
    {
        if (client == null) client = FindFirstObjectByType<LoftClient>();
        EnsureBuilt();
    }

    public void EnsureBuilt()
    {
        if (root != null) return;
        var existing = GameObject.Find("PreviewScreen");
        if (existing != null) LoftUtil.Kill(existing);
        root = new GameObject("PreviewScreen").transform;
        root.position = Position;
        // Face the CEO's seat; TextMesh/quads read from -Z, so look AWAY from the seat.
        var seat = new Vector3(0f, Position.y, LoftWorld.RIG_START_Z);
        root.rotation = Quaternion.LookRotation(Position - seat);
        LoftUtil.Prim(PrimitiveType.Cube, "Bezel", root, new Vector3(0f, 0f, 0.04f), new Vector3(W + 0.12f, H + 0.12f, 0.05f), new Color(0.06f, 0.06f, 0.08f));
        desktopQuad = Quad("Desktop", Vector3.zero, new Vector2(W, H));
        mobileQuad = Quad("Mobile", new Vector3(W / 2f - 0.27f, -H / 2f + 0.5f, -0.01f), new Vector2(0.42f, 0.9f));
        caption = LoftUtil.Label(root, "Caption", new Vector3(0f, H / 2f + 0.12f, 0f), 0.075f, new Color(1f, 0.95f, 0.8f), W + 0.4f);
        placeholder = LoftUtil.Label(root, "Placeholder", new Vector3(0f, 0f, -0.01f), 0.085f, new Color(0.7f, 0.75f, 0.85f), W - 0.2f);
        // Label faces +Z of its parent; this screen's front is its -Z side.
        caption.transform.localRotation = Quaternion.identity;
        placeholder.transform.localRotation = Quaternion.identity;
        Show(null);
    }

    Renderer Quad(string name, Vector3 pos, Vector2 size)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = name;
        LoftUtil.Kill(q.GetComponent<Collider>());
        q.transform.SetParent(root, false);
        q.transform.localPosition = pos;
        q.transform.localRotation = Quaternion.identity; // a Quad's visible face is -Z = toward the CEO here
        q.transform.localScale = new Vector3(size.x, size.y, 1f);
        var r = q.GetComponent<Renderer>();
        var shader = Shader.Find("Unlit/Texture");
        r.sharedMaterial = shader != null ? new Material(shader) : LoftUtil.Mat(Color.white);
        r.enabled = false;
        return r;
    }

    // Called by LoftDesk whenever the review target or its preview changes.
    public void Show(JObject task)
    {
        if (root == null) return;
        var preview = task?["preview"] as JObject;
        string status = (string)preview?["status"];
        string desktop = (string)preview?["desktop"];
        caption.text = task == null ? "PREVIEW"
            : $"PREVIEW · {(string)task["projectId"]} · {LoftUtil.Trunc(((string)task["title"] ?? "").Split('\n')[0], 34)}\n" +
              $"{(string)(task["diff"] as JObject)?["stat"] ?? ""}{(status == "ready" ? $"   live: {(string)preview["url"]}" : "")}";
        if (status != "ready")
        {
            desktopQuad.enabled = mobileQuad.enabled = false;
            shownDesktop = null;
            placeholder.text = task == null ? "Nothing in review"
                : status == "loading" ? "Taking screenshots…"
                : status == "error" ? "Preview failed:\n" + LoftUtil.Wrap((string)preview["error"], 44, 4)
                : ((task["diff"] as JObject)?["files"] as JArray)?.Count > 0 ? "No preview for this project" : "No file changes to preview";
            return;
        }
        placeholder.text = "";
        shownTaskId = (string)task["id"];
        if (desktop == shownDesktop) return; // already loaded
        shownDesktop = desktop;
        Load(desktop, desktopQuad, t => desktopTex = Replace(desktopTex, t));
        Load((string)preview["mobile"], mobileQuad, t => mobileTex = Replace(mobileTex, t));
    }

    void Load(string path, Renderer target, System.Action<Texture2D> keep)
    {
        if (client == null || string.IsNullOrEmpty(path)) return;
        client.ThenBytes(client.GetBytes(path), bytes =>
        {
            if (bytes == null || target == null) return;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes)) { Destroy(tex); return; }
            keep(tex);
            target.sharedMaterial.mainTexture = tex;
            target.enabled = true;
        });
    }

    static Texture2D Replace(Texture2D old, Texture2D now)
    {
        if (old != null && old != now) Destroy(old); // screenshots are big: free the previous one
        return now;
    }
}
