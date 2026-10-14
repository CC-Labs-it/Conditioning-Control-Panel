/* ============================================================================
 * toyProcs.js - the dock toys wake on their own (owner, 2026-10-12).
 *
 * Nothing in the dock is fired by hand any more: no key, no button. Each toy
 * listens for ONE run event and has a chance, by level, to wake when it
 * happens. Two fairness rules keep it from feeling like dice:
 *   rest  - after a wake the toy sleeps for `rest` seconds (chaosRun.js keeps
 *           the clock in the toy's cooldownLeft, so the dock shows it);
 *   pity  - every miss raises the next chance by PITY_STEP of the base, up to
 *           PITY_CAP times the base, and a wake clears it.
 * The crafted one-charge items wake once, for certain, on their own moment.
 *
 * E-Stim is not here: it has no window to open, it simply arcs on a share of
 * clicked pops (chaosRun.js onBenignPopped).
 *
 * Pure, node tested. Every number the owner may want to tune is in TOY_PROCS.
 * ==========================================================================*/

/** Run events a toy can listen for (fired by chaosRun.js wakeToys). */
export const PROC_EVENTS = ['pop', 'defuse', 'milestone', 'rabbit', 'expire', 'detonate', 'detonate-pre', 'chamber'];

// on: the event. chance: by level (the last one repeats for higher levels).
// rest: seconds asleep after a wake. says: the dock tooltip line.
export const TOY_PROCS = {
  vibe_popping:   { on: 'pop',       chance: [0.02, 0.025, 0.03, 0.04], rest: 20, says: 'of the treats you pop' },
  freeze_trigger: { on: 'defuse',    chance: [0.10, 0.14, 0.18, 0.22],  rest: 10, says: 'of the live ones you defuse' },
  the_wand:       { on: 'defuse',    chance: [0.12, 0.16, 0.20],        rest: 8,  says: 'of the live ones you defuse' },
  porn_dvd:       { on: 'milestone', chance: [0.25, 0.35, 0.50, 0.50],  rest: 30, says: 'of every tenth pop in a streak' },
  rabbit_caller:  { on: 'rabbit',    chance: [0.30, 0.40, 0.50],        rest: 12, says: 'of the rabbits you catch' },
  the_pump:       { on: 'expire',    chance: [0.15, 0.20, 0.25],        rest: 15, says: 'of the treats that slip away' },
  snap_field:     { on: 'detonate',  chance: [0.20, 0.30, 0.40],        rest: 8,  says: 'of detonations' },
  // crafted, one charge: certain, once
  rubber_up:      { on: 'detonate-pre', chance: [1], rest: 0, once: 'your first detonation' },
  slippery_slope: { on: 'milestone',    chance: [1], rest: 0, minStreak: 30, once: 'a streak of 30' },
  sugar_cube:     { on: 'chamber',      chance: [1], rest: 0, once: 'the next chamber' },
};

export const PITY_STEP = 0.5;   // each miss adds half the base chance
export const PITY_CAP = 3;      // never more than three times the base

/** The base chance for a toy at a level (1-based), 0 for a toy that is not here. */
export function baseChance(id, level) {
  const p = TOY_PROCS[id];
  if (!p) return 0;
  const i = Math.max(1, level | 0) - 1;
  return p.chance[Math.min(i, p.chance.length - 1)];
}

/** The chance right now, after `misses` misses in a row. */
export function procChance(id, level, misses) {
  const base = baseChance(id, level);
  if (base >= 1) return 1;
  return Math.min(1, base * Math.min(PITY_CAP, 1 + PITY_STEP * Math.max(0, misses | 0)));
}

/** Does this toy listen for this event at all (and is the moment big enough)? */
export function listensFor(id, event, info) {
  const p = TOY_PROCS[id];
  if (!p || p.on !== event) return false;
  if (p.minStreak && !((info && info.streak) >= p.minStreak)) return false;
  return true;
}

/**
 * Roll one toy for one event. `toy` carries { id, level, misses } and its miss
 * count is updated here. Returns true when it wakes.
 */
export function rollProc(toy, rnd = Math.random) {
  const c = procChance(toy.id, toy.level, toy.misses);
  if (c > 0 && rnd() < c) { toy.misses = 0; return true; }
  toy.misses = (toy.misses | 0) + 1;
  return false;
}

/**
 * Grabbing a toy you already hold (owner, 2026-10-13): it goes one level deeper for this
 * fall, up to the top of its own ladder, and wakes rested either way. `toy` carries
 * { level, power, maxed, cooldownLeft } and is updated here; returns true when it deepened.
 */
export function regrab(toy, levelValues) {
  const top = (levelValues && levelValues.length) || 1;
  const deeper = (toy.level | 0) < top;
  if (deeper) {
    toy.level = (toy.level | 0) + 1;
    toy.power = levelValues[toy.level - 1];
    toy.maxed = toy.level >= top;
  }
  toy.cooldownLeft = 0;
  return deeper;
}

/** Can a held toy still go deeper (so a second card of it is worth offering)? */
export function canDeepen(toy, levelValues) {
  return !!toy && (toy.level | 0) < ((levelValues && levelValues.length) || 1);
}

// Overload (duo): the E-Stim arcs this many times as often, never past OVERLOAD_CAP.
export const OVERLOAD_CAP = 0.6;
/** The share of clicked pops that arc, for an E-Stim at `level` (maxed or not) and a duo multiplier. */
export function estimChance(level, maxed, mult = 1) {
  const base = maxed ? 0.30 : [0.10, 0.15, 0.20][Math.min(Math.max(1, level | 0), 3) - 1];
  return Math.min(OVERLOAD_CAP, base * Math.max(1, mult));
}

/** Seconds a toy sleeps after a wake. */
export function restFor(id) { const p = TOY_PROCS[id]; return p ? p.rest : 0; }

/** The dock tooltip line: "wakes on 14% of the live ones you defuse". */
export function procLine(id, level) {
  const p = TOY_PROCS[id];
  if (!p) return '';
  if (p.once) return `wakes once, on ${p.once}`;
  const pct = Math.round(baseChance(id, level) * 1000) / 10;
  return `wakes on ${pct}% ${p.says}`;
}
