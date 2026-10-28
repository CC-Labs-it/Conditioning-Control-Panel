// Broader plan turns retain the original node identities used by the course's toys and flights.
import { createSpline } from './spline.js';
import { shapeAerialNodes } from './aerial.js';
const RAD = Math.PI / 180, TAU = Math.PI * 2;
// [node count, straight length] or [node count, turn degrees, radius]. Vertical loops are separate.
const PLAN = [
  [8,120], [6,90,120], [2,40], [10,180,120], [29,324], [15,160],
  [13,90,120], [41,450], [13,90,120], [27,300], [63,-360,120], [36,400],
  [13,90,120], [32,350], [13,90,120], [45,500], [9,100], [16,280], [4,40],
  [10,0,16,-20], [5,50], [9,0,14,20], [3,120],
  [6,180,140], [8,180,148.75], [11,180,157.5], [3,90], [2,24], [4,40],
  [23,null], [6,90,120], [16,null], [3,30], [1,12], [2,60],
  [3,-90,120], [3,-90,120], [5,120],
];
const smooth = u => u * u * (3 - 2 * u);
const lengthOf = p => p.length === 4 ? TAU * p[2] : p.length === 3 ? Math.abs(p[1]) * RAD * p[2] : p[1];
function walk(plan) {
  let x = 0, z = 0, h = 0;
  const starts = [];
  for (const p of plan) {
    starts.push({ x, z, h });
    if (p.length === 4) { x -= Math.sin(h) * p[3]; z += Math.cos(h) * p[3]; }
    else if (p.length === 3) {
      const sign = Math.sign(p[1]), next = h + p[1] * RAD;
      x += sign * p[2] * (Math.sin(next) - Math.sin(h));
      z += sign * p[2] * (Math.cos(h) - Math.cos(next)); h = next;
    } else { x += Math.cos(h) * (p[1] || 0); z += Math.sin(h) * (p[1] || 0); }
  }
  return { x, z, starts };
}

function closedPlan() {
  const plan = PLAN.map(p => p.slice()), base = walk(plan);
  const a = base.starts[29].h, b = base.starts[31].h;
  const det = Math.cos(a) * Math.sin(b) - Math.sin(a) * Math.cos(b);
  plan[29][1] = (-base.x * Math.sin(b) + base.z * Math.cos(b)) / det;
  plan[31][1] = (-Math.cos(a) * base.z + Math.sin(a) * base.x) / det;
  return plan;
}
const COURSE = closedPlan(), STARTS = walk(COURSE).starts;
const pitch = i => { const p = COURSE[(i + COURSE.length) % COURSE.length]; return lengthOf(p) / p[0]; };
const slope = n => Math.max(.5, Math.min(2.5, n));
const hermite = (u,a,b) => (u*u*u-2*u*u+u)*a + (-2*u*u*u+3*u*u) + (u*u*u-u*u)*b;

/** Mutates only the cloned road nodes. No index, height, gap, loop or shortcut anchor is removed. */
export function shapeThresholdNodes(nodes, json) {
  if (nodes.length !== 518) throw new Error('Threshold shape requires its 518 authored node anchors');
  let node = 0;
  COURSE.forEach((p, piece) => {
    const st = STARTS[piece], loop = p.length === 4, arc = p.length === 3;
    for (let k = 0; k < p[0]; k++, node++) {
      let u = k / p[0], x, z;
      // Match knot spacing at arc joins without moving the middle of the stepping field or courts.
      if (!loop && !arc && ![27,31,32,33,34].includes(piece)) {
        const own = p[1] / p[0], a = slope(pitch(piece - 1) / own), b = slope(pitch(piece + 1) / own);
        if (u < .25) u = .25 * hermite(u / .25, a, 1);
        else if (u > .75) u = .75 + .25 * hermite((u - .75) / .25, 1, b);
      }
      if (loop) {
        const f = p[2] * Math.sin(TAU * u), side = p[3] * smooth(u);
        x = st.x + Math.cos(st.h) * f - Math.sin(st.h) * side;
        z = st.z + Math.sin(st.h) * f + Math.cos(st.h) * side;
      } else if (arc) {
        const sign = Math.sign(p[1]), h = st.h + p[1] * RAD * u;
        x = st.x + sign * p[2] * (Math.sin(h) - Math.sin(st.h));
        z = st.z + sign * p[2] * (Math.cos(st.h) - Math.cos(h));
      } else { x = st.x + Math.cos(st.h) * p[1] * u; z = st.z + Math.sin(st.h) * p[1] * u; }
      nodes[node].p[0] = x; nodes[node].p[2] = z;
      // The old column corkscrew is now a gentle rising straight.
      if (piece === 17) nodes[node].roll = 0;
    }
  });
  // Broadening the coil must not turn its two-node kicker into a 146m open rail.
  let source, mapS;
  if (json) {
    const reference=json.nodes.map(n=>({...n,p:n.p.slice(),flags:n.flags.slice()}));
    shapeAerialNodes(json.id,reference);
    source=createSpline(reference.map(n=>n.p),reference.map(n=>n.flags.includes('jump')));
    mapS=(s,track)=>{
      const at=(s%source.lengthM+source.lengthM)%source.lengthM,seg=source.segAt(at);
      const u=(at-source.nodeS[seg])/(source.nodeS[seg+1]-source.nodeS[seg]);
      return track.nodeS[seg]+u*(track.nodeS[seg+1]-track.nodeS[seg]);
    };
  }
  return { routes: json?.routes?.map(r => r.id === 'dare' ? { ...r, open: { ds: 0, len: 24 } } : r),
    sourceLengthM:source?.lengthM,mapS };
}
