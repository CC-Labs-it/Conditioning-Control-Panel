/* render/slipMath.js - the slipstream ribbon's numbers and envelope (interaction wave, 2026-10-30). Pure: node tests read it.
 *
 * The sim already tracks the draft (sim/contact.js): fxv.slip is the wake charge 0..1, fxv.slipFrom the kart ahead.
 * render/slipRibbon.js draws thin translucent streaks from that kart's tail to the drafter's nose. Each ribbon
 * eases IN as the drafter enters the wake, grows with the charge, POPS on the slipstream fire (wider and brighter,
 * then decays) and fades OUT when the draft ends. Gentle Visuals: the rise never beats 300 ms, the fade is slower,
 * the pop is softer and the streaks never scroll (no flicker). Phones get one strand with fewer points.
 */
export const SLIP_RIBBON = Object.freeze({
  inS: 0.22, outS: 0.35, gentleRiseS: 0.3, gentleOutS: 0.8,
  popS: 0.35, gentlePopS: 0.7, popWidthMul: 2.2, gentlePopGain: 0.5,
  baseShare: 0.3,                    // a fresh draft shows this share; the charge brings the rest
  widthM: [0.05, 0.16], alpha: [0.12, 0.5], popAlpha: 0.35,
  points: 10, phonePoints: 5, strands: 3, phoneStrands: 1, spreadM: 0.32,
  dashPerM: 0.55, dashMps: 9, dashDepth: 0.45,
  colour: '#cfe8ff', popColour: '#ffffff', upM: 0.28, maxGapM: 18,
});

/** A fresh ribbon state per racer slot. */
export const newRibbon = () => ({ a: 0, pop: 0, from: -1, popGain: 1 });

const approach = (v, target, tcS, dt) => v + (target - v) * (1 - Math.exp(-dt / Math.max(tcS, 1e-3)));

/**
 * One frame of a ribbon. slip = fxv.slip (0..1), from = fxv.slipFrom (-1 none), fired = the slipstream fire
 * landed this frame. The ribbon keeps its last kart ahead while it fades or pops. Returns r.
 */
export function stepRibbon(r, { slip = 0, from = -1, fired = false } = {}, dt = 0, { gentle = false, L = SLIP_RIBBON } = {}) {
  if (from >= 0) r.from = from;
  if (fired && r.from >= 0) { r.pop = 1; r.popGain = gentle ? L.gentlePopGain : 1; }
  const on = from >= 0;
  const target = on ? L.baseShare + (1 - L.baseShare) * Math.min(1, Math.max(0, slip)) : 0;
  const tc = target > r.a ? (gentle ? Math.max(L.inS, L.gentleRiseS) : L.inS) : (gentle ? L.gentleOutS : L.outS);
  if (dt > 0) {
    r.a = approach(r.a, target, tc / 3, dt);   // about 95% of the way in tc
    if (r.pop > 0) r.pop = Math.max(0, r.pop - dt / (gentle ? L.gentlePopS : L.popS));
  }
  if (!on && r.a < 0.01 && r.pop <= 0) { r.a = 0; r.from = -1; }
  return r;
}

/** What to draw: { width (m), alpha (0..1), white (0..1 toward the pop colour) }; alpha 0 = draw nothing. */
export function ribbonLook(r, L = SLIP_RIBBON) {
  const pop = r.pop * r.pop * (r.popGain ?? 1);
  const width = (L.widthM[0] + (L.widthM[1] - L.widthM[0]) * r.a) * (1 + (L.popWidthMul - 1) * pop);
  const alpha = Math.min(1, (r.a > 0 ? L.alpha[0] + (L.alpha[1] - L.alpha[0]) * r.a : 0) + L.popAlpha * pop);
  return { width, alpha, white: pop };
}

/**
 * The dash shading along a strand at u (0 = the kart ahead, 1 = the drafter) for a strand lenM long at time t:
 * streaks flowing back toward the drafter, ends tapered. Gentle: still (no scroll, a smooth taper only).
 */
export function dashAt(u, lenM, t, { gentle = false, L = SLIP_RIBBON } = {}) {
  const taper = Math.sin(Math.PI * Math.min(1, Math.max(0, u)));
  if (gentle) return taper;
  const ph = (u * lenM * L.dashPerM - t * L.dashMps * L.dashPerM) * Math.PI * 2;
  return taper * (1 - L.dashDepth * (0.5 + 0.5 * Math.sin(ph)));
}
