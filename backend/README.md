# VIRTUAL LOFT — backend (living agents)

`backend/` only. Concurrency backend for researcher + developer: 3 living agents
(Scout / Builder / Keeper) via the Claude Agent SDK, loft layout persisted to
Supabase with a local-file fallback. No QA work lives here.

**No secret is committed.** `.env` is gitignored; only `.env.example`
(placeholders) is tracked. Credentials live in your local `backend/.env` only.

## Setup

```bash
cd backend
npm install
cp .env.example .env
```

## Credentials (Muse Spark 1.3 brain, three runners)

Brain is always **Muse Spark 1.3 Contributor** (`ANTHROPIC_MODEL` default,
gateway `ANTHROPIC_BASE_URL`). No Claude model is used. Keys come **only**
from the environment — `backend/.env` never exists in a fresh checkout.

**Default — official OpenAI SDK (simplest, verified live).**
`runAgent()` uses `openai` pointed at the gateway (`baseURL` + `apiKey`,
both natively supported) and `chat.completions.runTools()` runs the agentic
loop: model calls tool → local handler executes → result fed back, until
final text. Check: `node --env-file=.env tools/check-connection.js`.

**Fallback — direct fetch (zero deps).** Same behavior hand-rolled:
`runner: "direct"`. Kept for environments where the `openai` package
can't be installed.

**Optional — real Claude Agent SDK (verified end-to-end 2026-11-02).**
`runner: "sdk"` drives the same roles through `query()` + in-process MCP
`loft` server. The SDK CLI speaks Anthropic-native auth (`x-api-key`) while
this gateway needs `Authorization: Bearer`, and validates model names
client-side — so it drives Spark through the localhost bridge:

```bash
node --env-file=.env tools/spark-bridge.mjs --port 3847   # terminal 1
ANTHROPIC_BASE_URL=http://127.0.0.1:3847 node --env-file=.env \
  -e "import('./src/agents.js').then(async ({runAgent}) => {
    console.log(await runAgent('scout', 'summarize Quest 3 travel setups', { runner: 'sdk' }));
  })"                                                      # terminal 2
```

What the bridge does (all localhost): terminates the SDK's Anthropic
`POST /v1/messages` (+`GET /v1/models`, `HEAD /api/hello`), rewrites the
model label to Spark upstream (the CLI only accepts `claude-*` names —
weights are always Spark's), converts tools `input_schema`↔`parameters` and
`tool_use`↔`tool_calls` both ways, synthesizes Anthropic SSE for streaming
calls. Verified: 14 tools presented, `tool_use`/`tool_result` round-trips,
Spark-authored wall card returned, exit 0.

The harness keeps its saved key in its own server-side store (not the macOS
keychain, not this repo), so this backend cannot pull it automatically.
One-time setup — paste your saved key yourself:

```bash
cp .env.example .env   # then edit .env and paste the values below
set -a; source .env; set +a
node tools/check-connection.js   # exit 0 = brain reachable, key never printed
```

| `.env` key           | Value to paste                                           |
| -------------------- | -------------------------------------------------------- |
| `ANTHROPIC_BASE_URL` | Muse Spark 1.3 gateway URL                               |
| `ANTHROPIC_API_KEY`  | your saved Muse Spark 1.3 Contributor API key            |
| `SUPABASE_URL`       | your Supabase project URL                                |
| `SUPABASE_ANON_KEY`  | your Supabase anon key                                   |

Apply the cloud schema once in the Supabase SQL editor:
`supabase/schema.sql` (tables: `teams`, `lofts`, `boards`, `tasks`,
`agent_runs`). Without `SUPABASE_*` set, `src/loftStore.js` automatically uses
the local-file fallback (`backend/data/lofts/<roomId>.json`, gitignored).

## Travel-reload test (kos → cafe)

The cloud JSON is the source of truth. With Supabase configured:

```bash
set -a; source .env; set +a   # load backend/.env into the shell
node -e "import('./src/loftStore.js').then(async ({saveLoft,loadLoft}) => {
  const s = await saveLoft({roomId:'focus-loft',boards:[],agents:[],version:0,updatedAt:new Date().toISOString()});
  const l = await loadLoft('focus-loft');
  console.log('saved v'+s.version, 'loaded v'+l.version, JSON.stringify(l)===JSON.stringify(s) ? 'MATCH' : 'DIFF');
})"
```

Close the app, run the same load on a second machine: same
`roomId/boards/agents/version` must come back.

## Demo (offline smoke test, no network, no credentials)

```bash
npm test
# or: npm run demo
```

Loads `quest_loft/Assets/Boards/LoftLayout.json` when it exists, else an
inline sample loft; runs a save→reload roundtrip plus one fake task per agent
role (Scout search, Builder test run, Keeper card move).

## Layout

- `src/agents.js` — Scout/Builder/Keeper tool defs + `runAgent(role, task)`
  (Claude Agent SDK `query()`, lazy import) + `routeOfflineTask()` for demos.
- `src/loftStore.js` — `loadLoft` / `saveLoft` for
  `{roomId, boards, agents, version, updatedAt}`; Supabase upsert on
  `lofts(room_id)` with local-file fallback.
- `supabase/schema.sql` — `teams, lofts, boards, tasks, agent_runs` (+FKs,
  timestamps, version fields).
- `test/demo.js` — offline smoke test.
