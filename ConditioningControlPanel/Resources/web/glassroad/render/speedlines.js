/* render/speedlines.js - streaks on a cylinder around the kart's forward axis, flowing back past
 * it. Density = fx.speedLines (boost) plus a ramp from speedPct; each line has a seed and shows when
 * its seed is under the density, so density changes the count, never flickers a line. One draw call.
 * Polish 3 (atlas.js JUICE_LOOK): half again as many lines, longer, wide soft-edged streaks closer in,
 * each tinted from the spectrum (render/spectrum.js through juiceHues) over a white core; a boost makes
 * them longer, wider and brighter. Gentle (when scene.js hands feel) halves their alpha.
 */
import { Mesh, BufferGeometry, BufferAttribute, MeshBasicNodeMaterial, AdditiveBlending, DoubleSide, Color } from 'three/webgpu';
import { attribute, uniform, fract, cos, sin, vec3, step, float, abs, mix, PI } from 'three/tsl';
import { S } from '../sim/state.js';
import { LOOK } from './look.js';
import { Q } from './quality.js';
import { juiceLook, juiceHues } from './atlas.js';

export function createSpeedLines(feel = null) {
  const J = juiceLook(LOOK), HUES = juiceHues();
  const mul = J.lineCountMul, n = Math.max(1, Math.round(Q.speedLines * mul));
  const line = new Float32Array(n * 16), corner = new Float32Array(n * 8), tint = new Float32Array(n * 12), idx = new Uint32Array(n * 6);
  const r = (a, k = 1) => (a[0] + Math.random() * (a[1] - a[0])) * k;
  const cc = new Color();
  for (let i = 0; i < n; i++) {
    const L = [Math.random() * Math.PI * 2, r(J.lineRadiusM), Math.random(), r(LOOK.lineLenM, J.lineLenMul)];
    cc.set(HUES[(i * 7 + ((Math.random() * 3) | 0)) % HUES.length]);
    for (let k = 0; k < 4; k++) {
      line.set(L, (i * 4 + k) * 4);
      corner.set([k & 1 ? 1 : -1, k >> 1], (i * 4 + k) * 2);
      tint.set([cc.r, cc.g, cc.b], (i * 4 + k) * 3);
    }
    idx.set([0, 1, 2, 2, 1, 3].map((v) => v + i * 4), i * 6);
  }
  const g = new BufferGeometry();
  g.setAttribute('position', new BufferAttribute(new Float32Array(n * 12), 3));
  g.setAttribute('aLine', new BufferAttribute(line, 4));
  g.setAttribute('aCorner', new BufferAttribute(corner, 2));
  g.setAttribute('aTint', new BufferAttribute(tint, 3));
  g.setIndex(new BufferAttribute(idx, 1));

  const u = { phase: uniform(0), density: uniform(0), share: uniform(1), boost: uniform(0), alpha: uniform(J.lineAlpha) };
  const a = attribute('aLine', 'vec4'), c = attribute('aCorner', 'vec2'), tn = attribute('aTint', 'vec3');
  const t = fract(a.z.add(u.phase));
  const len = a.w.mul(u.boost.mul(J.lineBoostLen).add(1));
  const z = t.mul(LOOK.lineSpanM).sub(LOOK.lineSpanM * 0.85).sub(c.y.mul(len));
  const w = c.x.mul(J.lineWidthM).mul(u.boost.mul(J.lineBoostWidth).add(1));
  const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending, side: DoubleSide });
  m.positionNode = vec3(cos(a.x).mul(a.y).sub(sin(a.x).mul(w)), sin(a.x).mul(a.y).add(cos(a.x).mul(w)), z);
  const show = step(fract(a.z.mul(7.31)), u.density.mul(u.share));
  // a white-hot core across the width, the line's spectrum colour toward its soft edges
  const edge = abs(c.x);
  m.colorNode = mix(vec3(1, 1, 1), tn, edge.mul(0.5).add(0.5).mul(J.lineTint)).mul(u.boost.mul(J.lineBoostGain).add(1));
  m.opacityNode = show.mul(sin(t.mul(PI))).mul(float(1).sub(c.y.mul(0.8))).mul(edge.oneMinus()).mul(u.alpha);
  const mesh = new Mesh(g, m);
  mesh.frustumCulled = false; mesh.renderOrder = 3;

  /** Per frame: follow the kart (no squash), flow by speed. */
  function update(dt, kart, fx, obj) {
    mesh.position.copy(obj.position); mesh.quaternion.copy(obj.quaternion);
    const pct = kart[S.speedPct], boost = Math.min(1, Math.max(0, fx.speedLines || 0));
    const d = Math.max(boost, (pct - J.lineFromPct) * LOOK.lineGain);
    u.density.value = Math.min(1, Math.max(0, d));
    u.share.value = Math.min(1, Q.speedLines * mul / n);
    u.boost.value = boost;
    u.alpha.value = J.lineAlpha * (feel && feel.get('gentle.on') ? J.lineGentle : 1);
    u.phase.value = (u.phase.value + LOOK.lineFlowMps * Math.max(0.3, pct) * dt / LOOK.lineSpanM) % 1;
    mesh.visible = u.density.value > 0;
  }
  return { object: mesh, update, dispose() { g.dispose(); m.dispose(); } };
}
