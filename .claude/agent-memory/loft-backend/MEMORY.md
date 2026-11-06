# loft-backend notes

## Done (2026-11-06)
- Env: repo-root .env is the single source. `src/env.js` (loadEnv, llmConfig, llmProblems), `src/load-env.js` (import first in entry points), `src/llm.js` (client, askJson, extractJson, stripThink). AGENT_API_KEY/AGENT_BASE_URL/AGENT_MODEL; LLM_*/LOFT_DEFAULT_MODEL override. No default endpoint/model: server exits naming missing vars; defaultModel() returns "" when unset.
- Voice: `src/voice.js` (code words, normalizeIntent, summaries, createVoice), `src/stt.js` (local whisper.cpp | openai-compatible), routes POST /voice (raw WAV, 10 MB) and /voice/text in server.js. approve/reject intents carry taskId (extra field).
- `LOFT_DATA_DIR` env added to server (roles/projects/worktrees/previews dir), used by tests and Docker.
- Dockerfile, .dockerignore, DEPLOY.md, tools/setup-stt.sh, README + .env.example updated.
- Tests: env.test.js, voice.test.js, voice-routes.test.js (spawns real server with fake LLM + fake whisper script), helpers.js. runner.test.js sets LOFT_DEFAULT_MODEL now. 44 pass.

## Verified (ran)
- `npm run check` against Nebius DeepSeek-V4.1-Flash: tool calling works. Real task e2e (spawn, task, write_file+run_command, review, approve): works, costUsd null (non-OpenRouter).
- Real /voice with macOS `say` audio (Samantha, Daniel, Damayanti id_ID), whisper small, Metal: ~0.9 s STT + ~0.6-1.2 s intent per utterance.

## Lessons
- brew on macOS 14 has no bottles: `brew install whisper-cpp` tries to compile llvm+rust (hours). Built whisper.cpp via pip-installed cmake instead (binary in backend/data/stt/bin). Disk was ~200 MB free at one point; freed ~4 GB by deleting ~/.npm/_cacache and _npx (regenerable caches).
- Whisper auto language on mixed Indonesian/English: WITH an English vocabulary --prompt it kept English words right; without it, garbage. Keep the hint.
- `reasoning` field on responses is harmless; content has the JSON. Server start mutates data/projects.json (adds WORKSPACE_DIR as a project): back it up when testing against a scratch workspace, or use LOFT_DATA_DIR.
- macOS `date` has no %N; no `timeout`.

## Unverified / next
- Dockerfile never built (Docker daemon not running). Cloud STT provider (openai mode) only tested with a fake fetch.
- Real human speech/accents untested (TTS audio is cleaner than a Quest mic). Try ggml-medium or large-v3-turbo-q5_0 if small mishears.
- Local whisper spawns a process per call (model load each time, ~0.9 s). A resident whisper-server would cut that.
- One action per utterance; compound commands lose the second.

## Integration lessons (lead, 2026-11-06)
- Docker image built and run: auth, DeepSeek, /voice/text and a real agent task pass. /voice in the container needs STT_* (managed provider), still untested for real.
- Fixed: `backend/workspace` sat inside the vr_games repo, so tasks branched and would merge into the user's real main. Projects must now be the root of their own repo (`isRepoRoot`), `prepare` refuses otherwise.

## Round 2 (stopped early: quota). npm test = 61 pass.
DONE + tested (test/state.test.js, test/api.test.js, real Chromium for previews):
- Spawn fix: POST /agents {description} drafts a role server-side; voice spawn intent carries `role` + `writes`; unknown roleId text becomes description.
- Persistence: data/state.json (team, tasks, chat memory, suggestions); running/queued tasks become "stopped" after restart.
- Chat: POST /agents/:id/chat (+SSE say), memory 24 msgs. Voice actions `chat`, `board`.
- Endpoints: /boards?scene, /notes CRUD, /projects/:id/tree|file (traversal/symlink/.env blocked, optional ?taskId), /tasks/:id/diff, /suggestions (+accept/dismiss, LOFT_SUGGESTIONS=0), /preview/sessions (cap 3, idle 120s, metadata/file URLs blocked, task previews origin-locked).
- New files: src/stores.js, files.js, preview-sessions.js. Tests that spawn the server must pass PLAYWRIGHT_BROWSERS_PATH when overriding HOME.
NOT STARTED: STT work: large-v3-turbo download, faster-whisper Docker server, tools/stt-compare, STT_SAVE_SAMPLES, accuracy/latency comparison. Docker image never built. README/DEPLOY not updated for Round 2 endpoints. Not tested against the real LLM (only fake LLM): suggestions, chat, new voice actions.
NEXT: STT items above, then README endpoint table, then live check of chat/suggestions on a non-4101 port.
