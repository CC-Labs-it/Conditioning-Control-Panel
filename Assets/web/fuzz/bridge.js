/* ============================================================================
 * bridge.js - the postMessage wire with the host (CONTRACT.md). Same hygiene as
 * arcademy/bridge.js, cut down: multi-subscriber handlers, pre-buffer of host
 * frames that land before anyone listens, queue-until-init for page frames
 * except the boot allowlist. Unhosted (plain browser) every send is a no-op and
 * `isHosted` is false, so the free web build runs on this same file.
 *
 * A phone or web shell swaps only the transport block. Two transports here:
 *   webview  WPF WebView2 (`window.chrome.webview`).
 *   web      the website (app.cclabs.app/tc) frames this page with `?host=web`. Only a parent on HOST_ORIGINS counts
 *            (plus a local dev door), every frame goes to that exact origin, and only that parent's frames are read.
 *            No init within INIT_TIMEOUT_MS = standalone, so a bad embed never hangs the game.
 * ==========================================================================*/

export const PROTOCOL = 1;
export const BOOT_ALLOWED = new Set(['ready', 'log', 'heartbeat', 'boot-error', 'exit']);
export const HEARTBEAT_MS = 5000;
export const INIT_TIMEOUT_MS = 4000;

/** The website origins that may host this page. A preview origin is one more line here. */
export const HOST_ORIGINS = Object.freeze([
  'https://app.cclabs.app',
]);
/** Accepted only while the page itself is served from 127.0.0.1 or localhost. */
export const DEV_HOST_ORIGINS = Object.freeze(['http://localhost:3000', 'http://127.0.0.1:3000']);

const LOCAL_PAGE = new Set(['127.0.0.1', 'localhost']);

/** Is `origin` allowed to host a page served from `pageHostname`? Exact string match only. */
export function hostOriginAllowed(origin, pageHostname = '') {
  if (typeof origin !== 'string' || !origin) return false;
  if (HOST_ORIGINS.includes(origin)) return true;
  return LOCAL_PAGE.has(pageHostname) && DEV_HOST_ORIGINS.includes(origin);
}

/** The framing parent's origin: ancestorOrigins first, else the referrer's origin, else ''. */
export function pickParentOrigin(w) {
  const first = w?.location?.ancestorOrigins?.[0];
  if (typeof first === 'string' && first && first !== 'null') return first;
  try { return new URL(w?.document?.referrer || '').origin; } catch { return ''; }
}

/** The allowed parent origin for a web-hosted page, or '' (standalone). */
export function webHostOrigin(w) {
  if (!w || !w.parent || w.parent === w) return '';
  let wanted = false;
  try { wanted = new URLSearchParams(w.location?.search || '').get('host') === 'web'; } catch { wanted = false; }
  if (!wanted) return '';
  const origin = pickParentOrigin(w);
  return hostOriginAllowed(origin, w.location?.hostname || '') ? origin : '';
}

const win = typeof window !== 'undefined' ? window : null;
const webview = win && win.chrome && win.chrome.webview;
const parentOrigin = webview ? '' : webHostOrigin(win);
/** Live: a web parent that never answers init turns this back to false before the game starts. */
export let isHosted = !!webview || !!parentOrigin;
/** Which transport is live: 'webview', 'web' or null. */
export let transport = webview ? 'webview' : parentOrigin ? 'web' : null;

const handlers = new Map();
const preBuffer = [];
const queue = [];
let initialized = false;

function post(frame) {
  if (transport === 'webview') {
    try { webview.postMessage(frame); return true; } catch { return false; }
  }
  if (transport === 'web') {
    try { win.parent.postMessage(frame, parentOrigin); return true; } catch { return false; }
  }
  return false;
}

export function send(type, fields = {}) {
  const frame = { type, ...fields };
  if (!initialized && !BOOT_ALLOWED.has(type)) { queue.push(frame); return false; }
  return post(frame);
}

export function on(type, fn) {
  if (!handlers.has(type)) handlers.set(type, new Set());
  handlers.get(type).add(fn);
  for (let i = 0; i < preBuffer.length;) {
    if (preBuffer[i].type !== type) { i++; continue; }
    const frame = preBuffer.splice(i, 1)[0];
    try { fn(frame); } catch (e) { log('error', `handler ${type}: ${e?.message || e}`); }
  }
  return () => handlers.get(type)?.delete(fn);
}

function dispatch(frame) {
  if (!frame || typeof frame.type !== 'string') return;
  const set = handlers.get(frame.type);
  if (!set || set.size === 0) { preBuffer.push(frame); return; }
  for (const fn of set) { try { fn(frame); } catch (e) { log('error', `handler ${frame.type}: ${e?.message || e}`); } }
}

export function markInitialized() {
  initialized = true;
  while (queue.length) post(queue.shift());
}

export function log(level, msg) { send('log', { level, msg: String(msg).slice(0, 500) }); }

const STANDALONE_INIT = () => ({ protocol: PROTOCOL, hosted: false, caps: {}, mod: 'default', gentle: false, reduced: false });
const parse = (data) => {
  if (typeof data !== 'string') return data;
  try { return JSON.parse(data); } catch { return null; }
};
// Node test runs must not be held open by the heartbeat or the init timer.
const unref = (t) => { t?.unref?.(); return t; };

/** Boot: announce ready, start the heartbeat, resolve on init (or at once when unhosted, or after the init timeout). */
export function boot({ onInit, initTimeoutMs = INIT_TIMEOUT_MS } = {}) {
  if (transport === 'webview') {
    webview.addEventListener('message', (ev) => { const data = parse(ev.data); if (data) dispatch(data); });
    on('init', (frame) => {
      if (frame.protocol !== PROTOCOL) { send('boot-error', { msg: `protocol ${frame.protocol} != ${PROTOCOL}` }); return; }
      markInitialized();
      onInit?.(frame);
    });
    send('ready', { protocol: PROTOCOL });
    unref(setInterval(() => send('heartbeat'), HEARTBEAT_MS));
  } else if (transport === 'web') {
    let started = false;
    win.addEventListener('message', (ev) => {
      if (transport !== 'web' || ev.origin !== parentOrigin || ev.source !== win.parent) return;
      const data = parse(ev.data);
      if (data) dispatch(data);
    });
    const beat = unref(setInterval(() => send('heartbeat'), HEARTBEAT_MS));
    const timer = unref(setTimeout(() => {
      if (started) return;
      started = true;
      clearInterval(beat);
      transport = null;
      isHosted = false;
      queue.length = 0;
      markInitialized();
      onInit?.(STANDALONE_INIT());
    }, initTimeoutMs));
    on('init', (frame) => {
      if (started) return;
      if (frame.protocol !== PROTOCOL) { send('boot-error', { msg: `protocol ${frame.protocol} != ${PROTOCOL}` }); return; }
      started = true;
      clearTimeout(timer);
      markInitialized();
      onInit?.(frame);
    });
    send('ready', { protocol: PROTOCOL });
  } else {
    markInitialized();
    onInit?.(STANDALONE_INIT());
  }
}

/** Test hook: feed a host frame from outside (dev/smoke). */
export const _dispatch = dispatch;
