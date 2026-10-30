/* track/rampMesh.js - MeshData for the jump ramp wedges (track/ramps.js). No three.js; render/ramps.js uploads it.
 *
 * Every ramp of a course merges into three MeshData (one draw each, whatever the ramp count):
 * - body: the solid wedge: sloped top, both side walls down to the road, the short back face. A float `shade`
 *   per vertex (1 top, RAMP_SHADE.side walls, RAMP_SHADE.back) lets one palette material light it.
 * - chevrons: the top surface lifted DECAL_M, u = metres across from the ramp centre, v = metres from the foot,
 *   the boost-pad uv convention, so the pad shader's chevrons run up the wedge.
 * - lip: a LIP_M band on the top just before the lip, u = across, v from its start (the jump-lip stripe shader).
 * Heights come from rampHeight, the exact function the ground query stands karts on: what you see is what you ride.
 */
import { makeFrame } from './frames.js';
import { rampHeight } from './ramps.js';

const DECAL_M = 0.02, LIP_M = 0.6, STEP_M = 1, SINK_M = 0.05;
export const RAMP_SHADE = Object.freeze({ top: 1, side: 0.55, back: 0.38 });

function builder(withShade) {
  const P = [], Nn = [], U = [], I = [], SH = [];
  const v = (p, n, u, w, sh = 1) => { P.push(p[0], p[1], p[2]); Nn.push(n[0], n[1], n[2]); U.push(u, w); if (withShade) SH.push(sh); return P.length / 3 - 1; };
  /** a b on one row, c d on the next (a c on the same side); winds so the face points along `out`. */
  const quad = (a, b, c, d, out) => {
    const e1 = [0, 1, 2].map(k => P[b * 3 + k] - P[a * 3 + k]), e2 = [0, 1, 2].map(k => P[c * 3 + k] - P[a * 3 + k]);
    const cx = e1[1] * e2[2] - e1[2] * e2[1], cy = e1[2] * e2[0] - e1[0] * e2[2], cz = e1[0] * e2[1] - e1[1] * e2[0];
    if (cx * out[0] + cy * out[1] + cz * out[2] >= 0) I.push(a, b, c, b, d, c); else I.push(a, c, b, b, c, d);
  };
  const done = () => ({ positions: new Float32Array(P), normals: new Float32Array(Nn), uvs: new Float32Array(U), indices: new Uint32Array(I),
    ...(withShade ? { shade: new Float32Array(SH) } : {}) });
  return { v, quad, done, get size() { return P.length / 3; } };
}

const at = (base, n, h) => [base[0] + n[0] * h, base[1] + n[1] * h, base[2] + n[2] * h];

/** @returns {{ body, chevrons, lip }} MeshData (empty arrays when the course has no ramps) */
export function buildRampMeshes(track) {
  const body = builder(true), chevrons = builder(false), lip = builder(false);
  const L = track.lengthM, f = makeFrame();
  const surf = (s, x) => (track.surfaceAt ? track.surfaceAt(s, x, f) : (track.frameAt(s, f), f));
  for (const r of track.ramps || []) {
    const half = r.widthM / 2, xs = [r.x - half, r.x + half];
    // rows along the wedge (foot -> lip) and down the back face; d = metres before the lip (negative behind it)
    const rows = [];
    const n = Math.max(2, Math.ceil(r.lenM / STEP_M));
    for (let k = 0; k <= n; k++) rows.push(r.lenM * (1 - k / n));
    rows.push(-r.backM);
    const lipRow = n;                                    // rows[lipRow] === 0
    const pts = rows.map((d) => xs.map((x) => {
      const s = r.lipS - d, g = surf(s, x), p = g.p.slice(), nn = g.n.slice(), t = g.t.slice(), b = g.b.slice();
      // the edge columns sit exactly on the width bound, inside rampHeight's inclusive footprint
      const h = d === 0 ? r.heightM : rampHeight(r, s, x, L);
      return { s, d, p, n: nn, t, b, top: at(p, nn, h), foot: at(p, nn, -SINK_M) };
    }));
    // top (and its chevron decal), foot to lip, then the back face lip to road
    for (let k = 0; k < rows.length - 1; k++) {
      const [a, b] = pts[k], [c, d] = pts[k + 1], up = a.n;
      const back = k >= lipRow, sh = back ? RAMP_SHADE.back : RAMP_SHADE.top;
      const v0 = r.lenM - a.d, v1 = r.lenM - c.d;
      const ia = body.v(a.top, up, -half, v0, sh), ib = body.v(b.top, up, half, v0, sh), ic = body.v(c.top, up, -half, v1, sh), id = body.v(d.top, up, half, v1, sh);
      // the back face points back down the road (against t) and up
      body.quad(ia, ib, ic, id, back ? [up[0] - a.t[0], up[1] - a.t[1], up[2] - a.t[2]] : up);
      if (!back) {
        const ca = chevrons.v(at(a.top, up, DECAL_M), up, -half, v0), cb = chevrons.v(at(b.top, up, DECAL_M), up, half, v0);
        const cc = chevrons.v(at(c.top, up, DECAL_M), up, -half, v1), cd = chevrons.v(at(d.top, up, DECAL_M), up, half, v1);
        chevrons.quad(ca, cb, cc, cd, up);
      }
      // the side walls, from the top edge down to just under the road
      for (const [e, g, sign] of [[a, c, -1], [b, d, 1]]) {
        const out = [e.b[0] * sign, e.b[1] * sign, e.b[2] * sign];
        const i0 = body.v(e.top, out, v0, 1, RAMP_SHADE.side), i1 = body.v(e.foot, out, v0, 0, RAMP_SHADE.side);
        const i2 = body.v(g.top, out, v1, 1, RAMP_SHADE.side), i3 = body.v(g.foot, out, v1, 0, RAMP_SHADE.side);
        body.quad(i0, i1, i2, i3, out);
      }
    }
    // the lit lip: a band over the last LIP_M before the lip, lifted a little over the chevrons
    const lipRows = [LIP_M, LIP_M / 2, 0].map((d) => xs.map((x) => {
      const s = r.lipS - d, g = surf(s, x), h = d === 0 ? r.heightM : rampHeight(r, s, x, L);
      return { p: at(g.p, g.n, h + DECAL_M * 2), n: g.n.slice(), v: LIP_M - d };
    }));
    for (let k = 0; k < lipRows.length - 1; k++) {
      const [a, b] = lipRows[k], [c, d] = lipRows[k + 1];
      lip.quad(lip.v(a.p, a.n, -half, a.v), lip.v(b.p, b.n, half, b.v), lip.v(c.p, c.n, -half, c.v), lip.v(d.p, d.n, half, d.v), a.n);
    }
  }
  return { body: body.done(), chevrons: chevrons.done(), lip: lip.done() };
}
