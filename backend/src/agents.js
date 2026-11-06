// VIRTUAL LOFT — agent runner on the OpenAI Agents SDK.
//
// Any OpenAI-compatible endpoint, configured by the repo-root .env
// (AGENT_BASE_URL + AGENT_API_KEY + AGENT_MODEL; see src/env.js). No built-in
// default endpoint or model. Every agent has its OWN model id and its OWN role:
// {title, instructions, tools}. Roles can be written by AI from a plain
// description (draftRole) and are kept in a role library.

import { Agent, OpenAIChatCompletionsModel, run, setTracingDisabled } from "@openai/agents";
import { llmConfig } from "./env.js";
import { askJson, extractJson, llm, stripThink } from "./llm.js";
import { makeTools, TOOL_NAMES } from "./tools.js";

setTracingDisabled(true); // tracing would upload runs to OpenAI; not wanted with a third-party endpoint

export const MAX_AGENTS = 20;
export const SKINS = ["fox", "owl", "cat", "bear", "rabbit", "penguin", "frog", "panda"];
export const MODEL_ID_RE = /^[\w.\-/:@]{1,120}$/;

// Starter roles (the library can grow with AI-written ones).
export const PRESET_ROLES = [
  {
    id: "scout", title: "Researcher",
    instructions: "Investigate the task: read the workspace and fetch public pages when useful. Reply with a short brief: findings as bullets, sources as URLs. Never invent URLs. Do not modify files.",
    tools: ["read_file", "list_files", "search_files", "fetch_url"],
  },
  {
    id: "builder", title: "Coder",
    instructions: "Make the smallest working change in the workspace, run the relevant tests or build with run_command, check web pages with check_page, and finish with: files changed, how you verified it, PASS or FAIL. Never delete work you were not asked to delete. Do not commit or push: the loft does that when the CEO approves.",
    tools: ["read_file", "list_files", "search_files", "write_file", "edit_file", "run_command", "check_page"],
  },
  {
    id: "keeper", title: "Reviewer",
    instructions: "Review the workspace against the task and reply with a short checklist: done, missing, risky. Use check_page to look at web pages. Do not modify files.",
    tools: ["read_file", "list_files", "search_files", "check_page"],
  },
];

export const DEFAULT_AGENTS = [
  { name: "Scout", roleId: "scout", skin: "owl" },
  { name: "Builder", roleId: "builder", skin: "fox" },
  { name: "Keeper", roleId: "keeper", skin: "cat" },
];

export const defaultModel = () => llmConfig().model; // "" when unset: the server refuses to start, runs fail loudly

const SYSTEM = (agent) =>
  `You are ${agent.name}, the ${agent.role.title} on an AI team run by a human CEO who watches you work in VR.\n` +
  `${agent.role.instructions}\n` +
  `You work inside one project folder; all file paths are relative to it. Be concise.`;

function failNoModel() {
  throw new Error("no model configured: set AGENT_MODEL (or LOFT_DEFAULT_MODEL) in the repo-root .env");
}

// One task for one agent. -> { text, usage: {requests, inputTokens, outputTokens} }
export async function runTask({ agent, title, notes = "", workspace, abortController, onProgress = () => {}, checkPage = null }) {
  const signal = abortController?.signal;
  const a = new Agent({
    name: agent.name,
    instructions: SYSTEM(agent) + (notes ? `\nThe CEO's notes, for context (not tasks unless the task says so):\n${notes}` : ""),
    model: new OpenAIChatCompletionsModel(llm(), agent.model || defaultModel() || failNoModel()),
    tools: makeTools(workspace, agent.role.tools, { signal, onProgress, checkPage }),
  });
  const result = await run(a, String(title), { maxTurns: Number(process.env.LOFT_MAX_TURNS ?? 40), signal });
  const u = result.state?._context?.usage ?? result.state?.usage ?? {};
  return {
    text: String(result.finalOutput ?? ""),
    usage: { requests: u.requests ?? 0, inputTokens: u.inputTokens ?? 0, outputTokens: u.outputTokens ?? 0 },
  };
}

const firstJson = (text, open) => extractJson(text, open);

// CEO goal -> [{agentId, title}], one model call (planner model or default).
export async function planGoal({ goal, agents }) {
  const roster = agents.map((a) => `- ${a.id}: ${a.name}, ${a.role.title} — ${a.role.instructions.slice(0, 140)}`).join("\n");
  const text = await askJson(
    process.env.LOFT_PLANNER_MODEL || defaultModel(),
    "You are the planner of a small AI team. You only output JSON.",
    `CEO goal: ${goal}\n\nTeam:\n${roster}\n\nSplit the goal into 1-${Math.min(agents.length * 2, 8)} small tasks. ` +
      `Give each task to exactly one member whose role fits it. Different members run in parallel. ` +
      `Reply with ONLY a JSON array: [{"agentId":"<id>","title":"<task>"}]`,
  );
  return parsePlan(text, agents, goal);
}

// Total: bad output falls back to "whole goal -> one member who can write files".
export function parsePlan(text, agents, goal) {
  const ids = new Set(agents.map((a) => a.id));
  const items = firstJson(text, "[");
  const plan = (Array.isArray(items) ? items : [])
    .filter((t) => t && ids.has(t.agentId) && typeof t.title === "string" && t.title.trim())
    .map((t) => ({ agentId: t.agentId, title: t.title.trim().slice(0, 2000) }));
  if (plan.length) return plan;
  const fallback = agents.find((a) => a.role.tools.includes("write_file")) ?? agents[0];
  return fallback ? [{ agentId: fallback.id, title: goal }] : [];
}

// Plain-language need -> role draft {title, instructions, tools}. The CEO
// reviews it (tools that change things are flagged) before it is saved.
export async function draftRole(description) {
  const text = await askJson(
    process.env.LOFT_ROLE_MODEL || defaultModel(),
    "You design roles for AI agents. You only output JSON.",
    `Design one AI agent role for this need: "${description}"\n` +
      `Tools available (grant ONLY what the job needs; write/edit/run change the project):\n` +
      `read_file, list_files, search_files (read the project), write_file, edit_file (change files), ` +
      `run_command (shell: tests, builds), fetch_url (read public web pages), check_page (open the project's own page in a browser).\n` +
      `Reply with ONLY JSON: {"title":"2-3 words","instructions":"how it works and what its final answer must contain (max 80 words)","tools":["..."]}`,
  );
  return normalizeRole(firstJson(text, "{"), description);
}

export function normalizeRole(raw, fallbackTitle = "Assistant") {
  const r = raw && typeof raw === "object" ? raw : {};
  const tools = Array.isArray(r.tools) ? [...new Set(r.tools.filter((t) => TOOL_NAMES.includes(t)))] : [];
  return {
    title: String(r.title ?? fallbackTitle).trim().slice(0, 40) || "Assistant",
    instructions: String(r.instructions ?? "").trim().slice(0, 2000) || `Help the CEO with: ${fallbackTitle}`,
    tools: tools.length ? tools : ["read_file", "list_files", "search_files"],
  };
}

// A short in-role chat turn with memory. -> plain text (1-3 sentences).
export async function chatReply({ agent, history, text, context }) {
  const system =
    `You are ${agent.name}, the ${agent.role.title} on a small AI team. The CEO is talking to you in VR, face to face.\n${agent.role.instructions}\n` +
    `Reply in 1-3 short spoken sentences, in the CEO's language (English, Indonesian or mixed), in character. ` +
    `Only mention work that is in your task notes; never claim you did or will do something that is not there. If asked to do real work, say it needs to be given as a task.\n` +
    `If project files are shown below, answer questions about them in plain words, not code: the gist, still at most 3 short sentences, in one paragraph.\n` +
    `Your task notes:\n${context || "(no tasks yet)"}`;
  const model = agent.model || defaultModel() || failNoModel();
  const res = await llm().chat.completions.create({
    model,
    messages: [{ role: "system", content: system }, ...history.slice(-16), { role: "user", content: text }],
    temperature: 0.7,
    max_tokens: 1500,
  });
  return stripThink(res.choices?.[0]?.message?.content);
}

// After finished work: one optional follow-up the agent would suggest, or null. One LLM call.
export async function suggestNext({ agent, task }) {
  const text = await askJson(
    process.env.LOFT_PLANNER_MODEL || defaultModel(),
    "You are a careful teammate. You only output JSON.",
    `You are ${agent.name}, the ${agent.role.title}. You just finished this task:\nTask: ${String(task.title).slice(0, 400)}\nResult: ${String(task.result).slice(0, 1200)}\n\n` +
      `Is there ONE clearly worthwhile follow-up task for yourself (a real gap, bug, or missing check you saw)? Most of the time the answer is no.\n` +
      `Reply ONLY with JSON: {"suggest":false} or {"suggest":true,"title":"<the follow-up task, as an instruction>","reason":"<one short sentence why>"}`,
    { temperature: 0.2, maxTokens: 1500 },
  );
  const j = extractJson(text, "{");
  return j && j.suggest === true && typeof j.title === "string" && j.title.trim() ? { title: j.title.trim(), reason: String(j.reason ?? "") } : null;
}

// Finished work in one plain sentence for a CEO who doesn't read code, or null. One LLM call.
export async function plainSummary({ agent, task }) {
  const text = await askJson(
    process.env.LOFT_PLANNER_MODEL || defaultModel(),
    "You explain finished work to a busy non-programmer. You only output JSON.",
    `${agent.name} finished this task:\nTask: ${String(task.title).slice(0, 400)}\nChanges: ${task.diff?.stat ?? "none"}\nReport: ${String(task.result).slice(0, 2000)}\n\n` +
      `In ONE short plain sentence, say what changed or what was found. No code, no markdown, no line numbers. Use the language of the task.\n` +
      `Reply ONLY with JSON: {"sentence":"..."}`,
    { temperature: 0.2, maxTokens: 1500 },
  );
  const j = extractJson(text, "{");
  return typeof j?.sentence === "string" && j.sentence.trim() ? j.sentence.trim() : null;
}

// $ per token for a model id, from OpenRouter's public catalogue (cached).
// Any other provider, unknown model or network error -> null: tokens are still
// reported, cost just stays empty.
let prices;
export async function priceOf(model) {
  if (!llmConfig().baseURL.includes("openrouter.ai")) return null;
  try {
    prices ??= fetch("https://openrouter.ai/api/v1/models")
      .then((r) => r.json())
      .then((j) => new Map((j.data ?? []).map((m) => [m.id, { in: Number(m.pricing?.prompt) || 0, out: Number(m.pricing?.completion) || 0 }])));
    return (await prices).get(String(model).replace(/:online$|:floor$|:nitro$/, "")) ?? null;
  } catch {
    prices = undefined;
    return null;
  }
}
