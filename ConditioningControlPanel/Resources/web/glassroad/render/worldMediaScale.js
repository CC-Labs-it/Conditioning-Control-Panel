/* Larger pictures share the same media owners. Fit once, before warm-up. */
import { axesFrom, roadSamples, placeBillboard, haloSlots, tunnelLayout, mediaLook, tierOf, V3 } from './mediaMath.js';
import { fitMainframeBillboard, motionEnvelope } from './mainframeMediaMotion.js';
import { worldScreenBlockers, worldScreenSightlines } from './worldScreens.js';
import { boudoirBoxesIntersect } from './boudoirMedia.js';
import { screenBodyBox } from './screenBodyMath.js';
import { spatialCandidates } from './spatialCandidates.js';

export const WORLD_MEDIA_SCALE = 1.85;
const { add, sub, mul, dot, norm } = V3;
const body = screenBodyBox;
const radius = row => row.radius ?? Math.hypot(...row.size) / 2;
const prepared = row => ({ ...row, radius: radius(row) });
const overlaps = (a, b) => Math.hypot(...sub(a.p, b.p)) <= radius(a) + radius(b) && boudoirBoxesIntersect(a, b, .35);

/** Main and branch roads both reserve their whole driving corridor. */
export function mediaRoadCorridors(track, samples = null) {
  const road = samples || roadSamples(track, 4);
  return [...road, ...(track.forks || []).flatMap(f => f.samples(4))].map(f => ({
    p: add(f.p, mul(f.n, 3)), size: [f.w + 6, 18, (road.stepM || 4) + 2], axes: [f.b, f.n, f.t],
  }));
}

export function largeMediaLayout(track, layout, { samples = null, solids = [], tier = 'high' } = {}) {
  if (!layout || layout.largePictures) return layout;
  const M = mediaLook(), T = tierOf(tier, M), scale = WORLD_MEDIA_SCALE;
  const road = samples || roadSamples(track, 4), corridors = mediaRoadCorridors(track, road);
  const architecture = [...worldScreenBlockers(track), ...solids].map(prepared);
  const blockers = [...corridors.map(prepared), ...architecture], accepted = [];
  const blockerNear = spatialCandidates(blockers), architectureNear = spatialCandidates(architecture);
  const clear = (slot, nearby) => {
    const b = body(slot.flight ? motionEnvelope(slot, M) : slot);
    return !nearby.some(row => overlaps(b, row)) && !accepted.some(row => overlaps(b, row));
  };
  function fit(source, grow = true, relocated = false) {
    const f = track.frameAt(source.s || 0), side = source.side || Math.sign(dot(sub(source.C, f.p), f.b)) || 1;
    const slot = { ...source, w: source.w * (grow ? scale : 1), h: source.h * (grow ? scale : 1) };
    const base = add(source.C, mul(source.Y, (slot.h - source.h) / 2));
    const reach = 160 + Math.hypot(slot.w, slot.h) / 2 + Math.hypot(...sub(base, f.p));
    const nearby = blockerNear(base, reach).filter(row => Math.hypot(...sub(row.p, base)) < radius(row) + reach);
    const viewBlockers = architectureNear(base, reach).filter(row => Math.hypot(...sub(row.p, base)) < radius(row) + reach);
    // Preserve the readable bottom edge first; move only when the larger face meets architecture.
    for (const up of [0, 6, 12, 24]) for (const out of [0, 6, 16]) for (const front of [0, 4, 12, 24]) {
      slot.C = add(add(add(base, mul(f.n, up)), mul(f.b, side * out)), mul(slot.Z, front));
      if (slot.flight) slot.home = [...slot.C];
      if (!clear(slot, nearby)) continue;
      if (worldScreenSightlines(track, slot, viewBlockers) < 1) continue;
      if (slot.stalk) {
        const top = sub(slot.C, mul(slot.Y, slot.h / 2));
        slot.stalk = { top, bottom: add(source.stalk.bottom, sub(top, source.stalk.top)) };
      }
      accepted.push(body(slot.flight ? motionEnvelope(slot, M) : slot));
      return slot;
    }
    // A cabinet wedged between buttresses cannot hold a wider picture. Put its
    // face on the adjacent frontage, with the same identity and media owner.
    if (!relocated && source.frame !== 'tunnel') for (const shift of [0, -20, 20, -40, 40, -70, 70, -100, 100]) {
      const s = (source.s + shift + track.lengthM) % track.lengthM, frame = track.frameAt(s);
      const axes = axesFrom(norm(add(mul(frame.t, -.45), mul(frame.b, -side * .9))), frame.n);
      const C = add(add(frame.p, mul(frame.b, side * (frame.w / 2 + 4 + slot.w / 2 * Math.abs(dot(axes.X, frame.b))))), mul(frame.n, 3 + source.h / 2));
      const moved = fit({ ...source, ...axes, s, C, stalk: null }, grow, true);
      if (moved) return moved;
    }
    return null;
  }
  const fitter = track.id === 'mainframe' ? fitMainframeBillboard : placeBillboard;
  const authored = [...(layout.slots || []), ...(layout.billboards || []).map(b => fitter(b, track, road, M)).filter(Boolean)];
  const slots = authored.map(s => fit(s)).filter(Boolean);
  let halo = layout.halo ? { ...layout.halo, heightM: (layout.halo.heightM || 6) * scale,
    radiusM: (layout.halo.radiusM ?? 26) * scale } : null;
  if (halo) {
    const frame = track.frameAt((halo.f || 0) * track.lengthM);
    const count = Math.max(3, Math.round((halo.count || 12) * T.haloShare));
    // Closed cabinets need room at the inner corners of the tilted ring.
    let ringClear = false;
    for (let attempt = 0; attempt < 32; attempt++, halo.radiusM += 1) {
      const ring = haloSlots(halo, frame, count, 0, M).map(body);
      if (ring.every((panel, i) => !ring.slice(0, i).some(other => overlaps(panel, other)))) { ringClear = true; break; }
    }
    let found = false;
    // Reserve the full moving ring, not only its initial phase.
    for (let lift = 0; ringClear && lift <= 120; lift += 8) {
      halo.upM = (layout.halo.upM ?? 30) + lift;
      const envelope = { p: add(frame.p, mul(frame.n, halo.upM)), size: [2 * halo.radiusM + halo.heightM * 2.2,
        halo.heightM * 2.2, 2 * halo.radiusM + halo.heightM * 2.2], axes: [frame.b, frame.n, frame.t] };
      if ([...blockers, ...accepted].some(row => overlaps(envelope, row))) continue;
      accepted.push(envelope); found = true; break;
    }
    if (!found) halo = null;
  }
  const tunnel = layout.tunnel ? { ...layout.tunnel, stepM: (layout.tunnel.stepM ?? 9) * scale,
    padM: (layout.tunnel.padM ?? 3.5) + 8, heightM: (layout.tunnel.heightM ?? 11) * scale,
    wallHeightM: (layout.tunnel.wallHeightM ?? 3.6) * scale, ceilHeightM: (layout.tunnel.ceilHeightM ?? 4.2) * scale } : null;
  const tunnelSlots = tunnel ? tunnelLayout(tunnel, track, M, T.tunnelStepMul).slots.map(s => fit(s, false)).filter(Boolean) : null;
  return { ...layout, largePictures: true, slots, billboards: [], halo, tunnel, tunnelSlots };
}
