// VIRTUAL LOFT — the room: one shared layout (floor, main wall, 4 boards,
// camera rig) + swappable THEMES.
//
// A theme is a JSON file in Assets/Resources/Themes/<id>.json: sky color,
// lights, fog, floor/wall/board tints, ambient audio clip
// (Resources/Audio/<audio>) and a list of props (primitive or prefab from
// Resources/Props/<prefab>, optional glow/pulse/spin). New scene = new JSON
// file, no code. Layout never changes per theme, so boards and agents stay put.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

[Serializable] public class ThemeLight { public string color = "#FFFFFF"; public float intensity = 1f; public float[] euler; }
[Serializable] public class ThemeFog { public string color = "#000000"; public float density = 0.02f; }
[Serializable] public class ThemeProp
{
    public string shape = "Cube", prefab, color = "#FFFFFF";
    public float[] pos, scale, euler;
    public bool glow, pulse;
    public float spin;
}
[Serializable] public class Theme
{
    public string id, name, background = "#1E1A24", ambient = "#555555", floor = "#73523A", wall = "#9E7A57", board = "#292B38", audio;
    public ThemeLight light = new ThemeLight();
    public ThemeFog fog;
    public List<ThemeProp> props = new List<ThemeProp>();
}

public class LoftWorld : MonoBehaviour
{
    public const float RIG_START_Z = 6.5f;
    public static readonly (string id, string title, Vector3 pos, Vector2 size)[] Boards =
    {
        ("notes", "REVIEW", new Vector3(-4.2f, 1.6f, 0f), new Vector2(2.4f, 1.4f)),
        ("kanban", "KANBAN", new Vector3(-1.4f, 1.6f, 0f), new Vector2(2.8f, 1.6f)),
        ("calendar", "TEAM", new Vector3(1.6f, 1.6f, 0f), new Vector2(2.4f, 1.2f)),
        ("log", "LIVE LOG", new Vector3(4.2f, 1.6f, 0f), new Vector2(2.0f, 1.4f)),
    };

    public string defaultTheme = "loft";
    public Transform rig;
    public Camera cam;
    public Theme Current { get; private set; }

    Light sun;
    AudioSource ambience;
    Transform propsRoot;
    readonly List<(Transform t, Material m, Color c, ThemeProp p)> live = new List<(Transform, Material, Color, ThemeProp)>();

    void Awake()
    {
        Build();
        if (!ApplyTheme(PlayerPrefs.GetString("loft.theme", defaultTheme))) ApplyTheme(defaultTheme);
    }

    // Idempotent: reuses objects already in the scene, creates missing ones.
    public void Build()
    {
        EnsureRig();
        var lightGo = GameObject.Find("Directional Light") ?? new GameObject("Directional Light");
        // (No `??` on components: Unity's missing-component "null" isn't C# null.)
        sun = lightGo.GetComponent<Light>();
        if (sun == null) sun = lightGo.AddComponent<Light>();
        sun.type = LightType.Directional;

        var floor = GameObject.Find("Floor");
        if (floor == null)
        {
            floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(2.4f, 1f, 2f);
            floor.GetComponent<Renderer>().sharedMaterial = LoftUtil.Mat(Color.gray);
        }
        var wall = GameObject.Find("MainWall");
        if (wall == null)
        {
            wall = new GameObject("MainWall");
            var mesh = LoftUtil.Prim(PrimitiveType.Cube, "WallMesh", wall.transform, Vector3.zero, new Vector3(12f, 4f, 0.2f), Color.gray);
            mesh.transform.position = new Vector3(0f, 2f, -0.4f);
        }
        foreach (var (id, title, pos, size) in Boards)
        {
            var plate = GameObject.Find($"Board_{id}");
            if (plate == null)
            {
                plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plate.name = $"Board_{id}";
                plate.transform.position = new Vector3(pos.x, pos.y, pos.z + 0.05f);
                plate.transform.localScale = new Vector3(size.x, size.y, 0.08f);
                plate.GetComponent<Renderer>().sharedMaterial = LoftUtil.Mat(Color.gray);
            }
            // Text sits proud of the plate face (+0.7 local z * 0.08 depth) on the play side.
            LoftUtil.Text(plate.transform, "Title", new Vector3(0f, 0.35f, 0.7f), 64, 0.008f, new Color(1f, 0.95f, 0.8f)).text = title;
            var content = LoftUtil.Text(plate.transform, "Content", new Vector3(0f, -0.12f, 0.7f), 40, 0.01f, new Color(0.85f, 0.92f, 1f));
            if (string.IsNullOrEmpty(content.text)) content.text = "…";
        }
    }

    // Camera lives under a rig: teleport/walk move the rig, XR tracking moves
    // the camera inside it (moving the camera itself would fight head tracking).
    void EnsureRig()
    {
        if (cam == null) cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        if (cam == null)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = go.AddComponent<Camera>();
        }
        cam.clearFlags = CameraClearFlags.SolidColor;
        if (rig == null) rig = cam.transform.parent;
        if (rig == null || rig.name != "CameraRig")
        {
            rig = new GameObject("CameraRig").transform;
            rig.SetPositionAndRotation(new Vector3(0f, VirtualLoft.XR.Comfort.SEATED_EYE_HEIGHT, RIG_START_Z), Quaternion.Euler(0f, 180f, 0f)); // face the wall (-Z)
            cam.transform.SetParent(rig, false);
            cam.transform.localPosition = Vector3.zero;
            cam.transform.localRotation = Quaternion.identity;
        }
    }

    public static string[] ThemeIds() =>
        Resources.LoadAll<TextAsset>("Themes").Select(t => t.name).OrderBy(n => n).ToArray();

    public static Theme LoadTheme(string id)
    {
        var asset = Resources.Load<TextAsset>($"Themes/{id}");
        if (asset == null) return null;
        try
        {
            var t = JsonConvert.DeserializeObject<Theme>(asset.text);
            if (t != null) { t.id = id; t.light ??= new ThemeLight(); t.props ??= new List<ThemeProp>(); }
            return t;
        }
        catch (Exception e) { Debug.LogWarning($"[World] theme '{id}' is not valid JSON: {e.Message}"); return null; }
    }

    public bool ApplyTheme(string id)
    {
        var t = LoadTheme(id);
        if (t == null) { Debug.LogWarning($"[World] no theme '{id}' in Resources/Themes"); return false; }
        if (sun == null) Build();
        Current = t;

        cam.backgroundColor = LoftUtil.Hex(t.background, Color.black);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = LoftUtil.Hex(t.ambient, Color.gray);
        sun.color = LoftUtil.Hex(t.light.color, Color.white);
        sun.intensity = t.light.intensity;
        sun.transform.rotation = Quaternion.Euler(LoftUtil.V3(t.light.euler, new Vector3(50f, -30f, 0f)));
        RenderSettings.fog = t.fog != null;
        if (t.fog != null)
        {
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = LoftUtil.Hex(t.fog.color, Color.black);
            RenderSettings.fogDensity = t.fog.density;
        }
        Tint(GameObject.Find("Floor"), t.floor);
        Tint(GameObject.Find("MainWall"), t.wall);
        foreach (var b in Boards) Tint(GameObject.Find($"Board_{b.id}"), t.board);

        // Props: drop the old theme's set, spawn this one's.
        live.Clear();
        if (propsRoot != null) LoftUtil.Kill(propsRoot.gameObject);
        var old = GameObject.Find("ThemeProps");
        if (old != null) LoftUtil.Kill(old);
        propsRoot = new GameObject("ThemeProps").transform;
        foreach (var p in t.props) SpawnProp(p);

        // Ambient audio (optional clip at Resources/Audio/<audio>).
        if (ambience == null) ambience = GetComponent<AudioSource>();
        if (ambience == null) ambience = gameObject.AddComponent<AudioSource>();
        var clip = string.IsNullOrEmpty(t.audio) ? null : Resources.Load<AudioClip>($"Audio/{t.audio}");
        ambience.loop = true;
        ambience.volume = 0.4f;
        if (ambience.clip != clip) { ambience.clip = clip; if (clip != null && Application.isPlaying) ambience.Play(); }
        if (clip == null) ambience.Stop();

        PlayerPrefs.SetString("loft.theme", id);
        Debug.Log($"[World] theme -> {t.name ?? id} ({t.props.Count} props)");
        return true;
    }

    public string NextTheme()
    {
        var ids = ThemeIds();
        if (ids.Length == 0) return null;
        int i = Array.IndexOf(ids, Current?.id);
        var next = ids[(i + 1) % ids.Length];
        return ApplyTheme(next) ? next : null;
    }

    void SpawnProp(ThemeProp p)
    {
        GameObject go = null;
        if (!string.IsNullOrEmpty(p.prefab))
        {
            var prefab = Resources.Load<GameObject>($"Props/{p.prefab}");
            if (prefab != null) go = Instantiate(prefab);
        }
        if (go == null)
        {
            var shape = Enum.TryParse<PrimitiveType>(p.shape, true, out var s) ? s : PrimitiveType.Cube;
            go = LoftUtil.Prim(shape, "Prop", propsRoot, Vector3.zero, Vector3.one, LoftUtil.Hex(p.color, Color.white));
        }
        go.transform.SetParent(propsRoot, false);
        go.transform.localPosition = LoftUtil.V3(p.pos, Vector3.zero);
        go.transform.localScale = LoftUtil.V3(p.scale, Vector3.one);
        go.transform.localRotation = Quaternion.Euler(LoftUtil.V3(p.euler, Vector3.zero));
        var r = go.GetComponentInChildren<Renderer>();
        Material m = null;
        var c = LoftUtil.Hex(p.color, Color.white);
        if (r != null && string.IsNullOrEmpty(p.prefab))
        {
            m = r.sharedMaterial;
            if (p.glow) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * 1.5f); }
        }
        if (p.pulse || p.spin != 0f) live.Add((go.transform, m, c, p));
    }

    static void Tint(GameObject go, string hex)
    {
        if (go == null || string.IsNullOrEmpty(hex)) return;
        var r = go.GetComponentInChildren<Renderer>(); // includes go itself
        if (r == null) return;
        if (r.sharedMaterial == null || r.sharedMaterial.shader.name != "Standard") r.sharedMaterial = LoftUtil.Mat(Color.white);
        r.sharedMaterial.color = LoftUtil.Hex(hex, Color.gray);
    }

    // Party lights etc: pulse = hue cycle on glowing props, spin = deg/s.
    void Update()
    {
        float t = Time.time;
        foreach (var (tr, m, c, p) in live)
        {
            if (tr == null) continue;
            if (p.spin != 0f) tr.Rotate(0f, p.spin * Time.deltaTime, 0f, Space.World);
            if (p.pulse && m != null)
            {
                Color.RGBToHSV(c, out var h, out var s, out var v);
                var col = Color.HSVToRGB(Mathf.Repeat(h + t * 0.25f + tr.GetInstanceID() * 0.13f, 1f), s, v);
                m.color = col;
                m.SetColor("_EmissionColor", col * (1f + Mathf.Abs(Mathf.Sin(t * 4f))));
            }
        }
    }
}
