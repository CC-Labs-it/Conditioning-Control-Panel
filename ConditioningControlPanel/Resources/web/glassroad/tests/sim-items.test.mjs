// tests/sim-items.test.mjs - effect bubbles in the sim (CONTRACT-B1.md section 3): draws, pickups, throws,
// lobs, waves, traps, verdicts, mercy, Shatter edge rule, bot events.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { fakeTrack, place, makeFeel, recorder, acts, DT } from './sim-fake.mjs';
import { S } from '../sim/state.js';
import { createItems } from '../sim/items.js';
import { ITEM_IDS, ITEMS, bucketOf, weightsFor, rollItem, durationOf, makeRand } from '../sim/itemTable.js';

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
  const st = { order: [], progressOf: prog,
    placeOf: (i) => st.order.indexOf(i) + 1,
    gapToLeader: (i) => prog(st.order[0]) - prog(i) };
  st.refresh = () => { st.order = list.map((r) => r.i).sort((a, b) => prog(b) - prog(a)); return st; };
  return st.refresh();
}

function setup({ feel = {}, track = {}, karts = [{ s: 100 }, { s: 103 }] } = {}) {
  const tr = fakeTrack(track), f = makeFeel(feel), rec = recorder();
  const items = createItems(tr, f, rec, { seed: 7 });
  const list = karts.map((o, i) => racer(tr, i, o));
  const std = standings(list);
  const tick = (sec = DT) => { for (let n = Math.max(1, Math.round(sec / DT)); n > 0; n--) { rec.t += DT; items.update(DT, list, std.refresh(), RACE); } };
  const fire = (who, id, { back = false } = {}) => {
    const r = list[who];
    Object.assign(r.item, { id, rolling: false, rollT: 0, heldT: 0 });
    r.input.item = true; r.input.back = back;
    tick();
    r.input.item = false; r.input.back = false;
  };
  const hits = () => rec.all('item-hit').map((e) => e.payload);
  return { tr, f, rec, items, list, std, tick, fire, hits };
}

test('table: every id has a use, an icon and a duration; buckets from place and gap', () => {
  const f = makeFeel();
  for (const id of ITEM_IDS) {
    assert.ok(ITEMS[id].use && /^art\/fx\/.+\.png$/.test(ITEMS[id].icon), id);
    assert.ok(durationOf(f, id) > 0, id);
  }
  assert.equal(bucketOf(f, 1, 500), 'front');
  assert.equal(bucketOf(f, 4, 10), 'front');
  assert.equal(bucketOf(f, 4, 60), 'middle');
  assert.equal(bucketOf(f, 7, 200), 'back');
});

test('roll weights by bucket: only the toys kit draws; front gets Snap, the back Tether and Big Spiral', () => {
  const f = makeFeel();
  const ids = (b) => weightsFor(f, b).map(([id]) => id);
  const kit = ['pendulum', 'disc', 'tether', 'melt', 'snap', 'surge', 'bigSpiral'];
  for (const b of ['front', 'middle', 'back']) for (const id of ids(b)) assert.ok(kit.includes(id), id + ' still draws in ' + b);
  for (const big of ['tether', 'bigSpiral']) {
    assert.ok(!ids('front').includes(big), big + ' in front');
    assert.ok(ids('back').includes(big), big + ' missing at the back');
  }
  assert.ok(ids('front').includes('melt') && ids('front').includes('snap'));
  const rand = makeRand(3), n = 20000, count = {};
  for (let k = 0; k < n; k++) { const id = rollItem(f, 'front', rand); count[id] = (count[id] || 0) + 1; }
  const list = weightsFor(f, 'front'), total = list.reduce((a, [, w]) => a + w, 0);
  for (const [id, w] of list) assert.ok(Math.abs(count[id] / n - w / total) < 0.02, id);
  assert.equal(rollItem(f, 'back', () => 0.999999), weightsFor(f, 'back').at(-1)[0]);
});

test('box pickup: slot rolls rollS then shows; a full slot drives through', () => {
  const tr = Object.assign(fakeTrack(), { items: [{ kind: 'box', node: 50, x: 0, ds: 0, s: 500 }, { kind: 'box', node: 52, x: 0, ds: 0, s: 520 }] });
  const rec = recorder(), f = makeFeel(), items = createItems(tr, f, rec, { seed: 1 });
  const list = [racer(tr, 0, { s: 500 }), racer(tr, 1, { s: 520 })], std = standings(list);
  list[1].item.id = 'glass';
  items.update(DT, list, std, RACE);
  assert.equal(rec.all('item-box').length, 1);
  assert.equal(rec.first('item-box').payload.racer, 0);
  assert.ok(list[0].item.rolling && list[0].item.id);
  assert.equal(items.view().boxes[0].alive, false);
  assert.equal(items.view().boxes[1].alive, true, 'full slot leaves the box');
  for (let k = 0; k < Math.round(f.get('items.rollS') / DT); k++) items.update(DT, list, std, RACE);
  assert.equal(list[0].item.rolling, false);
  assert.equal(rec.first('item-roll').payload.id, list[0].item.id);
  assert.equal(items.view().held.length, 2);
});

test('falling below the road cannot collect boxes or bubbles through its underside', () => {
  const { list, rec, f } = setup({ karts: [{ s: 100, height: -5, airborne: true }] });
  // A separate pickup system puts one box and bubble at the same road position.
  const track = fakeTrack();
  track.items = [{ kind: 'box', s: 100, x: 0 }, { kind: 'bubble', s: 100, x: 0 }];
  const pickups = createItems(track, f, rec);
  const std = standings(list);
  pickups.update(DT, list, std, RACE);
  assert.equal(list[0].item.id, null);
  assert.equal(rec.all('bubble-pop').length, 0);
  assert.ok(pickups.view().boxes[0].alive);
  assert.ok(pickups.view().bubbles[0].alive);
  list[0].st[S.height] = 0;
  pickups.update(DT, list, std, RACE);
  assert.equal(rec.all('item-box').length, 1, 'touching road height still collects');
  assert.equal(rec.all('bubble-pop').length, 1);
});

test('pickup bubble pops for a small boost; bot events go out as racer-event', () => {
  const tr = fakeTrack();
  tr.items = [{ kind: 'bubble', node: 30, x: 1, ds: 0, s: 300 }, { kind: 'bubble', node: 60, x: 0, ds: 0, s: 600 }];
  const rec = recorder(), f = makeFeel(), items = createItems(tr, f, rec);
  const list = [racer(tr, 0, { s: 300, lateral: 1, speed: 20 }), racer(tr, 1, { s: 600, speed: 20 })];
  items.update(DT, list, standings(list), RACE);
  assert.equal(rec.all('bubble-pop').length, 2);
  const b = rec.first('boost-start').payload;
  assert.equal(b.kind, 'bubble'); assert.equal(b.amount, f.get('items.bubbleAmount'));
  const re = rec.first('racer-event').payload;
  assert.equal(re.racer, 1); assert.equal(re.name, 'boost-start'); assert.equal(re.p.kind, 'bubble');
});

test('projectile hits the kart ahead, not the one behind or the one farther on', () => {
  const t = setup({ karts: [{ s: 100 }, { s: 140, lateral: 2 }, { s: 60 }, { s: 400 }] });
  t.fire(0, 'flash');
  assert.equal(t.rec.first('item-fire').payload.target, 1);
  t.tick(3);
  const h = t.hits();
  assert.equal(h.length, 1);
  assert.equal(h[0].victim, 1); assert.equal(h[0].by, 0); assert.equal(h[0].verdict, 'hit');
  assert.equal(h[0].durationS, t.f.get('items.flashS'));
  assert.equal(t.list[0].item.id, null);
});

test('a throw that reaches nobody fizzles after its life; bots blinded by blind kinds', () => {
  const t = setup({ karts: [{ s: 100 }, { s: 900 }] });
  t.fire(0, 'brainDrain');
  t.tick(t.f.get('items.throwLifeS') + 0.1);
  assert.equal(t.hits().length, 0);
  assert.equal(t.items.view().projectiles.length, 0);
  assert.ok(t.items.view().pops.some((p) => p.at === 'fizzle'));
  const u = setup();
  u.fire(0, 'brainDrain');
  assert.equal(u.list[1].mods.blind, 1);
  assert.equal(u.list[1].fxv.effect, 'brainDrain');
});

test('replace, never stack: a new hit takes over the old one and its mods', () => {
  const t = setup();
  t.fire(0, 'spiral');
  t.tick(0.5);
  assert.notEqual(t.list[1].mods.steerAdd, 0);
  t.fire(0, 'freeze');
  t.tick(0.1);
  const m = t.list[1].mods;
  assert.equal(m.steerAdd, 0, 'spiral drift gone');
  assert.equal(m.steerMul, t.f.get('items.freezeSteerMul'));
  assert.equal(t.list[1].fxv.effect, 'freeze');
  assert.deepEqual(t.hits().map((h) => h.verdict), ['hit', 'hit']);
});

test('mercy: immune for mercyS after an effect ends', () => {
  const t = setup();
  t.fire(0, 'flash');
  t.tick(t.f.get('items.flashS') + t.f.get('items.effectOutS') + 0.1);
  t.fire(0, 'flash');
  assert.equal(t.hits()[1].verdict, 'mercy');
  assert.equal(t.hits()[1].durationS, 0);
  t.tick(t.f.get('items.mercyS'));
  t.fire(0, 'flash');
  assert.equal(t.hits()[2].verdict, 'hit');
});

test('mercy: at most viewBudgetS of blocked view per window, trimmed, then refused', () => {
  const t = setup();
  t.fire(0, 'brainDrain');            // 2.5 s
  t.tick(1.5);
  t.fire(0, 'brainDrain');            // replaces; 1.5 used, trimmed to 1.5
  t.tick(1.0);
  t.fire(0, 'brainDrain');            // 2.5 used, 0.5 left
  t.tick(0.3);
  t.fire(0, 'brainDrain');            // 0.2 left, under minHitS
  const h = t.hits();
  assert.deepEqual(h.map((x) => x.verdict), ['hit', 'hit', 'hit', 'mercy']);
  assert.ok(Math.abs(h[1].durationS - 1.5) < 0.05, 'trimmed ' + h[1].durationS);
  assert.ok(Math.abs(h[2].durationS - 0.5) < 0.05, 'trimmed ' + h[2].durationS);
  // blocked time actually spent inside the window never passes the budget
  const spent = 1.5 + 1.0 + Math.min(h[2].durationS, 0.3 + DT * 2);
  assert.ok(spent <= t.f.get('items.viewBudgetS') + 0.05);
});

test('mild kinds count mildBlockMul against the budget', () => {
  const t = setup({ feel: { 'items.pinkS': 8 } });
  t.list[1].st[S.s] = 120;
  t.fire(0, 'pink');
  const h = t.hits()[0];
  assert.equal(h.verdict, 'hit');
  assert.ok(Math.abs(h.durationS - t.f.get('items.viewBudgetS') / t.f.get('items.mildBlockMul')) < 1e-6);
});

test('Glass soaks one hit, then the next lands', () => {
  const t = setup();
  t.fire(1, 'glass');
  assert.equal(t.list[1].fxv.shield, 'glass');
  t.fire(0, 'spiral');
  t.fire(0, 'spiral');
  assert.deepEqual(t.hits().map((h) => h.verdict), ['glass', 'hit']);
  assert.equal(t.list[1].fxv.shield, null);
});

test('Golden ignores all hits for goldenS and shoves harder', () => {
  const t = setup();
  t.list[1].st[S.s] = 103;
  t.fire(1, 'golden');
  assert.equal(t.list[1].fxv.shield, 'golden');
  assert.equal(t.list[1].mods.shoveMul, t.f.get('items.goldenShoveMul'));
  for (const id of ['flash', 'shatter', 'melt']) t.fire(0, id === 'melt' ? 'flash' : id);
  assert.ok(t.hits().every((h) => h.verdict === 'golden'), JSON.stringify(t.hits()));
  assert.equal(t.list[1].st[S.stalled], 0);
  t.tick(t.f.get('items.goldenS'));
  assert.equal(t.list[1].fxv.shield, null);
  assert.equal(t.list[1].mods.shoveMul, 1);
});

test('Echo: throws go for the copies first', () => {
  const t = setup({ feel: { 'items.echoDecoyChance': 1 } });
  t.fire(1, 'echo');
  assert.equal(t.list[1].fxv.echo, 3);
  for (let k = 0; k < 4; k++) { t.fire(0, 'flash'); t.tick(0.2); }
  assert.deepEqual(t.hits().map((h) => h.verdict), ['decoy', 'decoy', 'decoy', 'hit']);
  assert.equal(t.list[1].fxv.echo, 0);
  assert.ok(t.items.view().pops.length > 0);
});

test('Shatter: refused near an open edge, fires and spins elsewhere', () => {
  const flags = (i) => (i >= 20 && i < 22 ? ['open'] : ['rail']);
  const t = setup({ track: { flags }, karts: [{ s: 180 }, { s: 183, speed: 20, engine: 20 }] });
  assert.equal(t.items.nearOpenEdge(180, 40), true);
  assert.equal(t.items.nearOpenEdge(255, 40), true);
  assert.equal(t.items.nearOpenEdge(300, 40), false);
  t.fire(0, 'shatter');
  assert.equal(t.rec.first('item-refused').payload.reason, 'edge');
  assert.equal(t.list[0].item.id, 'shatter', 'kept');
  assert.equal(t.hits().length, 0);
  const u = setup({ track: { flags }, karts: [{ s: 1000 }, { s: 1003, speed: 20, engine: 20 }] });
  u.fire(0, 'shatter');
  assert.equal(u.hits()[0].verdict, 'hit');
  const v = u.list[1];
  assert.ok(v.st[S.stalled] > 0);
  assert.ok(Math.abs(v.st[S.engineV] - 20 * u.f.get('items.shatterSpeedKeep')) < 1e-3);
  u.tick(0.5);
  assert.ok(v.fxv.spinRad > 0 && v.fxv.effect === 'shatter');
});

test('Shatter throw waits behind a victim near an open edge and never lands there', () => {
  const flags = (i) => (i >= 20 && i < 22 ? ['open'] : ['rail']);
  const t = setup({ track: { flags }, karts: [{ s: 140 }, { s: 235 }] });   // user 50 m clear, victim 25 m past the edge
  t.list[0].st[S.speedPct] = 1;                                                // throw flies at 52 m/s
  t.fire(0, 'shatter');
  assert.equal(t.rec.all('item-fire').length, 1);
  t.tick(2);
  assert.equal(t.hits().length, 0);
  const pr = t.items.view().projectiles[0];
  assert.ok(pr && pr.s < 235, 'waiting behind');
  t.list[1].st[S.s] = 270;                                                     // victim drives clear
  t.tick(2.2);
  assert.equal(t.hits()[0].victim, 1);
});

test('lob reaches the leader in lobFlightS; a leader lobs at place 2', () => {
  const t = setup({ karts: [{ s: 100 }, { s: 300 }, { s: 500 }] });
  t.fire(0, 'blackout');
  assert.equal(t.rec.first('item-fire').payload.target, 2);
  t.tick(t.f.get('items.lobFlightS') - 0.2);
  assert.equal(t.hits().length, 0);
  assert.ok(t.items.view().projectiles[0].p[1] > 5, 'high arc');
  t.tick(0.3);
  assert.equal(t.hits()[0].victim, 2);
  const u = setup({ karts: [{ s: 500 }, { s: 300 }, { s: 100 }] });
  u.fire(0, 'blackout');
  assert.equal(u.rec.first('item-fire').payload.target, 1);
});

test('Pink hits every kart ahead within pinkRangeM, nobody behind', () => {
  const t = setup({ karts: [{ s: 100 }, { s: 110 }, { s: 135 }, { s: 160 }, { s: 90 }] });
  t.fire(0, 'pink');
  assert.deepEqual(t.hits().map((h) => h.victim).sort(), [1, 2]);
  assert.equal(t.items.view().waves.length, 1);
});

test('Melt is laid behind; the layer is safe while it arms; a kart over it slides on ice', () => {
  const t = setup({ karts: [{ s: 100 }, { s: 50 }] });
  t.fire(0, 'melt');
  assert.equal(t.rec.first('item-fire').payload.mode, 'drop');
  const trap = t.items.view().traps[0];
  assert.ok(Math.abs(trap.s - (100 - t.f.get('items.dropBehindM'))) < 1e-6);
  assert.equal(t.hits().length, 0);
  t.list[1].st[S.s] = trap.s;
  t.tick();
  const h = t.hits()[0];
  assert.equal(h.victim, 1); assert.equal(h.kind, 'melt'); assert.equal(h.verdict, 'hit');
  assert.equal(t.list[1].mods.gripMul, t.f.get('items.meltGripMul'));
  assert.equal(t.list[1].mods.slideMul, t.f.get('items.meltSlideMul'));
  assert.equal(t.items.view().traps.length, 0);
});

test('back + item drops a throw as a trap behind; maxTraps keeps the newest', () => {
  const t = setup({ feel: { 'items.maxTraps': 2 }, karts: [{ s: 100 }, { s: 900 }] });
  t.fire(0, 'flash', { back: true });
  assert.equal(t.rec.first('item-fire').payload.mode, 'drop');
  t.fire(0, 'freeze', { back: true });
  t.fire(0, 'spiral', { back: true });
  assert.deepEqual(t.items.view().traps.map((x) => x.kind), ['freeze', 'spiral']);
});

test('Surge boosts by bucket; reset clears flights, traps and shields', () => {
  const t = setup({ karts: [{ s: 100, speed: 20, engine: 20 }, { s: 103 }] });
  t.fire(0, 'surge');
  const b = t.rec.first('boost-start').payload;
  assert.equal(b.kind, 'surge'); assert.equal(b.amount, t.f.get('items.surgeAmount')[0], 'front bucket');
  t.fire(1, 'glass');
  t.fire(0, 'melt');
  t.items.reset();
  const v = t.items.view();
  assert.equal(v.traps.length + v.projectiles.length, 0);
  t.tick();
  assert.equal(t.list[1].fxv.shield, null);
});

test('nothing runs before the green', () => {
  const t = setup();
  t.list[0].item.id = 'flash'; t.list[0].input.item = true;
  t.items.update(DT, t.list, t.std, { phase: 'countdown' });
  assert.equal(t.rec.list.length, 0);
});

test('budget property: a barrage on one kart never blocks more than viewBudgetS in any viewWindowS', () => {
  const t = setup({ karts: [{ s: 100 }, { s: 103 }] });
  const f = t.f, rand = makeRand(11), kinds = ['flash', 'spiral', 'brainDrain', 'freeze', 'pink'];
  for (let k = 0; k < 120; k++) { t.fire(0, kinds[Math.floor(rand() * kinds.length)]); t.tick(0.1 + rand() * 0.6); }
  const landed = t.rec.all('item-hit').filter((e) => e.payload.verdict === 'hit');
  assert.ok(landed.length > 5 && t.hits().some((h) => h.verdict === 'mercy'));
  const blocks = landed.map((e, j) => {
    const end = Math.min(e.t + e.payload.durationS, j + 1 < landed.length ? landed[j + 1].t : Infinity);
    const mul = ITEMS[e.payload.kind].view === 'mild' ? f.get('items.mildBlockMul') : 1;
    return [e.t, end, mul];
  });
  const W = f.get('items.viewWindowS');
  for (const [a] of blocks) {
    let used = 0;
    for (const [s, e, mul] of blocks) used += mul * Math.max(0, Math.min(e, a + W) - Math.max(s, a));
    assert.ok(used <= f.get('items.viewBudgetS') + 0.05, 'window at ' + a.toFixed(2) + ' used ' + used.toFixed(2));
  }
});

test('threshold: boxes and bubbles load; the open corner and the jump count as open edges', async () => {
  const { readFileSync } = await import('node:fs');
  const { loadTrack } = await import('../track/load.js');
  const tr = loadTrack(JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8')));
  const items = createItems(tr, makeFeel(), recorder());
  const v = items.view();
  assert.equal(v.boxes.length, tr.items.filter((i) => i.kind === 'box').length);
  assert.equal(v.bubbles.length, tr.items.filter((i) => i.kind === 'bubble').length);
  for (const b of v.boxes) assert.ok(b.p.every(Number.isFinite) && b.alive && b.k === 1);
  let openNode = -1;
  for (let i = 0; i < tr.nodeCount; i++) if (tr.nodeFlags(i).includes('open')) { openNode = i; break; }
  assert.ok(items.nearOpenEdge(tr.nodeS[openNode] + 1, 40));
  assert.ok(items.nearOpenEdge(tr.jumps[0].s - 20, 40), 'the jump gap counts');
  assert.equal(items.nearOpenEdge(tr.spawnPose(0).s, 40), false, 'the start straight is railed');
});
