// Runner + tools against a FAKE OpenAI-compatible server (no network, no key):
// proves the real Agents SDK loop calls our tools, uses each agent's own
// model id, reports usage, and that tools can't escape the workspace.
import assert from "node:assert/strict";
import { mkdtemp, readFile, writeFile } from "node:fs/promises";
import http from "node:http";
import os from "node:os";
import path from "node:path";
import { after, before, test } from "node:test";

const requests = [];
let script = [];
const server = http.createServer((req, res) => {
  let body = "";
  req.on("data", (c) => (body += c));
  req.on("end", () => {
    const json = JSON.parse(body || "{}");
    requests.push(json);
    const step = script.shift() ?? { content: "done" };
    const message = step.call
      ? { role: "assistant", content: null, tool_calls: [{ id: `c${requests.length}`, type: "function", function: { name: step.call, arguments: JSON.stringify(step.args) } }] }
      : { role: "assistant", content: step.content };
    const reply = () => {
      res.writeHead(200, { "content-type": "application/json" });
      res.end(JSON.stringify({
        id: `r${requests.length}`, object: "chat.completion", created: 0, model: json.model,
        choices: [{ index: 0, message, finish_reason: step.call ? "tool_calls" : "stop" }],
        usage: { prompt_tokens: 100, completion_tokens: 10, total_tokens: 110 },
      }));
    };
    step.delayMs ? setTimeout(reply, step.delayMs) : reply();
  });
});

let agents, tools, ws;
before(async () => {
  await new Promise((r) => server.listen(0, "127.0.0.1", r));
  process.env.LLM_BASE_URL = `http://127.0.0.1:${server.address().port}/v1`;
  process.env.LLM_API_KEY = "test-key";
  process.env.SUPER_SECRET_TOKEN = "do-not-leak";
  agents = await import("../src/agents.js");
  tools = await import("../src/tools.js");
  ws = await mkdtemp(path.join(os.tmpdir(), "loft-ws-"));
});
after(() => server.close());

const coder = (model) => ({ name: "Builder", model, role: { title: "Coder", instructions: "code", tools: ["write_file", "read_file", "run_command"] } });

test("agent loop: model calls our tools in the workspace, with the agent's own model id", async () => {
  requests.length = 0;
  script = [
    { call: "write_file", args: { path: "src/hello.txt", content: "hi there" } },
    { call: "run_command", args: { command: "cat src/hello.txt && echo key=$SUPER_SECRET_TOKEN" } },
    { content: "PASS: wrote src/hello.txt" },
  ];
  const progress = [];
  const out = await agents.runTask({ agent: coder("qwen/qwen3-coder"), title: "make hello", workspace: ws, onProgress: (l) => progress.push(l) });
  assert.equal(out.text, "PASS: wrote src/hello.txt");
  assert.equal(await readFile(path.join(ws, "src/hello.txt"), "utf8"), "hi there");
  assert.deepEqual(progress, ["write_file src/hello.txt", "run_command cat src/hello.txt && echo key=$SUPER_SECRET_TOKEN"]);
  assert.ok(requests.every((r) => r.model === "qwen/qwen3-coder"), "every call used the agent's model id");
  assert.deepEqual(requests[0].tools.map((t) => t.function.name).sort(), ["read_file", "run_command", "write_file"]);
  const cmdResult = JSON.stringify(requests[2].messages);
  assert.match(cmdResult, /hi there/, "command output fed back to the model");
  assert.doesNotMatch(cmdResult, /do-not-leak/, "secrets stripped from the command env");
  assert.equal(out.usage.requests, 3);
  assert.equal(out.usage.inputTokens, 300);
  assert.equal(out.usage.outputTokens, 30);
});

test("a role only gets the tools it was granted", async () => {
  requests.length = 0;
  script = [{ content: "ok" }];
  await agents.runTask({ agent: { ...coder("x/y"), role: { title: "Reviewer", instructions: "r", tools: ["read_file"] } }, title: "t", workspace: ws });
  assert.deepEqual(requests[0].tools.map((t) => t.function.name), ["read_file"]);
});

test("stop aborts a running agent", async () => {
  script = [{ content: "late", delayMs: 2000 }];
  const ac = new AbortController();
  const p = agents.runTask({ agent: coder("x/y"), title: "slow", workspace: ws, abortController: ac });
  setTimeout(() => ac.abort(), 100);
  await assert.rejects(p);
});

test("tools: workspace jail, exact edits, blocked private URLs", async () => {
  const f = tools.impl(ws);
  await assert.rejects(f.read_file({ path: "../../etc/passwd" }), /outside the workspace/);
  await assert.rejects(f.write_file({ path: "/tmp/evil.txt", content: "x" }), /outside the workspace/);
  await writeFile(path.join(ws, "a.txt"), "one two two");
  await assert.rejects(f.edit_file({ path: "a.txt", old_text: "two", new_text: "2" }), /exactly once in a\.txt \(found 2\)/);
  await f.edit_file({ path: "a.txt", old_text: "one", new_text: "1" });
  assert.equal(await readFile(path.join(ws, "a.txt"), "utf8"), "1 two two");
  assert.match(await f.search_files({ pattern: "two" }), /a\.txt:1:/);
  assert.match(await f.list_files({}), /a\.txt/);
  await assert.rejects(tools.assertPublicUrl("http://127.0.0.1:4101/state"), /blocked/);
  await assert.rejects(tools.assertPublicUrl("http://192.168.1.5/"), /blocked/);
  await assert.rejects(tools.assertPublicUrl("file:///etc/passwd"), /only http/);
  assert.match(await f.run_command({ command: "pwd" }), new RegExp(path.basename(ws)));
});

test("AI role draft: unknown tools dropped, write tools kept for the CEO to see", async () => {
  script = [{ content: 'Sure! {"title":"A11y Checker","instructions":"Check pages for accessibility issues.","tools":["read_file","search_files","rm_rf","run_command"]}' }];
  const role = await agents.draftRole("check our pages for accessibility");
  assert.deepEqual(role, { title: "A11y Checker", instructions: "Check pages for accessibility issues.", tools: ["read_file", "search_files", "run_command"] });
  script = [{ content: "no json at all" }];
  const fallback = await agents.draftRole("summarize logs");
  assert.deepEqual(fallback.tools, ["read_file", "list_files", "search_files"], "safe read-only fallback");
});
