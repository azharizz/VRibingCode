// VIRTUAL LOFT — read-only views of a project for the headset: folder tree, one file, a task's diff.
// Reachable from the network, so: auth is checked by the server, and every path is
// locked inside the project folder (no "..", no absolute paths, no symlink escapes),
// with .git and secret-looking files hidden.
import { promises as fs } from "node:fs";
import path from "node:path";
import { git } from "./projects.js";
import { HttpError } from "./orchestrator.js";
import { realInside, safePath } from "./tools.js";

const HIDDEN_DIRS = new Set([".git", "node_modules", "Library", "Temp", "obj", ".venv", "__pycache__"]);
const SECRET_FILE = /^\.env(\..*)?$|\.pem$|\.key$|^id_(rsa|ed25519)|^\.npmrc$|credentials/i;
const MAX_FILE = 200_000;
const MAX_ENTRIES = 500;

// rel -> absolute path inside root, or an HttpError. Never reveals what is outside.
export async function resolveInside(root, rel = "") {
  const r = String(rel ?? "");
  if (r.includes("\0")) throw new HttpError(400, "bad path");
  if (path.isAbsolute(r) || r.split(/[\\/]/).includes("..")) throw new HttpError(403, "path must stay inside the project");
  const parts = r.split(/[\\/]/).filter(Boolean);
  if (parts.some((x) => HIDDEN_DIRS.has(x) || (SECRET_FILE.test(x) && x !== ".env.example"))) throw new HttpError(403, "that path is not available");
  let p;
  try {
    p = safePath(root, r || ".");
    await realInside(root, p);
  } catch {
    throw new HttpError(403, "path must stay inside the project");
  }
  return p;
}

export async function tree(root, rel = "") {
  const dir = await resolveInside(root, rel);
  let entries;
  try {
    entries = await fs.readdir(dir, { withFileTypes: true });
  } catch (e) {
    throw new HttpError(e.code === "ENOTDIR" ? 400 : 404, e.code === "ENOTDIR" ? "not a folder" : "no such folder");
  }
  const out = [];
  for (const e of entries.sort((a, b) => Number(b.isDirectory()) - Number(a.isDirectory()) || a.name.localeCompare(b.name))) {
    if (HIDDEN_DIRS.has(e.name) || (SECRET_FILE.test(e.name) && e.name !== ".env.example")) continue;
    if (!e.isDirectory() && !e.isFile()) continue; // symlinks, sockets: skipped
    const entry = { name: e.name, type: e.isDirectory() ? "dir" : "file" };
    if (e.isFile()) entry.size = (await fs.stat(path.join(dir, e.name))).size;
    out.push(entry);
    if (out.length >= MAX_ENTRIES) break;
  }
  return { path: path.relative(root, dir).split(path.sep).join("/"), entries: out, truncated: out.length >= MAX_ENTRIES };
}

export async function readFile(root, rel) {
  if (!String(rel ?? "").trim()) throw new HttpError(400, "path is required");
  const p = await resolveInside(root, rel);
  let st;
  try {
    st = await fs.stat(p);
  } catch {
    throw new HttpError(404, "no such file");
  }
  if (!st.isFile()) throw new HttpError(400, "not a file");
  const fh = await fs.open(p, "r");
  try {
    const buf = Buffer.alloc(Math.min(st.size, MAX_FILE));
    await fh.read(buf, 0, buf.length, 0);
    const binary = buf.subarray(0, 8000).includes(0);
    return {
      path: path.relative(root, p).split(path.sep).join("/"), size: st.size, binary,
      truncated: st.size > MAX_FILE, content: binary ? "" : buf.toString("utf8"),
    };
  } finally {
    await fh.close();
  }
}

// Unified diff text -> {files:[{path,added,removed,hunks:[{header,lines:[{type:add|del|ctx,text}]}]}]}
export function parseDiff(text) {
  const files = [];
  let f = null;
  let h = null;
  for (const line of String(text).split("\n")) {
    if (line.startsWith("diff --git ")) {
      const m = line.match(/ b\/(.+)$/);
      f = { path: m ? m[1] : line.slice(11), added: 0, removed: 0, hunks: [] };
      files.push(f);
      h = null;
    } else if (!f) continue;
    else if (line.startsWith("@@")) {
      h = { header: line, lines: [] };
      f.hunks.push(h);
    } else if (h && (line[0] === "+" || line[0] === "-" || line[0] === " ")) {
      const type = line[0] === "+" ? "add" : line[0] === "-" ? "del" : "ctx";
      if (type === "add") f.added++;
      if (type === "del") f.removed++;
      h.lines.push({ type, text: line.slice(1).slice(0, 400) });
    } else if (line.startsWith("Binary files")) f.binary = true;
  }
  for (const x of files) x.hunks = x.hunks.slice(0, 40);
  return { files: files.slice(0, 100) };
}

// A task's change: its live worktree vs the main branch, or the merge commit once approved.
export async function taskDiff(project, task) {
  let text;
  if (task.worktree) text = await git(task.worktree, ["diff", "--no-color", "-U3", `${project.main}...HEAD`], { ok: true });
  else if (task.merged) text = await git(project.path, ["show", "--no-color", "-U3", "--format=", task.merged], { ok: true });
  else throw new HttpError(404, `task '${task.id}' has no changes to show`);
  if (text === null) throw new HttpError(404, "the diff is no longer available");
  return parseDiff(text);
}
