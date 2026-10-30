/* render/slipRibbon.js - the slipstream ribbon (interaction wave, 2026-10-30): thin translucent streaks from the
 * kart ahead to the drafting kart, drawn from the sim's view row (fxv.slip, fxv.slipFrom; sim/contact.js).
 * Render only. Envelope, pop and dash shading live in render/slipMath.js. Every slot shares one geometry and
 * one additive material: one draw call. Phones: one strand with fewer points.
 */
import { Mesh, BufferGeometry, BufferAttribute, DynamicDrawUsage, MeshBasicNodeMaterial, AdditiveBlending, DoubleSide, Color, Vector3 } from 'three/webgpu';
import { attribute } from 'three/tsl';
import { Q } from './quality.js';
import { LOOK } from './look.js';
import { SLIP_RIBBON, newRibbon, stepRibbon, ribbonLook, dashAt } from './slipMath.js';

export function createSlipRibbons(count = 8, { L = SLIP_RIBBON } = {}) {
  const phone = Q.name === 'phone', N = phone ? L.phonePoints : L.points, K = phone ? L.phoneStrands : L.strands;
  const per = K * N * 2, V = count * per;
  const pos = new BufferAttribute(new Float32Array(V * 3), 3).setUsage(DynamicDrawUsage);
  const col = new BufferAttribute(new Float32Array(V * 4), 4).setUsage(DynamicDrawUsage);
  const idx = [];
  for (let s = 0; s < count * K; s++) for (let i = 0; i < N - 1; i++) {
    const v = (s * N + i) * 2;
    idx.push(v, v + 1, v + 2, v + 2, v + 1, v + 3);
  }
  const g = new BufferGeometry();
  g.setAttribute('position', pos); g.setAttribute('aCol', col); g.setIndex(idx);
  const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending, side: DoubleSide });
  const c = attribute('aCol', 'vec4');
  m.colorNode = c.rgb; m.opacityNode = c.a;
  const mesh = new Mesh(g, m);
  mesh.frustumCulled = false; mesh.renderOrder = 1;

  const rib = Array.from({ length: count }, newRibbon);
  const base = new Color(L.colour), white = new Color(L.popColour), cc = new Color();
  const A = new Vector3(), B = new Vector3(), d = new Vector3(), up = new Vector3(), side = new Vector3();
  const fired = new Set();
  let t = 0;

  function clearSlot(k) { col.array.fill(0, k * per * 4, (k + 1) * per * 4); }

  /** A slipstream fire for racer i (bus 'slipstream' state 'fire'). */
  function fire(i) { fired.add(i); }

  /**
   * Per frame. racers[i].fxv = { slip, slipFrom }; objs[i] = the kart root (matrixWorld current, null = hidden);
   * near(i) = drawn at full detail. paused freezes the envelope and the dashes.
   */
  function update(dt, racers, objs, { gentle = false, paused = false, near = () => true } = {}) {
    if (!paused) t += dt;
    const step = paused ? 0 : dt;
    for (let k = 0; k < count; k++) {
      const r = racers[k], fxv = r && r.fxv, o = objs[k];
      const from = fxv && o && o.visible && near(k) && fxv.slipFrom >= 0 ? fxv.slipFrom : -1;
      const ribbon = stepRibbon(rib[k], { slip: fxv ? fxv.slip || 0 : 0, from, fired: fired.has(k) }, step, { gentle, L });
      const lead = ribbon.from >= 0 ? objs[ribbon.from] : null;
      const look = ribbonLook(ribbon, L);
      if (!o || !lead || !o.visible || !lead.visible || look.alpha <= 0.002) { clearSlot(k); continue; }
      // the kart ahead's tail to the drafter's nose (karts face -z), lifted a touch off the road
      A.set(0, L.upM, LOOK.saucerR * 0.9).applyMatrix4(lead.matrixWorld);
      B.set(0, L.upM, -LOOK.saucerR * 0.9).applyMatrix4(o.matrixWorld);
      d.subVectors(B, A);
      const len = d.length();
      if (len < 0.2 || len > L.maxGapM) { clearSlot(k); continue; }
      up.set(0, 1, 0).applyQuaternion(o.quaternion);
      side.crossVectors(d, up).normalize();
      cc.copy(base).lerp(white, Math.min(1, look.white));
      const P = pos.array, C = col.array, hw = look.width / 2;
      for (let s = 0; s < K; s++) {
        const off = K > 1 ? (s / (K - 1) - 0.5) * 2 * L.spreadM : 0;
        for (let i = 0; i < N; i++) {
          const u = i / (N - 1), a = look.alpha * dashAt(u, len, t + s * 0.13, { gentle, L });
          // strands pinch toward the drafter: the wake narrows into its nose
          const o2 = off * (1 - 0.6 * u);
          for (let e = 0; e < 2; e++) {
            const v = ((k * K + s) * N + i) * 2 + e, sg = e ? hw : -hw;
            P[v * 3] = A.x + d.x * u + side.x * (o2 + sg);
            P[v * 3 + 1] = A.y + d.y * u + side.y * (o2 + sg);
            P[v * 3 + 2] = A.z + d.z * u + side.z * (o2 + sg);
            C[v * 4] = cc.r; C[v * 4 + 1] = cc.g; C[v * 4 + 2] = cc.b; C[v * 4 + 3] = a;
          }
        }
      }
    }
    fired.clear();
    pos.needsUpdate = true; col.needsUpdate = true;
  }

  function reset() { for (let k = 0; k < count; k++) { rib[k] = newRibbon(); clearSlot(k); } fired.clear(); col.needsUpdate = true; }

  return { object: mesh, fire, update, reset, dispose() { g.dispose(); m.dispose(); } };
}
