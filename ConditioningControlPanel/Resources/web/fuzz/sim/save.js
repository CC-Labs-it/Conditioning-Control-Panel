/* ============================================================================
 * sim/save.js - one versioned save string: the run (gone at New run) and the lifetime (kept). localStorage in the
 * browser, a plain string anywhere else. Parsing never throws and never trusts the input: unknown ids are dropped,
 * numbers clamped. A v1 save (the old ladder) reads as no save.
 * ==========================================================================*/

import { ITEMS, BY_ID, FORKS } from '../data/items.js';
import { CONFIG } from '../data/config.js';
import { createRun, recalc } from './economy.js';
import { MOODS } from './moods.js';

export const SAVE_KEY = 'fuzz.save.v2';
export const SAVE_VERSION = 2;
export const SAVE_EVERY_MS = 5000;

export const ENDING_IDS = Object.freeze(ITEMS.filter((i) => i.kind === 'end').map((i) => i.id));

const num = (v, lo = 0, hi = Number.MAX_SAFE_INTEGER) => (Number.isFinite(v) ? Math.min(hi, Math.max(lo, v)) : lo);
const obj = (v) => (v && typeof v === 'object' && !Array.isArray(v) ? v : {});

/** What outlives a run: every ending seen, what the After lane needs. */
export function createLife() {
  return {
    age: false,        // said yes to the age box at least once (it is asked again on every load)
    contract: false,   // signed the long contract; later visits get the short update window
    updates: 0,        // update windows accepted: picks the next llama line
    runs: 0,           // runs finished
    endings: {},       // ending id -> times seen
    seen: {},          // unlock id -> 1, ever owned
    routes: [],        // last runs, newest first: {route, ending, ms}
    bestMs: 0,
    perk: null,        // a Look the next run starts owning
    store: {},         // ctx.store for After lane entries
  };
}

/** Called once when a run reaches an ending. Pure on the two objects. */
export function recordEnding(life, s, id) {
  if (!id) return life;
  life.runs += 1;
  life.endings[id] = (life.endings[id] || 0) + 1;
  noteSeen(life, s);
  life.routes = [{ route: Object.values(s.picks), ending: id, ms: Math.round(s.clock) }, ...life.routes].slice(0, 20);
  if (!life.bestMs || s.clock < life.bestMs) life.bestMs = Math.round(s.clock);
  return life;
}

export function noteSeen(life, s) {
  for (const id of s.order) life.seen[id] = 1;
  return life;
}

export function serialize(run, life, extras = {}) {
  return JSON.stringify({ v: SAVE_VERSION, run, life, extras });
}

function parseRun(src) {
  if (!src || typeof src !== 'object') return null;
  const s = createRun({ pace: num(src.pace, 1, CONFIG.MAX_PACE), seed: num(src.seed, 0, 2 ** 32), mood: MOODS[src.mood] ? src.mood : null });
  s.clock = num(src.clock);
  s.fuzz = num(src.fuzz);
  s.total = Math.max(s.fuzz, num(src.total));
  s.taps = num(src.taps);
  s.walls = num(src.walls);
  s.buys = num(src.buys);
  s.owned = { start: 1 };
  for (const [id, n] of Object.entries(obj(src.owned))) {
    const it = BY_ID[id];
    if (!it || it.group !== 'run') continue;
    const c = Math.floor(num(n, 0, it.grow ? it.grow.max : 1));
    if (c > 0) s.owned[id] = c;
  }
  const order = Array.isArray(src.order) ? src.order.filter((id) => typeof id === 'string' && s.owned[id]) : [];
  s.order = [...new Set(['start', ...order, ...Object.keys(s.owned)])];
  for (const [f, lane] of Object.entries(obj(src.picks))) if (FORKS[f]?.opts[lane] && s.owned[f]) s.picks[f] = lane;
  for (const [k, v] of Object.entries(obj(src.sources))) s.sources[k] = num(v);
  for (const [k, v] of Object.entries(obj(src.spent))) s.spent[k] = num(v);
  const side = obj(src.side);
  s.side = { budget: num(side.budget), asked: num(side.asked), paid: num(side.paid) };
  s.store = obj(src.store);
  s.endless = !!src.endless && !!s.owned.onemore;
  s.ended = src.ended && s.owned[src.ended.id] && BY_ID[src.ended.id]?.kind === 'end' ? { id: src.ended.id, at: num(src.ended.at) } : null;
  recalc(s);
  return s;
}

function parseLife(src) {
  const life = createLife();
  if (!src || typeof src !== 'object') return life;
  life.age = !!src.age;
  life.contract = !!src.contract;
  life.updates = Math.floor(num(src.updates));
  life.runs = Math.floor(num(src.runs));
  for (const [id, n] of Object.entries(obj(src.endings))) if (ENDING_IDS.includes(id)) life.endings[id] = Math.floor(num(n, 0, 1e6));
  for (const id of Object.keys(obj(src.seen))) if (BY_ID[id]) life.seen[id] = 1;
  if (Array.isArray(src.routes)) {
    life.routes = src.routes.slice(0, 20).filter((r) => r && typeof r === 'object').map((r) => ({
      route: Array.isArray(r.route) ? r.route.filter((x) => typeof x === 'string').slice(0, 5) : [],
      ending: String(r.ending),
      ms: num(r.ms),
    }));
  }
  life.bestMs = num(src.bestMs);
  life.perk = BY_ID[src.perk]?.lane === 'look' ? src.perk : null;
  life.store = obj(src.store);
  return life;
}

/** Returns {run, life, extras} or null when the string is not a save we can read. run can be null on its own. */
export function parse(raw) {
  if (typeof raw !== 'string' || raw.length > 400_000) return null;
  let data;
  try { data = JSON.parse(raw); } catch { return null; }
  if (!data || data.v !== SAVE_VERSION) return null;
  return { run: parseRun(data.run), life: parseLife(data.life), extras: obj(data.extras) };
}

/** Browser storage helpers: every call swallows the quota and privacy errors. */
export function load(storage) {
  try { const raw = storage?.getItem(SAVE_KEY); return raw ? parse(raw) : null; } catch { return null; }
}

export function store(storage, run, life, extras) {
  try { storage?.setItem(SAVE_KEY, serialize(run, life, extras)); return true; } catch { return false; }
}

export function forget(storage) {
  try { storage?.removeItem(SAVE_KEY); return true; } catch { return false; }
}
