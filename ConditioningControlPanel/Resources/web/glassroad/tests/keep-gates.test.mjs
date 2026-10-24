import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createKeepGates, keepGatePhase, gateSweep, KEEP_GATE } from '../sim/keepGates.js';
import { S, RESPAWN, cloneState } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { createAi } from '../sim/ai.js';
import { createContact } from '../sim/contact.js';
import { createRacers } from '../sim/racers.js';
import { updateRespawn } from '../sim/respawn.js';
import { loadTrack } from '../track/load.js';
import { getKeepLayout } from '../track/keep-layout.js';
import { fakeTrack, makeFeel, place, acts, recorder, DT } from './sim-fake.mjs';

const track = fakeTrack({w:26}), gate = {id:'test',s:110,x:0,w:14};
const build = () => createKeepGates(track,{gates:[gate]});
function racerAt(s,lateral=0,height=0) {
  return {i:1,kind:'local',finished:false,st:place(track,{s,lateral,height,speed:30}),feel:makeFeel(),events:recorder()};
}
function crossing({x=0,time=6,h=0,from=105,to=115}={}) {
  const gates=build(),r=racerAt(to,x,h),prev=place(track,{s:from,lateral:x,height:h,speed:30});
  return {gates,r,hit:gates.apply(r,prev,time)};
}

test('gate clock is repeatable, frozen when time is frozen and exposes warning before every lower',()=>{
  assert.equal(keepGatePhase(gate,0).phase,'clear');
  assert.equal(keepGatePhase(gate,3.2).phase,'warn');
  assert.equal(keepGatePhase(gate,5.2).phase,'lower');
  assert.equal(keepGatePhase(gate,6.4).phase,'hold');
  assert.equal(keepGatePhase(gate,7.7).phase,'rise');
  for(const t of [0,3.2,5.2,6.4,7.7]) {
    const a=keepGatePhase(gate,t),b=keepGatePhase(gate,t+KEEP_GATE.cycleS);
    assert.equal(a.phase,b.phase);assert.ok(Math.abs(a.liftM-b.liftM)<1e-10);
    assert.deepEqual(keepGatePhase(gate,t),a);
  }
});

test('swept rectangle catches fast/diagonal contacts but preserves both wide bypasses',()=>{
  assert.notEqual(gateSweep([0,-10],[0,10],7.65,1.2),null);
  assert.notEqual(gateSweep([-10,-3],[10,3],7.65,1.2),null);
  assert.equal(gateSweep([10,-10],[10,10],7.65,1.2),null);
  assert.equal(crossing().hit,'test');
  for(const x of [-10,10])assert.equal(crossing({x}).hit,null);
  const {r}=crossing();assert.ok(r.st[S.s]<110);assert.ok([...r.st].every(Number.isFinite));
  assert.equal(r.events.all('keep-gate-hit').length,1);
  assert.equal(r.st[S.stalled],Math.fround(.4));
});

test('open clearance, high/under-road travel, remote racers, finish and rescue do not collide',()=>{
  for(const options of [{time:0},{time:4},{h:10},{h:-4}])assert.equal(crossing(options).hit,null);
  for(const mutate of [r=>r.finished=true,r=>r.kind='remote',r=>r.st[S.respawnPhase]=RESPAWN.PULL]) {
    const g=build(),r=racerAt(115),p=place(track,{s:105});mutate(r);
    assert.equal(g.apply(r,p,6),null);
  }
});

test('lowering does not crush or teleport a racer already below the gate',()=>{
  const g=build(),r=racerAt(110.8),p=place(track,{s:110,speed:30}),before=[...r.st];
  assert.equal(g.apply(r,p,6),null);assert.deepEqual([...r.st],before);
});

test('sustained throttle cannot tunnel through hold during the event cooldown',()=>{
  const g=build(),r=racerAt(108.4),a=acts({throttle:1});
  let furthest=0;
  for(let i=0;i<1.15/DT;i++){
    const p=cloneState(r.st);step(r.st,a,track,r.feel,DT,r.events);g.apply(r,p,6+i*DT);
    furthest=Math.max(furthest,r.st[S.s]);
  }
  assert.ok(furthest<110, 'closed gate blocked at '+furthest);
  assert.ok(r.events.all('keep-gate-hit').length<=2,'bounded event repeats');
});

test('rear contact after gate resolution cannot push a stopped kart through a closed grille',()=>{
  const g=build(),r=racerAt(109.1),behind=racerAt(107.8),a=acts({throttle:1});
  r.i=0;r.fxv={};behind.i=1;behind.fxv={};
  const contact=createContact(track,r.feel,r.events),p=place(track,{s:108.4,speed:30});
  assert.equal(g.apply(r,p,6),'test');
  contact.update(DT,[r,behind],{phase:'racing',timeS:6});
  const pushed=track.query(r.st[S.posX],r.st[S.posY],r.st[S.posZ]);
  assert.ok(pushed.s>108.8,'real rear contact pushed the stopped kart inside the expanded footprint');
  let furthest=0;
  for(let i=1;i<69;i++){
    const previous=cloneState(r.st);step(r.st,a,track,r.feel,DT,r.events);g.apply(r,previous,6+i*DT);
    furthest=Math.max(furthest,r.st[S.s]);
  }
  assert.ok(furthest<108.8,'every resolved tick stays ahead of the closed grille: '+furthest);
  assert.ok(r.events.all('keep-gate-hit').length<=2,'contact still observes event cooldown');
});

test('a kart that entered while open still leaves freely when the gate lowers around it',()=>{
  const g=build(),r=racerAt(110),before=place(track,{s:108,speed:30});
  assert.equal(g.apply(r,before,5.4),null);
  for(let i=1;i<=48;i++)assert.equal(g.apply(r,cloneState(r.st),5.4+i*DT),null);
  const previous=cloneState(r.st);r.st.set(place(track,{s:112,speed:30}));
  assert.equal(g.apply(r,previous,6.2+DT),null);
  assert.equal(r.st[S.s],112);assert.equal(r.events.all('keep-gate-hit').length,0);
});
test('reset clears contact memory and repeated runs yield the same collision result',()=>{
  const g=build();let first;
  for(let k=0;k<2;k++){
    const r=racerAt(115),p=place(track,{s:105,speed:30});g.apply(r,p,6);
    if(k===0)first=[...r.st];else assert.deepEqual([...r.st],first);
    assert.equal(g.inspect(6).hits,1);g.reset();assert.equal(g.inspect(6).hits,0);
  }
  assert.throws(()=>createKeepGates(track,{gates:[{...gate,x:3}]}),/bypasses/);
});

test('the eight real bots finish Keep with gates active and held safe lanes through both halls',()=>{
  const tr=loadTrack(JSON.parse(readFileSync(new URL('../tracks/keep.json',import.meta.url),'utf8')));
  const layout=getKeepLayout(tr), rows=layout.gates || layout.halls.flatMap((h,j)=>h.gates.map((s,i)=>({id:h.id+i,s,w:14,x:0,offsetS:j+i*.8})));
  const gates=createKeepGates(tr,{gates:rows}),feel=makeFeel(),events=recorder();
  const rs=createRacers(tr,feel,events,{count:8,seed:241024}),ai=createAi(tr,feel,{seed:241024});
  const race={phase:'racing',timeS:0,lapStartS:0}, seen=new Set();
  for(const r of rs.list)r.raceView={phase:'racing',timeS:0,lapStartS:0,finish(){this.phase='finished';}};
  for(let tick=0;tick<60*260&&!rs.list.every(r=>r.finished);tick++){
    race.timeS+=DT;
    for(const r of rs.list){
      if(r.finished)continue;
      ai.drive(r,{race,racers:rs.list},DT,r.input);gates.steer(r,r.input,race.timeS);
      r.prev.set(r.st);step(r.st,r.input,tr,r.feel,DT,r.events);gates.apply(r,r.prev,race.timeS,r.events);
      updateRespawn(r.st,tr,r.feel,DT,r.events);r.raceView.timeS=race.timeS;r.progress.update(r.st,r.raceView);
      for(const row of rows)if(Math.abs(r.st[S.s]-row.s)<2){
        seen.add(r.i+':'+row.id);assert.ok(Math.abs(r.st[S.lateral])>row.w/2+.65, 'bot kept clear '+r.i);
      }
    }
  }
  assert.equal(seen.size,48,'all eight pass all six gates');
  for(const r of rs.list){assert.equal(r.finished,true,'finished '+r.i);assert.equal(r.falls,0,'falls '+r.i);}
});
