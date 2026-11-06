// VIRTUAL LOFT — prepares a web video for the 360 sphere: download it (every redirect
// checked, so it can't be pointed at this machine), convert it to H.264 + AAC that every
// player can open, and serve it with Range requests. Ids are random: the player can't
// send a token, so the id is the permission.
import { spawn } from "node:child_process";
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { Readable } from "node:stream";
import { pipeline } from "node:stream/promises";
import { HttpError } from "./orchestrator.js";
import { checkUrl, isPrivate } from "./preview-sessions.js";

const MAX_BYTES = 1.5 * 1024 ** 3;
const KEEP = 3; // finished videos kept on disk

export function createCinema({ dir, lookup, fetchFn = fetch, ffmpeg = process.env.FFMPEG_BIN || "ffmpeg" }) {
  const jobs = new Map(); // id -> { id, url, status, progress, error, file }
  const byUrl = new Map();
  fs.mkdirSync(dir, { recursive: true });

  async function download(job, tmp) {
    let url = job.url;
    for (let hop = 0; hop < 6; hop++) {
      const u = await checkUrl(url, { lookup });
      if (await isPrivate(u, { lookup })) throw new HttpError(403, "that address is blocked");
      const res = await fetchFn(u, { redirect: "manual", headers: { "User-Agent": "VirtualLoft/1.0" } });
      if (res.status >= 300 && res.status < 400 && res.headers.get("location")) {
        url = new URL(res.headers.get("location"), u).toString();
        continue;
      }
      if (!res.ok) throw new Error(`the site answered ${res.status}`);
      const size = Number(res.headers.get("content-length")) || 0;
      if (size > MAX_BYTES) throw new Error("that video is too large (limit 1.5 GB)");
      let got = 0;
      const counted = Readable.fromWeb(res.body);
      counted.on("data", (c) => {
        got += c.length;
        if (size) job.progress = Math.min(0.5, (got / size) * 0.5);
        if (got > MAX_BYTES) counted.destroy(new Error("that video is too large (limit 1.5 GB)"));
      });
      await pipeline(counted, fs.createWriteStream(tmp));
      return;
    }
    throw new Error("too many redirects");
  }

  // Local file only, so ffmpeg can never be steered at another address.
  const convert = (job, src, out) =>
    new Promise((resolve, reject) => {
      const args = ["-y", "-protocol_whitelist", "file", "-i", src, "-vf", "scale='min(2880,iw)':-2", "-c:v", "libx264", "-preset", "veryfast",
        "-crf", "23", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart", out];
      const child = spawn(ffmpeg, args);
      let duration = 0, err = "";
      child.stderr.on("data", (d) => {
        const s = String(d);
        err = (err + s).slice(-300);
        const hms = (m) => Number(m[1]) * 3600 + Number(m[2]) * 60 + Number(m[3]);
        const dm = s.match(/Duration: (\d+):(\d+):([\d.]+)/);
        if (dm) duration = hms(dm);
        const tm = s.match(/time=(\d+):(\d+):([\d.]+)/);
        if (tm && duration) job.progress = 0.5 + Math.min(0.5, (hms(tm) / duration) * 0.5);
      });
      child.on("error", (e) => reject(new Error(e.code === "ENOENT" ? "ffmpeg is not installed on the backend" : e.message)));
      child.on("close", (code) => (code === 0 ? resolve() : reject(new Error(`could not convert that video: ${err.slice(-120)}`))));
    });

  async function run(job) {
    const tmp = path.join(dir, `${job.id}.src`);
    const out = path.join(dir, `${job.id}.mp4`);
    try {
      await download(job, tmp);
      job.status = "converting";
      await convert(job, tmp, out);
      job.file = out;
      job.status = "ready";
      job.progress = 1;
      prune();
    } catch (e) {
      job.status = "error";
      job.error = String(e.message).slice(0, 200);
      byUrl.delete(job.url);
      fs.rmSync(out, { force: true });
    } finally {
      fs.rmSync(tmp, { force: true });
    }
  }

  function prune() {
    const ready = [...jobs.values()].filter((j) => j.status === "ready");
    for (const j of ready.slice(0, Math.max(0, ready.length - KEEP))) {
      fs.rmSync(j.file, { force: true });
      jobs.delete(j.id);
      byUrl.delete(j.url);
    }
  }

  const view = ({ id, status, progress, error }) => ({ id, status, progress, error, video: status === "ready" ? `/cinema/${id}/video.mp4` : null });

  async function prepare(url) {
    const u = await checkUrl(url, { lookup });
    if (await isPrivate(u, { lookup })) throw new HttpError(403, "that address is blocked");
    const key = u.toString();
    if (byUrl.has(key)) return view(byUrl.get(key));
    const job = { id: crypto.randomBytes(16).toString("hex"), url: key, status: "downloading", progress: 0, error: null, file: null };
    jobs.set(job.id, job);
    byUrl.set(key, job);
    run(job);
    return view(job);
  }

  function status(id) {
    const j = jobs.get(id);
    if (!j) throw new HttpError(404, "no such video");
    return view(j);
  }

  // Players seek with Range requests.
  function stream(id, req, res, headers = {}) {
    const j = jobs.get(id);
    if (!j?.file) throw new HttpError(404, "no such video");
    const size = fs.statSync(j.file).size;
    const m = String(req.headers.range ?? "").match(/^bytes=(\d*)-(\d*)$/);
    const base = { "Content-Type": "video/mp4", "Accept-Ranges": "bytes", ...headers };
    if (!m || (!m[1] && !m[2])) {
      res.writeHead(200, { ...base, "Content-Length": size });
      return fs.createReadStream(j.file).pipe(res);
    }
    const start = m[1] ? Number(m[1]) : Math.max(0, size - Number(m[2]));
    const end = m[1] && m[2] ? Math.min(Number(m[2]), size - 1) : size - 1;
    if (start >= size || start > end) {
      res.writeHead(416, { ...base, "Content-Range": `bytes */${size}` });
      return res.end();
    }
    res.writeHead(206, { ...base, "Content-Length": end - start + 1, "Content-Range": `bytes ${start}-${end}/${size}` });
    return fs.createReadStream(j.file, { start, end }).pipe(res);
  }

  return { prepare, status, stream };
}
