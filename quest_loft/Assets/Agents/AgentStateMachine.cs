using System;
using UnityEngine;

public enum AgentState { Idle, Working, Done }

public class AgentStateMachine : MonoBehaviour
{
    public string agentId = "scout";
    public AgentState CurrentState { get; private set; } = AgentState.Idle;
    public event Action<string, AgentState> OnStateChanged;

    public void SetState(AgentState next)
    {
        if (next == CurrentState) return;
        CurrentState = next;
        OnStateChanged?.Invoke(agentId, next);
    }
}
