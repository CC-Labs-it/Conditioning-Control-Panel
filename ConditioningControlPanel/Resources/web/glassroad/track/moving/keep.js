/* Horizontal ironwork on open Keep decks; the serpentine stays the permanent bypass. */
import { getKeepLayout } from '../keep-layout.js';

export function movingStations(track) {
  if (track.id !== 'keep') return { traps: [], bridge: null };
  const layout = getKeepLayout(track), landmark = id => layout.landmarks.find(row => row.id === id);
  const stations = [
    ['tower-bridge', 25, 'piston', 1],
    ['east-battlement', 35, 'slider', -1],
    ['crown-walk', 35, 'piston', -1],
    ['north-approach', 220, 'slider', 1],
    ['outer-rampart', 35, 'piston', 1],
    ['outer-rampart', 106, 'slider', -1],
  ];
  const traps = stations.map(([name, ds, kind, side], i) => {
    const s = landmark(name).s0 + ds, frame = track.frameAt(s);
    return { id: 'keep-iron-' + (i + 1), s, kind, side, w: 5, len: 4,
      x: side * Math.min(9, frame.w / 2 - 5), periodBeats: 2, offsetBeats: i % 2 };
  });

  // The chord passes below the raised serpentine, saving 43 m of the open main road.
  // Tangent handles make both mouths continuous with the permanent route.
  const from = landmark('serpentine-in').node, to = landmark('serpentine-out').to;
  const first = track.frameAt(track.nodeS[from]), last = track.frameAt(track.nodeS[to]);
  const points = [first.p, first.p.map((v, j) => v + first.t[j] * 25),
    last.p.map((v, j) => v - last.t[j] * 25), last.p]
    .map(p => ({ p: [...p], w: 8, roll: 0, surface: 'glass', flags: ['open'] }));
  const fork = { id: 'keep-piston-bridge', label: 'Piston bridge', kind: 'narrow', from, to, points };
  return { traps, bridge: { forkId: fork.id, fork, fromU: .3, toU: .65,
    periodS: 14, openS: 8, transitionS: .8 } };
}
