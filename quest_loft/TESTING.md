# VIRTUAL LOFT — Unity app (vibe as CEO)

You sit in the loft. Your AI team are animals at their seats. Give the team a
goal, watch each animal work live ("Edit src/auth.ts" over its head), then
approve or reject the results on the REVIEW wall.

## Run it (desktop, no headset)

1. Backend first: `cd backend && npm install && npm run serve` (see `backend/README.md`).
2. Open `quest_loft/` in Unity 6000.6.7f1 → menu **Loft → Build Loft Scene** (once,
   and after pulling changes to the room code) → **Play**.
3. Top-left desk panel: type a goal → **Send as TEAM GOAL**. Click an animal to
   select it, then **Give to …** for a direct task. `T` next scene, `Y`/`N`
   approve/reject, `X` stop all, WASD + right-drag to move.
4. **＋ New agent…** opens the spawn form (max 20 agents), where everything is free:
   - **Model**: type any model id (`qwen/qwen3-coder`, `anthropic/claude-haiku-5-5`, …); empty = server default.
   - **Animal**: any of the 8, or random (unused animals first).
   - **Role**: pick a saved one, or describe what you need → **Write role**. AI writes
     the title, instructions and tools; tools marked ⚠ change your project, click
     to remove them before **CREATE AGENT**. New roles are saved for next time.
   - `1/2/3` quick-spawn Researcher/Coder/Reviewer with the form's model.

## In the headset (Quest 3/3S)

- Right controller laser + trigger: point at an animal to select it, at the console
  below you for **+ AGENT / – AGENT / SCENE ▶ / GOAL ⌨ / TASK ⌨ / STOP / APPROVE /
  REJECT / EXIT**, at a floor pad to teleport.
- **+ AGENT** opens the same form: **ROLE ▶ / ANIMAL ▶ / MODEL ⌨ / NAME ⌨ /
  DESCRIBE ⌨** (AI writes a role) → **CREATE**. The console text shows the form.
- GOAL/TASK/REJECT open the system keyboard. Hold **A** for push-to-talk once a
  speech-to-text source feeds `VoiceCommands.HandleTranscription` (none ships yet):
  "run <goal>", "assign <task>", "stop", "done", "next", "focus mode".
- Point the headset at your Mac: see "Quest on Wi-Fi" in `backend/README.md`.

## Party mode ★

The "we shipped it!" moment, whenever you want: **★ PARTY** on the VR console,
the desktop panel, the `P` key, or say "party" / "ayo pesta". Lights drop to
club purple, a disco ball falls in and spins, spots sweep, the dance floor
pulses on a code-made 120 BPM beat, confetti rains, the wall says LAUNCH ·
PARTY!, and every agent hops over to the floor and dances. Press it again to
go back to work: everyone returns to their seat and real state, and the scene's
theme is restored. It goes through the backend, so every connected headset parties
together. Preview it without Unity: `animalgen/out/party/party.mp4`.

## Change the animals

All 8 animals are generated in code by `animalgen/` (Blender, see its README)
and imported automatically by `Assets/Editor/LoftAnimalImport.cs`. The backend
sends a `skin` id per agent: `fox`, `owl`, `cat`, `bear`, `rabbit`, `penguin`,
`frog`, `panda`.

- Drop a prefab at `Assets/Resources/Skins/<skin>.prefab` (model faces +Z, feet at 0).
- Give it an Animator with an **int parameter `State`**: 0 Idle, 1 Working,
  2 Done, 3 Failed. `AgentSkin` drives it; nothing else needs code.
- No prefab yet → a primitive placeholder animal with code animation.

## Change the scene (Party, Beach, …)

Add `Assets/Resources/Themes/<id>.json` (copy `beach.json`): sky/ambient/light/fog
colors, floor/wall/board tints, `audio` (clip at `Resources/Audio/<name>`), and
`props` (primitive `shape` or `prefab` from `Resources/Props/`, with `pos`,
`scale`, `euler`, `color`, `glow`, `pulse`, `spin`). Shipped: `loft`, `night`,
`meadow`, `beach`, `party`. The layout (wall, boards, seats) never changes per scene.
No audio clips ship yet; each theme stays silent until you add its clip.

## Automated check (headless)

```bash
Unity -batchmode -nographics -projectPath quest_loft \
  -executeMethod LoftVerify.RunAll -quit -logFile Tools/verify.log
```
Rebuilds the scene from code, then feeds real server-event JSON into the desk:
themes, spawn/cap 20/remove, live progress, review board, VR console routing,
teleport, voice payloads, offline safety. Backend logic: `cd backend && npm test`.
