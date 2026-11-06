// VIRTUAL LOFT — the agents' hands. Local tools for the OpenAI Agents SDK,
// all confined to ONE workspace folder:
//   read_file, list_files, search_files  (read)
//   write_file, edit_file, run_command   (change things: CEO must grant)
//   fetch_url                            (public http(s) only)
//   check_page                           (open the project's page from this task's folder in a real browser)
// Web *search* comes from the model side, e.g. an OpenRouter id ending in ":online".

import { exec } from "node:child_process";
import dns from "node:dns/promises";
import { promises as fs } from "node:fs";
import net from "node:net";
import path from "node:path";
import { tool } from "@openai/agents";

export const TOOL_NAMES = ["read_file", "list_files", "search_files", "write_file", "edit_file", "run_command", "fetch_url", "check_page"];
export const WRITE_TOOLS = ["write_file", "edit_file", "run_command"];

const MAX_OUT = 20_000;
const SKIP_DIRS = new Set([".git", "node_modules", "Library", "Temp", "obj", ".venv", "__pycache__"]);
// Never hand the agents' own credentials to shell commands they write.
const SECRET_ENV = /KEY|TOKEN|SECRET|PASSWORD|SUPABASE/i;

const clip = (s, n = MAX_OUT) => (s.length > n ? `${s.slice(0, n)}\n…[truncated ${s.length - n} chars]` : s);

export function safePath(workspace, rel = ".") {
  const root = path.resolve(workspace);
  const p = path.resolve(root, String(rel));
  if (p !== root && !p.startsWith(root + path.sep)) throw new Error(`path '${rel}' is outside the workspace`);
  return p;
}

// Symlinks could still point out of the workspace: check the real path too.
export async function realInside(workspace, p) {
  const root = await fs.realpath(path.resolve(workspace));
  let real;
  try {
    real = await fs.realpath(p);
  } catch {
    real = path.join(await fs.realpath(path.dirname(p)).catch(() => root), path.basename(p));
  }
  if (real !== root && !real.startsWith(root + path.sep)) throw new Error("path resolves outside the workspace");
  return p;
}

async function walk(root, dir, out, limit) {
  for (const e of await fs.readdir(dir, { withFileTypes: true })) {
    if (out.length >= limit) return;
    if (SKIP_DIRS.has(e.name)) continue;
    const full = path.join(dir, e.name);
    if (e.isDirectory()) await walk(root, full, out, limit);
    else out.push(path.relative(root, full));
  }
}

function isPrivateIp(ip) {
  if (net.isIPv4(ip)) {
    const [a, b] = ip.split(".").map(Number);
    return a === 10 || a === 127 || a === 0 || (a === 169 && b === 254) || (a === 172 && b >= 16 && b <= 31) || (a === 192 && b === 168) || (a === 100 && b >= 64 && b <= 127);
  }
  const v = ip.toLowerCase();
  return v === "::1" || v === "::" || v.startsWith("fc") || v.startsWith("fd") || v.startsWith("fe80") || v.startsWith("::ffff:127.") || v.startsWith("::ffff:10.") || v.startsWith("::ffff:192.168.");
}

export async function assertPublicUrl(raw) {
  const u = new URL(raw);
  if (!["http:", "https:"].includes(u.protocol)) throw new Error("only http(s) URLs");
  const host = u.hostname.replace(/^\[|\]$/g, "");
  const addrs = net.isIP(host) ? [host] : (await dns.lookup(host, { all: true })).map((a) => a.address);
  if (!addrs.length || addrs.some(isPrivateIp)) throw new Error("local/private network addresses are blocked");
  return u;
}

// The raw implementations (exported for tests). Each returns a string for the model.
export function impl(workspace, signal) {
  return {
    async read_file({ path: rel, offset = 0, limit = 400 }) {
      const p = await realInside(workspace, safePath(workspace, rel));
      const lines = (await fs.readFile(p, "utf8")).split("\n");
      const start = Math.max(0, Number(offset) || 0);
      const slice = lines.slice(start, start + Math.min(Number(limit) || 400, 2000));
      return clip(slice.map((l, i) => `${start + i + 1}\t${l}`).join("\n")) + (start + slice.length < lines.length ? `\n…(${lines.length} lines total)` : "");
    },
    async list_files({ path: rel = ".", contains = "" }) {
      const root = safePath(workspace, rel);
      const out = [];
      await walk(root, root, out, 2000);
      const hits = contains ? out.filter((f) => f.includes(contains)) : out;
      return hits.slice(0, 500).join("\n") || "(no files)";
    },
    async search_files({ pattern, path: rel = "." }) {
      const re = new RegExp(pattern);
      const root = safePath(workspace, rel);
      const files = [];
      await walk(root, root, files, 5000);
      const hits = [];
      for (const f of files) {
        if (hits.length >= 200) break;
        let text;
        try {
          text = await fs.readFile(path.join(root, f), "utf8");
        } catch {
          continue;
        }
        if (text.includes("\u0000")) continue; // binary
        text.split("\n").forEach((line, i) => {
          if (hits.length < 200 && re.test(line)) hits.push(`${f}:${i + 1}: ${line.trim().slice(0, 200)}`);
        });
      }
      return hits.join("\n") || "(no matches)";
    },
    async write_file({ path: rel, content }) {
      const p = safePath(workspace, rel);
      await fs.mkdir(path.dirname(p), { recursive: true });
      await realInside(workspace, p);
      await fs.writeFile(p, String(content), "utf8");
      return `wrote ${rel} (${String(content).length} chars)`;
    },
    async edit_file({ path: rel, old_text, new_text }) {
      const p = await realInside(workspace, safePath(workspace, rel));
      const text = await fs.readFile(p, "utf8");
      const n = text.split(old_text).length - 1;
      if (n !== 1) throw new Error(`old_text must appear exactly once in ${rel} (found ${n})`);
      await fs.writeFile(p, text.replace(old_text, () => new_text), "utf8");
      return `edited ${rel}`;
    },
    run_command({ command, timeout_sec = 120 }) {
      const env = Object.fromEntries(Object.entries(process.env).filter(([k]) => !SECRET_ENV.test(k)));
      const timeout = Math.min(Math.max(Number(timeout_sec) || 120, 1), 600) * 1000;
      return new Promise((resolve) => {
        const child = exec(String(command), { cwd: path.resolve(workspace), env, timeout, maxBuffer: 10 * 1024 * 1024, signal },
          (err, stdout, stderr) => {
            const code = err ? (err.killed ? "killed (timeout/stop)" : err.code ?? 1) : 0;
            resolve(clip(`exit ${code}\n${stdout}${stderr ? `\n[stderr]\n${stderr}` : ""}`));
          });
        child.stdin?.end();
      });
    },
    async fetch_url({ url }) {
      const u = await assertPublicUrl(url);
      const res = await fetch(u, { signal, redirect: "error", headers: { "user-agent": "virtual-loft-agent" } });
      const type = res.headers.get("content-type") ?? "";
      let body = await res.text();
      if (type.includes("html")) body = body.replace(/<(script|style)[\s\S]*?<\/\1>/gi, "").replace(/<[^>]+>/g, " ").replace(/\s+/g, " ");
      return clip(`HTTP ${res.status} ${type}\n${body}`);
    },
  };
}

const SCHEMAS = {
  read_file: ["Read a text file in the workspace (returns numbered lines).",
    { path: { type: "string" }, offset: { type: "number", description: "first line (0-based)" }, limit: { type: "number" } }, ["path"]],
  list_files: ["List files under a workspace folder (recursive, skips node_modules/.git).",
    { path: { type: "string" }, contains: { type: "string", description: "only paths containing this text" } }, []],
  search_files: ["Search file contents with a regular expression; returns file:line: text.",
    { pattern: { type: "string" }, path: { type: "string" } }, ["pattern"]],
  write_file: ["Create or overwrite a file in the workspace.",
    { path: { type: "string" }, content: { type: "string" } }, ["path", "content"]],
  edit_file: ["Replace text in a file. old_text must appear exactly once.",
    { path: { type: "string" }, old_text: { type: "string" }, new_text: { type: "string" } }, ["path", "old_text", "new_text"]],
  run_command: ["Run a shell command in the workspace (tests, builds, git). Returns exit code + output.",
    { command: { type: "string" }, timeout_sec: { type: "number" } }, ["command"]],
  fetch_url: ["Fetch a public web page or API (http/https) and return its text.",
    { url: { type: "string" } }, ["url"]],
  check_page: ["Open the project's web page from YOUR folder in a real browser (dev server started for you). Returns HTTP status, title, console errors and visible text. Use it to verify your work.",
    { path: { type: "string", description: "page path, default /" } }, []],
};

// One line for the animal's label: "edit_file src/auth.ts", "run_command npm test".
export function describeCall(name, args = {}) {
  const arg = args.path ?? args.command ?? args.pattern ?? args.url ?? args.contains ?? "";
  return `${name} ${String(arg)}`.trim().replace(/\s+/g, " ").slice(0, 120);
}

// SDK tools for one agent: only the names it was granted.
export function makeTools(workspace, names, { signal, onProgress = () => {}, checkPage = null } = {}) {
  const fns = impl(workspace, signal);
  fns.check_page = async ({ path: p = "/" }) => {
    if (!checkPage) return "check_page is not available for this project (no preview configured).";
    return checkPage(String(p || "/"));
  };
  return names.filter((n) => SCHEMAS[n]).map((name) => {
    const [description, properties, required] = SCHEMAS[name];
    return tool({
      name,
      description,
      strict: false, // works with non-OpenAI models behind OpenRouter
      parameters: { type: "object", properties, required, additionalProperties: false },
      async execute(input) {
        const args = typeof input === "string" ? JSON.parse(input || "{}") : (input ?? {});
        onProgress(describeCall(name, args));
        try {
          return await fns[name](args);
        } catch (err) {
          return `ERROR: ${String(err?.message ?? err).slice(0, 500)}`; // let the model recover
        }
      },
    });
  });
}
