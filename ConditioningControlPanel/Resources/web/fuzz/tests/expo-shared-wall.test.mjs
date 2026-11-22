import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {BufferGeometry, DoubleSide, Float32BufferAttribute, Mesh, MeshBasicMaterial, Raycaster, Vector3} from '../../vendor/three/three.module.min.js';
import {MeshoptDecoder} from '../../vendor/three/addons/libs/meshopt_decoder.module.js';

const assets = new URL('../assets/expo/zones/', import.meta.url);
const plan = JSON.parse(await readFile(new URL('zones.json', assets)));
await MeshoptDecoder.ready;

// Inspect the shipped architecture with its actual normalized coordinates and indices.
async function architecture(room, field, finish) {
  const def = plan.zones.find(zone => zone.id === room);
  const file = await readFile(new URL(def[field], assets));
  const jsonSize = file.readUInt32LE(12);
  const model = JSON.parse(file.toString('utf8', 20, 20 + jsonSize));
  const binary = file.subarray(28 + jsonSize);
  const node = model.nodes.find(node => node.name === 'Architecture');
  const primitive = model.meshes[node.mesh].primitives.find(p => model.materials[p.material].name === finish);
  function decode(id) {
    const accessor = model.accessors[id];
    const ext = model.bufferViews[accessor.bufferView].extensions.EXT_meshopt_compression;
    const data = Buffer.alloc(ext.count * ext.byteStride);
    const start = ext.byteOffset || 0;
    MeshoptDecoder.decodeGltfBuffer(data, ext.count, ext.byteStride, binary.subarray(start, start + ext.byteLength), ext.mode);
    return {data, accessor, stride:ext.byteStride};
  }
  const pos = decode(primitive.attributes.POSITION), index = decode(primitive.indices);
  assert.equal(pos.accessor.componentType, 5122);
  assert.equal(pos.accessor.normalized, true);
  const xyz = new Float32Array(pos.accessor.count * 3);
  for (let i = 0; i < pos.accessor.count; i++) {
    for (let axis = 0; axis < 3; axis++) xyz[i * 3 + axis] = pos.data.readInt16LE(i * pos.stride + axis * 2) / 32767 * node.scale[axis];
  }
  const geometry = new BufferGeometry();
  geometry.setAttribute('position', new Float32BufferAttribute(xyz, 3));
  geometry.setIndex(Array.from({length:index.accessor.count}, (_,i) => index.data.readUIntLE(i * index.stride, index.stride)));
  return new Mesh(geometry, new MeshBasicMaterial({side:DoubleSide}));
}

for (const field of ['file', 'mobileFile']) {
  test('the atrium keeps its own plaster when the adjoining Hall loads in ' + field, async () => {
    const [wall, backing, truss] = await Promise.all([
      architecture('atrium', field, 'pale_shell'), architecture('hall', field, 'pale_shell'), architecture('hall', field, 'alloy')
    ]);
    const ray = new Raycaster();
    function front(mesh, x, y) {
      ray.set(new Vector3(x, y, 16.4), new Vector3(0, 0, -1));
      const hit = ray.intersectObject(mesh)[0];
      assert.ok(hit, 'Missing surface at ' + [x, y]);
      return hit.point.z;
    }
    // Two wall panels, the infill above the main doors, and both mezzanine brackets.
    for (const [x,y,behind] of [[-2.12,2.84,backing], [-1.42,3.13,backing], [0,3.35,backing],
      [1.4,3.25,backing], [2.1,2.85,backing], [-11.06,5.63,truss], [11.06,5.63,truss]]) {
      assert.ok(front(wall,x,y) - front(behind,x,y) > .02, 'Shared-wall interference at ' + [x,y]);
    }
  });
}
