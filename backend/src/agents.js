// VIRTUAL LOFT — agent runner on the OpenAI Agents SDK.
//
// Any OpenAI-compatible endpoint: OpenRouter (default), the Muse Spark
// gateway, OpenAI, a local server... set LLM_BASE_URL + LLM_API_KEY.
// Every agent has its OWN model id (typed by the CEO, e.g.
// "anthropic/claude-haiku-5-5" or "qwen/qwen3-coder") and its OWN role:
// {title, instructions, tools}. Roles can be written by AI from a plain
// description (draftRole) and are kept in a role library.

import OpenAI from "openai";
import { Agent, OpenAIChatCompletionsModel, run, setTracingDisabled } from "@openai/agents";
import { makeTools, TOOL_NAMES } from "./tools.js";

setTracingDisabled(true); // tracing would upload runs to OpenAI; not wanted with OpenRouter/Spark

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

export const defaultModel = () => process.env.LOFT_DEFAULT_MODEL || "anthropic/claude-haiku-5-5";

let client;
function llm() {
  if (!process.env.LLM_API_KEY) throw new Error("LLM_API_KEY is not set (see backend/README.md)");
  client ??= new OpenAI({ baseURL: process.env.LLM_BASE_URL || "https://openrouter.ai/api/v1", apiKey: process.env.LLM_API_KEY });
  return client;
}

const SYSTEM = (agent) =>
  `You are ${agent.name}, the ${agent.role.title} on an AI team run by a human CEO who watches you work in VR.\n` +
  `${agent.role.instructions}\n` +
  `You work inside one project folder; all file paths are relative to it. Be concise.`;

// One task for one agent. -> { text, usage: {requests, inputTokens, outputTokens} }
export async function runTask({ agent, title, workspace, abortController, onProgress = () => {}, checkPage = null }) {
  const signal = abortController?.signal;
  const a = new Agent({
    name: agent.name,
    instructions: SYSTEM(agent),
    model: new OpenAIChatCompletionsModel(llm(), agent.model),
    tools: makeTools(workspace, agent.role.tools, { signal, onProgress, checkPage }),
  });
  const result = await run(a, String(title), { maxTurns: Number(process.env.LOFT_MAX_TURNS ?? 40), signal });
  const u = result.state?._context?.usage ?? result.state?.usage ?? {};
  return {
    text: String(result.finalOutput ?? ""),
    usage: { requests: u.requests ?? 0, inputTokens: u.inputTokens ?? 0, outputTokens: u.outputTokens ?? 0 },
  };
}

async function askJson(model, system, prompt) {
  const res = await llm().chat.completions.create({
    model,
    messages: [{ role: "system", content: system }, { role: "user", content: prompt }],
  });
  return String(res.choices?.[0]?.message?.content ?? "");
}

function firstJson(text, open, close) {
  try {
    return JSON.parse(text.slice(text.indexOf(open), text.lastIndexOf(close) + 1));
  } catch {
    return null;
  }
}

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
  const items = firstJson(text, "[", "]");
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
  return normalizeRole(firstJson(text, "{", "}"), description);
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

// $ per token for a model id, from OpenRouter's public catalogue (cached).
// Unknown model / other provider -> null (tokens are still reported).
let prices;
export async function priceOf(model) {
  if (!(process.env.LLM_BASE_URL || "https://openrouter.ai/api/v1").includes("openrouter.ai")) return null;
  try {
    prices ??= fetch("https://openrouter.ai/api/v1/models")
      .then((r) => r.json())
      .then((j) => new Map((j.data ?? []).map((m) => [m.id, { in: Number(m.pricing?.prompt) || 0, out: Number(m.pricing?.completion) || 0 }])));
    return (await prices).get(model.replace(/:online$|:floor$|:nitro$/, "")) ?? null;
  } catch {
    prices = undefined;
    return null;
  }
}
