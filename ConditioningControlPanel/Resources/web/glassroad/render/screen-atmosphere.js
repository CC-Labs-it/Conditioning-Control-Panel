/* Bounded screen atmosphere. All motion advances on the caller's pause-aware frame delta. */
export const ATMOSPHERE = Object.freeze({
  fogFillS: 3.6, fogLayers: 4, fogDepth: 1.3, fogDrift: 0.045,
  fogFar: '#8a246b', fogNear: '#ff9fd3',
  tunnelFrom: 1.3, tunnelTo: 0.19, tunnelSoft: 0.055, tunnelFloor: 0.025,
  meltWarp: 1.65, meltStreak: 1.5, meltChroma: 1.4,
});
const sat = (v) => Math.max(0, Math.min(1, v));
const ease = (v) => { const t = sat(v); return t * t * (3 - 2 * t); };

export const tunnelRadius = (amount) => ATMOSPHERE.tunnelFrom + (ATMOSPHERE.tunnelTo - ATMOSPHERE.tunnelFrom) * sat(amount);
export function fogFill(amount, age, durationS = 0) {
  const ingressS = durationS > 0 ? Math.min(ATMOSPHERE.fogFillS, durationS * 0.7) : ATMOSPHERE.fogFillS;
  return sat(amount) * ease(age / ingressS);
}

/** A replaced pink effect keeps its age through its fade. A fresh hit starts a new ingress. */
export function createAtmosphereClock() {
  let age = 0, seed = null;
  return {
    update(screen, amount, dt, timeMul = 1) {
      const nextSeed = screen?.params?.seed;
      if (!(amount > 0.001)) { age = 0; seed = null; return 0; }
      if (screen?.kind === 'pink' && nextSeed !== seed) { age = 0; seed = nextSeed; }
      age += Math.max(0, dt || 0) * timeMul;
      return fogFill(amount, age, screen?.durS);
    },
  };
}
