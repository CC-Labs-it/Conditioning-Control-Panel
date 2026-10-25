/* audio/sfxmap.js - which recorded sound plays for an event, and the engine layer maths (pure).
 *
 * No WebAudio here, so node --test runs it. sfxPlan(name, payload) says which sample files a cue
 * plays (id, level, rate, delay); null means the cue has no recorded sound and the synth in
 * audio/cues.js or audio/cues-b1.js plays it. resolvePlan picks sample or synth: a plan plays
 * from samples only when every file it names decoded, so a missing file falls back to the synth
 * for the whole cue. Engine: three seamless loops (idle hum, cruise, high-speed air) crossfaded
 * by speedPct, the one variable; the pitch travels at most audio.engPitchTravelSemis, never a whine.
 */
import { mixGet, rollTickTimes, ladderSemis } from './mix.js';
import { pressThudGain } from './pressThud.js';

/** Per-file level against the one loudness every file is normalised to (linear gain). */
export const SFX_GAIN = {
  'engine-idle': 0.42, 'engine-cruise': 0.38, 'engine-air': 0.3,
  boost: 0.62, launch: 0.7, 'drift-start': 0.36, 'drift-tick-1': 0.34, 'drift-tick-2': 0.4, 'drift-tick-3': 0.48,
  'drift-release': 0.55, hop: 0.4, 'land-light': 0.45, 'land-heavy': 0.62, wall: 0.5, bump: 0.55, pad: 0.55,
  'item-box': 0.45, 'roll-tick': 0.22, 'fire-throw': 0.5, 'fire-lob': 0.48, 'fire-drop': 0.5, 'item-hit': 0.62,
  'bubble-pop': 0.42, countdown: 0.5, go: 0.55, lap: 0.5, 'final-lap': 0.55, finish: 0.55, overtake: 0.42,
  'menu-move': 0.3, 'menu-ok': 0.4, 'menu-back': 0.36,
};

/** Ids that get a small fixed rate step per play, so repeats never sound stamped. */
const JITTER_IDS = new Set(['bump', 'wall', 'hop', 'land-light', 'land-heavy', 'drift-start', 'fire-throw', 'item-hit']);
const JITTER = [0, 0.025, -0.02, 0.012, -0.03, 0.018];
/** The k-th play's rate multiplier for an id (deterministic). */
export const jitterRate = (id, k) => (JITTER_IDS.has(id) ? 1 + JITTER[Math.abs(k | 0) % JITTER.length] : 1);

const clamp01 = (x) => (x > 0 ? (x < 1 ? x : 1) : 0);
const ease = (x) => Math.sin(clamp01(x) * Math.PI / 2);         // equal-power rise 0..1

/**
 * Engine layer weights at a speed (0..maxPct): idle fades out, cruise holds the middle with a
 * small dip at the top, air rises over the upper half. A boost leans on the air layer.
 * Polish 6: no closure per call; pass out to reuse one result record.
 * @returns {{idle: number, cruise: number, air: number}}
 */
export function engineLayers(speedPct, flags = {}, feel = null, out = null) {
  const n = clamp01(Number(speedPct) || 0);
  const idle = Math.cos(clamp01(n / mixGet(feel, 'engIdleOutPct')) * Math.PI / 2);
  const up = ease((n - mixGet(feel, 'engCruiseFromPct')) / (mixGet(feel, 'engCruiseFullPct') - mixGet(feel, 'engCruiseFromPct')));
  const dip = 1 - mixGet(feel, 'engCruiseDip') * clamp01((n - mixGet(feel, 'engCruiseDipPct')) / (1 - mixGet(feel, 'engCruiseDipPct')));
  let air = ease((n - mixGet(feel, 'engAirFromPct')) / (mixGet(feel, 'engAirFullPct') - mixGet(feel, 'engAirFromPct')));
  if (flags.boosting) air = Math.min(1, air + mixGet(feel, 'engBoostAir'));
  if (flags.onRoad === false) air *= 0.6;
  // out (optional): a record the caller reuses every frame, filled in place (no allocation)
  if (out) { out.idle = Math.max(0, idle); out.cruise = up * dip; out.air = air; return out; }
  return { idle: Math.max(0, idle), cruise: up * dip, air };
}

/** Engine pitch in semitones: speed sweeps low..high, drift / air / boost add a little, the whole travel is capped. */
export function enginePitchSemis(speedPct, flags = {}, feel = null) {
  const n = clamp01(Number(speedPct) || 0);
  const lo = mixGet(feel, 'engSemisLow');
  let s = lo + (mixGet(feel, 'engSemisHigh') - lo) * n;
  if (flags.drifting && flags.driftTier > 0) s += (Math.min(flags.driftTier, 3) / 3) * mixGet(feel, 'engDriftSemis');
  if (flags.airborne) s += mixGet(feel, 'engAirborneSemis');
  if (flags.boosting) s += mixGet(feel, 'engBoostSemis');
  return Math.min(lo + mixGet(feel, 'engPitchTravelSemis'), Math.max(lo, s));
}

export const semisToRate = (s) => Math.pow(2, (Number(s) || 0) / 12);

/** The engine low-pass: darker at idle, open at speed, wider on a boost. */
export function engineCutHz(speedPct, flags = {}, feel = null) {
  const n = clamp01(Number(speedPct) || 0);
  const hz = mixGet(feel, 'engLpLowHz') + (mixGet(feel, 'engLpHighHz') - mixGet(feel, 'engLpLowHz')) * n * n;
  return Math.min(20000, hz * (flags.boosting ? mixGet(feel, 'engLpBoostMul') : 1));
}

/** The bubble pop ladder: the next rung (state { rung, t } is updated in place). */
export function popRungNext(st, t, streakS, rungs) {
  st.rung = t - st.t < streakS ? Math.min(st.rung + 1, rungs - 1) : 0;
  st.t = t;
  return st.rung;
}

/** The word-pop ladder rung: the pop streak (sim/words.js) climbs the pentatonic ladder and wraps to its foot. */
export const wordPopRung = (streak, rungs) => Math.max(0, (streak | 0) - 1) % Math.max(1, rungs | 0);

/**
 * A word bubble taken (sim/words.js pop event): the bubble pop on the streak's rung, a fifth on top for a
 * row (a trigger or gate word), and an octave sparkle when the pop finishes a whole line.
 */
export function wordPopPlan(p, feel) {
  const r = semisToRate(Math.min(12, ladderSemis(wordPopRung(p.streak, mixGet(feel, 'popRungs')))));
  const plan = [one('bubble-pop', p.row ? 1 : 0.85, r)];
  if (p.row) plan.push(one('bubble-pop', 0.6, r * semisToRate(7), 0.04));
  if (p.lineDone) plan.push(one('bubble-pop', 0.7, r * 2, 0.08));
  return plan;
}

/** Self items used by the player (polish 6): Surge boosts, Glass rings, Echo doubles, Golden sparkles. */
const SELF_FIRE = {
  surge: () => [one('boost', 1, 0.92)],
  glass: () => [one('bubble-pop', 0.8, 0.84), one('pad', 0.45, 1.1, 0.03)],
  echo: () => [one('fire-throw', 0.5, 1.2), one('fire-throw', 0.3, 1.2, 0.09)],
  golden: () => [one('item-box', 0.7, 1.2), one('drift-release', 0.5, 1.3, 0.04)],
};

const fg = (feel, path, def) => { try { const v = feel?.get(path); return v === undefined ? def : v; } catch (e) { return def; } };
const one = (id, vol = 1, rate = 1, at = 0) => ({ id, vol, rate, at });

/**
 * The sample plan for a cue.
 * @param {string} name bus event or menu cue name
 * @param {object} p payload
 * @param {{feel?: object, laps?: number, now?: number, st?: {rung: number, t: number}}} o
 * @returns {null | Array<{id: string, vol: number, rate: number, at: number}>} null = synth only; [] = silent on purpose
 */
export function sfxPlan(name, p = {}, o = {}) {
  const feel = o.feel || null;
  p = p || {};
  switch (name) {
    case 'air-word-hop': return null; // Its ascending spring stays on the shared synth even with the bank loaded.
    case 'press-thud': return pressThudGain(p) > .01 ? [one('land-heavy', .06 * pressThudGain(p), .68)] : [];
    case 'countdown': return [one('countdown')];
    case 'go': return [one('go')];
    case 'launch-boost': return [one('launch')];
    case 'drift-start': return [one('drift-start')];
    case 'drift-tier': return [one('drift-tick-' + Math.max(1, Math.min(3, p.tier | 0 || 1)))];
    case 'drift-release': return p.tier > 0 ? [one('drift-release', 0.7 + 0.1 * Math.min(3, p.tier))] : [];
    case 'boost-start': {
      const k = p.kind;
      if (k === 'launch' || k === 'pad') return [];          // launch-boost and pad play their own
      if (k === 'slip') return [one('boost', 0.7, 1.06)];
      if (k === 'bubble') return [one('boost', 0.35, 1.12)];
      if (k === 'surge') return [one('boost', 0.85, 0.94)];
      return [one('boost')];
    }
    case 'hop': return [one('hop')];
    case 'land': {
      if (p.airWord) return null; // The soft contact tap stays separate from the rebound spring.
      const t = p.tier | 0;
      if (t <= 0) return [];
      return t >= 3 ? [one('land-heavy')] : [one('land-light', t === 1 ? 0.55 : 1)];
    }
    case 'wall': {
      const v = Math.min(1, (Number(p.dv) || 0) / fg(feel, 'wall.cueFullDvMps', 12));
      return v > 0.02 ? [one('wall', 0.35 + 0.65 * v, 0.96 + 0.08 * v)] : [];
    }
    case 'bump': {
      const dv = Number(p.dv) || 0;
      if (!(dv >= fg(feel, 'contact.cueMinDvMps', 1.5))) return [];
      return [one('bump', 0.4 + 0.6 * Math.min(1, dv / mixGet(feel, 'bumpFullDvMps')))];
    }
    case 'pad': return [one('pad')];
    case 'lap': {
      const laps = o.laps > 0 ? o.laps : 3, lap = p.lap | 0;
      if (lap >= laps) return [];                              // finish plays on the last line
      return [one(lap === laps - 1 ? 'final-lap' : 'lap')];
    }
    case 'finish': return [one('finish')];
    case 'item-box': {
      if (p.racer !== 0) return [one('item-box', 0.7)];
      const ticks = rollTickTimes(mixGet(feel, 'rollTicks'), fg(feel, 'items.rollS', 1));
      return [one('item-box'), ...ticks.map((at, k) => one('roll-tick', 1 - k * 0.04, k % 2 ? 1.06 : 1, at + 0.05))];
    }
    case 'item-roll': {
      if (p.racer !== 0) return [];
      const rare = p.id === 'shatter' || p.id === 'golden' || p.id === 'blackout';
      const self = p.id === 'surge' || p.id === 'glass' || p.id === 'echo' || p.id === 'golden';
      return [one('menu-ok', rare ? 1.1 : 0.85, rare ? 1.12 : self ? 1.06 : 1)];
    }
    case 'item-fire': {
      const { racer, id, mode, target } = p;
      if (racer !== 0 && target === 0 && (mode === 'throw' || mode === 'lob')) return null;   // incoming warning: synth
      // polish 6: every self item answers from the shipped bank too (a release at the kart), each its own figure
      if (mode === 'self') return SELF_FIRE[id] ? SELF_FIRE[id]() : [one('drift-release', 0.55, 1.15)];
      if (mode === 'throw' || mode === 'bounce' || mode === 'tether') return [one('fire-throw')];
      if (mode === 'lob') return [one('fire-lob')];
      if (mode === 'drop') return [one('fire-drop')];
      return null;
    }
    case 'item-hit': {
      if (p.verdict && p.verdict !== 'hit') return null;       // glass, golden, decoy, mercy: synth figures
      if (p.victim === 0) return [one('item-hit')];
      if (p.by === 0) return [one('item-hit', 0.6, 1.08)];
      return [one('bubble-pop', 0.6)];
    }
    case 'bubble-pop': {
      if (p.racer !== 0 || !o.st) return [one('bubble-pop', 0.6)];
      const rung = popRungNext(o.st, o.now || 0, mixGet(feel, 'popStreakS'), mixGet(feel, 'popRungs'));
      return [one('bubble-pop', 1, semisToRate(Math.min(12, ladderSemis(rung))))];
    }
    case 'word-pop': return wordPopPlan(p, feel);
    case 'overtake':
      if (p.racer === 0) return [one('overtake')];
      return p.passed === 0 ? null : [];
    case 'racer-event':
      return BOT_SAMPLE_ECHO.includes(p.name) ? sfxPlan(p.name, p.p || {}, { ...o, st: null }) : [];
    case 'menu-move': return [one('menu-move')];
    case 'menu-toggle': return [one('menu-move', 0.9, 1.12)];
    case 'menu-ok': return [one('menu-ok')];
    case 'menu-back': return [one('menu-back')];
    // polish 5 (S4): the online lobby on the same shipped menu sounds; the grid count on a low, quiet bell
    case 'seat-in': return [one('menu-ok', 0.7, 1.12)];
    case 'seat-out': return [one('menu-back', 0.7, 0.9)];
    case 'ready-on': return [one('menu-ok', 0.85, 1.26)];
    case 'ready-off': return [one('menu-back', 0.6, 1.06)];
    case 'lobby-tick': return [one('countdown', 0.45, 0.84)];
    default: return null;
  }
}

/** Bot events echoed from samples (the same one-shots audio/cues-b1.js BOT_ECHO lists). */
export const BOT_SAMPLE_ECHO = ['hop', 'land', 'wall', 'drift-start', 'boost-start', 'pad'];

/** 'sample' when a plan exists and every file it names is loaded, else 'synth'. */
export function resolvePlan(plan, has) {
  if (!Array.isArray(plan)) return 'synth';
  return plan.every((x) => has(x.id)) ? 'sample' : 'synth';
}
