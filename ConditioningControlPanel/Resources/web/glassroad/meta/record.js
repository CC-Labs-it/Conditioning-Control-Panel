/* meta/record.js - Record v1, the player's logbook (CONTRACT-LOGBOOK.md section 4). Frozen shape.
 *
 * One plain object: per course bests and counts, lifetime totals, the last 20 races, achievements
 * (id -> first unlock time) and unlock ids. mergeRecords is the ONE merge rule, shared with the server:
 * counters take the max, best times the smallest positive, medals the higher rank, achievements the
 * earliest time, unlocks the union, recent the union by `at` (newest 20). Merging never lowers anything.
 * cleanRecord clamps every number and drops unknown keys, so an old or hand-edited record never breaks
 * the page. Storage access is always in try/catch (private windows, blocked site data).
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
  r.updatedAt = earlier(x.updatedAt, y.updatedAt) === x.updatedAt ? y.updatedAt || x.updatedAt : x.updatedAt;   // the later one
  return r;
}

export function loadRecord(storage = defaultStorage()) {
  try { return cleanRecord(JSON.parse(storage && storage.getItem(RECORD_KEY) || 'null')); } catch (e) { return emptyRecord(); }
}

/** @returns {boolean} true when written */
export function saveRecord(record, storage = defaultStorage()) {
  try { if (!storage) return false; storage.setItem(RECORD_KEY, JSON.stringify(cleanRecord(record))); return true; } catch (e) { return false; }
}

function defaultStorage() { try { return globalThis.localStorage || null; } catch (e) { return null; } }
