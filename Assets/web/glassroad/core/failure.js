/* core/failure.js - honest failures: one record per boot failure, device loss or dead frame loop. Pure.
 *
 * The record names the course, the backend and tier, the loading stage reached and the error (message plus the
 * top of its stack). It is shown in small text on the loading cover (hud/loading.js fail), kept on
 * window.__glassroad.lastFailure and remembered in localStorage under FAILURE_KEY, so a tester's phone can tell
 * us what happened after a reload. Every storage access sits in try/catch (private windows, blocked storage).
 * The watchdog rule (stalled) is here too, so tests run it under bare node.
 */

export const FAILURE_KEY = 'glassroad.lastFailure';
/** The last few records, so a later refusal cannot hide the loss that caused it. */
export const FAILURE_LOG_KEY = 'glassroad.failureLog';
export const FAILURE_LOG_MAX = 4;
/** How long an earlier failure still explains this one. */
export const EARLIER_MS = 10 * 60 * 1000;
/** Chrome refuses WebGL and WebGPU to a site after a second GPU loss, for 2 minutes (gpu_data_manager_impl_private.cc,
 *  kBlockedDomainExpirationPeriod): one rebuild per window, because a second one only feeds that block. */
export const REBUILD_WINDOW_MS = 2 * 60 * 1000;
const REBUILDS = ['lost', 'stalled', 'frame'];

/** A short reason line from anything thrown: the message, or the value itself, trimmed. */
export function reasonOf(e) {
  if (e == null) return 'unknown';
  if (typeof e === 'string') return e.trim().slice(0, 240) || 'unknown';
  const m = e.message != null ? String(e.message) : String(e);
  const name = e.name && e.name !== 'Error' && !m.startsWith(e.name) ? e.name + ': ' : '';
  return (name + m).trim().slice(0, 240) || 'unknown';
}

/**
 * @param {{ kind?: string, course?: string, backend?: string, tier?: string, pinned?: boolean, stage?: string, at?: number, ua?: string }} ctx
 *   kind: 'boot' (a course failed to open), 'lost' (the GPU device or context went away), 'stalled' (frames stopped),
 *   'frame' (the frame loop threw), 'stall' (the loading bar stopped moving). stage: the last loading milestone reached.
 *   pinned: the tier came from a pinned Quality option (a phone pinned above its auto pick is the usual memory death).
 */
export function describeFailure(e, ctx = {}) {
  const stack = e && typeof e.stack === 'string' ? e.stack.split(/\r?\n/).map((l) => l.trim()).filter(Boolean).slice(0, 4).join(' | ').slice(0, 600) : '';
  return {
    v: 1, kind: ctx.kind || 'boot', at: Number.isFinite(ctx.at) ? ctx.at : Date.now(),
    course: ctx.course || null, backend: ctx.backend || null, tier: ctx.tier || null, pinned: ctx.pinned === true, stage: ctx.stage || null,
    reason: reasonOf(e), stack, ua: typeof ctx.ua === 'string' ? ctx.ua.slice(0, 200) : null,
  };
}

/** The cover's small line: the kind, the course and the reason; a pinned tier says so ("high pinned"). */
export function failureLine(rec) {
  if (!rec) return '';
  const where = [rec.course, rec.backend, rec.tier && rec.pinned ? rec.tier + ' pinned' : rec.tier].filter(Boolean).join(', ');
  const what = rec.kind === 'lost' ? 'graphics device lost' : rec.kind === 'stalled' ? 'frames stopped' : rec.kind === 'frame' ? 'frame loop threw' : rec.kind === 'stall' ? 'loading stalled' : 'boot failed';
  return what + (rec.stage ? ' after ' + rec.stage : '') + (where ? ' (' + where + ')' : '') + ': ' + rec.reason;
}

const store = () => { try { return globalThis.localStorage || null; } catch (e) { return null; } };

export function rememberFailure(rec, storage = store()) {
  try {
    storage?.setItem(FAILURE_KEY, JSON.stringify(rec));
    const { stack, ua, ...brief } = rec;
    storage?.setItem(FAILURE_LOG_KEY, JSON.stringify([...readFailureLog(storage), brief].slice(-FAILURE_LOG_MAX)));
    return true;
  } catch (e) { return false; }
}

export function readFailureLog(storage = store()) {
  try { const list = JSON.parse(storage?.getItem(FAILURE_LOG_KEY) || '[]'); return Array.isArray(list) ? list.filter((r) => r && r.v === 1) : []; } catch (e) { return []; }
}

/** The oldest recent failure before rec, said on the cover after rec's own line: usually the actual cause. */
export function earlierLine(rec, log = readFailureLog()) {
  const first = log.find((r) => r.at < rec.at && rec.at - r.at <= EARLIER_MS);
  if (!first) return '';
  const s = Math.round((rec.at - first.at) / 1000);
  return 'Earlier (' + (s < 90 ? s + ' s' : Math.round(s / 60) + ' min') + ' ago): ' + failureLine(first);
}

/** One automatic rebuild per window. Later dead worlds end on the reason and Retry. */
export function rebuildAllowed(failures, now = Date.now(), windowMs = REBUILD_WINDOW_MS) {
  return !failures.some((r) => REBUILDS.includes(r.kind) && now - r.at < windowMs);
}

export function readLastFailure(storage = store()) {
  try { const raw = storage?.getItem(FAILURE_KEY); const rec = raw ? JSON.parse(raw) : null; return rec && rec.v === 1 ? rec : null; } catch (e) { return null; }
}

/** The frame watchdog: frames should advance whenever the page is visible and nothing holds them on purpose.
 *  @param {{ nowMs: number, lastFrameMs: number, visible: boolean, visibleSinceMs?: number, covered: boolean, busy: boolean, stallMs?: number }} s
 *    covered = the loading cover is up (warm-ups pause the loop behind it), busy = a course is opening,
 *    visibleSinceMs = when the page last became visible (a hidden tab draws nothing and is never dead).
 *  @returns {boolean} true when the loop counts as dead */
export const STALL_MS = 6000;
/** The watchdog's own interval; a tick that comes more than twice this late ran after one long synchronous task. */
export const WATCH_MS = 2000;
/** True when the watchdog tick itself ran late: one synchronous task (a first-use pipeline compile, a GC on a
 *  phone; 3.6-7 s measured in #3000) held the whole main thread, so the loop was busy, not dead. The caller moves its
 *  frame stamps to now and lets the following ticks judge. */
export function blockedTick({ nowMs, lastTickMs = 0, tickMs = WATCH_MS }) {
  return lastTickMs > 0 && nowMs - lastTickMs > 2 * tickMs;
}
export function isStalled({ nowMs, lastFrameMs, visible, visibleSinceMs = 0, covered, busy, stallMs = STALL_MS, lastTickMs = 0, tickMs = WATCH_MS }) {
  if (!visible || covered || busy || !(lastFrameMs > 0)) return false;
  if (blockedTick({ nowMs, lastTickMs, tickMs })) return false;
  return nowMs - Math.max(lastFrameMs, visibleSinceMs || 0) > stallMs;
}

/** Consecutive frame exceptions before the loop is declared broken (one bad frame is a glitch, three is a dead world). */
export const FRAME_THROWS = 3;
