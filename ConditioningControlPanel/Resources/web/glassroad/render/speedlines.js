/* render/speedlines.js - thin streaks on a cylinder around the kart's forward axis, flowing back past
 * it. Density = fx.speedLines (boost) plus a ramp from speedPct; each line has a seed and shows when
 * its seed is under the density, so density changes the count, never flickers a line. One draw call.
 */
import { Mesh, BufferGeometry, BufferAttribute, MeshBasicNodeMaterial, AdditiveBlending, DoubleSide } from 'three/webgpu';
import { attribute, uniform, fract, cos, sin, vec3, step, float, PI } from 'three/tsl';
import { S } from '../sim/state.js';
import { LOOK } from './look.js';
import { Q } from './quality.js';

export function createSpeedLines() {
  const n = Q.speedLines, line = new Float32Array(n * 16), corner = new Float32Array(n * 8), idx = new Uint32Array(n * 6);
  const r = (a) => a[0] + Math.random() * (a[1] - a[0]);
  for (let i = 0; i < n; i++) {
    const L = [Math.random() * Math.PI * 2, r(LOOK.lineRadiusM), Math.random(), r(LOOK.lineLenM)];
    for (let k = 0; k < 4; k++) {
      line.set(L, (i * 4 + k) * 4);
      corner.set([k & 1 ? 1 : -1, k >> 1], (i * 4 + k) * 2);
    }
    idx.set([0, 1, 2, 2, 1, 3].map((v) => v + i * 4), i * 6);
  }
  const g = new BufferGeometry();
  g.setAttribute('position', new BufferAttribute(new Float32Array(n * 12), 3));
  g.setAttribute('aLine', new BufferAttribute(line, 4));
  g.setAttribute('aCorner', new BufferAttribute(corner, 2));
  g.setIndex(new BufferAttribute(idx, 1));

  const u = { phase: uniform(0), density: uniform(0), share: uniform(1) };
  const a = attribute('aLine', 'vec4'), c = attribute('aCorner', 'vec2');
  const t = fract(a.z.add(u.phase));
  const z = t.mul(LOOK.lineSpanM).sub(LOOK.lineSpanM * 0.85).sub(c.y.mul(a.w));
  const w = c.x.mul(LOOK.lineWidthM);
  const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending, side: DoubleSide });
  m.positionNode = vec3(cos(a.x).mul(a.y).sub(sin(a.x).mul(w)), sin(a.x).mul(a.y).add(cos(a.x).mul(w)), z);
  const show = step(fract(a.z.mul(7.31)), u.density.mul(u.share));
  m.colorNode = vec3(1, 1, 1);
  m.opacityNode = show.mul(sin(t.mul(PI))).mul(float(1).sub(c.y.mul(0.8))).mul(LOOK.lineAlpha);
  const mesh = new Mesh(g, m);
  mesh.frustumCulled = false; mesh.renderOrder = 3;

  /** Per frame: follow the kart (no squash), flow by speed. */
  function update(dt, kart, fx, obj) {
    mesh.position.copy(obj.position); mesh.quaternion.copy(obj.quaternion);
    const pct = kart[S.speedPct];
    const d = Math.max(fx.speedLines || 0, (pct - LOOK.lineFromPct) * LOOK.lineGain);
    u.density.value = Math.min(1, Math.max(0, d));
    u.share.value = Math.min(1, Q.speedLines / n);
    u.phase.value = (u.phase.value + LOOK.lineFlowMps * Math.max(0.3, pct) * dt / LOOK.lineSpanM) % 1;
    mesh.visible = u.density.value > 0;
  }
  return { object: mesh, update, dispose() { g.dispose(); m.dispose(); } };
}
