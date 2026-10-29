/* Goonworks road type: cream comic ink, dark plum borders and fairground tube colours. */
import { STYLES } from '../roadWordsMath.js';

const edge = Object.freeze([0.035, 0.006, 0.028]);
const modes = [
  { face: 'rounded', weight: 850, em: .78, ink: [1.10, 1.03, .87], glow: [1, .38, .57], glowA: .22 },
  { face: 'rounded', weight: 1000, em: .79, ink: [1.20, 1.08, .88], glow: [1, .50, .18], glowA: .30 },
  { face: 'display', weight: 700, em: .82, ink: [1.24, .96, .78], glow: [1, .25, .52], glowA: .42, gloss: .65 },
  { face: 'condensed', weight: 700, em: .80, ink: [1.18, 1.14, .94], glow: [.28, .95, 1], glowA: .24, spacing: .055 },
  { face: 'rounded', weight: 950, em: .78, ink: [1.12, 1.14, 1.02], glow: [.25, 1, .85], glowA: .34, gloss: .35 },
  { face: 'display', weight: 700, em: .84, ink: [1.40, 1.17, .91], glow: [1, .17, .58], glowA: .64, energy: .65 },
  { face: 'rounded', weight: 1000, em: .80, ink: [1.30, 1.03, .93], glow: [1, .54, .16], glowA: .46, gloss: 1, energy: .3 },
];

const styles = Object.freeze(STYLES.map((base, i) => Object.freeze({
  ...base, spacing: .025, upper: true, stroke: .19, inkA: 1, edge, edgeA: 1,
  stencil: 0, wear: 0, glitch: 0, kick: .12, energy: 0, gloss: 0,
  ...modes[i], ink: Object.freeze(modes[i].ink), glow: Object.freeze(modes[i].glow),
})));

const TAU = Math.PI * 2;
const HOLD = /\b(hold|still|stop|stay|locked)\b/i;
const SPIN = /\b(spin|turn|round|roll)\b/i;
const WATCH = /\b(watch|look|focus|see)\b/i;
const BOUNCE = /\b(bounce|jump|pop|hop)\b/i;

/** Playful whole-label motion leaves the outline and letter spacing intact. */
function pose(text, timeS, beatPhase, gentle, out = {}) {
  out.offsetM = 0; out.liftM = 0; out.scale = 1;
  if (gentle) return out;
  const label = String(text ?? '');
  const t = Number.isFinite(timeS) ? timeS : 0;
  const phase = Number.isFinite(beatPhase) ? beatPhase : 0;
  const pulse = .5 - .5 * Math.cos(phase * TAU);
  if (HOLD.test(label)) return out;
  if (SPIN.test(label)) {
    // A little carousel orbit, not a rotating word that becomes unreadable.
    out.offsetM = .22 * Math.sin(t * 1.4);
    out.liftM = .07 * (.5 - .5 * Math.cos(t * 1.4));
  } else if (WATCH.test(label)) {
    // A magnifying breath on the invitation to look.
    out.scale = 1 + .035 * (.5 - .5 * Math.cos(t * TAU / 4));
  } else if (BOUNCE.test(label)) {
    out.liftM = .22 * pulse;
    out.scale = 1 + .04 * pulse;
  } else {
    out.liftM = .035 * pulse;
    out.scale = 1 + .012 * pulse;
  }
  return out;
}

export default Object.freeze({ id: 'goonworks', styles, pose });
