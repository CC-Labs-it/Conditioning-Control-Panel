// tests/sim-toys.test.mjs - the toys kit (owner pick 2026-10-25): Pendulum, Disc, Tether, Snap and Big Spiral.
// None of them covers the screen; each acts on the kart.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { fakeTrack, place, makeFeel, recorder, acts, DT } from './sim-fake.mjs';
import { S } from '../sim/state.js';
import { createItems } from '../sim/items.js';
import { ITEMS } from '../sim/itemTable.js';
import { WIRE_ITEMS } from '../net/protocol.js';

const RACE = { phase: 'racing' };

function racer(track, i, opts = {}) {
  return { i, meta: { i, id: i ? 'bot' + i : 'you', name: i ? 'EMI bot' : 'You', isBot: i > 0, tint: i, slot: i },
    st: place(track, opts), input: acts({ item: false, back: false }),
    mods: { steerMul: 1, steerAdd: 0, throttleMul: 1, gripMul: 1, slideMul: 1, capMul: 1, shoveMul: 1, blind: 0 },
    item: { id: null, rolling: false, rollT: 0, heldT: 0 },
    fxv: { spinRad: 0, shield: null, echo: 0, effect: null, ghost: false, slip: 0, place: 0 }, finished: false };
}

function setup({ feel = {}, track = {}, karts = [{ s: 100 }, { s: 120 }] } = {}) {
  const tr = fakeTrack(track), f = makeFeel(feel), rec = recorder();
  const items = createItems(tr, f, rec, { seed: 7 });
  const list = karts.map((o, i) => racer(tr, i, o));
  const prog = (i) => list[i].st[S.s];
  const std = { order: [], progressOf: prog, placeOf: (i) => std.order.indexOf(i) + 1, gapToLeader: (i) => prog(std.order[0]) - prog(i) };
  const refresh = () => { std.order = list.map((r) => r.i).sort((a, b) => prog(b) - prog(a)); return std; };
  const tick = (sec = DT) => { for (let n = Math.max(1, Math.round(sec / DT)); n > 0; n--) { rec.t += DT; items.update(DT, list, refresh(), RACE); } };
  const fire = (who, id, { back = false } = {}) => {
    const r = list[who];
    Object.assign(r.item, { id, rolling: false, rollT: 0, heldT: 0 });
    r.input.item = true; r.input.back = back;
    tick();
    r.input.item = false; r.input.back = false;
  };
  const hits = () => rec.all('item-hit').map((e) => e.payload);
  return { tr, f, rec, items, list, tick, fire, hits };
}

const fly = (t, who, sec) => t.tick(sec);

test('the toys never cover the screen and keep wire order after the first twelve', () => {
  for (const id of ['pendulum', 'disc', 'tether', 'snap', 'bigSpiral', 'melt']) assert.ok(ITEMS[id].view === 'none' || ITEMS[id].view === null, id);
  assert.deepEqual(WIRE_ITEMS.slice(12), ['pendulum', 'disc', 'tether', 'snap', 'bigSpiral']);
});

test('Pendulum homes on the kart ahead and swings its steering, no stall', () => {
  const t = setup();
  t.fire(0, 'pendulum');
  fly(t, 0, 1.5);
  const h = t.hits()[0];
  assert.equal(h.victim, 1); assert.equal(h.kind, 'pendulum'); assert.equal(h.verdict, 'hit');
  assert.equal(h.durationS, t.f.get('items.pendulumS'));
  const seen = [];
  for (let k = 0; k < 40; k++) { t.tick(); seen.push(t.list[1].mods.steerAdd); }
  assert.ok(Math.max(...seen) > 0.2 && Math.min(...seen) < -0.2, 'it swings both ways');
  assert.equal(t.list[1].st[S.stalled] > 0, false);
});

test('Disc banks off railed edges, pops off open ones, and a hit stalls and spins', () => {
  const t = setup({ karts: [{ s: 100 }, { s: 400, lateral: 5 }] });
  t.list[0].input.steer = 1;
  t.fire(0, 'disc');
  fly(t, 0, 1.4);
  assert.ok(t.rec.all('disc-bounce').length >= 1, 'banked off a rail');
  const open = setup({ track: { flags: () => ['open'] }, karts: [{ s: 100 }] });
  open.fire(0, 'disc'); fly(open, 0, 1.2);
  assert.equal(open.rec.all('disc-bounce').length, 0);
  assert.equal(open.items.view().projectiles.length, 0, 'fell off the open edge');
  const hit = setup({ karts: [{ s: 100 }, { s: 115 }] });
  hit.list[1].st[S.engineV] = 20;
  hit.fire(0, 'disc'); fly(hit, 0, 0.6);
  const h = hit.hits().find((e) => e.kind === 'disc');
  assert.ok(h && h.victim === 1 && h.verdict === 'hit');
  assert.ok(hit.list[1].st[S.stalled] > 0 && hit.list[1].st[S.engineV] <= 10 + 1e-9);
  hit.tick(0.3);
  assert.ok(hit.list[1].fxv.spinRad > 0);
});

test('Tether hooks the kart ahead, tows and drags, holds through a drift and spins the victim once passed', () => {
  const t = setup({ karts: [{ s: 100 }, { s: 120 }] });
  t.fire(0, 'tether');
  assert.equal(t.rec.all('tether-hook').length, 1);
  assert.equal(t.list[0].item.id, null);
  t.tick();
  assert.ok(t.list[0].mods.capMul > 1 && t.list[1].mods.capMul < 1);
  assert.equal(t.items.view().tethers.length, 1);
  t.list[1].st[S.driftDir] = 1; t.tick(0.2);
  assert.equal(t.rec.all('tether-break').length, 0, 'a drift does not break it');
  t.list[0].st[S.s] = 125; t.tick();
  const br = t.rec.first('tether-break').payload;
  assert.equal(br.reason, 'pass');
  const h = t.hits().find((e) => e.kind === 'tether');
  assert.equal(h.victim, 1); assert.equal(h.by, 0);
  assert.ok(t.list[1].st[S.stalled] > 0);
  assert.equal(t.items.view().tethers.length, 0);
});

test('Tether keeps the item with nobody close, lets go after tetherMaxS and when the gap opens', () => {
  const far = setup({ karts: [{ s: 100 }, { s: 200 }] });
  far.fire(0, 'tether');
  assert.equal(far.list[0].item.id, 'tether');
  const t = setup({ karts: [{ s: 100 }, { s: 120 }] });
  t.fire(0, 'tether'); t.tick(t.f.get('items.tetherMaxS') + 0.1);
  assert.equal(t.rec.first('tether-break').payload.reason, 'timeout');
  assert.equal(t.hits().length, 0);
  const g = setup({ karts: [{ s: 100 }, { s: 120 }] });
  g.fire(0, 'tether'); g.list[1].st[S.s] = 100 + g.f.get('items.tetherBreakM') + 5; g.tick();
  assert.equal(g.rec.first('tether-break').payload.reason, 'range');
});

test('Snap shoves close karts away, breaks a Tether on you and pops what reaches you for snapGuardS', () => {
  const s = setup({ karts: [{ s: 100 }, { s: 104, lateral: 1 }] });
  s.fire(0, 'tether');
  assert.equal(s.items.view().tethers.length, 1);
  s.fire(1, 'snap');
  assert.equal(s.rec.first('tether-break').payload.reason, 'snap');
  const shove = s.hits().find((e) => e.kind === 'snap');
  assert.equal(shove.victim, 0);
  assert.ok(s.list[0].mods.steerAdd < 0, 'pushed away from the snapper (to the left)');
  assert.equal(s.rec.all('snap-burst').length, 1);
  assert.equal(s.items.view().guards.length, 1);
  const g = setup({ karts: [{ s: 100 }, { s: 115 }] });
  g.fire(1, 'snap'); g.fire(0, 'pendulum'); fly(g, 0, 0.6);
  const h = g.hits().find((e) => e.kind === 'pendulum');
  assert.equal(h.verdict, 'snapped');
});

test('Big Spiral lobs at the leader and slows it for bigSpiralS', () => {
  const t = setup({ karts: [{ s: 100 }, { s: 160 }] });
  t.fire(0, 'bigSpiral');
  fly(t, 0, t.f.get('items.lobFlightS') + 0.2);
  const h = t.hits().find((e) => e.kind === 'bigSpiral');
  assert.ok(h && h.victim === 1 && h.verdict === 'hit');
  t.tick();
  assert.equal(t.list[1].mods.capMul, t.f.get('items.bigSpiralCapMul'));
  t.tick(t.f.get('items.bigSpiralS') + 0.5);
  assert.equal(t.list[1].mods.capMul, 1);
});

test('Melt is the bigger spiral trap and no longer blurs the view', () => {
  const t = setup({ karts: [{ s: 100 }, { s: 40 }] });
  t.fire(0, 'melt');
  const trap = t.items.view().traps[0];
  assert.equal(trap.radiusM, 5.4);
  assert.ok(t.f.get('items.dropBehindM') > trap.radiusM, 'laid clear of the layer');
  t.list[1].st[S.s] = trap.s; t.tick();
  const h = t.hits()[0];
  assert.equal(h.kind, 'melt'); assert.equal(ITEMS.melt.view, 'none');
});

test('Snap never shoves a kart near an open edge, and a Tether passed there lets go without the spin', () => {
  const s = setup({ track: { flags: () => ['open'] }, karts: [{ s: 100 }, { s: 104, lateral: 1 }] });
  s.fire(1, 'snap');
  assert.equal(s.hits().filter((e) => e.kind === 'snap').length, 0);
  const t = setup({ track: { flags: () => ['open'] }, karts: [{ s: 100 }, { s: 120 }] });
  t.fire(0, 'tether'); t.tick(); t.list[0].st[S.s] = 125; t.tick();
  assert.equal(t.rec.first('tether-break').payload.reason, 'pass');
  assert.equal(t.hits().length, 0);
});
