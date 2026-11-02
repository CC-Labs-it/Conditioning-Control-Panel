/* render/helixMath.js - the DNA helix geometry (render/helix.js draws it). Pure: no three.js, so the tests load it. */

export const HELIX_LOOK = Object.freeze({
  stepM: 1, pitchM: 26, liftM: 2.2, outM: 2.6, strandW: 0.5, stubM: 0.42, stubEveryM: 3.25, stubW: 0.14,
  inRadiusMul: 2.4, nearFadeM: [3, 11], aheadM: 170, behindM: 40, gain: 1.1, kickGain: 0.9,
});

const TAU = Math.PI * 2;

/**
 * The strand and stub vertices. Per vertex: aC centre (lifted over the road), aB / aN the frame's side and
 * up, aT its tangent, aS the lap metre, aTh the base angle, aW the across offset (metres), aR the radial
 * share (1 = on the strand), aKind 0 strand / 1 stub. Pure; exported for the tests.
 */
export function helixGeometryData(track, L = HELIX_LOOK) {
  const lap = track.lengthM, n = Math.max(8, Math.floor(lap / L.stepM));
  const f = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] };
  const A = { aC: [], aB: [], aN: [], aT: [], aS: [], aTh: [], aW: [], aR: [], aKind: [] }, idx = [];
  let v = 0;
  const push = (fr, s, th, w, r, kind) => {
    A.aC.push(fr.p[0] + fr.n[0] * L.liftM, fr.p[1] + fr.n[1] * L.liftM, fr.p[2] + fr.n[2] * L.liftM);
    A.aB.push(...fr.b); A.aN.push(...fr.n); A.aT.push(...fr.t);
    A.aS.push(s); A.aTh.push(th); A.aW.push(w); A.aR.push(r); A.aKind.push(kind);
    return v++;
  };
  const radius = [];
  for (const strand of [0, 1]) {
    const base = v;
    for (let i = 0; i <= n; i++) {
      const s = (i % n) * lap / n, fr = track.frameAt(s, f);
      // outside the edge where the strand crosses the road plane: r^2 - lift^2 >= (w/2 + out)^2
      const r = Math.hypot(fr.w / 2 + L.outM, L.liftM);
      if (strand === 0) radius.push(r);
      const th = s / L.pitchM * TAU + strand * Math.PI;
      push(fr, s, th, -L.strandW / 2, r, 0); push(fr, s, th, L.strandW / 2, r, 0);
      if (i > 0) { const a = base + (i - 1) * 2; idx.push(a, a + 1, a + 2, a + 1, a + 3, a + 2); }
    }
  }
  // base-pair stubs: from each strand a short way in toward the axis (they never meet across the road)
  const stubs = Math.floor(lap / L.stubEveryM);
  for (let k = 0; k < stubs; k++) {
    const s = (k + 0.5) * lap / stubs, fr = track.frameAt(s, f), r = Math.hypot(fr.w / 2 + L.outM, L.liftM);
    for (const strand of [0, 1]) {
      const th = s / L.pitchM * TAU + strand * Math.PI;
      const a = push(fr, s, th, -L.stubW, r, 1), b = push(fr, s, th, L.stubW, r, 1);
      const c = push(fr, s, th, -L.stubW, r * (1 - L.stubM), 1), d = push(fr, s, th, L.stubW, r * (1 - L.stubM), 1);
      idx.push(a, b, c, b, d, c);
    }
  }
  return { attrs: A, index: idx, count: v, radius };
}
