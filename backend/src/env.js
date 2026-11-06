// VIRTUAL LOFT — environment. The repo-root .env is the single source:
//   AGENT_API_KEY, AGENT_BASE_URL (any OpenAI-compatible endpoint), AGENT_MODEL.
// LLM_API_KEY / LLM_BASE_URL / LOFT_DEFAULT_MODEL still win when set (override).
// backend/.env is optional (SUPABASE_*, HOST, LOFT_TOKEN...). Real environment
// variables beat both files. No default endpoint or model: a missing value fails loudly.
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
export const ROOT_ENV = path.resolve(here, "../../.env");
export const BACKEND_ENV = path.resolve(here, "../.env");

// Loads files that exist; never overrides a variable that is already set.
export function loadEnv(files = [ROOT_ENV, BACKEND_ENV]) {
  const loaded = [];
  for (const f of files) {
    if (!fs.existsSync(f)) continue;
    process.loadEnvFile(f);
    loaded.push(f);
  }
  return loaded;
}

const pick = (...names) => names.map((n) => process.env[n]?.trim()).find(Boolean) ?? "";

export const llmConfig = () => ({
  apiKey: pick("LLM_API_KEY", "AGENT_API_KEY"),
  baseURL: pick("LLM_BASE_URL", "AGENT_BASE_URL"),
  model: pick("LOFT_DEFAULT_MODEL", "AGENT_MODEL"),
});

// Names of what is missing (never values), for a loud startup error.
export function llmProblems() {
  const c = llmConfig();
  return [
    !c.apiKey && "AGENT_API_KEY (or LLM_API_KEY)",
    !c.baseURL && "AGENT_BASE_URL (or LLM_BASE_URL)",
    !c.model && "AGENT_MODEL (or LOFT_DEFAULT_MODEL)",
  ].filter(Boolean);
}
