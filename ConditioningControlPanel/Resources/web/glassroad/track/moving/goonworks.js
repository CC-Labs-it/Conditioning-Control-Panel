// Put the new timing on open approaches, leaving the densely packed toy halls alone.
import { getGoonworksLayout } from '../goonworks-layout.js';
import { goonworksSetpieces } from '../goonworks-setpieces.js';

export function movingStations(track) {
  if (track.id !== 'goonworks') return { traps: [], bridge: null };
  const layout = getGoonworksLayout(track), old = goonworksSetpieces(track, layout);
  // The rabbit bank rises above a parked head. Teach this stroke on the flat gallery entry.
  const anchors = [['grotto-gallery', .02], ['mill-approach', .55], ['quiz-approach', .55],
    ['ribbon-entry', .45], ['pinball-table', .25], ['finish-crossing', .5]];
  const traps = [];
  for (const [i, [name, u]] of anchors.entries()) {
    const span = layout.byId[name], target = span.s0 + (span.s1 - span.s0) * u;
    for (let delta = 0; delta <= 30; delta += 2) {
      const s = target + delta, f = track.frameAt(s);
      if (s > span.s1 - 12 || f.w < 28 || f.n[1] < .97 || Math.abs(f.t[1]) > .12) continue;
      if (old.some(row => Math.abs(row.s - s) < (row.depth || 6) / 2 + 20)) continue;
      if (track.boosts.some(row => Math.abs(row.s - s) < row.len / 2 + 16)) continue;
      if (track.aerials.some(row => s > row.rampS0 - 30 && s < row.landingS1 + 30)) continue;
      if (track.fields.some(row => s > row.fromS - 30 && s < row.toS + 30)) continue;
      if (!track.query(...f.p, f.seg).onRoad) continue;
      traps.push({ id: 'goonworks-sidewinder-' + (i + 1), s, kind: i % 2 ? 'piston' : 'slider',
        side: i % 2 ? -1 : 1, w: 5, len: 4, hitboxScale: .72, periodBeats: 2, offsetBeats: i % 2 });
      break;
    }
  }
  // A short flat chord cuts inside the final U-turn. The full turn is always driveable.
  const from = layout.byId['finish-west-turn'].from, to = layout.byId['finish-home-turn'].to;
  const first = track.frameAt(track.nodeS[from]), last = track.frameAt(track.nodeS[to]);
  const point = p => ({ p: [...p], w: 9, roll: 0, surface: 'glass', flags: ['open'] });
  const fork = { id: 'goonworks-piston-bridge', label: 'Piston bridge', kind: 'timed', from, to,
    points: [point(first.p), point(first.p.map((v, j) => v + first.t[j] * 12)),
      point(last.p.map((v, j) => v - last.t[j] * 12)), point(last.p)] };
  return { traps, bridge: { forkId: fork.id, fork, fromU: .42, toU: .58,
    periodS: 14, openS: 8, transitionS: .8 } };
}
