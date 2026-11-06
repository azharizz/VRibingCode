// VIRTUAL LOFT — projects + one git worktree per task.
//
// A project = a git repo the team works on: { id, name, path, main, dev, setup, url, kind }.
// Every task runs in its OWN worktree on its own branch (loft/<agent>/<task>),
// so parallel agents never collide and nothing touches `main` until the CEO approves:
//   approve -> squash-merge into main (one clean commit per task), worktree removed
//   reject  -> same worktree/branch goes back to the agent with feedback
//   conflict-> the agent gets a "merge main and resolve" task in its worktree
// Agents never push anywhere; publishing stays the CEO's call.
// git runs via execFile (no shell): titles/paths can't inject commands.

import { exec, execFile } from "node:child_process";
import { promises as fs } from "node:fs";
import path from "node:path";
import { promisify } from "node:util";

const execFileP = promisify(execFile);
const execP = promisify(exec);
const LOFT_GIT = ["-c", "core.quotepath=false", "-c", "user.name=Virtual Loft", "-c", "user.email=loft@localhost", "-c", "commit.gpgsign=false"];

export class ProjectError extends Error {
  constructor(status, message) {
    super(message);
    this.status = status;
  }
}

export async function git(cwd, args, { ok = false } = {}) {
  try {
    const { stdout } = await execFileP("git", [...LOFT_GIT, ...args], { cwd, maxBuffer: 20 * 1024 * 1024 });
    return stdout.trim();
  } catch (err) {
    if (ok) return null;
    throw new Error(`git ${args[0]}: ${String(err.stderr || err.message).trim().slice(0, 400)}`);
  }
}

const slug = (s) => String(s).toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "").slice(0, 32) || "project";
const exists = (p) => fs.stat(p).then(() => true, () => false);
const LEAVE = /^(node_modules|\.venv|venv|__pycache__)$/; // never scanned, never copied

const STARTERS = {
  empty: { "README.md": "# {name}\n\nBuilt by the Virtual Loft team.\n" },
  static: {
    "README.md": "# {name}\n\nStatic site. Open index.html, or let the loft preview it.\n",
    "index.html": `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>{name}</title>
  <link rel="stylesheet" href="style.css">
</head>
<body>
  <main>
    <h1>{name}</h1>
    <p>Your AI team will build this page.</p>
  </main>
</body>
</html>
`,
    "style.css": "body{font-family:system-ui,sans-serif;margin:0;display:grid;place-items:center;min-height:100vh;background:#faf7f2;color:#222}\nmain{max-width:40rem;padding:2rem;text-align:center}\n",
  },
};

export function createProjects({ dataDir, projectsDir, defaultPath }) {
  const file = path.join(dataDir, "projects.json");
  const wtRoot = path.join(dataDir, "worktrees");
  let projects = new Map();

  async function persist() {
    await fs.mkdir(dataDir, { recursive: true });
    await fs.writeFile(file, JSON.stringify([...projects.values()], null, 2));
  }

  async function isRepoRoot(dir) {
    const top = await git(dir, ["rev-parse", "--show-toplevel"], { ok: true });
    return top !== null && (await fs.realpath(top.trim())) === (await fs.realpath(dir));
  }

  async function ensureRepo(dir, init) {
    if (!(await exists(dir))) {
      if (!init) throw new ProjectError(400, `folder '${dir}' does not exist`);
      await fs.mkdir(dir, { recursive: true });
    }
    // A folder nested in some other repo is NOT a project: its branches and
    // approve-merges would land in that outer repo.
    if (!(await isRepoRoot(dir))) {
      if (!init) throw new ProjectError(400, `'${dir}' is not the root of a git repo (send init:true to create one)`);
      await git(dir, ["init", "-q", "-b", "main"]);
    }
    if ((await git(dir, ["rev-parse", "--verify", "HEAD"], { ok: true })) === null) {
      await git(dir, ["add", "-A"]);
      await git(dir, ["commit", "-q", "--allow-empty", "-m", "Start project (Virtual Loft)"]);
    }
    return git(dir, ["symbolic-ref", "--short", "HEAD"]);
  }

  async function load() {
    try {
      projects = new Map(JSON.parse(await fs.readFile(file, "utf8")).map((p) => [p.id, p]));
    } catch {
      projects = new Map();
    }
    if (defaultPath && ![...projects.values()].some((p) => path.resolve(p.path) === path.resolve(defaultPath))) {
      await add({ name: "workspace", path: defaultPath, init: true, kind: "static" });
    } else if (defaultPath) {
      await ensureRepo(defaultPath, true); // registered by an older version: may still sit inside another repo
    }
    return list();
  }

  const list = () => [...projects.values()];
  function get(id) {
    const p = projects.get(id) ?? (id ? null : list()[0]);
    if (!p) throw new ProjectError(404, `no project '${id}'`);
    return p;
  }

  // Register an existing repo (or an empty folder with init:true).
  async function add({ name, path: dir, dev = "", setup = "", url = "", kind = "", init = false } = {}) {
    if (!dir) throw new ProjectError(400, "path is required");
    const abs = path.resolve(String(dir).replace(/^~(?=$|\/)/, process.env.HOME ?? "~"));
    const main = await ensureRepo(abs, init);
    const real = await fs.realpath(abs);
    for (const known of projects.values()) if ((await fs.realpath(known.path).catch(() => known.path)) === real) return known; // same folder, same workspace
    let id = slug(name || path.basename(abs));
    for (let n = 2; projects.has(id); n++) id = `${slug(name || path.basename(abs))}-${n}`;
    const hasPkg = await exists(path.join(abs, "package.json"));
    const p = {
      id,
      name: String(name || path.basename(abs)).slice(0, 40),
      path: abs,
      main,
      dev: String(dev || ""), // e.g. "npm run dev -- --port {port}" ({port} and $PORT are filled in)
      setup: String(setup || ""), // runs once per new worktree, e.g. "npm ci"
      url: String(url || ""), // preview URL template, default http://127.0.0.1:{port}/
      kind: kind || (hasPkg || dev ? "web" : "static"),
    };
    projects.set(id, p);
    await persist();
    return p;
  }

  // Brand-new project folder (+ git init) from a tiny starter.
  async function create({ name, template = "static" } = {}) {
    const n = String(name ?? "").trim();
    if (!n) throw new ProjectError(400, "name is required");
    const files = STARTERS[template];
    if (!files) throw new ProjectError(400, `unknown template '${template}' (want ${Object.keys(STARTERS).join("|")})`);
    let dir = path.join(projectsDir, slug(n));
    for (let k = 2; await exists(dir); k++) dir = path.join(projectsDir, `${slug(n)}-${k}`);
    await fs.mkdir(dir, { recursive: true });
    for (const [f, body] of Object.entries(files)) await fs.writeFile(path.join(dir, f), body.replaceAll("{name}", n));
    return add({ name: n, path: dir, init: true, kind: template === "static" ? "static" : "" });
  }

  async function remove(id) {
    get(id);
    projects.delete(id); // unregister only: the folder and its history stay
    await persist();
  }

  // ---- per-task worktrees ----
  async function prepare(project, { taskId, agent }) {
    if (!(await isRepoRoot(project.path))) throw new ProjectError(409, `'${project.path}' is not its own git repo: refusing to touch the outer repo`);
    const branch = `loft/${slug(agent)}/${taskId}`;
    const worktree = path.join(wtRoot, project.id, taskId);
    await fs.mkdir(path.dirname(worktree), { recursive: true });
    await git(project.path, ["worktree", "add", "-q", "-b", branch, worktree, project.main]);
    // Dependencies: reuse the main checkout's node_modules when present (fast), else run setup.
    const mainModules = path.join(project.path, "node_modules");
    if (!project.setup && (await exists(mainModules)) && (await exists(path.join(worktree, "package.json")))) {
      await fs.symlink(mainModules, path.join(worktree, "node_modules"), "dir").catch(() => {});
    }
    if (project.setup) await execP(project.setup, { cwd: worktree, timeout: 10 * 60 * 1000, maxBuffer: 20 * 1024 * 1024 });
    return { branch, worktree };
  }

  // Git can't carry an empty folder or a repo inside a repo: give it files it can.
  async function tidy(worktree, dir = worktree) {
    const entries = await fs.readdir(dir, { withFileTypes: true });
    if (dir !== worktree && entries.length === 0) await fs.writeFile(path.join(dir, ".gitkeep"), "");
    for (const e of entries) {
      if (!e.isDirectory() || e.name === ".git" || LEAVE.test(e.name)) continue;
      const sub = path.join(dir, e.name);
      if (await exists(path.join(sub, ".git"))) {
        const tracked = await git(worktree, ["ls-files", "-s", "--", path.relative(worktree, sub)], { ok: true });
        if (tracked?.startsWith("160000")) continue; // a real submodule
        await fs.rm(path.join(sub, ".git"), { recursive: true, force: true });
      }
      await tidy(worktree, sub);
    }
  }

  async function save(worktree, message) {
    await tidy(worktree);
    await git(worktree, ["add", "-A", "--", ".", ":!node_modules"]);
    // --no-renames: with rename detection --quiet calls a pure file move "no changes"
    if ((await git(worktree, ["diff", "--cached", "--quiet", "--no-renames"], { ok: true })) === null) {
      await git(worktree, ["commit", "-q", "-m", message]);
    }
  }

  // Stashes are shared by the whole repo: take back only what was stashed on this task's branch.
  async function unstash(worktree, branch) {
    for (let i = 0; i < 20; i++) {
      const list = ((await git(worktree, ["stash", "list", "--format=%gs"], { ok: true })) ?? "").split("\n");
      const n = list.findLastIndex((s) => s.startsWith(`WIP on ${branch}:`) || s.startsWith(`On ${branch}:`));
      if (n < 0) return false;
      if ((await git(worktree, ["stash", "pop", "-q", `stash@{${n}}`], { ok: true })) === null) {
        await git(worktree, ["reset", "-q", "--hard"], { ok: true });
        return true; // it stays in the stash, safe
      }
    }
    return false;
  }

  // The agent may have left the task's branch: the work follows it back.
  async function rejoin(worktree, branch) {
    const head = await git(worktree, ["rev-parse", "HEAD"]);
    const tip = await git(worktree, ["rev-parse", branch]);
    if (head === tip) return;
    if ((await git(worktree, ["merge-base", "--is-ancestor", tip, "HEAD"], { ok: true })) === null) await git(worktree, ["merge", "-q", "--no-edit", branch]);
    await git(worktree, ["checkout", "-q", "-B", branch]);
  }

  // New files git ignores (build output): they are work too, but not for a commit.
  async function ignoredIn(worktree) {
    const out = (await git(worktree, ["ls-files", "--others", "--ignored", "--exclude-standard"], { ok: true })) ?? "";
    return out.split("\n").filter((f) => f && !f.split("/").some((part) => LEAVE.test(part)) && !/(^|\/)(\.gitkeep|\.DS_Store)$/.test(f));
  }

  // On approve they land next to the merged work. A file already there is never replaced.
  async function keepIgnored(project, worktree) {
    if (!worktree) return;
    for (const f of await ignoredIn(worktree)) {
      const to = path.join(project.path, f);
      if (await exists(to)) continue;
      await fs.mkdir(path.dirname(to), { recursive: true });
      await fs.copyFile(path.join(worktree, f), to);
    }
  }

  // Commit whatever the agent changed; report what differs from main.
  async function finish(project, { worktree, branch }, message) {
    await save(worktree, message);
    const stashed = branch ? await unstash(worktree, branch) : false;
    await save(worktree, message);
    if (branch) await rejoin(worktree, branch);
    const files = (await git(worktree, ["diff", "--name-only", `${project.main}...HEAD`])).split("\n").filter(Boolean);
    const stat = (await git(worktree, ["diff", "--shortstat", `${project.main}...HEAD`])) || "no file changes";
    return { files, stat, ignored: (await ignoredIn(worktree)).slice(0, 200), stashed };
  }

  // Where the site starts: "" (top), "index/" (one folder down), or null when there is no page.
  async function entry(project, dir = project.path) {
    if (project.dev || (await exists(path.join(dir, "index.html")))) return "";
    const all = await fs.readdir(dir, { withFileTypes: true });
    for (const d of all) if (d.isDirectory() && !d.name.startsWith(".") && d.name !== "node_modules" && (await exists(path.join(dir, d.name, "index.html")))) return `${d.name}/`;
    return null;
  }

  // What the project card shows.
  async function summary(project) {
    const log = await git(project.path, ["log", "-1", "--format=%s%x00%cr", project.main], { ok: true });
    const [title, when] = (log ?? "").split("\0");
    const site = (await entry(project)) !== null;
    return { id: project.id, name: project.name, site, latest: title ? { title: title.split(" — ")[0], when } : null };
  }

  // Squash-merge the task branch into main in the project's own checkout.
  async function merge(project, { branch, worktree }, message) {
    if (!(await git(project.path, ["diff", "--name-only", `${project.main}...${branch}`]))) {
      // Nothing to merge is only true if nothing sits outside the branch either.
      const left = worktree && (await exists(worktree));
      if (left && (await git(worktree, ["status", "--porcelain"], { ok: true }))) {
        throw new ProjectError(409, `${project.name}: this task has uncommitted work, so it was kept for review`);
      }
      if (left && (await git(worktree, ["rev-parse", "HEAD"], { ok: true })) !== (await git(worktree, ["rev-parse", branch], { ok: true }))) {
        throw new ProjectError(409, `${project.name}: this task's work is on another branch, so it was kept for review`);
      }
      if (left) await keepIgnored(project, worktree);
      await discard(project, { branch, worktree });
      return { ok: true, sha: null, note: "nothing to merge" };
    }
    const on = await git(project.path, ["symbolic-ref", "--short", "HEAD"], { ok: true });
    if (on !== project.main) throw new ProjectError(409, `${project.name}: check out '${project.main}' first (it is on '${on}')`);
    if (await git(project.path, ["status", "--porcelain", "--untracked-files=no"])) {
      throw new ProjectError(409, `${project.name}: you have uncommitted changes on ${project.main}; commit or stash them, then approve again`);
    }
    if ((await git(project.path, ["merge", "--squash", branch], { ok: true })) === null) {
      const conflicts = ((await git(project.path, ["diff", "--name-only", "--diff-filter=U"], { ok: true })) || "").split("\n").filter(Boolean);
      await git(project.path, ["reset", "-q", "--merge"], { ok: true });
      if (conflicts.length) return { ok: false, conflicts };
      // Not a conflict an agent can fix: git refused to write over the CEO's own files.
      const added = (await git(project.path, ["diff", "--name-only", "--no-renames", "--diff-filter=A", `${project.main}...${branch}`], { ok: true })) ?? "";
      const clash = [];
      for (const f of added.split("\n").filter(Boolean)) if (await exists(path.join(project.path, f))) clash.push(f);
      throw new ProjectError(409, clash.length
        ? `${clash.slice(0, 5).join(", ")} already on ${project.main} but not committed. Move or commit, then approve again`
        : `${project.name}: git could not merge this task, it was kept for review`);
    }
    await git(project.path, ["commit", "-q", "-m", message]);
    const sha = await git(project.path, ["rev-parse", "--short", "HEAD"]);
    await keepIgnored(project, worktree);
    await discard(project, { branch, worktree });
    return { ok: true, sha };
  }

  async function discard(project, { branch, worktree }) {
    if (worktree) await git(project.path, ["worktree", "remove", "--force", worktree], { ok: true });
    if (branch) await git(project.path, ["branch", "-D", branch], { ok: true });
    await git(project.path, ["worktree", "prune"], { ok: true });
  }

  return { load, list, get, add, create, remove, prepare, finish, merge, summary, entry, discard, templates: Object.keys(STARTERS) };
}
