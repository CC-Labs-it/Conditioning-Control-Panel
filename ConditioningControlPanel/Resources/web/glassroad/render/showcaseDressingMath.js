/* Static track construction in the track's own transported frame. No world-axis assumptions,
 * no extra barriers, and no geometry across a jump. Pure so stacked courses can test the layout. */
export const SHOWCASE_KIT = Object.freeze({ stepM: 4, sleeperEvery: 6, maxSegments: 640,
  // Chord faces bow inward relative to a loop: allow the tight local curvature at spline joins,
  // not just its nominal radius. This clearance keeps the face centre behind the curved road.
  fasciaW: 0.42, fasciaH: 0.52, fasciaDown: 0.55, trimW: 0.46, trimH: 0.07,
  trimDown: 0.38, sleeperDown: 0.77, sleeperH: 0.24, sleeperDepth: 0.7 });

const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const len = (a) => Math.hypot(...a);
const sub = (a, b) => a.map((v, i) => v - b[i]);
const norm = (a) => { const n = len(a); return n > 1e-8 ? a.map((v) => v / n) : null; };
const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const shifted = (f, lateral, height) => f.p.map((v, i) => v + f.b[i] * lateral + f.n[i] * height);
const gap = (f) => !f || !(f.w > 0) || (f.flags || []).includes('jump');

/** Three material batches, made once. All additions sit below the driving surface. */
export function showcaseLayout(track, options = {}) {
  const K = { ...SHOWCASE_KIT, ...options }, rows = { fascia: [], trim: [], sleeper: [] };
  const L = track && track.lengthM;
  if (!(L > 0) || typeof track.frameAt !== 'function') return rows;
  const count = Math.min(K.maxSegments, Math.max(1, Math.ceil(L / K.stepM))), step = L / count;
  for (let i = 0; i < count; i++) {
    const s0 = i * step, s1 = (i + 1) * step, s = (s0 + s1) * 0.5;
    // Keep separate samples: some frameAt implementations reuse their output object.
    const copy = (f) => ({ ...f, p: [...f.p], n: [...f.n], b: [...f.b], t: [...f.t] });
    const a = copy(track.frameAt(s0)), f = copy(track.frameAt(s)), b = copy(track.frameAt(s1 % L));
    if (gap(a) || gap(f) || gap(b) || gap(track.frameAt(s0 + step * 0.25)) || gap(track.frameAt(s0 + step * 0.75))) continue;
    if (dot(a.n, b.n) < 0.82) continue; // no long chord across a tight frame transition
    for (const side of [-1, 1]) {
      const edge = (v) => shifted(v, side * (v.w * 0.5 - K.fasciaW * 0.5), -K.fasciaDown);
      const pa = edge(a), pb = edge(b), forward = norm(sub(pb, pa)), distance = len(sub(pb, pa));
      if (!forward || distance > step * 1.6) continue;
      const right = norm(cross(forward, f.n));
      if (!right) continue;
      const up = norm(cross(right, forward));
      const p = pa.map((v, j) => (v + pb[j]) * 0.5);
      rows.fascia.push({ s, p, b: right, n: up, t: forward, size: [K.fasciaW, K.fasciaH, distance + 0.045] });
      rows.trim.push({ s, p: p.map((v, j) => v + up[j] * (K.fasciaDown - K.trimDown)),
        b: right, n: up, t: forward, size: [K.trimW, K.trimH, distance + 0.045] });
    }
    if (i % K.sleeperEvery === 0) rows.sleeper.push({ s, p: shifted(f, 0, -K.sleeperDown),
      b: [...f.b], n: [...f.n], t: [...f.t], size: [f.w + 0.12, K.sleeperH, K.sleeperDepth] });
  }
  return rows;
}
