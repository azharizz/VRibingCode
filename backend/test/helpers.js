// Shared test helpers: WAV builder, tiny fake servers.
import http from "node:http";

// 16 kHz mono 16-bit PCM WAV with a sine tone (loud enough to count as speech).
export function wav(seconds = 1, { rate = 16000, channels = 1, amp = 0.3 } = {}) {
  const n = Math.floor(seconds * rate) * channels;
  const data = Buffer.alloc(n * 2);
  for (let i = 0; i < n; i++) data.writeInt16LE(Math.round(Math.sin(i / 20) * amp * 32767), i * 2);
  const h = Buffer.alloc(44);
  h.write("RIFF", 0); h.writeUInt32LE(36 + data.length, 4); h.write("WAVE", 8); h.write("fmt ", 12);
  h.writeUInt32LE(16, 16); h.writeUInt16LE(1, 20); h.writeUInt16LE(channels, 22); h.writeUInt32LE(rate, 24);
  h.writeUInt32LE(rate * channels * 2, 28); h.writeUInt16LE(channels * 2, 32); h.writeUInt16LE(16, 34);
  h.write("data", 36); h.writeUInt32LE(data.length, 40);
  return Buffer.concat([h, data]);
}

// Fake OpenAI-compatible LLM: answers every chat call with reply(requestJson) (string content).
export async function fakeLlm(reply) {
  const calls = [];
  const server = http.createServer((req, res) => {
    let body = "";
    req.on("data", (c) => (body += c));
    req.on("end", () => {
      const json = JSON.parse(body || "{}");
      calls.push(json);
      res.writeHead(200, { "content-type": "application/json" });
      res.end(JSON.stringify({
        id: "r", object: "chat.completion", created: 0, model: json.model,
        choices: [{ index: 0, message: { role: "assistant", content: reply(json), reasoning: "thinking..." }, finish_reason: "stop" }],
        usage: { prompt_tokens: 1, completion_tokens: 1, total_tokens: 2 },
      }));
    });
  });
  await new Promise((r) => server.listen(0, "127.0.0.1", r));
  return { calls, url: `http://127.0.0.1:${server.address().port}/v1`, close: () => server.close() };
}
