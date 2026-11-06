// VIRTUAL LOFT — tiny shared helpers (text, materials, primitives, colors).
// Reading text: Label (TextMeshPro SDF). Text (legacy TextMesh) stays for old
// callers. Both read from -Z, so text faces +Z (the play side) with a local Y=180.

using System.Text;
using TMPro;
using UnityEngine;

public static class LoftUtil
{
    // Edit-mode safe destroy (editor verify/bootstrap run outside Play).
    public static void Kill(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
    }

    public static Material Mat(Color c) => new Material(Shader.Find("Standard")) { color = c };

    public static Color Hex(string hex, Color fallback) =>
        !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;

    public static Vector3 V3(float[] a, Vector3 fallback) =>
        a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : fallback;

    public static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Color color, bool keepCollider = false)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = Mat(color);
        if (!keepCollider) Kill(go.GetComponent<Collider>());
        return go;
    }

    // Find-or-create a TextMesh child (idempotent on already-built scenes).
    public static TextMesh Text(Transform parent, string name, Vector3 localPos, int fontSize, float charSize, Color color)
    {
        var t = parent.Find(name);
        if (t == null)
        {
            t = new GameObject(name).transform;
            t.SetParent(parent, false);
        }
        t.localPosition = localPos;
        t.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var tm = t.GetComponent<TextMesh>();
        if (tm == null) tm = t.gameObject.AddComponent<TextMesh>();
        tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tm.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
        tm.fontSize = fontSize;
        tm.characterSize = charSize;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = color;
        return tm;
    }

    // Crisp SDF text for anything the CEO reads in VR. heightM = LINE height in
    // meters (a line of text takes heightM from baseline to baseline); width > 0
    // wraps at that many meters, height > 0 sets the box (top/bottom aligned text
    // needs it). Parent must be uniformly scaled. Text faces +Z of its parent.
    public static TMP_Text Label(Transform parent, string name, Vector3 localPos, float heightM, Color color, float width = 0f,
        TextAlignmentOptions align = TextAlignmentOptions.Center, float height = 0f)
    {
        var t = parent.Find(name);
        if (t == null)
        {
            t = new GameObject(name, typeof(RectTransform)).transform;
            t.SetParent(parent, false);
        }
        t.localPosition = localPos;
        t.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var tm = t.GetComponent<TextMeshPro>();
        if (tm == null) tm = t.gameObject.AddComponent<TextMeshPro>();
        if (tm.font == null) tm.font = TMP_Settings.defaultFontAsset;
        // World line height = faceInfo.lineHeight / pointSize * fontSize * 0.1 (TMP's 3D scale).
        float ratio = tm.font != null && tm.font.faceInfo.pointSize > 0 ? tm.font.faceInfo.lineHeight / tm.font.faceInfo.pointSize : 1.17f;
        tm.fontSize = heightM * 10f / ratio;
        tm.color = color;
        tm.alignment = align;
        tm.richText = false;
        tm.textWrappingMode = width > 0f ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        tm.overflowMode = TextOverflowModes.Overflow;
        tm.rectTransform.sizeDelta = new Vector2(width > 0f ? width : 20f, height > 0f ? height : 20f);
        return tm;
    }

    public static string Trunc(string s, int n)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace("\n", " ").Trim();
        return s.Length <= n ? s : s.Substring(0, n - 1) + "…";
    }

    // Greedy word wrap for TextMesh (it has no wrapping of its own).
    public static string Wrap(string s, int width, int maxLines)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder();
        int line = 0, lines = 1;
        foreach (var word in s.Replace("\n", " ").Split(' '))
        {
            if (word.Length == 0) continue;
            if (line > 0 && line + 1 + word.Length > width)
            {
                if (++lines > maxLines) { sb.Append('…'); break; }
                sb.Append('\n');
                line = 0;
            }
            else if (line > 0) { sb.Append(' '); line++; }
            var w = word.Length > width ? word.Substring(0, width) : word;
            sb.Append(w);
            line += w.Length;
        }
        return sb.ToString();
    }
}
