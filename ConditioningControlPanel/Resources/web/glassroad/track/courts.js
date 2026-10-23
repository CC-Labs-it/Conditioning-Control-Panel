/* Court apertures in track coordinates. s is the start of an opening, not its centre. */

export function courtAt(courts, s) {
  return (courts || []).find((c) => s >= c.fromS && s < c.toS) || null;
}

/** Signed distance in the local (s, lateral) chart; negative inside an opening. */
export function holeDistance(h, s, x) {
  const ds = Math.abs(s - h.s - h.len / 2) - h.len / 2;
  const dx = Math.abs(x - h.x) - h.w / 2;
  return Math.hypot(Math.max(ds, 0), Math.max(dx, 0)) + Math.min(Math.max(ds, dx), 0);
}

/** Positive margin conservatively grows the rectangular opening on all four sides. */
export function holeAt(courts, s, x, margin = 0) {
  for (const c of courts || []) for (const h of c.holes) {
    if (s >= h.s - margin && s <= h.s + h.len + margin && Math.abs(x - h.x) <= h.w / 2 + margin) return h;
  }
  return null;
}

/** Supported lateral intervals. Margin grows holes and reserves clearance from both outer edges. */
export function supportIntervals(courts, s, halfWidth, margin = 0) {
  const lo = -halfWidth + margin, hi = halfWidth - margin;
  if (lo >= hi) return [];
  const cuts = [];
  for (const c of courts || []) for (const h of c.holes) {
    if (s >= h.s - margin && s <= h.s + h.len + margin) cuts.push([h.x - h.w / 2 - margin, h.x + h.w / 2 + margin]);
  }
  cuts.sort((a, b) => a[0] - b[0]);
  const out = [];
  let x = lo;
  for (const [a, b] of cuts) {
    if (b <= x || a >= hi) continue;
    if (a > x) out.push([x, Math.min(a, hi)]);
    x = Math.max(x, b);
  }
  if (x < hi) out.push([x, hi]);
  return out;
}

/** Normalize once, after the spline exists. Reject openings outside their court or through an outer edge. */
export function buildCourts(rows, nodeS, frameAt) {
  return (rows || []).map((c) => {
    const out = { ...c, fromS: nodeS[c.from], toS: nodeS[c.to], holes: c.holes.map((h) => ({ ...h, ds: h.ds || 0, s: nodeS[h.node] + (h.ds || 0) })) };
    for (const h of out.holes) {
      if (h.s < out.fromS || h.s + h.len > out.toS) throw new Error('court ' + c.id + ' hole ' + h.id + ' extends outside its court');
      for (let k = 0, n = Math.max(1, Math.ceil(h.len)); k <= n; k++) {
        const f = frameAt(h.s + h.len * k / n);
        if (Math.abs(h.x) + h.w / 2 >= f.w / 2 || f.flags.includes('jump') || f.flags.includes('magnet'))
          throw new Error('court ' + c.id + ' hole ' + h.id + ' needs an interior supported road footprint');
      }
    }
    return out;
  });
}
