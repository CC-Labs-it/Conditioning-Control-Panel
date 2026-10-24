import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { industryZones, industryPose, createMainframeIndustry } from '../sim/mainframeIndustry.js';
import { S, BOOST } from '../sim/state.js';
import { makeFeel, place } from './sim-fake.mjs';
import { makeRace } from './sim-racers-harness.mjs';
const track=loadTrack(JSON.parse(readFileSync(new URL('../tracks/mainframe.json',import.meta.url))));
const zones=industryZones(track);
function setup(z,{x=z.x,height=0,s=z.s}={}) {
  const log=[], events={emit:(name,p)=>log.push({name,...p})}, industry=createMainframeIndustry(track,events);
  const r={i:0,st:place(track,{s,lateral:x,height,speed:30}),feel:makeFeel(),events};
  return {log,industry,r};
}
test('machine footprint keeps a permanent broad centre lane and excludes shortcuts/islands',()=> {
  assert.equal(zones.length,15); assert.deepEqual(new Set(zones.map(z=>z.kind)),new Set(['piston','belt','tar']));
  assert.deepEqual(industryZones({id:'keep'}),[]);
  for(const z of zones) {
    assert.ok(Math.abs(z.x)-z.w/2>=6);
    assert.ok(Math.abs(z.x)+z.w/2 < track.frameAt(z.s).w/2);
    assert.ok(!(track.fields||[]).some(f=>z.s>f.fromS-40&&z.s<f.toS+40));
    assert.ok(!(track.routes||[]).some(r=>z.s>r.fromS-30&&z.s<r.toS+30));
  }
});
test('press has a full readable warning and identical physical/render pose at fixed race time',()=> {
  const z={...zones[0],offsetS:0};
  for(let t=2.4;t<4.2;t+=.05) { assert.equal(industryPose(z,t).warning,true); assert.equal(industryPose(z,t).active,false); }
  assert.equal(industryPose(z,5.5).active,true);
  assert.deepEqual(industryPose(z,5.5),industryPose(z,5.5));
});
test('press contact is bounded, single-shot, with centre bypass and other-deck rejection',()=> {
  const z=zones[0], active=5.5-z.offsetS;
  for(const [x,h,hit] of [[z.x,0,1],[0,0,0],[z.x,12,0],[z.x,-12,0]]) {
    const {industry,r,log}=setup(z,{x,height:h}); industry.update([r],active,1/60);
    assert.equal(industry.hits,hit); assert.equal(log.filter(e=>e.name==='industry-hit').length,hit);
    industry.update([r],active+.02,1/60); assert.equal(industry.hits,hit);
    if(hit) assert.ok(r.st[S.engineV]>=24 && r.st[S.stalled]<.33);
  }
});
test('tar slows only its physical surface; centre and hops stay free, pause and reset deterministic',()=> {
  const z=zones.find(z=>z.kind==='tar');
  for(const [x,h,slow] of [[z.x,0,true],[0,0,false],[z.x,1,false],[z.x,15,false]]) {
    const {industry,r}=setup(z,{x,height:h});
    for(let i=0;i<90;i++) industry.update([r],i/60,1/60);
    assert.equal(r.st[S.engineV]<30,slow); assert.ok(r.st[S.engineV]>=17);
    const value=r.st[S.engineV]; industry.update([r],89/60,1/60); assert.equal(r.st[S.engineV],value);
    industry.reset(); assert.equal(industry.hits,0);
  }
});
test('accelerator is a real boost only inside its marked physical lane',()=> {
  const z=zones.find(z=>z.kind==='belt');
  const {industry,r}=setup(z); industry.update([r],1,1/60);
  assert.equal(r.st[S.boostKind],BOOST.PAD); assert.ok(r.st[S.boostAmount]>.15&&r.st[S.boostAmount]<.17);
  const bypass=setup(z,{x:0}); bypass.industry.update([bypass.r],1,1/60); assert.equal(bypass.r.st[S.boostKind],BOOST.NONE);
  const stronger=setup(z); stronger.r.st[S.boostKind]=BOOST.DRIFT3; stronger.r.st[S.boostAmount]=.5; stronger.r.st[S.boostHoldS]=2;
  stronger.industry.update([stronger.r],1,1/60); assert.equal(stronger.r.st[S.boostKind],BOOST.DRIFT3);
  assert.equal(stronger.r.st[S.boostAmount],.5,'a conveyor never takes away a stronger earned boost');
});
test('real grid takes the dry machine bypass and finishes without added rescues',()=> {
  const run=makeRace({track,seed:20261025}), industry=createMainframeIndustry(track,{emit(){}});
  const drive=run.ai.drive; run.ai.drive=(r,ctx,dt,a)=>{drive(r,ctx,dt,a);industry.steer(r,a);};
  const step=run.racers.step; run.racers.step=(...args)=>{step(...args);if(['racing','finished'].includes(run.race.phase))industry.update(run.racers.list,run.racers.clockS,1/60);};
  run.run(1200,()=>run.racers.list.every(r=>r.finished));
  assert.ok(run.racers.list.every(r=>r.finished),JSON.stringify(run.racers.list.map(r=>({i:r.i,s:r.st[S.s],falls:r.falls}))));
  assert.ok(run.racers.list.every(r=>r.falls===0));
});

test('visible press heads share collision poses, Gentle timing and pause freezes every transform',async()=> {
  const hooks=registerHooks({resolve(specifier,context,next){
    const files={'three/webgpu':'three.webgpu.min.js','three/tsl':'three.tsl.min.js'};
    return files[specifier]?{url:new URL('../vendor/three/build/'+files[specifier],import.meta.url).href,shortCircuit:true}:next(specifier,context);
  }});
  const {buildMainframeHazards}=await import('../render/mainframeHazards.js'); hooks.deregister();
  const world=buildMainframeHazards(track,null); assert.equal(world.group.children.length,zones.length);
  for(const gentle of [false,true]) {
    world.update(.1,{timeS:5.5,gentle});
    for(const z of zones.filter(z=>z.kind==='piston')) assert.equal(world.group.getObjectByName(z.id).getObjectByName('press-head').position.y,industryPose(z,5.5).lift);
  }
  const transforms=()=>{const a=[];world.group.traverse(o=>a.push([...o.position.toArray(),...o.scale.toArray(),...o.quaternion.toArray()]));return a;};
  const before=transforms(); world.update(1,{timeS:9,paused:true}); assert.deepEqual(transforms(),before);
  world.dispose();
});
