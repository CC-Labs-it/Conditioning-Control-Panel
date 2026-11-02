/* track/driveline.js - the sampled ribbon (driveline quads) and the checklines. No three.js.
 *
 * Driveline: ROWS cross sections per segment, evenly spaced in s, plus one closing row at s = lengthM.
 * Row r: s, centre c, tangent t, normal n, right b, width w, segment seg. Quad r spans row r -> r + 1
 * with corners c -+ b w/2; quads on a gap segment carry gap = 1 (no road). The mesh is built from it.
 * Checklines: { idx, node, s, p, t, n, b, w }, one per checkpoints entry, in order.
 */
import { makeFrame } from './frames.js';

export const ROWS = 8;

export function buildDriveline(track, sp) {
  const N = sp.N, count = N * ROWS + 1;
  const s = new Float64Array(count), w = new Float32Array(count), seg = new Int32Array(count);
  const c = new Float64Array(count * 3), t = new Float32Array(count * 3), n = new Float32Array(count * 3), b = new Float32Array(count * 3);
  const gap = new Uint8Array(count - 1);
  const f = makeFrame();
  for (let r = 0; r < count; r++) {
    const i = Math.min(Math.floor(r / ROWS), N - 1), k = r - i * ROWS;
    const sr = sp.nodeS[i] + (sp.nodeS[i + 1] - sp.nodeS[i]) * k / ROWS;
    track.frameAt(r === count - 1 ? 0 : sr, f);
    s[r] = sr; w[r] = f.w; seg[r] = i;
    for (let a = 0; a < 3; a++) { c[r * 3 + a] = f.p[a]; t[r * 3 + a] = f.t[a]; n[r * 3 + a] = f.n[a]; b[r * 3 + a] = f.b[a]; }
    if (r < count - 1) gap[r] = sp.gap[i] ? 1 : 0;
  }
  return { rows: ROWS, count, quads: count - 1, s, c, t, n, b, w, seg, gap };
}

export function buildChecklines(track, checkpoints) {
  return checkpoints.map((node, idx) => {
    const f = track.frameAt(track.nodeS[node]);
    return { idx, node, s: track.nodeS[node], p: f.p, t: f.t, n: f.n, b: f.b, w: f.w };
  });
}
