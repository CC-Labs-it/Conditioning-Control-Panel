// tests/sim-fix-p1.test.mjs - polish pass 1, SIMFIX lane: the items view is built once per tick into pooled
// rows, bots read the live traps through trapRows (s and x, no track.query), a leading bot fires Blackout at
// place 2, lobs skip finished racers, and feel gaps are read without a throw per tick.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { fakeTrack, place, makeFeel, recorder, acts, DT } from './sim-fake.mjs';
import { S } from '../sim/state.js';
import { createItems } from '../sim/items.js';
import { createAi } from '../sim/ai.js';
import { createRacers, createFeelOverlay } from '../sim/racers.js';
import { createActions } from '../core/actions.js';
import { gap } from '../sim/gaps.js';
import { makeRace, threshold } from './sim-racers-harness.mjs';

const RACE = { phase: 'racing' };

function racer(track, i, opts = {}) {
  return { i, meta: { i, id: i ? 'bot' + i : 'you', name: i ? 'EMI bot' : 'You', isBot: i > 0, tint: i, slot: i },
    st: place(track, opts), input: acts({ item: false, back: false }),
    mods: { steerMul: 1, steerAdd: 0, throttleMul: 1, gripMul: 1, slideMul: 1, capMul: 1, shoveMul: 1, blind: 0 },
    item: { id: null, rolling: false, rollT: 0, heldT: 0 },
    fxv: { spinRad: 0, shield: null, echo: 0, effect: null, ghost: false, slip: 0, place: 0 }, finished: false };
}

function setup({ karts = [{ s: 100 }, { s: 103 }], track = {} } = {}) {
  const tr = fakeTrack(track), f = makeFeel(), rec = recorder();
  let frames = 0;
  const frameAt = tr.frameAt;
  tr.frameAt = (s, out) => { frames++; return frameAt(s, out); };
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
  return { tr, f, rec, items, list, tick, fire, frames: () => frames };
}

test('items view: built once per tick into the same pooled rows; trapRows carry s and x', () => {
  const t = setup({ karts: [{ s: 100 }, { s: 200 }] });
  t.fire(0, 'melt');
  t.fire(0, 'spiral', { back: true });
  const v1 = t.items.view(), row = v1.traps[0];
  assert.equal(v1.traps.length, 2);
  const f0 = t.frames();
  for (let k = 0; k < 7; k++) assert.equal(t.items.view(), v1, 'same object each call');
  assert.equal(t.frames(), f0, 'no rebuild inside one tick');
  t.tick();
  const v2 = t.items.view();
  assert.equal(v2.traps[0], row, 'pooled row reused across ticks');
  assert.ok(t.frames() > f0, 'rebuilt after a tick');
  const rows = t.items.trapRows();
  assert.equal(rows.length, 2);
  for (let k = 0; k < 2; k++) {
    assert.equal(rows[k].s, v2.traps[k].s); assert.equal(rows[k].x, v2.traps[k].x);
    assert.ok(Array.isArray(v2.traps[k].p) && Array.isArray(v2.traps[k].n));
  }
  t.items.reset();
  assert.equal(t.items.view().traps.length, 0, 'reset rebuilds');
});

test('bots read traps from trapRows: no items.view(), no track.query, and they steer round it', () => {
  const TR = threshold();
  let queries = 0;
  const q = TR.query;
  const drive = (traps) => {
    const feel = makeFeel(), racers = createRacers(TR, feel, { emit() {} }, { count: 2, seed: 1 }), ai = createAi(TR, feel, { seed: 1 });
    const bot = racers.list[1], out = Object.assign(createActions(), { item: false, back: false });
    const items = { trapRows: () => traps(bot), view() { throw new Error('view() called by a bot'); }, nearOpenEdge: () => false };
    TR.query = (...a) => { queries++; return q(...a); };
    try { ai.drive(bot, { racers: racers.list, race: { phase: 'racing', timeS: 10, lapStartS: 0 }, items }, DT, out); } finally { TR.query = q; }
    return out.steer;
  };
  const clear = drive(() => []);
  const wall = (bot) => Array.from({ length: 9 }, (_, k) => ({ id: k + 1, kind: 'melt', s: bot.st[S.s] + 12, x: k - 4, t: 1, layer: 0 }));
  const blocked = drive(wall);
  assert.equal(queries, 0, 'no track.query for traps');
  assert.notEqual(blocked, clear, 'the line moves off the trap');
  // an old items object with only view() still works through the view rows' s and x
  const feel = makeFeel(), racers = createRacers(TR, feel, { emit() {} }, { count: 2, seed: 1 }), ai = createAi(TR, feel, { seed: 1 });
  const bot = racers.list[1], out = Object.assign(createActions(), { item: false, back: false });
  const view = { traps: wall(bot).map((t) => ({ ...t, p: [0, 0, 0] })) };
  ai.drive(bot, { racers: racers.list, race: { phase: 'racing', timeS: 10, lapStartS: 0 }, items: { view: () => view } }, DT, out);
  assert.equal(out.steer, blocked);
});

test('a full race: bots never call items.view(), traps or not', () => {
  const R = makeRace({ seed: 2, items: createItems });
  let calls = 0;
  const v = R.items.view;
  R.items.view = () => { calls++; return v(); };
  R.run(40);
  assert.equal(calls, 0);
  assert.ok(R.events('item-fire').length > 0, 'items were used');
});

function aiRig(count = 3) {
  const TR = threshold(), feel = makeFeel({ 'ai.itemUseChancePerS': 50 });
  const racers = createRacers(TR, feel, { emit() {} }, { count, seed: 1 }), ai = createAi(TR, feel, { seed: 1 });
  return { racers, ai, race: { phase: 'racing', timeS: 10, lapStartS: 0 }, out: Object.assign(createActions(), { item: false, back: false }) };
}
const presses = (g, bot, secs = 3) => {
  let n = 0;
  for (let i = 0; i < Math.round(secs / DT); i++) { g.ai.drive(bot, { racers: g.racers.list, race: g.race }, DT, g.out); if (g.out.item && !g.out.back) n++; }
  return n;
};

test('a leading bot fires Blackout at a chaser in range; holds it with nobody racing behind', () => {
  const g = aiRig(), bot = g.racers.list[1];
  bot.item.id = 'blackout'; bot.place = 1; bot.progressM = 500; g.racers.list[2].progressM = 470; g.racers.player.progressM = 0;
  assert.ok(presses(g, bot) >= 1, 'leader lobs at place 2');
  const h = aiRig(), b2 = h.racers.list[1];
  b2.item.id = 'blackout'; b2.place = 1; b2.progressM = 500; h.racers.list[2].progressM = 470; h.racers.list[2].finished = true;
  h.racers.player.progressM = 470; h.racers.player.finished = true;
  assert.equal(presses(h, b2), 0, 'finished racers are not targets');
  const k = aiRig(), b3 = k.racers.list[1];   // behind a finished kart only: that kart is no target either
  b3.item.id = 'flash'; b3.place = 2; b3.progressM = 500; k.racers.list[2].progressM = 520; k.racers.list[2].finished = true; k.racers.player.progressM = 0;
  assert.equal(presses(k, b3), 0, 'no throw at a finished kart');
});

test('lob targets skip finished racers: the first still racing, place 2 when the user leads', () => {
  const t = setup({ karts: [{ s: 100 }, { s: 300 }, { s: 500 }] });
  t.list[2].finished = true;
  t.fire(0, 'blackout');
  assert.equal(t.rec.first('item-fire').payload.target, 1, 'past the finished leader');
  const u = setup({ karts: [{ s: 500 }, { s: 300 }, { s: 100 }] });
  u.list[1].finished = true;
  u.fire(0, 'blackout');
  assert.equal(u.rec.first('item-fire').payload.target, 2, 'leader skips a finished place 2');
  const w = setup({ karts: [{ s: 500 }, { s: 300 }] });
  w.list[1].finished = true;
  w.fire(0, 'blackout');
  assert.equal(w.rec.all('item-fire').length, 0, 'nobody racing: kept');
  assert.equal(w.list[0].item.id, 'blackout');
});

test('feel gaps: a missing key is read once, then served without a throw; an overlay key still wins', () => {
  const base = makeFeel();
  let gets = 0;
  const feel = { get: (k) => { gets++; return base.get(k); } };
  const a = gap(feel, 'kart.radiusM');
  for (let k = 0; k < 50; k++) assert.equal(gap(feel, 'kart.radiusM'), a);
  assert.equal(gets, 1);
  const o = createFeelOverlay(base);
  assert.equal(gap(o, 'kart.gripTcS'), gap(base, 'kart.gripTcS'));
  o.over['kart.gripTcS'] = 0.123;
  assert.equal(gap(o, 'kart.gripTcS'), 0.123, 'overlay value after a cached miss');
  delete o.over['kart.gripTcS'];
  assert.equal(gap(o, 'kart.gripTcS'), gap(base, 'kart.gripTcS'));
});
