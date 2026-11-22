/* Refit the existing plaster gable on the Grand Hall side of its shared atrium wall.
 * Run with Node. The optional argument is the original Expo meshoptimizer vendor directory's parent.
 */
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const {readView, components, repair} = require('./hall-end-truss.cjs');
const studio = path.resolve(process.argv[2] || 'C:/Projects/blender-scripting/expo');
const decoder = require(path.join(studio, 'vendor/meshopt_decoder.cjs'));
const encoder = require(path.join(studio, 'vendor/meshopt_encoder.cjs'));
const zones = path.resolve(__dirname, '../../../ConditioningControlPanel/Resources/web/fuzz/assets/expo/zones');

function refitGable(doc, binary) {
  const arch = doc.nodes.find(n => n.name === 'Architecture');
  const primitive = doc.meshes[arch.mesh].primitives.find(p => doc.materials[p.material].name === 'pale_shell');
  const {accessor, ext, data} = readView(doc, binary, primitive.attributes.POSITION);
  const indexed = readView(doc, binary, primitive.indices);
  if (accessor.componentType !== 5122 || !accessor.normalized || accessor.type !== 'VEC3') throw Error('Unexpected position format');
  const indices = Array.from({length:indexed.accessor.count}, (_,i) =>
    indexed.data.readUIntLE(i * indexed.ext.byteStride, indexed.ext.byteStride));
  const positions = Array.from({length:accessor.count}, (_,i) => [0,1,2].map(k => data.readInt16LE(i * ext.byteStride + k * 2)));
  const units = arch.scale.map(s => s / 32767);
  // This is the single flat end wall with the barrel roof silhouette. The hall-facing
  // surface of the atrium plaster is at Z=15.911. The gable belongs on the hall side
  // of that face, behind the lower hall infill at Z=15.885 and its mounted sign.
  const gables = [...components(positions, indices)].filter(part => part.length >= 27 && part.every(i => {
    const [x,y,z] = positions[i].map((value,k) => value * units[k]);
    return Math.abs(z-16) < .002 && y >= 3.25 && y < 10.1 && Math.abs(x) <= 12.01;
  }));
  if (gables.length !== 1 || gables[0].length !== 27) throw Error('Unexpected hall gable topology');
  const z = Math.round(15.890 / units[2]);
  for (const i of gables[0]) {
    positions[i][2] = z;
    data.writeInt16LE(z, i * ext.byteStride + 4);
  }
  accessor.min[2] = Infinity; accessor.max[2] = -Infinity;
  for (const p of positions) {
    accessor.min[2] = Math.min(accessor.min[2], p[2]);
    accessor.max[2] = Math.max(accessor.max[2], p[2]);
  }
  return new Map([[accessor.bufferView, Buffer.from(encoder.encodeGltfBuffer(data, ext.count, ext.byteStride, ext.mode))]]);
}

function fitBoothWalls(doc, binary) {
  const arch = doc.nodes.find(n => n.name === 'Architecture');
  const changes = new Map();
  const expected = {alloy:12, pale_shell:10, marble:8};
  for (const primitive of doc.meshes[arch.mesh].primitives) {
    const name = doc.materials[primitive.material].name;
    if (!(name in expected)) continue;
    const {accessor, ext, data} = readView(doc, binary, primitive.attributes.POSITION);
    const indexed = readView(doc, binary, primitive.indices);
    const indices = Array.from({length:indexed.accessor.count}, (_,i) => indexed.data.readUIntLE(i * indexed.ext.byteStride, indexed.ext.byteStride));
    const positions = Array.from({length:accessor.count}, (_,i) => [0,1,2].map(k => data.readInt16LE(i * ext.byteStride + 2*k)));
    const xUnit = arch.scale[0] / 32767, yUnit = arch.scale[1] / 32767;
    let count = 0;
    for (const part of components(positions, indices)) {
      let lo = Infinity, hi = -Infinity, bottom = Infinity, top = -Infinity;
      for (const i of part) {
        const [x,y] = positions[i];
        lo = Math.min(lo,x*xUnit); hi = Math.max(hi,x*xUnit);
        bottom = Math.min(bottom,y*yUnit); top = Math.max(top,y*yUnit);
      }
      if (bottom < -.005 || top > 3.401) continue;
      const side = Math.sign(lo+hi);
      const front = (side<0?-hi:lo)-12, back = (side<0?-lo:hi)-12;
      // Keep all hall-facing surfaces. Only the short return behind each arch, plinth
      // or wall section changes. Its back now meets the chamber's 90 mm plaster skin.
      if (front < -.27 || front > -.044 || back < .088 || back > .26) continue;
      const shift = Math.round((back-.055) / xUnit) * side;
      for (const i of part) if (positions[i][0]*side*xUnit>12) {
        positions[i][0] -= shift;
        data.writeInt16LE(positions[i][0],i*ext.byteStride);
      }
      count++;
    }
    if (count !== expected[name]) throw Error('Unexpected booth joinery count '+name+': '+count);
    accessor.min[0] = Infinity; accessor.max[0] = -Infinity;
    for (const [x] of positions) {accessor.min[0] = Math.min(accessor.min[0],x); accessor.max[0] = Math.max(accessor.max[0],x);}
    changes.set(accessor.bufferView,Buffer.from(encoder.encodeGltfBuffer(data,ext.count,ext.byteStride,ext.mode)));
  }
  if (changes.size !== 3) throw Error('Missing hall booth assemblies');
  return changes;
}

function fitAtriumGalleryWall(doc, binary) {
  const arch = doc.nodes.find(n => n.name === 'Architecture');
  const changes = new Map();
  for (const primitive of doc.meshes[arch.mesh].primitives) {
    const name = doc.materials[primitive.material].name;
    if (name !== 'pale_shell' && name !== 'alloy') continue;
    const {accessor, ext, data} = readView(doc, binary, primitive.attributes.POSITION);
    const indexed = readView(doc, binary, primitive.indices);
    const indices = Array.from({length:indexed.accessor.count}, (_,i) => indexed.data.readUIntLE(i * indexed.ext.byteStride,indexed.ext.byteStride));
    const positions = Array.from({length:accessor.count}, (_,i) => [0,1,2].map(k => data.readInt16LE(i * ext.byteStride + 2*k)));
    const unit = arch.scale[2]/32767;
    let count = 0;
    for (const part of components(positions,indices)) {
      let front = Infinity, back = -Infinity;
      for (const i of part) {front = Math.min(front,positions[i][2]*unit);back = Math.max(back,positions[i][2]*unit);}
      if (front < 40.80 || front > 40.93 || back < 41.08 || back > 41.20) continue;
      // This wall and its six outer jamb/head members sit in front of the two
      // galleries' end walls at Z=41.001. Preserve the atrium-facing detail.
      const shift = Math.round((back-40.970)/unit);
      for (const i of part) if (positions[i][2]*unit>41) {
        positions[i][2] -= shift;
        data.writeInt16LE(positions[i][2],i*ext.byteStride+4);
      }
      count++;
    }
    if (count !== (name==='alloy'?6:1)) throw Error('Unexpected gallery entrance components '+name+': '+count);
    accessor.min[2] = Infinity; accessor.max[2] = -Infinity;
    for (const p of positions) {accessor.min[2]=Math.min(accessor.min[2],p[2]);accessor.max[2]=Math.max(accessor.max[2],p[2]);}
    changes.set(accessor.bufferView,Buffer.from(encoder.encodeGltfBuffer(data,ext.count,ext.byteStride,ext.mode)));
  }
  return changes;
}
function writeImmutable(filename,result) {
  const output = path.join(zones, filename);
  if (fs.existsSync(output)) {
    if (!fs.readFileSync(output).equals(result)) throw Error('Immutable asset collision');
  } else fs.writeFileSync(output, result);
}

async function main() {
  await Promise.all([decoder.ready, encoder.ready]);
  const planFile = path.join(zones, 'zones.json');
  let text = fs.readFileSync(planFile, 'utf8');
  const hall = JSON.parse(text).zones.find(z => z.id === 'hall');
  for (const [field,original,suffix] of [
    ['file','hall-entry-a0967ab717ccbf37.glb','.glb'],
    ['mobileFile','hall-entry-c856e171f905ab62-mobile.glb','-mobile.glb']
  ]) {
    const gable = repair(fs.readFileSync(path.join(zones, original)), refitGable);
    const result = repair(gable, fitBoothWalls);
    const hash = crypto.createHash('sha256').update(result).digest('hex').slice(0,16);
    const filename = 'hall-entry-' + hash + suffix;
    if (hall[field] !== original && hall[field] !== filename) throw Error('Unexpected hall asset ' + hall[field]);
    writeImmutable(filename,result);
    text = text.replace(JSON.stringify(hall[field]), JSON.stringify(filename));
    console.log(field, filename);
  }
  const atrium = JSON.parse(text).zones.find(z => z.id === 'atrium');
  for (const [field,suffix] of [['file','.glb'],['mobileFile','-mobile.glb']]) {
    const original = 'atrium-08a61de17d70b835' + suffix;
    const result = repair(fs.readFileSync(path.join(zones,original)),fitAtriumGalleryWall);
    const hash = crypto.createHash('sha256').update(result).digest('hex').slice(0,16);
    const filename = 'atrium-entry-' + hash + suffix;
    if (atrium[field] !== original && atrium[field] !== filename) throw Error('Unexpected atrium asset '+atrium[field]);
    writeImmutable(filename,result);
    text = text.replace(JSON.stringify(atrium[field]),JSON.stringify(filename));
    if (field === 'file') {
      const zipped = filename+'.gz';
      writeImmutable(zipped,require('node:zlib').gzipSync(result,{level:9}));
      text = text.replace(JSON.stringify(atrium.arrivalGzip),JSON.stringify(zipped));
    }
    console.log(field,filename);
  }
  fs.writeFileSync(planFile, text);
}
if (require.main === module) main().catch(e => {console.error(e);process.exitCode = 1;});

