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
        MakePlate();
        label = LoftUtil.Label(transform, "Label", new Vector3(0f, 1.79f, 0f), 0.13f, Color.white, 1.0f);
        info = LoftUtil.Label(transform, "Info", new Vector3(0f, 1.535f, 0f), 0.1f, Color.white, 1.05f, TMPro.TextAlignmentOptions.Top, 0.34f);
        foreach (var t in new[] { label, info })
        {
            t.overflowMode = TMPro.TextOverflowModes.Ellipsis;
            t.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        }
        label.fontStyle = TMPro.FontStyles.Bold;
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
        var go = transform.Find("Plate")?.gameObject;
        if (go == null)
        {
            go = new GameObject("Plate");
            go.transform.SetParent(transform, false);
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
        if (s == AgentState.Working) LoftAudio.PlayAt("voice_" + skinId, at, 0.7f); // "on it!" in its own voice
        else if (s == AgentState.Done) LoftAudio.PlayAt("sfx_done", at);
        else if (s == AgentState.Failed) LoftAudio.PlayAt("sfx_failed", at);
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

    void Update()
    {
        var target = partying && Time.time >= partyDelay ? partySpot : home;
        bool travelling = (transform.position - target).sqrMagnitude > 0.0004f;
        transform.position = Vector3.MoveTowards(transform.position, target, (partying ? 2.6f : 2f) * Time.deltaTime);
        if (label != null) { label.gameObject.SetActive(!partying); info.gameObject.SetActive(!partying); plate.SetActive(!partying); } // labels off: let the dance floor breathe
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
