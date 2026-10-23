/* Continuous supported lines through a court. Choose once on approach, never weave between holes. */
import { clamp, smoothstep, wrap } from '../core/math.js';
import { holeAt, supportIntervals } from '../track/courts.js';

const LEAD = 50, CLEAR = 3, MARGIN = 1.25;

export function createCourtPaths(track) {
  const plans = (track.courts || []).filter(c => c.holes.length).map(court => {
    const first = Math.min(...court.holes.map(h => h.s));
    const last = Math.max(...court.holes.map(h => h.s + h.len));
    let bands = [[-Infinity, Infinity]];
    // Intersect support at every hole edge and middle, retaining the same corridor throughout.
    const samples = court.holes.flatMap(h => [h.s, h.s + h.len / 2, h.s + h.len]);
    for (const s of samples) {
      const available = supportIntervals([court], s, track.frameAt(s).w / 2, MARGIN);
      bands = bands.flatMap(a => available.map(b => [Math.max(a[0], b[0]), Math.min(a[1], b[1])]))
        .filter(b => b[1] - b[0] >= 0.2);
    }
    return { court, first, last, bands: bands.map(([lo, hi]) => ({ lo, hi,
      x: lo <= 0 && hi >= 0 ? 0 : clamp(0, lo + Math.min(1.5, (hi - lo) / 2), hi - Math.min(1.5, (hi - lo) / 2)) })) };
  });
  const at = s => plans.find(p => wrap(s - (p.court.fromS - LEAD), track.lengthM) <= p.court.toS - p.court.fromS + LEAD + 12) || null;
  return {
    at,
    choose(plan, seed, racer, lap, lateral = null) {
      if (!plan.bands.length) return null;
      let band;
      if (lateral !== null) band = plan.bands.reduce((best, b) => Math.abs(b.x - lateral) < Math.abs(best.x - lateral) ? b : best);
      else {
        let hash = 0;
        for (const ch of plan.court.id) hash = (Math.imul(hash, 33) + ch.charCodeAt(0)) >>> 0;
        hash = (hash + seed + Math.imul(racer + 1, 31) + Math.imul(lap, 17)) >>> 0;
        band = plan.bands[hash % plan.bands.length];
      }
      // reached: the line was picked from where the kart actually is (a late commit), so the entry blend is already over
      return { plan, band, reached: lateral !== null };
    },
    target(route, s, ahead, desired = 0) {
      const { plan, band } = route;
      if (band.x === 0 && s < plan.court.fromS) return desired;
      const enter = route.reached ? 1 : smoothstep(plan.court.fromS - LEAD, plan.first - 6, s + ahead);
      // Do not aim across a hole just because the pursuit point already lies beyond its end.
      const leave = smoothstep(plan.last + CLEAR, plan.court.toS - 4, s);
      const lane = band.x === 0 ? clamp(desired, band.lo + 0.1, band.hi - 0.1)
        : clamp(band.x + clamp(desired, -0.35, 0.35), band.lo, band.hi);
      return lane * enter * (1 - leave) + desired * leave;
    },
  };
}

/** Back up only when the centerline anchor or its immediate rollout crosses an aperture. */
export function courtRescueNode(track, index) {
  if (!track.courts?.length) return index;
  for (let tries = 0; tries < track.nodeCount; tries++) {
    const s = track.nodeS[index];
    let supported = true;
    for (let d = 0; d <= 12; d += 1) {
      if (holeAt(track.courts, wrap(s + d, track.lengthM), 0, MARGIN)) { supported = false; break; }
    }
    if (supported) return index;
    index = (index - 1 + track.nodeCount) % track.nodeCount;
  }
  return index;
}
