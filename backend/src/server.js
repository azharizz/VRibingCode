// VIRTUAL LOFT — HTTP + Server-Sent Events bridge (Unity <-> agents).
// Zero extra deps (node:http). Run from backend/:
//
//   node --env-file=.env src/server.js [--port 4101]
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
//   POST /party {on}              -> party mode on/off for every connected headset
//   GET /projects · POST /projects {name,path,dev?,setup?,url?,kind?,init?} (register a repo)
//   POST /projects/new {name, template: empty|static} · DELETE /projects/:id (unregister)
//   GET /previews/<task>-desktop.png|-mobile.png -> screenshots of a task in review
// Goals/tasks take projectId. Every task runs in its own git worktree; approve =
// squash-merge into the project's main branch (agents never push).
//   GET  /loft/:roomId[/boards], POST /loft -> board layout store

import crypto from "node:crypto";
import fs from "node:fs";
import http from "node:http";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { draftRole, planGoal, priceOf, runTask } from "./agents.js";
import { blankLoft, loadLoft, saveLoft, validateLoft } from "./loftStore.js";
import { createOrchestrator, HttpError } from "./orchestrator.js";
import { createPreviews } from "./previews.js";
import { createProjects } from "./projects.js";

const here = path.dirname(fileURLToPath(import.meta.url));
const argPort = process.argv.indexOf("--port");
const PORT = Number(argPort > -1 ? process.argv[argPort + 1] : (process.env.PORT ?? 4101));
const HOST = process.env.HOST ?? "127.0.0.1";
const TOKEN = process.env.LOFT_TOKEN ?? "";
const WORKSPACE = path.resolve(process.env.WORKSPACE_DIR ?? path.join(here, "../workspace"));
const MAX_BODY = 1_000_000;

if (!["127.0.0.1", "localhost", "::1"].includes(HOST) && !TOKEN) {
  console.error(`Refusing to listen on ${HOST} without LOFT_TOKEN: agents can run Bash in ${WORKSPACE}.`);
  process.exit(1);
}
fs.mkdirSync(WORKSPACE, { recursive: true });

const ROLES_FILE = path.join(here, "../data/roles.json");
const savedRoles = (() => {
  try {
    return JSON.parse(fs.readFileSync(ROLES_FILE, "utf8"));
  } catch {
    return [];
  }
})();
const DATA = path.join(here, "../data");
const PREVIEW_DIR = path.join(DATA, "previews");
const projects = createProjects({
  dataDir: DATA,
  projectsDir: path.resolve(process.env.PROJECTS_DIR ?? path.join(here, "../projects")),
  defaultPath: WORKSPACE, // WORKSPACE_DIR becomes the first project (git init if needed)
});
await projects.load();
const previews = createPreviews({ dir: PREVIEW_DIR });
for (const sig of ["SIGINT", "SIGTERM"]) process.on(sig, () => previews.close().finally(() => process.exit(0)));

const loft = createOrchestrator({
  runTask, planGoal, draftRole, priceOf, workspace: WORKSPACE, roles: savedRoles, projects, previews,
  saveRoles: (list) => {
    fs.mkdirSync(path.dirname(ROLES_FILE), { recursive: true });
    fs.writeFileSync(ROLES_FILE, JSON.stringify(list, null, 2));
  },
});
const storeOpts = { localOnly: !process.env.SUPABASE_URL };

const CORS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET,POST,DELETE,OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type, Authorization",
};

const readBody = (req) =>
  new Promise((resolve, reject) => {
    const chunks = [];
    let size = 0;
    req.on("data", (c) => {
      size += c.length;
      if (size > MAX_BODY) {
        reject(new HttpError(413, "body too large"));
        req.destroy();
      } else chunks.push(c);
    });
    req.on("end", () => {
      try {
        resolve(chunks.length ? JSON.parse(Buffer.concat(chunks).toString("utf8")) : {});
      } catch {
        reject(new HttpError(400, "invalid JSON body"));
      }
    });
    req.on("error", reject);
  });

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
  if (!authorized(req)) return send(res, 401, { ok: false, error: "missing or wrong LOFT_TOKEN" });

  if (m === "GET" && p === "/events") return openEvents(req, res);
  if (m === "GET" && p === "/state") return send(res, 200, loft.snapshot());
  if (m === "POST" && p === "/agents") return send(res, 201, { ok: true, agent: loft.spawn(await readBody(req)) });
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
  console.log(`llm: ${process.env.LLM_BASE_URL || "https://openrouter.ai/api/v1"}  default model: ${loft.snapshot().defaultModel}${process.env.LLM_API_KEY ? "" : "  (LLM_API_KEY missing!)"}`);
});
