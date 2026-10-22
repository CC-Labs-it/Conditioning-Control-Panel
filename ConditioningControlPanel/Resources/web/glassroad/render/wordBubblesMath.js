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
 * Pass 4 (owner: "the text inside the bubbles is actually shiny, pink and cool to look at"): far fewer bubbles, so
 * they are bigger (sizeM by row), and BUBBLE_TEXT is how their word is lettered (render/wordBubbles.js draws it).
 */
export const BUBBLE_LOOK = Object.freeze({
  sizeM: 3.0, gateSizeM: 2.4, triggerSizeM: 2.0,   // a trigger row is five across the road: they touch, a foam chain
  inS: 0.35, gentleInS: 0.7, overshoot: 0.08,
  bobM: 0.12, bobHz: 0.6,
  nowSwell: 0.08, nowS: 0.3,
  sungRiseS: 0.06, sungFadeS: 0.3, gentleSungRiseS: 0.3, gentleSungFadeS: 0.6, gentleSung: 0.6,
  sungKick: 0.14, sungKickS: 0.16,
  popS: 0.32, gentlePopS: 0.6, popGrow: 0.5,
  wordOutS: 0.08, gentleWordOutS: 0.3,   // pass 4: on a pop the word leaves the glass at once (it is the rush now)
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
 * sung (0..1, the karaoke light), text (0..1, the word inside the glass) }. flashOk: the photosafe budget granted this pop its flash.
 */
export function bubbleLook(v, { gentle = false, flashOk = false, seed = 0, L = BUBBLE_LOOK } = {}, out = {}) {
  out.ring = 0; out.ringScale = 1; out.flash = 0; out.lift = 0; out.sung = 0; out.text = 1;
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
    // the word flies out to the camera (render/wordRush.js): the swelling glass is left empty, never a huge word
    out.text = 1 - ease(v.outT / (gentle ? L.gentleWordOutS : L.wordOutS));
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

/**
 * The lettering inside a bubble (pass 4): one face per kind, so a trigger word keeps its identity wherever it
 * shows (the word rush wears the same face). Plain impact words are glossy editorial italics, a gate word is
 * highway-sign capitals, a trigger phrase is soft round heavy type. Sizes are shares of the atlas cell.
 * Colours: a pink gradient fill (pale top, a hot pink horizon, deep magenta under it, a little bounce light at
 * the foot), a thick dark plum stroke, a soft pink halo, a white gloss band over the top of the letters and
 * seeded glitter inside them; the shader adds the slow shimmer sweep and the glitter twinkle.
 */
export const BUBBLE_TEXT = Object.freeze({
  faces: Object.freeze({ 0: 'editorial', 3: 'display', 5: 'rounded' }),
  weight: Object.freeze({ 0: 400, 3: 700, 5: 1000 }),
  upper: Object.freeze({ 0: false, 3: true, 5: false }),
  maxPx: 0.4, minPx: 0.12, fitW: 0.8,          // font size cap / floor and the widest line, x cell
  lineH: 0.98, twoLineMaxPx: 0.36,             // a two-word phrase may stack on two lines when that letters it bigger
  stroke: 0.15, haloBlur: 0.2,                 // x font px
  plum: '#2b0628', halo: 'rgba(255,64,170,0.85)',
  fill: Object.freeze([[0, '#fff2fa'], [0.34, '#ffa6da'], [0.5, '#ff4cb3'], [0.78, '#d5137f'], [1, '#ff86cc']]),
  glossTo: 0.47, glossAlpha: 0.72,             // the gloss band covers the top of the letters to here (0 top, 1 foot)
  glitter: 22, stars: 3,                       // glitter dots inside the letters; four-point glints around them
  sweepHz: 0.42, gentleSweepHz: 0.2, sweepGain: 0.75, gentleSweepGain: 0.4, twinkleHz: 5,
});

/** The face key (render/fonts.js FACES) and weight a bubble of this row letters its word in. */
export const bubbleFace = (row, T = BUBBLE_TEXT) => ({ face: T.faces[row] || T.faces[0], weight: T.weight[row] || T.weight[0], upper: !!T.upper[row] });

/** ASCII only (the faces are ASCII subsets): curly quotes and dashes become plain ones, anything else is dropped. */
export const asciiWord = (w) => String(w || '').replace(/[‘’‚′]/g, "'").replace(/[“”„]/g, '"')
  .replace(/[–—]/g, '-').replace(/[^\x20-\x7e]/g, '');

/** The font px that fits `measured` px (the word's width at refPx) into the cell, inside the size cap and floor. */
export function fitPx(measured, refPx, cellPx, T = BUBBLE_TEXT) {
  const want = measured > 0 ? refPx * (T.fitW * cellPx) / measured : refPx;
  return Math.round(Math.max(cellPx * T.minPx, Math.min(cellPx * T.maxPx, want)));
}

/**
 * How a word is laid out in its cell: { px, lines }. `measure(str)` is the width of str at refPx in the face.
 * One line fitted to the cell; a phrase with a space stacks on two lines (split nearest the middle) when that
 * letters it bigger ("good / girl" on a trigger bubble, instead of a thin strip).
 */
export function letterLayout(text, measure, refPx, cellPx, T = BUBBLE_TEXT) {
  const one = fitPx(measure(text), refPx, cellPx, T);
  const sp = [];
  for (let i = 0; i < text.length; i++) if (text[i] === ' ') sp.push(i);
  if (!sp.length) return { px: one, lines: [text] };
  let cut = sp[0];
  for (const i of sp) if (Math.abs(i - text.length / 2) < Math.abs(cut - text.length / 2)) cut = i;
  const a = text.slice(0, cut), b = text.slice(cut + 1);
  const two = Math.min(Math.round(cellPx * T.twoLineMaxPx), fitPx(Math.max(measure(a), measure(b)), refPx, cellPx, T));
  return two > one ? { px: two, lines: [a, b] } : { px: one, lines: [text] };
}
