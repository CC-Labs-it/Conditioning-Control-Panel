/* Paired starting terraces. Shipped cabinet geometry and approved face pixels, built before warmup. */
import { Group, InstancedMesh, BoxGeometry, Box3, Matrix4, Vector3, Color, MeshBasicNodeMaterial } from 'three/webgpu';
import { texture, uv, vec2 } from 'three/tsl';
import { createSoftMaterial } from './materials/colossus.js';
import { faceCell, FACE_LOOK } from './faces.js';
import { planTerrace, TERRACE } from './terraceMath.js';

const writeBasis = (M, axes, p) => M.set(
  axes[0][0], axes[1][0], axes[2][0], p[0],
  axes[0][1], axes[1][1], axes[2][1], p[1],
  axes[0][2], axes[1][2], axes[2][2], p[2],
  0, 0, 0, 1,
);

/**
 * screenMap is borrowed from the world's existing reelTexture, never disposed here.
 * All source geometry and atlas textures are borrowed. Only new materials and platform geometry are owned.
 * avoid accepts colossi capsules or scatter/hero entries with a body capsule.
 */
export function buildTerrace(art, track, pal, { phone = false, avoid = [], samples = null, screenMap = null } = {}) {
  const object = new Group(); object.name = 'starting-terraces';
  const owned = new Set(), instances = [];
  const result = { object, count: 0, placements: [], diagnostics: { count: 0, chosenStartS: null, rejected: [] }, dispose() {
    object.removeFromParent();
    for (const mesh of instances) mesh.dispose();
    for (const item of owned) item.dispose();
    object.clear();
  } };
  const id = phone ? 'threshold-slot-row-lod1' : 'threshold-slot-row-lod0';
  const source = art?.model(id)?.scene;
  if (!source || !pal) { result.diagnostics.rejected.push('missing-asset-or-palette'); return result; }
  source.updateMatrixWorld(true);
  const bounds = new Box3().setFromObject(source), size = bounds.getSize(new Vector3());
  const plan = planTerrace(track, size.toArray(), { avoid, samples });
  result.placements = plan.placements; result.diagnostics.rejected = plan.diagnostics;
  if (!plan.placements.length) return result;
  const modelCentre = bounds.getCenter(new Vector3());
  const recenter = new Matrix4().makeTranslation(-modelCentre.x, -bounds.min.y, -modelCentre.z);
  const M = new Matrix4(), placement = new Matrix4(), local = new Matrix4();
  const addInstances = (geometry, material, matrices, name) => {
    const mesh = new InstancedMesh(geometry, material, matrices.length);
    mesh.name = name; mesh.userData.noCast = true;
    mesh.castShadow = false; mesh.receiveShadow = false;
    matrices.forEach((matrix, i) => mesh.setMatrixAt(i, matrix));
    mesh.instanceMatrix.needsUpdate = true; mesh.computeBoundingSphere();
    object.add(mesh); instances.push(mesh);
  };
  const keep = (item) => { owned.add(item); return item; };
  const mats = new Map();
  const atlas = art.tex('emi-faces');
  const materialFor = (src) => {
    if (mats.has(src)) return mats.get(src);
    let material;
    if (src.name === 'screen' && screenMap) material = new MeshBasicNodeMaterial({ map: screenMap });
    else if (src.name === 'emi_screen') {
      material = new MeshBasicNodeMaterial({ color: '#140b20' });
      if (atlas) {
        // Same top-origin glTF UV convention as karts.js; sample existing pixels, never paint a face.
        const c = faceCell('happy');
        material.colorNode = texture(atlas, uv().mul(0.25).add(vec2(c.col * 0.25, c.row * 0.25))).rgb.mul(FACE_LOOK.gain);
      }
    } else if (src.name === 'slot_lamps') material = new MeshBasicNodeMaterial({ color: '#ffe7c0' });
    else material = createSoftMaterial(src, pal, { haze: 0.6, rimGain: 0.06, fill: 0.2 }).material;
    material.name = 'terrace-' + src.name; keep(material); mats.set(src, material); return material;
  };
  source.traverse((part) => {
    if (!part.isMesh || Array.isArray(part.material)) return;
    local.multiplyMatrices(recenter, part.matrixWorld);
    const matrices = plan.placements.map((p) => writeBasis(placement, p.axes, p.p).clone().multiply(local));
    addInstances(part.geometry, materialFor(part.material), matrices, 'terrace-cabinets-' + part.name);
  });

  const soft = (name, color, metalness, roughness) => keep(createSoftMaterial({ name, color: new Color(color), metalness, roughness }, pal,
    { haze: 0.6, rimGain: 0.04, fill: 0.22 }).material);
  const enamel = soft('terrace-enamel', '#292738', 0.12, 0.34);
  const brass = soft('terrace-brass', '#c5a164', 0.8, 0.38);
  const slab = keep(new BoxGeometry(TERRACE.lengthM, TERRACE.thicknessM, TERRACE.depthM));
  addInstances(slab, enamel, plan.placements.map((p) => writeBasis(M, p.axes, p.platform.p).clone()), 'terrace-platforms');
  // Thin metal edges add construction detail without another glow or a transparent overlay.
  const unit = keep(new BoxGeometry(1, 1, 1)), trim = [];
  for (const p of plan.placements) {
    const deck = writeBasis(M, p.axes, p.platform.p).clone();
    for (const z of [-1, 1]) trim.push(deck.clone().multiply(new Matrix4().makeTranslation(0, TERRACE.thicknessM / 2 - 0.045, z * (TERRACE.depthM / 2 - 0.045)))
      .multiply(new Matrix4().makeScale(TERRACE.lengthM, 0.08, 0.07)));
    for (const x of [-1, 1]) trim.push(deck.clone().multiply(new Matrix4().makeTranslation(x * (TERRACE.lengthM / 2 - 0.045), TERRACE.thicknessM / 2 - 0.045, 0))
      .multiply(new Matrix4().makeScale(0.07, 0.08, TERRACE.depthM)));
  }
  addInstances(unit, brass, trim, 'terrace-brass-edges');
  result.count = plan.placements.length;
  result.diagnostics.count = result.count;
  result.diagnostics.chosenStartS = plan.placements[0].s;
  object.userData.terrace = { id, rows: result.count, cabinets: result.count * 6, ...result.diagnostics };
  return result;
}
