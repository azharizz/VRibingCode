using System;
using System.Collections.Generic;
using UnityEngine;

public class AgentManager : MonoBehaviour
{
    public List<AgentStateMachine> agents = new List<AgentStateMachine>();
    public event Action<string> OnAgentDone; // Done broadcast hook -> boards (BoardManager subscribes)

    void Awake() => Rewire();
    void OnEnable() => Rewire();

    // Rewire: (re)subscribe to every agent. Needed because agents can be
    // added after Awake (scene bootstrap, runtime spawn), and Unity does not
    // re-run Awake for that. Idempotent: unsubscribes first.
    public void Rewire()
    {
        foreach (var a in agents)
        {
            if (a == null) continue;
            a.OnStateChanged -= HandleState;
            a.OnStateChanged += HandleState;
        }
    }

    void OnDestroy()
    {
        foreach (var a in agents)
            if (a != null) a.OnStateChanged -= HandleState;
    }

    void HandleState(string id, AgentState s)
    {
        if (s == AgentState.Done) OnAgentDone?.Invoke(id);
    }

    public void WakeAll()
    {
        Rewire();
        foreach (var a in agents) if (a != null) a.SetState(AgentState.Idle);
    }

    public void AssignTask(string agentId, string task)
    {
        var a = agents.Find(x => x != null && x.agentId == agentId);
        if (a == null) { Debug.LogWarning($"[Agents] unknown agent: {agentId} task: {task}"); return; }
        Debug.Log($"[Agents] {agentId} <- {task}");
        a.SetState(AgentState.Working);
    }

    public void StopAll()
    {
        foreach (var a in agents) if (a != null) a.SetState(AgentState.Idle);
    }

    // ponytail: linear Find over 3 agents, no dict needed
}
