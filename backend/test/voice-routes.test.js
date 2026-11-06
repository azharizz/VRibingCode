// The real server process over HTTP: /voice + /voice/text, auth, CORS, 10 MB path,
// with a fake LLM and a fake whisper binary (no network, no key, no model).
import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { after, before, test } from "node:test";
import { fakeLlm, wav } from "./helpers.js";

const here = path.dirname(new URL(import.meta.url).pathname);
const TOKEN = "t".repeat(24);
let llm, child, base, tmp;
let reply = () => '{"action":"scene","sceneId":"galaxy"}';

before(async () => {
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), "loft-voice-"));
  llm = await fakeLlm((req) => reply(req));
  const whisper = path.join(tmp, "whisper");
  fs.writeFileSync(whisper, "#!/bin/sh\necho ' go to space'\n", { mode: 0o755 });
  fs.writeFileSync(path.join(tmp, "model.bin"), "x");
  const port = 41000 + Math.floor(Math.random() * 2000);
  base = `http://127.0.0.1:${port}`;
  child = spawn(process.execPath, [path.join(here, "../src/server.js"), "--port", String(port)], {
    env: {
      PATH: process.env.PATH, HOME: tmp, // HOME/cwd-independent: no real .env is read because every value is set here
      AGENT_API_KEY: "test", AGENT_BASE_URL: llm.url, AGENT_MODEL: "test/model",
      LOFT_TOKEN: TOKEN, LOFT_DATA_DIR: path.join(tmp, "data"), WORKSPACE_DIR: path.join(tmp, "ws"), PROJECTS_DIR: path.join(tmp, "projects"),
      WHISPER_BIN: whisper, STT_MODEL_PATH: path.join(tmp, "model.bin"),
    },
    stdio: "ignore",
  });
  for (let i = 0; i < 100; i++) {
    if (await fetch(`${base}/health`).then((r) => r.ok, () => false)) return;
    await new Promise((r) => setTimeout(r, 100));
  }
  throw new Error("server did not start");
});
after(() => {
  child?.kill();
  llm?.close();
});

const auth = { Authorization: `Bearer ${TOKEN}` };
const text = (body, headers = auth) => fetch(`${base}/voice/text`, { method: "POST", headers: { "content-type": "application/json", ...headers }, body: JSON.stringify(body) });
const audio = (buf, q = "", headers = auth) => fetch(`${base}/voice${q}`, { method: "POST", headers: { "content-type": "audio/wav", ...headers }, body: buf });

test("auth and CORS still apply to the voice routes", async () => {
  assert.equal((await text({ text: "hi" }, {})).status, 401);
  assert.equal((await audio(wav(1), "", {})).status, 401);
  const pre = await fetch(`${base}/voice`, { method: "OPTIONS" });
  assert.equal(pre.status, 204);
  assert.equal(pre.headers.get("access-control-allow-origin"), "*");
  assert.equal((await text({ text: "hi" })).headers.get("access-control-allow-origin"), "*");
});

test("POST /voice/text: contract fields; agent roster reaches the model", async () => {
  const r = await text({ text: "switch to space please", scene: "city" });
  assert.equal(r.status, 200);
  const j = await r.json();
  assert.deepEqual([j.transcript, j.intent, j.needsConfirm, j.risky], ["switch to space please", { action: "scene", sceneId: "galaxy" }, false, false]);
  assert.match(j.summary, /galaxy/);
  const sent = llm.calls.at(-1).messages.at(-1).content;
  assert.match(sent, /id=coder-\d+ name=Builder animal=fox/);
});

test("POST /voice/text: validation errors and model garbage", async () => {
  assert.equal((await text({})).status, 400);
  assert.equal((await text({ text: "x", agentId: "ghost" })).status, 404);
  assert.equal((await text({ text: "x", projectId: "ghost" })).status, 404);
  assert.equal((await text({ text: "x", scene: "mars" })).status, 400);
  const bad = await fetch(`${base}/voice/text`, { method: "POST", headers: auth, body: "{not json" });
  assert.equal(bad.status, 400);
  reply = () => "sorry, as a language model I cannot";
  const j = await (await text({ text: "do something" })).json();
  assert.equal(j.intent.action, "none");
  reply = () => '{"action":"scene","sceneId":"galaxy"}';
});

test("POST /voice: WAV -> whisper -> intent; pointed agent is validated", async () => {
  reply = () => '{"action":"scene","sceneId":"galaxy"}';
  const r = await audio(wav(1), "?scene=beach");
  assert.equal(r.status, 200);
  const j = await r.json();
  assert.equal(j.transcript, "go to space");
  assert.equal(j.intent.sceneId, "galaxy");
  assert.ok(j.sttMs >= 0 && j.audioSeconds === 1);
  assert.equal((await audio(wav(1), "?agentId=ghost")).status, 404);
  assert.equal((await audio(wav(1), "?scene=mars")).status, 400);
});

test("POST /voice: bad audio is a clean error, silence is a polite none", async () => {
  assert.equal((await audio(Buffer.alloc(0))).status, 400);
  assert.equal((await audio(Buffer.from("this is not audio, just a long enough text string, honest"))).status, 415);
  assert.equal((await audio(wav(1, { rate: 44100 }))).status, 415);
  const j = await (await audio(wav(1, { amp: 0 }))).json();
  assert.equal(j.intent.action, "none");
  assert.equal(j.transcript, "");
});

test("audio has its own 10 MB limit; JSON keeps 1 MB", async () => {
  const ok = await audio(wav(60)); // ~1.9 MB: far over the JSON limit, fine for audio
  assert.equal(ok.status, 200);
  const big = await audio(Buffer.alloc(10 * 1024 * 1024 + 10, 1));
  assert.equal(big.status, 413);
  const bigJson = await text({ text: "x".repeat(1_100_000) });
  assert.equal(bigJson.status, 413);
});
