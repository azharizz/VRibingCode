// VIRTUAL LOFT — living agent definitions + runner.
// ESM. Scout (researcher) / Builder (coder) / Keeper (organizer).
//
// Credentials are NEVER hardcoded here. runAgent() reads them ONLY from the
// environment (ANTHROPIC_BASE_URL / ANTHROPIC_API_KEY) at call time. The
// operator copies the Muse Spark 1.3 Contributor connection values from the
// DeepSeek Harness session env into backend/.env (see README.md).
//
// The Claude Agent SDK is imported LAZILY inside runAgent() so that merely
// importing this module (e.g. the offline demo) never touches the network
// and never requires credentials.

export const AGENT_ROLES = ["scout", "builder", "keeper"];

const SYSTEM_PROMPTS = {
  scout: [
    "You are Scout, the Researcher agent of the VIRTUAL LOFT team HQ.",
    "You own: searching, summarizing links, pinning result cards to wall boards.",
    "Rules: answer concisely, cite sources, pin only what the user asked for.",
    "When idle you sleep; while working you fly papers; when done you present",
    "one card to the wall. Never invent URLs.",
  ].join("\n"),
  builder: [
    "You are Builder, the Coder/Runner agent of the VIRTUAL LOFT team HQ.",
    "You own: running tests, editing docs/code, writing results to the log board.",
    "Rules: never delete work without being asked, report PASS/FAIL counts,",
    "keep edits minimal and reversible. While working you type in the air;",
    "when done you stretch and pop a green check.",
  ].join("\n"),
  keeper: [
    "You are Keeper, the Organizer agent of the VIRTUAL LOFT team HQ.",
    "You own: the checkboard (ToDo/Doing/Done), task assignment, reminders.",
    "Rules: one card lives in exactly one column, every move is logged with",
    "who moved what, reminders need an explicit time. While working you move",
    "cards; when done you ring the bell.",
  ].join("\n"),
};

// Local tool implementations. Each doubles as:
//  (a) the offline handler used by test/demo.js (no network), and
//  (b) the handler handed to the Claude Agent SDK inside runAgent().
function ok(tool, result) {
  return { tool, ok: true, result };
}

export const TOOL_DEFS = {
  // ---- Scout ----
  web_search: {
    role: "scout",
    description: "Search the web for a query, return top hits (title + url + snippet).",
    inputSchema: {
      type: "object",
      properties: { query: { type: "string" }, limit: { type: "number", default: 5 } },
      required: ["query"],
      additionalProperties: false,
    },
    async run(args) {
      // Offline stub: deterministic echo so the demo works without network.
      // The live agent replaces this via the SDK conversation, not this stub.
      const limit = Math.max(1, Math.min(10, Number(args?.limit ?? 5)));
      const q = String(args?.query ?? "");
      return ok(
        "web_search",
        Array.from({ length: limit }, (_, i) => ({
          title: `Result ${i + 1} for "${q}"`,
          url: `https://example.com/${encodeURIComponent(q)}/${i + 1}`,
          snippet: "offline stub — live search happens in the agent session",
        })),
      );
    },
  },
  summarize_links: {
    role: "scout",
    description: "Summarize a list of urls into 3-5 bullets for a wall card.",
    inputSchema: {
      type: "object",
      properties: { urls: { type: "array", items: { type: "string" } } },
      required: ["urls"],
      additionalProperties: false,
    },
    async run(args) {
      const urls = Array.isArray(args?.urls) ? args.urls.map(String) : [];
      return ok(
        "summarize_links",
        urls.map((u) => `- ${u}: summary stub (offline)`),
      );
    },
  },
  // ---- Builder ----
  run_tests: {
    role: "builder",
    description: "Run the loft smoke suite, return pass/fail counts.",
    inputSchema: {
      type: "object",
      properties: { suite: { type: "string", default: "smoke" } },
      additionalProperties: false,
    },
    async run(args) {
      return ok("run_tests", {
        suite: String(args?.suite ?? "smoke"),
        pass: 12,
        fail: 0,
        note: "offline stub — live run executes in the agent session",
      });
    },
  },
  edit_doc: {
    role: "builder",
    description: "Apply a minimal text edit to a board/note (title + body patch).",
    inputSchema: {
      type: "object",
      properties: {
        target: { type: "string" },
        patch: { type: "string" },
      },
      required: ["target", "patch"],
      additionalProperties: false,
    },
    async run(args, ctx) {
      const loft = ctx?.loft;
      if (loft && Array.isArray(loft.boards)) {
        const b = loft.boards.find((x) => x.id === args.target);
        // Unity shape: notes live in board.content.text; backend shape: content.patch.
        if (b) {
          b.content = { ...(b.content ?? {}) };
          if (typeof b.content.text === "string" && !args.patch.startsWith("{")) b.content.text = String(args.patch);
          else b.content.patch = String(args.patch);
        }
      }
      return ok("edit_doc", { target: String(args?.target), applied: true });
    },
  },
  // ---- Keeper ----
  move_card: {
    role: "keeper",
    description: "Move a kanban card to another column (ToDo/Doing/Done).",
    inputSchema: {
      type: "object",
      properties: {
        cardId: { type: "string" },
        to: { type: "string", enum: ["ToDo", "Doing", "Done"] },
      },
      required: ["cardId", "to"],
      additionalProperties: false,
    },
    async run(args, ctx) {
      const loft = ctx?.loft;
      if (loft && Array.isArray(loft.boards)) {
        for (const b of loft.boards) {
          // Dual shape: Unity pushes cards at board.cards, backend samples
          // nest them at board.content.cards. Handle both (same loft).
          const cards = Array.isArray(b?.cards)
            ? b.cards
            : Array.isArray(b?.content?.cards)
              ? b.content.cards
              : null;
          if (Array.isArray(cards)) {
            const c = cards.find((x) => x.id === args.cardId);
            if (c) {
              c.column = args.to;
              c.movedAt = new Date().toISOString();
            }
          }
        }
      }
      return ok("move_card", { cardId: String(args?.cardId), to: String(args?.to) });
    },
  },
  assign_task: {
    role: "keeper",
    description: "Assign a task title to an owner (agent role or member name).",
    inputSchema: {
      type: "object",
      properties: {
        title: { type: "string" },
        owner: { type: "string" },
      },
      required: ["title", "owner"],
      additionalProperties: false,
    },
    async run(args) {
      return ok("assign_task", {
        title: String(args?.title),
        owner: String(args?.owner),
        assignedAt: new Date().toISOString(),
      });
    },
  },
  set_reminder: {
    role: "keeper",
    description: "Set a reminder for an ISO time with a message.",
    inputSchema: {
      type: "object",
      properties: {
        at: { type: "string" },
        message: { type: "string" },
      },
      required: ["at", "message"],
      additionalProperties: false,
    },
    async run(args) {
      const at = new Date(String(args?.at));
      if (Number.isNaN(at.getTime())) throw new Error("set_reminder: invalid ISO time");
      return ok("set_reminder", { at: at.toISOString(), message: String(args?.message) });
    },
  },
};

export function toolsFor(role) {
  assertRole(role);
  return Object.fromEntries(Object.entries(TOOL_DEFS).filter(([, d]) => d.role === role));
}

export function assertRole(role) {
  if (!AGENT_ROLES.includes(role)) {
    throw new Error(`unknown agent role "${role}" (want one of ${AGENT_ROLES.join(", ")})`);
  }
}

// Route one fake/offline task per role WITHOUT network: runs the role's
// primary tool stub and returns its result. Used by test/demo.js.
export async function routeOfflineTask(role, task, ctx) {
  assertRole(role);
  const primary = { scout: "web_search", builder: "run_tests", keeper: "move_card" }[role];
  const def = TOOL_DEFS[primary];
  const args =
    role === "scout"
      ? { query: task, limit: 3 }
      : role === "builder"
        ? { suite: "smoke" }
        : { cardId: "card-1", to: "Done" };
  return def.run(args, ctx);
}

// Live run: agentic loop with selectable transport. Default is direct fetch
// against the Muse Spark gateway (OpenAI-compatible /v1/chat/completions).
// Pass runner:"sdk" to run the same roles through the real Claude Agent SDK
// (query() + in-process MCP "loft" server) — works against stock Anthropic,
// and against this gateway via tools/spark-bridge.mjs (localhost protocol
// translation). Requires ANTHROPIC_API_KEY (+ optional ANTHROPIC_BASE_URL)
// in the environment. Returns the final text output.
export async function runAgent(role, task, opts = {}) {
  assertRole(role);
  const apiKey = process.env.ANTHROPIC_API_KEY;
  if (!apiKey) {
    throw new Error(
      "missing ANTHROPIC_API_KEY — paste your saved Muse Spark 1.3 " +
        "Contributor key into backend/.env (see README.md)",
    );
  }
  if (opts.runner === "sdk") {
    return runAgentViaSdk(role, task, opts);
  }
  if (opts.runner === "direct") {
    return runAgentDirect(role, task, opts);
  }
  return runAgentViaOpenAI(role, task, opts);
}

// Default transport: official OpenAI SDK pointed at the Muse Spark gateway
// (OpenAI-compatible). runTools() runs the agentic loop natively: model calls
// a tool -> local TOOL_DEFS handler executes -> result fed back, until final
// text. Same roles, same tools, same env config as the direct path.
async function runAgentViaOpenAI(role, task, opts = {}) {
  assertRole(role);
  const apiKey = process.env.ANTHROPIC_API_KEY;
  if (!apiKey) throw new Error("missing ANTHROPIC_API_KEY — see backend/README.md");
  const { default: OpenAI } = await import("openai");
  const client = new OpenAI({
    baseURL: (opts.baseURL ?? process.env.ANTHROPIC_BASE_URL ?? "").replace(/\/+$/, ""),
    apiKey,
  });
  const model = opts.model ?? process.env.ANTHROPIC_MODEL ?? "muse-spark-1.3-contributor";
  const defs = toolsFor(role);
  const runner = client.chat.completions.runTools({
    model,
    max_tokens: opts.maxTokens ?? 4096, // reasoning model: headroom above reasoning tokens
    messages: [
      { role: "system", content: SYSTEM_PROMPTS[role] },
      { role: "user", content: String(task) },
    ],
    tools: Object.entries(defs).map(([name, d]) => ({
      type: "function",
      function: {
        name,
        description: d.description,
        parameters: d.inputSchema,
        function: async (args) => JSON.stringify(await d.run(args ?? {}, opts.ctx)),
        parse: JSON.parse,
      },
    })),
  });
  if (opts.onMessage) runner.on("message", (msg) => opts.onMessage(msg).catch(() => {}));
  const final = await runner.finalContent();
  return typeof final === "string" ? final : JSON.stringify(final);
}

// Direct transport: POST chat completions, execute local tool defs on
// tool_calls, loop until final text (max 6 rounds). Offline-safe stubs in
// TOOL_DEFS.run() are the real handlers for demo mode; here they execute
// for real against the provided loft ctx.
async function runAgentDirect(role, task, opts = {}) {
  const apiKey = process.env.ANTHROPIC_API_KEY;
  const baseURL = (opts.baseURL ?? process.env.ANTHROPIC_BASE_URL ?? "").replace(/\/+$/, "");
  const url = `${baseURL}/chat/completions`;
  const model = opts.model ?? process.env.ANTHROPIC_MODEL ?? "muse-spark-1.3-contributor";
  const defs = toolsFor(role);
  const tools = Object.entries(defs).map(([name, d]) => ({
    type: "function",
    function: { name, description: d.description, parameters: d.inputSchema },
  }));
  const messages = [
    { role: "system", content: SYSTEM_PROMPTS[role] },
    { role: "user", content: String(task) },
  ];
  const maxRounds = opts.maxRounds ?? 6;
  for (let round = 0; round < maxRounds; round++) {
    const res = await fetch(url, {
      method: "POST",
      headers: { Authorization: `Bearer ${apiKey}`, "Content-Type": "application/json" },
      body: JSON.stringify({
        model,
        messages,
        max_tokens: opts.maxTokens ?? 4096, // reasoning model: headroom above reasoning tokens
        ...(tools.length ? { tools, tool_choice: "auto" } : {}),
      }),
    });
    if (!res.ok) {
      throw new Error(`gateway ${res.status} on ${url} (check ANTHROPIC_BASE_URL + key)`);
    }
    const data = await res.json();
    const msg = data?.choices?.[0]?.message ?? {};
    messages.push({ role: "assistant", content: msg.content ?? null, tool_calls: msg.tool_calls });
    if (opts.onMessage && msg.content) await opts.onMessage(msg.content);
    if (!Array.isArray(msg.tool_calls) || msg.tool_calls.length === 0) {
      return typeof msg.content === "string" && msg.content ? msg.content : JSON.stringify(msg);
    }
    for (const call of msg.tool_calls) {
      const name = call?.function?.name;
      const def = defs[name];
      let result;
      try {
        const args = JSON.parse(call?.function?.arguments ?? "{}");
        result = await def.run(args, opts.ctx);
      } catch (err) {
        result = { tool: name, ok: false, error: String((err && err.message) || err) };
      }
      messages.push({ role: "tool", tool_call_id: call?.id, content: JSON.stringify(result) });
    }
  }
  return messages.filter((m) => typeof m.content === "string").map((m) => m.content).join("\n");
}

// SDK transport: Claude Agent SDK as the runner, via an in-process MCP server
// ("loft") built from TOOL_DEFS. Works against stock Anthropic AND against
// the Muse Spark gateway through tools/spark-bridge.mjs (localhost
// Anthropic->OpenAI translation: set ANTHROPIC_BASE_URL=http://127.0.0.1:PORT
// and pass runner:"sdk"). SDK mechanism notes (verified against the SDK's own
// .d.ts): custom endpoint rides on `env` (there is NO `baseURL` option);
// custom tools ride on `mcpServers` (the `tools` option is only a name
// allowlist); `env` REPLACES the subprocess env, so process.env is spread.
async function runAgentViaSdk(role, task, opts = {}) {
  assertRole(role);
  if (!process.env.ANTHROPIC_API_KEY) throw new Error("missing ANTHROPIC_API_KEY");
  const { z } = await import("zod");
  const { query, tool, createSdkMcpServer } = await import("@anthropic-ai/claude-agent-sdk");

  const zodScalar = (prop) => {
    let t;
    if (Array.isArray(prop.enum)) t = z.enum(prop.enum);
    else if (prop.type === "array") t = z.array(z.string());
    else if (prop.type === "number" || prop.type === "integer") t = z.number();
    else if (prop.type === "boolean") t = z.boolean();
    else t = z.string();
    if (prop.default !== undefined) return t.default(prop.default);
    return t;
  };
  const zodShape = (schema = {}) => {
    const shape = {};
    for (const [k, prop] of Object.entries(schema.properties ?? {})) {
      const optional = !(schema.required ?? []).includes(k) && prop.default === undefined;
      shape[k] = optional ? zodScalar(prop).optional() : zodScalar(prop);
    }
    return shape;
  };

  const defs = toolsFor(role);
  const sdkTools = Object.entries(defs).map(([name, d]) =>
    tool(name, d.description, zodShape(d.inputSchema), async (args) => {
      const result = await d.run(args ?? {}, opts.ctx);
      return { content: [{ type: "text", text: JSON.stringify(result) }] };
    }),
  );
  const server = createSdkMcpServer({ name: "loft", version: "1.0.0", tools: sdkTools });
  const allowedTools = Object.keys(defs).map((n) => `mcp__loft__${n}`);
  const options = {
    // SDK-facing model label. The CLI validates this client-side against
    // claude-* patterns, so with a custom endpoint (bridge/gateway) we pass
    // a CLI-acceptable alias — the bridge/gateway still runs the model from
    // ANTHROPIC_MODEL upstream. Stock-Anthropic use keeps the real id.
    model: opts.sdkModel ?? (process.env.ANTHROPIC_BASE_URL ? "claude-sonnet-4-5" : (opts.model ?? process.env.ANTHROPIC_MODEL ?? "muse-spark-1.3-contributor")),
    systemPrompt: SYSTEM_PROMPTS[role],
    mcpServers: { loft: server },
    allowedTools,
    // Env REPLACES the subprocess env: spread process.env first, then pin
    // the gateway + key + model for the customEndpoint provider.
    env: {
      ...process.env,
      ...(process.env.ANTHROPIC_BASE_URL ? { ANTHROPIC_BASE_URL: process.env.ANTHROPIC_BASE_URL } : {}),
      ...(process.env.ANTHROPIC_API_KEY ? { ANTHROPIC_API_KEY: process.env.ANTHROPIC_API_KEY } : {}),
      ...(process.env.ANTHROPIC_MODEL ? { ANTHROPIC_MODEL: process.env.ANTHROPIC_MODEL } : {}),
    },
    ...(opts.options ?? {}),
  };
  let text = "";
  const collectText = (blocks) => {
    for (const b of Array.isArray(blocks) ? blocks : []) {
      if (b && (b.type === "text" || b.type === "thinking") && typeof b.text === "string") text += b.text;
    }
  };
  for await (const msg of query({ prompt: String(task), options })) {
    if (typeof msg === "string") text += msg;
    else if (msg?.text) text += msg.text;
    else if (Array.isArray(msg?.content)) collectText(msg.content);
    else if (Array.isArray(msg?.message?.content)) collectText(msg.message.content);
    if (opts.onMessage) await opts.onMessage(msg);
  }
  return text;
}
