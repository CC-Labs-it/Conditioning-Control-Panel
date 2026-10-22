/* audio/mix.js - the B1 mix numbers and the pure parts of the audio lane (no WebAudio here).
 *
 * Every number reads feel.get('audio.<key>') first and falls back to MIX, so a later
 * feel.json `audio` group takes over with no code change. Pure helpers: which rival karts get
 * an engine voice (stable slots, nearest first), how loud and how detuned each one is, and how
 * loud an event is for the local player (theirs = full, a bot's = by distance, far = silent).
 */

// feel gap: audio.* has no feel.json group yet (reported). Gains are linear, times in seconds.
export const MIX = {
  masterGain: 0.8,            // the one master gain (Phase A MASTER_GAIN)
  musicGain: 0.55,            // music bus at volume 1, under the engine and cues
  musicFadeInS: 0.6,
  musicFadeOutS: 0.35,
  volTcS: 0.05,               // setVolumes glide
  rivalGain: 0.05,            // one rival engine at 0 m, before the sfx volume
  rivalNearM: 4,              // full level inside this
  rivalFarM: 80,              // silent past this (racers.nearby maxM)
  rivalDetuneCents: 70,       // per-racer spread, +-
  rivalIdleHz: 62,
  rivalRangeOct: 1.4,
  rivalCutLowHz: 300,
  rivalCutHighHz: 1400,
  rivalBoostCutMul: 1.8,
  rivalGlideS: 0.08,
  nearCueM: 45,               // a bot's cue is heard within this
  botCueMul: 0.5,             // a bot's own cue at 0 m, against the player's
  effectDuck: 0.55,           // engine, rivals and music while a screen effect lands
  effectDuckInS: 0.06,
  effectDuckHoldMaxS: 1.2,    // the duck lets go after this even if the effect runs on
  effectDuckOutS: 0.5,
  effectBedGain: 0.028,       // a bed under a screen effect: under the quietest engine level (0.07)
  rollTicks: 9,               // item-box roll ticks over items.rollS, slowing down
  bumpFullDvMps: 10,          // a bump at this closing speed is full volume
  popStreakS: 1.5,            // bubble pops inside this climb the pentatonic ladder
  popRungs: 8,
  slipAirGain: 0.035,         // slipstream air while charging
};

/** @returns {number} feel 'audio.<key>' when it exists, else MIX[key]. */
export function mixGet(feel, key) {
  try { const v = feel?.get('audio.' + key); if (v !== undefined) return v; } catch (e) { /* feel gap */ }
  return MIX[key];
}

const clamp01 = (x) => (x < 0 ? 0 : x > 1 ? 1 : x);

/** Distance to a 0..1 level: 1 inside nearM, 0 past farM, a squared ease between. */
export function distanceLevel(distM, nearM, farM) {
  if (!(distM >= 0)) return 0;
  if (distM <= nearM) return 1;
  if (distM >= farM) return 0;
  const k = 1 - (distM - nearM) / (farM - nearM);
  return k * k;
}

/** A fixed detune per racer index, in cents within +-spread, so each bot sounds like itself. */
export function racerDetuneCents(i, spread) {
  const steps = [0, 0.8, -0.6, 0.35, -0.95, 0.6, -0.3, 1, -0.8];
  return steps[Math.abs(i | 0) % steps.length] * spread;
}

/**
 * Voice allocation: the nearest n rivals get a voice. A racer that keeps its place in the
 * nearest n keeps its slot (no pitch jump on a re-sort); freed slots go to the new ones.
 * @param {number[]} slots length n, racer index or -1; updated in place and returned
 * @param {Array<{i: number, distM: number}>} rivals
 */
export function allocVoices(slots, rivals, n = slots.length) {
  const want = (rivals || []).filter((r) => r && r.i > 0 && r.distM >= 0)
    .slice().sort((a, b) => a.distM - b.distM).slice(0, n).map((r) => r.i);
  const keep = new Set(want);
  for (let k = 0; k < slots.length; k++) if (!keep.has(slots[k])) slots[k] = -1;
  for (const i of want) {
    if (slots.includes(i)) continue;
    const free = slots.indexOf(-1);
    if (free >= 0) slots[free] = i;
  }
  return slots;
}

/** One rival voice's targets: { gain, hz, detune, cutHz, pan }. */
export function rivalVoice(r, feel) {
  const g = (k) => mixGet(feel, k);
  const lvl = distanceLevel(r.distM, g('rivalNearM'), g('rivalFarM'));
  const p = clamp01(Math.max(0, r.speedPct || 0));
  const cut = (g('rivalCutLowHz') + (g('rivalCutHighHz') - g('rivalCutLowHz')) * p) * (r.boosting ? g('rivalBoostCutMul') : 1);
  return {
    gain: g('rivalGain') * lvl,
    hz: g('rivalIdleHz') * Math.pow(2, p * g('rivalRangeOct')),
    detune: racerDetuneCents(r.i, g('rivalDetuneCents')),
    cutHz: cut,
    pan: Math.max(-1, Math.min(1, r.pan || 0)),
  };
}

/** The racer indices an event payload names (racer, victim, by, a, b, passed, target). */
export function racersOf(p) {
  const out = [];
  if (!p || typeof p !== 'object') return out;
  for (const k of ['racer', 'victim', 'by', 'a', 'b', 'passed', 'target']) if (Number.isInteger(p[k])) out.push(p[k]);
  return out;
}

/**
 * How an event sounds for the local player. me = racer 0 is named (or nobody is named: a
 * local event such as effect-start). Else the nearest named bot sets level and pan.
 * @param {object} p payload
 * @param {Map<number, {distM: number, pan: number}>} near the last racers.nearby() rows by index
 * @returns {{me: boolean, level: number, pan: number}} level 0 = not heard
 */
export function eventLevel(p, near, nearM, botMul) {
  const who = racersOf(p);
  if (!who.length || who.includes(0)) return { me: true, level: 1, pan: 0 };
  let best = null;
  for (const i of who) {
    const r = near?.get(i);
    if (r && (!best || r.distM < best.distM)) best = r;
  }
  if (!best) return { me: false, level: 0, pan: 0 };
  const lvl = distanceLevel(best.distM, 3, nearM) * botMul;
  return { me: false, level: lvl, pan: Math.max(-1, Math.min(1, best.pan || 0)) };
}

/** Roll tick offsets (s from now): n ticks over rollS, each gap longer than the last (a wheel slowing). */
export function rollTickTimes(n, rollS) {
  if (!(n > 0) || !(rollS > 0)) return [];
  const w = [];
  for (let k = 0; k < n; k++) w.push(1 + k * 0.35);
  const sum = w.reduce((a, b) => a + b, 0), out = [];
  let t = 0;
  for (let k = 0; k < n; k++) { out.push(t); t += (w[k] / sum) * rollS * 0.92; }
  return out;
}

/** The C-pentatonic-style ladder on the root, in semitones: rung 5 is the octave. */
export function ladderSemis(rung) {
  const PENT = [0, 2, 4, 7, 9];
  const r = Math.max(0, rung | 0);
  return 12 * Math.floor(r / PENT.length) + PENT[r % PENT.length];
}
