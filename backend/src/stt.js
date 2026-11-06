// VIRTUAL LOFT — speech-to-text, pluggable by env (moving to the cloud = config only).
//   STT_PROVIDER=local   whisper.cpp on this machine (free, private). See tools/setup-stt.sh.
//                        WHISPER_BIN (default data/stt/bin/whisper-cli, else whisper-cli on PATH), STT_MODEL_PATH (default data/stt/ggml-small.bin)
//   STT_PROVIDER=openai  any OpenAI-compatible POST {STT_BASE_URL}/audio/transcriptions
//                        STT_BASE_URL, STT_API_KEY, STT_MODEL (e.g. whisper-1)
// Optional for both: STT_LANGUAGE (default "auto": mixed English/Indonesian gets a fair chance).
//                    STT_SAVE_SAMPLES=1 keeps recordings in data/stt/samples (off by default) so
//                    tools/stt-compare.js can run on your own voice. They never leave this machine.
import { spawn } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const LOCAL_BIN = path.join(here, "../data/stt/bin/whisper-cli"); // built by tools/setup-stt.sh
export const MAX_AUDIO = 10 * 1024 * 1024;

export class SttError extends Error {
  constructor(status, message) {
    super(message);
    this.status = status;
  }
}

// Checks the 16 kHz mono 16-bit PCM WAV contract; returns { seconds, rms } (rms 0..1 of the samples).
export function inspectWav(buf) {
  if (buf.length < 44 || buf.toString("latin1", 0, 4) !== "RIFF" || buf.toString("latin1", 8, 12) !== "WAVE") {
    throw new SttError(415, "audio must be a WAV file (16 kHz, mono, 16-bit)");
  }
  let fmt = null;
  let data = null;
  for (let o = 12; o + 8 <= buf.length; ) {
    const id = buf.toString("latin1", o, o + 4);
    const size = buf.readUInt32LE(o + 4);
    if (id === "fmt ") fmt = { format: buf.readUInt16LE(o + 8), channels: buf.readUInt16LE(o + 10), rate: buf.readUInt32LE(o + 12), bits: buf.readUInt16LE(o + 22) };
    if (id === "data") {
      data = buf.subarray(o + 8, Math.min(buf.length, o + 8 + size));
      break;
    }
    o += 8 + size + (size & 1);
  }
  if (!fmt || !data) throw new SttError(400, "broken WAV file (no fmt/data chunk)");
  if (fmt.format !== 1 || fmt.channels !== 1 || fmt.rate !== 16000 || fmt.bits !== 16) {
    throw new SttError(415, `WAV must be 16 kHz mono 16-bit PCM (got ${fmt.rate} Hz, ${fmt.channels} ch, ${fmt.bits}-bit)`);
  }
  const n = data.length >> 1;
  let sum = 0;
  for (let i = 0; i < n; i++) sum += (data.readInt16LE(i * 2) / 32768) ** 2;
  return { seconds: n / 16000, rms: n ? Math.sqrt(sum / n) : 0 };
}

// Whisper invents text on silence ("Thank you.", "[BLANK_AUDIO]"): drop tags and non-speech.
export function cleanTranscript(t) {
  return String(t ?? "")
    .replace(/\[[^\]]*\]|\([^)]*\)|♪[^♪]*♪?/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

function runLocal({ bin, modelPath, language, prompt, wav, spawnFn }) {
  if (!fs.existsSync(modelPath)) throw new SttError(503, `STT model not found at ${modelPath}: run backend/tools/setup-stt.sh`);
  const file = path.join(fs.mkdtempSync(path.join(os.tmpdir(), "loft-stt-")), "in.wav");
  fs.writeFileSync(file, wav);
  const args = ["-m", modelPath, "-f", file, "-l", language, "-nt", "-np", ...(prompt ? ["--prompt", prompt] : [])];
  return new Promise((resolve, reject) => {
    const child = spawnFn(bin, args);
    let out = "", err = "";
    const timer = setTimeout(() => child.kill("SIGKILL"), 60_000);
    child.stdout.on("data", (d) => (out += d));
    child.stderr.on("data", (d) => (err += d));
    child.on("error", (e) => reject(new SttError(503, e.code === "ENOENT" ? `${bin} not found: run backend/tools/setup-stt.sh` : `STT failed: ${e.message}`)));
    child.on("close", (code) => {
      clearTimeout(timer);
      fs.rmSync(path.dirname(file), { recursive: true, force: true });
      code === 0 ? resolve(out) : reject(new SttError(502, `whisper exited ${code}: ${err.slice(-200)}`));
    });
  });
}

async function runOpenAi({ baseUrl, apiKey, model, language, prompt, wav, fetchFn }) {
  const form = new FormData();
  form.set("file", new Blob([wav], { type: "audio/wav" }), "voice.wav");
  form.set("model", model);
  if (language !== "auto") form.set("language", language);
  if (prompt) form.set("prompt", prompt);
  const res = await fetchFn(`${baseUrl.replace(/\/+$/, "")}/audio/transcriptions`, { method: "POST", headers: { Authorization: `Bearer ${apiKey}` }, body: form });
  if (!res.ok) throw new SttError(502, `STT provider answered ${res.status}: ${(await res.text()).slice(0, 200)}`);
  const j = await res.json().catch(() => ({}));
  return String(j.text ?? "");
}

function saveSample(dir, wav, text) {
  fs.mkdirSync(dir, { recursive: true });
  const name = new Date().toISOString().replace(/[:.]/g, "-");
  fs.writeFileSync(path.join(dir, `${name}.wav`), wav);
  fs.writeFileSync(path.join(dir, `${name}.txt`), `${text}\n`); // what was heard: correct it to use as the expected text
}

// env, spawnFn and fetchFn are injectable for tests.
export function createStt({ env = process.env, spawnFn = spawn, fetchFn = fetch } = {}) {
  const provider = (env.STT_PROVIDER || "local").toLowerCase();
  const language = env.STT_LANGUAGE || "auto";
  if (!["local", "openai"].includes(provider)) throw new Error(`STT_PROVIDER must be local or openai (got '${provider}')`);

  // Returns { text, ms, seconds }. `hint` = vocabulary that biases spelling (names, code words).
  async function transcribe(wav, { hint = "" } = {}) {
    const { seconds, rms } = inspectWav(wav);
    if (seconds < 0.2 || rms < 0.002) return { text: "", ms: 0, seconds, silent: true };
    const t0 = Date.now();
    let raw;
    if (provider === "local") {
      raw = await runLocal({
        bin: env.WHISPER_BIN || (fs.existsSync(LOCAL_BIN) ? LOCAL_BIN : "whisper-cli"),
        modelPath: env.STT_MODEL_PATH || path.join(here, "../data/stt/ggml-small.bin"), // beat large-v3-turbo on speed and accuracy in tools/stt-compare
        language, prompt: hint, wav, spawnFn,
      });
    } else {
      const missing = ["STT_BASE_URL", "STT_API_KEY", "STT_MODEL"].filter((k) => !env[k]);
      if (missing.length) throw new SttError(503, `STT_PROVIDER=openai needs ${missing.join(", ")}`);
      raw = await runOpenAi({ baseUrl: env.STT_BASE_URL, apiKey: env.STT_API_KEY, model: env.STT_MODEL, language, prompt: hint, wav, fetchFn });
    }
    const text = cleanTranscript(raw);
    if (env.STT_SAVE_SAMPLES === "1") saveSample(env.STT_SAMPLE_DIR || path.join(here, "../data/stt/samples"), wav, text);
    return { text, ms: Date.now() - t0, seconds };
  }
  return { provider, transcribe };
}
