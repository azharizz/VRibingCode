// Round 2 over real HTTP: boards, notes, files (security), chat, restart persistence,
// spawn by description, clickable preview sessions (real Chromium). Fake LLM, no network.
import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import fs from "node:fs";
import http from "node:http";
import os from "node:os";
import path from "node:path";
import { after, before, test } from "node:test";
import { fakeLlm } from "./helpers.js";

const here = path.dirname(new URL(import.meta.url).pathname);
const TOKEN = "k".repeat(24);
const auth = { Authorization: `Bearer ${TOKEN}` };
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "loft-api-"));
const ws = path.join(tmp, "ws");
let llm, page, base, child, chatN = 0;

async function startServer() {
  const port = 43000 + Math.floor(Math.random() * 2000);
  const c = spawn(process.execPath, [path.join(here, "../src/server.js"), "--port", String(port)], {
    env: {
      PATH: process.env.PATH, HOME: tmp, PLAYWRIGHT_BROWSERS_PATH: process.env.PLAYWRIGHT_BROWSERS_PATH ?? path.join(os.homedir(), "Library/Caches/ms-playwright"), AGENT_API_KEY: "t", AGENT_BASE_URL: llm.url, AGENT_MODEL: "test/m", LOFT_TOKEN: TOKEN,
      LOFT_DATA_DIR: path.join(tmp, "data"), WORKSPACE_DIR: ws, PROJECTS_DIR: path.join(tmp, "projects"), LOFT_SUGGESTIONS: "0",
    },
    stdio: "ignore",
  });
  const b = `http://127.0.0.1:${port}`;
  for (let i = 0; i < 100; i++) {
    if (await fetch(`${b}/health`).then((r) => r.ok, () => false)) return { c, b };
    await new Promise((r) => setTimeout(r, 100));
  }
  throw new Error("server did not start");
}
const stop = (c) => new Promise((r) => { c.once("exit", r); c.kill("SIGTERM"); });

before(async () => {
  fs.mkdirSync(path.join(ws, "src"), { recursive: true });
  fs.writeFileSync(path.join(ws, "src/app.js"), "console.log('hi')\n");
  fs.writeFileSync(path.join(ws, ".env"), "SECRET=1");
  fs.writeFileSync(path.join(tmp, "outside.txt"), "TOP SECRET");
  fs.symlinkSync(path.join(tmp, "outside.txt"), path.join(ws, "link.txt"));
  llm = await fakeLlm((req) => {
    const sys = req.messages[0].content;
    const user = req.messages.at(-1).content;
    if (sys.includes("design roles")) return '{"title":"Data Analyst","instructions":"Analyse data and report.","tools":["read_file","list_files","run_command"]}';
    if (sys.includes("spoken command")) return user.includes("analyst") ? '{"action":"spawn","description":"data analyst"}' : '{"action":"none","reply":"ok"}';
    if (sys.includes("CEO is talking to you")) return `Hello boss #${++chatN} (${req.messages.length - 2} before)`;
    return "{}";
  });
  // a tiny page to click on
  page = http.createServer((req, res) => {
    res.writeHead(200, { "content-type": "text/html" });
    res.end(`<body style="margin:0"><button style="width:400px;height:300px;font-size:60px" onclick="document.body.style.background='red';this.textContent='CLICKED'">go</button></body>`);
  });
  await new Promise((r) => page.listen(0, "127.0.0.1", r));
  ({ c: child, b: base } = await startServer());
});
after(async () => {
  child?.kill();
  page?.close();
  llm?.close();
});

const call = (m, p, body, headers = auth) =>
  fetch(`${base}${p}`, { method: m, headers: { "content-type": "application/json", ...headers }, body: body === undefined || m === "GET" ? undefined : JSON.stringify(body) });

test("every Round 2 endpoint needs the token", async () => {
  for (const [m, p] of [["GET", "/boards"], ["PUT", "/boards"], ["GET", "/notes"], ["POST", "/notes"], ["PATCH", "/notes/n1"], ["DELETE", "/notes/n1"],
    ["GET", "/projects/workspace/tree"], ["GET", "/projects/workspace/file?path=src/app.js"], ["GET", "/tasks/t1/diff"], ["POST", "/agents/x/chat"],
    ["GET", "/suggestions"], ["POST", "/suggestions/s1/accept"], ["POST", "/preview/sessions"], ["GET", "/preview/sessions"],
    ["GET", "/preview/sessions/abc/frame"], ["POST", "/preview/sessions/abc/input"], ["DELETE", "/preview/sessions/abc"]]) {
    assert.equal((await call(m, p, {}, {})).status, 401, `${m} ${p}`);
    assert.equal((await call(m, p, {}, { Authorization: "Bearer wrong" })).status, 401, `${m} ${p} wrong token`);
  }
});

test("boards and notes over HTTP", async () => {
  const layout = [{ id: "b1", type: "review", pos: [0, 1.5, 2], rot: [0, 180, 0], size: [1, 1], args: {} }];
  assert.equal((await call("PUT", "/boards?scene=beach", layout)).status, 200);
  assert.deepEqual((await (await call("GET", "/boards?scene=beach")).json()).boards, layout);
  assert.deepEqual((await (await call("GET", "/boards?scene=city")).json()).boards, []);
  assert.equal((await call("PUT", "/boards?scene=beach", [{ id: "x", type: "nope", pos: [0, 0, 0] }])).status, 400);
  assert.equal((await call("GET", "/boards?scene=../x")).status, 400);
  const n = (await (await call("POST", "/notes", { title: "A", text: "b" })).json()).note;
  assert.equal((await (await call("PATCH", `/notes/${n.id}`, { text: "c" })).json()).note.text, "c");
  assert.equal((await (await call("GET", "/notes")).json()).notes.length, 1);
  assert.equal((await call("DELETE", `/notes/${n.id}`)).status, 200);
  assert.equal((await call("DELETE", `/notes/${n.id}`)).status, 404);
});

test("files: browse works, traversal and secrets are refused over HTTP", async () => {
  const tr = await (await call("GET", "/projects/workspace/tree")).json();
  assert.ok(tr.entries.some((e) => e.name === "src"));
  assert.ok(!tr.entries.some((e) => [".env", ".git", "link.txt"].includes(e.name)));
  assert.equal((await (await call("GET", "/projects/workspace/file?path=src/app.js")).json()).content, "console.log('hi')\n");
  for (const evil of ["../outside.txt", "..%2Foutside.txt", "%2e%2e/outside.txt", "src/../../outside.txt", "/etc/passwd", "%2Fetc%2Fpasswd", "link.txt", ".env", ".git/HEAD", "src/%00", "....//outside.txt/.."]) {
    const f = await call("GET", `/projects/workspace/file?path=${evil}`);
    assert.ok([400, 403, 404].includes(f.status), `file ${evil} -> ${f.status}`);
    assert.ok(!(await f.text()).includes("TOP SECRET"), `${evil} leaked`);
    const t = await call("GET", `/projects/workspace/tree?path=${evil}`);
    assert.ok([400, 403, 404].includes(t.status), `tree ${evil} -> ${t.status}`);
  }
  assert.equal((await call("GET", "/projects/ghost/tree")).status, 404);
  assert.equal((await call("GET", "/projects/workspace/tree?taskId=t99")).status, 404);
  assert.equal((await call("GET", "/tasks/t99/diff")).status, 404);
});

test("spawn by description (the voice bug): server writes the role; voice intent is a valid spawn", async () => {
  const v = await (await call("POST", "/voice/text", { text: "spawn new agent as data analyst" })).json();
  assert.equal(v.intent.action, "spawn");
  assert.equal(v.intent.role.title, "Data Analyst");
  // The headset may post the whole intent (with role) or only the description.
  const a = (await (await call("POST", "/agents", { name: "Dana", description: "data analyst", skin: "panda" })).json()).agent;
  assert.equal(a.role.title, "Data Analyst");
  assert.deepEqual(a.role.tools, ["read_file", "list_files", "run_command"]);
  assert.equal(a.skin, "panda");
  const b = (await (await call("POST", "/agents", { name: "Dan", role: v.intent.role })).json()).agent;
  assert.equal(b.role.title, "Data Analyst");
  assert.equal((await call("POST", "/agents", { description: "   " })).status, 201, "blank description falls back to the default role");
});

test("chat has memory, and the team survives a server restart", async () => {
  const state = await (await call("GET", "/state")).json();
  const dana = state.agents.find((a) => a.name === "Dana");
  const r1 = await (await call("POST", `/agents/${dana.id}/chat`, { text: "hello" })).json();
  assert.match(r1.reply, /Hello boss #\d+ \(0 before\)/);
  const r2 = await (await call("POST", `/agents/${dana.id}/chat`, { text: "again" })).json();
  assert.match(r2.reply, /\(2 before\)/);
  assert.equal((await call("POST", `/agents/${dana.id}/chat`, { text: " " })).status, 400);
  assert.equal((await call("POST", "/agents/ghost/chat", { text: "hi" })).status, 404);
  await new Promise((r) => setTimeout(r, 700)); // state is saved shortly after a change
  await stop(child);
  ({ c: child, b: base } = await startServer());
  const after = await (await call("GET", "/state")).json();
  assert.ok(after.agents.some((a) => a.id === dana.id && a.name === "Dana" && a.role.title === "Data Analyst"), "agent is back");
  assert.match((await (await call("POST", `/agents/${dana.id}/chat`, { text: "remember me?" })).json()).reply, /\(4 before\)/, "chat memory is back");
  assert.equal((await (await call("GET", "/boards?scene=beach")).json()).boards.length, 1, "boards persisted");
});

test("clickable preview: open, frame, click changes the page, cap 3, delete", async () => {
  const url = `http://127.0.0.1:${page.address().port}/`;
  const open = await call("POST", "/preview/sessions", { url, width: 800, height: 500 });
  assert.equal(open.status, 201);
  const s = await open.json();
  assert.deepEqual([s.width, s.height], [800, 500]);
  const frame = async () => Buffer.from(await (await call("GET", `/preview/sessions/${s.id}/frame`)).arrayBuffer());
  const before = await frame();
  assert.deepEqual([...before.subarray(0, 3)], [0xff, 0xd8, 0xff], "JPEG");
  assert.equal((await call("POST", `/preview/sessions/${s.id}/input`, { type: "click", x: 100, y: 100 })).status, 200);
  await new Promise((r) => setTimeout(r, 200));
  assert.notDeepEqual(await frame(), before, "the click changed the page");
  for (const bad of [{ type: "click", x: 9999, y: 1 }, { type: "click", x: "a", y: 1 }, { type: "key", key: "a;rm -rf" }, { type: "text", text: "" }, { type: "explode" }, {}]) {
    assert.equal((await call("POST", `/preview/sessions/${s.id}/input`, bad)).status, 400, JSON.stringify(bad));
  }
  assert.equal((await call("POST", `/preview/sessions/${s.id}/input`, { type: "scroll", dy: 100 })).status, 200);
  assert.equal((await call("POST", `/preview/sessions/${s.id}/input`, { type: "text", text: "abc" })).status, 200);
  assert.equal((await call("POST", `/preview/sessions/${s.id}/input`, { type: "key", key: "Enter" })).status, 200);
  const more = [];
  for (let i = 0; i < 2; i++) more.push((await (await call("POST", "/preview/sessions", { url })).json()).id);
  assert.equal((await call("POST", "/preview/sessions", { url })).status, 429, "cap of 3");
  assert.equal((await (await call("GET", "/preview/sessions")).json()).sessions.length, 3);
  for (const id of [s.id, ...more]) assert.equal((await call("DELETE", `/preview/sessions/${id}`)).status, 200);
  assert.equal((await call("GET", `/preview/sessions/${s.id}/frame`)).status, 404);
});

test("preview sessions refuse metadata, file URLs and unknown tasks", async () => {
  for (const [url, status] of [["http://169.254.169.254/latest/meta-data/", 403], ["http://metadata.google.internal/", 403], ["file:///etc/passwd", 400], ["javascript:1", 400], ["gopher://x", 400]]) {
    assert.equal((await call("POST", "/preview/sessions", { url })).status, status, url);
  }
  assert.equal((await call("POST", "/preview/sessions", {})).status, 400);
  assert.equal((await call("POST", "/preview/sessions", { taskId: "t404" })).status, 404);
  assert.equal((await (await call("GET", "/preview/sessions")).json()).sessions.length, 0);
});
