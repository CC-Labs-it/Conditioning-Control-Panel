/* Threshold road type: pearl enamel and prism edges held inside a deep plum outline. */
import { STYLES } from '../roadWordsMath.js';

const edge = Object.freeze([0.035, 0.012, 0.048]);
const modes = [
  { face: 'display', ink: [1.12, 1.08, 1.02], glow: [0.74, 0.60, 1.0], glowA: 0.24 },
  { face: 'condensed', ink: [1.15, 1.02, 1.11], glow: [1.0, 0.40, 0.72], glowA: 0.28, spacing: 0.055 },
  { face: 'editorial', weight: 400, upper: false, em: 0.84, ink: [1.20, 1.12, 0.95], glow: [0.48, 0.92, 1.0], glowA: 0.32, gloss: 0.55 },
  { face: 'display', ink: [1.18, 1.15, 1.12], glow: [1.0, 0.46, 0.78], glowA: 0.23, spacing: 0.045 },
  { face: 'condensed', ink: [0.96, 1.18, 1.19], glow: [0.42, 0.90, 1.0], glowA: 0.35, gloss: 0.4 },
  { face: 'display', ink: [1.30, 1.02, 1.18], glow: [1.0, 0.32, 0.68], glowA: 0.58, energy: 0.55, gloss: 0.6 },
  { face: 'editorial', weight: 400, upper: false, em: 0.84, ink: [1.25, 1.20, 1.08], glow: [0.67, 0.80, 1.0], glowA: 0.42, gloss: 1, energy: 0.2 },
];

const styles = Object.freeze(STYLES.map((base, i) => Object.freeze({
  ...base, weight: 700, em: 0.80, spacing: 0.025, upper: true, stroke: 0.18,
  inkA: 1, edge, edgeA: 1, stencil: 0, wear: 0, glitch: 0, kick: 0.10, energy: 0, gloss: 0,
  ...modes[i], ink: Object.freeze(modes[i].ink), glow: Object.freeze(modes[i].glow),
})));

const TAU = Math.PI * 2;
const FOCUS = /\b(focus|hold|still|stay|stop)\b/i;
const DRIFT = /\b(drift|float|flow|glide)\b/i;
const RISE = /\b(rise|lift|up|open)\b/i;
const BREATH = /\b(breathe?|slow|ease|relax)\b/i;

/** A complete enamel label moves as one piece; the dark outline never fragments. */
function pose(text, timeS, beatPhase, gentle, out = {}) {
  out.offsetM = 0; out.liftM = 0; out.scale = 1;
  if (gentle) return out;
  const t = Number.isFinite(timeS) ? timeS : 0;
  const phase = Number.isFinite(beatPhase) ? beatPhase : 0;
  const label = String(text ?? '');
  const pulse = 0.5 - 0.5 * Math.cos(phase * TAU);
  if (FOCUS.test(label)) return out;
  if (DRIFT.test(label)) {
    out.offsetM = 0.26 * Math.sin(t * 0.85);
    out.liftM = 0.09 * (0.5 - 0.5 * Math.cos(t * 0.85));
  } else if (RISE.test(label)) {
    out.liftM = 0.25 * (0.5 - 0.5 * Math.cos(t * 1.1));
    out.scale = 1 + 0.02 * pulse;
  } else if (BREATH.test(label)) {
    const breath = 0.5 - 0.5 * Math.cos(t * TAU / 5.6);
    out.scale = 1 + 0.04 * breath;
    out.liftM = 0.05 * breath;
  } else {
    out.liftM = 0.045 * pulse;
    out.scale = 1 + 0.012 * pulse;
  }
  return out;
}

export default Object.freeze({ id: 'threshold', styles, pose });
