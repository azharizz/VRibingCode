// Projects, per-task worktrees and previews against REAL git + REAL Chromium (offline).
import assert from "node:assert/strict";
import { existsSync } from "node:fs";
import { mkdir, mkdtemp, readFile, realpath, rm, stat, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { after, test } from "node:test";
import { createPreviews } from "../src/previews.js";
import { createProjects, git } from "../src/projects.js";

const tmp = await mkdtemp(path.join(os.tmpdir(), "loft-proj-"));
const projects = createProjects({ dataDir: path.join(tmp, "data"), projectsDir: path.join(tmp, "projects"), defaultPath: null });
const previews = createPreviews({ dir: path.join(tmp, "previews") });
after(() => previews.close());

test("new project: folder + git repo + starter, persisted", async () => {
  const p = await projects.create({ name: "Landing Site", template: "static" });
  assert.equal(p.id, "landing-site");
  assert.equal(p.main, "main");
  assert.equal(p.kind, "static");
  assert.ok(existsSync(path.join(p.path, "index.html")));
  assert.match(await git(p.path, ["log", "--oneline"]), /Start project/);
  const again = createProjects({ dataDir: path.join(tmp, "data"), projectsDir: path.join(tmp, "projects"), defaultPath: null });
  assert.deepEqual((await again.load()).map((x) => x.id), ["landing-site"]);
  await assert.rejects(projects.add({ path: path.join(tmp, "nope") }), /does not exist/);
});

test("a folder inside another repo never touches the outer repo", async () => {
  const outer = path.join(tmp, "outer");
  await mkdir(path.join(outer, "workspace"), { recursive: true });
  await git(outer, ["init", "-q", "-b", "main"]);
  await git(outer, ["commit", "-q", "--allow-empty", "-m", "outer work"]);
  const inner = path.join(outer, "workspace");
  const mk = () => createProjects({ dataDir: path.join(tmp, "nested"), projectsDir: path.join(tmp, "np"), defaultPath: inner });

  await assert.rejects(projects.add({ path: inner }), /not the root of a git repo/);
  const [p] = await mk().load(); // default workspace gets a repo of its own
  assert.equal((await git(inner, ["rev-parse", "--show-toplevel"])).trim(), await realpath(inner));
  assert.match(await git(inner, ["log", "--oneline"]), /Start project/);
  assert.doesNotMatch(await git(outer, ["branch", "--list", "loft/*"]), /loft/);

  await rm(path.join(inner, ".git"), { recursive: true }); // an entry saved before this rule
  const stale = createProjects({ dataDir: path.join(tmp, "nested"), projectsDir: path.join(tmp, "np") });
  await stale.load();
  await assert.rejects(stale.prepare(p, { taskId: "t1", agent: "Fox" }), /refusing to touch the outer repo/);
  assert.doesNotMatch(await git(outer, ["branch", "--list", "loft/*"]), /loft/);
});

test("parallel tasks get separate worktrees; approve = one squash commit on main", async () => {
  const p = projects.get("landing-site");
  const a = await projects.prepare(p, { taskId: "t1", agent: "Fox" });
  const b = await projects.prepare(p, { taskId: "t2", agent: "Owl" });
  assert.notEqual(a.worktree, b.worktree);
  assert.equal(a.branch, "loft/fox/t1");
  await writeFile(path.join(a.worktree, "about.html"), "<h1>About</h1>");
  await writeFile(path.join(b.worktree, "contact.html"), "<h1>Contact</h1>");
  assert.ok(!existsSync(path.join(p.path, "about.html")), "main untouched while work is in progress");

  const da = await projects.finish(p, a, "loft: about page");
  assert.deepEqual(da.files, ["about.html"]);
  assert.match(da.stat, /1 file changed/);
  const m = await projects.merge(p, a, "Add about page (Fox)");
  assert.equal(m.ok, true);
  assert.ok(existsSync(path.join(p.path, "about.html")), "approved work is on main");
  assert.match(await git(p.path, ["log", "-1", "--format=%s"]), /Add about page \(Fox\)/);
  assert.ok(!existsSync(a.worktree), "worktree cleaned up");
  assert.equal(await git(p.path, ["branch", "--list", a.branch]), "", "branch deleted");

  await projects.finish(p, b, "loft: contact page");
  assert.equal((await projects.merge(p, b, "Add contact page (Owl)")).ok, true, "second task merges on top");
  assert.equal((await git(p.path, ["log", "--oneline"])).split("\n").length, 3, "start + 2 squash commits");
});

test("conflicting edits are detected, main stays clean, agent can resolve in its worktree", async () => {
  const p = projects.get("landing-site");
  const a = await projects.prepare(p, { taskId: "t3", agent: "Fox" });
  const b = await projects.prepare(p, { taskId: "t4", agent: "Cat" });
  await writeFile(path.join(a.worktree, "index.html"), "<h1>Fox version</h1>");
  await writeFile(path.join(b.worktree, "index.html"), "<h1>Cat version</h1>");
  await projects.finish(p, a, "fox");
  await projects.finish(p, b, "cat");
  assert.equal((await projects.merge(p, a, "fox wins")).ok, true);
  const c = await projects.merge(p, b, "cat");
  assert.deepEqual(c, { ok: false, conflicts: ["index.html"] });
  assert.equal(await git(p.path, ["status", "--porcelain"]), "", "main left clean after the failed merge");
  // what the agent would do in its worktree
  await git(b.worktree, ["merge", p.main], { ok: true });
  await writeFile(path.join(b.worktree, "index.html"), "<h1>Fox + Cat version</h1>");
  await projects.finish(p, b, "resolve");
  assert.equal((await projects.merge(p, b, "cat, resolved")).ok, true);
  assert.equal(await readFile(path.join(p.path, "index.html"), "utf8"), "<h1>Fox + Cat version</h1>");
});

test("approve refuses when the CEO has uncommitted changes on main", async () => {
  const p = projects.get("landing-site");
  const a = await projects.prepare(p, { taskId: "t5", agent: "Fox" });
  await writeFile(path.join(a.worktree, "x.txt"), "x");
  await projects.finish(p, a, "x");
  await writeFile(path.join(p.path, "index.html"), "my own edit");
  await assert.rejects(projects.merge(p, a, "x"), /uncommitted changes/);
  await git(p.path, ["checkout", "--", "index.html"]);
  assert.equal((await projects.merge(p, a, "x")).ok, true);
});

test("preview: static site from the task's worktree -> desktop + phone screenshots", { timeout: 120_000 }, async () => {
  const p = projects.get("landing-site");
  const w = await projects.prepare(p, { taskId: "t6", agent: "Fox" });
  await writeFile(path.join(w.worktree, "index.html"), "<title>Shiny ☕ café</title><h1 style='color:tomato'>New hero</h1><p>Built in VR</p>");
  const shot = await previews.capture("t6", p, w.worktree);
  assert.equal(shot.title, "Shiny ☕ café", "utf-8 served even without <meta charset>");
  assert.match(shot.url, /^http:\/\/127\.0\.0\.1:\d+\/$/);
  for (const f of [shot.desktop, shot.mobile]) assert.ok((await stat(path.join(tmp, "previews", f))).size > 1000, `${f} is a real PNG`);
  assert.equal((await fetch(shot.url)).status, 200, "live server keeps running during review");
  previews.stop("t6");
  await assert.rejects(fetch(shot.url, { signal: AbortSignal.timeout(2000) }), "stopped after review");
  await projects.discard(p, w);
});

test("check_page: agents get a text report incl. console errors", { timeout: 120_000 }, async () => {
  const p = projects.get("landing-site");
  const w = await projects.prepare(p, { taskId: "t7", agent: "Fox" });
  await writeFile(path.join(w.worktree, "index.html"), "<title>Broken</title><h1>Hi</h1><script>nope()</script>");
  const report = await previews.checkPage("t7", p, w.worktree, "/");
  assert.match(report, /HTTP 200/);
  assert.match(report, /title: Broken/);
  assert.match(report, /console errors: .*nope/);
  assert.match(report, /visible text: Hi/);
  previews.stop("t7");
});

test("dev-command projects: {port} is filled in, server output kept for errors", { timeout: 60_000 }, async () => {
  const dir = path.join(tmp, "devapp");
  const p = await projects.add({ name: "devapp", path: dir, init: true, dev: "node server.js {port}" });
  await writeFile(path.join(p.path, "server.js"), "require('http').createServer((q,s)=>s.end('<title>Dev</title>dev ok')).listen(+process.argv[2],'127.0.0.1')");
  await git(p.path, ["add", "-A"]);
  await git(p.path, ["commit", "-q", "-m", "server"]);
  const w = await projects.prepare(p, { taskId: "t8", agent: "Fox" });
  const shot = await previews.capture("t8", p, w.worktree);
  assert.equal(shot.title, "Dev");
  previews.stop("t8");
});
