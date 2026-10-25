import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { setMachineryBeat, machineryBeatAt } from '../track/machineryBeat.js';
import { keepGatePhase } from '../sim/keepGates.js';
import { keepChainPhase } from '../sim/keepChains.js';
import { industryPose } from '../sim/mainframeIndustry.js';
import { perfumePhase } from '../sim/boudoirPerfume.js';
import { mascaraPhase, mirrorGatePhase, powderPressPhase, ribbonPhase } from '../track/boudoir-setpieces.js';
import { goonworksPose, goonworksSetpieces } from '../track/goonworks-setpieces.js';
import { scanClock, scanPose } from '../sim/mainframeScan.js';

const row = { offsetS: 0, kind: 'piston' };
const poses = [
  ['gate', (t,b) => keepGatePhase(row,t,{},b), 'liftM'],
  ['chain', (t,b) => keepChainPhase(row,t,b), 'x'],
  ['press', (t,b) => industryPose(row,t,b), 'lift'],
  ['brush', (t,b) => mascaraPhase(row,t,b), 'x'],
  ['mirror', (t,b) => mirrorGatePhase({...row,lane:1},t,b), 'liftM'],
  ['powder', (t,b) => powderPressPhase(row,t,b), 'liftM'],
  ['ribbon', (t,b) => ribbonPhase(row,t,b), 'direction'],
];
const goon = loadTrack(JSON.parse(readFileSync(new URL('../tracks/goonworks.json',import.meta.url),'utf8')));
for(const kind of ['wheel','shutter','block','flipper']) {
  const r = {...goonworksSetpieces(goon).find(r=>r.kind===kind), offsetS:0};
  poses.push([kind,(t,b)=>goonworksPose(r,t,b),kind==='wheel'?'angle':kind==='shutter'?'x':'height']);
}

test('every silent solo trap moves at twice the authored pace while authority remains unchanged',()=>{
  const track={}; setMachineryBeat(track,{enabled:true,fallback:true,raceS:0});
  for(const [name,pose,key] of poses) {
    let changed=0;
    for(let t=.03;t<12;t+=.13) {
      const faster=pose(t,machineryBeatAt(track,t))[key], old=pose(t,null)[key];
      assert.ok(Math.abs(faster-pose(t*2,null)[key])<1e-9,name+' uses the shared accelerated pose');
      if(Math.abs(faster-old)>.05)changed++;
    }
    assert.ok(changed>15,name+' has clearly faster movement');
  }
  setMachineryBeat(track,{enabled:false,fallback:true,raceS:6});
  assert.equal(machineryBeatAt(track,6),null,'online authority opts out');
});

test('song-driven motions double their cycles on heard subdivisions without shortening seek grace',()=>{
  const track={};setMachineryBeat(track,{enabled:true,songS:0,raceS:0,bpm:96});
  const oldFrame=t=>({beat:t/.625,beatS:.625,safe:false});
  for(const [name,pose,key] of poses)for(let t=12;t<20;t+=.21)
    assert.ok(Math.abs(pose(t,machineryBeatAt(track,t))[key]-pose(t*2,oldFrame(t*2))[key])<1e-8,name+' remains on the musical grid');
  setMachineryBeat(track,{enabled:true,songS:1,raceS:12,bpm:96});
  const frame=machineryBeatAt(track,12),graceS=-frame.beat*frame.beatS;
  assert.ok(graceS>=4*.625 && graceS<5*.625,'four heard warning beats after a seek');
  assert.equal(machineryBeatAt(track,12+graceS-.001).safe,true);
  assert.equal(machineryBeatAt(track,12+graceS+.001).safe,false);
});

test('faster perfume still has a visible 600 ms rise and fade in song and silent solo play',()=>{
  for(const fallback of [false,true]) {
    const track={};setMachineryBeat(track,{enabled:true,fallback,songS:0,raceS:0,bpm:120});
    const phases=[];
    for(let i=0;i<6000;i++)phases.push(perfumePhase(row,i/1000,{},machineryBeatAt(track,i/1000)).phase);
    for(const phase of ['spray','fade'])assert.ok(phases.filter(p=>p===phase).length>=599,phase+' is readable');
    assert.ok(phases.includes('active')&&phases.includes('clear'));
  }
});

test('silent brush velocity and faster scan drawing remain the exact collision motion',()=>{
  const track={};setMachineryBeat(track,{enabled:true,fallback:true,raceS:0});
  const t=.71,e=.00001,pose=s=>mascaraPhase(row,s,machineryBeatAt(track,s)),p=pose(t);
  assert.ok(Math.abs((pose(t+e).x-pose(t-e).x)/(2*e)-p.vx)<1e-6);
  for(const music of [null,{song:{bpm:96,downbeatS:.2},songS:3.7}]) {
    const slow=scanClock(music,3.7),fast=scanClock(music,3.7,2),zone={offset:0,travel:3};
    assert.equal(fast.timeS,slow.timeS);
    assert.deepEqual(scanPose(zone,fast.timeS,fast.bpm),scanPose(zone,slow.timeS*2,slow.bpm));
    assert.notDeepEqual(scanPose(zone,fast.timeS,fast.bpm),scanPose(zone,slow.timeS,slow.bpm));
  }
  assert.deepEqual(scanClock({playing:false,song:{bpm:96},songS:8},3,2),{timeS:3,bpm:240},'stopped recording uses race time');
  assert.deepEqual(scanClock({playing:true,analysis:{bpm:99,downbeats:[.3]},songS:4},3,2),{timeS:3.7,bpm:198},'picked recording uses its measured beat');
});
