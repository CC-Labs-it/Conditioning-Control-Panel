/* Boudoir road type: cream satin, rose lacquer and a continuous blackberry outline. */
import { STYLES } from '../roadWordsMath.js';

const edge = Object.freeze([0.055, 0.008, 0.032]);
const rose = Object.freeze([1.0, 0.30, 0.56]);
const modes = [
  { face: 'rounded', weight: 800, ink: [1.14, 1.04, 0.94], glowA: 0.24, gloss: 0.25 },
  { face: 'rounded', weight: 900, ink: [1.17, 0.93, 1.00], glowA: 0.28, spacing: 0.025 },
  { face: 'editorial', weight: 400, ink: [1.25, 1.04, 0.82], em: 0.84, glowA: 0.33, gloss: 0.75 },
  { face: 'display', weight: 700, ink: [1.20, 0.99, 0.94], upper: true, spacing: 0.045, glowA: 0.25, gloss: 0.3 },
  { face: 'rounded', weight: 1000, ink: [1.22, 0.80, 0.94], glowA: 0.38, gloss: 0.5 },
  { face: 'display', weight: 700, ink: [1.35, 0.88, 1.02], upper: true, glowA: 0.58, energy: 0.5, gloss: 0.5 },
  { face: 'editorial', weight: 400, ink: [1.30, 1.12, 1.01], em: 0.84, glowA: 0.42, gloss: 1, energy: 0.2 },
];

const styles = Object.freeze(STYLES.map((base, i) => Object.freeze({
  ...base, em: 0.80, spacing: 0.015, upper: false, stroke: 0.18,
  inkA: 1, edge, edgeA: 1, glow: rose,
  stencil: 0, wear: 0, kick: 0.10, glitch: 0, energy: 0, gloss: 0,
  ...modes[i], ink: Object.freeze(modes[i].ink),
})));

const TAU = Math.PI * 2;
const FLOAT = /\b(float|drift|satin|silk|soft)\b/i;
const KISS = /\b(kiss(?:es)?|blush|pink|gloss)\b/i;
const BREATH = /\b(breathe?|slow|ease|relax)\b/i;
const STILL = /\b(hold|still|stay|stop)\b/i;

/** A satin sway, a small kiss pulse or a slow breath moves the complete readable label. */
function pose(text, timeS, beatPhase, gentle, out = {}) {
  out.offsetM = 0; out.liftM = 0; out.scale = 1;
  if (gentle) return out;
  const t = Number.isFinite(timeS) ? timeS : 0;
  const phase = Number.isFinite(beatPhase) ? beatPhase : 0;
  const label = String(text ?? '');
  const pulse = 0.5 - 0.5 * Math.cos(phase * TAU);
  if (STILL.test(label)) return out;
  if (BREATH.test(label)) {
    const breath = 0.5 - 0.5 * Math.cos(t * TAU / 5.2);
    out.scale = 1 + 0.045 * breath;
    out.liftM = 0.075 * breath;
  } else if (FLOAT.test(label)) {
    out.offsetM = 0.20 * Math.sin(t * 1.1);
    out.liftM = 0.12 * (0.5 - 0.5 * Math.cos(t * 1.1));
  } else if (KISS.test(label)) {
    out.scale = 1 + 0.04 * pulse;
    out.liftM = 0.10 * pulse;
  } else {
    out.scale = 1 + 0.012 * pulse;
    out.liftM = 0.035 * pulse;
  }
  return out;
}

export default Object.freeze({ id: 'boudoir', styles, pose });
