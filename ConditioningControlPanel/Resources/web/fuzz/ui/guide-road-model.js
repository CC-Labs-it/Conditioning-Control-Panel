/* ui/guide-road-model.js - The Road: the Guide's plan of this run, as pure data (owner 2026-11-08, replaces the whole-catalog map).
 * One road cut into the seven Journey chapters. What the player bought sits where it was bought; what comes next is projected
 * the way the shop offers it, cheapest open row first. The picked route IS the road; side lanes ride along as pockets; a fork
 * is a signpost; chapters past the open one show only a count, the fork they hold and the endings. No DOM in here: tested in
 * tests/guide-road.test.mjs. */

import { BY_ID, FORKS, FORK_OF_LANE, ITEMS } from '../data/items.js';
import { LEAD_OF, ridersOf } from '../data/folds.js';
import * as E from '../sim/economy.js';
import { JOURNEY_CHAPTERS } from '../sim/journey-effects.js';

/** Lanes that ride along the road as pockets on the chapter sheet, with their short names. */
export const POCKETS = Object.freeze({ look: 'Looks', side: 'Extras', trouble: 'Questions', emi: 'EMI' });
const OFF_ROAD = new Set(['meta', 'after', 'exit']);
export const CHAPTER_ENDS = JOURNEY_CHAPTERS.map(([, end]) => end);
const FULL = CHAPTER_ENDS[CHAPTER_ENDS.length - 1];

const onRoad = (it) => it && it.group === 'run' && it.id !== 'start' && !OFF_ROAD.has(it.lane);
const laneOpen = (picks, it) => { const f = FORK_OF_LANE[it.lane]; return !f || picks[f] === it.lane; };

/** The first row of each fork lane: the face of its panel on a signpost. */
export function laneHead(lane, items = ITEMS) {
  const rows = items.filter((r) => r.lane === lane && r.group === 'run' && r.kind !== 'end');
  rows.sort((a, b) => a.cost - b.cost || items.indexOf(a) - items.indexOf(b));
  return rows[0]?.id || null;
}

/** owned, offer (open in the shop now, price aside), later. */
export function statusOf(run, id) {
  if (E.has(run, id)) return 'owned';
  const it = BY_ID[id];
  return it && E.isOpen(run, it) ? 'offer' : 'later';
}

/** Purchases made so far, the way the Journey counts them: forks are choices, not purchases. */
export const purchasesOf = (run) => Math.max(0, (run.buys || 0) - Object.keys(run.picks || {}).length);

/** Walk what is owned, in purchase order. Riders that came with a lead ride inside it. */
function pastSteps(run) {
  const steps = [];
  const leadStep = {};
  for (const id of run.order || []) {
    const it = BY_ID[id];
    if (!onRoad(it)) continue;
    const lead = LEAD_OF[id];
    if (lead && leadStep[lead]) { leadStep[lead].riders.push(id); continue; }
    if (it.kind === 'fork') { steps.push({ fork: id, pick: run.picks?.[id] || null }); continue; }
    const step = { id, riders: [], past: true };
    if (ridersOf(id).length) leadStep[id] = step;
    steps.push(step);
  }
  return steps;
}

/** What comes next, cheapest open row first, the way the shop deals them. An open fork not yet taken is a signpost; the count
 * behind it assumes its first option, and those rows are only counted (marked `guess`), never drawn as stamps. */
function futureSteps(run, items = ITEMS) {
  const owned = new Set(Object.keys(run.owned || {}).filter((id) => E.has(run, id)));
  const picks = { ...(run.picks || {}) };
  const guessed = new Set();
  const steps = [];
  const pool = items.filter((it) => onRoad(it) && it.kind !== 'end' && !owned.has(it.id));
  for (let guard = 0; guard < 600; guard++) {
    let best = null;
    for (const it of pool) {
      if (owned.has(it.id) || !laneOpen(picks, it)) continue;
      if (LEAD_OF[it.id] && !owned.has(LEAD_OF[it.id])) continue;   // a rider comes with its lead
      if (!it.req.every((q) => owned.has(q))) continue;
      if (it.any && !it.any.some((q) => owned.has(q))) continue;
      if (!best || it.cost < best.cost) best = it;
    }
    if (!best) break;
    owned.add(best.id);
    if (best.kind === 'fork') {
      const first = Object.keys(FORKS[best.id]?.opts || {})[0];
      picks[best.id] = first;
      guessed.add(first);
      steps.push({ fork: best.id, pick: null });
      continue;
    }
    const riders = ridersOf(best.id).filter((r) => BY_ID[r] && !owned.has(r));
    for (const r of riders) owned.add(r);
    const guess = guessed.has(best.lane) || (best.req.concat(best.any || [])).some((q) => guessed.has(BY_ID[q]?.lane));
    steps.push({ id: best.id, riders, past: false, guess });
  }
  return steps;
}

/** Endings this route can still reach: the main one, the exits, and the ending of every lane already picked. */
export function sealsOf(run, items = ITEMS) {
  return items.filter((it) => it.kind === 'end' && it.group === 'run' && !E.has(run, it.id)
    && (!FORK_OF_LANE[it.lane] || run.picks?.[FORK_OF_LANE[it.lane]] === it.lane)).map((it) => it.id);
}

/** Where each chapter ends, in purchases. The Journey uses its own numbers; the standard run stretches them to its own length. */
export function chapterEnds(run, total) {
  if (run.store?.journey || !(total > 0)) return CHAPTER_ENDS.slice();
  return CHAPTER_ENDS.map((end, i) => (i === CHAPTER_ENDS.length - 1 ? Math.max(total, 1) : Math.max(1, Math.round((end / FULL) * total))));
}

/** Road rows: the main path walks alone; two buys from different lanes in a row sit side by side. */
function rowsOf(steps) {
  const out = [];
  let row = null;
  for (const s of steps) {
    if (s.fork) { out.push({ fork: s.fork, pick: s.pick }); row = null; continue; }
    const lane = BY_ID[s.id].lane;
    if (lane === 'trunk' || !row || row.ids.length >= 2 || row.lanes.has(lane) || row.trunk) {
      row = { ids: [s.id], lanes: new Set([lane]), trunk: lane === 'trunk' };
      out.push(row);
    } else { row.ids.push(s.id); row.lanes.add(lane); }
  }
  return out.map((r) => (r.fork ? r : { ids: r.ids }));
}

/**
 * The whole plan: { chapters, open, next, total, made }. A chapter is
 * { n, state: past|open|future, from, to, made, count, road: [{ids}|{fork,pick}], pockets: {lane: [ids]}, forks, seals, guess }.
 */
export function buildRoad(run, items = ITEMS) {
  const past = pastSteps(run);
  const future = futureSteps(run, items);
  const made = purchasesOf(run);
  const pastCount = past.filter((s) => !s.fork).length;
  const total = made + future.filter((s) => !s.fork).length;
  const ends = chapterEnds(run, total);
  let open = ends.findIndex((end) => made < end);
  if (open < 0) open = ends.length - 1;
  /* Repeat buys count as purchases but keep one stamp: stretch the stamps already owned over the purchases made. */
  const scale = pastCount ? made / pastCount : 1;
  const chapters = ends.map((to, i) => ({
    n: i + 1, state: i < open ? 'past' : i === open ? 'open' : 'future', from: i ? ends[i - 1] : 0, to,
    steps: [], made: 0, count: 0, forks: [], seals: [], guess: 0,
  }));
  const chapterAt = (k) => { const i = ends.findIndex((end) => k < end); return chapters[i < 0 ? chapters.length - 1 : i]; };
  let k = 0;
  for (const s of past) {
    if (s.fork) { chapterAt(Math.floor(k * scale)).steps.push(s); continue; }
    const ch = chapterAt(Math.floor(k * scale));
    /* A stamp never lands after the open chapter: a run that bought ahead of the plan keeps it in the chapter it is in. */
    (ch.n - 1 > open ? chapters[open] : ch).steps.push(s);
    k += 1;
  }
  k = made;
  for (const s of future) {
    const ch = chapterAt(k);
    const target = ch.n - 1 < open ? chapters[open] : ch;
    target.steps.push(s);
    if (!s.fork) k += 1;
  }
  for (const ch of chapters) {
    const stamps = ch.steps.filter((s) => !s.fork);
    ch.made = stamps.filter((s) => s.past).length;
    ch.count = stamps.length;
    ch.forks = ch.steps.filter((s) => s.fork).map((s) => s.fork);
    ch.guess = stamps.filter((s) => s.guess).length;
    const drawn = ch.steps.filter((s) => s.fork || !s.guess);
    const pockets = {};
    const road = [];
    for (const s of drawn) {
      if (!s.fork && POCKETS[BY_ID[s.id].lane]) (pockets[BY_ID[s.id].lane] ||= []).push(s.id);
      else road.push(s);
    }
    ch.road = rowsOf(road);
    ch.pockets = pockets;
    ch.riders = Object.fromEntries(stamps.filter((s) => s.riders.length || ridersOf(s.id).length).map((s) => [s.id, ridersOf(s.id).filter((r) => BY_ID[r])]));
    delete ch.steps;
  }
  chapters[chapters.length - 1].seals = sealsOf(run, items);
  /* "next": the first road stamp of the open chapter on offer now, else the first pocket one. */
  const oc = chapters[open];
  const roadIds = oc.road.flatMap((r) => r.ids || []);
  const pocketIds = Object.values(oc.pockets).flat();
  const next = roadIds.find((id) => statusOf(run, id) === 'offer') || pocketIds.find((id) => statusOf(run, id) === 'offer') || null;
  return { chapters, open: open + 1, next, total, made };
}
