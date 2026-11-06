// VIRTUAL LOFT — the animal you see for one agent.
//
// Real models: put a prefab at Assets/Resources/Skins/<skin>.prefab (fox,
// owl, cat, ... or any id the backend sends). If it has an Animator with an
// int parameter "State", this drives it: 0 Idle, 1 Working, 2 Done, 3 Failed
// (build those 4 states + transitions in the controller). No prefab yet ->
// a primitive placeholder animal animated in code, so the room always works.
// Nothing else touches the model: state flows in via AgentStateMachine.

using TMPro;
using UnityEngine;

[RequireComponent(typeof(AgentStateMachine))]
public class AgentSkin : MonoBehaviour
{
    public Vector3 home;
    public string skinId, displayName, role, line, model;
    public double? costUsd;
    public long tokens;
    public bool selected;

    static readonly int StateHash = Animator.StringToHash("State");
    public const int PARTY_STATE = 4; // Animator "State" 4 = Party dance
    public bool partying;
    Vector3 partySpot;
    float partyDelay;
    AgentStateMachine sm;
    Transform modelTf; // the 3D animal (string `model` = LLM model id)
    Animator animator;
    bool driveAnimator;
    TMP_Text label, info; // name (big) + role/state lines
    float phase, bornAt;
    public int slot;      // stable desk / lounge index, assigned by AgentManager
    float repathAt, stuckT;
    Transform tag;        // name plate + bubble: turns to face the viewer, grows with distance
    GameObject bubble; TMP_Text bubbleText; Transform bubblePlate; float bubbleUntil;

    void Awake() => Init();

    // Lazy too: AddComponent in edit mode (editor verify) skips Awake.
    void Init()
    {
        if (sm != null) return;
        bornAt = Time.time;
        sm = GetComponent<AgentStateMachine>();
        sm.OnStateChanged += OnState;
        phase = Random.value * 10f;
        // One pick collider for the CEO's ray/mouse (children have none).
        var box = gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.6f, 0f);
        box.size = new Vector3(0.9f, 1.2f, 0.9f);
        tag = new GameObject("Tag").transform;
        tag.SetParent(transform, false);
        MakePlate();
        label = LoftUtil.Label(tag, "Label", new Vector3(0f, 1.79f, 0f), 0.13f, Color.white, 1.0f);
        info = LoftUtil.Label(tag, "Info", new Vector3(0f, 1.535f, 0f), 0.1f, Color.white, 1.05f, TMPro.TextAlignmentOptions.Top, 0.34f);
        foreach (var t in new[] { label, info })
        {
            t.overflowMode = TMPro.TextOverflowModes.Ellipsis;
            t.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        }
        label.fontStyle = TMPro.FontStyles.Bold;
        MakeBubble();
    }

    // Dark rounded plate behind the text: readable over sky, sea or planks.
    static Mesh plateMesh;
    static Material plateMat;
    void MakePlate()
    {
        if (plateMesh == null)
        {
            var m = new Mk();
            float hw = 0.56f, hh = 0.31f, c = 0.07f;
            var pts = new[] { new Vector3(-hw + c, hh), new Vector3(hw - c, hh), new Vector3(hw, hh - c), new Vector3(hw, -hh + c),
                              new Vector3(hw - c, -hh), new Vector3(-hw + c, -hh), new Vector3(-hw, -hh + c), new Vector3(-hw, hh - c) };
            for (int i = 1; i < pts.Length - 1; i++) m.QuadFacing(pts[0], pts[i], pts[i + 1], pts[i + 1], Color.white, Color.white, Vector3.forward);
            plateMesh = m.Build("agent_plate");
            plateMat = new Material(Resources.Load<Shader>("Shaders/LoftUnlit")) { name = "agent_plate" };
            plateMat.SetColor("_Tint", new Color(0.03f, 0.05f, 0.1f, 0.86f));
            plateMat.SetFloat("_Src", 5); plateMat.SetFloat("_Dst", 10); plateMat.SetFloat("_ZW", 0); plateMat.SetFloat("_FogAdd", 0);
            plateMat.renderQueue = 2900;
        }
        var go = tag.Find("Plate")?.gameObject;
        if (go == null)
        {
            go = new GameObject("Plate");
            go.transform.SetParent(tag, false);
            go.AddComponent<MeshFilter>().sharedMesh = plateMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = plateMat;
        }
        go.transform.localPosition = new Vector3(0f, 1.62f, -0.03f);
        plate = go;
    }
    GameObject plate;

    void OnDestroy()
    {
        if (sm != null) sm.OnStateChanged -= OnState;
    }

    public void Apply(string skin, string name, string roleName)
    {
        Init();
        displayName = string.IsNullOrEmpty(name) ? sm.agentId : name;
        role = roleName;
        if (string.IsNullOrEmpty(skin)) skin = "fox";
        if (skin != skinId || modelTf == null) { skinId = skin; BuildModel(); }
        RefreshLabel();
    }

    public void SetLine(string s) { line = s; RefreshLabel(); }
    public void SetMeta(string modelId, double? cost, long? tokenCount)
    {
        model = modelId;
        costUsd = cost;
        tokens = tokenCount ?? 0;
        RefreshLabel();
    }
    public void SetSelected(bool on) { selected = on; RefreshLabel(); }

    // Party overrides the visual only: real work state keeps updating underneath
    // and comes back the moment the party ends.
    public void SetParty(bool on, Vector3 spot, float delay)
    {
        Init();
        if (on != partying) partyDelay = Time.time + delay; // already dancing: just slide to the new spot
        partying = on;
        partySpot = spot;
        ApplyAnimatorState();
    }

    void ApplyAnimatorState()
    {
        if (driveAnimator) animator.SetInteger(StateHash, partying ? PARTY_STATE : (int)sm.CurrentState);
    }

    void BuildModel()
    {
        if (modelTf != null) LoftUtil.Kill(modelTf.gameObject);
        var prefab = Resources.Load<GameObject>($"Skins/{skinId}");
        GameObject go;
        if (prefab != null)
        {   // the imported animals face -Z: wrap them so they face the CEO (+Z)
            go = new GameObject("Model");
            var inner = Instantiate(prefab);
            inner.name = "Mesh";
            inner.transform.SetParent(go.transform, false);
            inner.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }
        else go = Placeholder(skinId);
        modelTf = go.transform;
        modelTf.localScale = Vector3.one * 1.25f;
        modelTf.SetParent(transform, false);
        animator = go.GetComponentInChildren<Animator>();
        driveAnimator = false;
        if (animator != null)
            foreach (var p in animator.parameters)
                if (p.nameHash == StateHash && p.type == AnimatorControllerParameterType.Int) driveAnimator = true;
        OnState(sm.agentId, sm.CurrentState);
    }

    void OnState(string _, AgentState s)
    {
        ApplyAnimatorState();
        RefreshLabel();
        if (Time.time - bornAt < 1f) return; // no chorus when a whole team arrives on connect
        var at = transform.position + Vector3.up;
        if (s == AgentState.Working) { LoftAudio.PlayAt("voice_" + skinId, at, 0.7f); Say("On it!", 2.5f); } // "on it!" in its own voice
        else if (s == AgentState.Done) { LoftAudio.PlayAt("sfx_done", at); Say("Done. Ready for your review.", 4f); }
        else if (s == AgentState.Failed) { LoftAudio.PlayAt("sfx_failed", at); Say("That failed. Want me to retry?", 4f); }
    }

    void RefreshLabel()
    {
        if (label == null || sm == null) return;
        var s = sm.CurrentState;
        label.color = selected ? new Color(1f, 0.85f, 0.2f)
            : s == AgentState.Working ? new Color(1f, 0.95f, 0.6f)
            : s == AgentState.Done ? new Color(0.5f, 1f, 0.5f)
            : s == AgentState.Failed ? new Color(1f, 0.45f, 0.4f)
            : Color.white;
        label.text = (selected ? "> " : "") + displayName;
        info.color = label.color;
        string body = $"{role} · {LoftDesk.ShortModel(model)}\n{s.ToString().ToUpperInvariant()}{(costUsd > 0 ? $" ${costUsd:0.000}" : "")}";
        info.text = string.IsNullOrEmpty(line) ? body : $"{body}\n{LoftUtil.Trunc(line, 20)}";
    }

    // ---------------- speech bubble ----------------
    void MakeBubble()
    {
        bubble = new GameObject("Bubble");
        bubble.transform.SetParent(tag, false);
        bubblePlate = new GameObject("BubblePlate").transform;
        bubblePlate.SetParent(bubble.transform, false);
        bubblePlate.gameObject.AddComponent<MeshFilter>().sharedMesh = plateMesh;
        var m = new Material(Resources.Load<Shader>("Shaders/LoftUnlit")) { name = "bubble" };
        m.SetColor("_Tint", new Color(0.97f, 0.95f, 0.9f, 0.97f));
        m.SetFloat("_Src", 5); m.SetFloat("_Dst", 10); m.SetFloat("_ZW", 0); m.SetFloat("_FogAdd", 0); m.renderQueue = 2950;
        bubblePlate.gameObject.AddComponent<MeshRenderer>().sharedMaterial = m;
        bubblePlate.localPosition = new Vector3(0f, 0f, 0.012f);
        bubbleText = LoftUtil.Label(bubble.transform, "Text", new Vector3(0f, 0f, -0.02f), 0.095f, new Color(0.1f, 0.12f, 0.18f), 1.7f);
        bubbleText.overflowMode = TMPro.TextOverflowModes.Ellipsis;
        bubble.SetActive(false);
    }

    // Short speech bubble over the animal. Real events only (state changes, chat replies).
    public void Say(string text, float seconds = 4f)
    {
        Init();
        if (string.IsNullOrWhiteSpace(text)) return;
        bubbleText.text = LoftUtil.Trunc(text, 90);
        bubbleText.ForceMeshUpdate();
        float w = Mathf.Clamp(bubbleText.preferredWidth + 0.24f, 0.5f, 1.9f), h = bubbleText.preferredHeight + 0.2f;
        bubblePlate.localScale = new Vector3(w / 1.12f, h / 0.62f, 1f);
        bubble.transform.localPosition = new Vector3(0f, 2.0f + h / 2f, 0f);
        bubbleText.rectTransform.sizeDelta = new Vector2(1.7f, h);
        bubbleUntil = Time.time + Mathf.Max(0.5f, seconds);
        bubble.SetActive(true);
    }

    // ---------------- life: where to be, how to get there ----------------
    enum Want { None, Desk, Ceo, Idle, Party }
    Want want = Want.None;
    int wantKey = -1;
    Vector3 target, faceAt;
    readonly System.Collections.Generic.List<Vector3> path = new System.Collections.Generic.List<Vector3>();
    int pathI;
    float roamAt, blockedT, ignoreSepUntil, yawTurn;
    Vector3 ceoPosUsed; float ceoYawUsed;
    public bool Walking { get; private set; }
    public Vector3 Target => target;
    const float WALK = 1.15f;

    static Transform CeoRig()
    {
        var c = Camera.main;
        return c == null ? null : (c.transform.parent != null ? c.transform.parent : c.transform);
    }
    static Vector3 CeoPos() { var c = Camera.main; return c != null ? new Vector3(c.transform.position.x, 0f, c.transform.position.z) : Plan.Ceo; }
    static float CeoYaw() { var r = CeoRig(); return r != null ? r.eulerAngles.y : 180f; }

    // Which place does the real state ask for? Working -> desk, Done/Failed -> the CEO, Idle -> lounge / view edge.
    void Brain()
    {
        var mgr = AgentManager.Instance;
        Want w;
        if (partying) w = Time.time >= partyDelay ? Want.Party : want;
        else
        {
            var st = sm.CurrentState;
            w = st == AgentState.Working && slot < Plan.DeskSpots.Length ? Want.Desk
              : st == AgentState.Done || st == AgentState.Failed ? Want.Ceo : Want.Idle;
        }
        if (w == Want.None) w = Want.Idle;
        int key = w == Want.Desk ? slot : w == Want.Ceo ? (mgr != null ? mgr.CeoRank(this) : 0) : w == Want.Party ? Mathf.RoundToInt(partySpot.x * 50f + partySpot.z * 5000f) : -1;
        bool replan = w != want || key != wantKey;
        if (w == Want.Ceo)
        {
            var cp = CeoPos(); float cy = CeoYaw();
            if ((cp - ceoPosUsed).sqrMagnitude > 0.8f * 0.8f || Mathf.Abs(Mathf.DeltaAngle(cy, ceoYawUsed)) > 25f) replan = true;
        }
        if (w == Want.Idle && !Walking && Time.time > roamAt && want == Want.Idle) { replan = true; wantKey = -2; } // time to wander
        if (!replan) return;
        bool roam = wantKey == -2;
        want = w; wantKey = key;
        switch (w)
        {
            case Want.Desk:
                target = Plan.DeskSpots[slot]; faceAt = Plan.DeskPos[slot];
                break;
            case Want.Ceo:
            {
                var cp = CeoPos(); float cy = CeoYaw();
                ceoPosUsed = cp; ceoYawUsed = cy;
                int n = mgr != null ? Mathf.Max(1, mgr.CeoWaiting()) : 1, r = Mathf.Min(key, 11);
                float ring = 2.0f + (r / 6) * 0.8f; int inRing = Mathf.Min(6, n - (r / 6) * 6);
                float ang = (r % 6 - (Mathf.Max(inRing, 1) - 1) / 2f) * 30f;
                var fwd = Quaternion.Euler(0f, cy + ang, 0f) * Vector3.forward;
                target = Nav.Nearest(cp + fwd * ring); faceAt = cp;
                break;
            }
            case Want.Party:
                target = partySpot; faceAt = Plan.Ceo;
                break;
            default:
                target = PickIdleSpot(mgr, roam);
                faceAt = target.z < 3f ? target + Vector3.back * 5f : Plan.LoungeCenter;
                roamAt = Time.time + Random.Range(16f, 34f);
                break;
        }
        GoTo(target);
    }

    Vector3 PickIdleSpot(AgentManager mgr, bool roam)
    {
        var all = new System.Collections.Generic.List<Vector3>(Plan.LoungeSpots);
        all.AddRange(Plan.ViewSpots);
        if (!roam) return Plan.LoungeSpots[slot % Plan.LoungeSpots.Length];
        for (int tries = 0; tries < 8; tries++)
        {
            var c = all[Random.Range(0, all.Count)];
            if ((c - target).sqrMagnitude > 0.5f && (mgr == null || !mgr.IsTaken(c, this))) return c;
        }
        return target;
    }

    void GoTo(Vector3 to)
    {
        var from = transform.position;
        Vector2? avoid = null;
        var cp = CeoPos();
        if (Plan.InWalk(cp)) avoid = new Vector2(cp.x, cp.z);
        path.Clear();
        path.AddRange(Nav.Path(from, to, avoid, 0.9f));
        pathI = 0; blockedT = 0f;
    }

    // Snap straight to the wanted place (initial team, edit-mode renders, tests).
    public void SnapToTarget()
    {
        Init();
        Brain();
        path.Clear();
        transform.position = target;
        var d = faceAt - target; d.y = 0f;
        if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(d);
        Walking = false;
    }

    void Move(float dt)
    {
        Walking = pathI < path.Count;
        var pos = transform.position;
        if (!Walking)
        {
            var d = faceAt - pos; d.y = 0f;
            if (d.sqrMagnitude > 0.04f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 220f * dt);
            // never stand inside the CEO: if they walked up to us, slide aside
            var cp = CeoPos();
            var away = pos - cp; away.y = 0f;
            if (away.sqrMagnitude < 0.55f * 0.55f && away.sqrMagnitude > 1e-4f)
            {
                var np = pos + away.normalized * 0.8f * dt;
                if (Nav.Free(np)) transform.position = np;
            }
            return;
        }
        var wp = path[pathI];
        var dir = wp - pos; dir.y = 0f;
        float dist = dir.magnitude;
        if (dist < 0.14f) { pathI++; return; }
        dir /= dist;
        float want_ = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        float diff = Mathf.DeltaAngle(transform.eulerAngles.y, want_);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.Euler(0f, want_, 0f), 300f * dt);
        float speed = (partying ? 2.2f : WALK) * Mathf.Clamp01(1.2f - Mathf.Abs(diff) / 70f);
        // yield to a neighbour right in front (lower id goes first); give up waiting after a moment
        var mgr = AgentManager.Instance;
        if (mgr != null && Time.time > ignoreSepUntil)
        {
            foreach (var o in mgr.agents)
            {
                if (o == null) continue;
                var os = o.Skin;
                if (os == null || os == this) continue;
                var od = os.transform.position - pos; od.y = 0f;
                if (od.sqrMagnitude < 0.75f * 0.75f && Vector3.Dot(od, dir) > 0.15f && (!os.Walking || slot > os.slot))
                { speed = 0f; blockedT += dt; break; }
            }
            if (blockedT > 1.6f) { ignoreSepUntil = Time.time + 1.5f; blockedT = 0f; }
        }
        var next = pos + dir * speed * dt;
        // the CEO is an obstacle too
        var c2 = CeoPos(); var toC = next - c2; toC.y = 0f;
        if (toC.sqrMagnitude < 0.7f * 0.7f && Plan.InWalk(c2)) { if (Time.time > repathAt) { repathAt = Time.time + 0.6f; GoTo(target); } next = pos; }
        if (Nav.Free(next) || !Nav.Free(pos)) { transform.position = next; stuckT = 0f; return; }
        stuckT += dt; // a wall ahead: plan a new way instead of standing there
        if (stuckT > 1f && Time.time > repathAt) { stuckT = 0f; repathAt = Time.time + 0.6f; GoTo(target); }
    }

    void LateUpdate()
    {
        if (tag == null) return;
        var cam = Camera.main;
        if (cam == null) return;
        var to = cam.transform.position - tag.position; to.y = 0f;
        float d = to.magnitude;
        if (d > 0.01f) tag.rotation = Quaternion.LookRotation(to);
        tag.localScale = Vector3.one * Mathf.Clamp(d / 3.5f, 1f, 2.6f);
        info.gameObject.SetActive(!partying && d < 11f);
        if (bubble.activeSelf && Time.time > bubbleUntil) bubble.SetActive(false);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        Brain();
        Move(dt);
        bool travelling = Walking;
        if (label != null) { label.gameObject.SetActive(!partying); plate.SetActive(!partying); if (partying) bubble.SetActive(false); } // labels off: let the dance floor breathe
        if (driveAnimator && animator != null) animator.SetInteger(StateHash, partying ? PARTY_STATE : travelling ? 0 : (int)sm.CurrentState);
        if (modelTf != null && travelling && !partying)
        {   // calm walk: a gentle bob and a slight forward lean (the model has no walk clip)
            float wt = Time.time * 7f + phase;
            modelTf.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(wt)) * 0.07f, 0f);
            modelTf.localRotation = Quaternion.Euler(6f, Mathf.Sin(wt) * 4f, Mathf.Sin(wt) * 5f);
            return;
        }
        if (modelTf != null && driveAnimator) { modelTf.localPosition = Vector3.zero; modelTf.localRotation = Quaternion.identity; }
        if (modelTf == null || driveAnimator) return;
        // Placeholder animation: breathe / busy bounce / happy hop / slump / dance.
        float t = Time.time + phase;
        if (partying)
        {
            float beat = Time.time * 2f; // 120 BPM, in sync with LoftParty
            float hop = travelling ? Mathf.Abs(Mathf.Sin(t * 9f)) * 0.18f : (1f - Mathf.Cos(beat * Mathf.PI * 2f)) * 0.035f;
            modelTf.localPosition = new Vector3(0f, hop, 0f);
            modelTf.localRotation = Quaternion.Euler(0f, Mathf.Sin(beat * Mathf.PI) * 25f, Mathf.Sin(beat * Mathf.PI) * 8f);
            return;
        }
        switch (sm.CurrentState)
        {
            case AgentState.Working:
                modelTf.localPosition = new Vector3(0f, 0.06f * Mathf.Abs(Mathf.Sin(t * 10f)), 0f);
                modelTf.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 3f) * 15f, 0f);
                break;
            case AgentState.Done:
                modelTf.localPosition = new Vector3(0f, 0.25f * Mathf.Abs(Mathf.Sin(t * 4f)), 0f);
                modelTf.localRotation = Quaternion.identity;
                break;
            case AgentState.Failed:
                modelTf.localPosition = Vector3.zero;
                modelTf.localRotation = Quaternion.Euler(0f, 0f, 20f);
                break;
            default:
                modelTf.localPosition = new Vector3(0f, 0.02f * Mathf.Sin(t * 2f), 0f);
                modelTf.localRotation = Quaternion.identity;
                break;
        }
    }

    // Primitive stand-in animal (faces +Z, toward the CEO). Body + head +
    // ears/beak by species so different skins read differently at a glance.
    public static GameObject Placeholder(string skin)
    {
        Color main, accent;
        string ears;
        switch (skin)
        {
            case "fox": main = new Color(0.95f, 0.5f, 0.15f); accent = Color.white; ears = "pointy"; break;
            case "cat": main = new Color(0.6f, 0.6f, 0.65f); accent = new Color(1f, 0.75f, 0.8f); ears = "pointy"; break;
            case "owl": main = new Color(0.55f, 0.4f, 0.25f); accent = new Color(1f, 0.8f, 0.2f); ears = "tufts"; break;
            case "bear": main = new Color(0.45f, 0.3f, 0.18f); accent = new Color(0.75f, 0.6f, 0.4f); ears = "round"; break;
            case "panda": main = Color.white; accent = new Color(0.1f, 0.1f, 0.1f); ears = "round"; break;
            case "rabbit": main = new Color(0.95f, 0.95f, 0.95f); accent = new Color(1f, 0.7f, 0.75f); ears = "long"; break;
            case "penguin": main = new Color(0.12f, 0.12f, 0.15f); accent = new Color(1f, 0.6f, 0.1f); ears = "beak"; break;
            case "frog": main = new Color(0.35f, 0.8f, 0.3f); accent = Color.white; ears = "eyes"; break;
            default:
                main = Color.HSVToRGB(Mathf.Abs(skin.GetHashCode() % 360) / 360f, 0.6f, 0.9f);
                accent = Color.white; ears = "round"; break;
        }
        var root = new GameObject("Model").transform;
        LoftUtil.Prim(PrimitiveType.Sphere, "Body", root, new Vector3(0f, 0.38f, 0f), new Vector3(0.6f, 0.62f, 0.55f), main);
        LoftUtil.Prim(PrimitiveType.Sphere, "Belly", root, new Vector3(0f, 0.36f, 0.17f), new Vector3(0.38f, 0.45f, 0.25f), accent);
        var head = LoftUtil.Prim(PrimitiveType.Sphere, "Head", root, new Vector3(0f, 0.86f, 0.05f), Vector3.one * 0.44f, main).transform;
        var black = new Color(0.05f, 0.05f, 0.05f);
        LoftUtil.Prim(PrimitiveType.Sphere, "EyeL", head, new Vector3(-0.2f, 0.08f, 0.42f), Vector3.one * 0.14f, black);
        LoftUtil.Prim(PrimitiveType.Sphere, "EyeR", head, new Vector3(0.2f, 0.08f, 0.42f), Vector3.one * 0.14f, black);
        switch (ears)
        {
            case "pointy":
            case "tufts":
                var s = ears == "tufts" ? new Vector3(0.12f, 0.22f, 0.08f) : new Vector3(0.16f, 0.3f, 0.1f);
                LoftUtil.Prim(PrimitiveType.Cube, "EarL", head, new Vector3(-0.28f, 0.5f, 0f), s, main).transform.localRotation = Quaternion.Euler(0f, 0f, 20f);
                LoftUtil.Prim(PrimitiveType.Cube, "EarR", head, new Vector3(0.28f, 0.5f, 0f), s, main).transform.localRotation = Quaternion.Euler(0f, 0f, -20f);
                break;
            case "long":
                LoftUtil.Prim(PrimitiveType.Capsule, "EarL", head, new Vector3(-0.17f, 0.8f, 0f), new Vector3(0.18f, 0.45f, 0.1f), main);
                LoftUtil.Prim(PrimitiveType.Capsule, "EarR", head, new Vector3(0.17f, 0.8f, 0f), new Vector3(0.18f, 0.45f, 0.1f), main);
                break;
            case "round":
                LoftUtil.Prim(PrimitiveType.Sphere, "EarL", head, new Vector3(-0.36f, 0.4f, 0f), Vector3.one * 0.26f, accent);
                LoftUtil.Prim(PrimitiveType.Sphere, "EarR", head, new Vector3(0.36f, 0.4f, 0f), Vector3.one * 0.26f, accent);
                break;
            case "beak":
                LoftUtil.Prim(PrimitiveType.Cube, "Beak", head, new Vector3(0f, -0.08f, 0.5f), new Vector3(0.2f, 0.1f, 0.25f), accent);
                break;
            case "eyes":
                LoftUtil.Prim(PrimitiveType.Sphere, "BulgeL", head, new Vector3(-0.22f, 0.38f, 0.2f), Vector3.one * 0.3f, main);
                LoftUtil.Prim(PrimitiveType.Sphere, "BulgeR", head, new Vector3(0.22f, 0.38f, 0.2f), Vector3.one * 0.3f, main);
                break;
        }
        return root.gameObject;
    }
}
