// VIRTUAL LOFT — agent roster in the room. The backend owns the team
// (default 3, max 20); LoftDesk feeds server events into Upsert/Remove.
// Each agent = AgentStateMachine (state) + AgentSkin (animal visual).

using System;
using System.Collections.Generic;
using UnityEngine;

public class AgentManager : MonoBehaviour
{
    public const int MAX_AGENTS = 20;
    const int PER_ROW = 10;
    const float SPACING = 1.3f, FRONT_Z = 3.0f, ROW_GAP = 1.3f;

    public List<AgentStateMachine> agents = new List<AgentStateMachine>();
    public event Action<string> OnAgentDone; // fires when an agent's task reaches review

    void Awake() => Clear();

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
        var a = Find(id);
        if (a == null)
        {
            if (agents.Count >= MAX_AGENTS) { Debug.LogWarning($"[Agents] room full ({MAX_AGENTS}), ignoring {id}"); return null; }
            var go = new GameObject($"Agent_{id}");
            a = go.AddComponent<AgentStateMachine>();
            a.agentId = id;
            a.OnStateChanged += HandleState;
            go.AddComponent<AgentSkin>();
            agents.Add(a);
            Layout(true);
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

    // ponytail: fixed 2-row grid in front of the wall; fine up to 20, swap for
    // themed desk anchors if a scene needs custom seating.
    void Layout(bool snapNewest)
    {
        for (int i = 0; i < agents.Count; i++)
        {
            int row = i / PER_ROW, col = i % PER_ROW;
            int inRow = Mathf.Min(PER_ROW, agents.Count - row * PER_ROW);
            var home = new Vector3((col - (inRow - 1) / 2f) * SPACING, 0f, FRONT_Z + row * ROW_GAP);
            var skin = agents[i].GetComponent<AgentSkin>();
            skin.home = home;
            if (snapNewest && i == agents.Count - 1) agents[i].transform.position = home;
        }
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
