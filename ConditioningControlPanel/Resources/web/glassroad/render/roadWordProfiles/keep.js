/* The Keep: ivory inscriptions, gilded seals and dark stone cuts. Motion moves
 * the whole inscription, preserving the letter shapes at racing speed. */
import { STYLES } from '../roadWordsMath.js';

const STONE = Object.freeze([0.055, 0.028, 0.045]);
const GOLD = Object.freeze([1.0, 0.63, 0.20]);
const IVORY = Object.freeze([1.20, 1.10, 0.86]);
const style = (index, overrides) => Object.freeze({
  ...STYLES[index], face: 'display', weight: 700, em: 0.80, spacing: 0.035,
  upper: true, ink: IVORY, inkA: 1, edge: STONE, edgeA: 1,
  glow: GOLD, glowA: 0.34, stroke: 0.18,
  stencil: 0, wear: 0.06, glitch: 0, kick: 0.08, energy: 0.2, gloss: 0,
  ...overrides,
});

const styles = Object.freeze([
  style(0, { id: 'keep-chalk', face: 'condensed', em: 0.76, spacing: 0.045,
    ink: [1.16, 1.12, 1.02], glowA: 0.18, kick: 0, wear: 0.10 }),
  style(1, { id: 'keep-carved', ink: [1.18, 1.05, 0.78], glowA: 0.24, wear: 0.12 }),
  style(2, { id: 'keep-herald', spacing: 0.06, ink: [1.26, 1.08, 0.62],
    glowA: 0.38, gloss: 0.35 }),
  style(3, { id: 'keep-stone', face: 'condensed', em: 0.78, spacing: 0.055,
    ink: [1.12, 1.12, 1.06], stroke: 0.20, wear: 0.16, kick: 0.16 }),
  style(4, { id: 'keep-seal', spacing: 0.07, ink: [1.20, 0.97, 0.63],
    glowA: 0.30, stroke: 0.20, kick: 0.12, gloss: 0.25 }),
  style(5, { id: 'keep-gilt', ink: [1.40, 1.20, 0.74], glowA: 0.64,
    kick: 0.20, energy: 0.65, gloss: 0.65, wear: 0 }),
  style(6, { id: 'keep-enamel', ink: [1.24, 1.09, 1.02], glow: [1.0, 0.49, 0.33],
    glowA: 0.46, gloss: 0.80, kick: 0.12, wear: 0.02 }),
]);

const TAU = Math.PI * 2;

/** Bounds leave space for the common renderer's rail and atlas safety margins.
 * The supplied song clock freezes the pose on pause. Gentle leaves it planted. */
function pose(text, timeS, beatPhase, gentle, out = {}) {
  out.offsetM = 0; out.liftM = 0; out.scale = 1;
  if (gentle) return out;
  const t = Number.isFinite(timeS) ? timeS : 0;
  const phase = Number.isFinite(beatPhase) ? beatPhase : 0;
  const breath = 0.5 - 0.5 * Math.cos(t * TAU / 4.8);
  const beat = 0.5 - 0.5 * Math.cos(phase * TAU);
  const word = String(text ?? '').toLowerCase();
  // A gate inscription feels latched, even while the floor around it responds.
  if (/\b(hold|stay|lock(?:ed)?|still|stone|bound)\b/.test(word)) return out;
  if (/\b(rise|lift|float|up|open)\b/.test(word)) {
    out.liftM = 0.24 * breath;
    out.scale = 1 + 0.018 * breath;
  } else if (/\b(breathe|breath|soft|rest|slow)\b/.test(word)) {
    out.scale = 0.99 + 0.04 * breath;
    out.liftM = 0.035 * breath;
  } else if (/\b(turn|sway|drift|follow)\b/.test(word)) {
    out.offsetM = 0.16 * Math.sin(t * TAU / 5.6);
    out.liftM = 0.025 * breath;
  } else {
    // A heavy seal answers the beat with a small press, never a springy bounce.
    out.scale = 1 - 0.012 * beat;
    out.liftM = 0.018 * breath;
  }
  return out;
}

export default Object.freeze({ id: 'keep', styles, pose });
