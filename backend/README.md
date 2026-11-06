# VIRTUAL LOFT — backend (the agents)

Spawnable AI agents (default **3**, max **20**) on the **OpenAI Agents SDK**,
talking to any OpenAI-compatible endpoint: **OpenRouter** (default), the Muse
Spark gateway, OpenAI, a local server, by setting `LLM_BASE_URL`.

Every agent is free-form, set in the spawn form:

| field | |
| --- | --- |
| `model` | any model id you type, e.g. `anthropic/claude-haiku-5-5`, `qwen/qwen3-coder`, `openai/gpt-5-mini:online` (OpenRouter `:online` = web search) |
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

Shell commands run without any `*KEY*/*TOKEN*/*SECRET*` variables in their
environment, with a timeout, and are killed on STOP.

**Flow:** CEO goal → the planner splits it by role → tasks queue per agent →
agents run **in parallel** (one task at a time each) → every tool call streams
to Unity ("edit_file src/auth.ts") → result waits in **review** → approve, or
reject with feedback (re-queued). Tokens are counted per task and per agent;
cost in $ is added when the model is in OpenRouter's public price list.

## Run

```bash
cd backend
npm install
cp .env.example .env            # LLM_API_KEY, LOFT_DEFAULT_MODEL, WORKSPACE_DIR
npm run check -- qwen/qwen3-coder   # one real agent turn with a tool call; exit 0 = works
npm run serve                   # http://127.0.0.1:4101
npm test                        # offline: orchestrator + real SDK loop vs a fake LLM server
```

Muse Spark gateway: `LLM_BASE_URL=<gateway>/v1`, `LLM_API_KEY=<key>`, and use its model ids.

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
| `POST /goal {goal}` | split across the team |
| `POST /tasks {agentId, title}` | give one agent a task |
| `POST /tasks/:id/approve` · `/reject {feedback}` | CEO review |
| `POST /stop {agentId?}` | stop one agent or all |
| `GET /roles` · `POST /roles {title, instructions, tools}` · `DELETE /roles/:id` | role library (`data/roles.json`) |
| `POST /roles/draft {description}` | AI-written role draft (`writes` lists the tools that change things) |
| `POST /party {on}` | party mode on/off, broadcast to every headset (late joiners get it in the snapshot) |
| `GET /loft/:room[/boards]` · `POST /loft` | board layout store |

## Limits (on purpose, for now)

- Team state is in memory: restarting the server starts a fresh team (the role library is kept).
- All agents share one workspace; give each its own git worktree if parallel builders collide.
