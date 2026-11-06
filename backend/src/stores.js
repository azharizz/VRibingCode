// VIRTUAL LOFT — small JSON stores in the data dir: board layouts per scene, and notes.
// Same file-per-store pattern as roles.json; writes are atomic (tmp + rename).
import fs from "node:fs";
import path from "node:path";
import { HttpError } from "./orchestrator.js";

export function jsonFile(file, fallback) {
  return {
    read() {
      try {
        return JSON.parse(fs.readFileSync(file, "utf8"));
      } catch {
        return structuredClone(fallback);
      }
    },
    write(value) {
      fs.mkdirSync(path.dirname(file), { recursive: true });
      const tmp = `${file}.${process.pid}.tmp`;
      fs.writeFileSync(tmp, JSON.stringify(value, null, 2));
      fs.renameSync(tmp, file);
    },
  };
}

// ---- boards: [{id,type,pos,rot,size,args}] per scene ----
export const BOARD_TYPES = ["kanban", "review", "log", "preview", "notes", "project", "files", "diff"];
const MAX_BOARDS = 60;
const vec = (v, field, lens = [2, 3, 4]) => {
  const nums = Array.isArray(v) ? v : v && typeof v === "object" ? Object.values(v) : null; // [x,y,z] or {x,y,z[,w]}
  if (!nums || !lens.includes(nums.length) || !nums.every((n) => typeof n === "number" && Number.isFinite(n) && Math.abs(n) < 1e6)) {
    throw new HttpError(400, `board ${field} must be numbers ([x,y,z] or {x,y,z})`);
  }
  return v;
};

export function validateBoards(list) {
  if (!Array.isArray(list)) throw new HttpError(400, "boards must be an array");
  if (list.length > MAX_BOARDS) throw new HttpError(400, `too many boards (max ${MAX_BOARDS})`);
  const seen = new Set();
  return list.map((b, i) => {
    if (!b || typeof b !== "object") throw new HttpError(400, `board ${i} must be an object`);
    const id = String(b.id ?? "").trim();
    if (!/^[\w.:-]{1,64}$/.test(id)) throw new HttpError(400, `board ${i}: bad id`);
    if (seen.has(id)) throw new HttpError(400, `duplicate board id '${id}'`);
    seen.add(id);
    if (!BOARD_TYPES.includes(b.type)) throw new HttpError(400, `board '${id}': type must be ${BOARD_TYPES.join("|")}`);
    const args = b.args ?? {};
    if (typeof args !== "object" || Array.isArray(args) || JSON.stringify(args).length > 4000) throw new HttpError(400, `board '${id}': args must be a small object`);
    return { id, type: b.type, pos: vec(b.pos, "pos"), rot: vec(b.rot ?? [0, 0, 0], "rot"), size: vec(b.size ?? [1, 1], "size"), args };
  });
}

export function createBoards(file) {
  const f = jsonFile(file, {});
  const sceneId = (s) => {
    const id = String(s || "default");
    if (!/^[\w-]{1,32}$/.test(id)) throw new HttpError(400, "bad scene id");
    return id;
  };
  return {
    get: (scene) => ({ scene: sceneId(scene), boards: f.read()[sceneId(scene)] ?? [] }),
    put(scene, boards) {
      const all = f.read();
      all[sceneId(scene)] = validateBoards(boards);
      f.write(all);
      return { scene: sceneId(scene), boards: all[sceneId(scene)] };
    },
  };
}

// ---- notes ----
const MAX_NOTES = 500;
export function createNotes(file, { emit = () => {} } = {}) {
  const f = jsonFile(file, []);
  let seq = f.read().reduce((m, n) => Math.max(m, Number(String(n.id).slice(1)) || 0), 0);
  const fields = (b, partial) => {
    const out = {};
    for (const [k, max] of [["title", 120], ["text", 20000], ["color", 24]]) {
      if (b[k] === undefined) continue;
      if (typeof b[k] !== "string") throw new HttpError(400, `${k} must be a string`);
      out[k] = b[k].slice(0, max);
    }
    if (b.agentId !== undefined) out.agentId = String(b.agentId).slice(0, 64);
    if (!partial && !out.text && !out.title) throw new HttpError(400, "a note needs a title or text");
    return out;
  };
  const now = () => new Date().toISOString();
  return {
    list: () => f.read(),
    create(b = {}) {
      const all = f.read();
      if (all.length >= MAX_NOTES) throw new HttpError(409, `note limit reached (${MAX_NOTES})`);
      const n = { id: `n${++seq}`, title: "", text: "", ...fields(b, false), createdAt: now(), updatedAt: now() };
      f.write([...all, n]);
      emit({ type: "notes" });
      return n;
    },
    update(id, b = {}) {
      const all = f.read();
      const i = all.findIndex((n) => n.id === id);
      if (i < 0) throw new HttpError(404, `no note '${id}'`);
      all[i] = { ...all[i], ...fields(b, true), updatedAt: now() };
      f.write(all);
      emit({ type: "notes" });
      return all[i];
    },
    remove(id) {
      const all = f.read();
      if (!all.some((n) => n.id === id)) throw new HttpError(404, `no note '${id}'`);
      f.write(all.filter((n) => n.id !== id));
      emit({ type: "notes" });
    },
  };
}
