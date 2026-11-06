// VIRTUAL LOFT — loft layout store (cloud JSON, single source of truth).
// ESM. Shape: { roomId, boards, agents, version, updatedAt }.
//
// Strategy: Supabase upsert when SUPABASE_URL + SUPABASE_ANON_KEY are set,
// otherwise (or on any cloud error) fall back to a local JSON file so the
// Simulator travel test (kos -> cafe) always works offline.
// Table `lofts` has a UNIQUE(room_id); the full layout lives in `layout` (jsonb).

import { promises as fs } from "node:fs";
import path from "node:path";

export function blankLoft(roomId = "focus-loft") {
  return {
    roomId,
    boards: [],
    agents: [],
    version: 1,
    updatedAt: new Date().toISOString(),
  };
}

export function validateLoft(loft) {
  if (!loft || typeof loft !== "object") throw new Error("loft must be an object");
  for (const k of ["roomId", "boards", "agents", "version", "updatedAt"]) {
    if (!(k in loft)) throw new Error(`loft missing field "${k}"`);
  }
  if (!Array.isArray(loft.boards)) throw new Error("loft.boards must be an array");
  if (!Array.isArray(loft.agents)) throw new Error("loft.agents must be an array");
  return loft;
}

function localPath(roomId, localDir) {
  const dir = localDir ?? path.resolve(process.cwd(), "backend/data/lofts");
  return path.join(dir, `${roomId}.json`);
}

async function loadLocal(roomId, localDir) {
  try {
    const raw = await fs.readFile(localPath(roomId, localDir), "utf8");
    return validateLoft(JSON.parse(raw));
  } catch (err) {
    if (err?.code === "ENOENT") return null;
    throw err;
  }
}

async function saveLocal(loft, localDir) {
  const p = localPath(loft.roomId, localDir);
  await fs.mkdir(path.dirname(p), { recursive: true });
  await fs.writeFile(p, JSON.stringify(loft, null, 2) + "\n", "utf8");
  return loft;
}

function supabaseEnv() {
  const url = process.env.SUPABASE_URL;
  const key = process.env.SUPABASE_ANON_KEY;
  return url && key ? { url, key } : null;
}

async function supabaseClient() {
  const env = supabaseEnv();
  if (!env) return null;
  const { createClient } = await import("@supabase/supabase-js");
  return createClient(env.url, env.key);
}

function toRow(loft) {
  return {
    room_id: loft.roomId,
    layout: { boards: loft.boards, agents: loft.agents },
    version: loft.version,
    updated_at: loft.updatedAt,
  };
}

function fromRow(row, roomId) {
  return validateLoft({
    roomId: row.room_id ?? roomId,
    boards: row.layout?.boards ?? [],
    agents: row.layout?.agents ?? [],
    version: row.version ?? 1,
    updatedAt: row.updated_at ?? new Date().toISOString(),
  });
}

// Load a loft: cloud first, local-file fallback. Returns null when missing
// in both places.
export async function loadLoft(roomId, opts = {}) {
  const sb = await supabaseClient().catch(() => null);
  if (sb && !opts.localOnly) {
    try {
      const { data, error } = await sb
        .from("lofts")
        .select("room_id, layout, version, updated_at")
        .eq("room_id", roomId)
        .maybeSingle();
      if (error) throw error;
      if (data) return fromRow(data, roomId);
    } catch (err) {
      if (!opts.allowFallback) throw err;
      // fall through to local
    }
  }
  return loadLocal(roomId, opts.localDir);
}

// Save a loft: bump version + updatedAt, upsert to cloud, mirror to local.
// Cloud failure falls back to local-only (still resolves) unless
// opts.strictCloud is set.
export async function saveLoft(loft, opts = {}) {
  const next = validateLoft({
    ...loft,
    version: (Number(loft.version) || 0) + 1,
    updatedAt: new Date().toISOString(),
  });
  const sb = await supabaseClient().catch(() => null);
  if (sb && !opts.localOnly) {
    try {
      const { error } = await sb.from("lofts").upsert(toRow(next), {
        onConflict: "room_id",
      });
      if (error) throw error;
    } catch (err) {
      if (opts.strictCloud) throw err;
      // fall through: local mirror keeps the travel test green offline
    }
  }
  return saveLocal(next, opts.localDir);
}
