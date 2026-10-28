/* Fit pictures around the machinery's complete motion envelope, including closed presses. */
import { industryZones } from '../sim/mainframeIndustry.js';
import { scanZones } from '../sim/mainframeScan.js';
import { boudoirSegmentBlocked } from './boudoirMedia.js';

const cache = new WeakMap();
const point = (f, x, y, z = 0) => f.p.map((v, i) => v + f.b[i] * x + f.n[i] * y + f.t[i] * z);
function obstacles(track) {
  if (cache.has(track)) return cache.get(track);
  const rows = [];
  const add = (f, x, y, size, scan = false) => rows.push({ p: point(f, x, y), size, radius: Math.hypot(...size) / 2, axes: [f.b, f.n, f.t], scan });
  for (const z of industryZones(track)) if (z.kind === 'piston') add(z, z.x, 7.3, [z.w + 1.2, 14.8, z.len + 1.2]);
  for (const z of scanZones(track)) {
    const f = track.frameAt(z.s + z.len / 2), height = z.height ?? 5.5;
    for (const side of [-1, 1]) add(f, side * (f.w / 2 + 1), height / 2 + 1, [1.5, height + 2.7, 2], true);
    add(f, 0, height + 1.6, [f.w + 3.7, 1.7, 2.2], true);
  }
  for (let s = 4; s < track.lengthM; s += 8) {
    const f = track.frameAt(s), id = track.sections[track.sectionAt(s)]?.id || '';
    if (!id.includes('canal') && !track.nodeFlags(f.seg).includes('jump')) add(f, 0, -1.9, [f.w + 13, 1.4, 8.5]);
  }
  const sec = track.sections.find(s => s.id === 'spiral-core');
  let core = null;
  if (sec) {
    const frames = [1 / 6, .5, 5 / 6].map(u => track.frameAt(sec.fromS + (sec.toS - sec.fromS) * u));
    const [[ax,,az], [bx,,bz], [cx,,cz]] = frames.map(f => f.p);
    const d = 2 * (ax * (bz - cz) + bx * (cz - az) + cx * (az - bz));
    if (Math.abs(d) > 1e-6) {
      const a = ax * ax + az * az, b = bx * bx + bz * bz, c = cx * cx + cz * cz;
      const x = (a * (bz - cz) + b * (cz - az) + c * (az - bz)) / d;
      const z = (a * (cx - bx) + b * (ax - cx) + c * (bx - ax)) / d;
      const all = Array.from({ length: 25 }, (_, i) => track.frameAt(sec.fromS + (sec.toS - sec.fromS) * i / 24));
      core = { x, z, r: Math.min(...all.map(f => Math.hypot(f.p[0] - x, f.p[2] - z) - f.w / 2 - 13)) + .5,
        low: Math.min(...all.map(f => f.p[1])) - 31, high: Math.max(...all.map(f => f.p[1])) + 31 };
    }
  }
  const result = { rows, core }; cache.set(track, result); return result;
}
function coreBlocks(a, b, core) {
  if (!core) return false;
  const dx = b[0] - a[0], dz = b[2] - a[2];
  const u = Math.max(0, Math.min(1, ((core.x - a[0]) * dx + (core.z - a[2]) * dz) / (dx * dx + dz * dz)));
  const y = a[1] + (b[1] - a[1]) * u;
  return y > core.low && y < core.high && Math.hypot(a[0] + dx * u - core.x, a[2] + dz * u - core.z) < core.r;
}
export function mainframePictureVisible(slot, track) {
  if (!track.courseShape) return true;
  const { rows, core } = obstacles(track);
  for (const lead of [30, 45, 60]) {
    const f = track.frameAt(slot.s - lead), from = point(f, 0, 3);
    let visible = 0;
    for (const u of [-.3, 0, .3]) for (const v of [-.25, 0, .25]) {
      const to = slot.C.map((x, i) => x + slot.X[i] * slot.w * u + slot.Y[i] * slot.h * v);
      if (coreBlocks(from, to, core)) return false;
      const low = to.map((x, k) => Math.min(x, from[k])), high = to.map((x, k) => Math.max(x, from[k]));
      const hits = rows.filter(r => r.p.every((x, k) => x + r.radius >= low[k] && x - r.radius <= high[k]) && boudoirSegmentBlocked(from, to, r));
      if (!hits.length) visible++;
      if (hits.some(r => !r.scan) || (u === 0 && v === 0 && hits.length)) return false;
    }
    if (visible < 6) return false;
  }
  return true;
}
