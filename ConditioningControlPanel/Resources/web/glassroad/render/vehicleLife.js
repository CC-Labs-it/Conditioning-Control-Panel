/* Rigid child mechanisms cut from shared skinned vehicle geometry. No bones or clips are added. */
import { BufferGeometry, BufferAttribute, Matrix4, Matrix3, Vector3, Group, Mesh } from 'three/webgpu';
import { motionState, stepMechanism, partitionLifeTriangles } from './vehicleLifeMath.js';

const cutCache = new WeakMap(), EMPTY = Object.freeze({});
const component = (a, i, k) => k === 0 ? a.getX(i) : k === 1 ? a.getY(i) : k === 2 ? a.getZ(i) : a.getW(i);

function rigidBone(mesh, vertices) {
  const joints = mesh.geometry.getAttribute('skinIndex'), weights = mesh.geometry.getAttribute('skinWeight');
  if (!joints || !weights || !mesh.skeleton) return -1;
  let owner = -1;
  for (const i of vertices) {
    let joint = -1, total = 0;
    for (let k = 0; k < 4; k++) {
      const w = component(weights, i, k);
      if (!Number.isFinite(w) || w < 0) return -1;
      if (w < 1e-6) continue;
      if (w < .999 || joint >= 0) return -1;
      joint = component(joints, i, k); total += w;
    }
    if (Math.abs(total - 1) > .001 || !Number.isInteger(joint) || joint < 0 || !mesh.skeleton.bones[joint]) return -1;
    if (owner >= 0 && owner !== joint) return -1;
    owner = joint;
  }
  return owner;
}

function acquireStatic(source, partition, key) {
  let entries = cutCache.get(source);
  if (!entries) { entries = new Map(); cutCache.set(source, entries); }
  let entry = entries.get(key);
  if (!entry) {
    const geometry = new BufferGeometry();
    // Own these buffers: disposing a geometry can evict its attributes from the renderer's GPU cache.
    // The clone context preserves interleaved buffer sharing and its vertex-buffer budget.
    const cloneContext = {};
    for (const [name, a] of Object.entries(source.attributes)) geometry.setAttribute(name, a.clone(cloneContext));
    const Index = source.index.array.constructor;
    geometry.setIndex(new BufferAttribute(new Index(partition.keep), 1));
    geometry.boundingBox = source.boundingBox; geometry.boundingSphere = source.boundingSphere;
    entry = { geometry, refs: 0 }; entries.set(key, entry);
  }
  entry.refs++;
  return { geometry: entry.geometry, release() {
    if (--entry.refs === 0) { entries.delete(key); entry.geometry.dispose(); }
  } };
}

function movingGeometry(mesh, vertices, inverse, pivot) {
  const source = mesh.geometry, out = new BufferGeometry();
  const normalMatrix = new Matrix3().getNormalMatrix(inverse), v = new Vector3();
  for (const [name, attribute] of Object.entries(source.attributes)) {
    if (name === 'skinIndex' || name === 'skinWeight' || name.toLowerCase() === '_life' || name === 'tangent') continue;
    if (attribute.itemSize > 4) continue;
    const values = new Float32Array(vertices.length * attribute.itemSize);
    for (let i = 0; i < vertices.length; i++) {
      const src = vertices[i], offset = i * attribute.itemSize;
      if (name === 'position' || name === 'normal') {
        v.set(attribute.getX(src), attribute.getY(src), attribute.getZ(src));
        if (name === 'position') v.applyMatrix4(inverse).sub(pivot);
        else v.applyMatrix3(normalMatrix).normalize();
        values[offset] = v.x; values[offset + 1] = v.y; values[offset + 2] = v.z;
      } else for (let k = 0; k < attribute.itemSize; k++) values[offset + k] = component(attribute, src, k);
    }
    out.setAttribute(name, new BufferAttribute(values, attribute.itemSize));
  }
  out.computeBoundingBox(); out.computeBoundingSphere();
  return out;
}

/**
 * meshes: the vehicle's rebound SkinnedMeshes. definition: {groups:[id,pivot,axis,mode,...]}.
 * update consumes normalized race signals; disposal restores each original geometry exactly.
 */
export function createVehicleLife(meshes, definition, { seed = 0 } = {}) {
  const metrics = { groups: 0, draws: 0, movingTriangles: 0, rejected: false };
  const noop = { update() {}, dispose() {}, metrics };
  const definitions = Array.isArray(definition) ? definition : definition?.groups;
  if (!Array.isArray(meshes) || !definitions?.length) return noop;
  const byId = new Map();
  for (const d of definitions) {
    if (!Number.isInteger(d.id) || d.id < 1 || d.id > 5 || byId.has(d.id) || !['spin', 'swing', 'pulse', 'press'].includes(d.mode)
      || d.pivot?.length !== 3 || d.axis?.length !== 3 || ![...d.pivot, ...d.axis].every(Number.isFinite)
      || Math.hypot(...d.axis) < .001) { metrics.rejected = true; return noop; }
    byId.set(d.id, d);
  }
  const plans = [], assemblies = new Map(), valid = new Set(byId.keys());
  // Validate the entire cut before changing any mesh. Mixed weights or IDs leave the static vehicle intact.
  for (const mesh of meshes) {
    const source = mesh.geometry, attr = source?.getAttribute('_life') || source?.getAttribute('_LIFE');
    if (!attr) continue;
    if (!source.index || attr.itemSize !== 1 || Array.isArray(mesh.material)) { metrics.rejected = true; return noop; }
    const ids = new Array(attr.count);
    for (let i = 0; i < attr.count; i++) ids[i] = attr.getX(i);
    const partition = partitionLifeTriangles(source.index.array, ids, valid);
    if (!partition) { metrics.rejected = true; return noop; }
    if (!partition.groups.size) continue;
    const moving = [];
    for (const [id, vertices] of partition.groups) {
      const boneIndex = rigidBone(mesh, vertices);
      if (boneIndex < 0) { metrics.rejected = true; return noop; }
      const bone = mesh.skeleton.bones[boneIndex], previous = assemblies.get(id);
      if (previous && previous !== bone) { metrics.rejected = true; return noop; }
      assemblies.set(id, bone); moving.push({ id, vertices, boneIndex });
    }
    plans.push({ mesh, source, partition, moving });
  }
  const draws = plans.reduce((n, p) => n + p.moving.length, 0);
  if (!draws) return noop;
  // At most five semantic assemblies; their preserved material splits cost at most ten draws.
  if (draws > 10) { metrics.rejected = true; return noop; }
  const cuts = [], owned = [], controls = new Map();
  for (const plan of plans) {
    const { mesh, source, partition } = plan;
    const cache = acquireStatic(source, partition, [...valid].sort().join(','));
    cuts.push({ mesh, source, cache });
    for (const p of plan.moving) {
      const d = byId.get(p.id), inverse = mesh.skeleton.boneInverses[p.boneIndex].clone().multiply(mesh.bindMatrix);
      const pivot = new Vector3(...d.pivot).applyMatrix4(inverse);
      let control = controls.get(p.id);
      if (!control) {
        const node = new Group(); node.name = 'vehicle-life-' + d.name;
        node.position.copy(pivot); node.matrixAutoUpdate = false;
        mesh.skeleton.bones[p.boneIndex].add(node);
        const axis = new Vector3(...d.axis).transformDirection(inverse);
        control = { node, pivot, axis, definition: d, state: motionState(), phase: (((Number.isFinite(seed) ? seed : 0) * .61803398875 + p.id * .137) % 1) * Math.PI * 2 };
        controls.set(p.id, control);
      }
      const geometry = movingGeometry(mesh, p.vertices, inverse, pivot);
      const child = new Mesh(geometry, mesh.material); child.name = 'mechanism-' + p.id;
      child.castShadow = mesh.castShadow; child.receiveShadow = mesh.receiveShadow;
      child.frustumCulled = false; control.node.add(child); owned.push(geometry);
      metrics.movingTriangles += p.vertices.length / 3;
    }
    mesh.geometry = cache.geometry;
  }
  metrics.groups = controls.size; metrics.draws = draws;
  const controlList = [...controls.values()];
  let disposed = false;
  const result = { metrics, update(dt, signals = EMPTY) {
    if (disposed) return;
    for (let i = 0; i < controlList.length; i++) {
      const c = controlList[i];
      const value = stepMechanism(c.state, c.definition, dt, signals, c.phase);
      const { node, pivot, axis } = c;
      node.position.copy(pivot); node.quaternion.identity(); node.scale.set(1, 1, 1);
      if (c.definition.mode === 'spin' || c.definition.mode === 'swing') node.quaternion.setFromAxisAngle(axis, value);
      else if (c.definition.mode === 'press') node.position.addScaledVector(axis, value);
      node.updateMatrix();
      if (c.definition.mode === 'pulse') {
        // Rank-one axis scale keeps a speaker cone anchored even when its bone axes are rotated.
        const x = axis.x, y = axis.y, z = axis.z;
        node.matrix.set(1 + value*x*x, value*x*y, value*x*z, pivot.x,
          value*y*x, 1 + value*y*y, value*y*z, pivot.y,
          value*z*x, value*z*y, 1 + value*z*z, pivot.z, 0, 0, 0, 1);
      }
      node.matrixWorldNeedsUpdate = true;
    }
  }, dispose() {
    if (disposed) return;
    disposed = true;
    for (const c of controls.values()) c.node.removeFromParent();
    for (const cut of cuts) { if (cut.mesh.geometry === cut.cache.geometry) cut.mesh.geometry = cut.source; cut.cache.release(); }
    for (const geometry of owned) geometry.dispose();
    controls.clear();
  } };
  result.update(0, { motionOff: true });
  return result;
}
