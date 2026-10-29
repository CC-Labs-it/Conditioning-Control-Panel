// Shallow joins and flush drainage dressing follow real support, never invented road.
import { Group, Mesh, BufferGeometry, Float32BufferAttribute, MeshStandardNodeMaterial, DoubleSide } from 'three/webgpu';
import { waterHalfWidth } from './waterMath.js';

const smooth = q => { q = Math.max(0, Math.min(1, q)); return q * q * (3 - 2 * q); };
const wrap = (s, length) => ((s % length) + length) % length;
const wet = (runs, s) => runs.some(r => s >= r.fromS && s <= r.toS);

export function waterJoinWeight(run, s, length = 0, runs = [run]) {
  const seam = length > 0 && runs[0]?.fromS < .001 && Math.abs(runs.at(-1)?.toS - length) < .001;
  const start = seam && run.fromS < .001 ? Infinity : s - run.fromS;
  const end = seam && Math.abs(run.toS - length) < .001 ? Infinity : run.toS - s;
  return smooth(Math.min(start, end) / Math.max(.000001, Math.min(6, (run.toS - run.fromS) / 2)));
}

/** A small lifted probe rejects cuts, holes, wrong road levels and curved-bank misses. */
export function transitionSupport(track, s, x = 0) {
  if (!track.loop && (s < 0 || s > track.lengthM)) return null;
  const at = track.loop ? wrap(s, track.lengthM) : s, f = track.surfaceAt ? track.surfaceAt(at, x) : track.frameAt(at);
  if (!track.surfaceAt) f.p = f.p.map((v, k) => v + f.b[k] * x);
  if (f.flags?.includes('jump') || f.n[1] < .45 || Math.abs(x) > f.w / 2 - .03) return null;
  if (track.fields?.some(r => at >= r.fromS && at <= r.toS)) return null;
  if (track.query) {
    const p = f.p.map((v, k) => v + f.n[k] * .06), q = track.query(...p, f.seg ?? -1);
    if (!q.onRoad || q.hole || Math.abs(q.height - .06) > .18 || Math.hypot(...q.p.map((v, k) => v - f.p[k])) > .35) return null;
  }
  return f;
}

export function waterTransitionEnds(track, runs) {
  const result = [];
  for (const run of runs) for (const dir of [-1, 1]) {
    const s = dir < 0 ? run.fromS : run.toS;
    if (wet(runs, wrap(s + dir * .5, track.lengthM))) continue;
    let dryM = 0;
    for (let d = .5; d <= 13; d += .5) {
      const at = s + dir * d, f = track.frameAt(at), section = track.sections?.[track.sectionAt?.(at)]?.id || '';
      if (wet(runs, wrap(at, track.lengthM)) || /submerged|emerge/.test(section)) break;
      if (![-.96, -.5, 0, .5, .96].every(k => transitionSupport(track, at, f.w * .5 * k))) break;
      dryM = d;
    }
    if (dryM >= 1) result.push({ run, s, dir, dryM });
  }
  return result;
}

/** The reservoir owns a horizontal waterline, so find real road-height crossings. */
export function reservoirTransitionEnds(track) {
  const from = track.sections?.find(s => s.id === 'submerged'), to = track.sections?.find(s => s.id === 'emerge') || from;
  const y = track.reservoir?.waterY, result = [];
  if (!from || !Number.isFinite(y)) return result;
  let previous = from.fromS, above = track.frameAt(previous).p[1] >= y;
  // Some authored reservoirs start below their waterline. Keep that membership
  // boundary: a wet lead-in does not silently extend underwater physics upstream.
  if (!above) {
    let dryM = 0;
    for (let d = .5; d <= 13; d += .5) {
      const s = from.fromS - d, f = track.frameAt(s);
      if (![-.96, -.5, 0, .5, .96].every(k => transitionSupport(track, s, f.w * .5 * k))) break;
      dryM = d;
    }
    if (dryM >= 1) result.push({ s: from.fromS, dir: -1, dryM, reservoir: true, authoredEntry: true });
  }
  for (let s = previous + 2; s <= to.toS; s += 2) {
    const nowAbove = track.frameAt(s).p[1] >= y;
    if (nowAbove !== above) {
      let lo = previous, hi = s;
      for (let i = 0; i < 18; i++) {
        const mid = (lo + hi) / 2;
        if ((track.frameAt(mid).p[1] >= y) === above) lo = mid; else hi = mid;
      }
      const boundary = (lo + hi) / 2, dir = nowAbove ? 1 : -1;
      let dryM = 0;
      for (let d = .5; d <= 13; d += .5) {
        const at = boundary + dir * d, f = track.frameAt(at);
        if (f.p[1] < y - .03 || ![-.96, -.5, 0, .5, .96].every(k => transitionSupport(track, at, f.w * .5 * k))) break;
        dryM = d;
      }
      if (dryM >= 1) result.push({ s: boundary, dir, dryM, reservoir: true });
    }
    above = nowAbove; previous = s;
  }
  return result;
}

/** Short spill outlets only where shallow water reaches an edge and gravity clears lower roads. */
export function transitionOutlets(track, ends, existing = []) {
  const result = [];
  for (const end of ends) {
    if (end.dir < 0 || end.run.toS - end.run.fromS < 24) continue;
    const middle = end.s - 7, side = result.length % 2 ? 1 : -1, f = track.frameAt(middle);
    if (waterHalfWidth(track, middle) < f.w / 2 - .1 || existing.some(r => middle >= r.fromS - 5 && middle <= r.toS + 5)) continue;
    let dropM = 10;
    for (const s of [middle - 2, middle, middle + 2]) {
      const edge = track.surfaceAt ? track.surfaceAt(s, (track.frameAt(s).w / 2 - .04) * side) : null;
      if (!edge) { dropM = 0; break; }
      for (let d = 1; d <= dropM + 1; d += .5) {
        const outward = side * (1.4 + Math.sin(Math.min(1, d / 10) * Math.PI * .6) * 2.6);
        const p = [edge.p[0] + edge.b[0] * outward, edge.p[1] - d, edge.p[2] + edge.b[2] * outward];
        const q = track.query?.(...p, edge.seg ?? -1);
        if (q?.onRoad && Math.abs(q.height) < 1.4) { dropM = Math.min(dropM, Math.max(0, d - 2)); break; }
      }
    }
    if (dropM >= 3) result.push({ fromS: middle - 2, toS: middle + 2, side, dropM });
  }
  return result;
}

export function buildWaterTransitions(track, runs) {
  const object = new Group(); object.name = 'water-shoreline-drains';
  const shallowEnds = waterTransitionEnds(track, runs), reservoirEnds = reservoirTransitionEnds(track);
  const ends = [...shallowEnds, ...reservoirEnds], metal = [], inset = [];
  const append = (list, s0, s1, x0, x1, lift) => {
    const count = Math.max(1, Math.ceil(Math.abs(s1 - s0) / .7));
    for (let i = 0; i < count; i++) {
      const a = s0 + (s1 - s0) * i / count, b = s0 + (s1 - s0) * (i + 1) / count;
      const contacts = [[a, x0], [a, x1], [b, x0], [b, x1]].map(([s, x]) => transitionSupport(track, s, x));
      if (contacts.some(f => !f)) continue;
      const p = contacts.map(f => f.p.map((v, k) => v + f.n[k] * lift));
      for (const j of [0, 2, 1, 1, 2, 3]) list.push(...p[j]);
    }
  };
  for (const end of ends) {
    if (end.dryM < 2) continue;
    const s = end.s + end.dir * 1.25, half = track.frameAt(s).w * .46;
    append(inset, s - .42, s + .42, -half, half, .012);
    for (const side of [-1, 1]) append(metal, s + side * .35, s + side * .44, -half, half, .036);
    const slots = Math.min(64, Math.max(6, Math.floor(half * 2 / .7)));
    for (let i = 0; i <= slots; i++) {
      const x = -half + 2 * half * i / slots;
      append(metal, s - .35, s + .35, x - .045, x + .045, .032);
    }
  }
  for (const [positions, name, colour, roughness, metalness] of [
    [inset, 'water-drain-recess', '#101d20', .72, .12], [metal, 'water-drain-grates', '#789594', .28, .7],
  ]) {
    if (!positions.length) continue;
    const geometry = new BufferGeometry(); geometry.setAttribute('position', new Float32BufferAttribute(positions, 3)); geometry.computeVertexNormals(); geometry.computeBoundingSphere();
    const material = new MeshStandardNodeMaterial({ color: colour, roughness, metalness, side: DoubleSide });
    const mesh = new Mesh(geometry, material); mesh.name = name; mesh.renderOrder = .2; object.add(mesh);
  }
  return { object, ends, shallowEnds, reservoirEnds, stats: { ends: ends.length, reservoirEnds: reservoirEnds.length, grateVertices: metal.length / 3, insetVertices: inset.length / 3 },
    dispose() { for (const mesh of object.children) { mesh.geometry.dispose(); mesh.material.dispose(); } object.removeFromParent(); object.clear(); } };
}
