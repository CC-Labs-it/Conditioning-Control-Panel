/* ============================================================================
 * sim/events.js - the timed side systems as pure functions: the golden bubble,
 * Triggers, mystery boxes and Levels. ui/widgets draw them; this file decides
 * when they happen and what they pay. Tested by tests/events.test.mjs.
 * ==========================================================================*/

import { baseFps, lump, has } from './economy.js';

/* Golden bubble (Cookie Clicker's golden cookie, scaled to a 30-minute run). */
export const GOLDEN = Object.freeze({
  unlockItem: 'spiral',   // first one can appear once this is owned
  minGapMs: 60_000,
  maxGapMs: 120_000,
  showMs: 10_000,
  weights: { rush: 45, change: 45, behaviour: 10 },
  rush: { mul: 7, ms: 30_000 },                   // Pink Rush: x7 FPS
  change: { bankShare: 0.15, minutesOfFps: 15 },  // Keep the Change: min(15% bank, 15 min of FPS)
  behaviour: { mul: 77, ms: 13_000 },             // Good Behaviour: x77 per click
});

export function nextGoldenAt(now, rng = Math.random) {
  return now + GOLDEN.minGapMs + rng() * (GOLDEN.maxGapMs - GOLDEN.minGapMs);
}

export function weighted(weights, rng = Math.random) {
  const entries = Object.entries(weights);
  const total = entries.reduce((n, [, w]) => n + w, 0);
  let r = rng() * total;
  for (const [k, w] of entries) { r -= w; if (r < 0) return k; }
  return entries[entries.length - 1][0];
}

export const rollGolden = (rng) => weighted(GOLDEN.weights, rng);

/** Apply a caught golden bubble. Returns what the toast should say. */
export function applyGolden(s, kind, now) {
  if (kind === 'rush') {
    s.effects.frenzyUntil = now + GOLDEN.rush.ms;
    s.effects.frenzyMul = GOLDEN.rush.mul;
    return { kind, label: 'Pink Rush', ms: GOLDEN.rush.ms };
  }
  if (kind === 'behaviour') {
    s.effects.clickFrenzyUntil = now + GOLDEN.behaviour.ms;
    s.effects.clickFrenzyMul = GOLDEN.behaviour.mul;
    return { kind, label: 'Good Behaviour', ms: GOLDEN.behaviour.ms };
  }
  const amount = Math.max(1, Math.min(s.fuzz * GOLDEN.change.bankShare, baseFps(s) * s.pace * 60 * GOLDEN.change.minutesOfFps) + 13);
  lump(s, Math.round(amount), 'golden');
  return { kind: 'change', label: 'Keep the Change', amount: Math.round(amount) };
}

/* Triggers (the powerup drops). BLANK is the ending's foreshadow. */
export const TRIGGERS = Object.freeze({
  everyMs: 50_000,
  kinds: ['focus', 'blank', 'deeper', 'sparkle'],
  focus: { ms: 15_000, mul: 8 },     // every click pays like a crit
  blank: { ms: 6_000 },              // mute everything, pink calm
  deeper: { ms: 15_000 },            // drifting bubbles at double speed
});

export const rollTrigger = (rng = Math.random) => TRIGGERS.kinds[Math.floor(rng() * TRIGGERS.kinds.length)];

export function applyTrigger(s, kind, now) {
  if (kind === 'focus') { s.effects.focusUntil = now + TRIGGERS.focus.ms; s.effects.clickFrenzyMul = Math.max(s.effects.clickFrenzyMul || 1, TRIGGERS.focus.mul); }
  else if (kind === 'blank') s.effects.blankUntil = now + TRIGGERS.blank.ms;
  else if (kind === 'deeper') s.effects.deeperUntil = now + TRIGGERS.deeper.ms;
  else if (kind === 'sparkle') s.sparkles = (s.sparkles || 0) + 1;
  return { kind };
}

/* Mystery boxes: land at random, expire, pay 7-32% of a minute of FPS. */
export const BOXES = Object.freeze({ minGapMs: 20_000, maxGapMs: 45_000, liveMs: 30_000, cap: 10, minShare: 0.07, maxShare: 0.32, sparkleChance: 0.25 });

export function nextBoxAt(now, rng = Math.random) { return now + BOXES.minGapMs + rng() * (BOXES.maxGapMs - BOXES.minGapMs); }

export function openBox(s, rng = Math.random) {
  const share = BOXES.minShare + rng() * (BOXES.maxShare - BOXES.minShare);
  const amount = Math.max(1, Math.round(baseFps(s) * s.pace * 60 * share));
  lump(s, amount, 'boxes');
  const sparkle = has(s, 'sparkles') && rng() < BOXES.sparkleChance;
  if (sparkle) s.sparkles = (s.sparkles || 0) + 1;
  return { amount, sparkle };
}

/* Levels: XP = lifetime Fuzz; each level needs 2000 * 1.25^n; Collect pays a minute of FPS. */
export const LEVELS = Object.freeze({ base: 2000, grow: 1.25 });

export function levelNeed(level) { return Math.round(LEVELS.base * Math.pow(LEVELS.grow, level)); }

export function levelProgress(s) {
  let level = 0, left = s.total;
  while (left >= levelNeed(level)) { left -= levelNeed(level); level += 1; }
  return { level, into: left, need: levelNeed(level) };
}

export function collectLevel(s, collected) {
  const { level } = levelProgress(s);
  if (level <= collected) return { ok: false, collected };
  const amount = Math.max(10, Math.round(baseFps(s) * s.pace * 60));
  lump(s, amount, 'levels');
  return { ok: true, collected: collected + 1, amount };
}
