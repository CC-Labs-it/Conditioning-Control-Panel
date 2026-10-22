/* render/skids.js - glowing skid marks on drifts (polish 4, PARTICLES lane).
 *
 * While a kart's skid flag is on (atlas.js skidOn: the sim's per-tick skid field when it has one, else a
 * grounded drift), both rear contact points lay ribbon segments on the road every P4_LOOK.skidStepM. All
 * karts share one pooled ring buffer of quads (atlas.js createSkidBuffer, the oldest segment is reused), so
 * it is ONE draw call and a flat cost. Each vertex keeps its birth time: a mark rises in skidRiseS, holds
 * skidHoldS, then eases out by skidLifeS (3.6 s); its fresh end burns white-hot for skidHotS. Hue from the
 * drift tier: blue, orange, pink (atlas.js tierColour); a slide with no tier yet leaves a pale, dimmer mark.
 * The clock only moves through update(dt, paused), so a paused race freezes every mark. Gentle halves the
 * alpha. Small area, no luminance step: the fade is smooth.
 *
 * API: createSkids(feel, count) -> { object, kart(i, obj, st, visible), update(dt, paused), clear(), dispose() }
 */
import { Mesh, BufferGeometry, BufferAttribute, DynamicDrawUsage, MeshBasicNodeMaterial, AdditiveBlending, DoubleSide, Color, Vector3, Quaternion } from 'three/webgpu';
import { attribute, uniform, smoothstep, abs, clamp, vec3, mix } from 'three/tsl';
import { S } from '../sim/state.js';
import { LOOK } from './look.js';
import { Q } from './quality.js';
import { p4Look, createSkidBuffer, stepWheel, skidOn, tierColour } from './atlas.js';

export function createSkids(feel, count = 8) {
  const P = p4Look(LOOK), cap = P.skidCap[Q.name] || P.skidCap.high, buf = createSkidBuffer(cap);
  const pos = new BufferAttribute(buf.pos, 3).setUsage(DynamicDrawUsage);
  const meta = new BufferAttribute(buf.meta, 2).setUsage(DynamicDrawUsage);
  const col = new BufferAttribute(buf.col, 3).setUsage(DynamicDrawUsage);
  const g = new BufferGeometry();
  g.setAttribute('position', pos); g.setAttribute('aMeta', meta); g.setAttribute('aCol', col);
  g.setIndex(new BufferAttribute(buf.index, 1));

  const u = { time: uniform(0), alpha: uniform(P.skidAlpha) };
  const mt = attribute('aMeta', 'vec2'), age = u.time.sub(mt.x);
  const rise = clamp(age.div(P.skidRiseS), 0, 1);
  const out = clamp(age.sub(P.skidHoldS).div(P.skidLifeS - P.skidHoldS), 0, 1).oneMinus();
  const hot = smoothstep(0, P.skidHotS, age).oneMinus();
  // normal blending with an over-bright colour: a solid stripe that reads on the bright glass road, and the
  // bloom pass turns the over-bright core into the glow (additive alone washed out on the rainbow lanes)
  const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, side: DoubleSide,
    polygonOffset: true, polygonOffsetFactor: -2, polygonOffsetUnits: -4 });
  const edge = abs(mt.y);
  m.colorNode = mix(attribute('aCol', 'vec3'), vec3(1, 1, 1), hot.mul(0.6).max(smoothstep(0, 0.5, edge).oneMinus().mul(0.35))).mul(P.skidGlow).mul(hot.mul(0.6).add(1));
  m.opacityNode = rise.mul(out.pow(1.5)).mul(smoothstep(0.55, 1, edge).oneMinus()).mul(u.alpha);
  const mesh = new Mesh(g, m);
  mesh.frustumCulled = false; mesh.renderOrder = 1; mesh.castShadow = false; mesh.receiveShadow = false;

  const wheels = Array.from({ length: count }, () => [{}, {}]);
  const tmp = new Vector3(), right = new Vector3(), quat = new Quaternion(), cc = new Color(), p = [0, 0, 0], r = [0, 0, 0], rgb = [1, 1, 1];
  let time = 0, dirty = false;

  /** Per kart i and frame, after its pose (obj.matrixWorld current); st = its interpolated state. */
  function kart(i, obj, st, visible = true) {
    const w = wheels[i];
    if (!w) return;
    const on = visible && skidOn(st, S), tier = st[S.driftStage] | 0;
    if (on) {
      cc.set(tierColour(tier, P));
      const k = tier >= 1 ? 1 : P.skidSlideAlpha;
      rgb[0] = cc.r * k; rgb[1] = cc.g * k; rgb[2] = cc.b * k;
      // the sim pose, not the drawn one: the kart leans and hops visually, the marks stay on the road
      quat.set(st[S.quatX], st[S.quatY], st[S.quatZ], st[S.quatW]);
      right.set(1, 0, 0).applyQuaternion(quat);
      r[0] = right.x; r[1] = right.y; r[2] = right.z;
    }
    for (let s = 0; s < 2; s++) {
      if (on) {
        tmp.set((s ? 1 : -1) * P.skidWheelX, P.skidLiftM, P.skidWheelZ).applyQuaternion(quat);
        p[0] = st[S.posX] + tmp.x; p[1] = st[S.posY] + tmp.y; p[2] = st[S.posZ] + tmp.z;
      }
      const seg = stepWheel(w[s], p, r, on, time, P);
      if (seg) { buf.lay(seg.a, p, seg.ra, r, seg.ta, time, P.skidWidthM / 2, rgb); dirty = true; }
    }
  }

  /** Once per frame after every kart(); paused (or dt 0) freezes the marks where they are. */
  function update(dt, paused = false) {
    if (!paused && dt > 0) time += dt;
    u.time.value = time;
    u.alpha.value = P.skidAlpha * (feel && feel.get && feel.get('gentle.on') ? P.skidGentle : 1);
    if (dirty) { pos.needsUpdate = true; meta.needsUpdate = true; col.needsUpdate = true; dirty = false; }
  }

  /** Drop every mark (restart, track change). */
  function clear() { buf.clear(); for (const w of wheels) { w[0].has = false; w[1].has = false; } meta.needsUpdate = true; }

  return { object: mesh, kart, update, clear, get laid() { return buf.laid; }, _dev: { buf, touch() { dirty = true; }, time: () => time }, dispose() { g.dispose(); m.dispose(); } };
}
