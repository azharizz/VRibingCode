// VIRTUAL LOFT — PARTY MODE. The "we shipped it!" moment, on demand.
//
// SetParty(true): lights dim to club purple, a disco ball drops in with a
// bounce and spins, 3 colored spots sweep, the dance floor lights up and
// pulses on the beat, confetti falls, a code-made 120 BPM beat plays, the
// wall says LAUNCH / PARTY!, and every agent hops over to the dance floor and
// dances (Animator State 4 "Party"). SetParty(false) puts everything back
// (lights, theme, agents to their seats, real work state).
// Same layout as the Blender preview (animalgen/party_preview.py).
// Triggered by LoftDesk (PARTY button / P key / server "party" event, so
// every connected headset parties together).

using System.Collections.Generic;
using UnityEngine;

public class LoftParty : MonoBehaviour
{
    public static readonly Vector3 Center = new Vector3(0f, 0f, 3.4f);
    const float BPM = 120f, BALL_Y = 2.55f, BALL_START_Y = 6f;
    static readonly Color[] Palette =
    {
        new Color(1f, 0.05f, 0.4f), new Color(0f, 0.7f, 1f), new Color(1f, 0.7f, 0f), new Color(0.2f, 1f, 0.15f), new Color(0.5f, 0.15f, 1f),
    };

    public bool On { get; private set; }
    public AgentManager agents;
    public LoftWorld world;

    Transform root, ball;
    readonly List<Light> spots = new List<Light>();
    readonly List<Material> tiles = new List<Material>();
    ParticleSystem confetti;
    AudioSource music;
    TextMesh[] banner;
    float t0;
    // saved room lighting
    Color savedAmbient, savedBg;
    float savedSun;
    bool savedFog;

    void Awake()
    {
        if (agents == null) agents = FindFirstObjectByType<AgentManager>();
        if (world == null) world = FindFirstObjectByType<LoftWorld>();
    }

    public void Toggle() => SetParty(!On);

    public void SetParty(bool on)
    {
        if (on == On) return;
        On = on;
        if (on) Begin(); else End();
        if (agents != null) agents.SetParty(on);
    }

    void Begin()
    {
        t0 = Time.time;
        Build();
        root.gameObject.SetActive(true);
        savedAmbient = RenderSettings.ambientLight;
        savedFog = RenderSettings.fog;
        var sun = Sun();
        savedSun = sun != null ? sun.intensity : -1f;
        if (world != null && world.cam != null) savedBg = world.cam.backgroundColor;
        ball.localPosition = new Vector3(0f, BALL_START_Y, 0f);
        confetti.Clear();
        confetti.Play();
        LoftAudio.Play("sfx_party_horn", 0.9f);
        LoftAudio.Play("sfx_confetti_pop", 0.9f);
        if (music.clip != null) music.PlayDelayed(0.55f); // after the horn, like the preview
    }

    void End()
    {
        if (root == null) return;
        root.gameObject.SetActive(false);
        confetti.Stop();
        music.Stop();
        RenderSettings.ambientLight = savedAmbient;
        RenderSettings.fog = savedFog;
        var sun = Sun();
        if (sun != null && savedSun >= 0f) sun.intensity = savedSun;
        if (world != null && world.cam != null) world.cam.backgroundColor = savedBg;
        if (world != null && world.Current != null) world.ApplyTheme(world.Current.id); // exact theme restore
    }

    void Update()
    {
        if (!On || root == null) return;
        float t = Time.time - t0;
        float beat = t * BPM / 60f;
        float pulse = Mathf.Pow(1f - Mathf.Repeat(beat, 1f), 3f); // kick on every beat

        // Club lighting fades in over 0.4 s.
        float k = Mathf.Clamp01(t / 0.4f);
        RenderSettings.ambientLight = Color.Lerp(savedAmbient, new Color(0.12f, 0.04f, 0.2f), k);
        RenderSettings.fog = false;
        var sun = Sun();
        if (sun != null && savedSun >= 0f) sun.intensity = Mathf.Lerp(savedSun, 0.08f, k);
        if (world != null && world.cam != null) world.cam.backgroundColor = Color.Lerp(savedBg, new Color(0.03f, 0.01f, 0.05f), k);

        // Disco ball: drop with a little bounce after 0.15 s, then spin.
        float d = Mathf.Clamp01((t - 0.15f) / 0.5f);
        float y = Mathf.LerpUnclamped(BALL_START_Y, BALL_Y, EaseOutBack(d));
        ball.localPosition = new Vector3(0f, y, 0f);
        ball.Rotate(0f, 70f * Time.deltaTime, 0f, Space.World);

        // Spots sweep across the dancers.
        for (int i = 0; i < spots.Count; i++)
        {
            float ph = t * 1.7f + i * 1.6f;
            var target = Center + new Vector3(Mathf.Sin(ph) * 2.2f, 0f, -1.2f - Mathf.Cos(ph * 0.7f));
            spots[i].transform.rotation = Quaternion.LookRotation(target - spots[i].transform.position);
            spots[i].intensity = Mathf.Lerp(0f, 6f, k) * (0.7f + 0.3f * pulse);
        }

        // Dance floor: each tile flips color/brightness every beat.
        int b = Mathf.FloorToInt(beat);
        for (int i = 0; i < tiles.Count; i++)
        {
            int h = (i * 7919 + b * 104729) & 0xffff;
            float on = (h % 3 == 0) ? 0f : (h % 3 == 1 ? 0.6f : 1.6f);
            var c = Palette[(h >> 4) % Palette.Length];
            tiles[i].SetColor("_EmissionColor", c * on * (0.6f + 0.4f * pulse) * k);
        }
        var bc = Color.Lerp(Color.clear, new Color(1f, 0.25f, 0.65f), Mathf.Clamp01((t - 0.5f) / 0.3f));
        foreach (var w in banner) w.color = bc; // per-mesh color: never touch the shared font material
    }

    Light sunCache;
    Light Sun()
    {
        if (sunCache == null)
        {
            var go = GameObject.Find("Directional Light"); // LoftWorld's key light
            if (go != null) sunCache = go.GetComponent<Light>();
        }
        return sunCache;
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }

    // ---- props (built once, hidden between parties) ----
    void Build()
    {
        if (root != null) return;
        root = new GameObject("PartyMode").transform;
        root.position = Center;

        // Disco ball on a rod: faceted (low-poly sphere, flat shaded) + glossy.
        ball = new GameObject("DiscoBall").transform;
        ball.SetParent(root, false);
        var sphere = LoftUtil.Prim(PrimitiveType.Sphere, "Ball", ball, Vector3.zero, Vector3.one * 0.76f, new Color(0.85f, 0.85f, 0.95f));
        var bm = sphere.GetComponent<Renderer>().sharedMaterial;
        bm.SetFloat("_Metallic", 0.9f);
        bm.SetFloat("_Glossiness", 0.95f);
        bm.EnableKeyword("_EMISSION");
        bm.SetColor("_EmissionColor", new Color(0.35f, 0.35f, 0.45f));
        LoftUtil.Prim(PrimitiveType.Cylinder, "Rod", ball, new Vector3(0f, 1.4f, 0f), new Vector3(0.03f, 1.0f, 0.03f), new Color(0.2f, 0.2f, 0.2f));

        // Dance floor 7 x 5 tiles under the dancers (in front of their desks).
        for (int ix = -3; ix <= 3; ix++)
            for (int iz = -2; iz <= 2; iz++)
            {
                var tile = LoftUtil.Prim(PrimitiveType.Cube, "Tile", root, new Vector3(ix * 0.62f, 0.006f, -0.9f - iz * 0.62f), new Vector3(0.58f, 0.01f, 0.58f), new Color(0.03f, 0.03f, 0.035f));
                var m = tile.GetComponent<Renderer>().sharedMaterial;
                m.EnableKeyword("_EMISSION");
                tiles.Add(m);
            }

        // 3 colored spots. ponytail: realtime spot lights cost a pass each on
        // Quest's forward renderer; 3 is fine for a short party, drop to 1-2 if fps dips.
        for (int i = 0; i < 3; i++)
        {
            var l = new GameObject($"Spot{i}").AddComponent<Light>();
            l.transform.SetParent(root, false);
            l.transform.localPosition = new Vector3(-3f + 3f * i, 4.2f, 1.2f);
            l.type = LightType.Spot;
            l.color = Palette[i];
            l.spotAngle = 40f;
            l.range = 9f;
            l.shadows = LightShadows.None;
            spots.Add(l);
        }

        // Confetti: one particle system, one draw call.
        var cgo = new GameObject("Confetti");
        cgo.transform.SetParent(root, false);
        cgo.transform.localPosition = new Vector3(0f, 4.6f, -0.6f);
        confetti = cgo.AddComponent<ParticleSystem>();
        confetti.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = confetti.main;
        main.startLifetime = 4f;
        main.startSpeed = 0.2f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = 0.12f;
        main.maxParticles = 600;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Palette[0], 0f), new GradientColorKey(Palette[1], 0.25f), new GradientColorKey(Palette[2], 0.5f),
                             new GradientColorKey(Palette[3], 0.75f), new GradientColorKey(Palette[4], 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        main.startColor = new ParticleSystem.MinMaxGradient(grad) { mode = ParticleSystemGradientMode.RandomColor };
        var emission = confetti.emission;
        emission.rateOverTime = 70f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 180) });
        var shape = confetti.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(6.5f, 0.1f, 3.8f);
        var rot = confetti.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
        var noise = confetti.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        var pr = cgo.GetComponent<ParticleSystemRenderer>();
        var shader = Shader.Find("Sprites/Default");
        if (shader != null) pr.sharedMaterial = new Material(shader);

        // LAUNCH  (ball)  PARTY! on the main wall.
        var words = new GameObject("Banner").transform;
        words.SetParent(root, false);
        words.localPosition = new Vector3(0f, 3.15f, -3.65f); // just in front of the wall face
        var left = LoftUtil.Text(words, "Launch", new Vector3(-2.4f, 0f, 0f), 80, 0.012f, Color.clear);
        left.text = "LAUNCH";
        var right = LoftUtil.Text(words, "Party", new Vector3(2.4f, 0f, 0f), 80, 0.012f, Color.clear);
        right.text = "PARTY!";
        banner = new[] { left, right };

        // Music: a 120 BPM kick + hi-hat loop generated in code (or Resources/Audio/party).
        music = cgo.AddComponent<AudioSource>();
        music.loop = true;
        music.volume = 0.5f;
        music.spatialBlend = 0f;
        var own = Resources.Load<AudioClip>("Audio/party");
        music.clip = own != null ? own : MakeBeat(); // audiogen's full track; code beat as fallback

        root.gameObject.SetActive(false);
    }

    // 2 bars of 4/4 at 120 BPM: kick on every beat, hi-hat on the off-beats.
    static AudioClip MakeBeat()
    {
        const int rate = 22050;
        int beatLen = Mathf.RoundToInt(rate * 60f / BPM), n = beatLen * 8;
        var data = new float[n];
        var rng = new System.Random(3);
        for (int i = 0; i < n; i++)
        {
            int inBeat = i % beatLen;
            float tk = inBeat / (float)rate;
            float kick = Mathf.Sin(2f * Mathf.PI * (55f * tk + 90f * (1f - Mathf.Exp(-tk * 30f)) / 30f)) * Mathf.Exp(-tk * 9f);
            int off = (i + beatLen / 2) % beatLen;
            float th = off / (float)rate;
            float hat = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-th * 60f) * 0.25f;
            data[i] = Mathf.Clamp(kick * 0.8f + hat, -1f, 1f);
        }
        var clip = AudioClip.Create("party-beat", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
