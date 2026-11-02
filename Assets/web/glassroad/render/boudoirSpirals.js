import { getBoudoirLayout } from '../track/boudoir-layout.js';

/** Room-scale Loom passages on the broad road, away from jumps and shortcut landings. */
export function boudoirSpiralLayout(track) {
  const layout = getBoudoirLayout(track); if (!layout) return null;
  const gates = [];
  for (const [id, share, arms] of [['powder-return', .6, 3], ['shelf-climb', .24, 2], ['shelf-climb', .7, 5],
    ['wardrobe-descent', .33, 4], ['wardrobe-descent', .77, 3], ['bath-approach', .25, 2], ['home', .35, 5]]) {
    const section = layout.landmarks.find(l => l.id === id); if (!section) continue;
    const s = section.s0 + (section.s1 - section.s0) * share, frame = track.frameAt(s);
    gates.push({ id: 'boudoir-loom-' + gates.length, f: s / track.lengthM, radiusM: Math.max(22, frame.w / 2 + 8),
      depthM: 12, upM: 3, helixTurns: .28,
      loom: { schema: 2, speed: 2,
        layer: { arms, turns: 1.2 + (arms % 2) * .6, duty: .38, style: arms % 2 ? 'log' : 'arch', direction: 1,
          colors: ['#f7a7cd', '#ffe9c6', '#c2a5ee'], bandMode: 'hard', speedMul: 1 },
        layer2: { enabled: true, arms: arms === 2 ? 5 : 2, turns: .8, duty: .16, style: 'arch', direction: -1,
          colors: ['#fff5db', '#ce97db'], bandMode: 'hard', speedMul: .7 },
        glow: .35, pulse: { amp: 0, cycles: 1 }, wobble: { amp: .035, freq: 2, cycles: 1 }, hueCycles: 0 } });
  }
  return { track: 'boudoir', gates };
}
