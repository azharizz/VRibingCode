// VIRTUAL LOFT — LoftWeather.cs: the beach's moods. One mood sets the sky, sun, fog, water,
// lanterns, rain and sound together, so "make it stormy" changes the whole world at once.

using System.Linq;
using UnityEngine;

public class LoftWeather : MonoBehaviour
{
    public class Mood
    {
        public string id, sky;
        public float yaw, exposure, sat, sunI, fog, glint, amp, rain, lantern, foam, foamLight, wind, isle, warm;
        public Color sun, ambSky, ambEq, ambGnd, fogColor, deep, shallow;
        public Vector3 sunEuler;

        public static Mood Lerp(Mood a, Mood b, float t) => new Mood
        {
            id = b.id, sky = b.sky, yaw = b.yaw,
            exposure = Mathf.Lerp(a.exposure, b.exposure, t), sat = Mathf.Lerp(a.sat, b.sat, t), sunI = Mathf.Lerp(a.sunI, b.sunI, t),
            fog = Mathf.Lerp(a.fog, b.fog, t), glint = Mathf.Lerp(a.glint, b.glint, t), amp = Mathf.Lerp(a.amp, b.amp, t),
            rain = Mathf.Lerp(a.rain, b.rain, t), lantern = Mathf.Lerp(a.lantern, b.lantern, t), foam = Mathf.Lerp(a.foam, b.foam, t),
            foamLight = Mathf.Lerp(a.foamLight, b.foamLight, t), wind = Mathf.Lerp(a.wind, b.wind, t), isle = Mathf.Lerp(a.isle, b.isle, t), warm = Mathf.Lerp(a.warm, b.warm, t),
            sun = Color.Lerp(a.sun, b.sun, t), ambSky = Color.Lerp(a.ambSky, b.ambSky, t), ambEq = Color.Lerp(a.ambEq, b.ambEq, t),
            ambGnd = Color.Lerp(a.ambGnd, b.ambGnd, t), fogColor = Color.Lerp(a.fogColor, b.fogColor, t),
            deep = Color.Lerp(a.deep, b.deep, t), shallow = Color.Lerp(a.shallow, b.shallow, t),
            sunEuler = Quaternion.Slerp(Quaternion.Euler(a.sunEuler), Quaternion.Euler(b.sunEuler), t).eulerAngles,
        };
    }

    static Color H(string s) => LoftUtil.Hex(s, Color.white);

    public static readonly Mood[] Moods =
    {
        new Mood { id = "day", sky = "sky_day", yaw = 0f, exposure = 1f, sat = 1.05f, sunI = 1.15f, sun = H("#FFF0D8"), sunEuler = new Vector3(48f, 150f, 0f),
            ambSky = H("#BBD6F0"), ambEq = H("#D8CDBB"), ambGnd = H("#8A7A60"), fogColor = H("#DCE6F0"), fog = 0.0012f,
            deep = H("#0B4F7A"), shallow = H("#1E9AA8"), glint = 1.4f, amp = 0.2f, foam = 0.5f, rain = 0f, lantern = 1.1f, foamLight = 1f, wind = 0.34f, isle = 0.42f, warm = 0f },
        new Mood { id = "sunset", sky = "sky_sunset", yaw = -0.35f, exposure = 1.1f, sat = 1.15f, sunI = 1f, sun = H("#FFB070"), sunEuler = new Vector3(7f, 0f, 0f),
            ambSky = H("#8E8CB8"), ambEq = H("#D9A58A"), ambGnd = H("#5E4A50"), fogColor = H("#D9A98C"), fog = 0.0016f,
            deep = H("#2A3358"), shallow = H("#6A5A78"), glint = 2.4f, amp = 0.2f, foam = 0.35f, rain = 0f, lantern = 1.6f, foamLight = 0.6f, wind = 0.3f, isle = 0.8f, warm = 0.35f },
        new Mood { id = "storm", sky = "sky_storm", yaw = 0f, exposure = 0.5f, sat = 0.55f, sunI = 0.22f, sun = H("#8FA0B0"), sunEuler = new Vector3(60f, 170f, 0f),
            ambSky = H("#55606C"), ambEq = H("#4A525C"), ambGnd = H("#2E3238"), fogColor = H("#3E474F"), fog = 0.0055f,
            deep = H("#0F2029"), shallow = H("#1E3338"), glint = 0.15f, amp = 0.5f, foam = 0.9f, rain = 1f, lantern = 2f, foamLight = 0.22f, wind = 0.85f, isle = 0.7f, warm = 0.6f },
        new Mood { id = "night", sky = "sky_night", yaw = 0f, exposure = 0.5f, sat = 0.75f, sunI = 0.18f, sun = H("#8FA8D8"), sunEuler = new Vector3(40f, 200f, 0f),
            ambSky = H("#2A3450"), ambEq = H("#232B42"), ambGnd = H("#141826"), fogColor = H("#141C2C"), fog = 0.002f,
            deep = H("#060E1C"), shallow = H("#0C1A2C"), glint = 0.7f, amp = 0.16f, foam = 0.2f, rain = 0f, lantern = 2.3f, foamLight = 0.12f, wind = 0.22f, isle = 0.5f, warm = 1f },
    };

    // Space has no weather, just one sky. Its core sits straight ahead of the deck.
    static readonly Mood Space = new Mood { id = "space", sky = "sky_space", yaw = 0.5f, exposure = 2.4f, sat = 1.3f, sunI = 0.95f, sun = H("#C4D4FF"), sunEuler = new Vector3(35f, 170f, 0f),
        ambSky = H("#5668B4"), ambEq = H("#44488A"), ambGnd = H("#222250"), fogColor = Color.black, fog = 0f, deep = Color.black, shallow = Color.black,
        lantern = 1.5f, foamLight = 0f, wind = 0f, isle = 1f, warm = 0f };

    public static Mood Find(string id) => id == "space" ? Space : Moods.FirstOrDefault(m => m.id == id);

    // What differs between the buildings: their roofs, their light, and which moods they have.
    public class Place
    {
        public string start = "day";
        public string[] moods = { "day", "sunset", "storm", "night" };
        public float roofY = SceneKit.EAVE_Y, roofTop = SceneKit.RIDGE_Y + 0.3f, x = SceneKit.ROOF_HX, z0 = SceneKit.EAVE_FRONT, z1 = SceneKit.EAVE_BACK;
        public float detail = 0.8f, water = 1f, land = 1f;
        public Color warm = new Color(1f, 0.66f, 0.36f);
    }

    static readonly System.Collections.Generic.Dictionary<string, Place> Places = new System.Collections.Generic.Dictionary<string, Place>
    {
        { "beach", new Place() },
        { "scenery", new Place { start = "sunset", detail = 0.7f, water = 0.2f, land = 1.25f } }, // peaks look best at golden hour
        { "city", new Place { start = "sunset", roofY = 5.8f, roofTop = 6.3f, x = 13.2f, z0 = 0.1f, z1 = 18.2f, detail = 0.12f, water = 0f, warm = new Color(0.8f, 0.82f, 1f) } },
        { "galaxy", new Place { start = "space", moods = new[] { "space" }, roofY = 0f, roofTop = 0f, detail = 0f, water = 0f } },
    };

    public static Place PlaceFor(string scene) => scene != null && Places.TryGetValue(scene, out var p) ? p : null;
    public string[] Choices => Active && place.moods.Length > 1 ? place.moods : new string[0];

    // Under the roof: rain doesn't fall here.
    public static bool Dry(Vector3 p) => Mathf.Abs(p.x) < SceneKit.ROOF_HX && p.z > SceneKit.EAVE_FRONT && p.z < SceneKit.EAVE_BACK;

    public const float Change = 6f;
    const int Streaks = 900;

    public bool Active { get; private set; }
    public string Current => to?.id;
    Mood from, to;
    float k = 1f, flash, nextBolt, thunderAt;
    LoftWorld world;
    WorldCtx ctx;
    Place place;
    Material rain;
    AudioSource rainSound;

    public void Bind(LoftWorld w, WorldCtx c, bool on)
    {
        world = w; ctx = c;
        place = on ? PlaceFor(c.theme.scene ?? c.theme.id) : null;
        on = Active = place != null;
        if (rainSound != null) rainSound.volume = 0f;
        // The roof's shadow and the wood grain are for this scene only.
        Shader.SetGlobalFloat("_LoftRoofY", on ? place.roofY : 0f);
        Shader.SetGlobalFloat("_LoftRoofTop", on ? place.roofTop : 0f);
        Shader.SetGlobalFloat("_LoftDetailAmt", on ? place.detail : 0f);
        Shader.SetGlobalColor("_LoftWarm", Color.black);
        if (!on) return;
        Shader.SetGlobalVector("_LoftRoof", new Vector4(-place.x, place.x, place.z0, place.z1));
        Shader.SetGlobalTexture("_LoftDetail", Resources.Load<Texture2D>("Detail/wood_grain"));
        from = to = Find(place.start); k = 1f;
        Textures(to, to);
        BuildRain();
        Apply(to);
        var args = System.Environment.GetCommandLineArgs(); // -loftMood storm: for checking a look without a microphone
        int i = System.Array.IndexOf(args, "-loftMood");
        if (Application.isPlaying && i >= 0 && i + 1 < args.Length) Set(args[i + 1]);
    }

    public bool Set(string id)
    {
        var next = Find(id);
        if (!Active || next == null || !place.moods.Contains(id)) return false;
        var now = Mood.Lerp(from, to, k);
        now.sky = k < 0.5f ? from.sky : to.sky;
        now.yaw = k < 0.5f ? from.yaw : to.yaw;
        from = now; to = next; k = 0f;
        Textures(from, to);
        return true;
    }

    void Textures(Mood a, Mood b)
    {
        foreach (var m in new[] { ctx.sky, ctx.water })
        {
            m.SetTexture("_PanoA", Resources.Load<Cubemap>("Sky/" + a.sky));
            m.SetTexture("_PanoB", Resources.Load<Cubemap>("Sky/" + b.sky));
            m.SetVector("_RotA", Turn(a.yaw)); m.SetVector("_RotB", Turn(b.yaw));
            m.SetFloat("_Blend", 0f);
        }
    }

    static Vector4 Turn(float yaw) => new Vector4(Mathf.Cos(yaw * Mathf.PI * 2f), Mathf.Sin(yaw * Mathf.PI * 2f), 0f, 0f);

    void BuildRain()
    {
        var mesh = ctx.Keep(new Mesh { name = "Rain" });
        var pos = new Vector3[Streaks * 4]; var uv = new Vector2[pos.Length]; var tri = new int[Streaks * 6];
        var rng = new System.Random(5);
        for (int i = 0; i < Streaks; i++)
        {
            var s = new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble());
            for (int c = 0; c < 4; c++) { pos[i * 4 + c] = s; uv[i * 4 + c] = new Vector2(c & 1, c >> 1); }
            int v = i * 4, t = i * 6;
            tri[t] = v; tri[t + 1] = v + 2; tri[t + 2] = v + 1; tri[t + 3] = v + 1; tri[t + 4] = v + 2; tri[t + 5] = v + 3;
        }
        mesh.vertices = pos; mesh.uv = uv; mesh.triangles = tri;
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f); // placed in the shader, so never culled
        rain = ctx.Keep(new Material(Resources.Load<Shader>("Shaders/LoftRain")));
        rain.SetVector("_Dry", new Vector4(-place.x, place.x, place.z0, place.z1));
        var go = WorldCtx.New("Rain", ctx.root.transform);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = rain;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    void Apply(Mood m)
    {
        world.SetBase(m.ambSky, m.ambEq, m.ambGnd, m.sun, m.sunI, Quaternion.Euler(m.sunEuler), m.fogColor, m.fog, m.exposure);
        var w = ctx.water;
        w.SetColor("_Deep", m.deep); w.SetColor("_Shallow", m.shallow); w.SetColor("_SunColor", m.sun);
        w.SetFloat("_Glint", m.glint); w.SetFloat("_Amp", m.amp * place.water); w.SetFloat("_Foam", m.foam);
        w.SetVector("_SunDir", Quaternion.Euler(m.sunEuler) * Vector3.back);
        ctx.sky.SetFloat("_Sat", m.sat);
        Shader.SetGlobalColor("_LoftWarm", place.warm * m.warm);
        if (ctx.windows != null) ctx.windows.SetFloat("_Gain", m.lantern * 0.8f);
        if (ctx.glow != null) ctx.glow.SetFloat("_Gain", m.lantern);
        if (ctx.leaf != null) ctx.leaf.SetFloat("_SwayAmp", m.wind);
        if (ctx.islands != null) ctx.islands.SetColor("_Tint", new Color(m.isle, m.isle * 1.1f, m.isle) * place.land);
        foreach (var f in ctx.foam) f.SetFloat("_Gain", m.foamLight);
        if (rain != null) rain.SetFloat("_Rain", m.rain);
        if (Application.isPlaying) Sound(m.rain);
    }

    void Sound(float amount)
    {
        if (rainSound == null && amount > 0f)
        {
            rainSound = gameObject.AddComponent<AudioSource>();
            rainSound.clip = Resources.Load<AudioClip>("Audio/loft_ambient_focus_rain_loop");
            rainSound.loop = true; rainSound.volume = 0f; rainSound.spatialBlend = 0f;
            rainSound.Play();
        }
        if (rainSound != null) rainSound.volume = amount * 0.55f;
    }

    void Update()
    {
        if (!Active || ctx == null || ctx.sky == null) return;
        if (k < 1f)
        {
            k = Mathf.Min(1f, k + Time.deltaTime / Change);
            float e = Mathf.SmoothStep(0f, 1f, k);
            ctx.sky.SetFloat("_Blend", e); ctx.water.SetFloat("_Blend", e);
            Apply(Mood.Lerp(from, to, e));
        }
        Lightning();
    }

    void Lightning()
    {
        bool storm = to.id == "storm" && k >= 1f;
        if (storm && Time.time >= nextBolt)
        {
            nextBolt = Time.time + Random.Range(9f, 22f);
            flash = 1f;
            thunderAt = Time.time + Random.Range(0.8f, 2.4f);
        }
        if (thunderAt > 0f && Time.time >= thunderAt) { thunderAt = 0f; LoftAudio.Play("sfx_thunder", 0.9f); }
        if (flash <= 0f) return;
        flash = Mathf.Max(0f, flash - Time.deltaTime * 3.2f);
        float f = flash * (0.6f + 0.4f * Mathf.Sin(flash * 40f)); // real lightning flickers
        ctx.sky.SetFloat("_Flash", f);
    }
}
