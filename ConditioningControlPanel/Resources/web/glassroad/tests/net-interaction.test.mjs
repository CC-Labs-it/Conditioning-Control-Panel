// The interaction wave online (lane D): payback names its mark on a throw (C2R fire `prefer`), the room decides.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createNetItems } from '../net/items.js';
import { C2R, checkMsg } from '../net/protocol.js';
import { fakeTrack, makeFeel, place, acts, recorder, DT } from './sim-fake.mjs';
import { S } from '../sim/state.js';

// Seat 0 is this client at s 100, seat 1 a remote kart ahead, seat 2 a remote kart behind.
function setup(markOf, places = [2, 1, 3]) {
  const track = fakeTrack(), feel = makeFeel(), rec = recorder(), sent = [], spent = [];
  const list = [100, 120, 80].map((s, i) => ({ i, kind: i ? 'remote' : 'player', meta: { seat: i, isBot: false },
    st: place(track, { s }), input: acts(), item: {}, mods: {}, fxv: {}, finished: false }));
  const items = createNetItems(track, feel, rec, { send: (m) => { assert.ok(checkMsg(C2R, m), JSON.stringify(m)); sent.push(m); } });
  items.setPayback({ markOf, spend: (i) => spent.push(i) });
  const standings = { order: [1, 0, 2], progressOf: (i) => list[i].st[S.s], placeOf: (i) => places[i], gapToLeader: () => 20 };
  const tick = (dt = DT) => items.update(dt, list, standings, { phase: 'racing' });
  tick();
  const press = (id) => { list[0].item.id = id; list[0].item.rolling = false; list[0].input.item = true; tick(); list[0].input.item = false; };
  return { list, press, sent, spent, track, items, rec, tick, feel };
}

test('payback online: a throw names the mark ahead in range and spends it; a mark behind or far is not sent', () => {
  let t = setup((i) => (i === 0 ? 1 : -1));
  t.press('pendulum');
  assert.equal(t.sent.at(-1).mode, 'throw'); assert.equal(t.sent.at(-1).prefer, 1); assert.deepEqual(t.spent, [0]);
  t = setup((i) => (i === 0 ? 2 : -1));
  t.press('pendulum');
  assert.equal(t.sent.at(-1).prefer, undefined, 'a mark behind'); assert.deepEqual(t.spent, []);
  t = setup((i) => (i === 0 ? 1 : -1));
  t.list[1].st = place(t.track, { s: 900 });
  t.press('pendulum');
  assert.equal(t.sent.at(-1).prefer, undefined, 'out of throw range');
  t = setup((i) => (i === 0 ? 1 : -1));
  t.press('surge');
  assert.equal(t.sent.at(-1).prefer, undefined, 'only throws'); assert.deepEqual(t.spent, []);
});

test('a ride the room refuses stops at once and the item stays held, as the room kept the slot', () => {
  const t = setup(() => -1, [3, 1, 2]);
  t.press('pendulum');
  const thrown = t.sent.at(-1);
  t.items.onFired({ sid: 4, seat: 0, id: thrown.id, item: 'pendulum', mode: 'throw', tick: 0, s: 100, x: 0, target: 1 });
  t.press('tunnel');
  assert.equal(t.sent.at(-1).mode, 'ride');
  assert.ok(t.items.tunnel.riding(0)); assert.equal(t.list[0].item.id, null);
  t.items.onRefused('fire');                    // the oldest unanswered fire: the ride (the throw was answered)
  assert.ok(!t.items.tunnel.riding(0), 'cancelled');
  assert.equal(t.rec.all('tunnel-end').at(-1).payload.reason, 'cancel');
  assert.equal(t.list[0].item.id, 'tunnel'); assert.equal(t.list[0].item.rolling, false);
  assert.equal(t.rec.all('item-refused').at(-1).payload.reason, 'room');
  t.tick();
  assert.notEqual(t.list[0].fxv.effect, 'tunnel', 'the record stops showing the ride');
  t.items.onRefused('fire');                    // nothing pending: nothing changes
  assert.equal(t.list[0].item.id, 'tunnel');
});

test('knock-loose online: the loser asks, the room drops it for everyone, a kart takes it on the room word', () => {
  const t = setup(() => -1);
  t.list[0].item.id = 'pendulum'; t.list[0].item.rolling = false;
  t.items.knock(0, 1, null, 12.345);
  assert.deepEqual(t.sent.at(-1), { t: 'knock', by: 1, dv: 12.35, tick: 0 });
  assert.equal(t.list[0].item.id, 'pendulum', 'held until the room agrees');
  t.items.knock(1, 0, null, 12);                 // a remote kart's knock is its own client's to report
  assert.equal(t.sent.at(-1).by, 1);
  t.items.onLoose({ lid: 5, item: 'pendulum', s: 100, x: 0, from: 0, by: 1 });
  assert.equal(t.list[0].item.id, null);
  assert.equal(t.rec.first('item-knocked').payload.id, 'pendulum');
  let v = t.items.view();
  assert.equal(v.loose.length, 1); assert.equal(v.loose[0].id, 'pendulum'); assert.equal(v.loose[0].key, 5);
  assert.equal(v.loose[0].p.length, 3); assert.equal(v.loose[0].n.length, 3);
  const n = t.sent.length;
  t.tick();
  assert.equal(t.sent.length, n, 'the loser waits out knockOwnerS');
  t.tick(t.feel.get('items.knockOwnerS') + 0.05); t.tick();
  assert.deepEqual(t.sent.at(-1), { t: 'take', lid: 5, tick: 0 });
  t.tick();
  assert.equal(t.sent.length, n + 1, 'one take in flight');
  t.items.onRefused('take');
  t.tick();
  assert.equal(t.sent.at(-1).t, 'take'); assert.equal(t.sent.length, n + 2, 'refused: tries again');
  t.items.onTook({ lid: 5, seat: 0, item: 'pendulum' });
  assert.equal(t.list[0].item.id, 'pendulum'); assert.equal(t.list[0].item.rolling, false);
  assert.equal(t.items.view().loose.length, 0);
  assert.equal(t.rec.first('item-loose-take').payload.racer, 0);
});

test('knock-loose online: loose items fade after knockLifeS and cap at knockMax, oldest first', () => {
  const t = setup(() => -1);
  const max = t.feel.get('items.knockMax');
  for (let k = 0; k < max + 2; k++) t.items.onLoose({ lid: k + 1, item: 'surge', s: 300 + k, x: 0, from: 1, by: 2 });
  let v = t.items.view();
  assert.equal(v.loose.length, max); assert.equal(v.loose[0].key, 3);
  t.tick(t.feel.get('items.knockLifeS') + 0.1);
  assert.equal(t.items.view().loose.length, 0);
});
