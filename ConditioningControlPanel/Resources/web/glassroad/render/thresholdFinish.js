/* Cast glass keeps its faint annealing ripples as the coloured road flows beneath it. */
import { float, sin, max, fwidth, smoothstep } from 'three/tsl';

export function thresholdGlassFinish(p) {
  const filtered = phase => sin(phase).mul(float(1).sub(smoothstep(.5, 1.6, fwidth(phase))));
  const ribbonPhase = p.y.mul(.19).add(sin(p.x.mul(.48)).mul(.55));
  const ribbon = filtered(ribbonPhase);
  const casting = filtered(p.x.mul(2.3).add(sin(ribbonPhase).mul(.7)));
  const grainPhase = p.x.mul(37).add(p.y.mul(1.3));
  const fine = float(1).sub(smoothstep(.35, 1.2, max(fwidth(grainPhase), fwidth(p.y.mul(2)))));
  const grain = sin(grainPhase).mul(fine);
  // Broad crossing annealing waves catch pearly highlights without repainting the floor art.
  const pearlPhase = p.y.mul(.63).add(sin(p.x.mul(.9)).mul(1.4));
  const pearl = filtered(pearlPhase).mul(filtered(p.x.mul(.7).sub(p.y.mul(.21))));
  return {
    body: ribbon.mul(.034).add(casting.mul(.026)).add(grain.mul(.008)).add(pearl.mul(.012)).add(.98),
    glaze: casting.mul(.22).add(grain.mul(.08)).add(pearl.mul(.1)).add(.96),
    // A shallow moulded face bends the highlight while the accepted driving surface stays flat.
    height: sin(p.x.mul(2.3).add(sin(ribbonPhase).mul(.7))).mul(.018)
      .add(sin(ribbonPhase).mul(.045)),
  };
}
