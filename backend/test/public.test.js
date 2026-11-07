// A public server: strangers hold the token, so nothing they do may run commands or reach inside.
import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { mkdtempSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { after, before, test } from "node:test";
import { resetDue } from "../src/orchestrator.js";
import { makeTools } from "../src/tools.js";

const here = path.dirname(new URL(import.meta.url).pathname);
const TOKEN = "p".repeat(24);
const kids = [];

async function server(env = {}) {
  const tmp = mkdtempSync(path.join(os.tmpdir(), "loft-public-"));
  const port = 45000 + Math.floor(Math.random() * 2000);
  const c = spawn(process.execPath, [path.join(here, "../src/server.js"), "--port", String(port)], {
    env: {
      PATH: process.env.PATH, HOME: tmp, AGENT_API_KEY: "t", AGENT_BASE_URL: "http://127.0.0.1:9", AGENT_MODEL: "test/m", LOFT_TOKEN: TOKEN,
      LOFT_DATA_DIR: path.join(tmp, "data"), WORKSPACE_DIR: path.join(tmp, "ws"), PROJECTS_DIR: path.join(tmp, "p"), LOFT_SUGGESTIONS: "0", LOFT_PUBLIC: "1", ...env,
    },
    stdio: "ignore",
  });
  kids.push(c);
  const base = `http://127.0.0.1:${port}`;
  for (let i = 0; i < 100; i++) {
    if (await fetch(`${base}/health`).then((r) => r.ok, () => false)) break;
    await new Promise((r) => setTimeout(r, 50));
  }
  const call = (m, p, body) => fetch(`${base}${p}`, { method: m, headers: { "content-type": "application/json", Authorization: `Bearer ${TOKEN}` }, body: body && JSON.stringify(body) });
  return { c, call };
}
let s;
before(async () => { s = await server(); });
after(() => kids.forEach((k) => k.kill()));

test("public: agents have no shell, whatever their role asks for", () => {
  process.env.LOFT_PUBLIC = "1";
  const names = makeTools("/tmp", ["read_file", "run_command"]).map((t) => t.name);
  delete process.env.LOFT_PUBLIC;
  assert.deepEqual(names, ["read_file"]);
  assert.ok(makeTools("/tmp", ["run_command"]).length === 1, "local use keeps it");
});

test("public: no opening folders, and no commands through a project's dev or setup", async () => {
  assert.equal((await s.call("POST", "/projects", { path: "/etc", init: true })).status, 403);
  assert.equal((await s.call("POST", "/projects", { path: "/tmp", dev: "curl evil", setup: "curl evil" })).status, 403);
});

test("public: the workspace can't be removed", async () => {
  assert.equal((await s.call("DELETE", "/projects/workspace")).status, 403);
  assert.ok((await (await s.call("GET", "/projects")).json()).projects.some((p) => p.id === "workspace"));
});

test("public: the browser can't open anything inside the server", async () => {
  for (const url of ["http://127.0.0.1:4101/state", "http://localhost:8080", "http://10.0.0.1", "http://169.254.169.254/", "http://metadata.google.internal/"]) {
    assert.equal((await s.call("POST", "/preview/sessions", { url })).status, 403, url);
  }
});

test("reset: due after the time, never while an agent is working", () => {
  assert.equal(resetDue({ uptimeHours: 7, hours: 6, busy: false }), true);
  assert.equal(resetDue({ uptimeHours: 7, hours: 6, busy: true }), false);
  assert.equal(resetDue({ uptimeHours: 5, hours: 6, busy: false }), false);
  assert.equal(resetDue({ uptimeHours: 99, hours: 0, busy: false }), false, "off by default");
});

test("reset: the server restarts itself, cleanly, so the host brings up a fresh room", async () => {
  const { c } = await server({ LOFT_RESET_HOURS: "0.0002" });
  const code = await new Promise((r) => { c.once("exit", r); setTimeout(() => r("still running"), 6000); });
  assert.equal(code, 0);
});
