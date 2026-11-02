/* render/edges.js - the open-edge language and the rail end posts in one draw (EDGES lane, render/edgesMath.js
 * holds the numbers and the photosafe rules). track/mesh.js builds the geometry three-free: edgeStrip (the hazard
 * strip and lip on every open edge), edgeHaze (the curtain falling away below that lip) and railCaps (a tall
 * post where a rail run stops). The rails themselves (taller, top line, posts) are render/materials/rails.js.
 *
 * createEdges(scene, track, pal, { pulse }) -> { object, update(dt, { gentle, paused }), dispose } | null
 * pulse: the music road's rail pulse uniform (render/musicRoad.js), so the edges breathe with the rails.
 */
import { Mesh, BufferGeometry, BufferAttribute, MeshBasicNodeMaterial, DoubleSide, Color } from 'three/webgpu';
import { uv, uniform, attribute, float, vec3, mix, clamp, smoothstep, fract, abs, fwidth, min, max, pow, sin, select,
  positionWorld, cameraPosition } from 'three/tsl';
import { LOOK } from './look.js';
import { EDGE_LOOK, EDGE_KIND, mergeEdgeData } from './edgesMath.js';
import { fitPeriod } from './spectrum.js';
import { EDGE_MESH } from '../track/mesh.js';

export function createEdgeMaterial(pal, { pulse = null } = {}) {
  const E = { ...EDGE_LOOK, ...(LOOK.edges || {}) };
  const u = { t: uniform(0), contrast: uniform(1) };
  const m = new MeshBasicNodeMaterial({ transparent: true, side: DoubleSide, depthWrite: false });
  const p = uv(), across = p.x, s = p.y, kind = attribute('kind', 'float');
  const lap = pal.flowWrap > 0 && pal.flowWrap < 1e5 ? pal.flowWrap : 0;
  const rampM = lap ? fitPeriod(lap, E.rampM) : E.rampM;
  const ramp = pal.ramp ? pal.ramp(s.div(rampM)) : mix(pal.col(0), pal.col(1), sin(s.div(rampM).mul(Math.PI * 2)).mul(0.5).add(0.5));
  const lift = pulse ? float(1).add(pulse.mul(E.pulseGain)) : float(1);
  const hot = mix(ramp, vec3(1, 0.98, 1), E.hot).mul(lift);
  const dc = new Color(E.dark), dark = vec3(dc.r, dc.g, dc.b);
  const near = smoothstep(E.nearM[0], E.nearM[1], positionWorld.distance(cameraPosition));

  // the strip: a steady lip on the very edge, diagonal hazard stripes inside it (contrast cut near the eye,
  // melted to their average where they get denser than the pixels), a soft inner border
  const x = s.add(across.mul(E.stripeSlant)).div(E.stripePeriodM);
  const tri = abs(fract(x).sub(0.5)).mul(2);                    // 0 mid-stripe A .. 1 mid-stripe B
  const w = min(fwidth(x), float(1)).mul(1.2).add(0.02);
  const raw = smoothstep(float(1 - E.stripeDuty).sub(w), float(1 - E.stripeDuty).add(w), tri);
  const dense = smoothstep(E.denseFrom[0], E.denseFrom[1], min(fwidth(x), float(1)));
  const stripe = mix(float(0.5), mix(raw, float(0.5), dense), mix(float(E.nearContrast), float(1), near).mul(u.contrast));
  const lip = float(1).sub(smoothstep(E.lipM * 0.7, E.lipM, across));
  const inner = float(1).sub(smoothstep(EDGE_MESH.stripM - E.innerSoftM, EDGE_MESH.stripM, across));
  const stripCol = mix(mix(dark, hot, stripe), hot, lip);
  const stripA = max(mix(float(E.stripeAlpha[0]), float(E.stripeAlpha[1]), stripe).mul(inner), lip);

  // the haze: bright at the lip, falling away, slow streaks drifting down into the drop
  const fall = pow(clamp(float(1).sub(across.div(EDGE_MESH.hazeM)), 0, 1), E.hazePow);
  const streak = pow(abs(sin(s.div(E.hazeStreakM).mul(Math.PI))), 6).mul(sin(across.mul(0.9).sub(u.t.mul(E.hazeDriftMps * 0.9))).mul(0.5).add(0.5));
  const top = float(1).sub(smoothstep(E.hazeTopM * 0.6, E.hazeTopM, across));
  const hazeCol = hot.mul(float(0.75).add(streak.mul(E.hazeStreakGain)).add(top.mul(0.3)));
  const hazeA = max(fall.mul(E.hazeAlpha).mul(float(1).add(streak.mul(E.hazeStreakGain))), top.mul(0.95));

  // the end post: a solid hot post with a dark band and a white top
  const band = float(1).sub(smoothstep(E.capBandW * 0.6, E.capBandW, abs(across.sub(E.capBandAt))));
  const capTop = smoothstep(E.capTop - 0.02, E.capTop + 0.02, across);
  const capCol = mix(mix(hot, dark, band.mul(0.8)), vec3(1, 0.98, 1), capTop);

  const isStrip = kind.lessThan(EDGE_KIND.haze - 0.5), isHaze = kind.lessThan(EDGE_KIND.cap - 0.5);
  m.colorNode = select(isStrip, stripCol, select(isHaze, hazeCol, capCol));
  m.opacityNode = clamp(select(isStrip, stripA, select(isHaze, hazeA, float(0.96))), 0, 1);
  return { material: m, uniforms: u };
}

export function createEdges(scene, track, pal, { pulse = null } = {}) {
  const d = track.meshes ? mergeEdgeData(track.meshes) : null;
  if (!d) return null;
  const g = new BufferGeometry();
  g.setAttribute('position', new BufferAttribute(d.positions, 3));
  g.setAttribute('normal', new BufferAttribute(d.normals, 3));
  g.setAttribute('uv', new BufferAttribute(d.uvs, 2));
  g.setAttribute('kind', new BufferAttribute(d.kinds, 1));
  g.setIndex(new BufferAttribute(d.indices, 1));
  g.computeBoundingSphere();
  const { material, uniforms } = createEdgeMaterial(pal, { pulse });
  const object = new Mesh(g, material);
  object.name = 'edges';
  object.renderOrder = 1;                       // over the road, under the route marks (renderOrder 2)
  scene.add(object);
  const E = { ...EDGE_LOOK, ...(LOOK.edges || {}) };
  let t = 0;
  function update(dt, { gentle = false, paused = false } = {}) {
    uniforms.contrast.value = gentle ? E.gentleContrast : 1;
    if (paused) return;
    t = (t + dt * (gentle ? E.gentleDrift : 1)) % 1e4;
    uniforms.t.value = t;
  }
  function dispose() { scene.remove(object); g.dispose(); material.dispose(); }
  return { object, update, dispose, counts: d.counts };
}
