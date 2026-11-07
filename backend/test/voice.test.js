// Voice: code words, intent validation (fake LLM), STT providers (fake binary / fake fetch).
import assert from "node:assert/strict";
import { EventEmitter } from "node:events";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { PRESET_ROLES } from "../src/agents.js";
import { cleanTranscript, createStt, inspectWav } from "../src/stt.js";
import { createVoice, fixCodeWords, normalizeIntent, summarize } from "../src/voice.js";
import { wav } from "./helpers.js";
import { extractJson } from "../src/llm.js";

const agent = (id, name, skin, title) => ({ id, name, skin, state: "idle", taskId: null, role: { title, instructions: "x", tools: [] } });
const AGENTS = [agent("scout-1", "Scout", "owl", "Researcher"), agent("builder-2", "Builder", "fox", "Coder"), agent("keeper-3", "Keeper", "cat", "Reviewer")];
const REVIEW = [{ id: "t7", agentId: "builder-2", title: "add /health", status: "review" }, { id: "t9", agentId: "keeper-3", title: "check page", status: "review" }];
const ctx = (over = {}) => ({ agents: AGENTS, projects: [{ id: "shop", name: "Shop" }], roles: PRESET_ROLES, review: REVIEW, pointed: null, ...over });

test("fixCodeWords: spoken code becomes code (English + Indonesian)", () => {
  assert.equal(fixCodeWords("add a slash health route"), "add a /health route");
  assert.equal(fixCodeWords("open src slash app dot js now"), "open src /app.js now");
  assert.equal(fixCodeWords("tambah garis miring api garis miring users"), "tambah /api/users");
  assert.equal(fixCodeWords("edit dot env please"), "edit .env please");
  assert.equal(fixCodeWords("run my underscore script with dash dash verbose"), "run my_script with --verbose");
  assert.equal(fixCodeWords("  titik js   "), ".js");
  assert.equal(fixCodeWords("dot matrix printer"), "dot matrix printer", "only known extensions");
  assert.equal(fixCodeWords(undefined), "");
});

test("normalizeIntent: ids are checked against the real roster, queue, projects, scenes", () => {
  assert.deepEqual(normalizeIntent({ action: "task", agentId: "fox", title: "fix it" }, ctx()), { action: "task", agentId: "builder-2", title: "fix it" }, "animal and name resolve to the id");
  assert.deepEqual(normalizeIntent({ action: "task", agentId: "ghost-9", title: "x" }, ctx()).action, "none", "unknown agent, nobody pointed");
  assert.deepEqual(normalizeIntent({ action: "task", agentId: "ghost-9", title: "x" }, ctx({ pointed: "keeper-3" })).agentId, "keeper-3", "falls back to the pointed agent");
  assert.deepEqual(normalizeIntent({ action: "goal", goal: "ship the site" }, ctx({ pointed: "scout-1" })), { action: "task", agentId: "scout-1", title: "ship the site" }, "bare instruction + pointed = task");
  assert.deepEqual(normalizeIntent({ action: "goal", goal: "ship the site" }, ctx()), { action: "goal", goal: "ship the site" });
  assert.equal(normalizeIntent({ action: "scene", sceneId: "mars" }, ctx()).action, "none");
  assert.equal(normalizeIntent({ action: "scene", sceneId: "Beach" }, ctx()).sceneId, "beach");
  assert.equal(normalizeIntent({ action: "project", projectId: "SHOP" }, ctx()).projectId, "shop");
  assert.equal(normalizeIntent({ action: "project", projectId: "nope" }, ctx()).action, "none");
  assert.equal(normalizeIntent({ action: "remove", agentId: "nope" }, ctx()).action, "none");
  assert.deepEqual(normalizeIntent({ action: "remove", agentId: "nope" }, ctx({ pointed: "scout-1" })), { action: "remove", agentId: "scout-1" });
  assert.equal(normalizeIntent({ action: "stop", agentId: "nope" }, ctx()).action, "none", "never turn a bad name into stop-all");
  assert.deepEqual(normalizeIntent({ action: "stop" }, ctx()), { action: "stop" });
});

test("normalizeIntent: approve/reject target the review queue; spawn fields are sanitized", () => {
  assert.equal(normalizeIntent({ action: "approve" }, ctx()).taskId, "t9", "latest waiting task");
  assert.equal(normalizeIntent({ action: "approve" }, ctx({ pointed: "builder-2" })).taskId, "t7", "pointed agent's task");
  assert.deepEqual(normalizeIntent({ action: "reject", feedback: "  too dark " }, ctx()), { action: "reject", taskId: "t9", feedback: "too dark" });
  assert.equal(normalizeIntent({ action: "approve" }, ctx({ review: [] })).action, "none");
  assert.deepEqual(
    normalizeIntent({ action: "spawn", name: "A".repeat(40), roleId: "wizard", skin: "dragon", model: "bad model!", description: "write docs" }, ctx()),
    { action: "spawn", name: "A".repeat(24), description: "write docs" }, "unknown role, skin and model are dropped");
  assert.deepEqual(normalizeIntent({ action: "spawn", roleId: "builder", skin: "panda", model: "qwen/qwen3-coder" }, ctx()), { action: "spawn", roleId: "builder", skin: "panda", model: "qwen/qwen3-coder" });
  assert.match(normalizeIntent({ action: "spawn" }, ctx({ agents: Array(20).fill(AGENTS[0]) })).reply, /full/);
  assert.deepEqual(normalizeIntent({ action: "party", on: false }, ctx()), { action: "party", on: false });
  assert.deepEqual(normalizeIntent({ action: "party" }, ctx()), { action: "party", on: true });
});

test("normalizeIntent is total: junk in, action none out", () => {
  for (const junk of [null, undefined, 5, "text", [], {}, { action: "launch_missiles" }, { action: 7 }, { action: "task" }, { action: "task", title: "x" }, { action: "scene" }]) {
    const i = normalizeIntent(junk, ctx());
    assert.equal(i.action, "none", JSON.stringify(junk));
    assert.ok(i.reply.length > 5, "helpful reply");
  }
});

const voiceWith = (ask, over = {}) => createVoice({ snapshot: () => ({ agents: AGENTS, projects: [{ id: "shop", name: "Shop" }], roles: PRESET_ROLES, tasks: REVIEW }), ask, extract: extractJson, ...over });

test("handleText: confirm/risky flags, summary, prompt carries roster + review queue", async () => {
  let prompt = "";
  const v = voiceWith(async (_s, p) => ((prompt = p), '<think>hmm</think>```json\n{"action":"approve"}\n```'));
  const r = await v.handleText({ text: "approve it", agentId: "builder-2", scene: "beach" });
  assert.deepEqual(r.intent, { action: "approve", taskId: "t7" });
  assert.equal(r.risky, true);
  assert.equal(r.needsConfirm, true);
  assert.match(r.summary, /Approve "add \/health"/);
  for (const must of ["builder-2", "fox", "Researcher", "t9", "shop", "Pointed-at agent", "Current scene: beach"]) assert.ok(prompt.includes(must), `prompt has ${must}`);

  const flags = async (obj) => {
    const out = await voiceWith(async () => JSON.stringify(obj)).handleText({ text: "x" });
    return [out.needsConfirm, out.risky];
  };
  assert.deepEqual(await flags({ action: "scene", sceneId: "city" }), [false, false]);
  assert.deepEqual(await flags({ action: "party" }), [false, false]);
  assert.deepEqual(await flags({ action: "selectNext" }), [false, false]);
  assert.deepEqual(await flags({ action: "none", reply: "hi" }), [false, false]);
  assert.deepEqual(await flags({ action: "task", agentId: "fox", title: "t" }), [true, false]);
  assert.deepEqual(await flags({ action: "remove", agentId: "fox" }), [true, true]);
  assert.deepEqual(await flags({ action: "reject" }), [true, true]);
  assert.deepEqual(await flags({ action: "stop" }), [true, true], "stop-all is risky");
  assert.deepEqual(await flags({ action: "stop", agentId: "fox" }), [true, false]);
});

test("handleText: model failure or garbage never throws; bad input is a clean 400/404", async () => {
  const garbage = await voiceWith(async () => "I think you want to go to the beach!").handleText({ text: "beach" });
  assert.equal(garbage.intent.action, "none");
  assert.equal(garbage.needsConfirm, false);
  const down = await voiceWith(async () => { throw new Error("ECONNRESET"); }).handleText({ text: "beach" });
  assert.equal(down.intent.action, "none");
  assert.match(down.warning, /ECONNRESET/);
  const v = voiceWith(async () => "{}");
  await assert.rejects(v.handleText({ text: "   " }), { status: 400 });
  await assert.rejects(v.handleText({}), { status: 400 });
  await assert.rejects(v.handleText({ text: "x", agentId: "ghost" }), { status: 404 });
  await assert.rejects(v.handleText({ text: "x", projectId: "ghost" }), { status: 404 });
  await assert.rejects(v.handleText({ text: "x", scene: "mars" }), { status: 400 });
});

test("code words are fixed before the model sees them", async () => {
  let prompt = "";
  const r = await voiceWith(async (_s, p) => ((prompt = p), '{"action":"goal","goal":"add /health"}')).handleText({ text: "add slash health" });
  assert.equal(r.transcript, "add /health");
  assert.ok(prompt.includes('"add /health"'));
});

test("inspectWav + silence + transcript cleanup", async () => {
  assert.equal(inspectWav(wav(1)).seconds, 1);
  assert.throws(() => inspectWav(Buffer.from("not a wav at all, definitely not, really not at all")), { status: 415 });
  assert.throws(() => inspectWav(wav(1, { rate: 44100 })), /16 kHz mono/);
  assert.throws(() => inspectWav(wav(1, { channels: 2 })), /16 kHz mono/);
  const stt = createStt({ env: { STT_PROVIDER: "local" }, spawnFn: () => assert.fail("silence must not reach whisper") });
  assert.equal((await stt.transcribe(wav(1, { amp: 0 }))).text, "");
  assert.equal(cleanTranscript(" [BLANK_AUDIO] (music) hello  there "), "hello there");
  assert.throws(() => createStt({ env: { STT_PROVIDER: "carrier-pigeon" } }), /local, openai or google/);
});

function fakeSpawn(out, code = 0) {
  const calls = [];
  const fn = (bin, args) => {
    calls.push({ bin, args });
    const c = new EventEmitter();
    c.stdout = new EventEmitter(); c.stderr = new EventEmitter(); c.kill = () => {};
    setImmediate(() => { c.stdout.emit("data", out); c.emit("close", code); });
    return c;
  };
  return { fn, calls };
}

test("stt local: runs whisper with auto language + vocabulary hint; missing model/binary are 503s", async () => {
  const model = path.join(fs.mkdtempSync(path.join(os.tmpdir(), "loft-stt-")), "ggml-fake.bin");
  fs.writeFileSync(model, "x");
  const sp = fakeSpawn(" ask the fox [BLANK_AUDIO]\n");
  const stt = createStt({ env: { STT_MODEL_PATH: model, WHISPER_BIN: "wh" }, spawnFn: sp.fn });
  const r = await stt.transcribe(wav(1), { hint: "Loft. Scout." });
  assert.equal(r.text, "ask the fox");
  const a = sp.calls[0].args;
  assert.equal(sp.calls[0].bin, "wh");
  assert.equal(a[a.indexOf("-l") + 1], "auto");
  assert.equal(a[a.indexOf("--prompt") + 1], "Loft. Scout.");
  await assert.rejects(createStt({ env: { STT_MODEL_PATH: "/nope/x.bin" }, spawnFn: sp.fn }).transcribe(wav(1)), { status: 503 });
  await assert.rejects(createStt({ env: { STT_MODEL_PATH: model }, spawnFn: fakeSpawn("", 3).fn }).transcribe(wav(1)), { status: 502 });
});

test("stt openai: OpenAI-compatible /audio/transcriptions; config errors are named", async () => {
  let seen;
  const fetchFn = async (url, init) => ((seen = { url, init }), { ok: true, json: async () => ({ text: "switch to the city" }) });
  const env = { STT_PROVIDER: "openai", STT_BASE_URL: "https://stt.example/v1/", STT_API_KEY: "k", STT_MODEL: "whisper-1" };
  const r = await createStt({ env, fetchFn }).transcribe(wav(1));
  assert.equal(r.text, "switch to the city");
  assert.equal(seen.url, "https://stt.example/v1/audio/transcriptions");
  assert.equal(seen.init.headers.Authorization, "Bearer k");
  assert.equal(seen.init.body.get("model"), "whisper-1");
  assert.equal(seen.init.body.has("language"), false, "auto: language left to the provider");
  await assert.rejects(createStt({ env: { STT_PROVIDER: "openai" }, fetchFn }).transcribe(wav(1)), /STT_BASE_URL, STT_API_KEY, STT_MODEL/);
  await assert.rejects(createStt({ env, fetchFn: async () => ({ ok: false, status: 401, text: async () => "no" }) }).transcribe(wav(1)), { status: 502 });
});

test("handleAudio: transcript -> intent, timings; STT errors keep their status", async () => {
  const stt = { transcribe: async () => ({ text: "switch to the beach", ms: 5, seconds: 1 }) };
  const r = await voiceWith(async () => '{"action":"scene","sceneId":"beach"}', { stt }).handleAudio(wav(1), { scene: "city" });
  assert.deepEqual([r.transcript, r.intent.sceneId, r.sttMs, r.audioSeconds], ["switch to the beach", "beach", 5, 1]);
  await assert.rejects(voiceWith(async () => "{}", { stt: { transcribe: async () => { throw Object.assign(new Error("model missing"), { status: 503 }); } } }).handleAudio(wav(1)), { status: 503 });
  await assert.rejects(voiceWith(async () => "{}").handleAudio(wav(1)), { status: 503 }, "no STT configured");
});

test("auto-approve by voice: on is spelled out, off is plain", () => {
  assert.deepEqual(normalizeIntent({ action: "autoApprove", on: true }, ctx()), { action: "autoApprove", on: true });
  assert.match(summarize({ action: "autoApprove", on: true }, ctx()), /without you/);
  assert.equal(normalizeIntent({ action: "autoApprove", on: false }, ctx()).on, false);
});

test("note saves right away; ask goes to the researcher unless someone is pointed at", () => {
  assert.deepEqual(normalizeIntent({ action: "note", text: "fix the footer" }, ctx()), { action: "note", text: "fix the footer" });
  assert.equal(normalizeIntent({ action: "note" }, ctx()).action, "none");
  const ask = normalizeIntent({ action: "ask", text: "what's in index.html?" }, ctx());
  assert.equal(ask.action, "chat");
  assert.ok(ctx().agents.some((a) => a.id === ask.agentId));
  assert.equal(normalizeIntent({ action: "ask", text: "hi" }, ctx({ pointed: "keeper-3" })).agentId, "keeper-3");
});

test("menu: opens right away", () => {
  assert.equal(normalizeIntent({ action: "menu" }, ctx()).action, "menu");
  assert.equal(summarize({ action: "menu" }, ctx()), "Open the menu");
});

test("preview: shows the latest work right away", () => {
  const i = normalizeIntent({ action: "preview" }, ctx());
  assert.equal(i.action, "preview");
  assert.equal(summarize(i, ctx()), "Show the latest work");
});

test("browse: opens right away and needs an address", async () => {
  const { normalizeIntent } = await import("../src/voice.js").catch(() => ({}));
  if (!normalizeIntent) return;
  const ctx = { agents: [], roles: [], projects: [], review: [] };
  assert.equal(normalizeIntent({ action: "browse", url: "youtube.com" }, ctx).action, "browse");
  assert.equal(normalizeIntent({ action: "browse" }, ctx).action, "none");
});

test("google speech: uses the service account, sends both languages and the vocabulary", async () => {
  const calls = [];
  const fetchFn = async (url, o) => {
    calls.push({ url, o });
    if (url.includes("metadata")) return { ok: true, json: async () => ({ access_token: "tok" }) };
    return { ok: true, json: async () => ({ results: [{ alternatives: [{ transcript: "open the" }] }, { alternatives: [{ transcript: "menu" }] }] }) };
  };
  const stt = createStt({ env: { STT_PROVIDER: "google" }, fetchFn });
  const r = await stt.transcribe(wav(1), { hint: "Virtual Loft, Scout, Keeper" });
  assert.equal(r.text, "open the menu");
  const body = JSON.parse(calls[1].o.body);
  assert.equal(calls[1].o.headers.Authorization, "Bearer tok");
  assert.deepEqual(body.config.alternativeLanguageCodes, ["id-ID"]);
  assert.deepEqual(body.config.speechContexts[0].phrases, ["Virtual Loft", "Scout", "Keeper"]);
});

test("google speech: a refusal is reported, not swallowed", async () => {
  const fetchFn = async (url) => (url.includes("metadata") ? { ok: true, json: async () => ({ access_token: "t" }) } : { ok: false, status: 403, text: async () => "denied" });
  await assert.rejects(createStt({ env: { STT_PROVIDER: "google" }, fetchFn }).transcribe(wav(1)), /403/);
});
