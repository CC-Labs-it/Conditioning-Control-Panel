// Offline index-only LOD. Original vertices, skin weights, materials and animations stay intact.
// node dev/gen-kart-lod.mjs <meshoptimizer 1.3.0 package directory>
import { readFileSync, writeFileSync } from 'node:fs';
import { registerHooks, createRequire } from 'node:module';
import { resolve } from 'node:path';
import { createHash } from 'node:crypto';
const require = createRequire(resolve(process.argv[2], 'package.json'));
const { MeshoptSimplifier } = require('meshoptimizer');
const hooks = registerHooks({ resolve(specifier, context, next) {
  const file = specifier === 'three' ? 'build/three.webgpu.min.js' : null;
  return file ? { url: new URL('../vendor/three/' + file, import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { GLTFLoader } = await import('../vendor/three/addons/loaders/GLTFLoader.js');
const { MeshoptDecoder } = await import('../vendor/three/addons/libs/meshopt_decoder.module.js');
hooks.deregister();
await Promise.all([MeshoptSimplifier.ready, MeshoptDecoder.ready]);
const bytes = readFileSync(new URL('../art/models/emi-teacup-kart.glb', import.meta.url));
const loader = new GLTFLoader().setMeshoptDecoder(MeshoptDecoder);
loader.register(parser => ({ name: 'geometryOnly', beforeRoot() {
  const strip = value => { if (!value || typeof value !== 'object') return;
    for (const key of Object.keys(value)) { if (key.endsWith('Texture')) delete value[key]; else strip(value[key]); } };
  for (const material of parser.json.materials || []) strip(material);
} }));
const gltf = await loader.parseAsync(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength), '');
const meshes = [];
gltf.scene.traverse(mesh => {
  if (!mesh.isMesh || !mesh.geometry.index) return;
  const g = mesh.geometry, p = g.attributes.position, indices = Uint32Array.from(g.index.array);
  const positions = new Float32Array(p.count * 3);
  for (let i = 0; i < p.count; i++) positions.set([p.getX(i), p.getY(i), p.getZ(i)], i * 3);
  // Joint identities are discrete: preserve vertices influenced by multiple joints at bending seams.
  const locks = new Uint8Array(p.count), weights = g.attributes.skinWeight;
  if (weights) for (let i = 0; i < p.count; i++) {
    if ([weights.getX(i), weights.getY(i), weights.getZ(i), weights.getW(i)].filter(w => w > .01).length > 1) locks[i] = 1;
  }
  const [low] = MeshoptSimplifier.simplifyWithAttributes(indices, positions, 3, new Float32Array(p.count), 1, [0], locks,
    Math.max(3, Math.floor(indices.length * .4 / 3) * 3), .02, ['LockBorder']);
  meshes.push({ name: mesh.name, vertices: p.count, sourceIndices: indices.length, indices: [...low] });
});
const output = { sourceSha256: createHash('sha256').update(bytes).digest('hex'), meshes };
writeFileSync(new URL('../art/models/emi-teacup-kart-lod1.json', import.meta.url), JSON.stringify(output) + '\n');
console.log(meshes.map(m => ({ name: m.name, before: m.sourceIndices / 3, after: m.indices.length / 3 })));
