/* sim/ghost.js - Ghost Run (Renewal Sky lane `ghost`): a compact record of every run and the race against it. Pure, no DOM.
 * Brief: handoff-1109/TC-PRESTIGE-WIRE-BRIEF.md.
 *
 * A recording (`rec`, kept in the run's own store while the run plays) samples the run every SAMPLE_S seconds of play
 * (state.clock, never ctx.speed) and notes every purchase, chapter and Golden Button catch. A finished run is packed into
 * a few KB and kept in the lane's life store: the last finished run and the fastest one, nothing else.
 *
 * Packed run: { v, ms, end, b: 'id,id,...', bt: 'base36 second deltas joined by .', q: 'total samples, one char each',
 *               ch: [second each Journey chapter began, or null], g: [seconds of Golden Button catches] }
 */

export const SAMPLE_S = 5;
export const MAX_SAMPLES = 2880;   // 4 h of play
export const MAX_BUYS = 500;
export const MAX_GOLD = 120;
export const MAX_CHAPTERS = 16;
export const MAX_BYTES = 6000;     // one packed run, worst case; a normal 90 min run packs to about 3 KB

const ALPHA = '0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ-_';   // 64; '~' escapes a big step
const Q_PER_DECADE = 20;           // a total sample is round(log10(1 + total) * 20): steps of about 12%
const ID_RE = /^[A-Za-z0-9_]{1,40}$/;

const num = (v, lo, hi, d = 0) => (Number.isFinite(+v) ? Math.min(hi, Math.max(lo, +v)) : d);
const sec = (ms) => Math.max(0, Math.round((+ms || 0) / 1000));
export const quant = (total) => Math.max(0, Math.round(Math.log10(1 + Math.max(0, +total || 0)) * Q_PER_DECADE));
export const unquant = (q) => Math.pow(10, q / Q_PER_DECADE) - 1;

/* ---- recording ---- */

export function createRec(seed = 0) {
  return { v: 1, seed: +seed || 0, n: 0, qv: 0, q: '', b: [], bt: [], ch: [0], g: [] };
}

/** A recording loaded from the run store; anything odd starts a fresh one for `seed`. */
export function parseRec(src, seed = 0) {
  if (!src || typeof src !== 'object' || src.v !== 1 || (+src.seed || 0) !== (+seed || 0)) return createRec(seed);
  const rec = createRec(seed);
  rec.n = Math.floor(num(src.n, 0, MAX_SAMPLES));
  rec.qv = Math.floor(num(src.qv, 0, 1e5));
  rec.q = typeof src.q === 'string' ? src.q.slice(0, MAX_SAMPLES * 4) : '';
  if (Array.isArray(src.b) && Array.isArray(src.bt)) {
    for (let i = 0; i < Math.min(src.b.length, src.bt.length, MAX_BUYS); i++) {
      if (typeof src.b[i] === 'string' && ID_RE.test(src.b[i])) { rec.b.push(src.b[i]); rec.bt.push(num(src.bt[i], 0, 1e6)); }
    }
  }
  if (Array.isArray(src.ch)) rec.ch = src.ch.slice(0, MAX_CHAPTERS).map((v) => (v == null ? null : num(v, 0, 1e6)));
  if (!rec.ch.length) rec.ch = [0];
  if (Array.isArray(src.g)) rec.g = src.g.slice(0, MAX_GOLD).map((v) => num(v, 0, 1e6));
  return rec;
}

function pushQ(rec, total) {
  const q = Math.max(rec.qv, quant(total));
  const d = q - rec.qv;
  rec.q += d < 63 ? ALPHA[d] : '~' + Math.min(d, 4095).toString(36).padStart(3, '0');
  rec.qv = q;
  rec.n += 1;
}

/** Samples for every SAMPLE_S boundary the clock has passed. Answers how many were added. */
export function sample(rec, clockMs, total) {
  let added = 0;
  while (rec.n < MAX_SAMPLES && rec.n * SAMPLE_S * 1000 <= clockMs) { pushQ(rec, total); added++; }
  return added;
}

export function noteBuy(rec, clockMs, id) {
  if (rec.b.length >= MAX_BUYS || typeof id !== 'string' || !ID_RE.test(id)) return;
  rec.b.push(id);
  rec.bt.push(sec(clockMs));
}

/** The first time the run reaches chapter `k`. */
export function noteChapter(rec, clockMs, k) {
  if (!Number.isInteger(k) || k < 0 || k >= MAX_CHAPTERS) return false;
  if (rec.ch[k] != null) return false;
  while (rec.ch.length < k) rec.ch.push(null);
  rec.ch[k] = sec(clockMs);
  return true;
}

export function noteGold(rec, clockMs) {
  if (rec.g.length < MAX_GOLD) rec.g.push(sec(clockMs));
}

/** Pack a finished run. */
export function finish(rec, clockMs, endId = null) {
  let prev = 0;
  const bt = rec.bt.map((t) => { const d = Math.max(0, t - prev); prev = Math.max(prev, t); return d.toString(36); }).join('.');
  const out = { v: 1, ms: Math.max(0, Math.round(+clockMs || 0)), end: typeof endId === 'string' && ID_RE.test(endId) ? endId : null,
    b: rec.b.join(','), bt, q: rec.q, ch: rec.ch.slice(0, MAX_CHAPTERS), g: rec.g.slice(0, MAX_GOLD) };
  return capSize(out);
}

/** A packed run is never bigger than MAX_BYTES: the samples go first, then the late buys. */
function capSize(p) {
  const size = () => JSON.stringify(p).length;
  while (size() > MAX_BYTES && p.q.length > 60) p.q = p.q.slice(0, Math.floor(p.q.length * 0.8)).replace(/~[0-9a-z]{0,2}$/, '');
  while (size() > MAX_BYTES && p.b.length) {
    const ids = p.b.split(','), ts = p.bt.split('.');
    const k = Math.max(1, Math.floor(ids.length * 0.8));
    p.b = ids.slice(0, k).join(','); p.bt = ts.slice(0, k).join('.');
    if (k === 1) break;
  }
  return p;
}

/* ---- the lane's life store ---- */

/** Keep a finished packed run: it is the last one, and the best one if no finished run was faster. */
export function keep(pstore, packed) {
  if (!pstore || !packed || !(packed.ms > 0)) return pstore;
  pstore.v = 1;
  pstore.last = packed;
  const best = unpack(pstore.best);
  if (!best || packed.ms < best.ms) pstore.best = packed;
  pstore.runs = Math.floor(num(pstore.runs, 0, 1e6)) + 1;
  return pstore;
}

/** The run to race: the fastest with Best Self, else the last. Null on a first run. */
export function chooseGhost(pstore, bestSelf = false) {
  if (!pstore || typeof pstore !== 'object') return null;
  return unpack(bestSelf ? pstore.best || pstore.last : pstore.last || pstore.best);
}

/** A packed run as arrays. Never throws: anything unreadable is null. */
export function unpack(p) {
  try {
    if (!p || typeof p !== 'object' || p.v !== 1 || !(+p.ms > 0)) return null;
    const ids = typeof p.b === 'string' && p.b ? p.b.split(',') : [];
    const ds = typeof p.bt === 'string' && p.bt ? p.bt.split('.') : [];
    const buys = [];
    let t = 0;
    for (let i = 0; i < Math.min(ids.length, ds.length, MAX_BUYS); i++) {
      const d = parseInt(ds[i], 36);
      if (!Number.isFinite(d) || d < 0) break;
      t += d;
      if (ID_RE.test(ids[i])) buys.push({ t, id: ids[i] });
    }
    const totals = [];
    const q = typeof p.q === 'string' ? p.q : '';
    let qv = 0;
    for (let i = 0; i < q.length && totals.length < MAX_SAMPLES; i++) {
      if (q[i] === '~') { const d = parseInt(q.slice(i + 1, i + 4), 36); if (!Number.isFinite(d)) break; qv += d; i += 3; }
      else { const d = ALPHA.indexOf(q[i]); if (d < 0) break; qv += d; }
      totals.push(unquant(qv));
    }
    const ch = Array.isArray(p.ch) ? p.ch.slice(0, MAX_CHAPTERS).map((v) => (v == null || !Number.isFinite(+v) ? null : +v)) : [0];
    const g = Array.isArray(p.g) ? p.g.slice(0, MAX_GOLD).map(Number).filter(Number.isFinite).sort((a, b) => a - b) : [];
    return { ms: +p.ms, end: typeof p.end === 'string' ? p.end : null, buys, totals, ch, g };
  } catch { return null; }
}

/* ---- the race ---- */

/** Seconds the player is ahead of the ghost (negative = behind), measured on purchases, which every Depth shares
 *  (pay and prices scale together). `count` = purchases the player made this run, `tSec` = seconds played. */
export function gapSeconds(ghost, count, tSec) {
  if (!ghost) return 0;
  const reached = count > 0 ? ghost.buys[count - 1] : { t: 0 };
  const next = ghost.buys[count];
  if (!reached) return Math.round(ghost.ms / 1000 - tSec) > 0 ? Math.round(ghost.ms / 1000 - tSec) : 0;
  if (tSec < reached.t) return Math.round(reached.t - tSec);          // got here before the ghost did
  if (next && tSec > next.t) return -Math.round(tSec - next.t);       // the ghost already made the next one
  if (!next && tSec * 1000 > ghost.ms) return -Math.round(tSec - ghost.ms / 1000);  // the ghost finished
  return 0;
}

/** "1:20", "12:05", "1:02:09". */
export function fmtGap(s) {
  s = Math.abs(Math.round(+s || 0));
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), r = String(s % 60).padStart(2, '0');
  return h ? `${h}:${String(m).padStart(2, '0')}:${r}` : `${m}:${r}`;
}

/** The chip's line, player-relative. */
export function gapLine(s) {
  if (Math.abs(s) < 1) return 'Level with your ghost';
  return s > 0 ? `${fmtGap(s)} ahead of your ghost` : `${fmtGap(s)} behind your ghost`;
}

/** The ghost's purchases made by `tSec`, for its spot on the map. */
export function ghostBuysAt(ghost, tSec) {
  if (!ghost) return [];
  let i = 0;
  while (i < ghost.buys.length && ghost.buys[i].t <= tSec) i++;
  return ghost.buys.slice(0, i);
}

/** Hints: the first thing the ghost bought that the player does not own yet. */
export function ghostNext(ghost, owned = {}) {
  if (!ghost) return null;
  for (const b of ghost.buys) if (!(owned[b.id] > 0)) return b.id;
  return null;
}

/** Did the player reach chapter k before the ghost? (A chapter the ghost never reached counts.) */
export function beatChapter(ghost, k, tSec) {
  if (!ghost || !(k > 0)) return false;
  const g = ghost.ch[k];
  return g == null || tSec < g;
}

/** Golden Buttons the ghost caught in (t0, t1]. */
export function goldsBetween(ghost, t0, t1) {
  if (!ghost) return 0;
  return ghost.g.filter((t) => t > t0 && t <= t1).length;
}

/* ---- what each star does ---- */

/** Stars by level (data/prestige.js `ghost` lane): 1 race, 2/3/5/8 chapter pay, 4/9 taps, 6 hints, 7 best self, 10 replay. */
export function perks(level) {
  const L = Math.floor(num(level, 0, 10));
  return {
    race: L >= 1,
    beat: L >= 8 ? 0.2 : L >= 5 ? 0.15 : L >= 3 ? 0.1 : L >= 2 ? 0.05 : 0,
    taps: L >= 9 ? 2 : L >= 4 ? 1 : 0,   // per second
    hints: L >= 6,
    best: L >= 7,
    replay: L >= 10,
  };
}
