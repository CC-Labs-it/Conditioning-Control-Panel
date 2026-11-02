import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { forkFixture, placeOnFork } from './forks-fixture.mjs';
import { makeFeel, place, recorder, acts, DT } from './sim-fake.mjs';
import { loadTrack } from '../track/load.js';
import { validate } from '../track/schema.js';
import { makeQuery } from '../track/query.js';
import { S, RESPAWN } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { updateRespawn } from '../sim/respawn.js';
import { createAi } from '../sim/ai.js';
import { createProgress } from '../sim/progress.js';
import { lipAhead } from '../sim/trick.js';
import { routeOf } from '../sim/routes.js';
import { fallBeneath } from '../sim/falls.js';
import { makeRace } from './sim-racers-harness.mjs';

const fixture = forkFixture(), course = loadTrack(JSON.parse(readFileSync(new URL('../tracks/boudoir.json', import.meta.url), 'utf8')));
const xyz = st => [st[S.posX], st[S.posY], st[S.posZ]];
const distance = (a,b) => Math.hypot(...a.map((v,i)=>v-b[i]));

test('Fork schema rejects malformed roads and allows optional omission', () => {
  const { json } = fixture;
  assert.ok(validate(json).ok);
  for (const mutate of [r=>r.to=r.from, r=>r.points.pop(), r=>r.points[1].w=2,
    r=>r.points[1].flags=['magnet'], r=>r.points[1].flow=[20,0], r=>r.points[2].p[1]=NaN]) {
    const copy=structuredClone(json); mutate(copy.forks[0]); assert.equal(validate(copy).ok,false);
  }
  const copy=structuredClone(json); delete copy.forks;
  assert.ok(validate(copy).ok); assert.equal(loadTrack(copy).forks,undefined);
});

test('Open forks have continuous independent surface frames, encoded segments and monotonic lap maps', () => {
  for (const track of [fixture.track,course]) for (const f of track.forks) {
    assert.ok(distance(f.frameAt(0).p,track.frameAt(f.fromS).p)<.01);
    assert.ok(distance(f.frameAt(f.lengthM).p,track.frameAt(f.toS).p)<.01);
    assert.ok(distance(f.frameAt(0).n,track.frameAt(f.fromS).n)<.01);
    assert.ok(distance(f.frameAt(f.lengthM).n,track.frameAt(f.toS).n)<.01);
    let prev=-Infinity;
    for(const frame of f.samples(3)) {
      assert.ok(frame.s>prev); prev=frame.s;
      assert.ok(Math.abs(f.localS(frame.s)-frame.localS)<1e-7);
      assert.equal(track.forkAtSeg(frame.seg),f);
      assert.ok(distance(track.pathFrameAt(frame.s,frame.seg).p,frame.p)<1e-7);
      const q=f.query(...frame.p,-1,makeQuery());
      assert.ok(q.onRoad && Math.abs(q.height)<.03 && Math.abs(q.lateral)<.03);
      assert.ok(Math.abs(q.forkS-frame.localS)<.04);
      assert.equal(q.surface,frame.surface);
    }
  }
});

test('A branch opens the real main rails at both flat junctions and retains its own rails', () => {
  const {track,fork}=fixture;
  assert.ok(track.forkPortals.length>=2);
  for(const portal of track.forkPortals) {
    const s=(portal.s0+portal.s1)/2,f=track.frameAt(s),p=f.p.map((v,i)=>v+f.b[i]*portal.side*f.w/2);
    assert.ok(track.mainRailOpen(s,portal.side));
    const q=track.query(...p,f.seg,makeQuery()); assert.ok(q.forkId || !q.railed);
  }
  const f=fork.frameAt(fork.lengthM/2),q=fork.query(...f.p.map((v,i)=>v+f.b[i]*(f.w/2-.1)),-1,makeQuery());
  assert.ok(q.railed && q.onRoad);
});

function driveFork(track,fork) {
  const feel=makeFeel(), events=recorder(), st=place(track,{s:fork.fromS-40,speed:24});
  const ai=createAi(track,feel,{seed:1,forkChoice:'all'}),r={i:0,st,feel,events,meta:{isBot:false}};
  const race={phase:'racing',timeS:0,lapStartS:0,finish(){}},a=acts(),progress=createProgress(track,events);progress.reset(st);
  st[S.checkpoint]=Math.max(0,track.checklines.findLastIndex(c=>c.s<=st[S.s]));
  let used=false,maxStep=0,travel=0,maxHeight=0,steps=0,maxProgressStep=0,off=0;
  for(;steps<120*60;steps++) {
    const p=xyz(st),s=st[S.s]; ai.drive(r,{race,racers:[r]},DT,a);step(st,a,track,feel,DT,events);updateRespawn(st,track,feel,DT,events);progress.update(st,race);race.timeS+=DT;
    const d=distance(p,xyz(st));maxStep=Math.max(maxStep,d);travel+=d;maxProgressStep=Math.max(maxProgressStep,st[S.s]-s);
    if(track.forkAtSeg(st[S.seg])){used=true;maxHeight=Math.max(maxHeight,Math.abs(st[S.height]));if(!st[S.grounded])off++;}
    if(used && !track.forkAtSeg(st[S.seg]) && st[S.s]>fork.toS+20)break;
  }
  return {st,events,used,maxStep,maxProgressStep,travel,maxHeight,steps,off,debug:ai.botOf(0).why};
}
for(const [track,fork] of [[fixture.track,fixture.fork],...course.forks.map(f=>[course,f])]) test('Real steering enters, drives and rejoins '+fork.id,()=>{
  const run=driveFork(track,fork),detail=JSON.stringify({...run,st:{s:run.st[S.s],seg:run.st[S.seg],lat:run.st[S.lateral]},events:run.events.names()});
  assert.ok(run.used,detail);assert.ok(run.steps<120*60,detail);
  assert.equal(run.events.all('rescue-pull').length,0,detail);
  assert.ok(run.maxStep<1.5,detail);assert.ok(run.maxProgressStep<30,detail);
  assert.ok(run.travel>fork.lengthM*.94,detail);
  assert.ok(run.maxHeight<1,detail);
  assert.equal(run.events.all('route-skip').length,0,detail);
});

test('Falling off an open branch rescues to its own earlier anchor',()=>{
  const{track,fork}=forkFixture({rail:false,raised:18}),st=placeOnFork(fork,{localS:fork.lengthM*.45,lateral:14,airborne:true,height:-2});
  const feel=makeFeel(),events=recorder(),a=acts();let pulled=false;
  const oldS=st[S.s], progress=createProgress(track,events),race={phase:'racing',timeS:0,lapStartS:0,finish(){}};
  progress.reset(st);st[S.checkpoint]=1;
  for(let i=0;i<600;i++) {step(st,a,track,feel,DT,events);updateRespawn(st,track,feel,DT,events);progress.update(st,race);pulled||=st[S.respawnPhase]===RESPAWN.PULL;if(pulled&&st[S.respawnPhase]===RESPAWN.GRACE)break;}
  assert.ok(pulled); assert.equal(track.forkAtSeg(st[S.seg]),fork);assert.ok(st[S.s]<=oldS);
  const q=track.query(...xyz(st),st[S.seg],makeQuery());assert.ok(q.onRoad);assert.ok(Math.abs(q.height)<.05);
  assert.equal(st[S.checkpoint],1);assert.equal(events.all('checkpoint').length,0);assert.equal(events.all('route-skip').length,0);
});

test('A lower main deck is not mistaken for an adjacent encoded branch segment',()=>{
  const {track,fork}=fixture, st=placeOnFork(fork),events=recorder(),f=track.frameAt(track.nodeS[1]);
  // The first encoded fork segments alias main node 1 under the old modulo check.
  st[S.seg]=fork.baseSeg+1;st[S.latchSeg]=st[S.seg];
  assert.ok(fallBeneath(track,st,f.p.map((v,i)=>v+(i===1?4:0)),makeQuery(),events,true));
  assert.equal(events.all('fall').length,1);
});

test('Branch hops ignore mapped main jump and boost triggers, and never latch a main drop route',()=>{
  const{track,fork}=fixture,st=placeOnFork(fork);
  track.boosts=[{s:st[S.s]+1,x:0,w:20}];track.jumps=[{s:st[S.s]+1}];
  track.routes=[{from:0,to:track.nodeCount}];
  assert.equal(lipAhead(track,st[S.s],0,3,makeFeel(),st[S.seg]),null);
  assert.equal(routeOf(track,st),null);
  track.boosts=[];track.jumps=[];track.routes=[];
});

test('Eight real bots finish two laps with deterministic branch choice',()=>{
  const h=makeRace({track:fixture.track,seed:42});const entered=new Set();
  h.run(180,()=>{for(const r of h.racers.list)if(h.track.forkAtSeg(r.st[S.seg]))entered.add(r.i);return h.racers.list.every(r=>r.finished);});
  assert.ok(entered.size>=2);assert.ok(h.racers.list.every(r=>r.finished));
  assert.deepEqual(h.racers.list.map(r=>r.falls),Array(8).fill(0));
});
