/* Mainframe road type: phosphor terminals, enamel circuit labels and green signal tubes. */
import { STYLES } from '../roadWordsMath.js';

const edge = Object.freeze([0.008, 0.018, 0.012]);
const phosphor = Object.freeze([0.22, 1.0, 0.38]);
const modes = [
  { face: 'mono', em: 0.74, ink: [0.90, 1.08, 0.94], glowA: 0.22 },
  { face: 'condensed', em: 0.80, ink: [1.08, 1.14, 1.09], glowA: 0.25, spacing: 0.045 },
  { face: 'display', em: 0.80, ink: [0.94, 1.17, 0.66], glowA: 0.36, gloss: 0.45 },
  { face: 'condensed', em: 0.78, ink: [1.16, 1.20, 1.16], glowA: 0.18, spacing: 0.055, stencil: 0.3, wear: 0.08 },
  { face: 'mono', em: 0.75, ink: [0.68, 1.20, 0.78], glowA: 0.42, spacing: 0.015 },
  { face: 'display', em: 0.82, ink: [0.93, 1.34, 0.99], glowA: 0.62, energy: 0.6 },
  { face: 'condensed', em: 0.80, ink: [1.18, 1.26, 1.12], glowA: 0.38, gloss: 1, energy: 0.25 },
];

const styles = Object.freeze(STYLES.map((base, i) => Object.freeze({
  ...base, weight: 700, spacing: 0.025, upper: true, stroke: 0.18,
  inkA: 1, edge, edgeA: 1, glow: phosphor,
  stencil: 0, wear: 0, kick: 0.12, glitch: 0, energy: 0, gloss: 0,
  ...modes[i], ink: Object.freeze(modes[i].ink),
})));

const TAU = Math.PI * 2;
const LOCK = /\b(lock(?:ed)?|hold|still|stop)\b/i;
const SCAN = /\b(scan(?:ning)?|search|read|seek)\b/i;
const FLOW = /\b(flow|drift|slide|stream)\b/i;
const BREATH = /\b(breathe?|slow|ease|soft)\b/i;

/** Whole labels move, never their letter strokes. Gentle keeps the paint completely planted. */
function pose(text, timeS, beatPhase, gentle, out = {}) {
  out.offsetM = 0; out.liftM = 0; out.scale = 1;
  if (gentle) return out;
  const t = Number.isFinite(timeS) ? timeS : 0;
  const phase = Number.isFinite(beatPhase) ? beatPhase : 0;
  const pulse = 0.5 - 0.5 * Math.cos(phase * TAU);
  const label = String(text ?? '');
  // A held command seats into the road, while a scan sweeps sideways across its lane.
  if (LOCK.test(label)) return out;
  if (SCAN.test(label)) out.offsetM = 0.24 * Math.sin(t * 1.2);
  else if (FLOW.test(label)) out.offsetM = 0.18 * Math.sin(t * 1.65);
  else if (BREATH.test(label)) {
    const breath = 0.5 - 0.5 * Math.cos(t * TAU / 4.8);
    out.scale = 1 + 0.035 * breath;
    out.liftM = 0.08 * breath;
    return out;
  }
  out.liftM = 0.055 * pulse;
  out.scale = 1 + 0.018 * pulse;
  return out;
}

export default Object.freeze({ id: 'mainframe', styles, pose });
