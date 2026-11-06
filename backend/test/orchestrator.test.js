// Orchestrator + store checks. Offline: runner/planner are injected, so
// these test OUR logic (roster, free model/role/skin, queues, parallelism,
// review flow, role library, usage), not the AI.
import assert from "node:assert/strict";
import { mkdtemp } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { parsePlan, SKINS } from "../src/agents.js";
import { loadLoft, saveLoft, validateLoft } from "../src/loftStore.js";
import { createOrchestrator } from "../src/orchestrator.js";

function gatedRunner() {
  const calls = [];
  const runTask = ({ agent, title, abortController, onProgress }) =>
    new Promise((resolve, reject) => {
      calls.push({ agent, title, resolve, reject });
      onProgress(`edit_file ${title}.ts`);
      abortController.signal.addEventListener("abort", () => reject(new Error("aborted")));
    });
  return { calls, runTask };
}
const tick = () => new Promise((r) => setImmediate(r));
const make = (opts = {}) => createOrchestrator({ runTask: async () => ({ text: "" }), planGoal: async () => [], ...opts });

test("3 default agents, cap 20, free model/role/animal per agent", () => {
  const loft = make();
  const snap = loft.snapshot();
  assert.deepEqual(snap.agents.map((a) => [a.name, a.skin, a.role.title]), [["Scout", "owl", "Researcher"], ["Builder", "fox", "Coder"], ["Keeper", "cat", "Reviewer"]]);
  const custom = loft.spawn({ name: "Ada", model: "openai/gpt-5-mini:online", skin: "panda", role: { title: "A11y Checker", instructions: "check", tools: ["read_file", "rm_rf"] } });
  assert.equal(custom.model, "openai/gpt-5-mini:online");
  assert.equal(custom.skin, "panda");
  assert.deepEqual(custom.role.tools, ["read_file"], "unknown tools dropped");
  // random animals: unused ones first
  const skins = [];
  for (let i = 0; i < 4; i++) skins.push(loft.spawn({ roleId: "builder", skin: "random" }).skin);
  assert.equal(new Set([...skins, "owl", "fox", "cat", "panda"]).size, 8, "no repeats while unused animals remain");
  assert.ok(skins.every((s) => SKINS.includes(s)));
  for (let i = 0; i < 12; i++) loft.spawn({});
  assert.equal(loft.snapshot().agents.length, 20);
  assert.throws(() => loft.spawn({}), /limit reached \(20\)/);
  loft.remove(custom.id);
  assert.throws(() => loft.spawn({ model: "bad model id!" }), /invalid model id/);
  assert.throws(() => loft.spawn({ roleId: "wizard" }), /unknown role/);
  assert.throws(() => loft.spawn({ role: { title: "x" } }), /needs instructions/);
});

test("role library: save (unique ids), presets protected, persisted via callback", async () => {
  let persisted = null;
  const loft = make({ saveRoles: (l) => (persisted = l), draftRole: async () => ({ title: "Tester", instructions: "run tests", tools: ["read_file", "run_command"] }) });
  const draft = await loft.draft("someone who runs the test suite");
  assert.deepEqual(draft.writes, ["run_command"], "write tools flagged for the CEO");
  const a = loft.saveRole(draft);
  const b = loft.saveRole(draft);
  assert.deepEqual([a.id, b.id], ["tester", "tester-2"]);
  assert.deepEqual(persisted.map((r) => r.id), ["tester", "tester-2"], "only custom roles persisted");
  assert.equal(loft.spawn({ roleId: "tester" }).role.title, "Tester");
  assert.throws(() => loft.deleteRole("builder"), /can't be deleted/);
  loft.deleteRole("tester-2");
  assert.deepEqual(persisted.map((r) => r.id), ["tester"]);
  const reloaded = make({ roles: persisted });
  assert.ok(reloaded.snapshot().roles.some((r) => r.id === "tester"), "saved roles come back on restart");
});

test("agents run in parallel, one task at a time each, then wait for review; usage + cost tracked", async () => {
  const r = gatedRunner();
  const loft = make({ runTask: r.runTask, priceOf: async (m) => (m === "cheap/model" ? { in: 0.000001, out: 0.000002 } : null) });
  const cheap = loft.spawn({ roleId: "builder", model: "cheap/model" });
  const [scout] = loft.snapshot().agents;
  const a = loft.assign(cheap.id, "login");
  const b = loft.assign(cheap.id, "signup");
  loft.assign(scout.id, "research");
  await tick();
  assert.equal(r.calls.length, 2, "two agents at once; 2nd task waits");
  assert.equal(r.calls[0].agent.model, "cheap/model", "runner gets the agent's own model");
  const t = (id) => loft.snapshot().tasks.find((x) => x.id === id);
  assert.equal(t(a.id).last, "edit_file login.ts");
  assert.equal(t(b.id).status, "queued");
  r.calls[0].resolve({ text: "PASS", usage: { requests: 2, inputTokens: 1000, outputTokens: 500 } });
  await tick();
  await tick();
  assert.equal(t(a.id).status, "review");
  assert.deepEqual(t(a.id).usage, { requests: 2, inputTokens: 1000, outputTokens: 500, costUsd: 0.002 });
  const agent = loft.snapshot().agents.find((x) => x.id === cheap.id);
  assert.equal(agent.usage.inputTokens, 1000);
  assert.equal(agent.usage.costUsd, 0.002);
  assert.equal(r.calls.length, 3, "picked up its next task");
  await loft.approve(a.id);
  await assert.rejects(loft.approve(a.id), /not in review/);
});

test("reject re-queues with feedback; failures surface; stop aborts", async () => {
  const r = gatedRunner();
  const loft = make({ runTask: r.runTask });
  const builder = loft.snapshot().agents[1];
  const t = loft.assign(builder.id, "login");
  await tick();
  r.calls[0].resolve({ text: "meh" });
  await tick();
  await tick();
  const retry = loft.reject(t.id, "add tests");
  assert.match(retry.title, /CEO feedback.*add tests/s);
  await tick();
  r.calls[1].reject(new Error("tests failed"));
  await tick();
  assert.equal(loft.snapshot().tasks.find((x) => x.id === retry.id).status, "failed");
  assert.equal(loft.snapshot().agents[1].state, "failed");
  loft.assign(builder.id, "long job");
  await tick();
  loft.stop();
  await tick();
  assert.equal(loft.snapshot().tasks.at(-1).status, "stopped");
  assert.equal(loft.snapshot().agents[1].state, "idle");
  assert.throws(() => loft.assign(builder.id, "   "), /title is required/);
});

test("goal is split across agents by the planner (sees roles)", async () => {
  let seen;
  const loft = make({
    runTask: () => new Promise(() => {}),
    planGoal: async ({ agents }) => {
      seen = agents;
      return [{ agentId: agents[0].id, title: "research auth libs" }, { agentId: agents[1].id, title: "build login page" }];
    },
  });
  const created = await loft.goal("add login");
  assert.deepEqual(created.map((t) => t.title), ["research auth libs", "build login page"]);
  assert.equal(seen[1].role.title, "Coder");
});

test("party mode is broadcast and part of the snapshot", () => {
  const loft = make();
  const events = [];
  loft.events.on("event", (e) => events.push(e));
  assert.equal(loft.snapshot().party, false);
  loft.setParty(true);
  assert.deepEqual(events.at(-1), { type: "party", on: true });
  assert.equal(loft.snapshot().party, true, "late joiners walk into the party");
  loft.setParty(false);
  assert.equal(loft.snapshot().party, false);
});

test("parsePlan is total and falls back to a member who can write files", () => {
  const agents = [{ id: "r-1", role: { tools: ["read_file"] } }, { id: "c-2", role: { tools: ["write_file"] } }];
  assert.deepEqual(parsePlan('ok: [{"agentId":"r-1","title":"x"},{"agentId":"ghost","title":"y"}]', agents, "g"), [{ agentId: "r-1", title: "x" }]);
  assert.deepEqual(parsePlan("no json here", agents, "g"), [{ agentId: "c-2", title: "g" }]);
});

test("loft store: one card shape, roundtrip, no path traversal", async () => {
  const dir = await mkdtemp(path.join(os.tmpdir(), "loft-"));
  const loft = validateLoft({ roomId: "kos", boards: [{ id: "kanban", content: { cards: [{ id: "c1", column: "Doing" }] } }], agents: [], version: 1, updatedAt: "x" });
  assert.deepEqual(loft.boards[0].cards, [{ id: "c1", column: "Doing" }]);
  await saveLoft(loft, { localOnly: true, localDir: dir });
  const back = await loadLoft("kos", { localOnly: true, localDir: dir });
  assert.equal(back.version, 2);
  await assert.rejects(loadLoft("../../etc/passwd", { localOnly: true, localDir: dir }), /invalid roomId/);
});

// ---- real git worktrees + real previews, fake model (end-to-end, offline) ----
import { existsSync } from "node:fs";
import { readFile, writeFile } from "node:fs/promises";
import { createPreviews } from "../src/previews.js";
import { createProjects, git } from "../src/projects.js";

test("e2e: tasks in worktrees -> diff + preview in review -> approve merges, conflict -> fix task, reject keeps worktree", { timeout: 180_000 }, async () => {
  const tmp = await mkdtemp(path.join(os.tmpdir(), "loft-e2e-"));
  const projects = createProjects({ dataDir: path.join(tmp, "data"), projectsDir: path.join(tmp, "p"), defaultPath: null });
  const site = await projects.create({ name: "Site", template: "static" });
  const previews = createPreviews({ dir: path.join(tmp, "shots") });
  const seen = [];
  // fake "model": each task writes the file named in its title into ITS workspace
  const runTask = async ({ title, workspace, checkPage }) => {
    seen.push(workspace);
    if (/git merge main/.test(title)) { // conflict task: merge main, resolve, nothing else
      await git(workspace, ["merge", "main"], { ok: true });
      await writeFile(path.join(workspace, "index.html"), "<h1>merged</h1>");
    } else {
      const m = title.match(/^write (\S+) = (.+)$/m);
      if (m) await writeFile(path.join(workspace, m[1]), m[2]);
    }
    const report = checkPage ? await checkPage("/") : "";
    return { text: `done ${report.includes("HTTP 200") ? "(page ok)" : ""}`, usage: { requests: 1, inputTokens: 10, outputTokens: 5 } };
  };
  const loft = createOrchestrator({ runTask, planGoal: async () => [], projects, previews });
  const [, fox, cat] = loft.snapshot().agents; // Builder, Keeper (Scout is [0])
  const until = async (fn) => { for (let i = 0; i < 400 && !fn(); i++) await new Promise((r) => setTimeout(r, 50)); assert.ok(fn()); };
  const task = (id) => loft.snapshot().tasks.find((t) => t.id === id);

  const a = loft.assign(fox.id, "write index.html = <h1>Fox</h1>", site.id);
  const b = loft.assign(cat.id, "write index.html = <h1>Cat</h1>", site.id);
  await until(() => task(a.id).status === "review" && task(b.id).status === "review");
  assert.notEqual(seen[0], seen[1], "parallel tasks, separate folders");
  assert.equal(await readFile(path.join(site.path, "index.html"), "utf8").then((x) => x.includes("Fox")), false, "main untouched");
  assert.deepEqual(task(a.id).diff.files, ["index.html"]);
  assert.match(task(a.id).result, /page ok/, "agent's check_page saw its own page");
  assert.equal(task(a.id).worktree, undefined, "server paths never leave the server");
  await until(() => task(a.id).preview?.status === "ready");
  assert.match(task(a.id).preview.desktop, /^\/previews\/t\d+-desktop\.png$/);

  await loft.approve(a.id);
  assert.equal(task(a.id).status, "approved");
  assert.match(task(a.id).merged, /^[0-9a-f]{7}/);
  assert.equal(await readFile(path.join(site.path, "index.html"), "utf8"), "<h1>Fox</h1>");

  const res = await loft.approve(b.id); // same file -> conflict
  assert.equal(task(b.id).status, "conflict");
  assert.deepEqual(task(b.id).conflicts, ["index.html"]);
  assert.equal(res.retry.agentId, cat.id, "the same agent gets the fix");
  await until(() => task(res.retry.id).status === "review");
  assert.equal(seen.at(-1), path.join(tmp, "data", "worktrees", "site", b.id), "fix runs in Cat's original worktree");
  const fixed = await loft.approve(res.retry.id);
  assert.equal(fixed.status, "approved", `fix approved (got ${fixed.status} ${JSON.stringify(fixed.conflicts ?? fixed.diff)})`);
  assert.equal(await readFile(path.join(site.path, "index.html"), "utf8"), "<h1>merged</h1>");

  const c = loft.assign(fox.id, "write about.html = v1", site.id);
  await until(() => task(c.id).status === "review");
  const retry = loft.reject(c.id, "make it v2");
  await until(() => task(retry.id).status === "review");
  assert.equal(seen.at(-1), seen.at(-2), "reject: retry continues in the same worktree");
  assert.match(await git(site.path, ["log", "-1", "--format=%s"]), /\(Virtual Loft\)$/, "approvals are squash commits on main");
  assert.ok(!existsSync(path.join(site.path, "about.html")), "rejected work never reached main");
  await previews.close();
});
