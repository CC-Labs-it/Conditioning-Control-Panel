// Static merge: fold many small meshes that move together into one draw per material.
// A "frame" is the node whose own transform may animate (a toy root, a rotor, a sliding screen).
// Every plain Mesh below it (not below a kept animated child) is baked into the frame's local space
// and grouped by everything that changes how it draws: material, shadow flags, layers, render order,
// frustum culling and the attribute layout. Groups of two or more become one Mesh on the frame; the
// originals and any Group left empty are removed. Positions take the full local matrix, normals the
// normal matrix, a mirrored transform swaps the winding, and drawRange is honoured, so the submitted
// triangles are the same in world space. Materials that read positionGeometry see baked coordinates:
// only merge meshes whose material does not depend on the source geometry's own frame.
import { BufferGeometry, BufferAttribute, Matrix3, Matrix4, Mesh, Vector3 } from 'three/webgpu';

const M = new Matrix4(), N = new Matrix3(), V = new Vector3();

function eligible(o) {
  return o.isMesh && !o.isInstancedMesh && !o.isSkinnedMesh && !o.isBatchedMesh && o.visible && o.geometry && o.material
    && !Array.isArray(o.material) && !o.geometry.morphAttributes?.position
    && o.onBeforeRender === Mesh.prototype.onBeforeRender && !o.children.length && !Object.keys(o.userData || {}).length;
}

function layoutKey(g) {
  return Object.keys(g.attributes).sort().map(k => { const a = g.attributes[k]; return k + a.itemSize + (a.normalized ? 'n' : '') + (a.isInterleavedBufferAttribute ? 'i' : ''); }).join(',');
}

function drawKey(o) {
  return [o.material.uuid, o.castShadow, o.receiveShadow, o.layers.mask, o.renderOrder, o.frustumCulled, layoutKey(o.geometry)].join('|');
}

/** Collect [mesh, matrix relative to frame] for every plain mesh under `frame`, stopping at `keep`. */
function collect(frame, keep, out) {
  const walk = (node, parentM) => {
    for (const child of node.children) {
      if (keep.has(child)) continue;
      if (child.isLight || child.isCamera || !child.visible) continue;
      if (child.matrixAutoUpdate) child.updateMatrix();
      const local = new Matrix4().multiplyMatrices(parentM, child.matrix);
      if (eligible(child)) out.push([child, local]);
      else if (!child.isMesh && !child.isSprite && !child.isLine && !child.isPoints && child.type === 'Group') walk(child, local);
    }
  };
  walk(frame, new Matrix4());
  return out;
}

/** Bake a list of [mesh, matrix] into one indexed geometry with the same attribute names. */
export function bakeGeometry(parts) {
  const first = parts[0][0].geometry, names = Object.keys(first.attributes);
  let vertices = 0, indices = 0;
  const spans = parts.map(([mesh]) => {
    const g = mesh.geometry, count = g.index ? g.index.count : g.attributes.position.count;
    const start = Math.max(0, g.drawRange.start), end = Math.min(count, start + g.drawRange.count);
    vertices += g.attributes.position.count; indices += Math.max(0, end - start);
    return [start, end];
  });
  const arrays = Object.fromEntries(names.map(n => [n, new Float32Array(vertices * first.attributes[n].itemSize)]));
  const index = vertices > 65535 ? new Uint32Array(indices) : new Uint16Array(indices);
  let vBase = 0, iBase = 0;
  parts.forEach(([mesh, matrix], k) => {
    const g = mesh.geometry, count = g.attributes.position.count, [start, end] = spans[k];
    N.getNormalMatrix(matrix);
    const flip = matrix.determinant() < 0;
    for (const name of names) {
      const a = g.attributes[name], size = a.itemSize, out = arrays[name];
      for (let i = 0; i < count; i++) {
        const o = (vBase + i) * size;
        if (name === 'position') { V.fromBufferAttribute(a, i).applyMatrix4(matrix); out[o] = V.x; out[o + 1] = V.y; out[o + 2] = V.z; }
        else if (name === 'normal') { V.fromBufferAttribute(a, i).applyMatrix3(N).normalize(); out[o] = V.x; out[o + 1] = V.y; out[o + 2] = V.z; }
        else if (name === 'tangent') {
          V.set(a.getX(i), a.getY(i), a.getZ(i)).transformDirection(matrix);
          out[o] = V.x; out[o + 1] = V.y; out[o + 2] = V.z; if (size > 3) out[o + 3] = a.getW(i);
        } else for (let c = 0; c < size; c++) out[o + c] = a.getComponent(i, c);
      }
    }
    const src = g.index;
    for (let i = start; i < end; i += 3) {
      const a = src ? src.getX(i) : i, b = src ? src.getX(i + 1) : i + 1, c = src ? src.getX(i + 2) : i + 2;
      index[iBase++] = vBase + a;
      index[iBase++] = vBase + (flip ? c : b);
      index[iBase++] = vBase + (flip ? b : c);
    }
    vBase += count;
  });
  const merged = new BufferGeometry();
  for (const name of names) {
    const a = first.attributes[name];
    merged.setAttribute(name, new BufferAttribute(arrays[name], a.itemSize, a.normalized));
  }
  merged.setIndex(new BufferAttribute(index, 1));
  merged.computeBoundingBox(); merged.computeBoundingSphere();
  return merged;
}

function prune(node, keep) {
  for (const child of [...node.children]) {
    if (keep.has(child)) continue;
    if (child.type === 'Group') { prune(child, keep); if (!child.children.length && !Object.keys(child.userData || {}).length) child.removeFromParent(); }
  }
}

/**
 * Merge the static meshes under `frame`. `keep` lists animated descendants (merged on their own call).
 * Returns { meshes, geometries, before, after } so the owner can dispose the new geometries.
 */
export function mergeStaticMeshes(frame, { keep = [], name = 'static-merge' } = {}) {
  const stop = new Set(keep), parts = collect(frame, stop, []), groups = new Map();
  for (const part of parts) { const k = drawKey(part[0]); if (!groups.has(k)) groups.set(k, []); groups.get(k).push(part); }
  const meshes = [], geometries = [];
  let before = 0, after = 0;
  for (const list of groups.values()) {
    before += list.length;
    if (list.length < 2) { after += 1; continue; }
    const src = list[0][0], geometry = bakeGeometry(list), mesh = new Mesh(geometry, src.material);
    mesh.name = name + '-' + (src.material.name || meshes.length);
    mesh.castShadow = src.castShadow; mesh.receiveShadow = src.receiveShadow; mesh.layers.mask = src.layers.mask;
    mesh.renderOrder = src.renderOrder; mesh.frustumCulled = src.frustumCulled;
    for (const [m] of list) m.removeFromParent();
    frame.add(mesh); meshes.push(mesh); geometries.push(geometry); after += 1;
  }
  prune(frame, stop);
  return { meshes, geometries, before, after };
}
