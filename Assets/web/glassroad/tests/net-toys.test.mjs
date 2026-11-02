import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createNetItems } from '../net/items.js';
import { EFFECTS, MODES, VERDICTS, createKart, packKart, unpackKart } from '../net/protocol.js';
import { fakeTrack, makeFeel, place, acts, recorder, DT } from './sim-fake.mjs';
import { S } from '../sim/state.js';

// Seat 0 is this client at s 100, seat 1 a remote kart 20 m ahead.
function setup() {
  const track = fakeTrack(), feel = makeFeel(), rec = recorder(), sent = [];
  const list = [100, 120].map((s, i) => ({ i, kind: i ? 'remote' : 'player', meta: { seat: i, isBot: false },
    st: place(track, { s }), input: acts(), item: {}, mods: {}, fxv: {}, finished: false }));
  const items = createNetItems(track, feel, rec, { send: (m) => sent.push(m) });
  const standings = { order: [1, 0], progressOf: (i) => list[i].st[S.s], placeOf: (i) => (i ? 1 : 2), gapToLeader: () => 20 };
  const tick = (dt = DT) => items.update(dt, list, standings, { phase: 'racing' });
  tick();
  const press = (id) => { list[0].item.id = id; list[0].item.rolling = false; list[0].input.item = true; tick(); list[0].input.item = false; };
  return { track, feel, rec, list, items, tick, press, sent };
}

test('the wire knows the toys kit: modes, the snapped verdict and every hit look in a record', () => {
  for (const m of ['bounce', 'tether']) assert.ok(MODES.includes(m));
  assert.ok(VERDICTS.includes('snapped'));
  assert.ok(EFFECTS.length <= 15, 'effect field is 4 bits');
  const dv = new DataView(new ArrayBuffer(40));
  for (const effect of EFFECTS) {
    packKart(dv, 0, Object.assign(createKart(), { effect }));
    assert.equal(unpackKart(dv, 0).effect, effect);
  }
});

test('a Tether is sent with someone close ahead, kept when nobody is, and drags both karts on the room word', () => {
  const t = setup();
  t.list[1].st = place(t.track, { s: 400 });                    // 300 m ahead: out of range
  t.press('tether');
  assert.equal(t.sent.length, 0); assert.equal(t.list[0].item.id, 'tether');
  t.list[1].st = place(t.track, { s: 120 });
  t.press('tether');
  assert.equal(t.sent.at(-1).mode, 'tether'); assert.equal(t.list[0].item.id, null);
  t.items.onFired({ sid: 7, seat: 0, id: 1, item: 'tether', mode: 'tether', tick: 0, s: 100, x: 0, target: 1 });
  t.tick();
  assert.equal(t.rec.first('tether-hook').payload.target, 1);
  assert.equal(t.items.view().tethers.length, 1);
  assert.ok(Math.abs(t.list[0].mods.capMul - t.feel.get('items.tetherTowCap')) < 1e-9);
  // the room says the tower passed: the link breaks with a recoil; a spin lands only on a local victim
  t.items.onHit({ sid: 7, victim: 1, by: 0, item: 'tether', verdict: 'hit', durationS: 1, tick: 0 });
  t.tick();
  assert.equal(t.rec.first('tether-break').payload.reason, 'pass');
  assert.equal(t.rec.first('item-hit').payload.how, 'tether');
  assert.equal(t.items.view().tethers.length, 0);
  assert.equal(t.list[0].mods.capMul, 1);
});

test('a Tether on this kart drags it, then spins it with a stall when the tower passes', () => {
  const t = setup();
  t.items.onFired({ sid: 3, seat: 1, id: 1, item: 'tether', mode: 'tether', tick: 0, s: 120, x: 0, target: 0 });
  t.tick();
  assert.ok(Math.abs(t.list[0].mods.capMul - t.feel.get('items.tetherDragCap')) < 1e-9);
  t.items.onHit({ sid: 3, victim: 0, by: 1, item: 'tether', verdict: 'hit', durationS: 1, tick: 0 });
  t.tick();
  assert.equal(t.items.state(0).eff.kind, 'tether');
  assert.ok(t.list[0].st[S.stalled] > 0);
  assert.ok(t.list[0].fxv.spinRad > 0);
  t.items.onFired({ sid: 4, seat: 1, id: 2, item: 'tether', mode: 'tether', tick: 0, s: 120, x: 0, target: 0 });
  t.items.onFizzle({ sid: 4, tick: 0 });
  assert.equal(t.rec.all('tether-break').at(-1).payload.reason, 'timeout');
});

test('Snap guards locally and a room shove pushes the victim away from the snapper', () => {
  const t = setup();
  t.press('snap');
  assert.equal(t.sent.at(-1).item, 'snap'); assert.equal(t.sent.at(-1).mode, 'self');
  assert.equal(t.items.view().guards.length, 1);
  assert.equal(t.rec.first('snap-burst').payload.racer, 0);
  const u = setup();
  u.list[0].st[S.lateral] = -3; u.list[1].st[S.lateral] = 2;
  u.items.onHit({ sid: 9, victim: 0, by: 1, item: 'snap', verdict: 'hit', durationS: 0.35, tick: 0 });
  u.tick();
  assert.equal(u.items.state(0).eff.side, -1);
  assert.ok(u.list[0].mods.steerAdd < 0);
  u.items.onHit({ sid: 10, victim: 0, by: 1, item: 'pendulum', verdict: 'snapped', durationS: 0, tick: 0 });
  assert.equal(u.rec.all('item-hit').at(-1).payload.verdict, 'snapped');
});

test('a room Disc flies with the room aim and banks off a rail', () => {
  const t = setup();
  t.items.onFired({ sid: 5, seat: 1, id: 1, item: 'disc', mode: 'bounce', tick: 0, s: 120, x: 0, target: null, vx: 7 });
  const p0 = t.items.view().projectiles[0];
  assert.equal(p0.mode, 'bounce');
  let bounced = false;
  for (let i = 0; i < 120 && t.items.view().projectiles.length; i++) { t.tick(); bounced ||= t.rec.all('disc-bounce').length > 0; }
  const railed = (t.track.frameAt(120).flags || []).includes('rail');
  if (railed) assert.ok(bounced, 'a railed fake road reflects the Disc');
  t.items.onFizzle({ sid: 5, tick: 0 });
  assert.equal(t.items.view().projectiles.length, 0);
});

test('Pendulum and Big Spiral hits land their own handling on this kart', () => {
  const t = setup();
  t.items.onHit({ sid: 1, victim: 0, by: 1, item: 'bigSpiral', verdict: 'hit', durationS: 2, tick: 0 });
  t.tick();
  assert.equal(t.list[0].mods.capMul, t.feel.get('items.bigSpiralCapMul'));
  t.items.onHit({ sid: 2, victim: 0, by: 1, item: 'pendulum', verdict: 'hit', durationS: 1.5, tick: 0 });
  for (let i = 0; i < 20; i++) t.tick();
  assert.notEqual(t.list[0].mods.steerAdd, 0);
});
