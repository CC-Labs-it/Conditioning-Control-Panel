import test from 'node:test';
import assert from 'node:assert/strict';
import { fakeTrack, place, makeFeel, recorder, acts, DT } from './sim-fake.mjs';
import { S } from '../sim/state.js';
import { ITEMS, SPIN_KINDS } from '../sim/itemTable.js';
import { itemUseMode } from '../sim/itemUse.js';
import { createItems } from '../sim/items.js';
import { createNetItems } from '../net/items.js';

const offensive = Object.keys(ITEMS).filter(id => ITEMS[id].use !== 'self');
const race = { phase: 'racing' };
function setup({ online = false, flags = () => ['rail'] } = {}) {
  const track = fakeTrack({ flags }), feel = makeFeel(), events = recorder(), sent = [];
  const racers = [100, 50].map((s, i) => ({ i, kind: 'player', meta: { seat: i, isBot: false },
    st: place(track, { s, lateral: 1, speed: 30 }), input: acts(), item: {}, mods: {}, fxv: {}, finished: false }));
  const standings = { order: [0, 1], progressOf: i => racers[i].st[S.s], placeOf: i => i + 1, gapToLeader: i => i * 50 };
  const items = online ? createNetItems(track, feel, events, { send: m => sent.push(m) }) : createItems(track, feel, events);
  const tick = (dt = DT) => items.update(dt, racers, standings, race);
  const move = (i, s, height = 0) => { racers[i].st = place(track, { s, lateral: 1, height, speed: 30 }); };
  const fire = (id, back = true) => {
    Object.assign(racers[0].item, { id, rolling: false, rollT: 0, heldT: 0 });
    Object.assign(racers[0].input, { item: true, back }); tick(); racers[0].input.item = false;
  };
  return { track, feel, events, sent, racers, items, tick, move, fire, hits: () => events.all('item-hit').map(e => e.payload) };
}

for (const id of offensive) test(`${id}: rear use leaves a fixed floor hazard that hits a following racer`, () => {
  const t = setup(); t.fire(id);
  const tr = structuredClone(t.items.view().traps[0]);
  assert.ok(tr, 'rear use creates a trap without requiring a forward target');
  assert.equal(t.racers[0].item.id, null);
  assert.equal(t.items.view().projectiles.length + t.items.view().tethers.length + t.items.view().waves.length, 0);
  assert.ok(Math.abs(tr.s - (100 - t.feel.get('items.dropBehindM'))) < 1e-8);
  assert.equal(tr.x, 1); assert.equal(tr.p[1], 0);
  t.move(0, 130); t.tick(.7);
  assert.deepEqual(t.items.view().traps[0].p, tr.p, 'the hazard stays where it was dropped');
  t.move(1, tr.s); t.tick();
  assert.equal(t.hits().length, 1); assert.equal(t.hits()[0].victim, 1);
  assert.equal(t.hits()[0].kind, id); assert.equal(t.hits()[0].how, 'trap');
  assert.equal(t.hits()[0].verdict, 'hit'); assert.equal(t.racers[1].fxv.effect, id);
  assert.equal(t.items.view().traps.length, 0);
  if (SPIN_KINDS.includes(id)) assert.ok(t.racers[1].st[S.stalled] > 0, 'snags spin the follower');
});

test('rear drops retain owner arming delay and cannot hit a different deck', () => {
  const t = setup(); t.fire('pink');
  const s = t.items.view().traps[0].s;
  t.move(0, s); t.move(1, s, 12); t.tick(.2);
  assert.equal(t.hits().length, 0, 'owner is protected and the other kart is on a different deck');
  t.move(0, 130); t.tick(.7); assert.equal(t.hits().length, 0);
  t.move(1, s); t.tick(); assert.equal(t.hits()[0].victim, 1);
});

test('dropped attacks still respect a follower shield', () => {
  const t = setup();
  t.racers[1].item = { id: 'glass', rolling: false }; t.racers[1].input.item = true;
  t.tick(); t.racers[1].input.item = false;
  t.fire('tether'); const s = t.items.view().traps[0].s; t.move(1, s); t.tick();
  assert.equal(t.hits()[0].verdict, 'glass'); assert.equal(t.racers[1].st[S.stalled], 0);
  assert.equal(t.items.view().tethers.length, 0, 'a dropped tether never creates a forward tow link');
});

test('spin hazards are kept instead of dropped beside an open edge', () => {
  for (const id of SPIN_KINDS) {
    const t = setup({ flags: () => ['open'] }); t.fire(id);
    assert.equal(t.racers[0].item.id, id); assert.equal(t.items.view().traps.length, 0);
    assert.equal(t.events.first('item-refused').payload.reason, 'edge');
  }
});

test('rear use still activates self items and forward attacks retain their normal mode', () => {
  for (const [id, item] of Object.entries(ITEMS)) {
    assert.equal(itemUseMode(id, false), item.use === 'trap' ? 'drop' : item.use);
    if (item.use !== 'self') continue;
    const t = setup(); t.fire(id);
    assert.equal(t.events.first('item-fire').payload.mode, 'self');
    assert.equal(t.items.view().traps.length, 0); assert.equal(t.racers[0].item.id, null);
    if (id === 'glass') assert.equal(t.racers[0].fxv.shield, 'glass');
  }
  const t = setup(); t.move(1, 150); t.fire('pendulum', false);
  const pr = t.items.view().projectiles[0]; assert.ok(pr.homing); assert.equal(pr.target, 1);
});

test('online rear requests use the existing drop packet and wait for room hit verdicts', () => {
  for (const id of offensive) {
    const t = setup({ online: true }); t.fire(id);
    const packet = t.sent.at(-1); assert.equal(packet.mode, 'drop'); assert.equal(packet.item, id);
    assert.equal(t.items.view().traps.length, 0, 'only the room confirms placement');
    t.items.onFired({ ...packet, sid: 9, seat: 0, target: null }); t.tick();
    const tr = t.items.view().traps[0]; assert.equal(tr.kind, id);
    assert.ok(Math.abs(tr.s - (100 - t.feel.get('items.dropBehindM'))) < 1e-8);
    t.move(1, tr.s); t.tick(); assert.equal(t.hits().length, 0, 'no local damage prediction');
    t.items.onHit({ sid: 9, victim: 1, by: 0, item: id, verdict: 'hit', durationS: 1 }); t.tick();
    assert.equal(t.hits()[0].how, 'trap'); assert.equal(t.racers[1].fxv.effect, id);
    assert.equal(t.items.view().traps.length, 0);
  }
});
