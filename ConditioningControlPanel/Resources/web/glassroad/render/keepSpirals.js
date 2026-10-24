import { getKeepLayout } from '../track/keep-layout.js';
/* Authored Loom portals are positioned from course landmarks, never a stale lap fraction. */
export function keepSpiralLayout(track) {
  const layout = getKeepLayout(track);
  if (!layout) return null;
  const gates = [];
  for (const [id, fraction, radiusM, depthM] of [
    ['outer-exit', .65, 20, 10], ['tower-spiral', .28, 19, 18],
    ['tower-spiral', .78, 19, 18], ['crown-walk', .55, 24, 24], ['moat-return', .7, 21, 14],
  ]) {
    const p = layout.landmarks.find(l => l.id === id);
    if (!p) continue;
    gates.push({ id: 'keep-loom-' + gates.length, f: (p.s0 + (p.s1 - p.s0) * fraction) / track.lengthM,
      radiusM, depthM, helixTurns: .22, upM: 4,
      loom: { schema: 2, speed: 2, layer: { arms: 3, turns: 1.25, duty: .4, style: 'log', direction: 1,
        colors: ['#ff4fa4', '#ffd799', '#b4deee'], bandMode: 'hard', speedMul: 1 },
        layer2: { enabled: true, arms: 2, turns: .75, duty: .18, style: 'arch', direction: -1,
          colors: ['#fc9ebc'], bandMode: 'hard', speedMul: 1 },
        glow: .4, pulse: { amp: 0, cycles: 1 }, wobble: { amp: 0, freq: 2, cycles: 1 }, hueCycles: 0 } });
  }
  return { track: 'keep', gates };
}
