// VIRTUAL LOFT — HTTP + Server-Sent Events bridge (Unity <-> agents).
// Zero extra deps (node:http). Run from backend/:
//
//   node src/server.js [--port 4101]     (loads ../.env itself: AGENT_API_KEY/AGENT_BASE_URL/AGENT_MODEL)
//
// Env: HOST (default 127.0.0.1; use 0.0.0.0 so a Quest on Wi-Fi can reach it),
//      LOFT_TOKEN (required when HOST is not loopback — agents run Bash!),
//      WORKSPACE_DIR (the repo the agents work in, default backend/workspace).
//
// Endpoints (JSON; send `Authorization: Bearer <LOFT_TOKEN>` when set):
//   GET  /health                  -> { ok, workspace }
//   GET  /state                   -> snapshot { agents, tasks, maxAgents }
//   GET  /events                  -> SSE stream: snapshot first, then
//                                    {type:"agent"|"agent.removed"|"task"|"goal"}
//   POST /agents {model?, roleId? | role:{title,instructions,tools}, name?, skin?}
//                                 -> spawn (max 20; skin omitted/"random" = unused animal)
//   DELETE /agents/:id            -> remove (stops its work)
//   POST /tasks {agentId,title}   -> queue a task on one agent
//   POST /goal {goal}             -> planner splits it across the team
//   POST /tasks/:id/approve       -> CEO accepts the result
//   POST /tasks/:id/reject {feedback?} -> re-queue with feedback
//   POST /stop {agentId?}         -> stop one agent or everyone
//   GET  /roles · POST /roles {title,instructions,tools} · DELETE /roles/:id
//   POST /roles/draft {description} -> AI-written role for the CEO to review
//   POST /voice?agentId=&projectId=&scene=  raw 16 kHz mono WAV (max 10 MB) -> {transcript,summary,needsConfirm,risky,intent}
//   POST /voice/text {text,agentId?,projectId?,scene?}  same, no audio (STT_PROVIDER=local|openai, see src/stt.js)
//   POST /party {on}              -> party mode on/off for every connected headset
//   GET /projects · POST /projects {name,path,dev?,setup?,url?,kind?,init?} (register a repo)
//   POST /projects/new {name, template: empty|static} · DELETE /projects/:id (unregister)
//   POST /cinema {url} -> prepare a video for VR · GET /cinema/:id -> progress · GET /cinema/:id/video.mp4
//   GET /previews/<task>-desktop.png|-mobile.png -> screenshots of a task in review
// Goals/tasks take projectId. Every task runs in its own git worktree; approve =
// squash-merge into the project's main branch (agents never push).
//   GET  /loft/:roomId[/boards], POST /loft -> board layout store

import { loadedEnvFiles } from "./load-env.js"; // must stay first: fills process.env from the repo-root .env
import crypto from "node:crypto";
import fs from "node:fs";
import http from "node:http";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { llmConfig, llmProblems } from "./env.js";
import { chatReply, draftRole, planGoal, priceOf, runTask, suggestNext } from "./agents.js";
import { askJson, extractJson } from "./llm.js";
import { createBoards, createNotes, jsonFile } from "./stores.js";
import { createStt, MAX_AUDIO } from "./stt.js";
import { createVoice } from "./voice.js";
import { blankLoft, loadLoft, saveLoft, validateLoft } from "./loftStore.js";
import { createOrchestrator, HttpError } from "./orchestrator.js";
import { readFile, taskDiff, tree } from "./files.js";
import { createCinema } from "./cinema.js";
import { createPreviewSessions } from "./preview-sessions.js";
import { createPreviews } from "./previews.js";
import { createProjects } from "./projects.js";

const here = path.dirname(fileURLToPath(import.meta.url));
const argPort = process.argv.indexOf("--port");
const PORT = Number(argPort > -1 ? process.argv[argPort + 1] : (process.env.PORT ?? 4101));
const HOST = process.env.HOST ?? "127.0.0.1";
const TOKEN = process.env.LOFT_TOKEN ?? "";
const WORKSPACE = path.resolve(process.env.WORKSPACE_DIR ?? path.join(here, "../workspace"));
const MAX_BODY = 1_000_000; // JSON; audio has its own limit (MAX_AUDIO, 10 MB)

if (!["127.0.0.1", "localhost", "::1"].includes(HOST) && !TOKEN) {
  console.error(`Refusing to listen on ${HOST} without LOFT_TOKEN: agents can run Bash in ${WORKSPACE}.`);
  process.exit(1);
}
const missing = llmProblems();
if (missing.length) {
  console.error(`LLM not configured: missing ${missing.join(", ")}. Put them in the repo-root .env (see backend/README.md).`);
  process.exit(1);
}
fs.mkdirSync(WORKSPACE, { recursive: true });

const DATA = path.resolve(process.env.LOFT_DATA_DIR ?? path.join(here, "../data")); // a mounted volume in the cloud
const ROLES_FILE = path.join(DATA, "roles.json");
const savedRoles = (() => {
  try {
    return JSON.parse(fs.readFileSync(ROLES_FILE, "utf8"));
  } catch {
    return [];
  }
})();
const PREVIEW_DIR = path.join(DATA, "previews");
const projects = createProjects({
  dataDir: DATA,
  projectsDir: path.resolve(process.env.PROJECTS_DIR ?? path.join(here, "../projects")),
  defaultPath: WORKSPACE, // WORKSPACE_DIR becomes the first project (git init if needed)
});
await projects.load();
const previews = createPreviews({ dir: PREVIEW_DIR });
const stateFile = jsonFile(path.join(DATA, "state.json"), null); // team, tasks, chat memory, suggestions survive restarts
const sessions = createPreviewSessions({ browser: () => previews.browser() });
const cinema = createCinema({ dir: path.join(DATA, "cinema") });
for (const sig of ["SIGINT", "SIGTERM"]) {
  process.on(sig, () => {
    try {
      loft.flush();
    } catch {
      /* best effort */
    }
    sessions.closeAll().then(() => previews.close()).finally(() => process.exit(0));
  });
}

const loft = createOrchestrator({
  runTask, planGoal, draftRole, priceOf, workspace: WORKSPACE, roles: savedRoles, projects, previews,
  chat: chatReply, suggest: suggestNext, state: stateFile.read(), saveState: (st) => stateFile.write(st),
  saveRoles: (list) => {
    fs.mkdirSync(path.dirname(ROLES_FILE), { recursive: true });
    fs.writeFileSync(ROLES_FILE, JSON.stringify(list, null, 2));
  },
});
const boards = createBoards(path.join(DATA, "boards.json"));
const notes = createNotes(path.join(DATA, "notes.json"), { emit: (e) => loft.events.emit("event", e) });
const voice = createVoice({
  draft: (d) => loft.draft(d),
  snapshot: () => loft.snapshot(),
  ask: (system, prompt) => askJson(process.env.LOFT_VOICE_MODEL || llmConfig().model, system, prompt, { temperature: 0, maxTokens: 2000 }),
  extract: (t) => extractJson(t, "{"),
  stt: createStt(),
});
const storeOpts = { localOnly: !process.env.SUPABASE_URL };

const CORS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET,POST,PUT,PATCH,DELETE,OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type, Authorization",
};

const readRaw = (req, max) =>
  new Promise((resolve, reject) => {
    const chunks = [];
    let size = 0;
    let over = false;
    req.on("data", (c) => {
      size += c.length;
      if (over) return; // keep draining so the 413 reply can reach the client
      if (size > max) {
        over = true;
        chunks.length = 0;
        reject(new HttpError(413, "body too large"));
      } else chunks.push(c);
    });
    req.on("end", () => resolve(Buffer.concat(chunks)));
    req.on("error", reject);
  });

const readBody = async (req) => {
  const buf = await readRaw(req, MAX_BODY);
  try {
    return buf.length ? JSON.parse(buf.toString("utf8")) : {};
  } catch {
    throw new HttpError(400, "invalid JSON body");
  }
};

const send = (res, code, obj) => {
  res.writeHead(code, { "Content-Type": "application/json", ...CORS });
  res.end(JSON.stringify(obj));
};

function authorized(req) {
  if (!TOKEN) return true;
  const got = Buffer.from(String(req.headers.authorization ?? "").replace(/^Bearer\s+/i, ""));
  const want = Buffer.from(TOKEN);
  return got.length === want.length && crypto.timingSafeEqual(got, want);
}

function openEvents(req, res) {
  res.writeHead(200, { "Content-Type": "text/event-stream", "Cache-Control": "no-cache", Connection: "keep-alive", ...CORS });
  const write = (e) => res.write(`data: ${JSON.stringify(e)}\n\n`);
  write(loft.snapshot());
  loft.events.on("event", write);
  const ping = setInterval(() => res.write(": ping\n\n"), 15000);
  req.on("close", () => {
    clearInterval(ping);
    loft.events.off("event", write);
  });
}

async function route(req, res) {
  const url = new URL(req.url ?? "/", "http://x");
  const p = url.pathname;
  const m = req.method;
  let g;

  if (m === "OPTIONS") return send(res, 204, {});
  if (m === "GET" && p === "/health") return send(res, 200, { ok: true, workspace: WORKSPACE });
  // The video player can't send a token: the long random id in the path is the permission.
  const clip = m === "GET" && p.match(/^\/cinema\/([0-9a-f]{32})\/video\.mp4$/);
  if (clip) return cinema.stream(clip[1], req, res, CORS);
  if (!authorized(req)) return send(res, 401, { ok: false, error: "missing or wrong LOFT_TOKEN" });

  if (m === "POST" && p === "/voice") {
    const wav = await readRaw(req, MAX_AUDIO);
    if (!wav.length) throw new HttpError(400, "empty audio body (send raw 16 kHz mono 16-bit WAV)");
    const q = url.searchParams;
    return send(res, 200, await voice.handleAudio(wav, { agentId: q.get("agentId"), projectId: q.get("projectId"), scene: q.get("scene") }));
  }
  if (m === "POST" && p === "/voice/text") return send(res, 200, await voice.handleText(await readBody(req)));
  if (m === "GET" && p === "/events") return openEvents(req, res);
  if (m === "GET" && p === "/state") return send(res, 200, loft.snapshot());
  if (m === "POST" && p === "/agents") {
    const b = await readBody(req);
    // Voice sends "spawn a data analyst" as a description: write the role here when none was given.
    if (!b.role && !b.roleId && typeof b.description === "string" && b.description.trim()) {
      const { writes, ...role } = await loft.draft(b.description);
      b.role = role;
    }
    return send(res, 201, { ok: true, agent: loft.spawn(b) });
  }
  if (m === "POST" && (g = p.match(/^\/agents\/([^/]+)\/chat$/))) return send(res, 200, { ok: true, ...(await loft.chat(decodeURIComponent(g[1]), (await readBody(req)).text)) });
  if (m === "DELETE" && (g = p.match(/^\/agents\/([^/]+)$/))) {
    loft.remove(decodeURIComponent(g[1]));
    return send(res, 200, { ok: true });
  }
  if (m === "POST" && p === "/tasks") {
    const { agentId, title, projectId } = await readBody(req);
    return send(res, 201, { ok: true, task: loft.assign(String(agentId ?? ""), title, projectId || null) });
  }
  if (m === "POST" && p === "/goal") {
    const { goal, projectId } = await readBody(req);
    return send(res, 201, { ok: true, tasks: await loft.goal(goal, projectId || null) });
  }
  if (m === "GET" && p === "/boards") return send(res, 200, boards.get(url.searchParams.get("scene")));
  if (m === "PUT" && p === "/boards") {
    const b = await readBody(req);
    return send(res, 200, boards.put(url.searchParams.get("scene"), Array.isArray(b) ? b : b.boards));
  }
  if (m === "GET" && p === "/notes") return send(res, 200, { notes: notes.list() });
  if (m === "POST" && p === "/notes") return send(res, 201, { ok: true, note: notes.create(await readBody(req)) });
  if (m === "PATCH" && (g = p.match(/^\/notes\/([^/]+)$/))) return send(res, 200, { ok: true, note: notes.update(decodeURIComponent(g[1]), await readBody(req)) });
  if (m === "DELETE" && (g = p.match(/^\/notes\/([^/]+)$/))) {
    notes.remove(decodeURIComponent(g[1]));
    return send(res, 200, { ok: true });
  }
  if (m === "GET" && (g = p.match(/^\/projects\/([^/]+)\/(tree|file)$/))) {
    const project = projects.get(decodeURIComponent(g[1]));
    let root = project.path;
    const taskId = url.searchParams.get("taskId"); // look inside a task in review instead of main
    if (taskId) {
      const t = loft.getTask(taskId);
      if (t.projectId !== project.id || !t.worktree) throw new HttpError(404, "that task has no folder to browse");
      root = t.worktree;
    }
    const rel = url.searchParams.get("path") ?? "";
    return send(res, 200, g[2] === "tree" ? await tree(root, rel) : await readFile(root, rel));
  }
  if (m === "GET" && (g = p.match(/^\/tasks\/([^/]+)\/diff$/))) {
    const t = loft.getTask(decodeURIComponent(g[1]));
    return send(res, 200, await taskDiff(projects.get(t.projectId), t));
  }
  if (m === "GET" && p === "/suggestions") return send(res, 200, { suggestions: loft.snapshot().suggestions });
  if (m === "POST" && (g = p.match(/^\/suggestions\/([^/]+)\/(accept|dismiss)$/))) {
    const id = decodeURIComponent(g[1]);
    if (g[2] === "accept") return send(res, 201, { ok: true, task: loft.acceptSuggestion(id) });
    loft.dismissSuggestion(id);
    return send(res, 200, { ok: true });
  }
  if (m === "POST" && p === "/preview/sessions") {
    const b = await readBody(req);
    let target;
    if (b.taskId) {
      const t = loft.getTask(String(b.taskId));
      if (t.status !== "review" || !t.worktree) throw new HttpError(409, "only a task in review has a live preview");
      target = { url: (await previews.serve(t.id, projects.get(t.projectId), t.worktree)).url, lockOrigin: true, taskId: t.id };
    } else if (typeof b.url === "string") target = { url: b.url };
    else throw new HttpError(400, "send taskId or url");
    return send(res, 201, { ok: true, ...(await sessions.open({ ...target, width: b.width, height: b.height })) });
  }
  if (m === "POST" && p === "/cinema") return send(res, 202, await cinema.prepare((await readBody(req)).url));
  if (m === "GET" && (g = p.match(/^\/cinema\/([0-9a-f]{32})$/))) return send(res, 200, cinema.status(g[1]));
  if (m === "GET" && p === "/preview/sessions") return send(res, 200, { sessions: sessions.list() });
  if (m === "GET" && (g = p.match(/^\/preview\/sessions\/(\w+)$/))) return send(res, 200, await sessions.info(g[1]));
  if (m === "GET" && (g = p.match(/^\/preview\/sessions\/(\w+)\/frame$/))) {
    const jpg = await sessions.frame(g[1]);
    res.writeHead(200, { "Content-Type": "image/jpeg", "Cache-Control": "no-store", ...CORS });
    return res.end(jpg);
  }
  if (m === "POST" && (g = p.match(/^\/preview\/sessions\/(\w+)\/input$/))) return send(res, 200, await sessions.input(g[1], await readBody(req)));
  if (m === "DELETE" && (g = p.match(/^\/preview\/sessions\/(\w+)$/))) {
    if (!(await sessions.close(g[1]))) throw new HttpError(404, "no such preview session");
    return send(res, 200, { ok: true });
  }
  if (m === "GET" && p === "/projects") return send(res, 200, { projects: projects.list(), templates: projects.templates });
  if (m === "POST" && p === "/projects") {
    const project = await projects.add(await readBody(req));
    loft.projectsChanged();
    return send(res, 201, { ok: true, project });
  }
  if (m === "POST" && p === "/projects/new") {
    const project = await projects.create(await readBody(req));
    loft.projectsChanged();
    return send(res, 201, { ok: true, project });
  }
  if (m === "DELETE" && (g = p.match(/^\/projects\/([^/]+)$/))) {
    await projects.remove(decodeURIComponent(g[1]));
    loft.projectsChanged();
    return send(res, 200, { ok: true });
  }
  if (m === "GET" && (g = p.match(/^\/previews\/([\w.-]+\.png)$/))) {
    const file = path.join(PREVIEW_DIR, g[1]);
    if (!fs.existsSync(file)) return send(res, 404, { ok: false, error: "no such preview" });
    res.writeHead(200, { "Content-Type": "image/png", "Cache-Control": "no-store", ...CORS });
    return fs.createReadStream(file).pipe(res);
  }
  if (m === "POST" && (g = p.match(/^\/tasks\/([^/]+)\/(approve|reject)$/))) {
    const id = decodeURIComponent(g[1]);
    if (g[2] === "approve") return send(res, 200, { ok: true, task: await loft.approve(id) });
    const { feedback } = await readBody(req);
    return send(res, 200, { ok: true, retry: loft.reject(id, feedback) });
  }
  if (m === "POST" && p === "/party") {
    const { on } = await readBody(req);
    return send(res, 200, { ok: true, on: loft.setParty(on) });
  }
  if (m === "GET" && p === "/roles") return send(res, 200, { roles: loft.snapshot().roles });
  if (m === "POST" && p === "/roles/draft") {
    const { description } = await readBody(req);
    return send(res, 200, { ok: true, draft: await loft.draft(description) });
  }
  if (m === "POST" && p === "/roles") return send(res, 201, { ok: true, role: loft.saveRole(await readBody(req)) });
  if (m === "DELETE" && (g = p.match(/^\/roles\/([^/]+)$/))) {
    loft.deleteRole(decodeURIComponent(g[1]));
    return send(res, 200, { ok: true });
  }
  if (m === "POST" && p === "/stop") {
    const { agentId } = await readBody(req);
    loft.stop(agentId ? String(agentId) : undefined);
    return send(res, 200, { ok: true });
  }

  if (m === "GET" && p.startsWith("/loft/")) {
    const rest = decodeURIComponent(p.slice("/loft/".length));
    const boardsOnly = rest.endsWith("/boards");
    const roomId = boardsOnly ? rest.slice(0, -"/boards".length) : rest;
    const doc = await loadLoft(roomId, storeOpts);
    if (!doc) return send(res, 404, { ok: false, error: `no loft '${roomId}'` });
    return send(res, 200, boardsOnly ? { boards: doc.boards } : { ok: true, loft: doc });
  }
  if (m === "POST" && p === "/loft") {
    // Accepts a full loft OR Unity's {roomId, snapshot:{boards:[...]}}; merges
    // onto the stored loft so version history survives a board-only push.
    const body = await readBody(req);
    const roomId = String(body.roomId ?? body?.loft?.roomId ?? "focus-loft");
    const boards = body.boards ?? body?.snapshot?.boards ?? body?.loft?.boards;
    const current = (await loadLoft(roomId, storeOpts)) ?? blankLoft(roomId);
    if (boards !== undefined) current.boards = boards;
    const saved = await saveLoft(validateLoft(current), storeOpts);
    return send(res, 200, { ok: true, loft: saved });
  }
  return send(res, 404, { ok: false, error: `no route ${m} ${p}` });
}

const server = http.createServer((req, res) =>
  route(req, res).catch((err) => {
    if (res.headersSent) return res.end();
    send(res, err?.status ?? 500, { ok: false, error: String(err?.message ?? err).slice(0, 300) });
  }),
);

server.listen(PORT, HOST, () => {
  console.log(`loft server: http://${HOST}:${PORT}  workspace: ${WORKSPACE}  auth: ${TOKEN ? "token" : "none (loopback only)"}`);
  console.log(`llm: ${llmConfig().baseURL}  default model: ${llmConfig().model}  (env files: ${loadedEnvFiles.length})`);
});
