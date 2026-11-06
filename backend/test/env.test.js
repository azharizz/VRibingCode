// Env loading, LLM config (AGENT_* with LLM_* override, no hidden defaults), tolerant JSON.
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { defaultModel, priceOf } from "../src/agents.js";
import { llmConfig, llmProblems, loadEnv } from "../src/env.js";
import { extractJson, stripThink } from "../src/llm.js";

const KEYS = ["AGENT_API_KEY", "AGENT_BASE_URL", "AGENT_MODEL", "LLM_API_KEY", "LLM_BASE_URL", "LOFT_DEFAULT_MODEL", "ENVTEST_ONLY"];
function withEnv(vars, fn) {
  const saved = Object.fromEntries(KEYS.map((k) => [k, process.env[k]]));
  for (const k of KEYS) delete process.env[k];
  Object.assign(process.env, vars);
  try {
    return fn();
  } finally {
    for (const k of KEYS) saved[k] === undefined ? delete process.env[k] : (process.env[k] = saved[k]);
  }
}

test("loadEnv: reads existing files in order, never overrides, skips missing", () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "loft-env-"));
  const a = path.join(dir, "a.env");
  const b = path.join(dir, "b.env");
  fs.writeFileSync(a, "AGENT_MODEL=from-root\nENVTEST_ONLY=1\n");
  fs.writeFileSync(b, "AGENT_MODEL=from-backend\nAGENT_BASE_URL=http://b/v1\n");
  withEnv({}, () => {
    assert.deepEqual(loadEnv([a, path.join(dir, "missing.env"), b]), [a, b]);
    assert.equal(process.env.AGENT_MODEL, "from-root", "first file wins");
    assert.equal(process.env.AGENT_BASE_URL, "http://b/v1");
  });
  withEnv({ AGENT_MODEL: "real-env" }, () => {
    loadEnv([a]);
    assert.equal(process.env.AGENT_MODEL, "real-env", "real environment beats files");
  });
});

test("llmConfig: AGENT_* is the source, LLM_* / LOFT_DEFAULT_MODEL override", () => {
  withEnv({ AGENT_API_KEY: "k1", AGENT_BASE_URL: "http://agent/v1", AGENT_MODEL: "m1" }, () => {
    assert.deepEqual(llmConfig(), { apiKey: "k1", baseURL: "http://agent/v1", model: "m1" });
    assert.equal(defaultModel(), "m1");
    assert.deepEqual(llmProblems(), []);
  });
  withEnv({ AGENT_API_KEY: "k1", AGENT_BASE_URL: "http://agent/v1", AGENT_MODEL: "m1", LLM_API_KEY: "k2", LLM_BASE_URL: "http://llm/v1", LOFT_DEFAULT_MODEL: "m2" }, () => {
    assert.deepEqual(llmConfig(), { apiKey: "k2", baseURL: "http://llm/v1", model: "m2" });
  });
});

test("no hidden fallback: a missing value is named, never defaulted", () => {
  withEnv({}, () => {
    assert.deepEqual(llmConfig(), { apiKey: "", baseURL: "", model: "" });
    assert.equal(defaultModel(), "");
    const p = llmProblems().join(" ");
    assert.match(p, /AGENT_API_KEY/);
    assert.match(p, /AGENT_BASE_URL/);
    assert.match(p, /AGENT_MODEL/);
  });
  withEnv({ AGENT_API_KEY: "   " }, () => assert.match(llmProblems().join(" "), /AGENT_API_KEY/), "blank counts as missing");
});

test("priceOf: only OpenRouter has a catalogue; anything else degrades to null", async () => {
  await withEnv({ AGENT_BASE_URL: "https://api.tokenfactory.nebius.com/v1/" }, async () => {
    assert.equal(await priceOf("deepseek-ai/DeepSeek-V4.1-Flash-1017"), null);
  });
  await withEnv({}, async () => assert.equal(await priceOf("x"), null));
});

test("extractJson survives think text, fences, prose and braces inside strings", () => {
  assert.deepEqual(extractJson('<think>maybe {"a":0}</think>\n```json\n{"a":1,"b":"x}y"}\n```'), { a: 1, b: "x}y" });
  assert.deepEqual(extractJson('Sure! Here you go: {"action":"scene","sceneId":"beach"} hope that helps'), { action: "scene", sceneId: "beach" });
  assert.deepEqual(extractJson('noise {broken {"ok":true}'), { ok: true });
  assert.deepEqual(extractJson('reasoning ... </think> [{"agentId":"a"}]', "["), [{ agentId: "a" }]);
  assert.equal(extractJson("no json here"), null);
  assert.equal(extractJson('{"a":'), null);
  assert.equal(extractJson(undefined), null);
  assert.equal(stripThink("<think>x</think>  hi"), "hi");
});
