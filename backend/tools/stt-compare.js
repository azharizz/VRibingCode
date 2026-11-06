#!/usr/bin/env node
// Compare speech-to-text setups on real recordings.
//   node tools/stt-compare.js [folder] [--url http://localhost:8000/v1]
// folder: pairs like clip.wav + clip.txt (the expected words). Default data/stt/samples,
// which STT_SAVE_SAMPLES=1 fills with your own voice (fix the .txt files first).
// Runs every ggml-*.bin in data/stt, plus an OpenAI-compatible server when --url is given.
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createStt } from "../src/stt.js";

const here = path.dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const urlAt = args.indexOf("--url");
const url = urlAt >= 0 ? args.splice(urlAt, 2)[1] : null;
const folder = path.resolve(args[0] ?? path.join(here, "../data/stt/samples"));

const words = (s) => s.toLowerCase().replace(/[^\p{L}\p{N}/.\s]/gu, " ").split(/\s+/).map((w) => w.replace(/\.+$/, "")).filter(Boolean);
export function wordErrorRate(expected, got) {
  const a = words(expected), b = words(got);
  let row = b.map((_, j) => j + 1);
  row.unshift(0);
  for (let i = 1; i <= a.length; i++) {
    const next = [i];
    for (let j = 1; j <= b.length; j++) next[j] = Math.min(row[j] + 1, next[j - 1] + 1, row[j - 1] + (a[i - 1] === b[j - 1] ? 0 : 1));
    row = next;
  }
  return a.length ? row[b.length] / a.length : 0;
}

const clips = fs.existsSync(folder) ? fs.readdirSync(folder).filter((f) => f.endsWith(".wav")).sort() : [];
if (!clips.length) {
  console.error(`no .wav files in ${folder}. Run the server with STT_SAVE_SAMPLES=1 and talk first.`);
  process.exit(1);
}
const dir = path.join(here, "../data/stt");
const setups = fs.readdirSync(dir).filter((f) => /^ggml-.*\.bin$/.test(f))
  .map((f) => ({ name: f.slice(5, -4), stt: createStt({ env: { STT_PROVIDER: "local", STT_MODEL_PATH: path.join(dir, f) } }) }));
if (url) setups.push({ name: `server ${url}`, stt: createStt({ env: { STT_PROVIDER: "openai", STT_BASE_URL: url, STT_API_KEY: "local", STT_MODEL: process.env.STT_MODEL || "large-v3-turbo" } }) });

const verbose = process.env.VERBOSE === "1";
const rows = [];
for (const { name, stt } of setups) {
  let err = 0, ms = 0, secs = 0;
  for (const c of clips) {
    const wav = fs.readFileSync(path.join(folder, c));
    const expected = fs.readFileSync(path.join(folder, c.replace(/\.wav$/, ".txt")), "utf8").trim();
    const r = await stt.transcribe(wav, { hint: "Builder, Scout, Keeper, fox, owl, cat, /health, README, npm, git" });
    const e = wordErrorRate(expected, r.text);
    err += e; ms += r.ms; secs += r.seconds;
    if (verbose) console.log(`${name} | ${c} | ${(e * 100).toFixed(0)}% | ${r.text}`);
  }
  rows.push({ setup: name, "word error %": ((err / clips.length) * 100).toFixed(1), "avg ms": Math.round(ms / clips.length), "x realtime": (secs / (ms / 1000)).toFixed(1) });
}
console.table(rows);
