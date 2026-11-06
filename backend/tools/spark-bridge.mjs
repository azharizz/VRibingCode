// VIRTUAL LOFT — spark-bridge.mjs (localhost protocol bridge)
// Lets the Claude Agent SDK drive the Muse Spark gateway even though the two
// speak different protocols:
//   SDK/CLI  --Anthropic (x-api-key, /v1/messages, SSE)--> 127.0.0.1:PORT
//   bridge   --OpenAI-compatible (Bearer, /chat/completions)--> Meta gateway
//
// Why: the SDK CLI authenticates Anthropic-style (x-api-key) and this gateway
// requires Authorization: Bearer (verified: Bearer->200, x-api-key->401).
// The bridge terminates the Anthropic side locally and re-issues the call
// OpenAI-style with the saved key. The key is read ONLY from the environment
// (ANTHROPIC_API_KEY) and never logged.
//
// Usage:
//   node --env-file=.env tools/spark-bridge.mjs [--port 3847]
//   # then, in another shell:
//   ANTHROPIC_BASE_URL=http://127.0.0.1:3847 node --env-file=.env tools/try-sdk.mjs
//
// Scope: single-user localhost dev bridge. Not for production exposure.

import http from "node:http";

const PORT = Number(process.argv.includes("--port") ? process.argv[process.argv.indexOf("--port") + 1] : 3847);
const GATEWAY = (process.env.ANTHROPIC_BASE_URL ?? "").replace(/\/+$/, "");
const API_KEY = process.env.ANTHROPIC_API_KEY ?? "";
// Single-model gateway: only Muse Spark ids exist upstream. The SDK CLI
// validates model names client-side against claude-* patterns, so it cannot
// even send "muse-spark-1.3-contributor". The CLI is only the runner — the
// weights that answer are always Spark — therefore any requested model label
// is routed to Spark here. Advertised alias below exists solely to pass the
// CLI's client-side check.
const UPSTREAM_MODEL = "muse-spark-1.3-contributor";
const ALIAS_IDS = ["claude-sonnet-4-5"];

if (!GATEWAY || !API_KEY) {
  console.error("spark-bridge: need ANTHROPIC_BASE_URL + ANTHROPIC_API_KEY in env (use --env-file=.env)");
  process.exit(2);
}

const readBody = (req) =>
  new Promise((resolve, reject) => {
    const chunks = [];
    req.on("data", (c) => chunks.push(c));
    req.on("end", () => {
      try {
        resolve(chunks.length ? JSON.parse(Buffer.concat(chunks).toString("utf8")) : {});
      } catch (e) {
        reject(e);
      }
    });
    req.on("error", reject);
  });

const textOf = (blocks) =>
  (Array.isArray(blocks) ? blocks : [{ type: "text", text: String(blocks ?? "") }])
    .filter((b) => b && (b.type === "text" || typeof b.text === "string"))
    .map((b) => b.text ?? "")
    .join("");

// Anthropic messages -> OpenAI messages
function toOpenAIMessages(system, messages) {
  const out = [];
  if (system) out.push({ role: "system", content: textOf(typeof system === "string" ? [{ type: "text", text: system }] : system) });
  for (const m of messages ?? []) {
    if (m.role === "user") {
      if (typeof m.content === "string") {
        out.push({ role: "user", content: m.content });
        continue;
      }
      for (const b of m.content ?? []) {
        if (b.type === "text") out.push({ role: "user", content: b.text });
        else if (b.type === "tool_result")
          out.push({
            role: "tool",
            tool_call_id: b.tool_use_id,
            content: typeof b.content === "string" ? b.content : JSON.stringify(b.content ?? null),
          });
      }
    } else if (m.role === "assistant") {
      if (typeof m.content === "string") {
        out.push({ role: "assistant", content: m.content });
        continue;
      }
      const text = textOf((m.content ?? []).filter((b) => b.type === "text"));
      const calls = (m.content ?? []).filter((b) => b.type === "tool_use");
      out.push({
        role: "assistant",
        content: text || null,
        ...(calls.length
          ? {
              tool_calls: calls.map((b) => ({
                id: b.id,
                type: "function",
                function: { name: b.name, arguments: JSON.stringify(b.input ?? {}) },
              })),
            }
          : {}),
      });
    }
  }
  return out;
}

// OpenAI chat.completion -> Anthropic message
function toAnthropicMessage(model, data) {
  const msg = data?.choices?.[0]?.message ?? {};
  const finish = data?.choices?.[0]?.finish_reason;
  const content = [];
  if (typeof msg.content === "string" && msg.content) content.push({ type: "text", text: msg.content });
  for (const c of msg.tool_calls ?? []) {
    let input = {};
    try {
      input = JSON.parse(c?.function?.arguments ?? "{}");
    } catch {
      input = {};
    }
    content.push({ type: "tool_use", id: c.id, name: c?.function?.name, input });
  }
  return {
    id: data?.id ?? `msg_${Date.now()}`,
    type: "message",
    role: "assistant",
    model,
    content,
    stop_reason: (msg.tool_calls?.length ? "tool_use" : null) ?? (finish === "length" ? "max_tokens" : "end_turn"),
    stop_sequence: null,
    usage: {
      input_tokens: data?.usage?.prompt_tokens ?? 0,
      output_tokens: data?.usage?.completion_tokens ?? 0,
    },
  };
}

// Synthesize an Anthropic SSE stream from one complete message.
function writeAnthropicSSE(res, message) {
  const send = (obj) => res.write(`data: ${JSON.stringify(obj)}\n\n`);
  send({ type: "message_start", message: { ...message, content: [] } });
  message.content.forEach((block, i) => {
    send({ type: "content_block_start", index: i, content_block: block.type === "text" ? { type: "text", text: "" } : { type: "tool_use", id: block.id, name: block.name, input: {} } });
    if (block.type === "text") send({ type: "content_block_delta", index: i, delta: { type: "text_delta", text: block.text } });
    else send({ type: "content_block_delta", index: i, delta: { type: "input_json_delta", partial_json: JSON.stringify(block.input ?? {}) } });
    send({ type: "content_block_stop", index: i });
  });
  send({ type: "message_delta", delta: { stop_reason: message.stop_reason, stop_sequence: null }, usage: message.usage });
  send({ type: "message_stop" });
  res.end();
}

const server = http.createServer(async (req, res) => {
  const path = (req.url ?? "").split("?")[0];
  console.log(`bridge: ${req.method} ${path}`);
  try {
    if (req.method === "HEAD" && path === "/api/hello") {
      res.writeHead(200);
      res.end();
      return;
    }
    if (req.method === "GET" && path === "/v1/models") {
      res.writeHead(200, { "Content-Type": "application/json" });
      res.end(
        JSON.stringify({
          object: "list",
          data: [UPSTREAM_MODEL, ...ALIAS_IDS].map((id) => ({ id, object: "model", owned_by: "meta" })),
        }),
      );
      return;
    }
    if (req.method === "POST" && path === "/v1/messages") {
      const body = await readBody(req);
      // Route every requested label to the single upstream Spark model.
      const model = UPSTREAM_MODEL;
      console.log(`bridge: model label "${body.model}" -> upstream "${model}" (tools: ${Array.isArray(body.tools) ? body.tools.length : 0}, stream: ${!!body.stream})`);
      const oaRes = await fetch(`${GATEWAY}/chat/completions`, {
        method: "POST",
        headers: { Authorization: `Bearer ${API_KEY}`, "Content-Type": "application/json" },
        body: JSON.stringify({
          model,
          messages: toOpenAIMessages(body.system, body.messages),
          max_tokens: Math.max(64, Number(body.max_tokens ?? 1024)),
          ...(Array.isArray(body.tools) && body.tools.length
            ? {
                tools: body.tools.map((t) => ({
                  type: "function",
                  function: { name: t.name, description: t.description ?? "", parameters: t.input_schema ?? {} },
                })),
                tool_choice: "auto",
              }
            : {}),
        }),
      });
      if (!oaRes.ok) {
        res.writeHead(502, { "Content-Type": "application/json" });
        res.end(JSON.stringify({ type: "error", error: { type: "api_error", message: `gateway ${oaRes.status}` } }));
        return;
      }
      const message = toAnthropicMessage(model, await oaRes.json());
      console.log(
        `bridge: upstream -> stop=${message.stop_reason} blocks=[${message.content.map((b) => b.type).join(",")}] text=${JSON.stringify((message.content.find((b) => b.type === "text")?.text ?? "").slice(0, 60))}`,
      );
      if (body.stream) {
        res.writeHead(200, { "Content-Type": "text/event-stream", "Cache-Control": "no-cache", Connection: "keep-alive" });
        writeAnthropicSSE(res, message);
      } else {
        res.writeHead(200, { "Content-Type": "application/json" });
        res.end(JSON.stringify(message));
      }
      return;
    }
    res.writeHead(404, { "Content-Type": "application/json" });
    res.end(JSON.stringify({ type: "error", error: { type: "not_found", message: `spark-bridge: no route ${req.method} ${req.url}` } }));
  } catch (err) {
    res.writeHead(500, { "Content-Type": "application/json" });
    res.end(JSON.stringify({ type: "error", error: { type: "api_error", message: String((err && err.message) || err).slice(0, 200) } }));
  }
});

server.listen(PORT, "127.0.0.1", () => console.log(`spark-bridge: Anthropic->OpenAI on http://127.0.0.1:${PORT} -> ${GATEWAY}`));
