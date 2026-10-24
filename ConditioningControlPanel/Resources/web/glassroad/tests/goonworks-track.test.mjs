import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { buildGoonworks } from '../dev/gen-goonworks.mjs';
import { GOONWORKS_PIECES, getGoonworksLayout, walkGoonworks } from '../track/goonworks-layout.js';
import { validate } from '../track/schema.js';
import { loadTrack } from '../track/load.js';
import { stoneIn } from '../track/courts.js';
import { makeRace } from './sim-racers-harness.mjs';
import { createItems } from '../sim/items.js';
import { createActions } from '../core/actions.js';
import { qfromBasis } from '../core/math.js';
import { S, createState, setPos, setQuat, setVel } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { updateRespawn } from '../sim/respawn.js';
import { createProgress } from '../sim/progress.js';
import { registeredSkip } from '../sim/routes.js';
import { makeFeel, DT } from './sim-fake.mjs';
const json = JSON.parse(readFileSync(new URL('../tracks/goonworks.json', import.meta.url), 'utf8'));
const track = loadTrack(json), layout = getGoonworksLayout(track);

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

test('leapfrog supplies ten broad landing decks with real small gaps and supported endpoints', () => {
  const f=track.fields[0]; assert.equal(f.stones.length,10); assert.ok(f.toS-f.fromS>299);
  for(const x of f.lanes) {
    assert.ok(stoneIn(f,f.fromS+.1,x)); assert.ok(stoneIn(f,f.toS-.1,x));
    for(let i=1;i<f.stones.length;i++) {
      const a=f.stones[i-1],b=f.stones[i],gap=b.s-a.s-a.len;
      assert.ok(gap>1.98&&gap<2.02); assert.equal(stoneIn(f,a.s+a.len+gap/2,x),null);
    }
  }
  assert.ok(f.stones.every(s=>s.w===30&&s.len>28));
  assert.equal(track.jumps.length,1); assert.ok(track.nodeFlags(layout.byId['pinball-gap'].node).includes('glide'));
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

test('all eight racers finish the two-lap course with items and no rescues', () => {
  const r=makeRace({track,seed:20261024,items:createItems});
  r.run(820,()=>r.racers.list.every(v=>v.finished));
  assert.ok(r.racers.list.every(v=>v.finished),'finish times '+r.racers.list.map(v=>v.finishS));
  assert.deepEqual(r.racers.list.map(v=>v.falls),Array(8).fill(0));
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
