import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {loadTrack} from '../track/load.js';
import {getKeepLayout} from '../track/keep-layout.js';
import {prisonPlan} from '../render/keepPrison.js';
import {keepMediaLayout} from '../render/keepMedia.js';
import {keepSpiralLayout} from '../render/keepSpirals.js';
import {selectScrolllerSlots} from '../render/mediaSelection.js';
import {rectPoints,tierSlots,mediaLook} from '../render/mediaMath.js';
const track=loadTrack(JSON.parse(readFileSync(new URL('../tracks/keep.json',import.meta.url),'utf8')));
const layout=keepMediaLayout(track),M=mediaLook(),course=getKeepLayout(track),prison=prisonPlan(track,course);

test('Keep architecture supplies a half-video half-Loom mix on every tier including the tower climb',()=>{
  assert.equal(keepMediaLayout({id:'threshold'}),null);
  assert.equal(layout.slots.length,54);assert.equal(new Set(layout.slots.map(s=>s.id)).size,54);
  for(const tier of ['high','mid','phone']){
    const visible=tierSlots(layout.slots,tier,M),chosen=selectScrolllerSlots(visible);
    for(const frame of ['billboard','halo','tunnel']){
      const all=visible.filter(s=>s.frame===frame),videos=chosen.filter(s=>s.frame===frame);
      assert.ok(all.length>=8);assert.equal(videos.length,all.length/2,frame+' '+tier);
    }
    assert.ok(visible.every(s=>[0,3].includes(s.program)),'procedural panels remain real Loom programs');
  }
  const spiral=course.landmarks.find(p=>p.id==='tower-spiral'),tower=layout.slots.filter(s=>s.mountId.startsWith('tower-tablet-'));
  assert.equal(tower.length,6);
  assert.ok(tower.every(s=>s.s>spiral.s0&&s.s<spiral.s1));
  assert.ok(tower.at(-1).s-tower[0].s>(spiral.s1-spiral.s0)*.65,'coverage spans the climb');
});

test('Every Keep media face has real masonry backing and gatehouse screens face approaching racers',()=>{
  const inside=(p,box)=>{
    if(box.tilt)return false;
    const dx=p[0]-box.p[0],dz=p[2]-box.p[2],c=Math.cos(box.yaw||0),s=Math.sin(box.yaw||0);
    return Math.abs(dx*c-dz*s)<=box.size[0]/2+.01&&Math.abs(p[1]-box.p[1])<=box.size[1]/2+.01&&Math.abs(dx*s+dz*c)<=box.size[2]/2+.01;
  };
  for(const slot of layout.slots){
    const points=[...rectPoints(slot,1),slot.C],mount=prison.mounts.find(m=>m.id===slot.mountId),f=track.frameAt(slot.s);
    assert.ok(mount,'physical mount '+slot.id);assert.deepEqual(slot.C,mount.p);
    assert.ok(points.every(p=>p.every(Number.isFinite)));
    const nearby=prison.boxes.filter(box=>Math.hypot(...box.p.map((v,i)=>v-slot.C[i]))<Math.hypot(...box.size)/2+12);
    for(const p of points)assert.ok([.1,.35,.7,1.5,3,5,8].some(d=>nearby.some(box=>inside(p.map((v,i)=>v-slot.Z[i]*d),box))),'stone behind '+slot.id);
    if(slot.frame==='halo'){
      assert.ok(points.every(p=>p[1]-f.p[1]>=16),'gatehouse overhead clearance');
      assert.ok(slot.Z.reduce((n,v,i)=>n+v*f.t[i],0)<-.99,'face approaching driver');
    }
  }
});

test('Keep Loom portals leave the exposed rampart, drawbridge and stepping stones readable',()=>{
  const gates=keepSpiralLayout(track).gates;
  assert.equal(gates.length,5);
  for(const gate of gates){
    const s=gate.f*track.lengthM,landmark=course.landmarks.find(p=>s>=p.s0&&s<p.s1);
    assert.ok(!/drawbridge|stones|crown-walk|rampart/.test(landmark.id));
    assert.equal(gate.loom.schema,2);assert.equal(gate.loom.pulse.amp,0);
  }
});
const hooks = (await import('node:module')).registerHooks({resolve(specifier,context,next){
  const files={'three/webgpu':'three.webgpu.min.js','three/tsl':'three.tsl.min.js'};
  return files[specifier]?{url:new URL('../vendor/three/build/'+files[specifier],import.meta.url).href,shortCircuit:true}:next(specifier,context);
}});
const {createKeepBackdrop,keepBackdropPlan}=await import('../render/keepBackdrop.js');
const {Scene,Fog,BackSide}=await import('three/webgpu');
hooks.deregister();

test('Keep horizon surrounds the entire course with cliffs and ruined towers, below a stationary overcast sky',()=>{
  assert.equal(keepBackdropPlan({id:'threshold'}),null);
  const plan=keepBackdropPlan(track);
  assert.deepEqual(keepBackdropPlan(track),plan);
  assert.equal(plan.rocks.length,3);
  assert.ok(plan.skyRadiusM>=3500&&plan.skyRadiusM<=4500);
  for(const layer of plan.rocks)for(const rock of layer){
    const distance=Math.hypot(rock.p[0]-plan.center[0],rock.p[2]-plan.center[2]);
    assert.ok(distance-Math.max(rock.size[0],rock.size[2])>plan.courseRadius+100,'cliffs clear roads');
    assert.ok(rock.size[1]>150,'cliff silhouette rises above buildings');
  }
  const sectors=new Set(plan.towers.map(t=>Math.floor((Math.atan2(t.p[2]-plan.center[2],t.p[0]-plan.center[0])+Math.PI)*4/Math.PI)%8));
  assert.equal(sectors.size,8,'ruined towers visible in every direction');
  for(let s=0;s<track.lengthM;s+=10)assert.ok(track.frameAt(s).p[1]>plan.groundY+18,'island clears road floors');
});

test('Keep backdrop stays within a small draw budget and restores the scene on teardown',()=>{
  const scene=new Scene(),priorFog=new Fog('#123456',100,1000);scene.fog=priorFog;
  const backdrop=createKeepBackdrop(scene,track),stats=backdrop.stats;
  assert.ok(stats.draws<=12);assert.ok(stats.rocks>=200);assert.ok(stats.towers>=24);
  assert.equal(stats.sky,1);assert.equal(stats.starfield,false);assert.equal(stats.island,1);assert.equal(stats.moat,1);
  const sky=backdrop.group.getObjectByName('keep-overcast-sky');
  assert.equal(sky.material.fog,false);assert.equal(sky.material.side,BackSide);assert.equal(sky.material.depthWrite,false);
  assert.equal(backdrop.group.getObjectByName('keep-still-black-moat').position.y,backdrop.plan.moatY-1);
  assert.equal(scene.fog.isFogExp2,true);assert.ok(scene.fog.density<=.0007);
  backdrop.group.traverse(o=>{if(o.isMesh){o.geometry.computeBoundingSphere();assert.ok(Number.isFinite(o.geometry.boundingSphere.radius));}});
  backdrop.dispose();assert.equal(scene.children.length,0);assert.equal(scene.fog,priorFog);
});