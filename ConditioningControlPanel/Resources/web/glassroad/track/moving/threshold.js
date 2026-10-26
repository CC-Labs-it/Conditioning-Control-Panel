// Keep the technical loops and stepping islands clear; teach the new timing on broad galleries.
const ANCHORS = [91, 111, 159, 249, 333, 350];
const distance = (a, b) => Math.hypot(...a.map((v, i) => v - b[i]));

function clearStation(track, target) {
  for (let radius = 0; radius <= 42; radius += 2) for (const sign of radius ? [1, -1] : [1]) {
    const s = target + radius * sign, f = track.frameAt(s);
    if (f.w < 24 || f.n[1] < .97 || Math.abs(f.t[1]) > .16 || f.flags.includes('jump')) continue;
    if ((track.boosts || []).some(b => Math.abs(b.s - s) < b.len / 2 + 14)) continue;
    if ((track.aerials || []).some(a => s > a.rampS0 - 35 && s < a.landingS1 + 25)) continue;
    if ((track.fields || []).some(a => s > a.fromS - 30 && s < a.toS + 30)) continue;
    const q = track.query(...f.p, f.seg);
    if (!q.onRoad || Math.abs(q.height) > .05) continue;
    return s;
  }
  return null;
}

function lensBridge(track) {
  // Cut the level lens elbow, leaving its longer outer road continuously available.
  const from = 260, to = 277;
  if (!Number.isFinite(track.nodeS[from]) || !Number.isFinite(track.nodeS[to])) return null;
  const a = track.frameAt(track.nodeS[from]), b = track.frameAt(track.nodeS[to]);
  const handle = Math.min(8, distance(a.p, b.p) * .05);
  const point = p => ({ p: [...p], w: 9, roll: 0, surface: 'glass', flags: ['open'] });
  const fork = { id: 'threshold-lens-piston-bridge', label: 'Lens bridge', kind: 'timed', from, to,
    points: [point(a.p), point(a.p.map((v, i) => v + a.t[i] * handle)),
      point(b.p.map((v, i) => v - b.t[i] * handle)), point(b.p)] };
  return { forkId: fork.id, fork, fromU: .3, toU: .65, periodS: 14, openS: 8, transitionS: .8 };
}

export function movingStations(track) {
  if (track.id !== 'threshold') return { traps: [], bridge: null };
  const traps = [];
  for (const [i, node] of ANCHORS.entries()) {
    if (!Number.isFinite(track.nodeS[node])) continue;
    const s = clearStation(track, track.nodeS[node]);
    if (s === null) continue;
    traps.push({ id: 'threshold-gallery-' + (i + 1), s, kind: i % 2 ? 'piston' : 'slider',
      side: i % 2 ? -1 : 1, w: 5, len: 4, periodBeats: 2, offsetBeats: i % 2 });
  }
  return { traps, bridge: lensBridge(track) };
}
