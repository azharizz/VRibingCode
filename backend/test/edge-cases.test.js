// Awkward things agents and people really do. Each one once lost work or misled the CEO.
import assert from "node:assert/strict";
import { execSync } from "node:child_process";
import { existsSync, readFileSync, symlinkSync } from "node:fs";
import { mkdir, mkdtemp, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { PRESET_ROLES } from "../src/agents.js";
import { extractJson } from "../src/llm.js";
import { createOrchestrator } from "../src/orchestrator.js";
import { createProjects, git } from "../src/projects.js";
import { createVoice } from "../src/voice.js";

const ME = "-c user.name=t -c user.email=t@x";
const sh = (cwd, cmd) => execSync(cmd, { cwd, shell: "/bin/zsh", stdio: "pipe" });

async function repo(prep) {
  const tmp = await mkdtemp(path.join(os.tmpdir(), "loft-edge-"));
  const projects = createProjects({ dataDir: path.join(tmp, "d"), projectsDir: path.join(tmp, "p"), defaultPath: null });
  const p = await projects.create({ name: "Edge", template: "static" });
  if (prep) sh(p.path, prep);
  return { tmp, projects, p };
}
// An agent's turn: `cmd` runs in the worktree, then the task finishes.
async function agent(prep, cmd) {
  const r = await repo(prep);
  const t = await r.projects.prepare(r.p, { taskId: "t1", agent: "Builder" });
  sh(t.worktree, cmd);
  return { ...r, t, done: await r.projects.finish(r.p, t, "work") };
}
const on = (p, f) => existsSync(path.join(p.path, f));

test("an empty folder is real work and reaches main", async () => {
  const { projects, p, t, done } = await agent(null, "mkdir -p assets/img");
  assert.ok(done.files.length > 0, "not 'no changes'");
  assert.ok((await projects.merge(p, t, "m")).sha, "a real commit");
  assert.ok(on(p, "assets/img"), "the folder is on main");
});

test("files in an ignored folder are reported, and approving keeps them", async () => {
  const { projects, p, t, done } = await agent(`echo dist/ > .gitignore && git add -A && git ${ME} commit -qm ig`, "mkdir dist && echo built > dist/app.js");
  assert.deepEqual(done.ignored, ["dist/app.js"]);
  await projects.merge(p, t, "m");
  assert.ok(on(p, "dist/app.js"), "still there after approving");
});

test("approving never overwrites an ignored file the CEO already has", async () => {
  const { projects, p, t } = await agent(`echo .env > .gitignore && git add -A && git ${ME} commit -qm ig && echo mine > .env`, "echo agent > .env");
  await projects.merge(p, t, "m");
  assert.equal(readFileSync(path.join(p.path, ".env"), "utf8"), "mine\n");
});

test("work on another branch still merges", async () => {
  const { projects, p, t, done } = await agent(null, `git checkout -q -b feature && echo hi > new.txt && git add -A && git ${ME} commit -qm f`);
  assert.deepEqual(done.files, ["new.txt"]);
  assert.ok((await projects.merge(p, t, "m")).sha);
  assert.ok(on(p, "new.txt"));
});

test("work on a detached HEAD still merges", async () => {
  const { projects, p, t } = await agent(null, "git checkout -q --detach && echo hi > new.txt");
  assert.ok((await projects.merge(p, t, "m")).sha);
  assert.ok(on(p, "new.txt"));
});

test("work left in a stash comes back; the CEO's own stash is left alone", async () => {
  const r = await repo(`echo mine >> README.md && git stash -q`);
  const t = await r.projects.prepare(r.p, { taskId: "t1", agent: "Builder" });
  sh(t.worktree, "echo agent >> index.html && git stash -q");
  const done = await r.projects.finish(r.p, t, "work");
  assert.deepEqual(done.files, ["index.html"]);
  assert.ok((await r.projects.merge(r.p, t, "m")).sha);
  assert.match(readFileSync(path.join(r.p.path, "index.html"), "utf8"), /agent/);
  assert.equal((await git(r.p.path, ["stash", "list"])).split("\n").length, 1, "the CEO's stash is still there");
});

test("a repo created inside the project arrives as plain files", async () => {
  const { projects, p, t } = await agent(null, `mkdir sub && cd sub && git init -q && echo x > a.txt && git add -A && git ${ME} commit -qm s`);
  assert.ok((await projects.merge(p, t, "m")).sha);
  assert.ok(on(p, "sub/a.txt"), "the file itself, not a pointer");
});

test("a task that changed nothing closes even when the CEO has edits on main", async () => {
  const { projects, p, t } = await agent("echo edit >> index.html", "true");
  assert.equal((await projects.merge(p, t, "m")).note, "nothing to merge");
  assert.match(readFileSync(path.join(p.path, "index.html"), "utf8"), /edit/, "the CEO's edit is untouched");
});

test("a clash with an untracked file says which file, keeps the work, and bothers no agent", async () => {
  const { projects, p, t } = await agent(null, "echo agent > same.txt");
  sh(p.path, "echo mine > same.txt");
  await assert.rejects(projects.merge(p, t, "m"), /same\.txt/);
  assert.equal(readFileSync(path.join(p.path, "same.txt"), "utf8"), "mine\n");
  assert.ok(existsSync(path.join(t.worktree, "same.txt")), "the work is kept");
});

test("the same folder twice is the same workspace, however it is written", async () => {
  const { tmp, projects, p } = await repo();
  assert.equal((await projects.add({ path: `${p.path}/` })).id, p.id);
  symlinkSync(p.path, path.join(tmp, "link"));
  assert.equal((await projects.add({ path: path.join(tmp, "link") })).id, p.id);
  assert.equal(projects.list().length, 1);
});

test("a hosted demo's first workspace already has a site", async () => {
  const tmp = await mkdtemp(path.join(os.tmpdir(), "loft-seed-"));
  const projects = createProjects({ dataDir: path.join(tmp, "d"), projectsDir: path.join(tmp, "p"), defaultPath: path.join(tmp, "ws"), seedSite: true });
  const [p] = await projects.load();
  assert.equal((await projects.summary(p)).site, true);
  assert.match(await git(p.path, ["ls-files"]), /index\.html/, "committed, so agents' worktrees have it");
});

test("file names stay readable", async () => {
  const { done } = await agent(null, "echo x > 'café menu.txt'");
  assert.deepEqual(done.files, ["café menu.txt"]);
});

const fakeProjects = (over) => ({
  get: () => ({ id: "p", main: "main", path: "/x" }), list: () => [{ id: "p" }],
  prepare: async () => ({ worktree: "/wt", branch: "b" }), finish: async () => ({ files: [], ignored: [] }),
  merge: async () => ({ ok: true, sha: null }), ...over,
});
const settle = async () => { for (let i = 0; i < 40; i++) await new Promise((r) => setImmediate(r)); };

test("output only in ignored files waits for the CEO instead of closing", async () => {
  const loft = createOrchestrator({ runTask: async () => ({ text: "built" }), planGoal: async () => [], projects: fakeProjects({ finish: async () => ({ files: [], ignored: ["dist/app.js"] }) }) });
  const t = loft.assign(loft.snapshot().agents[1].id, "build");
  await settle();
  assert.equal(loft.snapshot().tasks.find((x) => x.id === t.id).status, "review");
});

test("voice approve only reaches the current workspace", async () => {
  const agents = [{ id: "b", name: "Builder", skin: "fox", state: "idle", role: { title: "Coder", instructions: "x", tools: [] } }];
  const tasks = [{ id: "here", agentId: "b", title: "a", status: "review", projectId: "shop" }, { id: "away", agentId: "b", title: "b", status: "review", projectId: "blog" }];
  const v = createVoice({ snapshot: () => ({ agents, projects: [{ id: "shop", name: "Shop" }, { id: "blog", name: "Blog" }], roles: PRESET_ROLES, tasks }), ask: async () => '{"action":"approve"}', extract: extractJson });
  assert.equal((await v.handleText({ text: "approve it", projectId: "shop" })).intent.taskId, "here");
});
