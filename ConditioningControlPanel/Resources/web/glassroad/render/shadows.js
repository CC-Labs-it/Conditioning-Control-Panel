/* render/shadows.js - the lamp's soft shadows (BRIEF-POLISH2 contract).
 *
 * 'map'  : the directional lamp (top left, LOOK.lightDir) renders a PCF shadow map (r186 WebGPU and
 *          its WebGL2 fallback both run PCFShadowMap; PCFSoft is gone in r186, radius softens the
 *          edge instead). Casters: the colossi, the karts and (high tier) the big scatter. The road
 *          and pads receive through shadow catchers: a twin of each receiver mesh with a
 *          ShadowNodeMaterial drawn just over it, so the unlit glass keeps its own shading and only
 *          darkens where shadowed. The ortho frustum follows the player, snapped to whole texels so
 *          edges never crawl. Map size per tier: high full, mid half.
 * 'blob' : phone tier (or shadows off in the options): no map. A soft blob under each kart and the
 *          colossi's shadows baked once onto road strips (capsule shadows along the lamp ray).
 * 'off'  : nothing.
 * Contact blobs also sit under the karts in 'map' mode, fainter: they ground a kart where the
 * map's soft edge alone looks like it floats.
 */
import {
  Mesh, InstancedMesh, PlaneGeometry, BufferGeometry, BufferAttribute, Object3D, Quaternion, Vector3,
  DirectionalLight, MeshBasicNodeMaterial, ShadowNodeMaterial, VSMShadowMap, Color, FrontSide,
} from 'three/webgpu';
import { uv, vec3, float, smoothstep, length, uniform, vertexColor } from 'three/tsl';
import { LOOK } from './look.js';
import { worldLook, shadowFit, shadowMode, shadowSize, capsuleShade, shadeRanges, sampleTrack, V3 } from './colossiMath.js';

/** Catchers live on this layer so the VSM pass (which also draws receivers) never maps the road onto itself. */
export const CATCH_LAYER = 2;

const UPV = new Vector3(0, 1, 0), NV = new Vector3(), QT = new Quaternion();

/** Opaque meshes under root cast (tags, rings and glows stay out of the map). */
function castFrom(root, on = true) {
  if (!root) return;
  // userData.noCast: outline hulls and glows (the hero props) never cast
  root.traverse((o) => { if (o.isMesh && !(o.material && o.material.transparent)) o.castShadow = on && !o.userData.noCast; });
}

function blobMaterial(colour) {
  const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false });
  const c = new Color(colour);
  m.colorNode = vec3(c.r, c.g, c.b);
  m.polygonOffset = true; m.polygonOffsetFactor = -2; m.polygonOffsetUnits = -2;
  return m;
}

/** Road strips over the s ranges a colossus shades, alpha baked per vertex from capsuleShade. */
function spotGeometry(track, placed, dir, W, samples) {
  const P = [], A = [], I = [], f = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] };
  for (const ax of placed) {
    for (const [s0, s1] of shadeRanges(ax, dir, samples)) {
      const rows = Math.max(1, Math.ceil((s1 - s0) / W.spotStepM)), base = P.length / 3;
      for (let k = 0; k <= rows; k++) {
        const fr = track.frameAt(((s0 + (s1 - s0) * k / rows) % track.lengthM + track.lengthM) % track.lengthM, f);
        for (let i = 0; i <= W.spotCols; i++) {
          const x = (i / W.spotCols - 0.5) * fr.w;
          const X = V3.add(V3.add(fr.p, V3.mul(fr.b, x)), V3.mul(fr.n, 0.03));
          const v = capsuleShade(X, dir, ax);
          P.push(...X); A.push(v, v, v);
        }
      }
      const C = W.spotCols + 1;
      for (let k = 0; k < rows; k++) for (let i = 0; i < W.spotCols; i++) {
        const a = base + k * C + i, b = a + 1, c = a + C, d = c + 1;
        I.push(a, b, c, b, d, c);   // counter clockwise seen from above the road (b x t = n)
      }
    }
  }
  if (!I.length) return null;
  const g = new BufferGeometry();
  g.setAttribute('position', new BufferAttribute(new Float32Array(P), 3));
  // the shade rides in the vertex colour (a custom float attribute read in the fragment came back 0)
  g.setAttribute('color', new BufferAttribute(new Float32Array(A), 3));
  g.setIndex(I);
  g.computeBoundingSphere();
  return g;
}

/**
 * renderer: createRenderer's wrapper or a bare WebGPURenderer. light: the scene's DirectionalLight
 * (null: one is made, intensity 0, only for the map). Q: the live quality block (Q.shadows).
 * opts: { enabled (options.shadows), track, receivers: [Mesh], casters: [Object3D], karts: [Object3D],
 *         colossi: placed axes (colossi.placed), scatterCasters: [Object3D] }
 */
export function buildShadows(renderer, scene, light, Q, opts = {}) {
  const W = worldLook(), three = (renderer && renderer.three) || renderer;
  const dir = V3.norm(LOOK.lightDir), track = opts.track || null;
  const karts = opts.karts || [];
  let enabled = opts.enabled !== false;
  let mode = shadowMode(Q.shadows, Q.name, enabled);
  const owned = [];
  const lamp = light || new DirectionalLight('#ffffff', 0);
  if (!light) { lamp.position.set(...dir); scene.add(lamp); owned.push(lamp); }
  if (!lamp.target.parent) scene.add(lamp.target);

  // the map, set up once at the start tier's size (a later governor step only turns it off or on)
  const size = shadowSize(Q.name, W);
  const fitMap = mode === 'map' || mode === 'blob';
  if (three && three.shadowMap && fitMap) {
    three.shadowMap.enabled = true;
    three.shadowMap.type = VSMShadowMap;           // a wide blurred penumbra: PCF stays sub-metre at this texel size
    lamp.shadow.mapSize.set(size.map, size.map);
    const cam = lamp.shadow.camera;
    cam.left = -size.halfM; cam.right = size.halfM; cam.top = size.halfM; cam.bottom = -size.halfM;
    cam.near = 1; cam.far = W.shadowFarM; cam.updateProjectionMatrix();
    lamp.shadow.radius = W.shadowRadius; lamp.shadow.blurSamples = W.shadowBlurSamples; lamp.shadow.bias = W.shadowBias; lamp.shadow.normalBias = W.shadowNormalBias;
  }
  for (const c of opts.casters || []) castFrom(c);
  for (const k of karts) castFrom(k);
  const scatterCast = W.scatterCasts && Q.name === 'high';
  for (const c of opts.scatterCasters || []) castFrom(c, scatterCast);

  // shadow catchers over the receivers
  const catchMat = new ShadowNodeMaterial({ color: new Color(W.shadowColour), opacity: W.shadowOpacity, transparent: true, depthWrite: false, side: FrontSide });
  catchMat.polygonOffset = true; catchMat.polygonOffsetFactor = -1; catchMat.polygonOffsetUnits = -1;
  const catchers = [];
  for (const r of opts.receivers || []) {
    if (!r || !r.geometry) continue;
    const c = new Mesh(r.geometry, catchMat);
    c.receiveShadow = true; c.layers.set(CATCH_LAYER); c.renderOrder = (r.renderOrder || 0) + 1;
    c.position.copy(r.position); c.quaternion.copy(r.quaternion); c.scale.copy(r.scale);
    c.frustumCulled = r.frustumCulled;
    scene.add(c); catchers.push(c); owned.push(c);
  }

  // contact blobs: one instanced disc per kart, radial falloff times the mode's gain; a kart in the
  // air shrinks its blob away by blobFadeUpM (scale, not alpha: one material, no per-instance data)
  const blobGain = uniform(W.blobAlpha);
  const blobMat = blobMaterial(W.shadowColour);
  const r = length(uv().sub(0.5)).mul(2);
  const fall = float(1).sub(smoothstep(0.15, 1, r));
  blobMat.opacityNode = fall.mul(fall).mul(blobGain);
  const blobGeo = new PlaneGeometry(2, 2).rotateX(-Math.PI / 2);
  const blobs = karts.length ? new InstancedMesh(blobGeo, blobMat, karts.length) : null;
  if (blobs) { blobs.frustumCulled = false; blobs.renderOrder = 1; scene.add(blobs); owned.push(blobs); }
  const hints = karts.map(() => -1), E = new Object3D();

  // phone: the colossi's shadows baked onto road strips
  let spots = null;
  if (track && opts.colossi && opts.colossi.length) {
    const g = spotGeometry(track, opts.colossi, dir, W, opts.samples || sampleTrack(track, W.stepM));
    if (g) {
      const m = blobMaterial(W.shadowColour);
      m.opacityNode = vertexColor().r.mul(W.spotAlpha);
      spots = new Mesh(g, m); spots.renderOrder = 1; scene.add(spots); owned.push(spots);
    }
  }

  function apply() {
    const map = mode === 'map';
    lamp.castShadow = map;
    for (const c of catchers) c.visible = map;
    if (spots) spots.visible = mode === 'blob';
    if (blobs) blobs.visible = mode !== 'off';
  }
  apply();

  /** cameraPos: [x, y, z] or a Vector3; forward (optional): the view direction, for the lead. */
  function update(cameraPos, forward = null) {
    if (mode === 'off') return;
    if (mode === 'map') {
      const c = Array.isArray(cameraPos) ? cameraPos : [cameraPos.x, cameraPos.y, cameraPos.z];
      let centre = c;
      if (forward) {
        const fw = Array.isArray(forward) ? forward : [forward.x, forward.y, forward.z];
        const h = Math.hypot(fw[0], fw[2]) || 1;
        centre = [c[0] + fw[0] / h * W.shadowAheadM, c[1], c[2] + fw[2] / h * W.shadowAheadM];
      }
      const fit = shadowFit(centre, dir, size.halfM, size.map, W.shadowDistM);
      lamp.position.set(...fit.pos); lamp.target.position.set(...fit.target);
      lamp.updateMatrixWorld(); lamp.target.updateMatrixWorld();
    }
    if (!blobs || !track) return;
    blobGain.value = mode === 'map' ? W.blobMapAlpha : W.blobAlpha;
    for (let i = 0; i < karts.length; i++) {
      const k = karts[i], p = k.position;
      let s = 0;
      if (k.visible) {
        const q = track.query(p.x, p.y, p.z, hints[i]);
        hints[i] = q.seg;
        const up = Math.max(0, q.height);
        s = q.edgeDist >= -0.5 ? W.blobR * Math.max(0, 1 - up / W.blobFadeUpM) : 0;
        E.position.set(q.p[0] + q.normal[0] * 0.03, q.p[1] + q.normal[1] * 0.03, q.p[2] + q.normal[2] * 0.03);
        E.quaternion.copy(QT.setFromUnitVectors(UPV, NV.set(q.normal[0], q.normal[1], q.normal[2])));
      }
      E.scale.setScalar(s);
      E.updateMatrix(); blobs.setMatrixAt(i, E.matrix);
    }
    blobs.instanceMatrix.needsUpdate = true;
  }

  /** Re-read Q after a governor step (map <-> blob; the map keeps its boot size). */
  function retier() {
    const next = shadowMode(Q.shadows, Q.name, enabled);
    if (next !== mode) { mode = next; apply(); }
  }
  return {
    get mode() { return mode; },
    light: lamp,
    update,
    retier,
    /** The options toggle: off keeps the contact blobs and the baked strips, drops the map. */
    setEnabled(on) { enabled = !!on; retier(); },
    dispose() {
      lamp.castShadow = false;
      for (const o of owned) { if (o.parent) o.parent.remove(o); }
      catchMat.dispose(); blobMat.dispose(); blobGeo.dispose();
      if (spots) { spots.geometry.dispose(); spots.material.dispose(); }
    },
  };
}
