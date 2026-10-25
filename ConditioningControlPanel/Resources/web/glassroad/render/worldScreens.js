/* Additional world pictures use the existing shared media pool and Loom programs.
 * Placement is a loading-time operation. No extra decoder, canvas or moving geometry is created. */
import { axesFrom, roadSamples, placeBillboard, haloSlots, tunnelLayout, mediaLook, tierOf, tierSlots, V3 } from './mediaMath.js';
import { fitMainframeBillboard, motionEnvelope } from './mainframeMediaMotion.js';
import { boudoirWorldPlan, boudoirBoxesIntersect, boudoirSegmentBlocked } from './boudoirMedia.js';
import { getKeepLayout } from '../track/keep-layout.js';
import { prisonPlan } from './keepPrison.js';
import { spatialCandidates } from './spatialCandidates.js';
import { screenBodyBox } from './screenBodyMath.js';

const { add, sub, mul, dot, norm } = V3;
const WORLD = [[1, 0, 0], [0, 1, 0], [0, 0, 1]];
const local = (f, x, y, z = 0) => add(add(add(f.p, mul(f.b, x)), mul(f.n, y)), mul(f.t, z));
const box = screenBodyBox;
export const WORLD_SCREEN_TIERS = Object.freeze({
  high: Object.freeze({ max: 240, totalMax: 300, stations: 112, spacingM: 48 }),
  mid: Object.freeze({ max: 176, totalMax: 220, stations: 80, spacingM: 65 }),
  phone: Object.freeze({ max: 96, totalMax: 136, stations: 46, spacingM: 100 }),
});

/** Final authored planes without constructing render resources, for duplicate clearance. */
export function worldScreenExistingSlots(track, layout, { tier = 'high', samples = null } = {}) {
  if (!layout) return [];
  const M = mediaLook(), T = tierOf(tier, M), road = samples || roadSamples(track, 4);
  const fit = track.id === 'mainframe' ? fitMainframeBillboard : placeBillboard;
  const slots = [...(layout.slots || []), ...(layout.billboards || []).map(b => fit(b, track, road, M)).filter(Boolean)
    .map(b => b.flight ? motionEnvelope(b, M) : b)];
  if (layout.halo) {
    const frame = track.frameAt((layout.halo.f || 0) * track.lengthM);
    const halo = haloSlots(layout.halo, frame, Math.max(3, Math.round((layout.halo.count || 12) * T.haloShare)), 0, M);
    // Keep the whole orbit clear, including phases after the loading-time placement.
    halo[0].orbitEnvelope = { p: local(frame, 0, layout.halo.upM ?? 30), axis: [...frame.n],
      radius: (layout.halo.radiusM ?? 26) + Math.hypot(halo[0].w, halo[0].h) / 2 + M.borderM,
      halfHeight: Math.hypot(halo[0].w, halo[0].h) / 2 + M.borderM };
    slots.push(...halo);
  }
  if (layout.tunnel) slots.push(...tunnelLayout(layout.tunnel, track, M, T.tunnelStepMul).slots);
  return tierSlots(slots, tier, M);
}

function prepare(row) {
  const size = row.size;
  return { ...row, axes: row.axes || WORLD, radius: Math.hypot(...size) / 2 };
}
function near(row, p, radius) { return Math.hypot(...sub(row.p, p)) <= row.radius + radius; }
function keepBox(row) {
  const c = Math.cos(row.yaw || 0), s = Math.sin(row.yaw || 0), ct = Math.cos(row.tilt || 0), st = Math.sin(row.tilt || 0);
  return { ...row, axes: [[c * ct, st, -s * ct], [-c * st, ct, s * st], [s, 0, c]] };
}

/** Pure themed blockers also make offline coverage tests use the authored architecture. */
export function worldScreenBlockers(track) {
  if (track.id === 'boudoir') {
    const plan = boudoirWorldPlan(track);
    return [...plan.rows, ...plan.props.motionEnvelopes];
  }
  if (track.id === 'keep') {
    const plan = prisonPlan(track, getKeepLayout(track));
    return [...plan.boxes.map(keepBox), ...plan.drums.map(d => ({
      p: [d.x, (d.baseY + d.topY) / 2, d.z], size: [d.radiusM * 2, d.topY - d.baseY, d.radiusM * 2],
    }))];
  }
  return [];
}

function multiply(a, b) {
  const out = new Array(16);
  for (let col = 0; col < 4; col++) for (let row = 0; row < 4; row++)
    out[col * 4 + row] = a[row] * b[col * 4] + a[4 + row] * b[col * 4 + 1]
      + a[8 + row] * b[col * 4 + 2] + a[12 + row] * b[col * 4 + 3];
  return out;
}

/** Actual opaque dressing bounds, including instancing. Pass architecture groups, not the road/sky.
 * Conservative boxes deliberately reject ambiguous placements around curved and moving props. */
export function worldScreenSolids(roots = []) {
  const result = [];
  for (const root of Array.isArray(roots) ? roots : [roots]) {
    if (!root) continue;
    root.updateWorldMatrix?.(true, true);
    root.traverse(node => {
      if (!node.isMesh || !node.visible || !node.geometry) return;
      const materials = Array.isArray(node.material) ? node.material : [node.material];
      if (!materials.some(m => m && m.visible !== false && (!m.transparent || m.opacity >= .65))) return;
      const geo = node.geometry;
      if (!geo.boundingBox) geo.computeBoundingBox();
      const bounds = geo.boundingBox;
      if (!bounds || !Number.isFinite(bounds.max.x)) return;
      const center = ['x', 'y', 'z'].map(k => (bounds.min[k] + bounds.max[k]) / 2);
      const size = ['x', 'y', 'z'].map(k => bounds.max[k] - bounds.min[k]);
      const count = node.isInstancedMesh ? node.count : 1;
      for (let i = 0; i < count; i++) {
        const matrix = node.isInstancedMesh ? multiply(node.matrixWorld.elements, node.instanceMatrix.array.subarray(i * 16, i * 16 + 16)) : node.matrixWorld.elements;
        const axes = [0, 4, 8].map(j => [matrix[j], matrix[j + 1], matrix[j + 2]]);
        const scale = axes.map(a => Math.hypot(...a));
        if (scale.some(v => v < 1e-7)) continue;
        const p = [12, 13, 14].map((j, k) => matrix[j] + axes.reduce((sum, a, n) => sum + a[k] * center[n], 0));
        result.push({ name: node.name, p, axes: axes.map((a, n) => mul(a, 1 / scale[n])), size: size.map((v, n) => v * scale[n]) });
      }
    });
  }
  return result;
}

/** Sightlines must expose the whole centre and four inner picture corners on two approaches. */
export function worldScreenSightlines(track, slot, solids) {
  const points = [slot.C, ...[-.34, .34].flatMap(x => [-.34, .34].map(y => add(add(slot.C, mul(slot.X, slot.w * x)), mul(slot.Y, slot.h * y))))];
  let clear = 0;
  for (const lead of [22, 38, 58]) {
    const f = track.frameAt(slot.s - lead), eye = local(f, 0, 3);
    if (dot(sub(eye, slot.C), slot.Z) < 1) continue;
    if (points.every(p => !solids.some(row => boudoirSegmentBlocked(eye, p, row)))) clear++;
  }
  return clear;
}

/** Final media slots, capped per quality. Base layout stays authoritative; this supplements it.
 * `solids` are additional actual scene bounds. `existingSlots` excludes repeated picture planes.
 * Unknown course ids get the same safe road-frame layout for future course builders. */
export function worldScreenLayout(track, { tier = 'high', samples = null, solids = [], existingSlots = [] } = {}) {
  const T = WORLD_SCREEN_TIERS[tier] || WORLD_SCREEN_TIERS.high;
  const maxSlots = Math.max(0, Math.min(T.max, T.totalMax - existingSlots.length));
  const road = samples || roadSamples(track, 4), slots = [];
  const blockers = [...worldScreenBlockers(track), ...solids].map(prepare);
  const corridors = road.map(f => prepare({ p: local(f, 0, 3), size: [f.w + 6, 18, (road.stepM || 4) + 2], axes: [f.b, f.n, f.t] }));
  const sightBlockers = [...blockers, ...road.filter(f => !(track.nodeFlags?.(track.frameAt(f.s).seg) || []).includes('jump'))
    .map(f => prepare({ p: local(f, 0, -.5), size: [f.w, 1, (road.stepM || 4) + 1], axes: [f.b, f.n, f.t] }))];
  const count = Math.min(T.stations, Math.max(0, Math.floor(maxSlots / 2)), Math.max(20, Math.ceil(track.lengthM / T.spacingM)));
  const base = existingSlots.map(box).map(prepare), accepted = [];
  const corridorNear = spatialCandidates(corridors), blockerNear = spatialCandidates(blockers), sightNear = spatialCandidates(sightBlockers);
  const orbits = existingSlots.map(slot => slot.orbitEnvelope).filter(Boolean);
  let rejected = 0;
  const hazardous = s => {
    const f = track.frameAt(s), flags = track.nodeFlags?.(f.seg) || f.flags || [];
    if (flags.includes('jump')) return true;
    if ((track.jumps || []).some(j => s >= j.s - 24 && s <= j.landS + 24)) return true;
    if ((track.routes || []).some(r => s >= r.openS0 - 15 && s <= r.openS1 + 15)) return true;
    return false;
  };
  function fit(index, side, elevated = false, atS = null) {
    const s = atS ?? (index + .45) / count * track.lengthM;
    if (hazardous(s)) return;
    const f = track.frameAt(s), portrait = !elevated && index % 5 === 3;
    // Low panels sit inside Mainframe's solid aisle walls and retain readable picture area.
    const tight = track.id === 'mainframe';
    const variants = elevated ? [[16, 7.8, 17, 0], [12, 6, 15, 0]] : tight
      ? [[11, 6.2, 7.7, 3.7], [8, 4.5, 6.8, 3.4]]
      : portrait ? [[7.5, 12, 9.5, 7], [6, 9.5, 8, 4.5], [8, 5, 6.5, 3.8]]
        : [[17, 9.5, 8.5, 7], [13, 7.3, 7.5, 4.5], [8, 5, 6.5, 3.8]];
    for (const [w, h, y, gap] of variants) for (const yaw of elevated ? [0] : [tight ? 1.2 : .82, 1.35]) {
      const Z = elevated ? norm(add(mul(f.t, -1), mul(f.n, -.18))) : norm(add(mul(f.t, -Math.cos(yaw)), mul(f.b, -side * Math.sin(yaw))));
      const axes = axesFrom(Z, f.n);
      const offset = elevated ? side * (f.w * .24) : side * (f.w / 2 + gap + w / 2 * Math.abs(dot(axes.X, f.b)));
      const slot = { id: 'world-' + track.id + '-' + index + '-' + (elevated ? 'crown' : side < 0 ? 'left' : 'right'), frame: 'billboard',
        s, side, kind: portrait ? 'portrait' : 'landscape', w, h, C: local(f, offset, y), ...axes,
        phone: true, program: Math.floor(index / 2) % 2 ? 3 : 0, worldScreen: true, cluster: elevated ? 'crown' : portrait ? 'portrait' : 'ribbon' };
      const body = prepare(box(slot)), radius = body.radius;
      if (orbits.some(orbit => {
        const d = sub(body.p, orbit.p), height = dot(d, orbit.axis);
        return Math.abs(height) < orbit.halfHeight + radius && Math.hypot(...sub(d, mul(orbit.axis, height))) < orbit.radius + radius;
      }) || corridorNear(body.p, radius).some(row => boudoirBoxesIntersect(body, row))
        || blockerNear(body.p, radius).some(row => boudoirBoxesIntersect(body, row, .25))
        || [...base, ...accepted].some(row => near(row, body.p, radius) && boudoirBoxesIntersect(body, row, 1.2))) { rejected++; continue; }
      const nearby = sightNear(body.p, radius + 80);
      if (worldScreenSightlines(track, slot, nearby) < 2) { rejected++; continue; }
      slots.push(slot); accepted.push(body); return;
    }
  }
  // Keep pairs together and space the full lap before adding taller accents.
  for (let i = 0; i < count && slots.length < maxSlots - 1; i++) { fit(i, -1); fit(i, 1); }
  for (let i = 2; i < count && slots.length < maxSlots; i += tier === 'phone' ? 12 : 7) fit(i, i % 2 ? 1 : -1, true);
  // A short department can fall between the regular stations after a course grows.
  // Fill its remaining budget with the same solid and sightline checks as every other picture.
  if (track.id === 'mainframe') for (const [index, section] of track.sections.entries()) {
    if (slots.length >= maxSlots || slots.some(slot => slot.s >= section.fromS && slot.s < section.toS)) continue;
    for (const u of [.2,.5,.8]) {
      const before = slots.length, s = section.fromS + (section.toS-section.fromS)*u;
      for (const side of [-1,1]) if (slots.length < maxSlots) fit(count+index,side,false,s);
      if (slots.length > before) break;
    }
  }
  // The shared allocator alternates world faces. Even cohorts keep the added split exact.
  if (slots.length % 2) {
    const sectionOf = slot => track.sectionAt(slot.s);
    const duplicate = slots.findLastIndex(slot => slot.cluster !== 'crown' && slots.filter(other => sectionOf(other) === sectionOf(slot)).length > 1);
    slots.splice(Math.max(0, duplicate), 1);
  }
  return { slots, stats: { slots: slots.length, stations: count, max: T.max, rejected, tier, videoOwners: 0, extraUploads: 0 } };
}
