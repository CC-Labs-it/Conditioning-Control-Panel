/* ============================================================================
 * bridge.js - the postMessage wire with the host (CONTRACT.md). Same hygiene as
 * arcademy/bridge.js, cut down: multi-subscriber handlers, pre-buffer of host
 * frames that land before anyone listens, queue-until-init for page frames
 * except the boot allowlist. Unhosted (plain browser) every send is a no-op and
 * `isHosted` is false, so the free web build runs on this same file.
 *
 * A phone or web shell swaps only the transport block.
 * ==========================================================================*/

export const PROTOCOL = 1;
export const BOOT_ALLOWED = new Set(['ready', 'log', 'heartbeat', 'boot-error', 'exit']);
export const HEARTBEAT_MS = 5000;

const win = typeof window !== 'undefined' ? window : null;
const webview = win && win.chrome && win.chrome.webview;
export const isHosted = !!webview;

const handlers = new Map();
const preBuffer = [];
const queue = [];
let initialized = false;

function post(frame) {
  if (!webview) return false;
  try { webview.postMessage(frame); return true; } catch { return false; }
}

export function send(type, fields = {}) {
  const frame = { type, ...fields };
  if (!initialized && !BOOT_ALLOWED.has(type)) { queue.push(frame); return false; }
  return post(frame);
}

export function on(type, fn) {
  if (!handlers.has(type)) handlers.set(type, new Set());
  handlers.get(type).add(fn);
  for (let i = preBuffer.length - 1; i >= 0; i--) {
    if (preBuffer[i].type === type) { const f = preBuffer.splice(i, 1)[0]; try { fn(f); } catch (e) { log('error', `handler ${type}: ${e?.message || e}`); } }
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

/** Boot: announce ready, start the heartbeat, resolve on init (or at once when unhosted). */
export function boot({ onInit } = {}) {
  if (webview) {
    webview.addEventListener('message', (ev) => {
      let data = ev.data;
      if (typeof data === 'string') { try { data = JSON.parse(data); } catch { return; } }
      dispatch(data);
    });
    on('init', (frame) => {
      if (frame.protocol !== PROTOCOL) { send('boot-error', { msg: `protocol ${frame.protocol} != ${PROTOCOL}` }); return; }
      markInitialized();
      onInit?.(frame);
    });
    send('ready', { protocol: PROTOCOL });
    setInterval(() => send('heartbeat'), HEARTBEAT_MS);
  } else {
    markInitialized();
    onInit?.({ protocol: PROTOCOL, hosted: false, caps: {}, mod: 'default', gentle: false, reduced: false });
  }
}

/** Test hook: feed a host frame from outside (dev/smoke). */
export const _dispatch = dispatch;
