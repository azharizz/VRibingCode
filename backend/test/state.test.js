// Round 2 logic, offline: persistence, chat memory, suggestions, boards/notes stores,
// file/diff views (path traversal!), preview URL guard, spawn-by-description voice.
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { PRESET_ROLES } from "../src/agents.js";
import { parseDiff, readFile, resolveInside, taskDiff, tree } from "../src/files.js";
import { createOrchestrator } from "../src/orchestrator.js";
import { checkUrl } from "../src/preview-sessions.js";
import { createProjects } from "../src/projects.js";
import { createBoards, createNotes, validateBoards } from "../src/stores.js";
import { extractJson } from "../src/llm.js";
import { createVoice } from "../src/voice.js";

const tmp = () => fs.mkdtempSync(path.join(os.tmpdir(), "loft-r2-"));
const tick = (ms = 0) => new Promise((r) => setTimeout(r, ms));
const done = { runTask: async () => ({ text: "finished: ok" }), planGoal: async () => [] };

test("persistence: team, tasks and chat memory survive a restart; unfinished work is marked stopped", async () => {
  let saved;
  const a = createOrchestrator({ ...done, saveDelayMs: 1, saveState: (s) => (saved = JSON.parse(JSON.stringify(s))), chat: async () => "hi boss" });
  const x = a.spawn({ name: "Ada", role: { title: "Data Analyst", instructions: "analyse", tools: ["read_file"] }, skin: "panda" });
  const t = a.assign(x.id, "first job");
  await tick(30);
  await a.chat(x.id, "hello");
  const hang = createOrchestrator({ ...done, runTask: () => new Promise(() => {}), saveDelayMs: 1, saveState: (s) => (saved = JSON.parse(JSON.stringify(s))) });
  const h = hang.spawn({ name: "Slow" });
  hang.assign(h.id, "never finishes");
  await tick(30);
  assert.equal(saved.tasks.find((k) => k.title === "never finishes").status, "running");
  const hangState = saved;

  await tick(30);
  const b = createOrchestrator({ ...done, state: hangState });
  const snap = b.snapshot();
  assert.equal(snap.agents.length, 4, "defaults 3 + Slow, no extra defaults added");
  const lost = snap.tasks.find((k) => k.title === "never finishes");
  assert.equal(lost.status, "stopped");
  assert.match(lost.result, /restart/);
  assert.equal(snap.agents.find((k) => k.id === h.id).state, "idle");
  const c = b.spawn({});
  assert.ok(!snap.agents.some((k) => k.id === c.id), "ids keep counting after restore");
  assert.notEqual(t.id, undefined);
});

test("persistence round trip incl. chat memory", async () => {
  let saved;
  const a = createOrchestrator({ ...done, saveDelayMs: 1, saveState: (s) => (saved = JSON.parse(JSON.stringify(s))), chat: async ({ history }) => `I remember ${history.length} messages` });
  const x = a.spawn({ name: "Ada" });
  assert.equal((await a.chat(x.id, "one")).reply, "I remember 0 messages");
  assert.equal((await a.chat(x.id, "two")).reply, "I remember 2 messages");
  await tick(30);
  const b = createOrchestrator({ ...done, state: saved, chat: async ({ history }) => `I remember ${history.length} messages` });
  assert.equal((await b.chat(x.id, "three")).reply, "I remember 4 messages", "memory restored");
});

test("chat: short, emits say, validates", async () => {
  const seen = [];
  const loft = createOrchestrator({ ...done, chat: async () => "  Fine, boss.  " });
  loft.events.on("event", (e) => seen.push(e));
  const id = loft.snapshot().agents[0].id;
  assert.deepEqual(await loft.chat(id, "how are you"), { reply: "Fine, boss." });
  assert.deepEqual(seen.find((e) => e.type === "say"), { type: "say", agentId: id, text: "Fine, boss." });
  await assert.rejects(loft.chat(id, "  "), { status: 400 });
  await assert.rejects(loft.chat("ghost", "hi"), { status: 404 });
  await assert.rejects(createOrchestrator(done).chat(id, "hi"), { status: 501 });
  const empty = createOrchestrator({ ...done, chat: async () => "" });
  await assert.rejects(empty.chat(empty.snapshot().agents[0].id, "hi"), { status: 502 });
});

test("suggestions: at most one per finished task, never acts alone, accept queues, dismiss drops", async () => {
  let calls = 0;
  const seen = [];
  const loft = createOrchestrator({ ...done, suggest: async () => (calls++, { title: "Add tests for it", reason: "no coverage" }) });
  loft.events.on("event", (e) => seen.push(e));
  const id = loft.snapshot().agents[0].id;
  loft.assign(id, "build it");
  await tick(30);
  assert.equal(calls, 1);
  const sug = seen.find((e) => e.type === "suggestion");
  assert.deepEqual([sug.agentId, sug.title, sug.reason], [id, "Add tests for it", "no coverage"]);
  assert.equal(loft.snapshot().tasks.length, 1, "nothing queued by itself");
  const task = loft.acceptSuggestion(sug.id);
  assert.equal(task.title, "Add tests for it");
  assert.equal(loft.snapshot().suggestions.length, 0);
  await tick(30);
  loft.dismissSuggestion(loft.snapshot().suggestions[0].id); // the accepted task also finished and raised one
  assert.equal(loft.snapshot().suggestions.length, 0);
  assert.throws(() => loft.dismissSuggestion("s999"), { status: 404 });

  process.env.LOFT_SUGGESTIONS = "0";
  try {
    const off = createOrchestrator({ ...done, suggest: async () => assert.fail("disabled") });
    off.assign(off.snapshot().agents[0].id, "x");
    await tick(30);
  } finally {
    delete process.env.LOFT_SUGGESTIONS;
  }
  const broken = createOrchestrator({ ...done, suggest: async () => { throw new Error("llm down"); } });
  broken.assign(broken.snapshot().agents[0].id, "x");
  await tick(30);
  assert.equal(broken.snapshot().tasks[0].status, "review", "a failing suggestion never disturbs the work");
});

test("boards: validated layout per scene; notes: CRUD with limits", () => {
  const dir = tmp();
  const boards = createBoards(path.join(dir, "boards.json"));
  assert.deepEqual(boards.get("beach"), { scene: "beach", boards: [] });
  const layout = [{ id: "b1", type: "kanban", pos: [0, 1.5, 2], rot: [0, 180, 0], size: [1.2, 0.8], args: { project: "shop" } }, { id: "b2", type: "log", pos: { x: 1, y: 1, z: 1 }, rot: [0, 0, 0, 1], size: [1, 1] }];
  assert.equal(boards.put("beach", layout).boards.length, 2);
  assert.equal(createBoards(path.join(dir, "boards.json")).get("beach").boards[0].args.project, "shop", "persisted on disk");
  assert.deepEqual(boards.get("city").boards, [], "per scene");
  for (const bad of [{ id: "x", type: "banana", pos: [0, 0, 0] }, { id: "x", type: "log", pos: [0, "a", 0] }, { id: "../x", type: "log", pos: [0, 0, 0] }, { id: "x", type: "log", pos: [0, 0, NaN] }, "str", { id: "x", type: "log", pos: [0, 0, 0], args: [] }]) {
    assert.throws(() => validateBoards([bad]), { status: 400 }, JSON.stringify(bad));
  }
  assert.throws(() => validateBoards([layout[0], layout[0]]), /duplicate/);
  assert.throws(() => validateBoards("no"), { status: 400 });
  assert.throws(() => boards.get("../etc"), { status: 400 });

  let events = 0;
  const notes = createNotes(path.join(dir, "notes.json"), { emit: () => events++ });
  const n = notes.create({ title: "Idea", text: "ship it" });
  assert.equal(n.id, "n1");
  assert.equal(notes.update("n1", { text: "ship it Friday" }).text, "ship it Friday");
  assert.equal(notes.update("n1", {}).title, "Idea");
  assert.throws(() => notes.create({}), { status: 400 });
  assert.throws(() => notes.create({ text: 5 }), { status: 400 });
  assert.throws(() => notes.update("zz", { text: "x" }), { status: 404 });
  assert.equal(createNotes(path.join(dir, "notes.json")).create({ text: "again" }).id, "n2", "ids continue after reload");
  notes.remove("n1");
  assert.deepEqual(notes.list().map((x) => x.id), ["n2"]);
  assert.throws(() => notes.remove("n1"), { status: 404 });
  assert.ok(events >= 4);
});

test("files: tree and file are locked inside the project (traversal, absolute, symlink, secrets)", async () => {
  const dir = tmp();
  const root = path.join(dir, "proj");
  fs.mkdirSync(path.join(root, "src"), { recursive: true });
  fs.mkdirSync(path.join(root, ".git"));
  fs.writeFileSync(path.join(root, "src/a.js"), "console.log(1)\n");
  fs.writeFileSync(path.join(root, ".env"), "SECRET=1");
  fs.writeFileSync(path.join(root, "bin.dat"), Buffer.from([1, 0, 2]));
  fs.writeFileSync(path.join(dir, "outside.txt"), "TOP SECRET");
  fs.symlinkSync(path.join(dir, "outside.txt"), path.join(root, "link.txt"));
  fs.symlinkSync(dir, path.join(root, "linkdir"));

  const t = await tree(root, "");
  assert.deepEqual(t.entries.map((e) => e.name), ["src", "bin.dat"], "no .git, no .env, no symlinks");
  assert.equal((await tree(root, "src")).entries[0].name, "a.js");
  assert.equal((await readFile(root, "src/a.js")).content, "console.log(1)\n");
  assert.equal((await readFile(root, "bin.dat")).binary, true);
  for (const evil of ["../outside.txt", "src/../../outside.txt", "/etc/passwd", "..", "src/..\\..\\outside.txt", "link.txt", "linkdir/outside.txt", ".env", ".git/config", "src/\0x"]) {
    await assert.rejects(readFile(root, evil), (e) => [400, 403].includes(e.status), `file ${JSON.stringify(evil)}`);
    await assert.rejects(tree(root, evil), (e) => [400, 403].includes(e.status), `tree ${JSON.stringify(evil)}`);
  }
  await assert.rejects(readFile(root, "nope.txt"), { status: 404 });
  await assert.rejects(readFile(root, ""), { status: 400 });
  await assert.rejects(readFile(root, "src"), { status: 400 });
  await assert.rejects(resolveInside(root, "linkdir"), { status: 403 });
});

test("diff: parsed shape, from a real task worktree and after merge", async () => {
  const dir = tmp();
  const projects = createProjects({ dataDir: path.join(dir, "data"), projectsDir: path.join(dir, "p"), defaultPath: null });
  const p = await projects.create({ name: "Diff Test", template: "static" });
  const { worktree, branch } = await projects.prepare(p, { taskId: "t1", agent: "Fox" });
  fs.appendFileSync(path.join(worktree, "style.css"), "h1{color:red}\n");
  fs.writeFileSync(path.join(worktree, "new.txt"), "hello\n");
  await projects.finish(p, { worktree }, "work");
  const task = { id: "t1", worktree, branch };
  const d = await taskDiff(p, task);
  assert.deepEqual(d.files.map((f) => f.path).sort(), ["new.txt", "style.css"]);
  const css = d.files.find((f) => f.path === "style.css");
  assert.equal(css.added, 1);
  assert.equal(css.removed, 0);
  assert.match(css.hunks[0].header, /^@@/);
  assert.deepEqual(css.hunks[0].lines.at(-1), { type: "add", text: "h1{color:red}" });
  assert.ok(css.hunks[0].lines.some((l) => l.type === "ctx"));
  const res = await projects.merge(p, task, "merge");
  const after = await taskDiff(p, { id: "t1", merged: res.sha });
  assert.deepEqual(after.files.map((f) => f.path).sort(), ["new.txt", "style.css"], "still viewable after approve");
  await assert.rejects(taskDiff(p, { id: "t2" }), { status: 404 });
  assert.deepEqual(parseDiff(""), { files: [] });
  assert.equal(execFileSync("git", ["-C", p.path, "log", "--oneline"]).toString().split("\n").filter(Boolean).length, 2);
});

test("preview URL guard: http(s) only, metadata and link-local blocked, also via DNS", async () => {
  const ok = (host) => async () => [{ address: host }];
  assert.equal((await checkUrl("http://127.0.0.1:5173/", { lookup: ok("127.0.0.1") })).port, "5173");
  assert.equal((await checkUrl("https://example.com/a", { lookup: ok("93.184.216.34") })).hostname, "example.com");
  for (const [u, status] of [
    ["file:///etc/passwd", 400], ["ftp://x/y", 400], ["javascript:alert(1)", 400], ["not a url", 400], ["http://user:pw@example.com/", 400],
    ["http://169.254.169.254/latest/meta-data/", 403], ["http://metadata.google.internal/computeMetadata/v1/", 403],
    ["http://[fd00:ec2::254]/", 403], ["http://[::ffff:169.254.169.254]/", 403], ["http://100.100.100.200/", 403], ["http://169.254.1.5/", 403],
  ]) await assert.rejects(checkUrl(u, { lookup: ok("93.184.216.34") }), { status }, u);
  await assert.rejects(checkUrl("http://evil.example/", { lookup: ok("169.254.169.254") }), { status: 403 }, "DNS name pointing at metadata");
});

test("voice: spawn with a new role (data analyst) gets a drafted role; chat and board actions", async () => {
  const agents = [{ id: "scout-1", name: "Scout", skin: "owl", state: "idle", taskId: null, role: { title: "Researcher", instructions: "x", tools: [] } }];
  const mk = (reply, draft) => createVoice({ snapshot: () => ({ agents, projects: [], roles: PRESET_ROLES, tasks: [] }), ask: async () => reply, extract: extractJson, draft });
  const draft = async (d) => ({ title: "Data Analyst", instructions: `analyse: ${d}`, tools: ["read_file", "run_command"] });
  const r = await mk('{"action":"spawn","roleId":"data analyst"}', draft).handleText({ text: "spawn new agent as data analyst" });
  assert.equal(r.intent.action, "spawn");
  assert.equal(r.intent.description, "data analyst", "an unknown role id is kept as the need");
  assert.equal(r.intent.role.title, "Data Analyst");
  assert.deepEqual(r.intent.writes, ["run_command"]);
  assert.match(r.summary, /Data Analyst/);
  assert.equal(r.needsConfirm, true);
  const noDraft = await mk('{"action":"spawn","description":"data analyst"}', async () => { throw new Error("down"); }).handleText({ text: "x" });
  assert.equal(noDraft.intent.action, "spawn", "draft failure still yields a spawn the server can finish");
  assert.equal(noDraft.intent.role, undefined);
  const known = await mk('{"action":"spawn","roleId":"builder"}', async () => assert.fail("role exists")).handleText({ text: "x" });
  assert.equal(known.intent.roleId, "builder");

  const chat = await mk('{"action":"chat","agentId":"owl","text":"hello there"}').handleText({ text: "hi owl" });
  assert.deepEqual(chat.intent, { action: "chat", agentId: "scout-1", text: "hello there" });
  assert.deepEqual([chat.needsConfirm, chat.risky], [false, false]);
  const pointed = await mk('{"action":"chat","text":"how is it going"}').handleText({ text: "how is it going", agentId: "scout-1" });
  assert.equal(pointed.intent.agentId, "scout-1");
  assert.equal((await mk('{"action":"chat","text":"hi"}').handleText({ text: "hi" })).intent.action, "none", "nobody to talk to");
  const board = await mk('{"action":"board","op":"open","type":"Kanban"}').handleText({ text: "show tasks" });
  assert.deepEqual([board.intent, board.needsConfirm], [{ action: "board", op: "open", type: "kanban" }, false]);
  assert.deepEqual((await mk('{"action":"board","op":"close","type":"all"}').handleText({ text: "x" })).intent, { action: "board", op: "close", type: "all" });
  assert.equal((await mk('{"action":"board","op":"open","type":"hologram"}').handleText({ text: "x" })).intent.action, "none");
  assert.equal((await mk('{"action":"board","op":"explode","type":"log"}').handleText({ text: "x" })).intent.action, "none");
});
