// VIRTUAL LOFT — the one LLM client (OpenAI-compatible) + tolerant JSON extraction.
import OpenAI from "openai";
import { llmConfig, llmProblems } from "./env.js";

let client;
let clientKey = "";
export function llm() {
  const p = llmProblems().filter((x) => !x.startsWith("AGENT_MODEL"));
  if (p.length) throw new Error(`LLM not configured: set ${p.join(", ")} in the repo-root .env (see backend/README.md)`);
  const c = llmConfig();
  if (!client || clientKey !== c.baseURL + c.apiKey) {
    client = new OpenAI({ baseURL: c.baseURL, apiKey: c.apiKey });
    clientKey = c.baseURL + c.apiKey;
  }
  return client;
}

// Reasoning models may put <think>…</think> (or <thinking>) in the content.
export const stripThink = (t) =>
  String(t ?? "").replace(/<(think|thinking|reasoning)>[\s\S]*?<\/\1>/gi, "").replace(/^[\s\S]*?<\/(think|thinking)>/i, "").trim();

// Plain-text answer from one chat call (reasoning field ignored, think text stripped).
export async function askJson(model, system, prompt, { temperature, maxTokens } = {}) {
  if (!model) throw new Error("no model configured: set AGENT_MODEL (or LOFT_DEFAULT_MODEL) in the repo-root .env");
  const res = await llm().chat.completions.create({
    model,
    messages: [{ role: "system", content: system }, { role: "user", content: prompt }],
    ...(temperature != null && { temperature }),
    ...(maxTokens != null && { max_tokens: maxTokens }),
  });
  return stripThink(res.choices?.[0]?.message?.content);
}

// First balanced JSON value of the wanted kind ("{" or "[") in free text:
// survives code fences, prose around it, and braces inside strings.
export function extractJson(text, open = "{") {
  const s = stripThink(text).replace(/```(?:json)?/gi, " ");
  const close = open === "{" ? "}" : "]";
  for (let start = s.indexOf(open); start !== -1; start = s.indexOf(open, start + 1)) {
    let depth = 0, inStr = false, esc = false;
    for (let i = start; i < s.length; i++) {
      const ch = s[i];
      if (inStr) {
        if (esc) esc = false;
        else if (ch === "\\") esc = true;
        else if (ch === '"') inStr = false;
      } else if (ch === '"') inStr = true;
      else if (ch === open) depth++;
      else if (ch === close && --depth === 0) {
        try {
          return JSON.parse(s.slice(start, i + 1));
        } catch {
          break; // not valid from this start; try the next opener
        }
      }
    }
  }
  return null;
}
