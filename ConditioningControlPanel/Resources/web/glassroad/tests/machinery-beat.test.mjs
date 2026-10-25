import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { setMachineryBeat, machineryBeatAt, gearBeatTravel } from '../track/machineryBeat.js';
import { industryPose, industryZones, createMainframeIndustry } from '../sim/mainframeIndustry.js';
import { goonworksPose, goonworksSetpieces } from '../track/goonworks-setpieces.js';
import { createGoonworksHazards } from '../sim/goonworksHazards.js';
import { makeFeel, place, recorder } from './sim-fake.mjs';
import { S } from '../sim/state.js';
import { registerHooks } from 'node:module';
const trackFor = id => loadTrack(JSON.parse(readFileSync(new URL('../tracks/'+id+'.json',import.meta.url),'utf8')));
const clock = (track,songS,raceS=songS,more={}) => setMachineryBeat(track,{enabled:true,songS,raceS,bpm:96,offsetS:.22,...more});

test('machinery uses the heard song anchor, freezes on pause and stays opt-in for authoritative races',()=>{
  const track={}; assert.equal(machineryBeatAt(track,2),null);
  clock(track,.22,0);clock(track,10.22,10);
  assert.equal(machineryBeatAt(track,10).beat,16);
  assert.equal(machineryBeatAt(track,10.125).beat,16.2);
  clock(track,10.22,10,{playing:false});
  assert.equal(machineryBeatAt(track,11).beat,16);
  setMachineryBeat(track,null); assert.equal(machineryBeatAt(track,9),null);
  clock(track,10,10,{enabled:false}); assert.equal(machineryBeatAt(track,10),null);
});

test('a recording arriving after the race has started visibly rearms before claiming collision',()=>{
  const track=trackFor('mainframe'),zone=industryZones(track)[0];clock(track,5,5);
  const frame=machineryBeatAt(track,5),pose=industryPose(zone,5,frame);
  assert.equal(frame.safe,true);assert.equal(pose.warning,true);assert.equal(pose.active,false);assert.equal(pose.lift,7);
});

test('gear stroke stays continuous, monotonic and has a clearly faster beat than the crawl',()=>{
  for(let i=-100;i<500;i++) assert.ok(gearBeatTravel((i+1)/100)>=gearBeatTravel(i/100));
  assert.ok(Math.abs(gearBeatTravel(1-1e-7)-gearBeatTravel(1+1e-7))<1e-5);
  const stroke=gearBeatTravel(.21)-gearBeatTravel(.07),crawl=gearBeatTravel(.81)-gearBeatTravel(.67);
  assert.ok(stroke>crawl*12);
  assert.equal(gearBeatTravel(24),24);
});

test('press impacts land on musical beats after three full warning beats, with bounded holds',()=>{
  const track=trackFor('mainframe'),zone={...industryZones(track)[0],offsetS:0}; clock(track,.22,0);
  const pose=beat=>industryPose(zone,beat*.625,machineryBeatAt(track,beat*.625));
  for(let b=4;b<7;b+=.1){assert.equal(pose(b).warning,true);assert.equal(pose(b).active,false);}
  assert.ok(pose(7.5).lift>0); assert.equal(pose(8).lift,0);
  assert.equal(pose(7.99).impactIndex,-1); assert.equal(pose(8).impactIndex,0);
  assert.equal(pose(24).impactIndex,1); assert.equal(pose(14).lift,7);
  const r={i:0,st:place(track,{s:zone.s,lateral:zone.x,speed:30}),feel:makeFeel(),events:recorder()};
  const machine=createMainframeIndustry(track,r.events);machine.update([r],5,1/60);
  assert.equal(machine.hits,1);assert.ok(r.st[S.stalled]<=.32);
});

test('song seeks and loop wraps visibly rearm the same physical pose before another impact',()=>{
  const track=trackFor('mainframe'),zone={...industryZones(track)[0],offsetS:0};
  clock(track,12,12);clock(track,1,12.02);
  let frame=machineryBeatAt(track,12.02),pose=industryPose(zone,12.02,frame);
  assert.equal(frame.safe,true);assert.equal(pose.warning,true);assert.equal(pose.lift,7);assert.equal(pose.active,false);
  assert.equal(pose.impactIndex,null);
  const until=-frame.beat*frame.beatS;
  frame=machineryBeatAt(track,12.02+until+.001);pose=industryPose(zone,12.02+until+.001,frame);
  assert.equal(frame.safe,false);assert.equal(pose.lift,7);
  setMachineryBeat(track,null);clock(track,.22,0);assert.equal(machineryBeatAt(track,0).beat,0);
});

test('Goonworks swept contact uses the same kicked wheel pose, including the safe bypass',()=>{
  const track=trackFor('goonworks'),rows=goonworksSetpieces(track),wheel=rows.find(r=>r.kind==='wheel');
  clock(track,.22,0);
  const t=8*.625,pose=goonworksPose(wheel,t,machineryBeatAt(track,t));
  assert.equal(pose.angle,wheel.phase+8*Math.PI/12);
  const at=(along,x)=>{
    const st=place(track,{s:wheel.s+along,lateral:x,speed:30});
    for(let j=0;j<3;j++)st[S.posX+j]=wheel.p[j]+wheel.t[j]*along+wheel.b[j]*x;
    return st;
  };
  for(const [x,hit] of [[0,wheel.id],[wheel.bypass,null]]){
    const hazards=createGoonworksHazards(track),r={i:0,kind:'local',st:at(7,x),feel:makeFeel(),events:recorder()};
    assert.equal(hazards.apply(r,at(-7,x),t),hit);
    assert.deepEqual(hazards.inspect(t).toys.find(r=>r.id===wheel.id).angle,pose.angle);
  }
});

test('song-synced rising blocks and flippers preserve warning time and actual clear windows',()=>{
  const track=trackFor('goonworks'),rows=goonworksSetpieces(track);clock(track,.22,0);
  for(const kind of ['block','flipper']){
    const row={...rows.find(r=>r.kind===kind),offsetS:0},pose=b=>goonworksPose(row,b*.625,machineryBeatAt(track,b*.625));
    for(let b=5;b<8;b+=.1){assert.equal(pose(b).warning,true);assert.equal(pose(b).active,false);}
    assert.equal(pose(0).height,0);assert.equal(pose(15).height,0);assert.ok(pose(8.3).height>0);
  }
});

test('rendered press and wheel transforms exactly match song-clock collision, including Gentle and pause',async()=>{
  const hooks=registerHooks({resolve(specifier,context,next){
    if(specifier==='three/webgpu'||specifier==='three/tsl')return{url:new URL('../vendor/three/build/'+(specifier.endsWith('tsl')?'three.tsl.min.js':'three.webgpu.min.js'),import.meta.url).href,shortCircuit:true};
    return next(specifier,context);
  }});
  try{
    const {Scene}=await import('three/webgpu');
    const {buildMainframeHazards}=await import('../render/mainframeHazards.js');
    const {createGoonworksHazardView}=await import('../render/goonworksHazards.js');
    const main=trackFor('mainframe'),goon=trackFor('goonworks'),press=buildMainframeHazards(main,null),wheels=createGoonworksHazardView(new Scene(),goon);
    clock(main,.22,0);clock(goon,.22,0);
    for(const gentle of [false,true])for(const timeS of [2.7,4.5,5,6.3]){
      press.update(.1,{timeS,gentle});wheels.update(timeS);
      for(const zone of industryZones(main).filter(z=>z.kind==='piston')){
        const root=press.group.getObjectByName(zone.id),pose=industryPose(zone,timeS,machineryBeatAt(main,timeS));
        assert.equal(root.getObjectByName('press-head').position.y,pose.lift);
        const shaft=root.getObjectByName('telescopic-press-shaft');
        assert.ok(Math.abs(shaft.position.y+shaft.scale.y/2-12.4)<1e-10);
      }
      for(const row of goonworksSetpieces(goon).filter(r=>r.kind==='wheel'))assert.equal(wheels.group.getObjectByName(row.id).getObjectByName('wheel-rotor').rotation.z,goonworksPose(row,timeS,machineryBeatAt(goon,timeS)).angle);
    }
    const head=press.group.getObjectByName('press-head'),lift=head.position.y;
    press.update(.1,{timeS:9,paused:true});assert.equal(head.position.y,lift);
    press.dispose();wheels.dispose();
  }finally{hooks.deregister();}
});
