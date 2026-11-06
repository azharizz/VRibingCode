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

# Round 2 contract (phases 1-5)

## Extra hard rules
- Work on branch `vr-rebuild`. Do not commit or push: the lead does that.
- Edit-mode checks are not enough. These bugs passed them: `while (Find) Destroy` hangs in Play,
  DontSaveInEditor objects keep dead materials in Play, reusing objects queued for Destroy,
  JSON null is a JValue. Add a real Play-mode smoke run in your scratch copy where you can.
- UI root +Z faces the head, so local +X is the viewer's LEFT. Backing panels go at negative z.
- TMP font lacks some symbols: use plain words or draw shapes. Never use emoji glyphs.
- Stay inside your owned files. Disk has ~13 GB free: clone Library with `cp -cR`, stop under 3 GB.

## Ownership
- loft-backend: `backend/**`.
- loft-world: `Assets/World/**`, `App/LoftWorld.cs` (except board code), `Resources/Themes/**`,
  `Resources/Shaders/**`, `Agents/**`, `App/LoftParty.cs`, `App/LoftUtil.cs`.
- loft-interaction: `XR/**` except `XR/LoftUi.cs`, `Boards/LoftClient.cs`, `App/LoftDesk.cs` except board code,
  `Editor/LoftBootstrap.cs`, `Editor/LoftVerify.cs`, `Editor/LoftQuestBuild.cs`.
- loft-boards: new `Assets/Panels/**`, `XR/LoftUi.cs`, `App/LoftPreviewScreen.cs`, the board code in `LoftWorld.cs`
  (Boards, BuildBoard) and in `LoftDesk.cs` (SetBoard, RefreshViews, board text). Add `LoftClient` calls in a
  new partial file `Assets/Panels/LoftClient.Panels.cs` (loft-interaction makes the class `partial` first).

## World (phase 2, phase 4)
- Floor about 3x the area in every scene, higher roof, areas to walk to (work area, lounge, view edge).
  Keep it open: no big walls in front of the view. Publish `LoftWorld.WalkBounds` and `LoftWorld.Spots`
  (named Vector3s: desks, lounge, ceo) for others.
- Art: palms must read as palms (long arching fronds, no willow droop), real thatch layers, a wider sea view.
- Fixed wall boards go away. Default start: an open space, boards are spawned.
- Living agents, driven by REAL state: Working = walk to a desk and work, Done/Review = walk to the CEO and wait,
  Idle = wander or chat in the lounge, never through furniture. `AgentSkin.Say(text, seconds)` shows a speech bubble.

## Movement (phase 2, owner loft-interaction)
Teleport anywhere on the floor: push right stick forward, arc plus landing marker, release to go, inside
`WalkBounds`. Snap turn stays. Smooth walk on left stick is OFF by default and toggled in the wrist menu.
Desktop: WASD stays.

## Pointer hook (owner loft-interaction, consumed by loft-boards)
`interface IPointerTarget { void OnHover(bool on); void OnPress(Ray r, RaycastHit h); void OnDrag(Ray r); void OnRelease(); }`
in `XR/LoftInput.cs` or a new XR file. `LoftInteraction` routes trigger events to any `IPointerTarget` it hits, before
its normal click logic. Land this first.

## Boards (phases 3, 5, owner loft-boards)
Design system first, in `Assets/Panels/`: tokens (colors, type scale, spacing, radius), parts (card, pill, avatar,
progress bar, tab, list row, button), rounded panels by mesh or 9-slice, not cubes. A shared frame: title bar, grab handle,
resize corner, close button, consistent shadow/edge. Boards are assembled from parts. Quality bar: looks like a real product UI,
with hierarchy and whitespace, never a block of text.
Types: `kanban`, `review`, `log`, `preview`, `notes`, `files`, `diff`.
Spawn by wrist menu or voice. Move by grabbing, resize by corner with proportional text, close with X. Layout saved per scene
through the backend. No fixed limit, but boards beyond ~12 m or out of view stop updating. Live previews capped at 3.
`Assets/Panels/Editor/BoardGallery.cs` renders every board with sample data to PNGs at VR scale.
Expose `LoftPanels.Open(type)`, `Close(type)`, `CloseAll()` for voice and the wrist menu.

## Backend additions (owner loft-backend)
- Fix "spawn new agent as data analyst": voice spawn with a new role must work end to end.
- `GET/PUT /boards?scene=` board layout `[{id,type,pos,rot,size,args}]`.
- `GET/POST/PATCH/DELETE /notes`.
- `GET /projects/:id/tree?path=`, `GET /projects/:id/file?path=` (locked to the project folder).
- `GET /tasks/:id/diff` -> `{files:[{path,added,removed,hunks:[{header,lines:[{type,text}]}]}]}`.
- `POST /agents/:id/chat {text}` -> `{reply}`, SSE `{type:"say",agentId,text}`. Short, in role, with memory.
- Team, tasks, chat memory and notes persist across restarts in the data dir.
- Suggestions: after a finished task an agent may raise at most one, SSE `{type:"suggestion",id,agentId,title,reason}`,
  `POST /suggestions/:id/accept` queues it, `/dismiss` drops it. Never acts alone. One LLM call, `LOFT_SUGGESTIONS=0` disables.
- Clickable preview: `POST /preview/sessions {taskId|url}` -> `{id,width,height}`, `GET /preview/sessions/:id/frame` (JPEG),
  `POST /preview/sessions/:id/input {type:click|move|scroll|key|text,...}`, `DELETE`. Playwright. Cap 3, idle timeout. URL locked to
  the project's own preview or an http(s) url the CEO gave; block cloud metadata and file URLs.
- New voice actions: `chat{agentId,text}`, `board{op:open|close,type}`. Plain greetings to an agent become `chat`.
- STT: compare whisper.cpp small vs large-v3-turbo vs faster-whisper with `tools/stt-compare`. Provide a faster-whisper
  server (OpenAI-compatible, Docker) for cloud. Optional `STT_SAVE_SAMPLES=1` (off by default) keeps recordings locally so the
  user can run the comparison on their own voice.
