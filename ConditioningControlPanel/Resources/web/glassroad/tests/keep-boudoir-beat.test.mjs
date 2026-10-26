import test from 'node:test';
import assert from 'node:assert/strict';
import { keepGatePhase, createKeepGates } from '../sim/keepGates.js';
import { keepChainPhase, pendulumBeatPhase, createKeepChains, KEEP_CHAIN } from '../sim/keepChains.js';
import { mascaraPhase, mirrorGatePhase, powderPressPhase, ribbonPhase } from '../track/boudoir-setpieces.js';
import { setMachineryBeat, machineryBeatAt } from '../track/machineryBeat.js';
import { createBoudoirHazards } from '../sim/boudoirHazards.js';
import { fakeTrack, place, makeFeel, recorder } from './sim-fake.mjs';
const frame = (beat, bpm = 96.75, extra = {}) => ({ beat, beatS: 60 / bpm, safe: beat < 0, ...extra });
const brush = { id:'brush',kind:'sweeper',s:100,x:0,w:14,depthM:2.5,cycleS:6.4,offsetS:0,armM:8.6,pivotY:10.25,amplitudeM:2.9 };

test('presses and mirrored lanes change on whole beats and always leave a full open lane', () => {
  const row = { offsetS:0 };
  assert.equal(keepGatePhase(row,0,{},frame(.5)).phase,'warn');
  assert.equal(keepGatePhase(row,0,{},frame(.75)).phase,'lower');
  assert.equal(keepGatePhase(row,0,{},frame(1)).liftM,0);
  assert.equal(powderPressPhase(row,999,frame(1)).liftM,0);
  for(let beat=0;beat<80;beat+=.03) {
    const poses = [0,1,2].map(lane => mirrorGatePhase({offsetS:0,lane},0,frame(beat)));
    assert.ok(poses.some(p => p.liftM === 8.4));
  }
  assert.equal(ribbonPhase(row,0,frame(1)).direction,1);
  assert.equal(ribbonPhase(row,0,frame(-1,96.75,{rearming:true})).direction,0);
});
test('whole-beat pendulums retain bounded cadence and exact velocity derivatives', () => {
  for(const bpm of [40,96.75,128,240]) {
    const bs=60/bpm, period=4;
    for(let beat=.17;beat<period;beat+=.19) {
      const epsilon=.00001, p=mascaraPhase(brush,0,frame(beat,bpm)), a=mascaraPhase(brush,0,frame(beat-epsilon/bs,bpm)), b=mascaraPhase(brush,0,frame(beat+epsilon/bs,bpm));
      assert.ok(Math.abs((b.x-a.x)/(2*epsilon)-p.vx)<1e-6);
      assert.ok(Math.abs((b.y-a.y)/(2*epsilon)-p.vy)<1e-6);
      assert.ok(Math.abs(p.vx)<=Math.asin(2.9/8.6)*8.6*Math.PI*2/(period*bs)+1e-9);
    }
    assert.ok(Math.abs(mascaraPhase(brush,0,frame(period/2,bpm)).vx)<1e-10);
  }
});
test('seek rearm visibly opens gates, raises pendulums and lowers them with a bounded derivative', () => {
  const safe=frame(-2,96.75,{rearming:true});
  assert.equal(keepGatePhase({},0,{},safe).blocking,false);
  for(const lane of [0,1,2]) assert.equal(mirrorGatePhase({lane},0,safe).blocking,false);
  assert.ok(keepChainPhase({},0,safe).y-KEEP_CHAIN.radiusM>1.6);
  assert.ok(mascaraPhase(brush,0,safe).y-mascaraPhase(brush,0,safe).radiusM>1.6);
  for(let beat=.05;beat<8;beat+=.1) {
    const p=pendulumBeatPhase(brush,6.4,frame(beat,96.75,{rearming:true})); assert.ok(Math.abs(p.liftSpeed)<=2.5+1e-9);
    const epsilon=.00001, bs=60/96.75, a=mascaraPhase(brush,0,frame(beat-epsilon/bs,96.75,{rearming:true})), b=mascaraPhase(brush,0,frame(beat+epsilon/bs,96.75,{rearming:true}));
    assert.ok(Math.abs((b.y-a.y)/(2*epsilon)-mascaraPhase(brush,0,frame(beat,96.75,{rearming:true})).vy)<1e-6);
  }
});

test('initial pickup keeps pendulums at their authored height and continuous through GO with any offset', () => {
  const epsilon = .000001;
  for (const bpm of [40,96.75,240]) for (const offsetS of [0,1.4,-2.2,15.8]) {
    const row = {...brush,offsetS}, bs = 60/bpm;
    const a = mascaraPhase(row,0,frame(-epsilon/bs,bpm)), p = mascaraPhase(row,0,frame(0,bpm)), b = mascaraPhase(row,0,frame(epsilon/bs,bpm));
    assert.equal(a.pivotY,brush.pivotY); assert.equal(p.pivotY,brush.pivotY); assert.equal(b.pivotY,brush.pivotY);
    assert.ok(a.safe); assert.ok(!b.safe);
    assert.ok(Math.abs((b.x-a.x)/(2*epsilon)-p.vx)<1e-6);
    assert.ok(Math.abs((b.y-a.y)/(2*epsilon)-p.vy)<1e-6);
    const ca = keepChainPhase(row,0,frame(-epsilon/bs,bpm)), cb = keepChainPhase(row,0,frame(epsilon/bs,bpm));
    assert.equal(ca.pivotY,KEEP_CHAIN.pivotM); assert.equal(cb.pivotY,KEEP_CHAIN.pivotM);
    assert.ok(Math.hypot(cb.x-ca.x,cb.y-ca.y)<.001);
  }
});

test('seek release preserves pendulum position and velocity for offset swings', () => {
  const epsilon = .000001;
  for (const bpm of [40,96.75,240]) for (const offsetS of [0,1.4,-2.2,15.8]) {
    const row = {...brush,offsetS}, bs = 60/bpm, period = 4;
    const offset = Math.round(offsetS/bs), release = ((-offset%period)+period)%period;
    for (const boundary of [release,release+Math.max(2,Math.ceil(3/bs))]) {
      const poses = [-epsilon,0,epsilon].map(dt => mascaraPhase(row,0,frame(boundary+dt/bs,bpm,{rearming:true})));
      for (const p of poses) {
        assert.ok(Math.hypot(p.x-poses[1].x,p.y-poses[1].y)<.001);
        assert.ok(Math.hypot(p.vx-poses[1].vx,p.vy-poses[1].vy)<.001);
      }
    }
    const keepPeriod = 4, keepRelease = ((-offset%keepPeriod)+keepPeriod)%keepPeriod;
    const held = keepChainPhase(row,0,frame(keepRelease-epsilon,bpm,{rearming:true}));
    const moving = keepChainPhase(row,0,frame(keepRelease+epsilon,bpm,{rearming:true}));
    assert.ok(held.safe); assert.ok(!moving.safe);
    assert.ok(Math.hypot(moving.x-held.x,moving.y-held.y)<.001);
  }
});

test('mirror shutters lower continuously after pickup and seek without repeating the opening grace', () => {
  const bpm = 96.75, bs = 60/bpm, epsilon = .00001;
  for (const rearming of [false,true]) for (const offsetS of [0,1.4,-2.2,15.8]) {
    const offset = Math.round(offsetS/bs), release = rearming ? ((-offset%4)+4)%4 : 0;
    const poses = beat => [0,1,2].map(lane => mirrorGatePhase({lane,offsetS},0,frame(beat,bpm,{rearming})));
    const held = poses(release-epsilon), moving = poses(release+epsilon);
    for (let lane=0;lane<3;lane++) {
      assert.equal(held[lane].liftM,8.4);
      assert.ok(Math.abs(moving[lane].liftM-held[lane].liftM)<.00001);
      assert.ok(moving[lane].warning);
    }
    for (const beat of [release,release+.5,release+1,release+2-epsilon,release+2,release+16,release+32]) {
      assert.ok(poses(beat).some(p => p.liftM===8.4));
    }
    const ending = poses(release+2-epsilon), ended = poses(release+2+epsilon);
    for (let lane=0;lane<3;lane++) assert.ok(Math.abs(ending[lane].liftM-ended[lane].liftM)<.001);
    if (rearming) {
      const later = poses(release+16), closedLane = later[0].nextLane;
      assert.equal(later[closedLane].liftM,0);
      assert.equal(poses(release+17)[closedLane].liftM,0);
    }
  }
});
test('simulation inspection uses the same music pose without changing authored rows or online fallback', () => {
  const track=fakeTrack({w:36}), gate={id:'gate',s:100,w:14,offsetS:0}, chain={id:'chain',s:140,offsetS:0};
  const plan={presses:[{...gate,kind:'press',x:0,depthM:8}],sweepers:[brush],conveyors:[],mirrorGates:[]}, original=JSON.stringify(plan);
  const gates=createKeepGates(track,{gates:[gate]}), chains=createKeepChains(track,{chains:[chain]}), boudoir=createBoudoirHazards(track,plan);
  setMachineryBeat(track,{enabled:true,songS:9*60/96.75,raceS:1,bpm:96.75,offsetS:0});
  const beat=machineryBeatAt(track,1);
  assert.equal(gates.inspect(1).gates[0].liftM,keepGatePhase(gate,1,{},beat).liftM);
  assert.equal(chains.inspect(1).chains[0].x,keepChainPhase(chain,1,beat).x);
  assert.equal(boudoir.inspect(1).sweepers[0].heads[0].x,mascaraPhase(brush,1,beat).x);
  setMachineryBeat(track,null);
  assert.equal(gates.inspect(1).gates[0].liftM,keepGatePhase(gate,1).liftM);
  assert.equal(chains.inspect(1).chains[0].x,keepChainPhase(chain,1).x);
  assert.equal(boudoir.inspect(1).sweepers[0].heads[0].x,mascaraPhase(brush,1).x);
  assert.equal(JSON.stringify(plan),original);
});
test('physical gate contact follows the heard hold and opens through a music seek', () => {
  const track=fakeTrack({w:36}), gate={id:'gate',s:110,w:14,offsetS:0};
  const crossing = gates => {
    const r={i:0,st:place(track,{s:115,speed:40}),events:recorder(),feel:makeFeel()};
    return gates.apply(r,place(track,{s:105,speed:40}),.1);
  };
  setMachineryBeat(track,{enabled:true,songS:.55*60/96.75,raceS:.1,bpm:96.75});
  assert.equal(crossing(createKeepGates(track,{gates:[gate]})),'gate');
  setMachineryBeat(track,{enabled:true,songS:80,raceS:.1,bpm:96.75});
  assert.equal(crossing(createKeepGates(track,{gates:[gate]})),null);
});
