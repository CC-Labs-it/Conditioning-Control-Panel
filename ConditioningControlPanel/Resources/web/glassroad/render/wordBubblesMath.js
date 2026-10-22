/* render/wordBubblesMath.js - how a word bubble looks at a moment (render/wordBubbles.js), pure.
 *
 * Every time comes from the bubble view (sim/words.js), which reads the song clock: inT since it showed,
 * outT since it popped or was missed, remainS to its word. IN: grows and fades in with a small overshoot.
 * While live: a slow bob and a swell as its word arrives. THE KARAOKE LIGHT (pass 3, the owner could not tell
 * which bubble the voice was on): at the word's exact start the bubble lights warm (`sung`, read by the shader)
 * and kicks a little bigger, holds the light while the voice sings it (lenS) and lets it go after, so every
 * bubble down the line answers its word on the voice, near or far, popped or not yet. OUT on a pop: the glass
 * swells and clears, a ring runs out, an optional flash core (only when the photosafe budget allowed it).
 * OUT on a miss: it shrinks, rises a little and fades. Gentle: every fade and rise slower (the light rises
 * over the gentle rise floor, never a step), smaller moves, no flash.
 */
export const BUBBLE_LOOK = Object.freeze({
  sizeM: 2.2, gateSizeM: 1.9,
  inS: 0.35, gentleInS: 0.7, overshoot: 0.08,
  bobM: 0.12, bobHz: 0.6,
  nowSwell: 0.08, nowS: 0.3,
  sungRiseS: 0.06, sungFadeS: 0.3, gentleSungRiseS: 0.3, gentleSungFadeS: 0.6, gentleSung: 0.6,
  sungKick: 0.14, sungKickS: 0.16,
  popS: 0.32, gentlePopS: 0.6, popGrow: 0.5,
  ringS: 0.45, gentleRingS: 0.8, ringGrow: 1.6, ringAlpha: 0.9,
  flashS: 0.16, flashAlpha: 0.75, flashSize: 1.3,
  missS: 0.5, gentleMissS: 0.9, missRiseM: 0.4,
});

const sat = (x) => (x < 0 ? 0 : x > 1 ? 1 : x);
const ease = (x) => { x = sat(x); return x * x * (3 - 2 * x); };

/** The karaoke light of a live bubble (0..1): rises on the word's start, holds for lenS, falls after. */
export function sungLight(v, gentle = false, L = BUBBLE_LOOK) {
  const since = -v.remainS, len = v.lenS > 0 ? v.lenS : 0.25;
  if (!(since >= 0)) return 0;
  const rise = gentle ? L.gentleSungRiseS : L.sungRiseS, fade = gentle ? L.gentleSungFadeS : L.sungFadeS;
  const up = ease(since / rise), down = since <= len ? 1 : 1 - ease((since - len) / fade);
  return up * down * (gentle ? L.gentleSung : 1);
}

/**
 * The bubble's draw: { alpha, scale (x sizeM), lift (metres up), ring (alpha, 0 = none), ringScale, flash (alpha),
 * sung (0..1, the karaoke light) }. flashOk: the photosafe budget granted this pop its flash.
 */
export function bubbleLook(v, { gentle = false, flashOk = false, seed = 0, L = BUBBLE_LOOK } = {}, out = {}) {
  out.ring = 0; out.ringScale = 1; out.flash = 0; out.lift = 0; out.sung = 0;
  const inS = gentle ? L.gentleInS : L.inS, a = sat(v.inT / inS);
  const grow = a < 1 ? ease(a) * (1 + (gentle ? 0 : L.overshoot) * Math.sin(Math.PI * a)) : 1;
  const bob = (gentle ? 0.5 : 1) * L.bobM * Math.sin((v.inT * L.bobHz + seed) * Math.PI * 2);
  if (v.state === 'live') {
    // before the word: a small swell as it nears; on the word: the light and a kick
    const pre = v.remainS > 0 && v.remainS <= L.nowS ? 1 - v.remainS / L.nowS : 0;
    out.sung = sungLight(v, gentle, L);
    const since = -v.remainS;
    const kick = since >= 0 ? (gentle ? 0.4 : 1) * L.sungKick * Math.exp(-since / L.sungKickS) * ease(since / 0.03) : 0;
    out.alpha = ease(a);
    out.scale = (0.6 + 0.4 * grow) * (1 + (gentle ? 0.5 : 1) * L.nowSwell * ease(pre) + kick);
    out.lift = bob;
    return out;
  }
  if (v.state === 'popped') {
    const ps = gentle ? L.gentlePopS : L.popS, q = sat(v.outT / ps);
    out.alpha = 1 - ease(q);
    out.scale = 1 + L.popGrow * (gentle ? 0.5 : 1) * ease(q);
    const rs = gentle ? L.gentleRingS : L.ringS, r = sat(v.outT / rs);
    out.ring = r < 1 ? L.ringAlpha * (1 - r) * (gentle ? 0.6 : 1) : 0;
    out.ringScale = 1 + L.ringGrow * ease(r);
    out.flash = flashOk && !gentle && v.outT < L.flashS ? L.flashAlpha * (1 - v.outT / L.flashS) : 0;
    out.lift = bob;
    return out;
  }
  // missed
  const ms = gentle ? L.gentleMissS : L.missS, q = sat(v.outT / ms);
  out.alpha = (1 - ease(q)) * 0.85;
  out.scale = 1 - 0.2 * ease(q);
  out.lift = bob + L.missRiseM * ease(q);
  return out;
}

/**
 * How big a word pop's juice is (render/wordPopFx.js), from the pop event (sim/words.js): particles in the
 * scene burst, light streaks flying out, the ground ring's size, and a flare (a line finished, a row, or a
 * streak landing on a tenth) that also asks photosafe. The streak grows the pop a little at a time, so a
 * long run of clean pops feels like it is building. Gentle: about half of everything, no flare.
 */
export const POP_JUICE = Object.freeze({
  particles: 18, particlesPerStreak: 0.6, particlesMax: 34, rowParticles: 1.6, lineParticles: 1.4,
  streaks: 8, streaksPerStreak: 0.35, streaksMax: 16, rowStreaks: 1.5, lineStreaks: 1.5,
  ringM: 2.2, rowRingM: 3.4, lineRingM: 4.2,
  streakLenM: [0.7, 1.5], streakSpeedMps: [7, 13], streakLifeS: 0.38, streakW: 0.045, nearM: [2.5, 9], peakAlpha: 0.8,
  gentleMul: 0.5,
});

export function popJuice(e, { gentle = false, J = POP_JUICE } = {}) {
  const streak = Math.max(1, e.streak | 0), row = (e.row | 0) > 0, line = !!e.lineDone;
  const big = (row ? 1 : 0) + (line ? 1 : 0) + (streak % 10 === 0 ? 1 : 0);
  const mul = gentle ? J.gentleMul : 1;
  const p = Math.min(J.particlesMax, J.particles + J.particlesPerStreak * (streak - 1)) * (row ? J.rowParticles : 1) * (line ? J.lineParticles : 1);
  const s = Math.min(J.streaksMax, J.streaks + J.streaksPerStreak * (streak - 1)) * (row ? J.rowStreaks : 1) * (line ? J.lineStreaks : 1);
  return {
    particles: Math.round(p * mul), streaks: Math.round(s * mul),
    ringM: line ? J.lineRingM : row ? J.rowRingM : J.ringM,
    flare: !gentle && big > 0, gold: line || streak % 10 === 0, level: Math.min(1, (streak - 1) / 20),
  };
}

/** Font size (px) that fits a word into a cell of width px (bold system-ui at about 0.6 em per glyph). */
export function fitFontPx(word, cellPx, { max = 0.3, min = 0.12, fill = 0.8, perChar = 0.6 } = {}) {
  const n = Math.max(1, String(word).length);
  return Math.round(Math.max(cellPx * min, Math.min(cellPx * max, (cellPx * fill) / (n * perChar))));
}
