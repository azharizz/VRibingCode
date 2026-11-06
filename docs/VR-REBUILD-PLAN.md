# VR rebuild plan (shared contract for all teammates)

Goal: stop being a 2D dashboard in a grey box. The CEO sits in a beautiful open
place, talks to animal agents, and rarely touches a menu.

## Hard rules
- Do NOT git commit or push. Do NOT read or print `.env` values (key names only).
- The user's Unity Editor is open on `quest_loft/`. Never close it, never run batchmode
  on that path. To compile/test, rsync `quest_loft/` (including Library) to your own
  folder under the session scratchpad and run Unity batchmode there:
  `/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -quit -projectPath <copy> -logFile <copy>/build.log`
  Source of truth stays `quest_loft/`: edit there, sync, compile the copy.
- Only edit files you own (below). Need a change elsewhere? Report it, do not make it.
- Never claim something works unless you ran it. Report what you could not verify.
- Match existing code style: small files, short comments, no over-abstraction.
- Original art only, generated in code. No copied game assets, no downloads of models.

## Art direction
Stylized low-poly, matching the code-generated vertex-colored animals. Warm, soft, calm.
Everything is generated (meshes + shaders): no licensing, tiny build, Quest friendly.
Quest budget: built-in pipeline, one directional light, baked-style flat shading,
sky/water shaders must be cheap (max ~2 texture samples, no per-pixel fbm loops),
under ~150 draw calls, under ~150k triangles.

## World (owner: loft-world)
One shared floor plan, the same in every scene. Seat, agents and screens never move,
so scene change is a fade. Three layers per scene:
1. Shell: the building, open on the view side, no big wall in front of the CEO.
2. Sky/backdrop: far 360 background, generated shader (real horizon, no solid color).
3. Life: near 3D things that move.

| id | shell | backdrop | life |
|---|---|---|---|
| beach | wooden cabana, thatched roof, posts | ocean, sun low, clouds | gerstner-style waves, palms swaying, birds |
| scenery | mountain lodge deck, railing | valley and peaks, layered ridges | drifting clouds/mist, trees |
| city | glass penthouse, slim frames | skyline, dusk | blinking windows, traffic streaks, planes |
| galaxy | observation deck, curved ribs | stars, nebula, planet | slow star drift, comets |

Keep `party` working. Layout rules:
- The CEO faces the view. Sun comes from the view side, slightly behind the CEO's shoulder,
  so the things the CEO looks at are front-lit. Never backlight agents or screens.
- Real ambient (trilight or skybox) so nothing goes black.
- Screens: 2-3 large curved or angled screens at 1.5-2.2 m, leaving the horizon visible.
- Readable: body text at least ~1.3 degrees tall, titles larger. `LoftUtil.Label(...)`
  (TextMeshPro SDF) for all reading text. Parents of text must be uniformly scaled.
- Enable 4x MSAA on the Android quality level (index 2, Medium).
- Remove floating debug labels like "PAD 1". Seat is the default; teleport spots stay
  subtle floor markers at sensible places near the agents and screens.
- Public API others depend on: `LoftWorld.ApplyTheme(id)`, `LoftWorld.NextTheme()`,
  `LoftWorld.ThemeIds()`, `LoftWorld.Boards`, `LoftWorld.rig`, `LoftWorld.cam`.
  Add `LoftWorld.FadeToTheme(id)` for the fade transition.

## Voice API (owner: loft-backend, consumer: loft-interaction)
- `POST /voice?agentId=&projectId=&scene=` body: raw 16 kHz mono 16-bit WAV (`audio/wav`), max 10 MB.
- `POST /voice/text {text, agentId?, projectId?, scene?}` same result, no audio. Used by tests and the desktop harness.
- Response `{ transcript, summary, needsConfirm, risky, intent: { action, ... } }`.
  `summary` is one short human sentence for the confirm card.
- Actions: `goal{goal}`, `task{agentId,title}`, `spawn{name?,roleId?,description?,skin?,model?}`,
  `remove{agentId}`, `scene{sceneId}`, `approve`, `reject{feedback?}`, `stop{agentId?}`,
  `party`, `project{projectId}`, `selectNext`, `none{reply}`.
- `risky` true for approve, remove, reject, stop-all: must be pressed, never voice-only.
- `needsConfirm` false for scene, party, selectNext, none: act immediately.
- Works in mixed English and Indonesian. Fixes code words ("slash health" -> `/health`).
- A pointed-at agent (`agentId`) means "this one": a bare instruction becomes a `task` for it.
- STT is pluggable, chosen by env, so moving to GCP/AWS is config only:
  `STT_PROVIDER=local` (whisper.cpp on this Mac, free and private) or
  `STT_PROVIDER=openai` with `STT_BASE_URL`, `STT_API_KEY`, `STT_MODEL` (any OpenAI-compatible
  `/audio/transcriptions`). Intent uses the same LLM as the agents.

## Interaction (owner: loft-interaction)
| Control | Action |
|---|---|
| Right trigger | point and select, with hover highlight, cursor dot, haptic tick |
| Hold A | talk (release to process) |
| Right stick left/right | 30 degree snap turn |
| B | cancel / back |
| Look at left wrist (or X) | small wrist menu for rare actions |

Flow: point at an animal, hold A, speak, release, "thinking" state, confirm card near the
agent/at comfortable distance shows transcript + summary. A or trigger on Send = send, B = cancel.
Risky actions need a press on a clearly separated button. Failures show on the card and
offer "say it again". The big 11-button panel is removed. Wrist menu: scene picker
(real list, not cycling), project, stop all, exit (exit needs a hold or second press).
Keyboard stays only as a backup for exact text like a model id.

## Virtual Quest (no headset available)
Meta XR Simulator is installed and is the XR test target. Also build a desktop rig in
Play mode (no XR device): mouse aims the right controller, click = trigger, hold V = A,
Esc/C = B, Q/E = snap turn, M = wrist, uses the Mac microphone through the same voice path.
It must exercise the SAME code as VR, not a separate UI. The old OnGUI desktop overlay
is reduced to a small debug strip, off by default.
Automated checks in `Assets/Editor/LoftVerify.cs` must be updated to the new world.
