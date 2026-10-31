/* meta/record.js - Record v1, the player's logbook (CONTRACT-LOGBOOK.md section 4). Frozen shape.
 *
 * One plain object: per course bests and counts, lifetime totals, the last 20 races, achievements
 * (id -> first unlock time) and unlock ids. mergeRecords is the ONE merge rule, shared with the server:
 * counters take the max, best times the smallest positive, medals the higher rank, achievements the
 * earliest time, unlocks the union, recent the union by `at` (newest 20). Merging never lowers anything.
 * cleanRecord clamps every number and drops unknown keys, so an old or hand-edited record never breaks
 * the page. Storage access is always in try/catch (private windows, blocked site data).
 *
 * The optional `garage` block (72 contract C1) is the one exception to "merging never lowers": the kart look a player
 * last chose, { state, look, updatedAt }. The newer updatedAt wins the WHOLE block, never field by field; a side
 * without one (an older client, an old server) never erases it. It is stored as sent, unclamped, so a pick this device
 * cannot wear yet survives; what is actually worn goes through hud/garageState.js cleanGarage when it is applied.
 */
import { COURSES } from '../track/catalog.js';
import { MEDAL_ORDER } from './medals.js';

export const RECORD_KEY = 'glassroad.record.v1';
export const RECENT_MAX = 20;
const NUM_MAX = 1e9;
const ID_RE = /^[a-z0-9][a-z0-9:._-]{0,63}$/;

export const COURSE_COUNTS = Object.freeze(['races', 'finishes', 'wins', 'podiums', 'trials']);
export const COURSE_BESTS = Object.freeze(['bestRaceS', 'bestLapS', 'trialBestS', 'trialBestLapS']);
export const TOTALS = Object.freeze(['races', 'finishes', 'wins', 'podiums', 'online', 'distanceM', 'driftBoosts', 'airS',
  'falls', 'rescues', 'overtakes', 'itemsLanded', 'tetherBreaks', 'wordsCaught', 'songGates', 'cleanLaps']);
const RECENT_KEYS = Object.freeze(['track', 'mode', 'place', 'of', 'timeS', 'at']);
/** The server keeps a block under this size and drops only the block otherwise; the page reads the same way. */
export const GARAGE_MAX_BYTES = 4096;
/** What this device last applied or stamped: { at: <block updatedAt>, clamped: <something in it was not wearable> }. */
export const GARAGE_SEEN_KEY = 'glassroad.garage.seen.v1';

export function emptyCourse() {
  const c = {};
  for (const k of COURSE_COUNTS) c[k] = 0;
  for (const k of COURSE_BESTS) c[k] = 0;
  c.bestSectorsS = [0, 0, 0];
  c.medal = null;
  return c;
}

export function emptyRecord() {
  const courses = {};
  for (const c of COURSES) courses[c.id] = emptyCourse();
  const totals = {};
  for (const k of TOTALS) totals[k] = 0;
  return { v: 1, courses, totals, recent: [], achievements: {}, unlocks: [], updatedAt: '' };
}

const num = (x) => (Number.isFinite(x) && x > 0 ? Math.min(x, NUM_MAX) : 0);
const int = (x) => Math.floor(num(x));
const iso = (x) => (typeof x === 'string' && x.length <= 40 && !Number.isNaN(Date.parse(x)) ? x : '');
const medal = (m) => (MEDAL_ORDER.includes(m) ? m : null);
const plain = (x) => !!x && typeof x === 'object' && !Array.isArray(x);

/** A plain copy of the block, or null when it is the wrong shape or too big (only the block goes, never the record). */
export function cleanGarageBlock(raw) {
  if (!plain(raw) || !plain(raw.state) || !plain(raw.look) || !iso(raw.updatedAt)) return null;
  try {
    const json = JSON.stringify({ state: raw.state, look: raw.look, updatedAt: raw.updatedAt });
    return new TextEncoder().encode(json).length < GARAGE_MAX_BYTES ? JSON.parse(json) : null;
  } catch (e) { return null; }
}
/** The newer block whole; a tie keeps the first. */
function newerGarage(a, b) {
  if (!a || !b) return a || b || null;
  return Date.parse(b.updatedAt) > Date.parse(a.updatedAt) ? b : a;
}

export function cleanRecord(raw) {
  const r = emptyRecord();
  if (!raw || typeof raw !== 'object' || raw.v !== 1) return r;
  for (const id of Object.keys(r.courses)) {
    const src = raw.courses && raw.courses[id];
    if (!src || typeof src !== 'object') continue;
    const c = r.courses[id];
    for (const k of COURSE_COUNTS) c[k] = int(src[k]);
    for (const k of COURSE_BESTS) c[k] = num(src[k]);
    if (Array.isArray(src.bestSectorsS)) c.bestSectorsS = [0, 1, 2].map((i) => num(src.bestSectorsS[i]));
    c.medal = medal(src.medal);
  }
  if (raw.totals && typeof raw.totals === 'object') for (const k of TOTALS) r.totals[k] = k === 'distanceM' || k === 'airS' ? num(raw.totals[k]) : int(raw.totals[k]);
  if (Array.isArray(raw.recent)) r.recent = sortRecent(raw.recent.map(cleanRecent).filter(Boolean));
  if (raw.achievements && typeof raw.achievements === 'object') {
    for (const [id, at] of Object.entries(raw.achievements)) if (ID_RE.test(id) && iso(at)) r.achievements[id] = at;
  }
  if (Array.isArray(raw.unlocks)) r.unlocks = [...new Set(raw.unlocks.filter((u) => typeof u === 'string' && ID_RE.test(u)))].sort();
  r.updatedAt = iso(raw.updatedAt);
  const garage = cleanGarageBlock(raw.garage);
  if (garage) r.garage = garage;
  return r;
}

function cleanRecent(x) {
  if (!x || typeof x !== 'object' || !COURSES.some((c) => c.id === x.track) || !iso(x.at)) return null;
  const out = {};
  for (const k of RECENT_KEYS) out[k] = x[k];
  out.mode = x.mode === 'trial' ? 'trial' : 'race';
  out.place = Math.min(int(x.place), 64); out.of = Math.min(int(x.of), 64); out.timeS = num(x.timeS);
  return out;
}
const sortRecent = (list) => {
  const seen = new Set(), out = [];
  for (const x of [...list].sort((a, b) => Date.parse(b.at) - Date.parse(a.at))) {
    const key = x.at + '|' + x.track;
    if (seen.has(key)) continue;
    seen.add(key); out.push(x);
    if (out.length >= RECENT_MAX) break;
  }
  return out;
};

const minPos = (a, b) => (a > 0 && b > 0 ? Math.min(a, b) : a > 0 ? a : b);
const higherMedal = (a, b) => (MEDAL_ORDER.indexOf(a) >= MEDAL_ORDER.indexOf(b) ? a : b);
const earlier = (a, b) => (!a ? b : !b ? a : Date.parse(a) <= Date.parse(b) ? a : b);
const later = (a, b) => (!a ? b : !b ? a : Date.parse(a) >= Date.parse(b) ? a : b);

/** The one merge rule (section 4). Both sides are cleaned first; the result is a new object. */
export function mergeRecords(a, b) {
  const x = cleanRecord(a), y = cleanRecord(b), r = emptyRecord();
  for (const id of Object.keys(r.courses)) {
    const c = r.courses[id], p = x.courses[id], q = y.courses[id];
    for (const k of COURSE_COUNTS) c[k] = Math.max(p[k], q[k]);
    for (const k of COURSE_BESTS) c[k] = minPos(p[k], q[k]);
    c.bestSectorsS = [0, 1, 2].map((i) => minPos(p.bestSectorsS[i], q.bestSectorsS[i]));
    c.medal = higherMedal(p.medal, q.medal);
  }
  for (const k of TOTALS) r.totals[k] = Math.max(x.totals[k], y.totals[k]);
  r.recent = sortRecent([...x.recent, ...y.recent]);
  for (const id of new Set([...Object.keys(x.achievements), ...Object.keys(y.achievements)])) r.achievements[id] = earlier(x.achievements[id], y.achievements[id]);
  r.unlocks = [...new Set([...x.unlocks, ...y.unlocks])].sort();
  r.updatedAt = later(x.updatedAt, y.updatedAt);   // the later one (an empty side never blanks it)
  const garage = newerGarage(x.garage, y.garage);
  if (garage) r.garage = garage;
  return r;
}

export function loadRecord(storage = defaultStorage()) {
  try { return cleanRecord(JSON.parse(storage && storage.getItem(RECORD_KEY) || 'null')); } catch (e) { return emptyRecord(); }
}

/** @returns {boolean} true when written */
export function saveRecord(record, storage = defaultStorage()) {
  try { if (!storage) return false; storage.setItem(RECORD_KEY, JSON.stringify(cleanRecord(record))); return true; } catch (e) { return false; }
}

export function loadGarageSeen(storage = defaultStorage()) {
  try {
    const s = JSON.parse(storage && storage.getItem(GARAGE_SEEN_KEY) || 'null');
    return s && iso(s.at) ? { at: s.at, clamped: s.clamped === true } : { at: '', clamped: false };
  } catch (e) { return { at: '', clamped: false }; }
}
export function saveGarageSeen(at, clamped, storage = defaultStorage()) {
  try { if (storage) storage.setItem(GARAGE_SEEN_KEY, JSON.stringify({ at, clamped: !!clamped })); } catch (e) { /* the next look just applies it again */ }
}

/** A real player change: the block goes on the local record, later than anything this device has seen even when its
 * clock runs behind. Call only on a player's change, never on load. @returns the record to push, or null. */
export function stampGarageBlock({ state, look }, storage = defaultStorage(), now = Date.now()) {
  const record = loadRecord(storage);
  const seen = record.garage ? Date.parse(record.garage.updatedAt) : 0;
  const block = cleanGarageBlock({ state, look, updatedAt: new Date(Math.max(now, seen + 1)).toISOString() });
  if (!block) return null;
  record.garage = block;
  if (!saveRecord(record, storage)) return null;
  saveGarageSeen(block.updatedAt, false, storage);
  return record;
}

/** The local record's block when it still has to be applied here: newer than what this device last took, or the same
 * one if part of it could not be worn then (a grant may have landed since). Else null. */
export function dueGarageBlock(storage = defaultStorage()) {
  const block = loadRecord(storage).garage;
  if (!block) return null;
  const seen = loadGarageSeen(storage);
  if (!seen.at || Date.parse(block.updatedAt) > Date.parse(seen.at)) return block;
  return seen.clamped && seen.at === block.updatedAt ? block : null;
}

function defaultStorage() { try { return globalThis.localStorage || null; } catch (e) { return null; } }
