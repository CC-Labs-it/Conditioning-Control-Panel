import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { buildGoonworks } from '../dev/gen-goonworks.mjs';
import { GOONWORKS_PIECES, getGoonworksLayout, walkGoonworks } from '../track/goonworks-layout.js';
import { goonworksSetpieces } from '../track/goonworks-setpieces.js';
import { validate } from '../track/schema.js';
import { loadTrack } from '../track/load.js';
import { stoneIn } from '../track/courts.js';
import { makeRace } from './sim-racers-harness.mjs';
import { createItems } from '../sim/items.js';
import { createActions } from '../core/actions.js';
import { qfromBasis, qrotate } from '../core/math.js';
import { S, createState, setPos, setQuat, setVel } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { updateRespawn } from '../sim/respawn.js';
import { createProgress } from '../sim/progress.js';
import { registeredSkip } from '../sim/routes.js';
import { glideActive } from '../sim/glide.js';
import { calmMul } from '../sim/steer.js';
import { createGoonworksHazards } from '../sim/goonworksHazards.js';
import { makeFeel, place, acts, recorder, DT } from './sim-fake.mjs';
const json = JSON.parse(readFileSync(new URL('../tracks/goonworks.json', import.meta.url), 'utf8'));
const track = loadTrack(json), layout = getGoonworksLayout(track);
const padRuns = track.boosts.reduce((runs, pad, index) => {
  const last = runs.at(-1), previous = last?.at(-1);
  if (previous && pad.s - previous.s < 40 && pad.x === previous.x) last.push({ ...pad, index });
  else runs.push([{ ...pad, index }]);
  return runs;
}, []);

test('GOONWORKS is reproducible, closed, substantial and compact with a level eight-kart grid', () => {
  assert.deepEqual(buildGoonworks(), json);
  assert.deepEqual(validate(json), { ok: true, errors: [], warnings: [] });
  const end = walkGoonworks(GOONWORKS_PIECES);
  assert.ok(Math.hypot(end.x, end.z) < 1e-6);
  assert.equal(track.laps, 2); assert.ok(track.lengthM > 5500 && track.lengthM < 7000);
  assert.ok(layout.bounds.maxX-layout.bounds.minX < 1000);
  assert.ok(layout.bounds.maxZ-layout.bounds.minZ < 1000);
  assert.ok(json.nodes.every(n => n.w >= 32));
  for (let i=0;i<8;i++) assert.ok(Math.abs(track.spawnPose(i).p[1])<.001);
  assert.deepEqual(layout.sections.map(s=>s.id), ['start','rabbit-hole','screen-grotto','aperture-mill','pop-quiz','ribbon-helix','leapfrog','pinball-launch','grand-fold','finish']);
});

test('mechanics halls expose straight broad stations and no jump or stone gaps', () => {
  for (const [id, minWidth] of [['grotto-gallery',36],['mill-hall',44],['quiz-ballroom',44],['pinball-table',44]]) {
    const p=layout.byId[id], reference=track.frameAt(p.s0+20);
    for(let s=p.s0+20;s<p.s1-15;s+=5) {
      const f=track.frameAt(s);
      assert.ok(f.w>=minWidth-.01,id+' width'); assert.ok(Math.abs(f.p[1]-reference.p[1])<.01,id+' height');
      assert.ok(f.t.reduce((a,v,k)=>a+v*reference.t[k],0)>.999,id+' straight');
      assert.ok(track.query(...f.p,f.seg).onRoad,id+' supported');
    }
  }
});

test('boost chains offer supported side lines and a clear coast line with recovery space before toys and jumps', () => {
  assert.ok(json.boosts.length >= 20 && json.boosts.length <= 28, 'authored side-chain plan retained');
  assert.ok(track.boosts.length >= 18 && track.boosts.length <= 28, 'long-flight approaches retain the other boost choices');
  assert.ok(padRuns.length >= 7 && padRuns.every(run => run.length >= 2 && run.length <= 4));
  assert.ok(padRuns.some(run => run[0].x < 0) && padRuns.some(run => run[0].x > 0));
  const toys = goonworksSetpieces(track, layout);
  const approaches = [...toys.map(row => row.s - row.depth / 2), layout.field.s0, layout.launch.rampS0,
    ...track.routes.map(route => route.openS0)];
  const noPads = [layout.field, layout.byId['pinball-table'], layout.byId['pinball-ramp'], layout.byId['pinball-gap'],
    layout.byId['pinball-landing'], layout.byId['fold-descent'],
    ...['grotto-gallery', 'mill-hall', 'quiz-ballroom'].map(id => layout.byId[id])];
  for (const pad of track.boosts) {
    assert.ok(Math.abs(pad.x) - pad.w / 2 >= 2.5, 'central coast line');
    assert.ok(!noPads.some(range => pad.s < range.s1 && pad.s + pad.len > range.s0), 'no pad on a precision surface');
    for (const s of approaches) assert.ok((s - pad.s - pad.len + track.lengthM) % track.lengthM >= 120,
      'boost must expire before timed toys, the field, launch and shortcut lips');
    for (const ds of [0, pad.len / 2, pad.len]) for (const x of [pad.x - pad.w / 2, pad.x, pad.x + pad.w / 2]) {
      const f = track.surfaceAt(pad.s + ds, x), q = track.query(...f.p, f.seg);
      assert.ok(q.onRoad && Math.abs(q.height) < .025, 'pad follows its physical driving surface');
      assert.ok(q.edgeDist >= 6, 'room to recover outside the pad lane');
    }
  }
});

function drivePadRun(run, lateral) {
  const start = run[0].s - 12, st = place(track, { s: start, lateral, speed: 30 });
  const f = track.surfaceAt(start, lateral), feel = makeFeel(), events = recorder();
  setPos(st, f.p); setQuat(st, qfromBasis([0, 0, 0, 1], f.b, f.n, f.t.map(v => -v)));
  setVel(st, f.t.map(v => v * 30));
  const end = run.at(-1).s + run.at(-1).len + 15;
  for (let i = 0; i < 12 / DT && st[S.s] < end; i++) {
    const ahead = track.surfaceAt(st[S.s] + 12, lateral);
    const d = ahead.p.map((v, k) => v - st[S.posX + k]);
    const q = [st[S.quatX], st[S.quatY], st[S.quatZ], st[S.quatW]];
    const right = qrotate([0, 0, 0], q, [1, 0, 0]), forward = qrotate([0, 0, 0], q, [0, 0, -1]);
    const dot = a => a.reduce((v, n, k) => v + n * d[k], 0);
    const steer = Math.max(-1, Math.min(1, Math.atan2(dot(right), Math.max(.1, dot(forward))) *
      feel.get('ai.steerGain') / Math.max(.2, calmMul(st[S.engineV], feel))));
    step(st, acts({ throttle: 1, steer }), track, feel, DT, events);
    updateRespawn(st, track, feel, DT, events);
  }
  assert.ok(st[S.s] >= end, 'chain drive reaches its exit');
  assert.equal(events.all('drop').length, 0); assert.equal(events.all('wall').length, 0);
  return events.all('pad').map(event => event.payload.index);
}

test('every side-lane chain pays consecutive physical boosts while the centre remains a usable bypass', () => {
  for (const run of padRuns) {
    assert.deepEqual(drivePadRun(run, run[0].x), run.map(pad => pad.index), 'all pads pay on the chosen line');
    assert.deepEqual(drivePadRun(run, 0), [], 'centre can pass the full chain without a forced boost');
  }
});

test('boost recovery leaves physical braking distance before the earliest timed obstacle', () => {
  const pad = track.boosts.at(-1), st = place(track, { s: pad.s - .1, lateral: pad.x, speed: 39 });
  const feel = makeFeel(), events = recorder();
  for (let i = 0; i < 6 / DT; i++) {
    step(st, acts({ brake: 1 }), track, feel, DT, events);
    if (events.all('pad').length && st[S.engineV] <= 21) break;
  }
  assert.equal(events.all('pad').length, 1);
  assert.ok(st[S.engineV] <= 21, 'full braking can recover from the complete pad envelope');
  const recoverM = st[S.s] - pad.s - pad.len;
  assert.ok(recoverM > 90, 'the test includes the boost floor that persists under braking');
  const hazards = goonworksSetpieces(track, layout);
  for (const boost of track.boosts) for (const row of hazards) {
    const clear = (row.s - row.depth / 2 - boost.s - boost.len + track.lengthM) % track.lengthM;
    assert.ok(clear > recoverM + 25, 'at least 25 m of reaction space remains after braking to toy speed');
  }
});

test('ribbon braid makes two real turns across three visible heights', () => {
  const r=layout.byId['ribbon-braid'], length=r.s1-r.s0;
  assert.equal(GOONWORKS_PIECES.find(p=>p.id==='ribbon-braid').turn,720);
  for(let s=r.s0+40;s<r.s0+length/2-40;s+=10) {
    const a=track.frameAt(s),b=track.frameAt(s+length/2);
    assert.ok(Math.hypot(a.p[0]-b.p[0],a.p[2]-b.p[2])<.5);
    assert.ok(b.p[1]-a.p[1]>31.5);
    const q=track.query(...a.p,a.seg); assert.ok(q.onRoad&&Math.abs(q.s-s)<.05);
  }
});

test('leapfrog supplies five broad landing decks with readable jump gaps and supported endpoints', () => {
  const f=track.fields[0]; assert.equal(f.stones.length,5); assert.equal(f.platforming,true); assert.ok(Math.abs(f.toS-f.fromS-200)<.01);
  for(const x of f.lanes) {
    assert.ok(stoneIn(f,f.fromS+.1,x)); assert.ok(stoneIn(f,f.toS-.1,x));
    for(let i=1;i<f.stones.length;i++) {
      const a=f.stones[i-1],b=f.stones[i],gap=b.s-a.s-a.len;
      assert.ok(gap>9.98&&gap<10.02); assert.equal(stoneIn(f,a.s+a.len+gap/2,x),null);
    }
  }
  assert.ok(f.stones.every(s=>s.w===30&&s.len>26.8));
  assert.equal(track.jumps.length,1); assert.ok(track.nodeFlags(layout.byId['pinball-gap'].node).includes('glide'));
});

function pinballFlight({ speed = 30, held = true, release = Infinity } = {}) {
  const launch = layout.launch, st = place(track, { s: launch.rampS0 - 25, speed });
  const feel = makeFeel(), events = recorder();
  let launched = false, launchS = 0, airtime = 0, activeTicks = 0, releaseChecked = false;
  for (let tick = 0; tick < 600; tick++) {
    const hop = held && airtime < release;
    const previousAir = st[S.airborne];
    step(st, acts({ throttle: 1, hop }), track, feel, DT, events);
    updateRespawn(st, track, feel, DT, events);
    assert.equal(events.all('drop').length, 0, 'Pinball flight stays above a physical landing');
    if (!launched && !previousAir && st[S.airborne]) { launched = true; launchS = st[S.s]; }
    if (!launched) continue;
    airtime += DT;
    if (glideActive(st)) activeTicks++;
    if (!hop && release < Infinity) { assert.equal(glideActive(st), false, 'release ends lift immediately'); releaseChecked = true; }
    if (!st[S.airborne] || events.all('land').length) return { st, airtime, activeTicks, releaseChecked, distance: st[S.s] - launchS };
  }
  assert.fail('Pinball flight did not land');
}

test('holding jump deploys the Pinball rotor and releasing it gives an earlier physical landing', () => {
  for (const speed of [12, 30, 40]) {
    const normal = pinballFlight({ speed, held: false }), held = pinballFlight({ speed });
    const released = pinballFlight({ speed, release: .65 });
    assert.ok(held.activeTicks > 20, 'held rotor deployed at speed ' + speed);
    assert.ok(held.distance > normal.distance + 8, 'rotor extends the normal jump');
    assert.ok(held.airtime > normal.airtime + .3, 'rotor slows the descent');
    assert.ok(released.releaseChecked && released.distance < held.distance - 8, 'released rotor lands sooner');
    assert.ok(held.st[S.s] >= layout.launch.landingS0 && held.st[S.s] < layout.launch.landingS1 + 35);
    assert.equal(glideActive(held.st), false, 'landing closes the rotor');
  }
});

test('Pinball rotor cannot rearm in the same flight or turn an ordinary hop into a glide', () => {
  const feel = makeFeel(), events = recorder(), st = place(track, { s: layout.launch.rampS1 - 2, speed: 30 });
  for (let i = 0; i < 60 && !glideActive(st); i++) step(st, acts({ throttle: 1, hop: true }), track, feel, DT, events);
  assert.equal(glideActive(st), true);
  step(st, acts({ throttle: 1, hop: false }), track, feel, DT, events);
  for (let i = 0; i < 20; i++) {
    step(st, acts({ throttle: 1, hop: true }), track, feel, DT, events);
    assert.equal(glideActive(st), false);
  }
  const ordinary = place(track, { s: 40, speed: 30 });
  for (let i = 0; i < 50; i++) {
    step(ordinary, acts({ throttle: 1, hop: true, hopPressed: i === 0 }), track, feel, DT, events);
    assert.equal(glideActive(ordinary), false);
  }
});

test('all nonadjacent road crossings clear the full banked driving width', () => {
  const frames=[];for(let s=0;s<track.lengthM;s+=8)frames.push({s,f:track.frameAt(s)});
  const clashes=[];let crossings=0;
  for(let i=0;i<frames.length;i++)for(let j=i+20;j<frames.length;j++) {
    const a=frames[i],b=frames[j];if(track.lengthM-b.s+a.s<160)continue;
    if(Math.hypot(a.f.p[0]-b.f.p[0],a.f.p[2]-b.f.p[2])>(a.f.w+b.f.w)/2)continue;
    const clear=Math.abs(a.f.p[1]-b.f.p[1])-Math.abs(a.f.b[1])*a.f.w/2-Math.abs(b.f.b[1])*b.f.w/2;
    if(clear<10)clashes.push([a.s,b.s,+clear.toFixed(1)]);crossings++;
  }
  assert.deepEqual(clashes.slice(0,20),[]);assert.ok(crossings>50);
});

test('all eight racers finish the boosted two-lap course with active toys, items and no rescues', () => {
  const r=makeRace({track,seed:20261024,items:createItems}), hazards=createGoonworksHazards(track,layout);
  const drive=r.ai.drive.bind(r.ai), racersStep=r.racers.step.bind(r.racers);
  r.ai.drive=(racer,ctx,dt,actions)=>{drive(racer,ctx,dt,actions);if(ctx.race.phase==='racing'||ctx.race.phase==='finished')hazards.steer(racer,actions);};
  r.racers.step=(dt,actions,race,ctx)=>racersStep(dt,actions,race,{...ctx,hazards});
  r.run(820,()=>r.racers.list.every(v=>v.finished));
  assert.ok(r.racers.list.every(v=>v.finished),'finish times '+r.racers.list.map(v=>v.finishS));
  assert.deepEqual(r.racers.list.map(v=>v.falls),Array(8).fill(0));
  assert.ok(r.events('pad').length+r.botEvents('pad').length>=20,'physical pads pay during the full race');
});

function driveCut(route, speed, yaw) {
  const st=createState(),s=route.openS0+3,f=track.frameAt(s),side=route.side==='L'?-1:1,lat=side*(f.w/2-4);
  const c=Math.cos(yaw*Math.PI/180),sn=Math.sin(yaw*Math.PI/180)*side;
  const t=f.t.map((v,k)=>v*c+f.b[k]*sn),b=f.b.map((v,k)=>v*c-f.t[k]*sn);
  setPos(st,f.p.map((v,k)=>v+f.b[k]*lat));setQuat(st,qfromBasis([0,0,0,1],b,f.n,t.map(v=>-v)));setVel(st,t.map(v=>v*speed));
  st[S.engineV]=speed;st[S.grounded]=1;st[S.seg]=st[S.latchSeg]=f.seg;st[S.s]=s;st[S.lateral]=lat;st[S.hopT]=1000;
  const events=[],sink={emit(name,data){events.push({name,...data});}},feel=makeFeel(),actions=createActions(),progress=createProgress(track,sink),race={phase:'racing',timeS:0};
  progress.reset(st);st[S.checkpoint]=track.checklines.filter(c=>c.s<=s).length-1;
  for(let i=0;i<7/DT;i++) {
    actions.throttle=1;step(st,actions,track,feel,DT,sink);updateRespawn(st,track,feel,DT,sink);race.timeS+=DT;progress.update(st,race);
    if(events.some(e=>e.name==='route-skip'||e.name==='drop'))break;
  }
  return {events,st};
}
test('both Grand Fold cuts land physically before receiving shortcut progress',()=>{
  assert.equal(track.routes.length,2);
  for(const route of track.routes)for(const [speed,yaw] of [[24,25],[30,30],[34,25]]) {
    const {events,st}=driveCut(route,speed,yaw),land=events.find(e=>e.name==='route-land');
    assert.ok(land,JSON.stringify({route:route.id,speed,yaw,s:st[S.s],events:events.filter(e=>/route|drop|fall|wall/.test(e.name))}));
    assert.ok(events.findIndex(e=>e.name==='route-skip')>events.findIndex(e=>e.name==='route-land'));
    assert.ok(!events.some(e=>e.name==='drop'));assert.ok(land.skipM>450&&land.skipM<750);
    assert.equal(registeredSkip(track,route.openS0,land.s)?.id,route.id);
  }
});

test('Rabbit Hole uses physically curved support matching both visible banks',()=>{
  const tube=layout.tube;assert.equal(typeof track.canalProfile,'function');
  for(const s of [tube.s0,tube.s1])assert.ok(Math.abs(track.canalProfile(s,16,{}).height)<.001);
  let checked=0;
  for(let s=tube.s0+45;s<tube.s1-45;s+=12)for(const x of [-16,-10,10,16]) {
    const f=track.surfaceAt(s,x),q=track.query(...f.p,f.seg);
    assert.ok(q.onRoad);assert.ok(Math.abs(q.height)<.025,'curved support '+s+'/'+x+': '+q.height);
    assert.ok(q.normal.reduce((v,n,k)=>v+n*f.n[k],0)>.999);
  }
  const mesh=track.meshes.road;
  for(let i=0;i<mesh.positions.length/3;i++) {
    const s=mesh.uvs[i*2+1]%track.lengthM,x=mesh.uvs[i*2];
    if(s<tube.s0+45||s>tube.s1-45||Math.abs(x)<8)continue;
    const f=track.frameAt(s),j=i*3,up=f.n.reduce((v,n,k)=>v+n*mesh.normals[j+k],0);
    if(up<.2)continue;
    const q=track.query(...mesh.positions.slice(j,j+3),f.seg);
    assert.ok(q.onRoad||q.edgeDist>-.002);assert.ok(Math.abs(q.height)<.025,'bank mesh gap '+q.height);checked++;
  }
  assert.ok(checked>50);assert.ok(track.canalProfile((tube.s0+tube.s1)/2,16,{}).height>7.9);
});
