# V2 Multiplayer Validation — without a headset (XR Simulator)

**Target:** TASK.md §2 V2 accept — 3 real users, same loft, no "where is checkboard?" confusion.
**No-headset path:** XR Simulator 2-fake-user test + Play-Mode self-test. Only 72fps-truth,
noisy-mic, and comfort-truth stay deferred to the borrowed-Quest weekend.

## 0. Automated gate (run first, ~10 seconds)

1. Open `quest_loft/` in Unity Hub → 6000.6.7f1 → open Focus Loft scene.
2. Add `StandupFlowTest` (`Assets/Tests/StandupFlowTest.cs`) to an empty GameObject.
   Auto-wires to `AgentManager` / `BoardManager` / `VoiceCommands` / `Multiplayer`.
3. Press **Play with XR Simulator** (Quest 3/3S profile, `VoiceCommands.testMode = true`).
4. Console must show 15× `[STANDUP-TEST] PASS` + `SUMMARY: 15 pass, 0 fail — GREEN`,
   zero errors. Steps covered: wake 3 agents → ID+EN voice routing → Builder Working →
   log `PASS 12/12` → VoiceMove to Done → button fallback → Done broadcast →
   CEO/member allow-deny rules → activity log entries → save/load roundtrip.

## 1. Two-fake-user Simulator test (manual, ~20 min)

1. Duplicate the project (or ParrelSync clone) → open the clone as **Member-1**,
   original as **CEO**. Same machine, two Editor instances.
2. Both: Play (Simulator) → `Multiplayer.JoinRoom("focus-01", <name>, <role>)`
   with `roomName = loft-focus-01` on both, `simulatedUsers = {CEO, Member-1}`.
3. CEO: `Assign("CEO", "agent:builder", "Member-1")` → expect `true` + activity entry.
4. Member-1: `Assign("Member-1", ...)` → expect **denied** (`false`) + warning.
5. CEO adds kanban card "Launch copy"; Member-1 moves own card Doing → Done → `true`.
6. Member-1 tries moving CEO's card → **denied** (`false`).
7. Verify on BOTH instances: `activityLog` contains matching who-moved-what entries
   (ISO-8601, ≤256 cap) and the wall Log board shows the same lines.
8. **No-confusion check:** on each instance, point ray at each board and read its name
   tag aloud — Kanban-left / Notes-center / Calendar+Log-right must match on both.

## 2. Team voice procedure (Simulator, PC mic)

1. `VoiceCommands.testMode = true`, hold **T** as push-to-talk stand-in.
2. Say each of the 10 commands (EN + ID) → matching `lastCommand` in console.
3. For every command, also click its Quick-Menu button → same event must fire
   (`InvokeCommand` path). Mic-fail fallback = buttons, never a dead end.

## 3. V2 exit criteria

- [ ] StandupFlowTest GREEN on 2 consecutive runs, zero console errors.
- [ ] 2-fake-user run: allow/deny matrix all correct, logs match on both instances.
- [ ] No-confusion check passes (board positions identical on both).
- [ ] Voice 10/10 EN + 10/10 ID route in testMode; 10/10 button fallbacks fire.
- [ ] `backend npm test` still green (cloud JSON stays source of truth).

Deferred to borrowed Quest (see QA-REPORT): 72fps sustained, 30-min wear, noisy mic,
2m legibility truth, battery drain, real `adb install`, 3-user device session.
