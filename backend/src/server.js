// VIRTUAL LOFT — local HTTP bridge (Unity <-> backend).
// Zero new dependencies (node:http only). Run on the dev Mac next to Unity:
//
//   node --env-file=.env src/server.js [--port 4101]
//
// Endpoints (all JSON, CORS open for localhost/editor):
//   GET  /health              -> { ok:true, model, gateway }
//   GET  /loft/:roomId        -> loft JSON {roomId, boards, agents, version, updatedAt}
//   POST /loft                -> save loft JSON (bumps version), returns saved
//   POST /agent/run {role,task} -> runAgent() agentic loop, returns {role, text}
// Unity side: quest_loft/Assets/Boards/LoftClient.cs talks to this server.

import http from "node:http";
import { loadLoft, saveLoft, validateLoft, blankLoft } from "./loftStore.js";
import { runAgent, AGENT_ROLES } from "./agents.js";

const PORT = Number(process.argv.includes("--port") ? process.argv[process.argv.indexOf("--port") + 1] : 4101);

const readBody = (req) =>
  new Promise((resolve, reject) => {
    const chunks = [];
    req.on("data", (c) => chunks.push(c));
    req.on("end", () => {
      try {
        resolve(chunks.length ? JSON.parse(Buffer.concat(chunks).toString("utf8")) : {});
      } catch (e) {
        reject(e);
      }
    });
    req.on("error", reject);
  });

const send = (res, code, obj) => {
  res.writeHead(code, {
    "Content-Type": "application/json",
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Methods": "GET,POST,OPTIONS",
    "Access-Control-Allow-Headers": "Content-Type",
  });
  res.end(JSON.stringify(obj));
};

const server = http.createServer(async (req, res) => {
  const path = (req.url ?? "").split("?")[0];
  try {
    if (req.method === "OPTIONS") {
      send(res, 204, {});
      return;
    }
    if (req.method === "GET" && path === "/health") {
      send(res, 200, {
        ok: true,
        model: process.env.ANTHROPIC_MODEL ?? "muse-spark-1.3-contributor",
        gateway: process.env.ANTHROPIC_BASE_URL ?? "(unset)",
      });
      return;
    }
    if (req.method === "GET" && path.startsWith("/loft/")) {
      const rest = decodeURIComponent(path.slice("/loft/".length));
      const boardsOnly = rest.endsWith("/boards");
      const roomId = boardsOnly ? rest.slice(0, -"/boards".length) : rest;
      const loft = await loadLoft(roomId, { localOnly: !process.env.SUPABASE_URL });
      if (!loft) {
        send(res, 404, { ok: false, error: `no loft '${roomId}'` });
        return;
      }
      send(res, 200, boardsOnly ? { boards: loft.boards } : { ok: true, loft });
      return;
    }
    if (req.method === "POST" && path === "/loft") {
      // Accepts a full loft OR a Unity partial {roomId, snapshot:{boards:[...]}}
      // (BoardManager.Save() output nested under snapshot). Merges onto the
      // stored loft so agents/version history survive a board-only push.
      const body = await readBody(req);
      const roomId = body.roomId ?? body?.loft?.roomId ?? "focus-loft";
      const incomingBoards = body.boards ?? body?.snapshot?.boards ?? body?.loft?.boards;
      const incomingAgents = body.agents ?? body?.loft?.agents;
      const current =
        (await loadLoft(roomId, { localOnly: !process.env.SUPABASE_URL })) ?? blankLoft(roomId);
      if (incomingBoards !== undefined) current.boards = incomingBoards;
      if (incomingAgents !== undefined) current.agents = incomingAgents;
      const saved = await saveLoft(validateLoft(current), { localOnly: !process.env.SUPABASE_URL });
      send(res, 200, { ok: true, loft: saved });
      return;
    }
    if (req.method === "POST" && path === "/agent/run") {
      const { role, task } = await readBody(req);
      if (!AGENT_ROLES.includes(role)) {
        send(res, 400, { ok: false, error: `unknown role '${role}' (want scout|builder|keeper)` });
        return;
      }
      const text = await runAgent(role, String(task ?? ""));
      send(res, 200, { ok: true, role, text });
      return;
    }
    send(res, 404, { ok: false, error: `no route ${req.method} ${path}` });
  } catch (err) {
    send(res, 500, { ok: false, error: String((err && err.message) || err).slice(0, 300) });
  }
});

server.listen(PORT, "127.0.0.1", () => console.log(`loft server: http://127.0.0.1:${PORT} (Unity -> backend)`));
