// VIRTUAL LOFT — see what the team built, before you approve it.
//
// For a task in review: run the project's dev server FROM THAT TASK'S WORKTREE
// on a free port (or a tiny built-in static server when the project has no dev
// command), wait until it answers, then Playwright takes a desktop and a phone
// screenshot. The server keeps running while the task is in review, so
// "open live" works; it stops on approve/reject/stop.
// checkPage() gives agents the same browser as a text report (check_page tool).

import { spawn } from "node:child_process";
import { createReadStream, promises as fs } from "node:fs";
import http from "node:http";
import net from "node:net";
import path from "node:path";

const TYPES = { ".html": "text/html", ".css": "text/css", ".js": "text/javascript", ".mjs": "text/javascript", ".json": "application/json",
  ".svg": "image/svg+xml", ".png": "image/png", ".jpg": "image/jpeg", ".jpeg": "image/jpeg", ".gif": "image/gif", ".webp": "image/webp", ".ico": "image/x-icon" };

export function freePort() {
  return new Promise((resolve, reject) => {
    const s = net.createServer();
    s.listen(0, "127.0.0.1", () => {
      const { port } = s.address();
      s.close(() => resolve(port));
    });
    s.on("error", reject);
  });
}

// Plain static server for "static" projects (index.html in the worktree).
function staticServer(root, port) {
  const server = http.createServer(async (req, res) => {
    try {
      let rel = decodeURIComponent(new URL(req.url, "http://x").pathname);
      if (rel.endsWith("/")) rel += "index.html";
      const p = path.resolve(root, "." + rel);
      if (!p.startsWith(path.resolve(root) + path.sep)) throw new Error("outside");
      await fs.access(p);
      const type = TYPES[path.extname(p)] ?? "application/octet-stream";
      // utf-8 for text: AI-written pages often omit <meta charset> and emoji/accents would turn into mojibake
      res.writeHead(200, { "content-type": /^(text|application\/(json|javascript))/.test(type) ? `${type}; charset=utf-8` : type, "cache-control": "no-store" });
      createReadStream(p).pipe(res);
    } catch {
      res.writeHead(404, { "content-type": "text/plain" });
      res.end("not found");
    }
  });
  return new Promise((resolve) => server.listen(port, "127.0.0.1", () => resolve({ stop: () => server.close() })));
}

async function waitForUrl(url, ms = 90_000) {
  const end = Date.now() + ms;
  let last = "";
  while (Date.now() < end) {
    try {
      const r = await fetch(url, { signal: AbortSignal.timeout(3000) });
      if (r.status < 500) return r.status;
      last = `HTTP ${r.status}`;
    } catch (e) {
      last = e.message;
    }
    await new Promise((r) => setTimeout(r, 500));
  }
  throw new Error(`preview server never answered at ${url} (${last})`);
}

export function createPreviews({ dir, browserLauncher = null }) {
  const servers = new Map(); // key -> { url, stop, log }
  let browser;

  async function getBrowser() {
    if (!browser) {
      const launch = browserLauncher ?? (async () => (await import("playwright")).chromium.launch());
      browser = await launch();
    }
    return browser;
  }

  // Start (or reuse) the dev server for a worktree. Returns { url }.
  async function serve(key, project, worktree) {
    if (servers.has(key)) return servers.get(key);
    const port = await freePort();
    const url = (project.url || "http://127.0.0.1:{port}/").replaceAll("{port}", String(port));
    let entry;
    if (project.dev) {
      const cmd = project.dev.replaceAll("{port}", String(port));
      const child = spawn(cmd, { cwd: worktree, shell: true, detached: true, env: { ...process.env, PORT: String(port), BROWSER: "none" } });
      let log = "";
      const keep = (d) => (log = (log + d).slice(-4000));
      child.stdout.on("data", keep);
      child.stderr.on("data", keep);
      entry = {
        url,
        get log() {
          return log;
        },
        stop: () => {
          try {
            process.kill(-child.pid, "SIGTERM"); // whole process group (npm -> vite -> esbuild)
          } catch {
            /* already gone */
          }
        },
      };
      child.on("exit", (code) => keep(`\n[dev server exited ${code}]`));
    } else {
      const s = await staticServer(worktree, port);
      entry = { url, log: "", stop: s.stop };
    }
    servers.set(key, entry);
    try {
      await waitForUrl(url);
    } catch (e) {
      stop(key);
      throw new Error(`${e.message}\n${entry.log.slice(-800)}`);
    }
    return entry;
  }

  function stop(key) {
    const s = servers.get(key);
    if (s) {
      s.stop();
      servers.delete(key);
    }
  }

  // Screenshots for the CEO: desktop + phone. -> { url, desktop, mobile, title }
  async function capture(taskId, project, worktree) {
    const { url } = await serve(taskId, project, worktree);
    const b = await getBrowser();
    await fs.mkdir(dir, { recursive: true });
    const shots = {};
    let title = "";
    for (const [name, viewport] of [["desktop", { width: 1280, height: 800 }], ["mobile", { width: 390, height: 844 }]]) {
      const page = await b.newPage({ viewport, deviceScaleFactor: name === "mobile" ? 2 : 1 });
      try {
        await page.goto(url, { waitUntil: "networkidle", timeout: 20_000 }).catch(() => page.waitForLoadState("load"));
        await page.waitForTimeout(400); // fonts/animations settle
        title ||= await page.title();
        const file = `${taskId}-${name}.png`;
        await page.screenshot({ path: path.join(dir, file) });
        shots[name] = file;
      } finally {
        await page.close();
      }
    }
    return { url, title, ...shots };
  }

  // Agent tool: open the page from the agent's own worktree, report as text.
  async function checkPage(key, project, worktree, pagePath = "/") {
    const { url } = await serve(key, project, worktree);
    const target = new URL(String(pagePath || "/"), url).toString();
    const b = await getBrowser();
    const page = await b.newPage({ viewport: { width: 1280, height: 800 } });
    const errors = [];
    page.on("console", (m) => m.type() === "error" && errors.push(m.text()));
    page.on("pageerror", (e) => errors.push(e.message));
    try {
      const res = await page.goto(target, { waitUntil: "networkidle", timeout: 20_000 }).catch(() => null);
      const text = (await page.evaluate(() => document.body?.innerText ?? "").catch(() => "")).replace(/\s+/g, " ").trim();
      return [
        `URL ${target}`,
        `HTTP ${res ? res.status() : "no response"}`,
        `title: ${await page.title()}`,
        `console errors: ${errors.length ? errors.slice(0, 10).join(" | ") : "none"}`,
        `visible text: ${text.slice(0, 1500) || "(empty page)"}`,
      ].join("\n");
    } finally {
      await page.close();
    }
  }

  async function close() {
    for (const k of [...servers.keys()]) stop(k);
    if (browser) await browser.close().catch(() => {});
    browser = null;
  }

  return { serve, stop, capture, checkPage, close, url: (key) => servers.get(key)?.url ?? null };
}
