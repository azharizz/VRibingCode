# TESTING — VIRTUAL LOFT (no headset needed)

## A. Test the VR app in XR Simulator (5 min)

1. **Unity Hub → Add → select `quest_loft/`** → open with **6000.6.7f1**.
2. **First open (offline-safe):** `Packages/manifest.json` has Unity-registry
   packages only → project opens with zero errors (verified headless:
   `Tools/unity-batch-loftclient2.log`, 0× `error CS`).
3. **Full XR packages (needs normal network):**
   ```bash
   cd quest_loft
   cp Tools/manifest.with-meta-photon.json Packages/manifest.json
   ```
   Reopen Unity → Package Manager resolves Meta XR All-in-One 71.0.0 +
   Interaction + Voice + OpenXR 1.13 + Fusion 2.2.6 (matches XR Simulator 71).
4. **Play:** select XR Simulator (Quest 3/3S profile) → press **Play**.
   Mouse = controller ray, keyboard = head look, PC mic = Quest mic.
5. **Automated gate:** add `Assets/Tests/StandupFlowTest.cs` to an empty
   GameObject → Play → console must show 15× `[STANDUP-TEST] PASS` +
   `SUMMARY: 15 pass, 0 fail — GREEN`, zero errors.
6. **Manual standup:** `AgentManager.WakeAll()` → `AssignTask("builder",
   "run landing test")` → Builder Working → Log `PASS 12/12` →
   `BoardManager.MoveCard("kanban", id, "Done")` (or voice *"move that to
   done"* / Quick-Menu button) → agent Done → check pop + sound.

## B. Test WITH live backend integration (10 min)

Terminal 1 — backend (needs `backend/.env` filled, see `backend/README.md`):
```bash
cd backend
npm install
node --env-file=.env src/server.js --port 4101
# expect: loft server: http://127.0.0.1:4101
```

Terminal 2 — Unity Play (Simulator): attach `LoftClient` next to
`BoardManager`, press Play, then in the component call (via UI button or
test script):
- `CheckHealth` → console `[LoftClient] backend reachable`
- `SaveToServer` → boards pushed to server (version bumps)
- `LoadFromServer` → pulls boards back (travel test: same JSON on any machine)
- `RunAgent("builder", "run landing test")` → **live Muse Spark** runs the
  agentic loop server-side, text comes back into Unity for the Log board

Proven 2026-11-02: all four endpoints curl-verified, Builder returned
`PASS 12 / FAIL 0` over HTTP, Unity compiles clean with `LoftClient`.

## C. What each side proves alone

- **Unity alone:** full standup flow, voice routing, roles, comfort —
  Simulator only, backend optional.
- **Backend alone:** `npm test` (offline travel-reload + 3 agent routes) and
  `node tools/check-connection.js` (live Spark reachability).
- **Together:** this page. The contract is the loft JSON
  `{roomId, boards, agents, version, updatedAt}` — identical shape in
  `Assets/Boards/LoftLayout.json`, `loftStore.js`, and Supabase `lofts.layout`.
