// VIRTUAL LOFT — build context for one generated scene: owns every mesh and
// material it creates so a scene switch can free them (no leaks).
using System.Collections.Generic;
using UnityEngine;

public class WorldCtx
{
    public readonly GameObject root;
    public readonly Theme theme;
    public readonly WorldLife life;
    public Material sky, water, glow, leaf, islands, windows; // set by LoftWorld and the scenes
    public readonly List<Material> foam = new List<Material>();
    readonly List<Object> owned = new List<Object>();
    static Shader lit, unlit, skySh, waterSh;

    public WorldCtx(Theme t)
    {
        theme = t;
        root = New("ThemeProps", null);
        life = root.AddComponent<WorldLife>();
        if (lit == null) lit = Resources.Load<Shader>("Shaders/LoftLit");
        if (unlit == null) unlit = Resources.Load<Shader>("Shaders/LoftUnlit");
        if (skySh == null) skySh = Resources.Load<Shader>("Shaders/LoftSky");
        if (waterSh == null) waterSh = Resources.Load<Shader>("Shaders/LoftWater");
    }

    public static GameObject New(string name, Transform parent)
    {
        var go = new GameObject(name);
        if (!Application.isPlaying) go.hideFlags = HideFlags.DontSaveInEditor; // generated: never saved into Loft.unity
        if (parent != null) go.transform.SetParent(parent, false);
        return go;
    }

    public Transform Group(string name, Transform parent = null, Vector3 pos = default)
    {
        var g = New(name, parent != null ? parent : root.transform).transform;
        g.localPosition = pos;
        return g;
    }

    T Own<T>(T o) where T : Object { owned.Add(o); return o; }

    public Material Lit(float sway = 0f, float speed = 1.2f, Color? tint = null)
    {
        var m = Own(new Material(lit) { name = "w_lit" });
        m.SetFloat("_SwayAmp", sway); m.SetFloat("_SwaySpeed", speed);
        m.SetColor("_Tint", tint ?? Color.white);
        return m;
    }

    // mode: 0 opaque, 1 alpha blend, 2 additive.
    public Material Unlit(Color? tint = null, float gain = 1f, int mode = 0, float blink = 0f, float scroll = 0f, int queue = -1)
    {
        var m = Own(new Material(unlit) { name = "w_unlit" });
        m.SetColor("_Tint", tint ?? Color.white);
        m.SetFloat("_Gain", gain); m.SetFloat("_Blink", blink); m.SetFloat("_Scroll", scroll);
        if (mode == 0) { m.SetFloat("_Src", 1); m.SetFloat("_Dst", 0); m.SetFloat("_ZW", 1); m.SetFloat("_FogAdd", 1); }
        else
        {
            m.SetFloat("_Src", mode == 1 ? 5 : 1); m.SetFloat("_Dst", mode == 1 ? 10 : 1);
            m.SetFloat("_ZW", 0); m.SetFloat("_FogAdd", mode == 2 ? 2 : 1);
            m.renderQueue = queue > 0 ? queue : 2500;
        }
        if (queue > 0) m.renderQueue = queue;
        return m;
    }

    public Material Sky(bool panorama = false) => sky = Own(new Material(panorama ? Resources.Load<Shader>("Shaders/LoftPano") : skySh) { name = "w_sky" });
    public Material Water(bool panorama = false) => water = Own(new Material(panorama ? Resources.Load<Shader>("Shaders/LoftOcean") : waterSh) { name = "w_water" });
    public T Keep<T>(T o) where T : Object => Own(o);

    public GameObject Mesh(Mk mk, string name, Material mat, Transform parent = null, Vector3 pos = default)
    {
        var go = New(name, parent != null ? parent : root.transform);
        go.transform.localPosition = pos;
        var mesh = Own(mk.Build(name));
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        Triangles += mk.Tris;
        return go;
    }
    public int Triangles;

    public void Dispose()
    {
        foreach (var o in owned) LoftUtil.Kill(o);
        owned.Clear();
        LoftUtil.Kill(root);
    }
}
