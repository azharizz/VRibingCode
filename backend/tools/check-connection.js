// VIRTUAL LOFT — connection check (no secrets printed, ever).
// Verifies the Muse Spark 1.3 Contributor wiring used by src/agents.js:
//   1. ANTHROPIC_API_KEY present (length + prefix shape only, value never logged)
//   2. ANTHROPIC_BASE_URL (gateway) present
//   3. Minimal end-to-end runAgent("scout", "Reply with exactly: OK") —
//      direct fetch transport (OpenAI-compatible). The Claude Agent SDK path
//      is stock-Anthropic only: its bundled CLI pins its own auth flow and
//      returns 401 against this gateway (verified), so it is NOT used here.
// Usage: node --env-file=.env tools/check-connection.js (from backend/)
// Exit 0 = brain reachable. Exit 2 = setup incomplete. Exit 1 = gateway error.
import { runAgent } from "../src/agents.js";

const key = process.env.ANTHROPIC_API_KEY ?? "";
const baseURL = process.env.ANTHROPIC_BASE_URL ?? "";
const model = process.env.ANTHROPIC_MODEL ?? "muse-spark-1.3-contributor";

if (!key) {
  console.error(
    "SETUP INCOMPLETE (exit 2): ANTHROPIC_API_KEY is empty.\n" +
      "Paste your saved Muse Spark 1.3 Contributor key into backend/.env " +
      "(see backend/README.md).",
  );
  process.exit(2);
}
if (!baseURL) {
  console.error(
    "SETUP INCOMPLETE (exit 2): ANTHROPIC_BASE_URL is empty.\n" +
      "Set it to your Muse Spark gateway (e.g. https://api.meta.ai/v1).",
  );
  process.exit(2);
}
console.log(`key: present (${key.length} chars, starts with "${key.slice(0, 4)}...")`);
console.log(`gateway: ${baseURL}`);
console.log(`model: ${model}`);

try {
  const text = await runAgent("scout", "Reply with exactly: OK");
  if (text.includes("OK")) {
    console.log("BRAIN REACHABLE: gateway -> model OK (exit 0)");
  } else {
    console.error(`UNEXPECTED REPLY (exit 1): ${JSON.stringify(text.slice(0, 200))}`);
    process.exit(1);
  }
} catch (err) {
  console.error(`GATEWAY ERROR (exit 1): ${String((err && err.message) || err).slice(0, 300)}`);
  process.exit(1);
}
