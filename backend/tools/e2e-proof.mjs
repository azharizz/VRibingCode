// VIRTUAL LOFT — end-to-end proof (LIVE, uses real Muse Spark, no stubs).
// Solves a real problem on the real loft and leaves AI-made edits behind:
//
//   PROBLEM: "Morning standup" card is stuck in Doing, ToDo is piling up.
//   TASK TO KEEPER: standup is over — move it to Done, assign "Write launch
//   notes" to builder, confirm what changed.
//
// Flow (same as Unity LoftClient does at runtime):
//   1. Seed server loft (Unity-shaped boards, like BoardManager.Save pushes).
//   2. Run keeper LIVE via runAgent (real agentic loop, real tool calls mutate ctx loft).
//   3. Save the mutated loft back to the server.
//   4. Print BEFORE/AFTER + write Tools/e2e-result.json for Unity to consume.
//
// Usage: node --env-file=.env tools/e2e-proof.mjs (from backend/)
// Exit 0 = keeper actually edited the loft. Exit 1 = no edit happened.
import { promises as fs } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { runAgent, TOOL_DEFS } from "../src/agents.js";

const SERVER = process.env.LOFT_SERVER ?? "http://127.0.0.1:4101";
const ROOM = "focus-loft";
const here = path.dirname(fileURLToPath(import.meta.url));

const seedBoards = [
  {
    id: "kanban",
    type: "Kanban",
    cards: [
      { id: "standup-01", text: "Morning standup", column: "Doing" },
      { id: "todo-01", text: "Write launch notes", column: "ToDo" },
      { id: "todo-02", text: "Book Quest for weekend", column: "ToDo" },
    ],
  },
  { id: "notes", type: "Notes", cards: [] },
  { id: "calendar", type: "Calendar", cards: [] },
  { id: "log", type: "Log", cards: [] },
];

async function post(p, body) {
  const res = await fetch(`${SERVER}${p}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error(`POST ${p} -> ${res.status}: ${await res.text()}`);
  return res.json();
}
async function getBoards() {
  const res = await fetch(`${SERVER}/loft/${ROOM}/boards`);
  if (!res.ok) throw new Error(`GET boards -> ${res.status}`);
  return (await res.json()).boards;
}

console.log("== E2E PROOF: keeper solves the stuck-standup problem (LIVE) ==");
// 1. Seed (Unity shape, like LoftClient.SaveToServer pushes)
await post("/loft", { roomId: ROOM, snapshot: { boards: seedBoards } });
const before = await getBoards();
const beforeCard = before.find((b) => b.id === "kanban")?.cards?.find((c) => c.id === "standup-01");
console.log(`BEFORE: standup-01 column = ${beforeCard?.column}`);

// 2. Live keeper run — ctx loft IS the server loft; tool calls mutate it.
const loft = { roomId: ROOM, boards: before, agents: [], version: 1, updatedAt: new Date().toISOString() };
// Ground-truth tool capture: wrap TOOL_DEFS handlers (SDK message-event
// shapes differ per transport; wrapping the handlers is transport-proof).
const toolCalls = [];
for (const [name, def] of Object.entries(TOOL_DEFS)) {
  const orig = def.run.bind(def);
  def.run = async (args, ctx) => {
    toolCalls.push(`${name}(${JSON.stringify(args).slice(0, 120)})`);
    return orig(args, ctx);
  };
}
const text = await runAgent(
  "keeper",
  "Standup is over. Move card standup-01 to Done. Then assign task 'Write launch notes' to owner builder. Reply with one line confirming each action.",
  { ctx: { loft } },
);
console.log(`KEEPER SAYS: ${String(text).slice(0, 300)}`);
console.log(`TOOLS CALLED: ${toolCalls.length ? toolCalls.join(" | ") : "(none — FAIL)"}`);

// 3. Save mutated loft back (like LoftClient.SaveToServer)
await post("/loft", { roomId: ROOM, boards: loft.boards });
const after = await getBoards();
const afterCard = after.find((b) => b.id === "kanban")?.cards?.find((c) => c.id === "standup-01");
console.log(`AFTER: standup-01 column = ${afterCard?.column} (movedAt=${afterCard?.movedAt ?? "?"})`);

// 4. Handoff file for Unity headless consumption
const outPath = path.resolve(here, "../../quest_loft/Tools/e2e-result.json");
await fs.writeFile(outPath, JSON.stringify({ boards: after }, null, 2) + "\n", "utf8");
console.log(`wrote ${outPath}`);

const moved = afterCard?.column === "Done";
const assigned = toolCalls.some((t) => t.startsWith("assign_task"));
if (!moved) {
  console.error("E2E FAIL: keeper did not move the card to Done");
  process.exit(1);
}
if (!assigned) {
  console.error("E2E FAIL: keeper did not assign the follow-up task");
  process.exit(1);
}
console.log("E2E PROOF GREEN: live keeper moved the card + assigned the task on the shared loft.");
