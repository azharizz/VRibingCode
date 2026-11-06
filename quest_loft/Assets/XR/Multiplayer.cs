// VIRTUAL LOFT — Multiplayer.cs
// Unity 6 + Meta XR SDK v71 + Photon Fusion 2 (room: 2-6 users, same loft)
// Roles: CEO can assign any agent/card; member can move ONLY own cards.
// Activity log records who-moved-what (cloud + wall Log board).
// Ray + avatar sync are stubs: wire to Interaction SDK ray + Fusion NetworkTransform.
//
// Pinned versions (verified Nov 2026): Photon Fusion 2.1.1 Stable (Build 2177,
// 30 Jun 2026; also 2.0.12 Mar 2026), Meta XR All-in-One v207.0 (baseline v71
// line), Unity OpenXR Plugin 1.13.0+, Unity 6000 LTS.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace VirtualLoft.XR
{
    public enum LoftRole { CEO = 0, Member = 1 }

    [Serializable]
    public struct ActivityEntry
    {
        public string timestampUtc;  // ISO-8601
        public string userName;      // display name
        public string action;        // e.g. "move", "assign", "done"
        public string what;          // e.g. "card:launch-copy" / "agent:builder"
        public string whereFrom;     // e.g. "Backlog wall"
        public string whereTo;       // e.g. "Done wall"

        public override string ToString() =>
            $"[{timestampUtc}] {userName} {action} {what} ({whereFrom} -> {whereTo})";
    }

    /// <summary>
    /// Fusion room manager stub. Attach once per loft scene.
    /// Real netcode: implement INetworkRunnerCallbacks on a NetworkRunner
    /// wrapper and call these methods from RPCs. This file defines the RULES
    /// (roles, capacity, log) so gameplay code never branches on networking.
    /// </summary>
    [DisallowMultipleComponent]
    public class Multiplayer : MonoBehaviour
    {
        public const int MIN_PLAYERS = 2;
        public const int MAX_PLAYERS = 6;
        public const string ROOM_PREFIX = "loft-";

        [Header("Local identity (set at join)")]
        public string localUserName = "CEO";
        public LoftRole localRole = LoftRole.CEO;

        [Header("Room")]
        [Tooltip("Room name = loft id, e.g. loft-focus-01. Same loft instance for all.")]
        public string roomName = "loft-focus-01";
        [Tooltip("Simulated occupants for XR Simulator 2-fake-user test.")]
        public List<string> simulatedUsers = new List<string> { "CEO", "Member-1" };

        [Header("Activity log (who-moved-what)")]
        public List<ActivityEntry> activityLog = new List<ActivityEntry>(capacity: 256);
        public UnityEvent<ActivityEntry> onActivityLogged;

        // ---- Room lifecycle (stubs: wire to Fusion NetworkRunner) ----------------
        // STUB: StartRunner(roomName) ->
        //   var runner = gameObject.AddComponent<NetworkRunner>();
        //   await runner.StartGame(new StartGameArgs {
        //     GameMode = GameMode.Shared, SessionName = roomName,
        //     PlayerCount = MAX_PLAYERS, SceneManager = ... });
        // Shared mode fits 2-6 casual loft (no dedicated host needed).
        public void JoinRoom(string loftId, string userName, LoftRole role)
        {
            roomName = ROOM_PREFIX + loftId;
            localUserName = userName;
            localRole = role;
            Debug.Log($"[Multiplayer] JoinRoom stub: {roomName} as {userName} ({role}). Wire to Fusion NetworkRunner.StartGame in production.");
        }

        public void LeaveRoom()
        {
            Debug.Log($"[Multiplayer] LeaveRoom stub: {roomName}. Wire to runner.Shutdown().");
        }

        // ---- Role rules -----------------------------------------------------------
        // CEO can assign any agent/card. Member can move ONLY cards they own.
        // Enforce at the CALLER (board/agent UI) via CanMove / CanAssign.

        public bool CanAssign(string requester) =>
            RoleOf(requester) == LoftRole.CEO;

        public bool CanMove(string requester, string cardOwner) =>
            RoleOf(requester) == LoftRole.CEO || requester == cardOwner;

        LoftRole RoleOf(string user) =>
            user == localUserName ? localRole
            : user.StartsWith("CEO") ? LoftRole.CEO : LoftRole.Member;

        /// <summary>CEO assigns agent/card to someone. Denied for members.</summary>
        public bool Assign(string requester, string what, string assignee)
        {
            if (!CanAssign(requester))
            {
                Debug.LogWarning($"[Multiplayer] Assign DENIED: {requester} is not CEO.");
                return false;
            }
            Log(requester, "assign", what, "", assignee);
            // STUB RPC: [Rpc(RpcSources.All, RpcTargets.All)] RpcAssign(...) -> agents set owner.
            return true;
        }

        /// <summary>Move a card between walls/columns. Members move own cards only.</summary>
        public bool MoveCard(string requester, string cardId, string cardOwner, string from, string to)
        {
            if (!CanMove(requester, cardOwner))
            {
                Debug.LogWarning($"[Multiplayer] Move DENIED: {requester} cannot move {cardOwner}'s card {cardId}.");
                return false;
            }
            Log(requester, "move", "card:" + cardId, from, to);
            // STUB RPC: RpcMoveCard(cardId, from, to) -> Kanban board state + Networked property.
            return true;
        }

        public void MarkDone(string requester, string cardId, string from)
        {
            Log(requester, "done", "card:" + cardId, from, "Done");
            // STUB RPC: RpcMarkDone(cardId).
        }

        // ---- Activity log ----------------------------------------------------------

        public void Log(string user, string action, string what, string from, string to)
        {
            var entry = new ActivityEntry
            {
                timestampUtc = DateTime.UtcNow.ToString("o"),
                userName = user,
                action = action,
                what = what,
                whereFrom = from,
                whereTo = to
            };
            activityLog.Add(entry);
            if (activityLog.Count > 256) activityLog.RemoveAt(0);
            Debug.Log("[Activity] " + entry);
            onActivityLogged?.Invoke(entry);
            // Production: also POST to Supabase agent_runs / board_events table.
        }

        // ---- Sync stubs ------------------------------------------------------------
        // STUB (ray sync): each player spawns a Networked ray-pointer prefab with a
        // NetworkTransform; hover highlight (agent ring / board handles) is LOCAL
        // only — never spam RPCs for hover. Send RPC only on select/grab commit.
        public void SyncRayStub(Vector3 origin, Vector3 direction)
        {
            // Wire: ray visual prefab position = origin/direction via Fusion
            // NetworkTransform; remote clients render remote rays in team color.
        }

        // STUB (avatar sync): spawn one Networked avatar prefab per player
        // (head + hands via OVR CameraRig / Meta Movement SDK data, sent through
        // NetworkTransform + compressed hand blendshape struct at 20Hz).
        public void SyncAvatarStub(Vector3 headPos, Quaternion headRot, Vector3 leftHand, Vector3 rightHand)
        {
            // Wire: write into [Networked] struct on player NetworkObject.
        }

        // 2-fake-user Simulator test: Play scene twice (or ParrelSync clone),
        // JoinRoom same loft id, simulatedUsers = both names, move a card on
        // client A -> assert activityLog entry appears on client B.
    }
}
