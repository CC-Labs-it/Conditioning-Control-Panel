/* ui/stage-model.js - who gets the stage. Pure: no DOM, no clocks of its own, no rng. ui/stage.js runs it each frame.
 *
 * Owner 2026-11-10 ("small buys get the stage"): the more the player owns, the more each little visual buy is lost in the
 * pile. Two rules:
 *  1. Solo: a look/ambient buy lands, and for SOLO_MS the other ambient layers ease down to SOLO_DIM while the new one plays
 *     at full strength under a small paper tag. It pays SOLO_PAYS small floating numbers on the way.
 *  2. Cast: at most CAST ambient effects at full strength (CAST_LITE on phones). The newest buy holds a seat for NEWEST_MS,
 *     pinned effects always hold one, the rest idle at IDLE and rotate in one at a time every ROTATE_MS.
 * "Everything at once" (state.all) turns the cast off: every effect at full strength. A solo still happens.
 *
 * Ambient = decorative background/overlay layers only. The button, counter, shop, toys, dock objects, picture screens,
 * the theme, Wake Up, cards and endings are never on this list. A `steady` entry (the page wash, which is the theme) is
 * never dimmed and never takes a seat, but its own buy still gets a solo. */

export const SOLO_MS = 4500;
export const SOLO_DIM = 0.15;
export const IDLE = 0.25;
export const CAST = 3;
export const CAST_LITE = 2;
export const NEWEST_MS = 3 * 60 * 1000;
export const ROTATE_MS = 40 * 1000;
export const SOLO_PAYS = 3;
/** Clock offsets of the solo's pays, inside the solo. */
export const SOLO_PAY_AT = Object.freeze([900, 2100, 3300]);
/** Milliseconds a full 0 -> 1 change takes: down quicker than up, so a solo reads at once and a return feels soft. */
export const EASE_DOWN_MS = 700;
export const EASE_UP_MS = 1100;

/** Stage families: one entry per thing the player sees, whatever rows feed it. The first id names the family. */
export const FAMILIES = Object.freeze({
  spiral: Object.freeze(['overlay', 'e_spiral', 'a_spiral', 'sp_colors', 'sp_shapes']),
  trail: Object.freeze(['trail', 'starburst', 'word_comet']),
  words: Object.freeze(['e_bounce', 'e_words']),
  ticker: Object.freeze(['ticker']),
  heat: Object.freeze(['score_heat']),
  wash: Object.freeze(['colour', 'paint']),
});
/** Families that are never dimmed (the page itself, which is the theme). */
export const STEADY = Object.freeze(new Set(['wash']));

const FAMILY_OF = new Map(Object.entries(FAMILIES).flatMap(([fam, ids]) => ids.map((id) => [id, fam])));
/** The stage id an item belongs to, or null when it is not ambient. */
export const stageIdOf = (itemId) => FAMILY_OF.get(itemId) || null;

/** How many ambient effects (families, not rows) a set of owned rows draws. Steady ones do not count. */
export function ambientOwned(owned = {}) {
  const fams = new Set();
  for (const [id, n] of Object.entries(owned)) {
    const fam = n > 0 ? stageIdOf(id) : null;
    if (fam && !STEADY.has(fam)) fams.add(fam);
  }
  return fams.size;
}

/** A run's first stage state. A save that already owns more than CAST ambient effects starts with everything at once,
 *  so nobody's page changes unasked; a new run starts with the cast. */
export function initialStage(owned = {}) {
  return { all: ambientOwned(owned) > CAST, pins: [], at: {} };
}

/** The seats: which entries play at full strength right now. `entries` are { id, order, at?, steady?, live? }:
 *  order = buy order (larger is later), at = run clock of the buy this run, live = it has something on screen. */
export function castOf(entries, now, { pins = [], lite = false } = {}) {
  const cap = lite ? CAST_LITE : CAST;
  const pool = entries.filter((e) => !e.steady && e.live !== false).sort((a, b) => a.order - b.order);
  const cast = new Set();
  for (const e of pool) if (pins.includes(e.id)) cast.add(e.id);
  let newest = null;
  for (const e of pool) if (Number.isFinite(e.at) && now - e.at >= 0 && now - e.at < NEWEST_MS && (!newest || e.at >= newest.at)) newest = e;
  if (newest) cast.add(newest.id);
  const free = cap - cast.size;
  const rest = pool.filter((e) => !cast.has(e.id));
  if (free <= 0 || !rest.length) return cast;
  if (rest.length <= free) { for (const e of rest) cast.add(e.id); return cast; }
  /* A window over the resting ones that moves one place every ROTATE_MS: one in, one out. */
  const step = Math.floor(Math.max(0, now) / ROTATE_MS);
  for (let k = 0; k < free; k++) cast.add(rest[(step + k) % rest.length].id);
  return cast;
}

/** Target strength of every entry: { strengths: {id: 0..1}, cast: Set, solo: id|null }. */
export function stageTargets(entries, now, { solo = null, all = false, pins = [], lite = false } = {}) {
  const strengths = {};
  const soloOn = !!solo && now >= solo.from && now < solo.until && entries.some((e) => e.id === solo.id);
  const cast = all ? new Set(entries.map((e) => e.id)) : castOf(entries, now, { pins, lite });
  for (const e of entries) {
    if (e.steady) strengths[e.id] = 1;
    else if (soloOn) strengths[e.id] = e.id === solo.id ? 1 : SOLO_DIM;
    else strengths[e.id] = cast.has(e.id) ? 1 : IDLE;
  }
  return { strengths, cast, solo: soloOn ? solo.id : null };
}

/** One frame of easing from `cur` toward `target`, linear, dt in ms. */
export function approach(cur, target, dt) {
  if (!Number.isFinite(cur)) return target;
  const d = target - cur;
  if (Math.abs(d) < 1e-3) return target;
  const step = Math.max(0, dt) / (d < 0 ? EASE_DOWN_MS : EASE_UP_MS);
  return Math.abs(d) <= step ? target : cur + Math.sign(d) * step;
}

/** A solo that starts now. */
export const startSolo = (id, name, now) => ({ id, name, from: now, until: now + SOLO_MS, paid: 0 });

/** How many of the solo's pays are due by `now` and not yet paid. */
export function soloPaysDue(solo, now) {
  if (!solo) return 0;
  const due = SOLO_PAY_AT.filter((t) => now - solo.from >= t && t < SOLO_MS).length;
  return Math.max(0, Math.min(SOLO_PAYS, due) - (solo.paid || 0));
}

/** The small ask of one solo pay: about a second and a half of income, at least a few taps. */
export const soloAsk = ({ fps = 0, click = 0 } = {}) => Math.max(1, fps * 1.5, click * 3);

/** The tag's words. */
export const soloTag = (name) => `New: ${name}`;
