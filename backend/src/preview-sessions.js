// VIRTUAL LOFT — a real browser in VR: a live Chromium page the headset sees as a stream of
// JPEG frames (CDP screencast) and drives with click/scroll/key/text/goto/back/forward/reload.
// Capped (3), closed when idle.
//
// SECURITY: this is a browser the network can steer. Only http(s) URLs; cloud metadata,
// link-local and file: are blocked for the first URL AND every redirect/subrequest
// (also by DNS name); a task's preview may only navigate within its own origin.
// A public website must never reach this machine: the backend on loopback can run shell
// commands. Private and loopback addresses are blocked, except the one local origin the
// CEO opened on purpose.
import crypto from "node:crypto";
import dns from "node:dns/promises";
import net from "node:net";
import { HttpError } from "./orchestrator.js";

export const MAX_SESSIONS = 3;
const META_HOSTS = new Set(["metadata.google.internal", "metadata", "instance-data", "instance-data.ec2.internal", "100.100.100.200", "metadata.azure.com"]);

function badIp(ip) {
  if (net.isIPv4(ip)) {
    const [a, b] = ip.split(".").map(Number);
    return (a === 169 && b === 254) || ip === "100.100.100.200" || a === 0;
  }
  const x = ip.toLowerCase();
  const mapped = x.match(/^::ffff:(\d+\.\d+\.\d+\.\d+)$/); // ::ffff:169.254.169.254
  if (mapped) return badIp(mapped[1]);
  const hex = x.match(/^::ffff:([0-9a-f]{1,4}):([0-9a-f]{1,4})$/); // the form URL() normalizes to
  if (hex) {
    const n = (parseInt(hex[1], 16) << 16) | parseInt(hex[2], 16);
    return badIp([n >>> 24, (n >>> 16) & 255, (n >>> 8) & 255, n & 255].join("."));
  }
  return x.startsWith("fe80:") || x === "fd00:ec2::254" || x === "::";
}

function privateIp(ip) {
  if (net.isIPv4(ip)) {
    const [a, b] = ip.split(".").map(Number);
    return a === 127 || a === 10 || (a === 192 && b === 168) || (a === 172 && b >= 16 && b <= 31) || (a === 100 && b >= 64 && b <= 127);
  }
  const x = ip.toLowerCase();
  const mapped = x.match(/^::ffff:(\d+\.\d+\.\d+\.\d+)$/);
  if (mapped) return privateIp(mapped[1]);
  const hex = x.match(/^::ffff:([0-9a-f]{1,4}):([0-9a-f]{1,4})$/);
  if (hex) {
    const n = (parseInt(hex[1], 16) << 16) | parseInt(hex[2], 16);
    return privateIp([n >>> 24, (n >>> 16) & 255, (n >>> 8) & 255, n & 255].join("."));
  }
  return x === "::1" || /^f[cd]/.test(x);
}

export async function isPrivate(u, { lookup = dns.lookup } = {}) {
  const host = u.hostname.replace(/^\[|\]$/g, "").toLowerCase();
  if (host === "localhost" || host.endsWith(".localhost") || host.endsWith(".local")) return true;
  if (net.isIP(host)) return privateIp(host);
  const addrs = await lookup(host, { all: true }).catch(() => []);
  return addrs.some((a) => privateIp(a.address));
}

// What a person types or says -> an address. "google.com" gets https://, plain words become a search.
export function toUrl(text) {
  const t = String(text ?? "").trim();
  if (/^(javascript|data|file|ftp|blob|about|vbscript|chrome|view-source):/i.test(t)) return t; // checkUrl refuses these
  if (/^[a-z][a-z0-9+.-]*:/i.test(t) && !/^[\w.-]+:\d+(\/|$)/.test(t)) return t;
  if (!/\s/.test(t) && /^[\w-]+(\.[\w-]+)+(:\d+)?([/?#].*)?$/.test(t)) return `https://${t}`;
  if (/^(localhost|\d+\.\d+\.\d+\.\d+)(:\d+)?([/?#].*)?$/.test(t)) return `http://${t}`;
  return `https://duckduckgo.com/?q=${encodeURIComponent(t)}`;
}

// -> URL object, or throws HttpError. Resolves names so a DNS alias of the metadata IP is caught too.
export async function checkUrl(raw, { lookup = dns.lookup } = {}) {
  let u;
  try {
    u = new URL(String(raw));
  } catch {
    throw new HttpError(400, "not a valid URL");
  }
  if (!["http:", "https:"].includes(u.protocol)) throw new HttpError(400, "only http and https URLs can be opened");
  if (u.username || u.password) throw new HttpError(400, "URLs with a login are not allowed");
  const host = u.hostname.replace(/^\[|\]$/g, "").toLowerCase();
  if (META_HOSTS.has(host) || host.endsWith(".internal") || (net.isIP(host) && badIp(host))) throw new HttpError(403, "that address is blocked");
  if (!net.isIP(host)) {
    const addrs = await lookup(host, { all: true }).catch(() => []);
    if (addrs.some((a) => badIp(a.address))) throw new HttpError(403, "that address is blocked");
  }
  return u;
}

const num = (v, name, lo, hi) => {
  if (typeof v !== "number" || !Number.isFinite(v) || v < lo || v > hi) throw new HttpError(400, `${name} must be a number between ${lo} and ${hi}`);
  return v;
};

export function createPreviewSessions({ browser, idleMs = Number(process.env.PREVIEW_IDLE_MS) || 120_000, lookup }) {
  const sessions = new Map();

  async function close(id) {
    const s = sessions.get(id);
    if (!s) return false;
    sessions.delete(id);
    clearTimeout(s.timer);
    await s.context.close().catch(() => {});
    return true;
  }
  const touch = (s) => {
    clearTimeout(s.timer);
    s.timer = setTimeout(() => close(s.id), idleMs);
    s.timer.unref?.();
  };
  const need = (id) => {
    const s = sessions.get(id);
    if (!s) throw new HttpError(404, `no preview session '${id}'`);
    touch(s);
    return s;
  };

  // url: already-decided address (a task's own preview, or what the CEO typed). lockOrigin: task previews.
  async function open({ url, lockOrigin = false, width = 1280, height = 800, taskId = null }) {
    if (sessions.size >= MAX_SESSIONS) throw new HttpError(429, `too many live previews (max ${MAX_SESSIONS}); close one first`);
    const first = await checkUrl(toUrl(url), { lookup });
    const localOrigin = (await isPrivate(first, { lookup })) ? first.origin : null;
    const w = Math.round(num(width, "width", 320, 1920));
    const h = Math.round(num(height, "height", 240, 1200));
    const b = await browser();
    const context = await b.newContext({ viewport: { width: w, height: h }, serviceWorkers: "block", acceptDownloads: false });
    const origin = first.origin;
    await context.route("**/*", async (route) => {
      const req = route.request();
      const u = req.url();
      if (/^(data|blob|about):/i.test(u)) return route.continue();
      try {
        const parsed = await checkUrl(u, { lookup });
        if (parsed.origin !== localOrigin && (await isPrivate(parsed, { lookup }))) throw new Error("private address");
        if (lockOrigin && req.isNavigationRequest() && req.frame() === page.mainFrame() && parsed.origin !== origin) throw new Error("outside");
      } catch {
        return route.abort("blockedbyclient");
      }
      return route.continue();
    });
    const page = await context.newPage();
    const s = { id: crypto.randomBytes(8).toString("hex"), context, page, width: w, height: h, taskId, timer: null, latest: null, lockOrigin };
    sessions.set(s.id, s);
    touch(s);
    // Chromium pushes a frame whenever the page changes: smooth, and idle pages cost nothing.
    const cdp = await context.newCDPSession(page);
    cdp.on("Page.screencastFrame", (f) => {
      s.latest = Buffer.from(f.data, "base64");
      cdp.send("Page.screencastFrameAck", { sessionId: f.sessionId }).catch(() => {});
    });
    await cdp.send("Page.startScreencast", { format: "jpeg", quality: 60, maxWidth: w, maxHeight: h, everyNthFrame: 1 });
    try {
      await page.goto(first.toString(), { waitUntil: "load", timeout: 20_000 });
    } catch (e) {
      if (!page.url() || page.url() === "about:blank") {
        await close(s.id);
        throw new HttpError(502, `could not open the page: ${String(e.message).split("\n")[0].slice(0, 160)}`);
      }
    }
    return { id: s.id, width: w, height: h };
  }

  async function frame(id) {
    const s = need(id);
    return s.latest ?? s.page.screenshot({ type: "jpeg", quality: 70 });
  }

  async function info(id) {
    const { page } = need(id);
    return { url: page.url(), title: await page.title().catch(() => "") };
  }

  async function input(id, e = {}) {
    const s = need(id);
    const { page, width, height } = s;
    switch (e.type) {
      case "click": {
        const x = num(e.x, "x", 0, width), y = num(e.y, "y", 0, height);
        await page.mouse.click(x, y, { button: ["left", "right", "middle"].includes(e.button) ? e.button : "left" });
        break;
      }
      case "move":
        await page.mouse.move(num(e.x, "x", 0, width), num(e.y, "y", 0, height));
        break;
      case "scroll":
        if (e.x !== undefined) await page.mouse.move(num(e.x, "x", 0, width), num(e.y, "y", 0, height));
        await page.mouse.wheel(num(e.dx ?? 0, "dx", -5000, 5000), num(e.dy ?? 0, "dy", -5000, 5000));
        break;
      case "key": {
        const key = String(e.key ?? "");
        if (!/^[\w+ -]{1,40}$/.test(key)) throw new HttpError(400, "bad key");
        await page.keyboard.press(key);
        break;
      }
      case "text":
        if (typeof e.text !== "string" || !e.text || e.text.length > 500) throw new HttpError(400, "text must be 1-500 characters");
        await page.keyboard.type(e.text);
        break;
      case "goto": {
        if (s.lockOrigin) throw new HttpError(403, "a task preview stays on its own page");
        const u = await checkUrl(toUrl(e.url), { lookup });
        page.goto(u.toString(), { waitUntil: "commit", timeout: 20_000 }).catch(() => {}); // the frames show progress
        break;
      }
      case "back":
        page.goBack({ waitUntil: "commit", timeout: 10_000 }).catch(() => {});
        break;
      case "forward":
        page.goForward({ waitUntil: "commit", timeout: 10_000 }).catch(() => {});
        break;
      case "reload":
        page.reload({ waitUntil: "commit", timeout: 20_000 }).catch(() => {});
        break;
      default:
        throw new HttpError(400, "type must be click|move|scroll|key|text|goto|back|forward|reload");
    }
    return { ok: true };
  }

  const list = () => [...sessions.values()].map(({ id, width, height, taskId }) => ({ id, width, height, taskId }));
  async function closeAll() {
    await Promise.all([...sessions.keys()].map(close));
  }
  return { open, frame, info, input, close, closeAll, list };
}
