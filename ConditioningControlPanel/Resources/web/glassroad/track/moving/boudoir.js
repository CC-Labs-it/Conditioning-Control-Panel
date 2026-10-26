import { getBoudoirLayout } from '../boudoir-layout.js';
import { getBoudoirSetpieces } from '../boudoir-setpieces.js';

const ANCHORS = [['powder-sweep', .5], ['shelf-approach', .4], ['pearl-slalom-out', .4],
  ['upper-turn', .7], ['wardrobe-out', .85], ['home-turn', .55]];

function powderBridge(track) {
  const from = 46, to = 69;
  const a = track.frameAt(track.nodeS[from]), b = track.frameAt(track.nodeS[to]), handle = 8;
  const point = p => ({ p: [...p], w: 9, roll: 0, surface: 'glass', flags: ['open'] });
  const fork = { id: 'boudoir-powder-piston-bridge', label: 'Powder bridge', kind: 'timed', from, to,
    points: [point(a.p), point(a.p.map((v, i) => v + a.t[i] * handle)),
      point(b.p.map((v, i) => v - b.t[i] * handle)), point(b.p)] };
  return { forkId: fork.id, fork, fromU: .4, toU: .6, periodS: 14, openS: 8, transitionS: .8 };
}

function clearStation(track, target, avoid) {
  for (let radius = 0; radius <= 45; radius += 2) for (const sign of radius ? [1, -1] : [1]) {
    const s = target + radius * sign, f = track.frameAt(s);
    if (s < 200 || s > track.lengthM - 120 || f.w < 28 || f.n[1] < .97 || Math.abs(f.t[1]) > .16 || f.flags.includes('jump')) continue;
    if (avoid.some(a => s > a.s0 && s < a.s1)) continue;
    if ((track.boosts || []).some(b => Math.abs(b.s - s) < b.len / 2 + 14)) continue;
    if ((track.aerials || []).some(a => s > a.rampS0 - 35 && s < a.landingS1 + 30)) continue;
    if ((track.fields || []).some(a => s > a.fromS - 30 && s < a.toS + 30)) continue;
    const q = track.query(...f.p, f.seg);
    if (!q.onRoad || Math.abs(q.height) > .05) continue;
    return s;
  }
  return null;
}

export function movingStations(track) {
  if (track.id !== 'boudoir') return { traps: [], bridge: null };
  const layout = getBoudoirLayout(track), old = getBoudoirSetpieces(track, layout);
  const avoid = [...old.presses, ...old.sweepers, ...old.mirrorGates].map(r => ({ s0: r.s - 24, s1: r.s + 24 }));
  for (const r of [...old.conveyors, ...layout.perfume]) avoid.push({ s0: r.s0 - 18, s1: r.s1 + 18 });
  for (const node of [46, 69, 238, 270, 599, 680, 375, 506]) {
    const s = track.nodeS[node]; avoid.push({ s0: s - 45, s1: s + 45 });
  }
  const traps = [];
  for (const [i, [id, u]] of ANCHORS.entries()) {
    const section = layout.landmarks.find(s => s.id === id);
    if (!section) continue;
    const s = clearStation(track, section.s0 + (section.s1 - section.s0) * u, avoid);
    if (s === null) continue;
    traps.push({ id: 'boudoir-satin-slide-' + (i + 1), s, kind: i % 2 ? 'piston' : 'slider',
      side: i % 2 ? -1 : 1, w: 5, len: 4, periodBeats: 2, offsetBeats: i % 2 });
  }
  // Keep the established dry fork permanent; this shorter powder chord owns the timed cut.
  return { traps, bridge: powderBridge(track) };
}
