import { BufferAttribute } from 'three/webgpu';
import { VERTEX_SHADE_ATTRIBUTE, vertexShadePlan } from './vertexShadeMath.js';

/** Source bytes are the same buffer passed to GLTFLoader.parseAsync. No second GLB download. */
export async function attachVertexShade(gltf, row, source, bytes) {
  if (!globalThis.crypto?.subtle || !gltf?.parser?.associations) return false;
  const digest = await crypto.subtle.digest('SHA-256', source);
  const hash = Array.from(new Uint8Array(digest), (n) => n.toString(16).padStart(2, '0')).join('');
  const meshes = [], decoded = [], parser = gltf.parser;
  gltf.scene.traverse((mesh) => {
    if (!mesh.isMesh) return;
    const ref = parser.associations.get(mesh);
    const primitive = parser.json.meshes?.[ref?.meshes]?.primitives?.[ref?.primitives];
    const accessor = parser.json.accessors?.[primitive?.attributes?.POSITION];
    meshes.push(mesh);
    decoded.push({ meshIndex: ref?.meshes, primitiveIndex: ref?.primitives,
      vertexCount: mesh.geometry?.getAttribute('position')?.count, accessorCount: accessor?.count,
      hasShade: !!mesh.geometry?.getAttribute(VERTEX_SHADE_ATTRIBUTE) });
  });
  const plan = vertexShadePlan(row, hash, bytes.byteLength, decoded);
  if (!plan) return false;
  // Validate every primitive before changing any geometry. A reused geometry receives the same accessor data.
  for (const p of plan) {
    const mesh = meshes[p.index];
    if (!mesh.geometry.getAttribute(VERTEX_SHADE_ATTRIBUTE)) {
      const values = new Uint8Array(bytes.slice(p.byteOffset, p.byteOffset + p.byteLength));
      mesh.geometry.setAttribute(VERTEX_SHADE_ATTRIBUTE, new BufferAttribute(values, 2, true));
    }
    const mark = (src) => {
      const m = src.clone();
      m.userData.ccpVertexShade = !p.neutral;
      return m;
    };
    mesh.material = Array.isArray(mesh.material) ? mesh.material.map(mark) : mark(mesh.material);
  }
  return true;
}
