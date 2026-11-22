/* Fit the Grand Hall's end truss and door surround inside the shared atrium wall.
 * Run with Node from any directory. The optional argument is the Expo authoring
 * workspace containing the original meshoptimizer 0.22 encoder and decoder.
 */
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const studio = process.argv[2] || 'C:/Projects/blender-scripting/expo';
const decoder = require(path.resolve(studio, 'vendor/meshopt_decoder.cjs'));
const encoder = require(path.resolve(studio, 'vendor/meshopt_encoder.cjs'));
const zones = path.resolve(__dirname, '../../../ConditioningControlPanel/Resources/web/fuzz/assets/expo/zones');
const sourceStem = 'hall-ded2f60ac02e9836';

function readView(doc, binary, index) {
  const accessor = doc.accessors[index], view = doc.bufferViews[accessor.bufferView];
  const ext = view.extensions.EXT_meshopt_compression;
  const start = ext.byteOffset || 0;
  const data = Buffer.alloc(view.byteLength);
  decoder.decodeGltfBuffer(data, ext.count, ext.byteStride, binary.subarray(start, start + ext.byteLength), ext.mode, ext.filter || 'NONE');
  return {accessor, ext, data};
}

// Bevels and UV seams can give the same point several vertex indices.
// Reconnect coincident points before separating the original pieces of joinery.
function components(positions, indices) {
  const first = new Map();
  const parents = positions.map((p,i) => {
    const key = p.join(',');
    if (!first.has(key)) first.set(key, i);
    return first.get(key);
  });
  function root(i) {
    while (parents[i] !== i) {
      parents[i] = parents[parents[i]];
      i = parents[i];
    }
    return i;
  }
  for (let i = 0; i < indices.length; i += 3) {
    const a = root(indices[i]);
    parents[root(indices[i + 1])] = a;
    parents[root(indices[i + 2])] = a;
  }
  const parts = new Map();
  positions.forEach((_,i) => {
    const key = root(i);
    if (!parts.has(key)) parts.set(key, []);
    parts.get(key).push(i);
  });
  return parts.values();
}

function fitHallEnd(doc, binary) {
  const arch = doc.nodes.find(node => node.name === 'Architecture');
  const changes = new Map();
  for (const primitive of doc.meshes[arch.mesh].primitives) {
    const name = doc.materials[primitive.material].name;
    if (!['alloy','graphite','pale_shell','marble'].includes(name)) continue;
    const {accessor, ext, data} = readView(doc, binary, primitive.attributes.POSITION);
    if (accessor.componentType !== 5122 || !accessor.normalized || accessor.type !== 'VEC3') throw Error('Unexpected position format');
    const index = readView(doc, binary, primitive.indices);
    if (![5123,5125].includes(index.accessor.componentType)) throw Error('Unexpected triangle index format');
    const indices = Array.from({length:index.accessor.count}, (_,i) =>
      index.data.readUIntLE(i * index.ext.byteStride, index.ext.byteStride));
    const positions = Array.from({length:accessor.count}, (_,i) => [0,1,2].map(k =>
      data.readInt16LE(i * ext.byteStride + 2 * k)));
    const yUnit = arch.scale[1] / 32767, zUnit = arch.scale[2] / 32767;
    const trussShift = Math.round(.25 / zUnit);
    let trussCount = 0, surroundCount = 0;
    for (const part of components(positions, indices)) {
      let bottom = Infinity, top = -Infinity, front = Infinity, back = -Infinity;
      for (const i of part) {
        const [,y,z] = positions[i];
        bottom = Math.min(bottom, y * yUnit);
        top = Math.max(top, y * yUnit);
        front = Math.min(front, z * zUnit);
        back = Math.max(back, z * zUnit);
      }
      // The end truss is centred on Z=16. The two wall brackets start at
      // Y=4.784, and the deepest member is its 0.25 m springer.
      const truss = ['alloy','graphite'].includes(name) && bottom >= 4.70 &&
        front >= 15.87 && back >= 16.025 && Math.abs((front + back) / 2 - 16) <= .002;
      // The two wall panels, infill, architrave, jambs and plinths overran the
      // atrium plaster at Z=16.090. Keep their hall-facing surfaces and bevels
      // fixed. The shortened backs finish 30 mm inside the atrium plaster.
      const surround = bottom >= -.001 && top <= 3.40 && front > 15.74 &&
        front < 15.90 && back > 16.10 && back < 16.25;
      if (!truss && !surround) continue;
      const shift = truss ? trussShift : Math.round((back - 16.06) / zUnit);
      for (const i of part) {
        if (surround && positions[i][2] * zUnit <= 16) continue;
        positions[i][2] -= shift;
        data.writeInt16LE(positions[i][2], i * ext.byteStride + 4);
      }
      if (truss) trussCount++;
      else surroundCount++;
    }
    const expected = {alloy:[14,3], graphite:[44,0], pale_shell:[0,3], marble:[0,2]};
    if (trussCount !== expected[name][0] || surroundCount !== expected[name][1]) {
      throw Error('Unexpected Hall end components in ' + name + ': ' + [trussCount,surroundCount]);
    }
    accessor.min = [Infinity, Infinity, Infinity];
    accessor.max = [-Infinity, -Infinity, -Infinity];
    for (const p of positions) for (let k = 0; k < 3; k++) {
      accessor.min[k] = Math.min(accessor.min[k], p[k]);
      accessor.max[k] = Math.max(accessor.max[k], p[k]);
    }
    changes.set(accessor.bufferView, Buffer.from(encoder.encodeGltfBuffer(data, ext.count, ext.byteStride, ext.mode)));
  }
  if (changes.size !== 4) throw Error('Hall end assembly missing');
  return changes;
}

function repair(input) {
  const jsonSize = input.readUInt32LE(12);
  const doc = JSON.parse(input.toString('utf8', 20, 20 + jsonSize));
  const binary = input.subarray(28 + jsonSize);
  const changes = fitHallEnd(doc, binary);
  const chunks = [];
  let offset = 0;
  for (const [i,view] of doc.bufferViews.entries()) {
    const source = view.extensions?.EXT_meshopt_compression || view;
    if (source.buffer !== 0) throw Error('Unexpected geometry buffer');
    const start = source.byteOffset || 0;
    const chunk = changes.get(i) || binary.subarray(start, start + source.byteLength);
    source.byteOffset = offset;
    source.byteLength = chunk.length;
    const pad = Buffer.alloc((-chunk.length) & 3);
    chunks.push(chunk, pad);
    offset += chunk.length + pad.length;
  }
  doc.buffers[0].byteLength = offset;
  const json = Buffer.from(JSON.stringify(doc));
  const jsonPad = Buffer.alloc((-json.length) & 3, 32);
  const header = Buffer.from(input.subarray(0, 20));
  header.writeUInt32LE(28 + json.length + jsonPad.length + offset, 8);
  header.writeUInt32LE(json.length + jsonPad.length, 12);
  const binHeader = Buffer.alloc(8);
  binHeader.writeUInt32LE(offset);
  binHeader.write('BIN', 4);
  return Buffer.concat([header, json, jsonPad, binHeader, ...chunks]);
}

async function main() {
  await Promise.all([decoder.ready, encoder.ready]);
  const planFile = path.join(zones, 'zones.json');
  let text = fs.readFileSync(planFile, 'utf8');
  const plan = JSON.parse(text);
  const hall = plan.zones.find(room => room.id === 'hall');
  for (const [suffix, field] of [['.glb','file'], ['-mobile.glb','mobileFile']]) {
    const result = repair(fs.readFileSync(path.join(zones, sourceStem + suffix)));
    const hash = crypto.createHash('sha256').update(result).digest('hex').slice(0,16);
    const filename = 'hall-entry-' + hash + suffix;
    const prior = JSON.stringify(hall[field]);
    if (text.split(prior).length !== 2) throw Error('Ambiguous or missing hall visual filename');
    text = text.replace(prior, JSON.stringify(filename));
    const output = path.join(zones, filename);
    if (fs.existsSync(output)) {
      if (!fs.readFileSync(output).equals(result)) throw Error('Immutable asset collision');
    } else fs.writeFileSync(output, result);
    console.log(field, filename);
  }
  // Texture maps and collision still belong to the existing room edition.
  fs.writeFileSync(planFile, text);
}
main().catch(e => {console.error(e);process.exitCode = 1;});
