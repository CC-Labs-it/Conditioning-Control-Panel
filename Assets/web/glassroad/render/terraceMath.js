/* Static starting terraces. Pure layout and solid-volume clearance, independent of the renderer. */
import { onLoop, sampleTrack } from './colossiMath.js';

export const TERRACE = Object.freeze({
  candidates: Object.freeze([38, 32, 44, 26, 50, 56, 62]),
  // A raised display deck keeps the cabinet fronts visible above the 1.2 m glass rail.
  lengthM: 16, depthM: 4.4, thicknessM: 0.85, topM: 0.7,
  edgeGapM: 0.7, cabinetOutM: 3.1, roadSideM: 0.5,
  roadAboveM: 4.5, roadBelowM: 1.2, sampleM: 2,
});
const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const sub = (a, b) => a.map((v, i) => v - b[i]);
const add = (a, b) => a.map((v, i) => v + b[i]);
const mul = (a, k) => a.map((v) => v * k);
const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const shifted = (f, across, up) => add(add(f.p, mul(f.b, across)), mul(f.n, up));

/** Separating-axis test of two solid oriented boxes: includes face interiors and contained volumes. */
export function terraceBoxesOverlap(a, b) {
  const d = sub(b.p, a.p), axes = [...a.axes, ...b.axes];
  for (const x of a.axes) for (const y of b.axes) axes.push(cross(x, y));
  for (const axis of axes) {
    if (dot(axis, axis) < 1e-12) continue;
    const radius = (box) => box.half.reduce((sum, h, i) => sum + h * Math.abs(dot(box.axes[i], axis)), 0);
    if (Math.abs(dot(d, axis)) > radius(a) + radius(b) + 1e-7) return false;
  }
  return true;
}

/** Conservative capsule/box test, including an axis passing through the middle of a large face. */
export function terraceHitsBody(box, value, gap = 0.3) {
  const body = value.body || value;
  if (!body.B || !body.top || !Number.isFinite(body.r)) return false;
  const start = sub(body.B, box.p), end = sub(body.top, box.p);
  let lo = 0, hi = 1;
  for (let i = 0; i < 3; i++) {
    const p = dot(start, box.axes[i]), delta = dot(sub(end, start), box.axes[i]);
    const half = box.half[i] + body.r + gap;
    if (Math.abs(delta) < 1e-9) { if (Math.abs(p) > half) return false; continue; }
    const a = (-half - p) / delta, b = (half - p) / delta;
    lo = Math.max(lo, Math.min(a, b)); hi = Math.min(hi, Math.max(a, b));
    if (lo > hi) return false;
  }
  return true;
}

export function terraceRoadBoxes(samples, L = TERRACE) {
  const along = (samples.stepM || L.sampleM) * 0.5 + 0.3;
  return samples.map((f) => ({
    p: shifted(f, 0, (L.roadAboveM - L.roadBelowM) / 2),
    axes: [f.t, f.n, f.b],
    half: [along, (L.roadAboveM + L.roadBelowM) / 2, f.w / 2 + L.roadSideM],
  }));
}

function stretchSafe(track, s, L) {
  const f = track.frameAt(s), half = L.lengthM / 2 + 2;
  if (s - half <= 8 || s + half >= track.lengthM - 8) return false;
  for (let q = s - half; q <= s + half; q += 1) {
    const x = track.frameAt(q), flags = x.flags || [];
    if (!flags.includes('rail') && !(flags.includes('railL') && flags.includes('railR'))) return false;
    if (flags.some((v) => ['magnet', 'jump', 'open', 'pit'].includes(v)) || onLoop(track, q, 6)) return false;
    if (x.n[1] < 0.97 || dot(f.t, x.t) < 0.996 || dot(f.n, x.n) < 0.999 || Math.abs(x.w - f.w) > 0.2) return false;
    if ((track.routes || []).some((r) => q >= r.openS0 - 2 && q <= r.openS1 + 2)) return false;
  }
  return true;
}

/** Two rows or none. `size` is the decoded model AABB size, not a guessed source mesh radius. */
export function planTerrace(track, size, { avoid = [], samples = null, L = TERRACE } = {}) {
  const diagnostics = [];
  if (!track || !size || size.some((v) => !Number.isFinite(v) || v <= 0) || size[0] > L.lengthM - 1 || size[2] > 2.5) {
    return { placements: [], diagnostics: ['unsupported-model-bounds'] };
  }
  // Refine a coarse shared sample set so a crossing deck cannot slip between samples.
  const roadSamples = samples && samples.stepM <= L.sampleM ? samples : sampleTrack(track, L.sampleM);
  const roads = terraceRoadBoxes(roadSamples, L);
  for (const s of L.candidates) {
    if (!stretchSafe(track, s, L)) { diagnostics.push({ s, reason: 'track-shape-or-opening' }); continue; }
    const f = track.frameAt(s), pair = [];
    for (const side of [-1, 1]) {
      const axes = [mul(f.t, side), [...f.n], mul(f.b, side)];
      const p = shifted(f, side * (f.w / 2 + L.cabinetOutM), L.topM);
      const body = { p: add(p, mul(f.n, size[1] / 2)), axes, half: size.map((v) => v / 2) };
      const platform = {
        p: shifted(f, side * (f.w / 2 + L.edgeGapM + L.depthM / 2), L.topM - L.thicknessM / 2),
        axes, half: [L.lengthM / 2, L.thicknessM / 2, L.depthM / 2],
      };
      const boxes = [body, platform];
      if (boxes.some((box) => roads.some((road) => terraceBoxesOverlap(box, road)))) {
        diagnostics.push({ s, side, reason: 'road-volume' }); break;
      }
      if (boxes.some((box) => avoid.some((item) => terraceHitsBody(box, item)))) {
        diagnostics.push({ s, side, reason: 'landmark-volume' }); break;
      }
      pair.push({ s, side, p, axes, body, platform });
    }
    if (pair.length === 2) return { placements: pair, diagnostics };
  }
  return { placements: [], diagnostics };
}
