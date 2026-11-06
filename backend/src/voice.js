// VIRTUAL LOFT — voice: transcript -> one validated intent for Unity's confirm card.
//   POST /voice (raw WAV) and POST /voice/text {text} both end in handleText().
// The LLM only proposes; everything it says is checked against the real roster,
// projects, roles, review queue and scenes. Total: nothing here throws on bad model output.
import { MAX_AGENTS, MODEL_ID_RE, SKINS } from "./agents.js";
import { HttpError } from "./orchestrator.js";
import { WRITE_TOOLS } from "./tools.js";

export const SCENES = ["beach", "scenery", "city", "galaxy", "party", "loft", "meadow", "night"];
const RISKY = new Set(["approve", "reject", "remove"]);
const IMMEDIATE = new Set(["scene", "party", "selectNext", "chat", "board", "browse", "none"]); // needsConfirm false
export const ACTIONS = ["goal", "task", "spawn", "remove", "scene", "approve", "reject", "stop", "party", "project", "browse", "selectNext", "chat", "board", "none"];
export const BOARD_TYPES = ["kanban", "review", "log", "preview", "notes", "files", "diff"];

// Spoken code words -> characters ("slash health" -> "/health", "index dot js" -> "index.js").
export function fixCodeWords(text) {
  return String(text ?? "")
    .replace(/\b(?:slash|garis miring)\s+([\w.-]+)/gi, "/$1")
    .replace(/(\/[\w.-]+)\s+(?=\/)/g, "$1")
    .replace(/\s+(?:dot|titik)\s+(js|ts|jsx|tsx|json|md|py|css|html|txt|yml|yaml|cs|sh|png|csv|xml|lock)\b/gi, ".$1")
    .replace(/(^|\s)(?:dot|titik)\s+(env|gitignore|github)\b/gi, "$1.$2")
    .replace(/\s*\b(?:underscore|garis bawah)\b\s*/gi, "_")
    .replace(/\b(?:dash dash|double dash|strip strip)\s+(\w+)/gi, "--$1")
    .replace(/\s+/g, " ")
    .trim();
}

const norm = (s) => String(s ?? "").toLowerCase().trim();
const clip = (s, n) => String(s ?? "").replace(/\s+/g, " ").trim().slice(0, n).trimEnd();
const str = (v) => (typeof v === "string" ? v.trim() : "");

// "fox", "Builder", "builder-2" -> the real agent (id, name, then animal, then unique role title).
function findAgent(agents, ref) {
  const r = norm(ref);
  if (!r) return null;
  const by = (f) => {
    const hit = agents.filter(f);
    return hit.length === 1 ? hit[0] : null;
  };
  return agents.find((a) => norm(a.id) === r) ?? by((a) => norm(a.name) === r) ?? by((a) => a.skin === r.replace(/^the /, "")) ?? by((a) => norm(a.role.title) === r);
}

function findProject(projects, ref) {
  const r = norm(ref);
  return r ? projects.find((p) => norm(p.id) === r) ?? projects.find((p) => norm(p.name) === r) ?? null : null;
}

const none = (reply) => ({ action: "none", reply: clip(reply, 240) });

// raw (any JSON the model produced) -> a valid intent. ctx = { agents, projects, roles, review, pointed }.
export function normalizeIntent(raw, ctx) {
  const o = raw && typeof raw === "object" && !Array.isArray(raw) ? raw : null;
  if (!o) return none("Sorry, I did not understand that. Try: \"ask the fox to fix the login bug\".");
  const action = str(o.action);
  if (!ACTIONS.includes(action)) return none(str(o.reply) || "Sorry, I did not catch a command in that. Say it again?");
  const agent = (...refs) => refs.map((r) => findAgent(ctx.agents, r)).find(Boolean) ?? null;
  const pointed = ctx.pointed ? ctx.agents.find((a) => a.id === ctx.pointed) : null;

  switch (action) {
    case "goal": {
      const goal = clip(o.goal ?? o.title, 4000);
      if (!goal) return none("What should the team work on?");
      if (!ctx.agents.length) return none("There are no agents yet. Say \"add an agent\" first.");
      if (pointed) return { action: "task", agentId: pointed.id, title: goal }; // pointed at one = a task for it
      return { action, goal };
    }
    case "task": {
      const a = agent(o.agentId, o.agent) ?? pointed;
      const title = clip(o.title ?? o.goal, 4000);
      if (!title) return none("What should they do?");
      if (!a) return none("Which agent should do that? Point at one, or say its name.");
      return { action, agentId: a.id, title };
    }
    case "spawn": {
      if (ctx.agents.length >= MAX_AGENTS) return none(`The team is full (${MAX_AGENTS} agents).`);
      const i = { action };
      const name = clip(o.name, 24);
      if (name) i.name = name;
      const role = ctx.roles.find((r) => norm(r.id) === norm(o.roleId)) ?? ctx.roles.find((r) => norm(r.title) === norm(o.roleId));
      if (role) i.roleId = role.id;
      const wanted = str(o.roleId);
      const description = clip(o.description || (!role && wanted ? wanted : ""), 500); // "data analyst" is a need, not a role id
      if (description) i.description = description; // no matching role: Unity can offer POST /roles/draft with this
      const skin = norm(o.skin);
      if (SKINS.includes(skin) || skin === "random") i.skin = skin;
      const model = str(o.model);
      if (model && MODEL_ID_RE.test(model)) i.model = model;
      return i;
    }
    case "remove": {
      const a = agent(o.agentId, o.agent) ?? pointed;
      return a ? { action, agentId: a.id } : none("Which agent should I remove? Point at one, or say its name.");
    }
    case "scene": {
      const id = norm(o.sceneId ?? o.scene);
      return SCENES.includes(id) ? { action, sceneId: id } : none("I know these scenes: beach, scenery, city, galaxy and party.");
    }
    case "approve":
    case "reject": {
      const t = (pointed && [...ctx.review].reverse().find((x) => x.agentId === pointed.id)) ?? ctx.review.at(-1);
      if (!t) return none("Nothing is waiting for your review right now.");
      const i = { action, taskId: t.id };
      if (action === "reject") {
        const feedback = clip(o.feedback, 2000);
        if (feedback) i.feedback = feedback;
      }
      return i;
    }
    case "stop": {
      const wanted = str(o.agentId ?? o.agent);
      const a = agent(wanted);
      if (wanted && !a) return none("I could not find that agent to stop.");
      const target = a ?? pointed;
      return target ? { action, agentId: target.id } : { action };
    }
    case "party":
      return { action, on: o.on !== false }; // "stop the party" = on:false
    case "project": {
      const p = findProject(ctx.projects, o.projectId ?? o.project ?? o.name);
      return p ? { action, projectId: p.id } : none(`Which project? I have: ${ctx.projects.map((x) => x.name).join(", ") || "none"}.`);
    }
    case "selectNext":
      return { action };
    case "chat": {
      const a = agent(o.agentId, o.agent) ?? pointed;
      const text = clip(o.text ?? o.message, 1000);
      if (!text) return none("What do you want to say?");
      return a ? { action, agentId: a.id, text } : none("Who are you talking to? Point at an agent or say its name.");
    }
    case "browse": {
      const url = clip(o.url ?? o.query, 500);
      if (!url) return none("What should I open?");
      return { action: "browse", url };
    }
    case "board": {
      const op = norm(o.op);
      const type = norm(o.type ?? o.boardType);
      if (!["open", "close"].includes(op)) return none("Do you want to open or close a board?");
      if (!BOARD_TYPES.includes(type) && !(op === "close" && type === "all")) return none(`Boards I know: ${BOARD_TYPES.join(", ")}.`);
      return { action, op, type };
    }
    default:
      return none(str(o.reply) || "Sorry, I did not catch that. Say it again?");
  }
}

// One short human sentence for the confirm card.
export function summarize(i, ctx) {
  const name = (id) => ctx.agents.find((a) => a.id === id)?.name ?? id;
  const task = (id) => ctx.review.find((t) => t.id === id);
  const q = (s) => `"${clip(s, 70)}"`;
  const s = {
    goal: () => `Give the team the goal ${q(i.goal)}`,
    task: () => `Ask ${name(i.agentId)} to ${q(i.title)}`,
    spawn: () => `Add ${i.name ?? i.role?.title ?? "a new agent"}${i.skin && i.skin !== "random" ? ` (${i.skin})` : ""}${i.roleId ? ` as ${i.roleId}` : i.description ? ` for ${q(i.description)}` : ""}`,
    remove: () => `Remove ${name(i.agentId)} from the team`,
    scene: () => `Switch to the ${i.sceneId} scene`,
    approve: () => `Approve ${q(task(i.taskId)?.title)} and merge it`,
    reject: () => `Send ${q(task(i.taskId)?.title)} back${i.feedback ? ` with feedback ${q(i.feedback)}` : ""}`,
    stop: () => (i.agentId ? `Stop ${name(i.agentId)}` : "Stop everyone"),
    party: () => (i.on ? "Start the party" : "Stop the party"),
    project: () => `Switch to project ${ctx.projects.find((p) => p.id === i.projectId)?.name ?? i.projectId}`,
    selectNext: () => "Select the next agent",
    chat: () => `Say to ${name(i.agentId)}: ${q(i.text)}`,
    browse: () => `Open ${i.url}`,
    board: () => (i.type === "all" ? "Close all boards" : `${i.op === "open" ? "Open" : "Close"} the ${i.type} board`),
    none: () => i.reply,
  };
  return clip(s[i.action](), 160);
}

export function buildPrompt(ctx, text, scene) {
  const roster = ctx.agents.map((a) => `- id=${a.id} name=${a.name} animal=${a.skin} role=${a.role.title} state=${a.state}${a.taskId ? " (busy)" : ""}`).join("\n") || "(no agents)";
  const review = ctx.review.map((t) => `- task ${t.id} by ${t.agentId}: ${clip(t.title, 90)}`).join("\n") || "(nothing waiting)";
  const system =
    "You turn a spoken command from a CEO into ONE JSON object. The CEO manages a team of animal-shaped AI agents in VR. " +
    "The speech is English, Indonesian (Bahasa Indonesia) or a mix, transcribed by a speech recognizer, so expect small mistakes. " +
    "Output ONLY the JSON object, no prose, no code fence.";
  const prompt =
    `CEO said: ${JSON.stringify(text)}\n\n` +
    `Team (use the id values):\n${roster}\n\nRoles you can give a new agent: ${ctx.roles.map((r) => `${r.id} (${r.title})`).join(", ")}\n` +
    `Projects: ${ctx.projects.map((p) => `${p.id} (${p.name})`).join(", ") || "none"}\n` +
    `Waiting for CEO review:\n${review}\n` +
    `Current scene: ${scene || "unknown"}. Scenes: beach, scenery (mountains, forest, valley), city (skyline), galaxy (space, night sky), party.\n` +
    `Pointed-at agent ("this one", "that one", or a bare instruction): ${ctx.pointed ? ctx.pointed : "none"}\n\n` +
    `Pick "action" from:\n` +
    `- goal {goal}: a big objective for the whole team\n` +
    `- task {agentId,title}: work for ONE agent. Animals mean agents ("the fox"). A bare instruction with a pointed-at agent is a task for it.\n` +
    `- spawn {name?,roleId?,description?,skin?,model?}: add an agent. For a job with no matching role ("data analyst") put it in description. skin is one of ${SKINS.join(", ")}. roleId must be one listed above, else put the need in description.\n` +
    `- remove {agentId} · stop {agentId?} (no agentId = stop everyone) · approve · reject {feedback?} (about the review queue)\n` +
    `- chat {agentId,text}: the CEO just talks to an agent (greeting, question, small talk, "how is it going"). Not an instruction to do work. Keep their words.\n` +
    `- board {op:"open"|"close",type}: show or hide a panel in the room. type is one of ${BOARD_TYPES.join(", ")} (close also takes "all"). Hints: tasks/kanban=kanban, "what's waiting"=review, activity=log, website/screen=preview, notes, folders=files, changes=diff.\n` +
    `- browse {url}: open a website, video or web search in the room's browser ("open google.com", "search for VR videos", "buka youtube"). url is the address, or the search words. This is the CEO browsing, not work for an agent.\n` +
    `- scene {sceneId} · party {on:true|false} · project {projectId} · selectNext (next agent)\n` +
    `- none {reply}: not a command or unclear; reply briefly in the CEO's language\n` +
    `Rules: keep task titles/goals as the CEO's own words, in English if mixed. Fix spoken code words: "slash health" means "/health", "dot js" means ".js". ` +
    `Indonesian hints: kucing=cat rubah=fox burung hantu=owl beruang=bear kelinci=rabbit katak=frog; buat/tambah=add; hapus=remove; setuju/terima=approve; tolak=reject; berhenti=stop; pantai=beach; gunung=scenery; kota=city; luar angkasa=galaxy. ` +
    `Never invent ids. If unsure, use none.\n` +
    `Example: {"action":"task","agentId":"builder-2","title":"add a /health route"}`;
  return { system, prompt };
}

// ask(system, prompt) -> model text; extract(text) -> parsed JSON or null.
// draft(description) -> {title,instructions,tools}: writes a role for a spawn with no matching one.
export function createVoice({ snapshot, ask, extract, stt = null, draft = null }) {
  function context({ agentId, projectId } = {}) {
    const s = snapshot();
    if (agentId && !s.agents.some((a) => a.id === agentId)) throw new HttpError(404, `no agent '${agentId}'`);
    if (projectId && !s.projects.some((p) => p.id === projectId)) throw new HttpError(404, `no project '${projectId}'`);
    return { agents: s.agents, projects: s.projects, roles: s.roles, review: s.tasks.filter((t) => t.status === "review"), pointed: agentId || null, projectId: projectId || null };
  }

  // A spawn with no matching role gets an AI-written one, so the headset can spawn it in one POST /agents.
  async function withRole(intent) {
    try {
      const role = await draft(intent.description);
      return { ...intent, role, writes: role.tools.filter((t) => WRITE_TOOLS.includes(t)) };
    } catch {
      return intent; // still valid: POST /agents {description} drafts the role on the server
    }
  }

  // -> { transcript, summary, needsConfirm, risky, intent }. Never throws for model problems.
  async function interpret(transcript, ctx, scene) {
    const text = fixCodeWords(transcript);
    const t0 = Date.now();
    let intent;
    let warning;
    if (!text) intent = none("I did not hear anything. Hold the button and say it again.");
    else {
      try {
        const { system, prompt } = buildPrompt(ctx, text, scene);
        intent = normalizeIntent(extract(await ask(system, prompt)), ctx);
        if (intent.action === "spawn" && !intent.roleId && intent.description && draft) intent = await withRole(intent);
      } catch (err) {
        warning = String(err?.message ?? err).slice(0, 160);
        intent = none("I could not reach my language model just now. Please try again.");
      }
    }
    const risky = RISKY.has(intent.action) || (intent.action === "stop" && !intent.agentId);
    const out = {
      ok: true, transcript: text, summary: summarize(intent, ctx),
      needsConfirm: !IMMEDIATE.has(intent.action), risky, intent, intentMs: Date.now() - t0,
    };
    if (warning) out.warning = `llm: ${warning}`;
    return out;
  }

  function checkScene(scene) {
    if (scene && !SCENES.includes(norm(scene))) throw new HttpError(400, `unknown scene '${scene}' (${SCENES.join("|")})`);
    return scene ? norm(scene) : "";
  }

  async function handleText({ text, agentId, projectId, scene } = {}) {
    if (typeof text !== "string" || !text.trim()) throw new HttpError(400, "text is required");
    const ctx = context({ agentId, projectId });
    return interpret(clip(text, 2000), ctx, checkScene(scene));
  }

  async function handleAudio(wav, { agentId, projectId, scene } = {}) {
    if (!stt) throw new HttpError(503, "speech-to-text is not configured");
    const ctx = context({ agentId, projectId });
    const sc = checkScene(scene);
    const hint = `Virtual Loft. ${ctx.agents.map((a) => a.name).join(", ")}. ${SKINS.join(", ")}. slash health, dot js.`;
    let heard;
    try {
      heard = await stt.transcribe(wav, { hint });
    } catch (err) {
      throw err instanceof HttpError || err?.status ? err : new HttpError(502, `speech-to-text failed: ${String(err?.message ?? err).slice(0, 200)}`);
    }
    const out = await interpret(heard.text, ctx, sc);
    return { ...out, sttMs: heard.ms, audioSeconds: Number(heard.seconds.toFixed(2)), ...(heard.text !== out.transcript && { rawTranscript: heard.text }) };
  }

  return { handleText, handleAudio, interpret, context };
}
