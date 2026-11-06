# VIRTUAL LOFT — backend (the agents)

Spawnable AI agents (default **3**, max **20**) on the **OpenAI Agents SDK**,
talking to any OpenAI-compatible endpoint (Nebius, OpenRouter, OpenAI, a local
server...), configured in the **repo-root `.env`**: `AGENT_API_KEY`,
`AGENT_BASE_URL`, `AGENT_MODEL`. There is no built-in default endpoint or model:
if one is missing the server refuses to start and names what to set.

Every agent is free-form, set in the spawn form:

| field | |
| --- | --- |
| `model` | any model id the endpoint serves (empty = `AGENT_MODEL`), e.g. `qwen/qwen3-coder`, or on OpenRouter `openai/gpt-5-mini:online` (web search) |
| `role` | a saved role, or a new one: describe what you need and AI writes `{title, instructions, tools}`, which you review and save |
| `skin` | any of the 8 animals, or random (unused animals first) |
| `name` | anything |

**Tools** (`src/tools.js`) are locked to `WORKSPACE_DIR`. Each role gets only
the tools it was granted:

| tool | does |
| --- | --- |
| `read_file`, `list_files`, `search_files` | read the project |
| `write_file`, `edit_file`, `run_command` ⚠ | change the project (AI-drafted roles flag these for the CEO) |
| `fetch_url` | public http(s) pages only; local/private addresses are blocked |
| `check_page` | open the project's page **from this task's worktree** in a real browser: status, title, console errors, text |

Shell commands run without any `*KEY*/*TOKEN*/*SECRET*` variables in their
environment, with a timeout, and are killed on STOP.

**Projects & workspaces.** The team works on *projects* (git repos): register
your existing repos, or let the loft create a new one from a starter.
`WORKSPACE_DIR` becomes the first project automatically. **Every task runs in its
own git worktree on its own branch** (`loft/<agent>/<task>`), so parallel agents
never collide and `main` is untouched until you decide:

| CEO | what happens in git |
| --- | --- |
| **approve** | squash-merge into the project's main branch: one clean commit per task, worktree removed |
| **reject + feedback** | the same agent continues in the same worktree/branch |
| conflict on approve | task marked `conflict`, the same agent gets "merge main and resolve" in its worktree |

Approve refuses if you have uncommitted changes on main (it never touches your work).
Agents never push; publishing stays with you.

**Preview.** When a task with file changes reaches review, the loft runs the
project's dev server **from that task's worktree** (or a built-in static server
for plain HTML), and Playwright takes desktop + phone screenshots. They appear on
the PREVIEW screen in VR, and the live URL stays up while the task is in review.
Agents get `check_page` to open their own page in a real browser before saying "done".

```bash
npx playwright install chromium   # once, ~100 MB
curl -XPOST localhost:4101/projects -d '{"name":"shop","path":"~/code/shop","dev":"npm run dev -- --port {port}","setup":"npm ci"}'
curl -XPOST localhost:4101/projects/new -d '{"name":"Landing Site","template":"static"}'
```

Project fields: `dev` = dev-server command (`{port}` / `$PORT` filled in),
`setup` = runs once per new worktree (default: link the main checkout's
`node_modules` if present), `url` = preview URL template (default
`http://127.0.0.1:{port}/`).

**Flow:** CEO goal → the planner splits it by role → tasks queue per agent →
agents run **in parallel** (one task at a time each) → every tool call streams
to Unity ("edit_file src/auth.ts") → result waits in **review** → approve, or
reject with feedback (re-queued). Tokens are counted per task and per agent;
cost in $ is added only on OpenRouter (public price list); on any other provider `costUsd` stays `null` and tokens are still counted.

## Run

```bash
cd backend
npm install
# put AGENT_API_KEY / AGENT_BASE_URL / AGENT_MODEL in the repo-root .env (see .env.example)
npm run check                   # one real agent turn with a tool call; exit 0 = works
npm run serve                   # http://127.0.0.1:4101 (loads ../.env, then backend/.env if present)
tools/setup-stt.sh              # once, for local voice (whisper.cpp + multilingual model)
npm test                        # offline: orchestrator + real SDK loop vs a fake LLM server
```

`LLM_API_KEY`, `LLM_BASE_URL` and `LOFT_DEFAULT_MODEL` still work as overrides of the `AGENT_*` values. Models that return a `reasoning` field or `<think>` text are fine: think text is stripped and JSON is extracted tolerantly (code fences, prose around it).

**Pick models that support tool calling.** Cheap models are fine for reading
and reviewing roles. Coding roles need a model that handles long multi-step tool
use, otherwise tasks fail or stop early (you'll see them as FAILED, with their cost).

## Quest on Wi-Fi

```bash
HOST=0.0.0.0 LOFT_TOKEN=$(openssl rand -hex 24) npm run serve
echo '{"url":"http://<mac-lan-ip>:4101","token":"<same token>"}' > loft-server.json
adb push loft-server.json /sdcard/Android/data/com.defaultcompany.focusloft/files/
```

The server refuses non-loopback hosts without `LOFT_TOKEN`, because agents can run shell commands.

## API (JSON, `Authorization: Bearer <LOFT_TOKEN>` when set)

| | |
| --- | --- |
| `GET /events` | Server-Sent Events: `snapshot` (agents, tasks, roles, skins, tools, defaultModel), then `agent`, `agent.removed`, `task`, `goal`, `roles` |
| `GET /state` | the snapshot |
| `POST /agents {model?, roleId? \| role, name?, skin?}` | spawn (409 at 20) |
| `DELETE /agents/:id` | remove (stops its work) |
| `POST /goal {goal, projectId?}` | split across the team, in that project |
| `POST /tasks {agentId, title, projectId?}` | give one agent a task |
| `POST /tasks/:id/approve` · `/reject {feedback}` | CEO review |
| `POST /stop {agentId?}` | stop one agent or all |
| `GET /roles` · `POST /roles {title, instructions, tools}` · `DELETE /roles/:id` | role library (`data/roles.json`) |
| `POST /roles/draft {description}` | AI-written role draft (`writes` lists the tools that change things) |
| `POST /party {on}` | party mode on/off, broadcast to every headset (late joiners get it in the snapshot) |
| `GET /projects` · `POST /projects {name, path, dev?, setup?, url?, init?}` · `POST /projects/new {name, template}` · `DELETE /projects/:id` | projects (unregister keeps the folder) |
| `GET /previews/<task>-desktop.png` / `-mobile.png` | screenshots of a task in review |
| `POST /voice?agentId=&projectId=&scene=` | raw 16 kHz mono 16-bit WAV (max 10 MB) -> `{transcript, summary, needsConfirm, risky, intent}` |
| `POST /voice/text {text, agentId?, projectId?, scene?}` | same without audio |
| `GET /loft/:room[/boards]` · `POST /loft` | board layout store |

## Voice

`POST /voice` turns speech into one validated **intent** for the confirm card;
`POST /voice/text` does the same from text (tests, desktop harness). Actions:
`goal`, `task`, `spawn`, `remove`, `scene`, `approve`, `reject`, `stop`, `party`,
`project`, `selectNext`, `none{reply}`. `risky` (approve, reject, remove, stop-all)
must be pressed in VR; `needsConfirm` is false for scene, party, selectNext, none.
Approve/reject carry the `taskId` of the review task they refer to.

- English, Indonesian and mixed speech. Spoken code words are fixed ("slash health" -> `/health`, "dot js" -> `.js`).
- The intent prompt includes the live roster, roles, projects, review queue, the pointed-at agent and current scene,
  and every id in the answer is checked against them. Bad model output gives `action: "none"` with a reply, never an error.
- A pointed-at `agentId` means "this one": a bare instruction becomes a `task` for it.
- One action per utterance ("switch to the city and start the party" yields the first).

STT is chosen by env (`src/stt.js`): `STT_PROVIDER=local` (whisper.cpp, language auto-detected) or `openai`
(`STT_BASE_URL`, `STT_API_KEY`, `STT_MODEL`, any OpenAI-compatible `/audio/transcriptions`).
Local setup: `tools/setup-stt.sh` (builds `whisper-cli` and downloads `ggml-small.bin` into the gitignored `data/stt/`;
on macOS 14 or older `brew install whisper-cpp` compiles llvm and rust from source, so the script builds whisper.cpp directly).
Quiet or silent audio never reaches whisper (it invents text on silence).

Cloud deployment notes: `DEPLOY.md`.

## Limits (on purpose, for now)

- Team state is in memory: restarting the server starts a fresh team (the role library is kept).
- Worktrees of rejected/failed/stopped tasks stay in `data/worktrees/` until the task is approved (reject reuses them).
- Desktop-app preview (Electron via Playwright, native via window capture) is the next step.
