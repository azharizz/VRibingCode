// VIRTUAL LOFT — LoftKeepUp.cs: idle agents sometimes keep a ball in the air, and now and then
// pass it to the CEO. The ball follows scripted arcs, not physics, so every touch lands on the
// animal. Work always wins: a busy agent leaves at once. The ball never flies at the CEO's face.

using System.Collections.Generic;
using UnityEngine;
using VirtualLoft.XR;

public class LoftKeepUp : MonoBehaviour
{
    public const float BALL_R = 0.15f;
    public static readonly Vector3 Center = new Vector3(0f, 0f, 3.1f); // open deck between the seat and the view
    public static float MinWait = 180f, MaxWait = 300f;                // idle minutes between games

    enum Phase { Waiting, Gathering, Flying, ToCeo, AtCeo, Dropped }
    Phase phase = Phase.Waiting;

    readonly List<AgentSkin> players = new List<AgentSkin>();
    Transform ball;
    Renderer ballR;
    string ballScene;
    Vector3 from, to, rest;
    float flightT, flightTime, apex, nextGame, gatherUntil, endAt, atCeoUntil, dropT;
    int current = -1, next = -1, touches, streak;
    LoftBody.Touch nextTouch;
    bool touched;

    public bool Playing => phase != Phase.Waiting;
    public IReadOnlyList<AgentSkin> Players => players;
    public Vector3 BallPos => ball != null ? ball.position : Vector3.zero;

    // Height over the ground, with a flat start and end at `a` and `b`.
    public static Vector3 Arc(Vector3 a, Vector3 b, float height, float t)
    {
        var p = Vector3.Lerp(a, b, t);
        p.y += 4f * height * t * (1f - t);
        return p;
    }

    public static float TouchHeight(LoftBody.Touch t) => t == LoftBody.Touch.Head ? 1.62f : t == LoftBody.Touch.Knee ? 0.62f : 0.34f;

    void Start() => nextGame = Time.time + Random.Range(45f, 90f); // the first one comes soon, so it gets noticed

    void Update()
    {
        if (phase == Phase.Waiting)
        {
            if (Time.time >= nextGame) { nextGame = Time.time + Random.Range(MinWait, MaxWait); TryStart(); }
            return;
        }
        if (Blocked()) { End(false); return; }
        for (int i = players.Count - 1; i >= 0; i--)
            if (players[i] == null || !Free(players[i])) Leave(i);
        if (players.Count < 2 && phase != Phase.ToCeo && phase != Phase.AtCeo) { End(true); return; }

        ball.Rotate(Vector3.right, 240f * Time.deltaTime, Space.Self);
        switch (phase)
        {
            case Phase.Gathering: Gather(); break;
            case Phase.Flying: Fly(); break;
            case Phase.ToCeo: FlyToCeo(); break;
            case Phase.AtCeo: Hover(); break;
            case Phase.Dropped: Fetch(); break;
        }
    }

    static bool Free(AgentSkin s) => s.State == AgentState.Idle && !s.Partying;

    static bool Blocked()
    {
        var li = LoftInteraction.Current;
        return li != null && li.cinema != null && li.cinema.Active;
    }

    public bool TryStart()
    {
        var mgr = AgentManager.Instance;
        if (mgr == null || Playing || Blocked()) return false;
        var idle = new List<AgentSkin>();
        foreach (var a in mgr.agents)
        {
            var s = a != null ? a.GetComponent<AgentSkin>() : null;
            if (s != null && Free(s)) idle.Add(s);
        }
        if (idle.Count < 2) return false;
        int n = Mathf.Min(idle.Count, Random.Range(2, 5));
        players.Clear();
        while (players.Count < n)
        {
            int i = Random.Range(0, idle.Count);
            players.Add(idle[i]);
            idle.RemoveAt(i);
        }
        for (int i = 0; i < n; i++)
        {
            float a = (i / (float)n) * Mathf.PI * 2f + 0.5f;
            players[i].JoinGame(Center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.25f, Center);
        }
        EnsureBall();
        ball.gameObject.SetActive(false);
        phase = Phase.Gathering;
        gatherUntil = Time.time + 16f;
        endAt = Time.time + Random.Range(35f, 75f);
        touches = streak = 0;
        return true;
    }

    void Gather()
    {
        bool all = true;
        foreach (var p in players) all &= p.AtGameSpot;
        if (!all && Time.time < gatherUntil) return;
        current = 0;
        var p0 = players[0];
        ball.gameObject.SetActive(true);
        from = p0.transform.position + p0.transform.forward * 0.25f + Vector3.up * 0.9f;
        ball.position = from;
        Pass(PickNext(), 0.9f);
    }

    int PickNext()
    {
        if (players.Count == 1) return 0;
        // now and then an animal juggles it alone
        if (Random.value < 0.22f) return Mathf.Clamp(current, 0, players.Count - 1);
        int n;
        do n = Random.Range(0, players.Count); while (n == current);
        return n;
    }

    void Pass(int who, float time)
    {
        next = who;
        var t = players[who];
        nextTouch = (LoftBody.Touch)Random.Range(1, 4);
        from = ball.position;
        to = t.transform.position + t.transform.forward * (nextTouch == LoftBody.Touch.Head ? 0.06f : 0.32f) + Vector3.up * TouchHeight(nextTouch);
        flightT = 0f; flightTime = time; touched = false;
        apex = who == current ? Random.Range(0.5f, 0.8f) : Random.Range(0.9f, 1.5f);
        phase = Phase.Flying;
    }

    void Fly()
    {
        flightT += Time.deltaTime / flightTime;
        ball.position = Arc(from, to, apex, Mathf.Clamp01(flightT));
        if (next >= players.Count) next = 0;
        var who = players[next];
        if (!touched && flightT * flightTime >= flightTime - 0.2f)
        {
            touched = true;
            who.Body?.Play(nextTouch, Random.value < 0.5f);
        }
        if (flightT < 1f) return;

        current = next;
        touches++; streak++;
        LoftAudio.PlayAt("sfx_click", ball.position, 0.22f);
        if (streak == 10) who.Say("Ten!", 2f);
        if (streak == 20) who.Say("Twenty! Nice!", 2.5f);
        if (Time.time >= endAt) { End(true); return; }
        if (touches > 3 && Random.value < 0.07f) { Drop(); return; }
        if (touches > 4 && touches % 7 == 0 && CeoHere() && Random.value < 0.6f) { PassToCeo(); return; }
        Pass(PickNext(), Random.Range(0.75f, 1.05f));
    }

    static Camera Cam => Camera.main;
    static bool CeoHere() => Cam != null && Vector3.Distance(Cam.transform.position, Center) < 7f;

    void PassToCeo()
    {
        var head = Cam.transform;
        var fwd = head.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude < 0.01f ? Vector3.back : fwd.normalized;
        from = ball.position;
        // in front and below the eyes, an easy reach and never at the face
        to = head.position + fwd * 0.72f + Vector3.down * 0.28f;
        flightT = 0f; flightTime = 1.5f; apex = 0.7f;
        phase = Phase.ToCeo;
    }

    void FlyToCeo()
    {
        flightT += Time.deltaTime / flightTime;
        ball.position = Arc(from, to, apex, Mathf.Clamp01(flightT));
        if (HandHit()) { Return(); return; }
        if (flightT < 1f) return;
        rest = ball.position;
        atCeoUntil = Time.time + 2.2f;
        phase = Phase.AtCeo;
    }

    void Hover()
    {
        ball.position = rest + Vector3.up * Mathf.Sin(Time.time * 3f) * 0.025f; // a slow drift gives the CEO time
        if (HandHit()) { Return(); return; }
        if (Time.time >= atCeoUntil) Drop(); // no penalty: an agent just fetches it
    }

    bool HandHit()
    {
        var li = LoftInteraction.Current;
        return li != null && li.BallHit(ball.position, BALL_R + 0.13f);
    }

    public void Return()
    {
        if (phase != Phase.ToCeo && phase != Phase.AtCeo) return;
        LoftAudio.Play("sfx_click", 0.5f);
        if (players.Count == 0) { End(true); return; }
        touches++; streak++;
        current = -1;
        players[Random.Range(0, players.Count)].Say("Nice one!", 2f);
        Pass(Random.Range(0, players.Count), 1.2f);
    }

    void Drop()
    {
        streak = 0;
        from = ball.position;
        to = new Vector3(from.x, BALL_R, from.z);
        var away = from - Center; away.y = 0f;
        to += (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward) * 0.7f;
        to = Plan.ClampToWalk(to);
        dropT = 0f;
        phase = Phase.Dropped;
        if (players.Count > 0)
        {
            var who = players[Mathf.Clamp(current, 0, players.Count - 1)];
            who.Say(Random.value < 0.5f ? "Oops!" : "Whoops!", 1.8f);
        }
    }

    void Fetch()
    {
        dropT += Time.deltaTime;
        ball.position = Arc(from, to, 0.08f, Mathf.Clamp01(dropT / 0.8f));
        if (dropT < 1.8f) return;
        if (players.Count < 2) { End(true); return; }
        from = to + Vector3.up * 0.9f;
        current = Random.Range(0, players.Count);
        ball.position = from;
        Pass(PickNext(), 1f);
    }

    void Leave(int i)
    {
        players[i]?.LeaveGame();
        players.RemoveAt(i);
        if (next >= players.Count) next = 0;
        if (current >= players.Count) current = players.Count - 1;
    }

    public void End(bool wave)
    {
        foreach (var p in players) if (p != null) p.LeaveGame();
        if (wave && players.Count > 0 && players[0] != null) players[0].Say("Good game!", 2f);
        players.Clear();
        if (ball != null) ball.gameObject.SetActive(false);
        phase = Phase.Waiting;
    }

    void EnsureBall()
    {
        var world = FindFirstObjectByType<LoftWorld>();
        var scene = world != null && world.Current != null ? world.Current.id : "beach";
        if (ball != null && ballScene == scene) return;
        if (ball != null) { ball.gameObject.SetActive(false); LoftUtil.Kill(ball.gameObject); }
        ballScene = scene;

        var m = new Mk(5);
        bool glow = scene == "galaxy" || scene == "city";
        Color[] stripes = scene == "beach" ? new[] { Mk.Hex("#F2735A"), Mk.Hex("#FFFFFF"), Mk.Hex("#2BA7A3"), Mk.Hex("#FFFFFF"), Mk.Hex("#F7C948"), Mk.Hex("#FFFFFF") }
            : scene == "galaxy" ? new[] { Mk.Hex("#7FD4FF"), Mk.Hex("#3E6BFF") }
            : scene == "city" ? new[] { Mk.Hex("#FF7AC8"), Mk.Hex("#5FE6FF") }
            : new[] { Mk.Hex("#FFFFFF"), Mk.Hex("#C9663A") };
        m.Sphere(Vector3.zero, BALL_R, 10, 18, n =>
        {
            float a = Mathf.Atan2(n.z, n.x) / (Mathf.PI * 2f) + 0.5f;
            return stripes[Mathf.Clamp((int)(a * stripes.Length), 0, stripes.Length - 1)];
        });
        var go = new GameObject("KeepUpBall");
        go.AddComponent<MeshFilter>().sharedMesh = m.Build("keepup_ball");
        ballR = go.AddComponent<MeshRenderer>();
        ballR.sharedMaterial = new Material(Resources.Load<Shader>(glow ? "Shaders/LoftUnlit" : "Shaders/LoftLit")) { name = "keepup_ball" };
        var c = go.AddComponent<SphereCollider>();
        c.radius = BALL_R * 1.8f; // an easy target for the laser
        go.AddComponent<BallTarget>().game = this;
        ball = go.transform;
    }

    // Laser plus trigger: the backup for anyone who doesn't want to swing.
    class BallTarget : MonoBehaviour, IPointerTarget
    {
        public LoftKeepUp game;
        public void OnHover(bool on) { }
        public void OnPress(Ray r, RaycastHit h) => game.Return();
        public void OnDrag(Ray r) { }
        public void OnRelease() { }
    }
}
