using System;
using UnityEngine;

// Int values are the Animator "State" parameter (see AgentSkin).
public enum AgentState { Idle = 0, Working = 1, Done = 2, Failed = 3 }

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
