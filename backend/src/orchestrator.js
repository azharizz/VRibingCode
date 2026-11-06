// VIRTUAL LOFT — orchestrator: agent roster (spawn 1..20) + per-agent task
// queues + live events. Agents run in PARALLEL; each agent works its own
// queue one task at a time. Finished work waits in "review" for the CEO to
// approve or reject (reject = re-queue with the CEO's feedback).
//
// Task status: queued -> running -> review -> approved | rejected
//                                  \-> failed      (any) -> stopped
// Agent state (drives the animal animation in Unity): idle|working|done|failed
// Agent = { id, name, skin, model, role:{title,instructions,tools}, usage }.
// Everything about an agent is free: any animal, any model id, any role.
//
// ponytail: state is in-memory (restart = fresh team); persist snapshot() to
// loftStore if sessions must survive restarts.
// ponytail: all agents share one workspace; give each agent its own git
// worktree if parallel builders start stepping on each other's files.

import { EventEmitter } from "node:events";
import { DEFAULT_AGENTS, defaultModel, MAX_AGENTS, MODEL_ID_RE, normalizeRole, PRESET_ROLES, SKINS } from "./agents.js";
import { TOOL_NAMES, WRITE_TOOLS } from "./tools.js";

export class HttpError extends Error {
  constructor(status, message) {
    super(message);
    this.status = status;
  }
}

const now = () => new Date().toISOString();
const errText = (e) => String((e && e.message) || e).slice(0, 1000);

function cleanText(s, max, field) {
  const t = String(s ?? "").trim();
  if (!t) throw new HttpError(400, `${field} is required`);
  return t.slice(0, max);
}

const slug = (s) => String(s).toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "").slice(0, 32) || "role";
const blankUsage = () => ({ requests: 0, inputTokens: 0, outputTokens: 0, costUsd: null });

export function createOrchestrator({
  runTask, planGoal, draftRole = null, priceOf = async () => null, workspace,
  maxAgents = MAX_AGENTS, defaults = DEFAULT_AGENTS, roles: savedRoles = [], saveRoles = () => {},
}) {
  const events = new EventEmitter();
  events.setMaxListeners(100); // one listener per connected headset/viewer
  const agents = new Map();
  const tasks = new Map();
  const running = new Map(); // agentId -> AbortController
  const roles = new Map([...PRESET_ROLES, ...savedRoles].map((r) => [r.id, { ...r }]));
  let seq = 0;

  const emit = (e) => events.emit("event", e);
  const setAgent = (a, patch) => {
    Object.assign(a, patch);
    emit({ type: "agent", agent: { ...a } });
  };
  const setTask = (t, patch) => {
    Object.assign(t, patch, { updatedAt: now() });
    emit({ type: "task", task: { ...t } });
  };
  const needAgent = (id) => {
    const a = agents.get(id);
    if (!a) throw new HttpError(404, `no agent '${id}'`);
    return a;
  };
  const needTask = (id) => {
    const t = tasks.get(id);
    if (!t) throw new HttpError(404, `no task '${id}'`);
    return t;
  };

  // ---- roles: presets + AI-written ones the CEO saved ----
  function cleanRole(r) {
    const role = normalizeRole(r);
    if (!r || !String(r.instructions ?? "").trim()) throw new HttpError(400, "role needs instructions");
    return role;
  }

  function saveRole(r) {
    const role = cleanRole(r);
    let id = r.id && roles.has(r.id) ? r.id : slug(role.title);
    if (!r.id || !roles.has(r.id)) for (let n = 2; roles.has(id); n++) id = `${slug(role.title)}-${n}`;
    const saved = { id, ...role };
    roles.set(id, saved);
    saveRoles([...roles.values()].filter((x) => !PRESET_ROLES.some((p) => p.id === x.id)));
    emit({ type: "roles", roles: [...roles.values()] });
    return saved;
  }

  function deleteRole(id) {
    if (PRESET_ROLES.some((p) => p.id === id)) throw new HttpError(409, "preset roles can't be deleted");
    if (!roles.delete(id)) throw new HttpError(404, `no role '${id}'`);
    saveRoles([...roles.values()].filter((x) => !PRESET_ROLES.some((p) => p.id === x.id)));
    emit({ type: "roles", roles: [...roles.values()] });
  }

  async function draft(description) {
    const d = String(description ?? "").trim().slice(0, 1000);
    if (!d) throw new HttpError(400, "describe what the agent should do");
    if (!draftRole) throw new HttpError(501, "role drafting not configured");
    const role = await draftRole(d);
    return { ...role, writes: role.tools.filter((t) => WRITE_TOOLS.includes(t)) }; // CEO must see these
  }

  function pickSkin(skin) {
    if (SKINS.includes(skin)) return skin;
    const used = new Set([...agents.values()].map((a) => a.skin));
    const free = SKINS.filter((x) => !used.has(x));
    const pool = free.length ? free : SKINS;
    return pool[Math.floor(Math.random() * pool.length)]; // "random" / missing: unused animals first
  }

  // spawn({ name?, skin?: id|"random", model?, roleId? | role:{title,instructions,tools} })
  function spawn({ name, skin, model, roleId, role } = {}) {
    if (agents.size >= maxAgents) throw new HttpError(409, `agent limit reached (${maxAgents})`);
    const m = String(model ?? "").trim() || defaultModel();
    if (!MODEL_ID_RE.test(m)) throw new HttpError(400, `invalid model id '${m}'`);
    let r;
    if (role) r = cleanRole(role);
    else if (roleId) {
      r = roles.get(roleId);
      if (!r) throw new HttpError(400, `unknown role '${roleId}' (have ${[...roles.keys()].join("|")})`);
    } else r = roles.get("builder");
    const n = ++seq;
    const a = {
      id: `${slug(r.title)}-${n}`,
      name: String(name ?? "").trim().slice(0, 24) || `${r.title} ${n}`,
      skin: pickSkin(skin),
      model: m,
      role: { title: r.title, instructions: r.instructions, tools: [...r.tools] },
      state: "idle",
      taskId: null,
      usage: blankUsage(),
    };
    agents.set(a.id, a);
    emit({ type: "agent", agent: { ...a } });
    return { ...a };
  }

  function remove(id) {
    needAgent(id);
    stop(id);
    agents.delete(id);
    emit({ type: "agent.removed", id });
  }

  function assign(agentId, title) {
    const a = needAgent(agentId);
    const t = {
      id: `t${++seq}`,
      agentId,
      title: cleanText(title, 4000, "title"),
      status: "queued",
      last: "",
      result: "",
      createdAt: now(),
      updatedAt: now(),
    };
    tasks.set(t.id, t);
    emit({ type: "task", task: { ...t } });
    pump(a);
    return { ...t };
  }

  async function pump(a) {
    if (running.has(a.id) || !agents.has(a.id)) return;
    const t = [...tasks.values()].find((x) => x.agentId === a.id && x.status === "queued");
    if (!t) return;
    const ac = new AbortController();
    running.set(a.id, ac);
    setTask(t, { status: "running" });
    setAgent(a, { state: "working", taskId: t.id });
    try {
      const out = await runTask({
        agent: a,
        title: t.title,
        workspace,
        abortController: ac,
        onProgress: (line) => {
          if (!ac.signal.aborted) setTask(t, { last: String(line).slice(0, 200) });
        },
      });
      const text = typeof out === "string" ? out : out?.text ?? "";
      const usage = await addUsage(a, out?.usage);
      if (!ac.signal.aborted) setTask(t, { status: "review", result: String(text).slice(0, 8000), usage });
    } catch (err) {
      if (!ac.signal.aborted) setTask(t, { status: "failed", result: errText(err) });
    } finally {
      running.delete(a.id);
      if (agents.has(a.id)) {
        const state = t.status === "review" ? "done" : t.status === "failed" ? "failed" : "idle";
        setAgent(a, { state, taskId: null });
        pump(a);
      }
    }
  }

  // Tokens always; $ when the model's price is known (OpenRouter catalogue).
  async function addUsage(a, u) {
    if (!u) return null;
    const price = await priceOf(a.model).catch(() => null);
    const cost = price ? u.inputTokens * price.in + u.outputTokens * price.out : null;
    const total = a.usage;
    total.requests += u.requests ?? 0;
    total.inputTokens += u.inputTokens ?? 0;
    total.outputTokens += u.outputTokens ?? 0;
    if (cost != null) total.costUsd = (total.costUsd ?? 0) + cost;
    return { ...u, costUsd: cost };
  }

  function stop(agentId) {
    if (agentId) needAgent(agentId);
    for (const t of tasks.values()) {
      if ((!agentId || t.agentId === agentId) && (t.status === "queued" || t.status === "running")) {
        setTask(t, { status: "stopped" });
      }
    }
    for (const [id, ac] of running) if (!agentId || id === agentId) ac.abort();
  }

  function approve(taskId) {
    const t = needTask(taskId);
    if (t.status !== "review") throw new HttpError(409, `task '${taskId}' is ${t.status}, not in review`);
    setTask(t, { status: "approved" });
    const a = agents.get(t.agentId);
    if (a && a.state === "done") setAgent(a, { state: "idle" });
    return { ...t };
  }

  function reject(taskId, feedback) {
    const t = needTask(taskId);
    if (t.status !== "review" && t.status !== "failed") {
      throw new HttpError(409, `task '${taskId}' is ${t.status}, nothing to reject`);
    }
    setTask(t, { status: "rejected" });
    if (!agents.has(t.agentId)) return null;
    const note = String(feedback ?? "").trim().slice(0, 2000) || "Not good enough yet, try again.";
    return assign(
      t.agentId,
      `${t.title}\n\nCEO feedback on your previous attempt: ${note}\nPrevious result:\n${t.result.slice(0, 2000)}`,
    );
  }

  async function goal(text) {
    const g = cleanText(text, 4000, "goal");
    if (!agents.size) throw new HttpError(409, "no agents — spawn one first");
    emit({ type: "goal", goal: g, status: "planning" });
    let plan;
    try {
      const roster = [...agents.values()].map(({ id, name, role }) => ({ id, name, role }));
      plan = await planGoal({ goal: g, agents: roster, workspace });
    } catch (err) {
      emit({ type: "goal", goal: g, status: "failed", error: errText(err) });
      throw err;
    }
    const created = plan.filter((p) => agents.has(p.agentId)).map((p) => assign(p.agentId, p.title));
    emit({ type: "goal", goal: g, status: "planned", taskIds: created.map((t) => t.id) });
    return created;
  }

  // Party mode is shared: every connected headset gets the same moment.
  let party = false;
  function setParty(on) {
    party = Boolean(on);
    emit({ type: "party", on: party });
    return party;
  }

  function snapshot() {
    return {
      type: "snapshot",
      maxAgents,
      party,
      defaultModel: defaultModel(),
      skins: SKINS,
      tools: TOOL_NAMES,
      writeTools: WRITE_TOOLS,
      roles: [...roles.values()],
      agents: [...agents.values()].map((a) => ({ ...a })),
      tasks: [...tasks.values()].slice(-100).map((t) => ({ ...t })),
    };
  }

  for (const d of defaults) spawn(d);
  return { events, spawn, remove, assign, goal, approve, reject, stop, snapshot, saveRole, deleteRole, draft, setParty };
}
