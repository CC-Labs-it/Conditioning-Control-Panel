// The interaction wave online (lane D): payback names its mark on a throw (C2R fire `prefer`), the room decides.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createNetItems } from '../net/items.js';
import { C2R, checkMsg } from '../net/protocol.js';
import { fakeTrack, makeFeel, place, acts, recorder, DT } from './sim-fake.mjs';
import { S } from '../sim/state.js';

// Seat 0 is this client at s 100, seat 1 a remote kart ahead, seat 2 a remote kart behind.
function setup(markOf) {
  const track = fakeTrack(), feel = makeFeel(), rec = recorder(), sent = [], spent = [];
  const list = [100, 120, 80].map((s, i) => ({ i, kind: i ? 'remote' : 'player', meta: { seat: i, isBot: false },
    st: place(track, { s }), input: acts(), item: {}, mods: {}, fxv: {}, finished: false }));
  const items = createNetItems(track, feel, rec, { send: (m) => { assert.ok(checkMsg(C2R, m), JSON.stringify(m)); sent.push(m); } });
  items.setPayback({ markOf, spend: (i) => spent.push(i) });
  const standings = { order: [1, 0, 2], progressOf: (i) => list[i].st[S.s], placeOf: (i) => [2, 1, 3][i], gapToLeader: () => 20 };
  const tick = (dt = DT) => items.update(dt, list, standings, { phase: 'racing' });
  tick();
  const press = (id) => { list[0].item.id = id; list[0].item.rolling = false; list[0].input.item = true; tick(); list[0].input.item = false; };
  return { list, press, sent, spent, track };
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
