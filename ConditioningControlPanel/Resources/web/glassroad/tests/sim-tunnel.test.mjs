// tests/sim-tunnel.test.mjs - Tunnel Vision (lane B 2026-10-30): the ride, its end rules, rams, immunity, the guard.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { fakeTrack, place, makeFeel, recorder, acts, DT } from './sim-fake.mjs';
import { S } from '../sim/state.js';
import { createItems } from '../sim/items.js';
import { ITEM_IDS, ITEMS, weightsFor } from '../sim/itemTable.js';
import { itemUseMode } from '../sim/itemUse.js';
import { WIRE_ITEMS, EFFECTS, MODES } from '../net/protocol.js';

const RACE = { phase: 'racing' };

function racer(track, i, opts = {}) {
  return { i, meta: { i, id: i ? 'bot' + i : 'you', name: i ? 'EMI bot' : 'You', isBot: i > 0, tint: i, slot: i },
    st: place(track, opts), input: acts({ item: false, back: false }), speed: opts.speed || 0, lateral: opts.lateral || 0,
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

function setup({ feel = {}, track = {}, karts = [{ s: 100 }] } = {}) {
  const tr = fakeTrack(track), f = makeFeel(feel), rec = recorder();
  const items = createItems(tr, f, rec, { seed: 3 });
  const list = karts.map((o, i) => racer(tr, i, o));
  const std = standings(list);
  // the boot order: racers.step (the ride moves its kart, the others roll on at their speed), then items.update
  const tick = (sec = DT) => {
    for (let n = Math.max(1, Math.round(sec / DT)); n > 0; n--) {
      rec.t += DT;
      for (const r of list) {
        if (items.tunnel.riding(r.i)) items.tunnel.step(r, DT);
        else if (r.st[S.grounded] && r.speed > 0) {
          const st = place(tr, { s: r.st[S.s] + r.speed * DT, lateral: r.st[S.lateral], speed: r.speed });
          r.st.set(st);
        }
      }
      items.update(DT, list, std.refresh(), RACE);
    }
  };
  const fire = (who, id = 'tunnel') => {
    const r = list[who];
    Object.assign(r.item, { id, rolling: false, rollT: 0, heldT: 0 });
    r.input.item = true; tick(); r.input.item = false;
  };
  return { tr, f, rec, items, list, std, tick, fire };
}

test('table: Tunnel Vision is the last id, a ride, back bucket only, on the wire and in the effects', () => {
  const f = makeFeel();
  assert.equal(ITEM_IDS[ITEM_IDS.length - 1], 'tunnel');
  assert.equal(ITEMS.tunnel.use, 'ride');
  assert.equal(ITEMS.tunnel.view, null);
  assert.equal(itemUseMode('tunnel', true), 'ride', 'a rear press still rides');
  assert.ok(!weightsFor(f, 'front').some(([id]) => id === 'tunnel') && !weightsFor(f, 'middle').some(([id]) => id === 'tunnel'));
  assert.ok(weightsFor(f, 'back').some(([id]) => id === 'tunnel'));
  assert.equal(WIRE_ITEMS[WIRE_ITEMS.length - 1], 'tunnel');
  assert.ok(EFFECTS.includes('tunnel') && EFFECTS.length <= 15, 'fits the 4 bit effect field');
  assert.ok(MODES.includes('ride'));
});

test('a press rides: fast, hovering, hands off, effect shown; ends on time with the guard', () => {
  const t = setup({ karts: [{ s: 100, speed: 20 }, { s: 3000 }, { s: 3100 }] });
  t.fire(0);
  t.list[0].speed = 0;   // the harness stops rolling it on after the ride
  assert.equal(t.rec.all('tunnel-start').length, 1);
  assert.equal(t.list[0].item.id, null);
  assert.ok(t.items.tunnel.riding(0));
  t.tick(1.5);
  const top = t.f.get('speed.topMps'), v = Math.hypot(t.list[0].st[S.velX], t.list[0].st[S.velY], t.list[0].st[S.velZ]);
  assert.ok(v >= top * t.f.get('items.tunnelSpeedMul') - 0.5, 'rides at 1.6x top: ' + v);
  assert.ok(Math.abs(t.list[0].st[S.height] - t.f.get('items.tunnelHoverM')) < 1e-3);
  assert.equal(t.list[0].fxv.effect, 'tunnel');
  assert.equal(t.items.view().rides.length, 1);
  for (let k = 0; k < 600 && t.items.tunnel.riding(0); k++) t.tick();
  const end = t.rec.all('tunnel-end').map((e) => e.payload);
  assert.equal(end.length, 1);
  assert.equal(end[0].reason, 'time');
  assert.ok(t.rec.t >= t.f.get('items.tunnelS'), 'rode the whole time');
  assert.ok(!t.items.tunnel.riding(0));
  assert.equal(t.list[0].st[S.height], 0);
  assert.ok(t.list[0].st[S.engineV] >= top * 1.5, 'leaves at full speed');
  assert.ok(t.items.view().guards.some((g) => g.racer === 0), 'the guard follows the ride');
});

test('it stops at place 2: never takes the lead', () => {
  const t = setup({ karts: [{ s: 100 }, { s: 300, speed: 20 }, { s: 140, speed: 10 }] });
  t.fire(0);
  t.tick(6);
  const end = t.rec.all('tunnel-end').map((e) => e.payload);
  assert.equal(end.length, 1);
  assert.equal(end[0].reason, 'place');
  t.std.refresh();
  assert.equal(t.std.placeOf(0), 2);
  assert.ok(t.list[1].st[S.s] > t.list[0].st[S.s], 'the leader is still ahead');
});

test('karts in the way are launched once (tunnel-ram), the rider is immune to traps', () => {
  const t = setup({ karts: [{ s: 100, speed: 20 }, { s: 130 }, { s: 3000 }, { s: 3100 }] });
  t.fire(0);
  t.list[0].speed = 0;
  t.tick(1.2);
  const rams = t.rec.all('tunnel-ram').map((e) => e.payload);
  assert.equal(rams.length, 1);
  assert.equal(rams[0].attacker, 0); assert.equal(rams[0].victim, 1);
  assert.ok(t.list[1].st[S.airborne] === 1 && t.list[1].st[S.stalled] === 0, 'launched, no stall');
  assert.equal(t.list[1].fxv.spinRad, 0);
  // a trap on the line does nothing to the rider
  t.list[3].item.id = 'melt'; t.list[3].input.item = true; t.tick(); t.list[3].input.item = false;
  const trapS = t.list[3].st[S.s];
  const before = t.rec.all('item-hit').length;
  for (let k = 0; k < 120 && t.items.tunnel.riding(0); k++) t.tick();
  assert.equal(t.rec.all('item-hit').filter((e) => e.payload.victim === 0).length, 0, 'no hit on the rider (trap at ' + trapS + ')');
  assert.ok(t.rec.all('item-hit').length >= before);
});

test('the end waits for solid railed road', () => {
  const open = (seg) => (seg >= 60 && seg < 75 ? [] : ['rail']);   // 600..750 m has no rails
  const t = setup({ track: { flags: open }, karts: [{ s: 450 }, { s: 3000 }, { s: 3100 }] });
  t.fire(0);
  // 4 s at ~1.6 x top ends near 450 + 4 * v: make sure the time runs out inside the open stretch
  const v = t.f.get('speed.topMps') * t.f.get('items.tunnelSpeedMul');
  t.f.set('items.tunnelS', (650 - 450) / v);
  t.tick(8);
  const end = t.rec.all('tunnel-end');
  assert.equal(end.length, 1);
  assert.ok(t.list[0].st[S.s] > 750, 'released past the open stretch: ' + t.list[0].st[S.s]);
});

test('a rider is never hit by a throw; at place 2 or better the press keeps the item', () => {
  const t = setup({ karts: [{ s: 100 }, { s: 90 }, { s: 3000 }, { s: 3100 }] });
  t.fire(0);
  assert.ok(t.items.tunnel.riding(0));
  t.fire(1, 'pendulum');
  t.tick(1);
  assert.equal(t.rec.all('item-hit').filter((e) => e.payload.victim === 0).length, 0);
  const u = setup({ karts: [{ s: 100 }, { s: 3000 }] });
  u.fire(0);
  assert.equal(u.list[0].item.id, 'tunnel');
  assert.equal(u.rec.all('item-refused')[0].payload.reason, 'place');
  assert.ok(!u.items.tunnel.riding(0));
});
