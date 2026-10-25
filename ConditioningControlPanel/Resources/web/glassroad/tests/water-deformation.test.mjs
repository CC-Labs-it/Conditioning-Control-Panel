import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three': 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { vec3 } = await import('three/tsl');
const { buildWaterVolume } = await import('../render/waterVolume.js');
hooks.deregister();
const pal = { col: () => vec3(.2,.5,.7) };
function canal() {
  return { lengthM: 60, nodeS: [0,20,40,60], sections: [{id:'coolant-canal',fromS:0,toS:40}],
    frameAt(s) { return { p:[0,5,-s],b:[1,0,0],t:[0,0,-1],n:[0,1,0],w:24,surface:'grip',flags:[] }; },
    surfaceAt(s,x) {
      const length=Math.hypot(x*.12,1);
      return {...this.frameAt(s),p:[x,5+x*x*.06,-s],n:[-x*.12/length,1/length,0],b:[1/length,x*.12/length,0]};
    },
  };
}

test('dense water geometry covers the entire curved canal and carries its real contact basis', t => {
  const tr=canal(),water=buildWaterVolume(tr,pal);t.after(()=>water.dispose());
  const geometry=water.object.getObjectByName('water-surface-raised').geometry;
  const {position,uv,aWaterN,aWaterUV}=geometry.attributes;
  let largestLateral=0,highest=0;
  for(let i=0;i<position.count;i++) {
    const x=uv.getX(i),s=uv.getY(i),contact=tr.surfaceAt(s,x);
    const offset=contact.p.map((p,k)=>position.array[i*3+k]-p);
    const lift=offset.reduce((sum,v,k)=>sum+v*contact.n[k],0);
    for(let axis=0;axis<3;axis++)assert.ok(Math.abs(offset[axis]-contact.n[axis]*lift)<.00001,'follows the curved support normal');
    assert.ok(lift>=.00799&&lift<=.10001,'bounded shallow water and wet patches');
    if(s>=6&&s<=34)assert.ok(Math.abs(lift-.1)<.00001,'interior liquid keeps its full height');
    if(Math.abs(s)<.00001||Math.abs(s-40)<.00001)assert.ok(Math.abs(lift-.015)<.00001,'shoreline meets the shallow join');
    if(s>40)assert.ok(lift<=.01501,'dry-side patches stay flush');
    assert.ok(Math.abs(aWaterN.getX(i)-contact.n[0])<1e-6);
    assert.ok(aWaterUV.getX(i)>0&&aWaterUV.getX(i)<1&&aWaterUV.getY(i)>0&&aWaterUV.getY(i)<1);
    largestLateral=Math.max(largestLateral,Math.abs(x));highest=Math.max(highest,position.getY(i));
  }
  assert.ok(largestLateral>11.9&&highest>13,'water follows skate-park banks, not a narrow flat centre strip');
  assert.ok(position.count>600,'enough vertices for actual local depressions and raised rims');
  const buffers=new Set(Object.values(geometry.attributes).map(a=>a.isInterleavedBufferAttribute?a.data:a));
  assert.ok(buffers.size<=8,'transported basis stays within the guaranteed WebGPU buffer limit');
});

test('one fixed texture carries signed shape, shading slopes and foam, uploads only on changed steps', t => {
  const water=buildWaterVolume(canal(),pal);t.after(()=>water.dispose());
  const texture=water.heightTexture,pixels=texture.image.data;
  assert.equal(pixels,water.field.pixels);
  assert.equal(texture.generateMipmaps,false);
  assert.equal(texture.flipY,false);
  const liquids=water.object.children.filter(m=>m.isMesh&&!m.isInstancedMesh);
  assert.ok(liquids.length>=2&&liquids.every(m=>m.material.positionNode),'surface, depth rim and spill lip share deformation');
  const version=texture.version;
  water.update(0);assert.equal(texture.version,version);
  water.field.disturb({s:15,x:3,radius:4,strength:.6,foam:.9,dirX:1});
  water.update(1/60);
  assert.equal(texture.version,version+1,'only one texture upload for this frame');
  assert.equal(texture.image.data,pixels,'no per-frame texture allocation');
  let lowest=255,highest=0,foam=0,slope=0;
  for(let i=0;i<pixels.length;i+=4) {lowest=Math.min(lowest,pixels[i]);highest=Math.max(highest,pixels[i]);foam=Math.max(foam,pixels[i+3]);slope=Math.max(slope,Math.abs(pixels[i+1]-128));}
  assert.ok(lowest<115&&highest>140,'both trough and raised displacement are present');
  assert.ok(foam>20&&slope>3,'normal and foam respond to the same disturbance');
  const beforePause=pixels.slice(),pausedVersion=texture.version,steps=water.field.stats.steps;
  water.update(.5,{paused:true});
  assert.deepEqual(pixels,beforePause);assert.equal(texture.version,pausedVersion);assert.equal(water.field.stats.steps,steps);
  water.reset();assert.equal(texture.version,pausedVersion+1);assert.equal(water.field.sample(15,3).height,0);
});

for(const name of ['threshold','keep','mainframe'])test(`${name} has bounded full-width shared fluid on desktop and phone`,t=>{
  const track=loadTrack(JSON.parse(readFileSync(new URL(`../tracks/${name}.json`,import.meta.url),'utf8')));
  const desktop=buildWaterVolume(track,pal,null,{quality:'mid'}),phone=buildWaterVolume(track,pal,null,{quality:'phone'});
  t.after(()=>{desktop.dispose();phone.dispose();});
  assert.ok(desktop.field.runs.length>0);
  assert.ok(desktop.stats.surfaceVertices<120000);
  assert.ok(phone.stats.surfaceVertices<desktop.stats.surfaceVertices);
  assert.equal(phone.field.width,32);assert.equal(desktop.field.width,64);
  assert.deepEqual(desktop.runs,phone.runs);
  desktop.object.traverse(mesh=>{if(mesh.geometry)for(const a of Object.values(mesh.geometry.attributes))assert.ok(a.array.every(Number.isFinite));});
  for(const water of [desktop,phone]) {
    const field=water.field;
    const checkEdge=(a,i,run,s,side)=>{
      const u=((side>0?run.nx-1:0)+.5)/field.width;
      const v=(run.rowOffset+Math.max(0,Math.min(1,(s-run.fromS)/(run.toS-run.fromS)))*(run.ny-1)+.5)/field.height;
      assert.ok(Math.abs(a.getX(i)-u)<1e-6,'edge samples its own outermost texel column');
      assert.ok(Math.abs(a.getY(i)-v)*field.height<.001,'edge samples its own run and distance within Float32 metre precision, never the neutral fallback row');
      assert.ok(a.getY(i)>.5/field.height);
    };
    const walls=water.object.getObjectByName('water-visible-depth').geometry.attributes;
    for(let i=0;i<walls.position.count;i++) {
      const s=walls.uv.getY(i),x=walls.uv.getX(i);
      const run=field.runs.find(r=>s>=r.fromS-.001&&s<=r.toS+.001);
      assert.ok(run);checkEdge(walls.aWaterUV,i,run,s,Math.sign(x));
    }
    const fall=water.object.getObjectByName('water-spills-into-void')?.geometry.attributes;
    let vertex=0;
    for(const spill of water.spills) {
      const run=field.runs.find(r=>(spill.fromS+spill.toS)/2>=r.fromS&&(spill.fromS+spill.toS)/2<=r.toS);
      const rows=Math.max(2,Math.ceil((spill.toS-spill.fromS)/1.5));
      for(let layer=0;layer<2;layer++)for(let row=0;row<=rows;row++)for(let depth=0;depth<=12;depth++) {
        checkEdge(fall.aWaterUV,vertex++,run,spill.fromS+(spill.toS-spill.fromS)*row/rows,spill.side);
      }
    }
    assert.equal(vertex,fall?.position.count||0,'every spill vertex, including each lip, was checked');
  }
});
