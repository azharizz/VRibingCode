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
const LOFT_GIT = ["-c", "user.name=Virtual Loft", "-c", "user.email=loft@localhost", "-c", "commit.gpgsign=false"];

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

  async function ensureRepo(dir, init) {
    if (!(await exists(dir))) {
      if (!init) throw new ProjectError(400, `folder '${dir}' does not exist`);
      await fs.mkdir(dir, { recursive: true });
    }
    if ((await git(dir, ["rev-parse", "--show-toplevel"], { ok: true })) === null) {
      if (!init) throw new ProjectError(400, `'${dir}' is not a git repo (send init:true to create one)`);
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

  // Commit whatever the agent changed; report what differs from main.
  async function finish(project, { worktree }, message) {
    await git(worktree, ["add", "-A", "--", ".", ":!node_modules"]);
    if ((await git(worktree, ["diff", "--cached", "--quiet"], { ok: true })) === null) {
      await git(worktree, ["commit", "-q", "-m", message]);
    }
    const files = (await git(worktree, ["diff", "--name-only", `${project.main}...HEAD`])).split("\n").filter(Boolean);
    const stat = (await git(worktree, ["diff", "--shortstat", `${project.main}...HEAD`])) || "no file changes";
    return { files, stat };
  }

  // Squash-merge the task branch into main in the project's own checkout.
  async function merge(project, { branch, worktree }, message) {
    const on = await git(project.path, ["symbolic-ref", "--short", "HEAD"], { ok: true });
    if (on !== project.main) throw new ProjectError(409, `${project.name}: check out '${project.main}' first (it is on '${on}')`);
    if (await git(project.path, ["status", "--porcelain", "--untracked-files=no"])) {
      throw new ProjectError(409, `${project.name}: you have uncommitted changes on ${project.main}; commit or stash them, then approve again`);
    }
    if (!(await git(project.path, ["diff", "--name-only", `${project.main}...${branch}`]))) {
      await discard(project, { branch, worktree });
      return { ok: true, sha: null, note: "nothing to merge" };
    }
    if ((await git(project.path, ["merge", "--squash", branch], { ok: true })) === null) {
      const conflicts = ((await git(project.path, ["diff", "--name-only", "--diff-filter=U"], { ok: true })) || "").split("\n").filter(Boolean);
      await git(project.path, ["reset", "-q", "--merge"], { ok: true });
      return { ok: false, conflicts };
    }
    await git(project.path, ["commit", "-q", "-m", message]);
    const sha = await git(project.path, ["rev-parse", "--short", "HEAD"]);
    await discard(project, { branch, worktree });
    return { ok: true, sha };
  }

  async function discard(project, { branch, worktree }) {
    if (worktree) await git(project.path, ["worktree", "remove", "--force", worktree], { ok: true });
    if (branch) await git(project.path, ["branch", "-D", branch], { ok: true });
    await git(project.path, ["worktree", "prune"], { ok: true });
  }

  return { load, list, get, add, create, remove, prepare, finish, merge, discard, templates: Object.keys(STARTERS) };
}
