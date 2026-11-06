// VIRTUAL LOFT — the world: one shared floor plan (seat, agents, 4 boards, camera
// rig) + swappable generated SCENES (beach, scenery, city, galaxy, party).
//
// A theme is a small JSON in Assets/Resources/Themes/<id>.json: which scene to
// build, sky colors + sun, trilight ambient, fog, key light, water colors, board
// tint and the ambient audio clip. The scene's meshes/shaders are generated in
// code (Assets/World/*). Seat, boards and agents never move, so a scene change
// is just a fade. Everything a scene creates is owned by a WorldCtx and freed
// on the next switch.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;

[Serializable] public class ThemeLight { public string color = "#FFFFFF"; public float intensity = 1f; public float[] euler; }
[Serializable] public class ThemeFog { public string color = "#000000"; public float density = 0.02f; }
[Serializable] public class ThemeAmbient { public string sky = "#8899AA", equator = "#667788", ground = "#444444"; }
[Serializable] public class ThemeSky
{
    public string zenith = "#3C7BC4", mid = "#7FB6E6", horizon = "#F6D9B0", below, sunColor = "#FFE2B0", nebA = "#7A2CC4", nebB = "#1E64C8";
    public float sunAz = 28f, sunEl = 10f, sunSize = 0.05f, glow = 0.6f, stars, neb, exposure = 1f;
}
[Serializable] public class ThemeWater { public string shallow = "#33C2C4", deep = "#0B5C93"; public float amp = 0.18f, speed = 1f, glint = 1.5f, foam = 0.5f; }
[Serializable] public class ThemeProp { public string shape = "Cube", prefab, color = "#FFFFFF"; public float[] pos, scale, euler; public bool glow, pulse; public float spin; }
[Serializable] public class Theme
{
    public string id, name, scene, board = "#14202E", accent = "#FFD27A", audio;
    public ThemeSky sky = new ThemeSky();
    public ThemeAmbient ambient = new ThemeAmbient();
    public ThemeLight light = new ThemeLight();
    public ThemeFog fog;
    public ThemeWater water = new ThemeWater();
    public List<ThemeProp> props = new List<ThemeProp>(); // legacy, unused by the generated scenes
}

public class LoftWorld : MonoBehaviour
{
    public const float RIG_START_Z = 6.5f;
    public static readonly Vector3 Seat = new Vector3(0f, 1.2f, RIG_START_Z);
    public const float DECK_FRONT = 0f, DECK_BACK = 10.4f, DECK_HALF = 7.4f; // shared floor plan (x +-DECK_HALF)

    // A point `dist` m from the seat at `bearing` degrees (+ = CEO's right).
    public static Vector3 Polar(float bearing, float dist, float y)
    {
        float b = bearing * Mathf.Deg2Rad;
        return new Vector3(-Mathf.Sin(b) * dist, y, RIG_START_Z - Mathf.Cos(b) * dist);
    }

    // Two big screens flank the open middle (horizon stays visible); two slim ones sit further out.
    public static readonly (string id, string title, Vector3 pos, Vector2 size)[] Boards =
    {
        ("notes", "REVIEW", Polar(49f, 3.5f, 1.4f), new Vector2(1.7f, 1.1f)),
        ("kanban", "KANBAN", Polar(-49f, 3.5f, 1.4f), new Vector2(1.7f, 1.1f)),
        ("calendar", "TEAM", Polar(-88f, 3.4f, 2.0f), new Vector2(1.4f, 0.8f)),
        ("log", "LIVE LOG", Polar(-88f, 3.4f, 1.05f), new Vector2(1.4f, 0.95f)),
    };

    public string defaultTheme = "beach";
    public Transform rig;
    public Camera cam;
    public Theme Current { get; private set; }
    public int SceneTriangles => ctx != null ? ctx.Triangles : 0;

    Light sun;
    AudioSource ambience;
    WorldCtx ctx;
    Material boardScreen, boardFrame, fadeMat;
    Transform fadeQuad;
    bool fading;
    // base lighting (SetMood scales these)
    Color ambSky, ambEq, ambGnd, fogCol, sunCol;
    float fogDen, sunInt;

    void Awake()
    {
        Build();
        ApplyTheme(Resolve(PlayerPrefs.GetString("loft.theme", defaultTheme)));
    }

    void OnDestroy()
    {
        if (ctx != null) ctx.Dispose();
        LoftUtil.Kill(boardScreen); LoftUtil.Kill(boardFrame); LoftUtil.Kill(fadeMat);
    }

    // A saved or default id that no longer exists must not trap the user: fall back to beach.
    public string Resolve(string id)
    {
        var ids = ThemeIds();
        if (ids.Contains(id)) return id;
        return ids.Contains("beach") ? "beach" : ids.FirstOrDefault();
    }

    // Idempotent: reuses objects already in the scene, creates missing ones.
    public void Build()
    {
        foreach (var old in new[] { "MainWall", "Floor" }) // pre-cabana scenes
        {
            GameObject g;
            while ((g = GameObject.Find(old)) != null) { g.SetActive(false); LoftUtil.Kill(g); } // Destroy waits for frame end; Find skips inactive
        }
        EnsureRig();
        var lightGo = GameObject.Find("Directional Light") ?? new GameObject("Directional Light");
        // (No `??` on components: Unity's missing-component "null" isn't C# null.)
        sun = lightGo.GetComponent<Light>();
        if (sun == null) sun = lightGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.shadows = LightShadows.None;
        if (boardScreen == null)
        {
            var u = Resources.Load<Shader>("Shaders/LoftUnlit");
            var l = Resources.Load<Shader>("Shaders/LoftLit");
            boardScreen = new Material(u) { name = "board_screen" };
            boardFrame = new Material(l) { name = "board_frame" };
            fadeMat = new Material(u) { name = "fade" };
            fadeMat.SetColor("_Tint", new Color(0f, 0f, 0f, 0f));
            fadeMat.SetFloat("_Src", 5); fadeMat.SetFloat("_Dst", 10); fadeMat.SetFloat("_ZW", 0); fadeMat.SetFloat("_ZT", 8);
            fadeMat.SetFloat("_FogAdd", 0); fadeMat.renderQueue = 4000;
        }
        foreach (var (id, title, pos, size) in Boards) BuildBoard(id, title, pos, size);
        BuildFade();
    }

    void BuildBoard(string id, string title, Vector3 pos, Vector2 size)
    {
        var go = GameObject.Find($"Board_{id}");
        // Old cube plate, or a leftover from an edit-mode build whose materials are gone.
        var oldFrame = go != null ? go.transform.Find("Frame") : null;
        if (go != null && (oldFrame == null || oldFrame.GetComponent<MeshRenderer>().sharedMaterial != boardFrame))
        {
            go.SetActive(false);
            LoftUtil.Kill(go);
            go = null;
        }
        if (go == null)
        {
            go = WorldCtx.New($"Board_{id}", null);
            var m = new Mk(id.Length);
            float w = size.x, h = size.y;
            var dark = Mk.Hex("#232C38");
            // bezel, accent line, stand: all in the board's local space (+Z faces the seat)
            m.Box(new Vector3(0, 0, -0.03f), new Vector3(w + 0.1f, h + 0.1f, 0.04f), dark, 0.02f);
            m.Box(new Vector3(0, h / 2 - 0.17f, -0.002f), new Vector3(w - 0.14f, 0.012f, 0.004f), Mk.Hex("#FFD27A"), 0f);
            var foot = new Vector3(0, -pos.y, -0.1f);
            m.Prism(foot + Vector3.up * 0.02f, new Vector3(0, -h / 2 - 0.05f, -0.06f), 0.035f, 0.035f, 6, Mk.Hex("#3A3F4A"), true, false);
            m.Prism(foot, foot + Vector3.up * 0.03f, 0.3f, 0.3f, 10, Mk.Hex("#2E3440"), false, true);
            var frame = new GameObject("Frame");
            if (!Application.isPlaying) frame.hideFlags = HideFlags.DontSaveInEditor;
            frame.transform.SetParent(go.transform, false);
            frame.AddComponent<MeshFilter>().sharedMesh = m.Build("board_frame");
            frame.AddComponent<MeshRenderer>().sharedMaterial = boardFrame;
            var s = new Mk();
            var top = Mk.Hex("#FFFFFF"); var bot = Mk.Hex("#C9D2DE");
            s.QuadFacing(new Vector3(-w / 2, h / 2, 0), new Vector3(w / 2, h / 2, 0), new Vector3(w / 2, -h / 2, 0), new Vector3(-w / 2, -h / 2, 0), top, bot, Vector3.forward);
            var scr = new GameObject("Screen");
            if (!Application.isPlaying) scr.hideFlags = HideFlags.DontSaveInEditor;
            scr.transform.SetParent(go.transform, false);
            scr.AddComponent<MeshFilter>().sharedMesh = s.Build("board_screen");
            scr.AddComponent<MeshRenderer>().sharedMaterial = boardScreen;
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 0, -0.03f); col.size = new Vector3(w + 0.1f, h + 0.1f, 0.1f);
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
        }
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(new Vector3(Seat.x - pos.x, 0f, Seat.z - pos.z)));
        var t = LoftUtil.Label(go.transform, "Title", new Vector3(0f, size.y / 2 - 0.095f, 0.012f), 0.1f, new Color(1f, 0.92f, 0.72f), size.x - 0.2f);
        t.text = title;
        t.fontStyle = FontStyles.Bold;
        float top_ = size.y / 2 - 0.21f, bottom = -size.y / 2 + 0.07f;
        var c = LoftUtil.Label(go.transform, "Content", new Vector3(0f, (top_ + bottom) / 2, 0.012f), 0.078f, new Color(0.88f, 0.94f, 1f),
            size.x - 0.2f, TextAlignmentOptions.TopLeft, top_ - bottom);
        c.overflowMode = TextOverflowModes.Ellipsis;
        if (string.IsNullOrEmpty(c.text)) c.text = "…";
    }

    void BuildFade()
    {
        if (fadeQuad != null) return;
        var old = cam.transform.Find("FadeQuad");
        if (old != null) LoftUtil.Kill(old.gameObject);
        var go = WorldCtx.New("FadeQuad", cam.transform);
        go.transform.localPosition = new Vector3(0f, 0f, 0.3f);
        var m = new Mk();
        m.Tri2(new Vector3(-2, -2, 0), new Vector3(2, -2, 0), new Vector3(0, 3, 0), Color.white, Color.white, Color.white);
        go.AddComponent<MeshFilter>().sharedMesh = m.Build("fade");
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = fadeMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.SetActive(false);
        fadeQuad = go.transform;
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
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 1500f;
        cam.allowMSAA = true;
        cam.allowHDR = false;
        if (rig == null) rig = cam.transform.parent;
        if (rig == null || rig.name != "CameraRig")
        {
            rig = new GameObject("CameraRig").transform;
            rig.SetPositionAndRotation(new Vector3(0f, VirtualLoft.XR.Comfort.SEATED_EYE_HEIGHT, RIG_START_Z), Quaternion.Euler(0f, 180f, 0f)); // face the view (-Z)
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
            if (t != null)
            {
                t.id = id; t.light ??= new ThemeLight(); t.sky ??= new ThemeSky(); t.ambient ??= new ThemeAmbient();
                t.water ??= new ThemeWater(); t.props ??= new List<ThemeProp>();
            }
            return t;
        }
        catch (Exception e) { Debug.LogWarning($"[World] theme '{id}' is not valid JSON: {e.Message}"); return null; }
    }

    public bool ApplyTheme(string id)
    {
        var t = LoadTheme(id);
        if (t == null) { Debug.LogWarning($"[World] no theme '{id}' in Resources/Themes"); return false; }
        if (sun == null || boardScreen == null) Build();
        Current = t;

        // Drop the old scene completely (also one saved into an old Loft.unity).
        if (ctx != null) ctx.Dispose();
        GameObject stale;
        while ((stale = GameObject.Find("ThemeProps")) != null) { stale.SetActive(false); LoftUtil.Kill(stale); }
        ctx = new WorldCtx(t);
        var skyM = ctx.Sky();
        var waterM = ctx.Water();
        ConfigureSky(skyM, waterM, t);
        switch (t.scene ?? t.id)
        {
            case "scenery": ScenerySc.Build(ctx); break;
            case "city": CitySc.Build(ctx); break;
            case "galaxy": GalaxySc.Build(ctx); break;
            case "party": BeachSc.Build(ctx, true); break;
            default: BeachSc.Build(ctx, false); break;
        }

        RenderSettings.skybox = skyM;
        cam.clearFlags = CameraClearFlags.Skybox;
        ambSky = LoftUtil.Hex(t.ambient.sky, Color.gray); ambEq = LoftUtil.Hex(t.ambient.equator, Color.gray); ambGnd = LoftUtil.Hex(t.ambient.ground, Color.gray);
        sunCol = LoftUtil.Hex(t.light.color, Color.white); sunInt = t.light.intensity;
        fogCol = LoftUtil.Hex(t.fog?.color, LoftUtil.Hex(t.sky.horizon, Color.gray)); fogDen = t.fog != null ? t.fog.density : 0f;
        sun.transform.rotation = Quaternion.Euler(LoftUtil.V3(t.light.euler, new Vector3(40f, 160f, 0f)));
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.fog = t.fog != null;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        SetMood(0f);

        var screen = LoftUtil.Hex(t.board, new Color(0.08f, 0.12f, 0.18f));
        boardScreen.SetColor("_Tint", screen);
        boardFrame.SetColor("_Tint", Color.white);

        // Ambient audio (optional clip at Resources/Audio/<audio>).
        if (ambience == null) ambience = GetComponent<AudioSource>();
        if (ambience == null) ambience = gameObject.AddComponent<AudioSource>();
        var clip = string.IsNullOrEmpty(t.audio) ? null : Resources.Load<AudioClip>($"Audio/{t.audio}");
        ambience.loop = true;
        ambience.volume = 0.4f;
        if (ambience.clip != clip) { ambience.clip = clip; if (clip != null && Application.isPlaying) ambience.Play(); }
        if (clip == null) ambience.Stop();

        ctx.life.Tick(Application.isPlaying ? Time.time : 3f, 0f); // initial poses (also in edit mode)
        PlayerPrefs.SetString("loft.theme", id);
        Debug.Log($"[World] theme -> {t.name ?? id} ({ctx.Triangles} tris)");
        return true;
    }

    static void ConfigureSky(Material sky, Material water, Theme t)
    {
        var s = t.sky;
        var hor = LoftUtil.Hex(s.horizon, Color.white);
        sky.SetColor("_Zenith", LoftUtil.Hex(s.zenith, Color.blue));
        sky.SetColor("_Mid", LoftUtil.Hex(s.mid, Color.cyan));
        sky.SetColor("_Horizon", hor);
        sky.SetColor("_Below", LoftUtil.Hex(s.below, hor));
        sky.SetColor("_SunColor", LoftUtil.Hex(s.sunColor, Color.white));
        sky.SetColor("_Neb1", LoftUtil.Hex(s.nebA, Color.magenta));
        sky.SetColor("_Neb2", LoftUtil.Hex(s.nebB, Color.blue));
        float az = s.sunAz * Mathf.Deg2Rad, el = s.sunEl * Mathf.Deg2Rad;
        var dir = new Vector4(-Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), -Mathf.Cos(az) * Mathf.Cos(el), 0f);
        sky.SetVector("_SunDir", dir);
        sky.SetFloat("_SunSize", s.sunSize); sky.SetFloat("_Glow", s.glow);
        sky.SetFloat("_Stars", s.stars); sky.SetFloat("_Neb", s.neb); sky.SetFloat("_Exposure", s.exposure);
        var w = t.water;
        water.SetColor("_Shallow", LoftUtil.Hex(w.shallow, Color.cyan));
        water.SetColor("_Deep", LoftUtil.Hex(w.deep, Color.blue));
        water.SetColor("_SkyH", hor);
        water.SetColor("_SkyZ", LoftUtil.Hex(s.mid, Color.cyan));
        water.SetColor("_SunColor", LoftUtil.Hex(s.sunColor, Color.white));
        water.SetVector("_SunDir", dir);
        water.SetFloat("_Amp", w.amp); water.SetFloat("_Speed", w.speed); water.SetFloat("_Glint", w.glint); water.SetFloat("_Foam", w.foam);
        water.SetFloat("_Exposure", s.exposure);
    }

    // k 0 = the theme's own light, 1 = club dark (party). Scene materials keep
    // reading the global ambient/sun, so they dim with it; glowing things stay bright.
    public void SetMood(float k, Color? tint = null)
    {
        if (Current == null) return;
        var dark = tint ?? new Color(0.14f, 0.05f, 0.22f);
        RenderSettings.ambientSkyColor = Color.Lerp(ambSky, dark * 1.4f, k);
        RenderSettings.ambientEquatorColor = Color.Lerp(ambEq, dark, k);
        RenderSettings.ambientGroundColor = Color.Lerp(ambGnd, dark * 0.6f, k);
        RenderSettings.fogColor = Color.Lerp(fogCol, dark * 0.5f, k);
        RenderSettings.fogDensity = fogDen;
        sun.color = sunCol;
        sun.intensity = Mathf.Lerp(sunInt, 0.1f, k);
        float ex = Mathf.Lerp(Current.sky.exposure, 0.12f, k);
        if (ctx != null && ctx.sky != null) { ctx.sky.SetFloat("_Exposure", ex); ctx.water.SetFloat("_Exposure", ex); }
    }

    public string NextTheme()
    {
        var ids = ThemeIds();
        if (ids.Length == 0) return null;
        int i = Array.IndexOf(ids, Current?.id);
        var next = ids[(i + 1) % ids.Length];
        if (Application.isPlaying) { FadeToTheme(next); return next; }
        return ApplyTheme(next) ? next : null;
    }

    // Fade to black, swap the scene while the screen is dark, fade back in.
    public void FadeToTheme(string id)
    {
        if (!Application.isPlaying || cam == null) { ApplyTheme(id); return; }
        if (fading || LoadTheme(id) == null) return;
        StartCoroutine(FadeRoutine(id));
    }

    IEnumerator FadeRoutine(string id)
    {
        fading = true;
        BuildFade();
        fadeQuad.gameObject.SetActive(true);
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.3f) { SetFade(t); yield return null; }
        SetFade(1f);
        ApplyTheme(id);
        yield return null;
        for (float t = 1f; t > 0f; t -= Time.unscaledDeltaTime / 0.45f) { SetFade(t); yield return null; }
        fadeQuad.gameObject.SetActive(false);
        fading = false;
    }

    void SetFade(float a) => fadeMat.SetColor("_Tint", new Color(0.02f, 0.02f, 0.03f, Mathf.SmoothStep(0f, 1f, a)));
}
