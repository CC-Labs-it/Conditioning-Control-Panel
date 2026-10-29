// Dry machine halls keep the centre lane open while side rams strike with the song.
export function movingStations(track) {
  if (track.id !== 'mainframe') return { traps: [], bridge: null };
  const anchors = [
    [133, 4, 'piston', -1], [157, 3, 'slider', 1],
    [544, 3, 'piston', 1], [553, 3, 'slider', -1],
    [564, 5, 'piston', -1], [677, 4, 'slider', 1],
  ];
  const traps = anchors.map(([node, ds, kind, side], i) => ({
    id: 'mainframe-bus-ram-' + i, s: track.nodeS[node] + ds,
    kind, side, w: 6, len: 5, travelM: 6, periodBeats: 2, offsetBeats: i % 2,
  }));
  // A dry chord cuts inside the gantry U-turn. The original road is always available.
  const from = 628, to = 650, a = track.frameAt(track.nodeS[from]), b = track.frameAt(track.nodeS[to]);
  const point = p => ({ p, w: 8, roll: 0, surface: 'glass', flags: ['open'] });
  const add = (p, axis, d) => p.map((v, j) => v + axis[j] * d);
  const lerp = u => a.p.map((v, j) => v + (b.p[j] - v) * u);
  const fork = { id: 'mainframe-piston-gantry', kind: 'timed', label: 'PISTON BRIDGE', from, to,
    points: [point([...a.p]), point(add(a.p, a.t, 12)), point(lerp(.3)), point(lerp(.7)),
      point(add(b.p, b.t, -12)), point([...b.p])] };
  // Broader road turns lengthen the chord; shorten only its moving cut, retaining the timer.
  return { traps, bridge: { forkId: fork.id, fork, fromU: .4, toU: .6, periodS: 14, openS: 8, transitionS: .8 } };
}
