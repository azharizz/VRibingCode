// VIRTUAL LOFT — connection check (no secrets printed). One real agent turn
// with the same runner the team uses. Usage (from backend/):
//   npm run check [-- <model id>]      exit 0 = model reachable
import "../src/load-env.js"; // repo-root .env first
import { llmConfig, llmProblems } from "../src/env.js";
import { defaultModel, runTask } from "../src/agents.js";

const model = process.argv[2] || defaultModel();
const missing = llmProblems().filter((x) => !(model && x.startsWith("AGENT_MODEL")));
if (missing.length) {
  console.error(`SETUP INCOMPLETE (exit 2): missing ${missing.join(", ")} — see backend/README.md.`);
  process.exit(2);
}
console.log(`endpoint: ${llmConfig().baseURL}  model: ${model}`);
try {
  const agent = { name: "Check", model, role: { title: "Tester", instructions: "Answer exactly as asked.", tools: ["list_files"] } };
  const { text, usage } = await runTask({ agent, title: "Call list_files once, then reply with exactly: OK", workspace: process.cwd() });
  const ok = text.includes("OK");
  console.log(ok ? `REACHABLE + tool calling works (${usage.requests} requests, ${usage.inputTokens}+${usage.outputTokens} tokens)` : `unexpected reply: ${text.slice(0, 200)}`);
  process.exit(ok ? 0 : 1);
} catch (err) {
  console.error(`model call failed (exit 1): ${String(err?.message ?? err).slice(0, 300)}`);
  process.exit(1);
}
