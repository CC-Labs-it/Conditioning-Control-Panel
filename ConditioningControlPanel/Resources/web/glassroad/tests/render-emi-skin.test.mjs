import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';

const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { three: 'build/three.webgpu.min.js', 'three/webgpu': 'build/three.webgpu.min.js', 'three/tsl': 'build/three.tsl.min.js' };
  const path = files[specifier] || (specifier.startsWith('three/addons/') ? specifier.slice(6) : null);
  return path ? { url: new URL('../vendor/three/' + path, import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { GLTFLoader } = await import('three/addons/loaders/GLTFLoader.js');
const { MeshoptDecoder } = await import('three/addons/libs/meshopt_decoder.module.js');
const { createEmiSkin } = await import('../render/emiSkin.js');
hooks.deregister();

// Parse the real shipped mesh/rig; omit only textures so the check needs no browser or GPU.
const source = readFileSync(new URL('../art/models/emi-teacup-kart.glb', import.meta.url));
const oldJsonLength = source.readUInt32LE(12);
const json = JSON.parse(source.subarray(20, 20 + oldJsonLength).toString());
json.materials = json.materials.map((m) => ({ name: m.name }));
const jsonBytes = Buffer.from(JSON.stringify(json)), padded = (jsonBytes.length + 3) & ~3;
const binary = source.subarray(20 + oldJsonLength), glb = Buffer.alloc(20 + padded + binary.length, 0x20);
source.copy(glb, 0, 0, 20); glb.writeUInt32LE(glb.length, 8); glb.writeUInt32LE(padded, 12);
jsonBytes.copy(glb, 20); binary.copy(glb, 20 + padded);
const model = await new GLTFLoader().setMeshoptDecoder(MeshoptDecoder).parseAsync(glb.buffer.slice(glb.byteOffset, glb.byteOffset + glb.byteLength), '');

test('accepted kart skin fits eight GPU vertex buffers and preserves every tint mask', () => {
  const meshes = []; model.scene.traverse((m) => { if (m.isSkinnedMesh && m.material.name === 'kart_surface') meshes.push(m); });
  assert.ok(meshes.length);
  const sources = meshes.map((m) => m.geometry), skins = createEmiSkin(model.scene);
  meshes.forEach((m, index) => {
    const g = m.geometry, shell = g.getAttribute('garageShell'), detail = g.getAttribute('garageDetail');
    assert.notEqual(g, sources[index]);
    assert.equal(sources[index].getAttribute('garageShell'), undefined, 'shared source is unchanged');
    assert.equal(shell.data, detail.data, 'both shader attributes use one GPU buffer');
    const buffers = new Set(Object.values(g.attributes).map((a) => a.isInterleavedBufferAttribute ? a.data : a));
    assert.ok(buffers.size <= 8, 'actual accepted geometry uses ' + buffers.size + ' buffers');
    const joints = g.getAttribute('skinIndex'), weights = g.getAttribute('skinWeight'), seen = new Set();
    const expected = { EMI_root: [1, 0, 0, 0], ant0: [0, 1, 0, 0], shoulderL: [0, 0, 1, 0], footL: [0, 0, 0, 1] };
    for (let n = 0; n < joints.count; n++) for (let k = 0; k < 4; k++) {
      const name = m.skeleton.bones[joints.getComponent(n, k)].name;
      if (!expected[name] || weights.getComponent(n, k) < 0.99 || seen.has(name)) continue;
      assert.deepEqual([shell.getX(n), detail.getX(n), detail.getY(n), detail.getZ(n)], expected[name], name);
      seen.add(name);
    }
    assert.equal(seen.size, Object.keys(expected).length, 'real shell, antenna, hand and shoe vertices checked');
  });
  skins.dispose();
});
