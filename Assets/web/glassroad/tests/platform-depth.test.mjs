import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { visualClearanceSamples,capVisualDepth } from '../track/visualDepth.js';
const hooks=registerHooks({resolve(specifier,context,next){
  const files={'three/webgpu':'three.webgpu.min.js','three/tsl':'three.tsl.min.js'};
  return files[specifier]?{url:new URL('../vendor/three/build/'+files[specifier],import.meta.url).href,shortCircuit:true}:next(specifier,context);
}});
const {platformBodyGeometry,desiredPlatformDepth}=await import('../render/platformDepth.js');
const {buildStones,stoneLayout,STONE_KIT}=await import('../render/stones.js');
const {buildGoonworks,goonworksRibbonShell}=await import('../render/goonworks.js');
const {Group,Mesh,BoxGeometry,MeshStandardMaterial,Matrix4,Vector3}=await import('three/webgpu');hooks.deregister();
const frame=(p,s=500)=>({p,s,b:[1,0,0],n:[0,1,0],t:[0,0,-1],w:20});
test('beveled bodies have tangible depth entirely beneath unchanged top footprints',()=>{
  for(const id of ['threshold','keep','boudoir','mainframe']){
    const g=platformBodyGeometry(id),p=g.getAttribute('position');
    assert.equal(g.boundingBox.max.y,0);assert.equal(g.boundingBox.min.y,-1);
    assert.ok(g.boundingBox.max.x<=2.5&&g.boundingBox.min.x>=-2.5);
    assert.ok(g.boundingBox.max.z<=2.5&&g.boundingBox.min.z>=-2.5);
    for(let i=0;i<p.count;i+=3){
      const a=new Vector3().fromBufferAttribute(p,i),b=new Vector3().fromBufferAttribute(p,i+1),c=new Vector3().fromBufferAttribute(p,i+2);
      assert.ok(b.sub(a).cross(c.sub(a)).length()>1e-6,'no flattened or degenerate faces');
    }
    assert.equal(p.count/3,72,'one small shared hull geometry per course');g.dispose();
  }
});
test('actual GOONWORKS renderer deepens rounded keels while keeping single-surface caps and landing motion',t=>{
  const track=loadTrack(JSON.parse(readFileSync(new URL('../tracks/goonworks.json',import.meta.url),'utf8'))),world=buildGoonworks(track);
  assert.deepEqual(goonworksRibbonShell(track).positions,track.meshes.road.positions,'dark skin never adds depth below the clearance-capped road');
  assert.equal(world.group.getObjectByName('goonworks-thick-ribbon').material.polygonOffset,true);
  assert.ok(world.platforms.length>0);
  for(const {row,holder} of world.platforms){
    const cap=holder.getObjectByName('single-surface-platform-cap'),keel=holder.getObjectByName('goonworks-platform-keel');
    assert.ok(cap&&keel);assert.ok(row.depthM>=7.7&&row.depthM<=10);
    assert.ok(Math.abs(keel.position.y+keel.scale.y/2+.6)<1e-7,'keel joins cap without moving it');
    assert.equal(cap.position.y,0);assert.deepEqual(cap.scale.toArray(),[1,1,1]);
  }
  const p=world.platforms[0],rest=p.holder.position.clone();world.hit(p.row.id);world.update(.2);
  assert.ok(p.holder.position.distanceTo(rest)>0);world.reset();assert.ok(p.holder.position.equals(rest));
  t.diagnostic('goonworks actual: '+world.platforms.length+' rounded keels, '+Math.min(...world.platforms.map(p=>p.row.depthM)).toFixed(2)+'..'+Math.max(...world.platforms.map(p=>p.row.depthM)).toFixed(2)+'m');world.dispose();
});
test('depth cap protects headroom on lower main roads and fork routes using spatial samples',()=>{
  const track={lengthM:100,frameAt:s=>frame([0,0,s],s)};
  const samples=visualClearanceSamples(track),candidate=frame([0,10,50]);candidate.t=[0,0,1];
  const depth=capVisualDepth(candidate,7,samples,{halfWidth:8,halfLength:8,motionM:.45});
  assert.ok(depth>4.8&&depth<5.05,depth);
  assert.equal(capVisualDepth(frame([80,10,50]),7,samples,{halfWidth:8,halfLength:8}),7);
  const fork={id:'lower',lengthM:100,frameAt:s=>frame([0,4,s],s)};
  const forkSamples=visualClearanceSamples({...track,forks:[fork]});
  const forkDepth=capVisualDepth(candidate,7,forkSamples,{halfWidth:8,halfLength:8,motionM:.45});
  assert.ok(forkDepth>.8&&forkDepth<1.05,forkDepth);
  assert.equal(capVisualDepth(frame([0,7,50]),7,forkSamples,{halfWidth:8,halfLength:8}),0);
});
test('accepted-model courses gain thick islands while top and landing transforms stay unchanged',t=>{
  for(const id of ['threshold','keep','boudoir','mainframe']){
    const track=loadTrack(JSON.parse(readFileSync(new URL('../tracks/'+id+'.json',import.meta.url),'utf8')));
    const before=stoneLayout(track),source=new Group(),geo=new BoxGeometry(5,.8,5),mat=new MeshStandardMaterial();mat.name='surface';source.add(new Mesh(geo,mat));
    const built=buildStones(track,{model:()=>({scene:source})},null);
    assert.ok(built.rows.length>0,id);let deep=0;
    for(const [i,row] of built.rows.entries()){
      assert.deepEqual(row.p,before[i].p,'top support frame is unchanged');
      assert.ok(row.depthM>=0&&row.depthM<=desiredPlatformDepth(row,id));if(row.depthM>=1.3)deep++;
    }
    assert.ok(deep>=built.rows.length*.8,id+' readable thick bodies '+deep+'/'+built.rows.length);
    t.diagnostic(id+': '+built.rows.length+' hulls, '+Math.min(...built.rows.map(r=>r.depthM)).toFixed(2)+'..'+Math.max(...built.rows.map(r=>r.depthM)).toFixed(2)+'m deep');
    const movingRow=built.rows.find(r=>r.broad&&r.shape!=='hex');
    const holder=movingRow&&built.group.getObjectByName('stone:'+movingRow.id);
    if(holder){
      const hull=holder.children.find(o=>o.name.startsWith('platform-depth:'));assert.ok(hull,id+' structural body');
      const top=holder.position.clone(),local=hull.matrix.clone();built.hit(movingRow.stone);built.update(.3);
      assert.ok(holder.position.distanceTo(top)>0,id+' hull follows landing');
      const paused=holder.position.clone();built.update(1,{paused:true});assert.ok(holder.position.equals(paused));
      built.reset();assert.ok(holder.position.distanceTo(top)<1e-8);assert.ok(hull.matrix.equals(local));
    }
    built.dispose();geo.dispose();mat.dispose();
  }
});
test('fallback depth extends down without shifting its previous top',()=>{
  const track=loadTrack(JSON.parse(readFileSync(new URL('../tracks/threshold.json',import.meta.url),'utf8'))),built=buildStones(track,null,null);
  const mesh=built.group.getObjectByName('stone-slab'),matrix=new Matrix4();
  for(const [i,row] of built.rows.entries()){
    mesh.getMatrixAt(i,matrix);const top=new Vector3(0,STONE_KIT.slab[1]/2,0).applyMatrix4(matrix);
    const expected=new Vector3(...row.p).addScaledVector(new Vector3(...row.n),STONE_KIT.slab[1]);
    assert.ok(top.distanceTo(expected)<1e-3,'existing fallback top stays fixed');
  }
  built.dispose();
});
