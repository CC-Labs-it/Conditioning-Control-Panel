/* Cast glass keeps its faint annealing ripples as the coloured road flows beneath it. */
import { float, sin, max, fwidth, smoothstep } from 'three/tsl';

export function thresholdGlassFinish(p) {
  const ribbon = sin(p.y.mul(.19).add(sin(p.x.mul(.48)).mul(.55)));
  const casting = sin(p.x.mul(2.3).add(ribbon.mul(.7)));
  const grainPhase = p.x.mul(37).add(p.y.mul(1.3));
  const fine = float(1).sub(smoothstep(.35, 1.2, max(fwidth(grainPhase), fwidth(p.y.mul(2)))));
  const grain = sin(grainPhase).mul(fine);
  return {
    body: ribbon.mul(.018).add(casting.mul(.012)).add(grain.mul(.006)).add(.985),
    glaze: casting.mul(.12).add(grain.mul(.06)).add(.94),
  };
}
