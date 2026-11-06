// VIRTUAL LOFT — backend smoke test (offline, no network, no credentials).
// 1. Loads quest_loft/Assets/Boards/LoftLayout.json when it exists,
//    else falls back to an inline sample loft.
// 2. Saves + reloads it (travel-reload roundtrip) via local-file fallback.
// 3. Routes ONE fake task per agent role (scout/builder/keeper) offline.
// Usage: npm test | npm run demo   (from backend/)

import assert from "node:assert/strict";
import { promises as fs } from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { AGENT_ROLES, routeOfflineTask } from "../src/agents.js";
import { loadLoft, saveLoft, validateLoft } from "../src/loftStore.js";

const here = path.dirname(fileURLToPath(import.meta.url));
const LAYOUT_CANDIDATE = path.resolve(
  here,
  "../../quest_loft/Assets/Boards/LoftLayout.json",
);

function sampleLoft() {
  return {
    roomId: "focus-loft",
    boards: [
      {
        id: "kanban-main",
        type: "kanban",
        wall: "main",
        pos: { x: 0, y: 1.5, z: 0 },
        size: { w: 4, h: 2 },
        content: {
          cards: [{ id: "card-1", title: "Morning standup", column: "Doing" }],
        },
      },
      {
        id: "notes-1",
        type: "notes",
        wall: "main",
        pos: { x: -4, y: 1.5, z: 0 },
        size: { w: 3, h: 2 },
        content: { text: "Infinite wall, zero mess." },
      },
    ],
    agents: [
      { id: "scout", desk: "desk-1" },
      { id: "builder", desk: "desk-2" },
      { id: "keeper", desk: "desk-3" },
    ],
    version: 1,
    updatedAt: new Date().toISOString(),
  };
}

async function loadQuestLayout() {
  try {
    const raw = await fs.readFile(LAYOUT_CANDIDATE, "utf8");
    console.log(`layout source: ${LAYOUT_CANDIDATE}`);
    return validateLoft(JSON.parse(raw));
  } catch (err) {
    if (err?.code !== "ENOENT") throw err;
    console.log("layout source: inline sample (quest_loft not present yet)");
    return validateLoft(sampleLoft());
  }
}

const FAKE_TASKS = {
  scout: "summarize Quest vs Immersed for traveling founders",
  builder: "run landing test",
  keeper: "move morning standup card to Done",
};

const results = {};
const loft = await loadQuestLayout();

// Travel-reload roundtrip through an isolated temp dir (local fallback,
// no Supabase env set -> never touches the network).
const tmp = await fs.mkdtemp(path.join(os.tmpdir(), "loft-demo-"));
const saved = await saveLoft(loft, { localOnly: true, localDir: tmp });
const reloaded = await loadLoft(loft.roomId, { localOnly: true, localDir: tmp });
assert.deepEqual(reloaded.boards, saved.boards, "travel reload: boards differ");
assert.deepEqual(reloaded.agents, saved.agents, "travel reload: agents differ");
assert.equal(reloaded.version, saved.version, "travel reload: version differs");
console.log(
  `PASS travel-reload roomId=${saved.roomId} version=${saved.version} boards=${saved.boards.length}`,
);

// One fake task per role, offline.
assert.deepEqual([...AGENT_ROLES].sort(), ["builder", "keeper", "scout"]);
for (const role of AGENT_ROLES) {
  const res = await routeOfflineTask(role, FAKE_TASKS[role], { loft: saved });
  assert.equal(res.ok, true, `${role}: tool result not ok`);
  results[role] = res;
  console.log(`PASS ${role}: ${res.tool} ok`);
}

console.log(`\nSMOKE OK — 1 travel-reload + ${AGENT_ROLES.length} agent routes, no network used.`);
export { results };
