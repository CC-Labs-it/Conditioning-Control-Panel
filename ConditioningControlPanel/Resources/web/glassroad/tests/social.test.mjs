// tests/social.test.mjs - interaction wave lane C: knock-loose items, payback marks, reactions (sim/social.js).
// Lane A's contact.js `hard-bump` and lane B's `tunnel-ram` are emitted by hand here (the contracts in the brief).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { fakeTrack, place, makeFeel, recorder, acts, DT } from './sim-fake.mjs';
import { S } from '../sim/state.js';
import { createItems } from '../sim/items.js';
import { createSocial, socialTap, knockOf } from '../sim/social.js';

const RACE = { phase: 'racing' };

function racer(track, i, opts = {}) {
  return { i, meta: { i, id: i ? 'bot' + i : 'you', name: i ? 'EMI bot' : 'You', isBot: i > 0, tint: i, slot: i },
    st: place(track, opts), input: acts({ item: false, back: false }),
    mods: { steerMul: 1, steerAdd: 0, throttleMul: 1, gripMul: 1, slideMul: 1, capMul: 1, shoveMul: 1, blind: 0 },
    item: { id: null, rolling: false, rollT: 0, heldT: 0 },
    fxv: { spinRad: 0, shield: null, echo: 0, effect: null, ghost: false, slip: 0, place: 0 }, finished: false };
}

function standings(list) {
  const prog = (i) => list[i].st[S.s];
  const st = { order: [], progressOf: prog, placeOf: (i) => st.order.indexOf(i) + 1, gapToLeader: (i) => prog(st.order[0]) - prog(i) };
  st.refresh = () => { st.order = list.map((r) => r.i).sort((a, b) => prog(b) - prog(a)); return st; };
  return st.refresh();
}

function setup({ feel = {}, karts = [{ s: 100 }, { s: 103 }] } = {}) {
  const tr = fakeTrack(), f = makeFeel(feel), rec = recorder();
  const social = createSocial(f, { seed: 3 });
  const q = socialTap(rec, (n, p) => social.see(n, p));
  const items = createItems(tr, f, q, { seed: 7 });
  social.bind(items);
  const list = karts.map((o, i) => racer(tr, i, o));
  const std = standings(list);
  const tick = (sec = DT) => {
    for (let n = Math.max(1, Math.round(sec / DT)); n > 0; n--) {
      rec.t += DT; items.update(DT, list, std.refresh(), RACE); social.update?.(DT, list, RACE);
    }
  };
  const moveTo = (r, s) => { const st = place(tr, { s }); r.st.set(st); };
  return { tr, f, rec, q, social, items, list, std, tick, moveTo };
}

test('knock-loose: a hard bump at knockDvMps pops the held item behind the loser with that exact id', () => {
  const { rec, q, items, list, tick } = setup();
  tick();
  list[0].item.id = 'disc';
  q.emit('hard-bump', { winner: 1, loser: 0, dv: 8.9, p: [0, 0, 0] });
  assert.equal(list[0].item.id, 'disc', 'a soft hard bump keeps the item');
  q.emit('hard-bump', { winner: 1, loser: 0, dv: 9.5, p: [0, 0, 0] });
  assert.equal(list[0].item.id, null);
  const k = rec.first('item-knocked').payload;
  assert.deepEqual([k.from, k.by, k.id], [0, 1, 'disc']);
  tick();
  const loose = items.view().loose;
  assert.equal(loose.length, 1);
  assert.equal(loose[0].id, 'disc');
});

test('knock-loose: an empty or rolling slot has nothing to lose', () => {
  const { rec, q, list, tick } = setup();
  tick();
  q.emit('hard-bump', { winner: 1, loser: 0, dv: 20 });
  Object.assign(list[0].item, { id: 'disc', rolling: true });
  q.emit('hard-bump', { winner: 1, loser: 0, dv: 20 });
  assert.equal(rec.all('item-knocked').length, 0);
});

test('knock-loose: a tunnel ram knocks too, also when a bot rides (racer-event)', () => {
  const { rec, q, list, tick } = setup();
  tick();
  list[1].item.id = 'snap';
  q.emit('racer-event', { racer: 0, name: 'tunnel-ram', p: { attacker: 0, victim: 1, p: null } });
  assert.equal(list[1].item.id, null);
  assert.equal(rec.first('item-knocked').payload.id, 'snap');
  assert.deepEqual(knockOf(makeFeel(), 'tunnel-ram', { attacker: 2, victim: 2 }), null);
});

test('knock-loose: anyone with an empty slot takes it; the loser only after knockOwnerS; it fizzles after knockLifeS', () => {
  const { rec, q, items, list, tick, moveTo } = setup({ karts: [{ s: 100 }, { s: 140 }] });
  tick();
  list[0].item.id = 'pendulum';
  q.emit('hard-bump', { winner: 1, loser: 0, dv: 12 });
  moveTo(list[0], 97);                  // straight back over it
  tick(0.5);
  assert.equal(list[0].item.id, null, 'the loser cannot re-take it at once');
  tick(0.6);
  assert.equal(list[0].item.id, 'pendulum', 'the loser may take it back after a second');
  assert.equal(rec.first('item-loose-take').payload.racer, 0);
  assert.equal(items.view().loose.length, 0);

  q.emit('hard-bump', { winner: 1, loser: 0, dv: 12 });
  moveTo(list[0], 60);
  tick(12.1);
  assert.equal(items.view().loose.length, 0, 'gone after knockLifeS');
  assert.equal(list[0].item.id, null);
});

test('knock-loose: a racer holding an item drives through a loose one', () => {
  const { q, items, list, tick, moveTo } = setup({ karts: [{ s: 100 }, { s: 140 }] });
  tick();
  list[0].item.id = 'disc';
  list[1].item.id = 'glass';
  q.emit('hard-bump', { winner: 1, loser: 0, dv: 12 });
  moveTo(list[1], 97);
  tick();
  assert.equal(list[1].item.id, 'glass');
  assert.equal(items.view().loose.length, 1);
  list[1].item.id = null;
  tick();
  assert.equal(list[1].item.id, 'disc');
});

test('payback: an item hit, a lost hard bump or a tunnel ram makes the hitter your mark for paybackS', () => {
  const { q, social, tick } = setup({ karts: [{ s: 100 }, { s: 110 }, { s: 120 }] });
  tick();
  q.emit('item-hit', { victim: 0, by: 2, kind: 'disc', verdict: 'glass' });
  assert.equal(social.markOf(0), -1, 'a blocked hit is no score');
  q.emit('item-hit', { victim: 0, by: 2, kind: 'disc', verdict: 'hit' });
  assert.equal(social.markOf(0), 2);
  q.emit('hard-bump', { winner: 1, loser: 0, dv: 6 });
  assert.equal(social.markOf(0), 1, 'the last hitter wins');
  q.emit('racer-event', { racer: 2, name: 'tunnel-ram', p: { attacker: 2, victim: 1 } });
  assert.equal(social.markOf(1), 2);
  assert.equal(social.markView(0).racer, 1);
  tick(19.9);
  assert.equal(social.markOf(0), 1);
  tick(0.2);
  assert.equal(social.markOf(0), -1);
  assert.equal(social.markView(0), null);
});

test('payback: the next throw homes on the mark when it is ahead in range, then the mark clears', () => {
  const { q, social, list, tick, rec } = setup({ karts: [{ s: 100 }, { s: 110 }, { s: 130 }] });
  tick();
  q.emit('item-hit', { victim: 0, by: 2, kind: 'disc', verdict: 'hit' });
  Object.assign(list[0].item, { id: 'pendulum' }); list[0].input.item = true;
  tick(); list[0].input.item = false;
  assert.equal(rec.first('item-fire').payload.target, 2, 'past the nearer kart to the mark');
  assert.equal(social.markOf(0), -1);
  // without a mark the nearest kart ahead is the target
  Object.assign(list[0].item, { id: 'pendulum' }); list[0].input.item = true;
  tick(); list[0].input.item = false;
  assert.equal(rec.all('item-fire')[1].payload.target, 1);
});

test('payback: a mark behind you or out of range keeps normal targeting and stays', () => {
  const { q, social, list, tick, rec } = setup({ karts: [{ s: 100 }, { s: 110 }, { s: 90 }] });
  tick();
  q.emit('hard-bump', { winner: 2, loser: 0, dv: 7 });
  Object.assign(list[0].item, { id: 'pendulum' }); list[0].input.item = true;
  tick(); list[0].input.item = false;
  assert.equal(rec.first('item-fire').payload.target, 1);
  assert.equal(social.markOf(0), 2);
});
