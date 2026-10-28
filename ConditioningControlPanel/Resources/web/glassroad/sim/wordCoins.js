/* sim/wordCoins.js - road words as coins (playtest fix 2026-10-28, lane words; handoff-1028/GLASSROAD-PLAYTEST-FIXES.md).
 *
 * Tester: "road lyric words do nothing; no reason to catch anything". One painted marking per phrase is a COIN
 * (pickCoins: the shortest real word of the phrase, a small target): it holds a fixed spot on the riskier line
 * (coinLane: the inside of the bend it sits in, or a side picked by phrase on a straight), never on a moving trap
 * row (underTrap), and lights while its word is sung (the karaoke light, render/roadWordsMath.js). Driving over it
 * while it is lit (coinLit + coinTouch) is +1 on the count (createWordCoins), capped; each point raises the
 * player's top speed a little (topMul); a landed hit (item or trap, sim/hitterHit.js) drops a few.
 * Numbers live in feel.json group `wordCoins` (FEEL.md). Solo only unless `wordCoins.online` (boot.js gates it):
 * online stays on today's rules until the room rollout. Pure: no three.js, no DOM.
 */
import { clamp, wrap } from '../core/math.js';

const DEFAULTS = Object.freeze({
  on: true, online: false, cap: 10, topPctPer: 0.009, hitDrop: 3, litPreS: 0.6, litPostS: 0.9,
  laneShare: 0.55, edgeMarginM: 1.4, bendM: 30, straightM: 0.4, trapClearM: 8, touchSlackM: 0.6, kartHalfM: 0.6,
  minChars: 3, popBubbles: true,
});
const KEYS = Object.keys(DEFAULTS);

/** The group as a plain object (a missing key keeps its default, so an older feel.json still boots). */
export function coinTuning(feel = null) {
  const out = {};
  for (const k of KEYS) {
    let v;
    try { v = feel && feel.get ? feel.get('wordCoins.' + k) : undefined; } catch { v = undefined; }
    out[k] = v === undefined || v === null ? DEFAULTS[k] : v;
  }
  return out;
}

/**
 * One coin per phrase of the painted script (sim/roadWords.js scriptRoad): the marking with the fewest letters
 * among those with at least minChars (ties: the first); a phrase with none takes its longest marking.
 * @returns {Uint8Array} 1 = coin, by script index
 */
export function pickCoins(script, C = DEFAULTS) {
  const out = new Uint8Array(script ? script.length : 0);
  let i = 0;
  while (script && i < script.length) {
    const ph = script[i].phrase;
    let best = -1, longest = i, j = i;
    for (; j < script.length && script[j].phrase === ph; j++) {
      const c = script[j].chars;
      if (c >= C.minChars && (best < 0 || c < script[best].chars)) best = j;
      if (c > script[longest].chars) longest = j;
    }
    out[best >= 0 ? best : longest] = 1;
    i = j;
  }
  return out;
}

/** Deterministic +-1 from a phrase index (the side of a coin on a straight). */
const sideOf = (phrase) => ((Math.imul((phrase | 0) + 1, 0x9e3779b1) >>> 16) & 1 ? 1 : -1);

const FA = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0], w: 14 };
const FB = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0], w: 14 };
const FM = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0], w: 14 };

/**
 * The coin's lateral (metres along the road's b axis) at s: the inside of the bend (where the chord of the road
 * bendM long, centred on s, passes), laneShare of the usable half width; on a straight a side by phrase.
 */
export function coinLane(track, s, phrase, C = DEFAULTS) {
  const L = track.lengthM;
  const m = track.frameAt(wrap(s, L), FM), a = track.frameAt(wrap(s - C.bendM / 2, L), FA), b = track.frameAt(wrap(s + C.bendM / 2, L), FB);
  let d = 0;
  for (let k = 0; k < 3; k++) d += ((a.p[k] + b.p[k]) / 2 - m.p[k]) * m.b[k];
  const side = Math.abs(d) >= C.straightM ? Math.sign(d) : sideOf(phrase);
  const usable = Math.max(0, m.w / 2 - C.edgeMarginM);
  return side * C.laneShare * usable;
}

/** True when a marking lenM long centred on s overlaps a moving trap row (with trapClearM to spare). */
export function underTrap(track, s, lenM, C = DEFAULTS) {
  const rows = (track.moving && track.moving.traps) || [], L = track.lengthM;
  for (const r of rows) {
    const x = wrap(s - r.s, L), d = x > L / 2 ? x - L : x;
    if (Math.abs(d) <= (lenM + (r.len || 0)) / 2 + C.trapClearM) return true;
  }
  return false;
}

/** Lit: from litPreS before its word starts to litPostS after it ends (song seconds). */
export const coinLit = (tS, startS, endS, C = DEFAULTS) => tS >= startS - C.litPreS && tS <= Math.max(startS, endS) + C.litPostS;

/** The kart (lateral lat) is on the painted marking: dS = marking centre minus kart s, lane + width as painted. */
export const coinTouch = (dS, lenM, lane, widthM, lat, C = DEFAULTS) =>
  Math.abs(dS) <= lenM / 2 + C.touchSlackM && Math.abs((lat || 0) - lane) <= widthM / 2 + C.kartHalfM;

/**
 * The player's count. add() -> true when it rose (false at the cap); hit() -> points lost; topMul() the top speed
 * multiplier the count buys (1 + count x topPctPer).
 */
export function createWordCoins(C = DEFAULTS) {
  let count = 0;
  return {
    get count() { return count; },
    get cap() { return C.cap; },
    add() { if (count >= C.cap) return false; count++; return true; },
    hit() { const lost = Math.min(count, C.hitDrop); count -= lost; return lost; },
    topMul() { return 1 + clamp(count, 0, C.cap) * C.topPctPer; },
    reset() { count = 0; },
  };
}
