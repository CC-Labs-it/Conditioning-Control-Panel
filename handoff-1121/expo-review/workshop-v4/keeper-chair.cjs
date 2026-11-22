// Pull the original empty chair clear of the desk pedestal on both display profiles.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const studio = process.argv[2] || 'C:/Projects/blender-scripting/expo';
const decoder = require(path.resolve(studio, 'vendor/meshopt_decoder.cjs'));
const encoder = require(path.resolve(studio, 'vendor/meshopt_encoder.cjs'));
const {readView, components, repair} = require('./hall-end-truss.cjs');
const zones = process.argv[3] || path.resolve(__dirname, '../../../ConditioningControlPanel/Resources/web/fuzz/assets/expo/zones');
const offset = .62;

function moveChair(doc, binary) {
  const arch = doc.nodes.find(n => n.name === 'Architecture');
  const changes = new Map(), counts = {ash:0,cloth:0};
  for (const prim of doc.meshes[arch.mesh].primitives) {
    const material = doc.materials[prim.material].name;
    if (!(material in counts)) continue;
    const {accessor,ext,data} = readView(doc,binary,prim.attributes.POSITION);
    const indices = readView(doc,binary,prim.indices);
    if (accessor.componentType !== 5122 || !accessor.normalized || accessor.type !== 'VEC3') throw Error('Unexpected habitat positions');
    const order = Array.from({length:indices.accessor.count}, (_,i) => indices.data.readUIntLE(i*indices.ext.byteStride,indices.ext.byteStride));
    const positions = Array.from({length:accessor.count}, (_,i) => [0,1,2].map(k => data.readInt16LE(i*ext.byteStride+k*2)));
    const unit = arch.scale.map(s => s/32767),shift = Math.round(offset/unit[0]);
    for (const part of components(positions,order)) {
      // Chair corners from the original room's chair(19.66,-11.60) specification.
      const chair = part.every(i => {
        const p = positions[i],x=p[0]*unit[0],y=p[1]*unit[1],z=p[2]*unit[2];
        return x>=19.38 && x<=19.94 && y>=-.025 && y<=.99 && z>=11.33 && z<=11.87;
      });
      if (!chair) continue;
      counts[material]++;
      for (const i of part) {
        positions[i][0] += shift;
        data.writeInt16LE(positions[i][0], i*ext.byteStride);
      }
    }
    accessor.min[0]=Math.min(...positions.map(p => p[0]));
    accessor.max[0]=Math.max(...positions.map(p => p[0]));
    changes.set(accessor.bufferView,Buffer.from(encoder.encodeGltfBuffer(data,ext.count,ext.byteStride,ext.mode)));
  }
  if (counts.ash!==5 || counts.cloth!==1) throw Error('Expected the four legs, back and seat. Found '+JSON.stringify(counts));
  return changes;
}

function moveCollision(input) {
  const size = input.readUInt32LE(12),doc = JSON.parse(input.toString('utf8',20,20+size));
  for (const name of ['Habitat chair seat','Habitat chair back']) {
    const node = doc.nodes.find(n=>n.name===name);
    node.translation[0] += offset;
  }
  const json = Buffer.from(JSON.stringify(doc)),pad=Buffer.alloc((-json.length)&3,32),head=Buffer.from(input.subarray(0,20));
  const tail = input.subarray(20+size);
  head.writeUInt32LE(20+json.length+pad.length+tail.length,8);head.writeUInt32LE(json.length+pad.length,12);
  return Buffer.concat([head,json,pad,tail]);
}

async function main() {
  await Promise.all([decoder.ready,encoder.ready]);
  const manifest=path.join(zones,'zones.json');let text=fs.readFileSync(manifest,'utf8');
  const room=JSON.parse(text).zones.find(z=>z.id==='habitat');
  for (const [key,source,suffix] of [['file','habitat-f7dc893cfcac2ed0.glb','.glb'],['mobileFile','habitat-f7dc893cfcac2ed0-mobile.glb','-mobile.glb'],['collision','habitat-chair-3d44dd1687937a14-collision.glb','-collision.glb']]) {
    const bytes=fs.readFileSync(path.join(zones,source));
    const result=key==='collision'?moveCollision(bytes):repair(bytes,moveChair);
    const hash=crypto.createHash('sha256').update(result).digest('hex').slice(0,16),name='habitat-keeper-'+hash+suffix;
    const old=JSON.stringify(room[key]);
    if (text.split(old).length!==2) throw Error('Missing or repeated '+key+' '+old);
    fs.writeFileSync(path.join(zones,name),result);text=text.replace(old,JSON.stringify(name));console.log(name);
  }
  fs.writeFileSync(manifest,text);
}
main().catch(e=>{console.error(e);process.exitCode=1;});
