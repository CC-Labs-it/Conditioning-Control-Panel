/* render/wordBubblesMath.js - how a word bubble looks at a moment (render/wordBubbles.js), pure.
 *
 * Every time comes from the bubble view (sim/words.js), which reads the song clock: inT since it showed,
 * outT since it popped or was missed, remainS to its word. IN: grows and fades in with a small overshoot.
 * While live: a slow bob and a swell as its word arrives. OUT on a pop: the glass swells and clears, a
 * ring runs out, an optional flash core (only when the photosafe budget allowed it). OUT on a miss: it
 * shrinks, rises a little and fades. Gentle: every fade and rise slower, smaller moves, no flash.
 */
export const BUBBLE_LOOK = Object.freeze({
  sizeM: 2.2, gateSizeM: 1.9,
  inS: 0.35, gentleInS: 0.7, overshoot: 0.08,
  bobM: 0.12, bobHz: 0.6,
  nowSwell: 0.1, nowS: 0.35,
  popS: 0.32, gentlePopS: 0.6, popGrow: 0.5,
  ringS: 0.45, gentleRingS: 0.8, ringGrow: 1.6, ringAlpha: 0.9,
  flashS: 0.16, flashAlpha: 0.75, flashSize: 1.3,
  missS: 0.5, gentleMissS: 0.9, missRiseM: 0.4,
});

const sat = (x) => (x < 0 ? 0 : x > 1 ? 1 : x);
const ease = (x) => { x = sat(x); return x * x * (3 - 2 * x); };

/**
 * The bubble's draw: { alpha, scale (x sizeM), lift (metres up), ring (alpha, 0 = none), ringScale, flash (alpha) }.
 * flashOk: the photosafe budget granted this pop its flash.
 */
export function bubbleLook(v, { gentle = false, flashOk = false, seed = 0, L = BUBBLE_LOOK } = {}, out = {}) {
  out.ring = 0; out.ringScale = 1; out.flash = 0; out.lift = 0;
  const inS = gentle ? L.gentleInS : L.inS, a = sat(v.inT / inS);
  const grow = a < 1 ? ease(a) * (1 + (gentle ? 0 : L.overshoot) * Math.sin(Math.PI * a)) : 1;
  const bob = (gentle ? 0.5 : 1) * L.bobM * Math.sin((v.inT * L.bobHz + seed) * Math.PI * 2);
  if (v.state === 'live') {
    const now = v.remainS <= L.nowS && v.remainS > -L.nowS ? 1 - Math.abs(v.remainS) / L.nowS : 0;
    out.alpha = ease(a);
    out.scale = (0.6 + 0.4 * grow) * (1 + (gentle ? 0.5 : 1) * L.nowSwell * ease(now));
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

/** Font size (px) that fits a word into a cell of width px (bold system-ui at about 0.6 em per glyph). */
export function fitFontPx(word, cellPx, { max = 0.3, min = 0.12, fill = 0.8, perChar = 0.6 } = {}) {
  const n = Math.max(1, String(word).length);
  return Math.round(Math.max(cellPx * min, Math.min(cellPx * max, (cellPx * fill) / (n * perChar))));
}
