import { test } from 'node:test';
import assert from 'node:assert/strict';
import { S, RESPAWN, BOOST } from '../sim/state.js';
import { createContact } from '../sim/contact.js';
import { createFeelOverlay, identityMods } from '../sim/racers.js';
import { makeFeel, fakeTrack, place, recorder, DT, run, acts } from './sim-fake.mjs';

// a racer stand-in: what contact reads (st, feel, mods, fxv, events)
function racer(i, track, feel, opts) {
  const ev = recorder();
  return { i, st: place(track, opts), feel: createFeelOverlay(feel), mods: identityMods(), fxv: { ghost: false, slip: 0 }, events: ev };
}
const RACING = { phase: 'racing', timeS: 10 };
const quat = (st) => [st[S.quatX], st[S.quatY], st[S.quatZ], st[S.quatW]];

for (const kind of ['glass','golden']) for (const reverse of [false,true]) test(`bubble ram ${kind}, reversed=${reverse}: launches the victim and preserves the holder`,()=>{
  const feel=makeFeel(), tr=fakeTrack({flags:()=>[]}), rec=recorder(), c=createContact(tr,feel,rec);
  const a=racer(0,tr,feel,{s:100,lateral:5.2,speed:20}), b=racer(1,tr,feel,{s:100,lateral:6,speed:20});
  a.fxv.shield=kind;
  c.update(DT,reverse?[b,a]:[a,b],RACING);
  assert.equal(a.st[S.airborne],0); assert.equal(a.st[S.engineV],20);
  assert.equal(b.st[S.airborne],1); assert.equal(b.st[S.grounded],0); assert.ok(b.st[S.velY]>=6.49);
  const f=tr.frameAt(100,{}), lateral=b.st[S.velX]*f.b[0]+b.st[S.velZ]*f.b[2];
  assert.ok(lateral>=17.99);
  assert.equal(rec.all('bubble-ram').length,1); assert.equal(rec.first('bubble-ram').payload.victim,1);
  run(b.st,tr,feel,acts({throttle:1}),2,rec,st=>st[S.respawnPhase]>0);
  assert.ok(b.st[S.respawnPhase]>0,'the actual flight can crash beyond an open edge');
});

test('bubble ram cooldown, paired shields, ghost protection and pause',()=>{
  const feel=makeFeel(),tr=fakeTrack(),rec=recorder(),c=createContact(tr,feel,rec);
  const a=racer(0,tr,feel,{lateral:0,speed:20}),b=racer(1,tr,feel,{lateral:1,speed:20});
  a.fxv.shield='glass'; b.fxv.shield='golden';
  const initialA=a.st.slice(), initialB=b.st.slice();
  c.update(DT,[a,b],{phase:'paused',timeS:10}); assert.equal(rec.all('bubble-ram').length,0);
  c.update(DT,[a,b],{phase:'racing',timeS:0}); assert.equal(rec.all('bubble-ram').length,0);
  c.update(DT,[a,b],RACING); assert.equal(a.st[S.airborne],1); assert.equal(b.st[S.airborne],1);
  a.st.set(initialA); b.st.set(initialB); c.update(DT,[a,b],RACING);
  assert.equal(rec.all('bubble-ram').length,1,'one launch per contact cooldown');
  c.reset(); b.st[S.respawnPhase]=RESPAWN.GRACE; c.update(DT,[a,b],RACING);
  assert.equal(rec.all('bubble-ram').length,1,'rescue grace cannot be rammed');
});

test('bubble contact preserves its gameplay envelope and respects rails and assisted edge protection',()=>{
  for(const mode of ['open','rail','gentle','assist']) {
    const feel=makeFeel(mode==='gentle'?{'gentle.on':true}:{}), tr=fakeTrack({flags:()=>mode==='rail'?['rail']:[]});
    const rec=recorder(),c=createContact(tr,feel,rec);
    const a=racer(0,tr,feel,{lateral:4.8,speed:20}),b=racer(1,tr,feel,{lateral:6.6,speed:20});
    a.fxv.shield='glass'; if(mode==='assist') b.feel.over['gentle.on']=true;
    c.update(DT,[a,b],RACING);
    assert.equal(rec.all('bubble-ram').length,1,'the shell hits before bare kart bodies touch');
    run(b.st,tr,b.feel,acts({throttle:1}),2,rec,st=>st[S.respawnPhase]>0);
    if(mode==='open') assert.ok(b.st[S.respawnPhase]>0);
    else assert.equal(b.st[S.respawnPhase],0,mode+' still holds the edge');
  }
});

test('bump: overlapping karts push apart across the road, no spin, a bump cue', () => {
  const feel = makeFeel(), tr = fakeTrack(), rec = recorder(), c = createContact(tr, feel, rec);
  const a = racer(0, tr, feel, { s: 100, lateral: -0.5, speed: 20 }), b = racer(1, tr, feel, { s: 100, lateral: 0.5, speed: 20 });
  // a slides right into b
  const f = tr.frameAt(100, {});
  a.st[S.velX] += f.b[0] * 4; a.st[S.velZ] += f.b[2] * 4;
  const qa = quat(a.st), qb = quat(b.st);
  c.update(DT, [a, b], RACING);
  const lat = (r) => { const q = tr.query(r.st[S.posX], r.st[S.posY], r.st[S.posZ]); return q.lateral; };
  assert.ok(lat(b) - lat(a) >= 2 * feel.get('contact.radiusM') - 1e-6, 'separated');
  const vb = b.st[S.velX] * f.b[0] + b.st[S.velZ] * f.b[2], va = a.st[S.velX] * f.b[0] + a.st[S.velZ] * f.b[2];
  assert.ok(vb > 0 && va < 0, 'moving apart ' + va + ' ' + vb);
  assert.deepEqual(quat(a.st), qa); assert.deepEqual(quat(b.st), qb);
  assert.equal(a.st[S.stalled], 0); assert.equal(b.st[S.stalled], 0);
  const bump = rec.first('bump');
  assert.ok(bump && bump.payload.a === 0 && bump.payload.b === 1 && bump.payload.dv >= feel.get('contact.cueMinDvMps'));
  // a second touch inside the cooldown separates but sends no new cue
  b.st[S.posX] = a.st[S.posX]; b.st[S.posZ] = a.st[S.posZ] + 0.3;
  c.update(DT, [a, b], RACING);
  assert.equal(rec.all('bump').length, 1);
});

test('weights: the boosting kart moves less, Golden shoves harder', () => {
  const feel = makeFeel(), tr = fakeTrack();
  const run = (setup) => {
    const c = createContact(tr, feel, recorder());
    const a = racer(0, tr, feel, { s: 100, lateral: -0.3, speed: 20 }), b = racer(1, tr, feel, { s: 100, lateral: 0.3, speed: 20 });
    setup(a, b);
    const a0 = a.st[S.posX], b0 = b.st[S.posX];
    c.update(DT, [a, b], RACING);
    return [Math.hypot(a.st[S.posX] - a0, 0), Math.hypot(b.st[S.posX] - b0, 0)];
  };
  const even = run(() => {});
  assert.ok(Math.abs(even[0] - even[1]) < 1e-3);
  const boost = run((a) => { a.st[S.boostKind] = BOOST.PAD; });
  assert.ok(boost[0] < boost[1]);
  const gold = run((a) => { a.mods.shoveMul = feel.get('items.goldenShoveMul'); });
  assert.ok(gold[0] < gold[1] && gold[1] > even[1]);
});

test('ghosts: the start window and every rescue phase never touch', () => {
  const feel = makeFeel(), tr = fakeTrack(), c = createContact(tr, feel, recorder());
  const a = racer(0, tr, feel, { s: 100, lateral: -0.2 }), b = racer(1, tr, feel, { s: 100, lateral: 0.2 });
  const x0 = a.st[S.posX];
  c.update(DT, [a, b], { phase: 'racing', timeS: 0.5 });
  assert.equal(a.fxv.ghost, true); assert.equal(a.st[S.posX], x0);
  b.st[S.respawnPhase] = RESPAWN.GRACE;
  c.update(DT, [a, b], RACING);
  assert.equal(b.fxv.ghost, true); assert.equal(a.fxv.ghost, false); assert.equal(a.st[S.posX], x0);
  c.update(DT, [a, b], { phase: 'countdown', timeS: 0 });
  assert.equal(a.fxv.ghost, false, 'no ghost look on the grid');
});

test('open edge: a push may carry a kart off, never under Gentle or the assist; rails always hold', () => {
  const tr = fakeTrack({ flags: () => [] });             // open both sides, w 14
  const shove = (feel, assist) => {
    const c = createContact(tr, feel, recorder());
    // a deep overlap at the edge (sized by contact.radiusM: VIEW shrank the karts in polish 6)
    const r2 = 2 * feel.get('contact.radiusM');
    const a = racer(0, tr, feel, { s: 100, lateral: 6.8 - r2 * 0.4, speed: 20 }), b = racer(1, tr, feel, { s: 100, lateral: 6.8, speed: 20 });
    if (assist) b.feel.over['gentle.on'] = true;
    c.update(DT, [a, b], RACING);
    return tr.query(b.st[S.posX], b.st[S.posY], b.st[S.posZ]).lateral;
  };
  assert.ok(shove(makeFeel(), false) > 7, 'past the open edge');
  assert.ok(shove(makeFeel({ 'gentle.on': true }), false) <= 7 + 1e-6, 'Gentle holds it inside');
  assert.ok(shove(makeFeel(), true) <= 7 + 1e-6, 'the assist holds it inside');
  const railed = fakeTrack();
  const feel = makeFeel(), c = createContact(railed, feel, recorder());
  const a = racer(0, railed, feel, { s: 100, lateral: 5.6, speed: 20 }), b = racer(1, railed, feel, { s: 100, lateral: 6.6, speed: 20 });
  c.update(DT, [a, b], RACING);
  assert.ok(railed.query(b.st[S.posX], b.st[S.posY], b.st[S.posZ]).lateral <= 7, 'a rail is a rail');
});

test('slipstream: charge in the wake, fire a slip boost, cooldown, out when leaving', () => {
  const feel = makeFeel(), tr = fakeTrack(), rec = recorder(), c = createContact(tr, feel, rec);
  const lead = racer(0, tr, feel, { s: 108, lateral: 0, speed: 28 }), tail = racer(1, tr, feel, { s: 100, lateral: 0.5, speed: 28 });
  for (const r of [lead, tail]) r.st[S.speedPct] = 28 / feel.get('speed.topMps');
  const n = Math.ceil(feel.get('contact.slipChargeS') / DT) + 1;
  for (let i = 0; i < n; i++) { rec.t += DT; c.update(DT, [lead, tail], RACING); }
  const states = rec.all('slipstream').filter((e) => e.payload.racer === 1).map((e) => e.payload.state);
  assert.deepEqual(states, ['in', 'fire']);
  const bs = tail.events.first('boost-start');
  assert.ok(bs && bs.payload.kind === 'slip' && bs.payload.amount === feel.get('contact.slipAmount'));
  assert.equal(tail.st[S.boostKind], BOOST.PAD);
  // cooldown: no charge right after
  c.update(DT, [lead, tail], RACING);
  assert.equal(tail.fxv.slip, 0);
  // too far behind: nothing
  const far = racer(2, tr, feel, { s: 80, lateral: 0, speed: 28 });
  far.st[S.speedPct] = lead.st[S.speedPct];
  c.update(DT, [lead, far], RACING);
  assert.equal(far.fxv.slip, 0);
  // in, then leave the wake sideways: out
  const t2 = racer(3, tr, feel, { s: 100, lateral: 0, speed: 28 }), rec2 = recorder(), c2 = createContact(tr, feel, rec2);
  t2.st[S.speedPct] = lead.st[S.speedPct];
  c2.update(DT, [lead, t2], RACING);
  assert.ok(t2.fxv.slip > 0);
  t2.st[S.lateral] = 4;
  c2.update(DT, [lead, t2], RACING);
  assert.deepEqual(rec2.all('slipstream').filter((e) => e.payload.racer === 3).map((e) => e.payload.state), ['in', 'out']);
});

test('slipstream: the kick waits through a court approach and fires once the holes are behind', () => {
  const feel = makeFeel(), tr = fakeTrack(), rec = recorder();
  tr.courts = [{ fromS: 120, toS: 200, holes: [{ id: 'h', s: 150, x: -7, w: 6, len: 8 }] }];
  const c = createContact(tr, feel, rec);
  const lead = racer(0, tr, feel, { s: 108, lateral: 0, speed: 28 }), tail = racer(1, tr, feel, { s: 100, lateral: 0.5, speed: 28 });
  for (const r of [lead, tail]) r.st[S.speedPct] = 28 / feel.get('speed.topMps');
  const n = Math.ceil(feel.get('contact.slipChargeS') / DT) + 4;
  for (let i = 0; i < n; i++) { rec.t += DT; c.update(DT, [lead, tail], RACING); }
  const states = () => rec.all('slipstream').filter((e) => e.payload.racer === 1).map((e) => e.payload.state);
  assert.deepEqual(states(), ['in'], 'charged but held inside the court lead');
  assert.equal(tail.st[S.boostKind], BOOST.NONE);
  // past the last hole: the held charge fires on the next tick
  lead.st[S.s] = 170; tail.st[S.s] = 162;
  rec.t += DT; c.update(DT, [lead, tail], RACING);
  assert.deepEqual(states(), ['in', 'fire']);
  assert.equal(tail.st[S.boostKind], BOOST.PAD);
});
