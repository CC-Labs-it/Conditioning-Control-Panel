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

/* Stepping-stone fields (stones wave): inside a field the deck is gone and only the stones carry a kart.
 * A stone is anchored like a hole ({ node, ds, x, w, len } -> s = nodeS[node] + ds); the void between stones is
 * the field's one shared hole object { id: '<field id>:void', field, lip: true } (track/query.js hands it out). */

/** The field containing s, or null. */
export function fieldAt(fields, s) {
  if (!fields) return null;
  for (let i = 0; i < fields.length; i++) { const f = fields[i]; if (s >= f.fromS && s < f.toS) return f; }
  return null;
}

/** The stone under (s, x) in field f, or null. A positive margin shrinks every stone on all four sides. */
export function stoneIn(f, s, x, margin = 0) {
  const st = f.stones;
  for (let i = 0; i < st.length; i++) {
    const h = st[i];
    if (s >= h.s + margin && s <= h.s + h.len - margin && Math.abs(x - h.x) <= h.w / 2 - margin) return h;
  }
  return null;
}

/** The stone under (s, x) across every field, or null (also null outside every field). */
export function stoneAt(fields, s, x, margin = 0) {
  const f = fieldAt(fields, s);
  return f ? stoneIn(f, s, x, margin) : null;
}

/** Distance from (s, x) to the nearest stone edge of field f (0 on a stone). */
export function stoneDistance(f, s, x) {
  let best = Infinity;
  for (let i = 0; i < f.stones.length; i++) best = Math.min(best, Math.max(0, holeDistance(f.stones[i], s, x)));
  return best;
}

/**
 * Supported lateral intervals at s inside a field: the stones' [x - w/2, x + w/2] shrunk by margin, clipped to the
 * road, sorted and merged. Outside every field it returns null: the road as usual (see supportIntervals).
 */
export function fieldSupport(fields, s, halfWidth, margin = 0) {
  const f = fieldAt(fields, s);
  if (!f) return null;
  const lo = -halfWidth + margin, hi = halfWidth - margin, cuts = [];
  for (const h of f.stones) {
    if (s < h.s + margin || s > h.s + h.len - margin) continue;
    const a = Math.max(lo, h.x - h.w / 2 + margin), b = Math.min(hi, h.x + h.w / 2 - margin);
    if (b > a) cuts.push([a, b]);
  }
  cuts.sort((a, b) => a[0] - b[0]);
  const out = [];
  for (const c of cuts) {
    const last = out[out.length - 1];
    if (last && c[0] <= last[1]) last[1] = Math.max(last[1], c[1]);
    else out.push([c[0], c[1]]);
  }
  return out;
}

/** Normalize fields once, after the spline exists. Reject a stone outside its field or through an outer edge. */
export function buildFields(rows, nodeS, frameAt) {
  return (rows || []).map((c) => {
    const out = { ...c, fromS: nodeS[c.from], toS: nodeS[c.to], lanes: (c.lanes || []).slice(),
      stones: c.stones.map((h) => ({ ...h, ds: h.ds || 0, s: nodeS[h.node] + (h.ds || 0) })) };
    // the shared void object; not enumerable so the field still serialises (it points back at the field)
    Object.defineProperty(out, 'void', { value: { id: c.id + ':void', field: out, lip: true }, enumerable: false });
    for (const h of out.stones) {
      if (h.s < out.fromS - 1e-6 || h.s + h.len > out.toS + 1e-6) throw new Error('field ' + c.id + ' stone ' + h.id + ' extends outside its field');
      for (let k = 0, n = Math.max(1, Math.ceil(h.len)); k <= n; k++) {
        const f = frameAt(h.s + h.len * k / n);
        if (Math.abs(h.x) + h.w / 2 > f.w / 2 || f.flags.includes('jump') || f.flags.includes('magnet'))
          throw new Error('field ' + c.id + ' stone ' + h.id + ' needs an interior road footprint');
      }
    }
    return out;
  });
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
