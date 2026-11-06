// VIRTUAL LOFT — agent roster in the room. The backend owns the team
// (default 3, max 20); LoftDesk feeds server events into Upsert/Remove.
// Each agent = AgentStateMachine (state) + AgentSkin (animal visual).

using System;
using System.Collections.Generic;
using UnityEngine;

public class AgentManager : MonoBehaviour
{
    public static AgentManager Instance { get; private set; }
    public const int MAX_AGENTS = 20;
    float awakeAt;
    

    public List<AgentStateMachine> agents = new List<AgentStateMachine>();
    public event Action<string> OnAgentDone; // fires when an agent's task reaches review

    void Awake() { Instance = this; awakeAt = Time.time; Clear(); }

    // Drops every agent (also the capsules baked into pre-runtime-spawn scenes).
    public void Clear()
    {
        foreach (var a in agents) if (a != null) LoftUtil.Kill(a.gameObject);
        agents.Clear();
    }

    public AgentStateMachine Find(string id) => agents.Find(a => a != null && a.agentId == id);

    public static AgentState ParseState(string s) => s switch
    {
        "working" => AgentState.Working,
        "done" => AgentState.Done,
        "failed" => AgentState.Failed,
        _ => AgentState.Idle,
    };

    // Spawn or update one agent. Returns null when the room is full.
    public AgentStateMachine Upsert(string id, string displayName, string role, string skin, AgentState state)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (Instance == null) Instance = this;
        var a = Find(id);
        if (a == null)
        {
            if (agents.Count >= MAX_AGENTS) { Debug.LogWarning($"[Agents] room full ({MAX_AGENTS}), ignoring {id}"); return null; }
            var go = new GameObject($"Agent_{id}");
            a = go.AddComponent<AgentStateMachine>();
            a.agentId = id;
            a.OnStateChanged += HandleState;
            var sk = go.AddComponent<AgentSkin>();
            sk.slot = FreeSlot();
            agents.Add(a);
            Layout(true);
            // the team that is already here when we connect stands at its place; later hires walk in
            go.transform.position = Time.time - awakeAt < 4f ? Plan.LoungeSpots[sk.slot % Plan.LoungeSpots.Length] : Plan.Spawn + new Vector3((agents.Count % 5 - 2) * 0.5f, 0f, 0f);
            if (Partying) SetParty(true); // late arrivals join the party
        }
        a.GetComponent<AgentSkin>().Apply(skin, displayName, role);
        a.SetState(state);
        return a;
    }

    public bool Remove(string id)
    {
        var a = Find(id);
        if (a == null) return false;
        a.OnStateChanged -= HandleState;
        agents.Remove(a);
        LoftUtil.Kill(a.gameObject);
        Layout(false);
        if (Partying) SetParty(true);
        return true;
    }

    // Every agent owns a stable slot: desk `slot` to work at, lounge spot `slot` to idle at.
    // Where it actually is comes from its real state (AgentSkin): desk, the CEO, or the lounge.
    void Layout(bool snapNewest)
    {
        foreach (var a in agents)
        {
            var skin = a.GetComponent<AgentSkin>();
            int n = Plan.LoungeSpots.Length, ring = skin.slot / n;
            var around = Quaternion.Euler(0f, ring * 97f + skin.slot * 40f, 0f) * Vector3.forward * (0.8f * ring); // later arrivals stand nearby
            skin.home = Plan.ClampToWalk(Plan.LoungeSpots[skin.slot % n] + around);
        }
    }

    int FreeSlot()
    {
        for (int s = 0; ; s++) if (!agents.Exists(a => a != null && a.GetComponent<AgentSkin>().slot == s)) return s;
    }

    public int CeoWaiting() => agents.FindAll(a => a != null && IsWaiting(a)).Count;
    static bool IsWaiting(AgentStateMachine a) => !a.GetComponent<AgentSkin>().partying && (a.CurrentState == AgentState.Done || a.CurrentState == AgentState.Failed);
    // Order among the agents waiting on the CEO (stable by slot).
    public int CeoRank(AgentSkin me)
    {
        int r = 0;
        foreach (var a in agents)
            if (a != null && IsWaiting(a) && a.GetComponent<AgentSkin>() != me && a.GetComponent<AgentSkin>().slot < me.slot) r++;
        return r;
    }
    public bool IsTaken(Vector3 spot, AgentSkin except)
    {
        foreach (var a in agents)
        {
            if (a == null) continue;
            var s = a.GetComponent<AgentSkin>();
            if (s != except && ((s.Target - spot).sqrMagnitude < 0.6f || (s.transform.position - spot).sqrMagnitude < 0.6f)) return true;
        }
        return false;
    }

    // ---- party: everyone gathers on the dance floor ----
    public bool Partying { get; private set; }

    public void SetParty(bool on)
    {
        Partying = on;
        var spots = PartySpots(agents.Count);
        for (int i = 0; i < agents.Count; i++)
        {
            if (agents[i] == null) continue;
            // Stagger: neighbours set off ~0.12 s apart, like the preview.
            agents[i].GetComponent<AgentSkin>().SetParty(on, spots[i], i * 0.12f);
        }
    }

    // Arc behind the dance-floor center, facing the CEO; rows of 5/7 (staggered),
    // then a line in front of the floor. Rows 1-2 = animalgen/party_preview.py.
    public static List<Vector3> PartySpots(int n)
    {
        var spots = new List<Vector3>(n);
        int i = 0, row = 0;
        while (i < n)
        {
            int cap = row == 0 ? 5 : row == 1 ? 7 : 8;
            int k = Mathf.Min(cap, n - i);
            float r = 1.9f + row * 0.95f;
            float off = (row % 2 == 1 && k < cap) ? 0.5f : 0f;
            for (int j = 0; j < k; j++)
            {
                if (row >= 2) // a deeper arc would hit the wall: extra dancers line up in front of the floor
                {
                    spots.Add(LoftParty.Center + new Vector3((j - (k - 1) / 2f) * 0.95f, 0f, 0.9f + (row - 2) * 0.8f));
                    continue;
                }
                float a = Mathf.Deg2Rad * (-62f + 124f * (j + 0.5f + off) / (k + off));
                spots.Add(LoftParty.Center + new Vector3(Mathf.Sin(a) * r, 0f, -Mathf.Cos(a) * r));
            }
            i += k;
            row++;
        }
        return spots;
    }

    void HandleState(string id, AgentState s)
    {
        if (s == AgentState.Done) OnAgentDone?.Invoke(id);
    }

    void OnDestroy()
    {
        foreach (var a in agents) if (a != null) a.OnStateChanged -= HandleState;
    }
}
