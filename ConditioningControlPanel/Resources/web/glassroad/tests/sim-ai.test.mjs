import { test } from 'node:test';
import assert from 'node:assert/strict';
import { S, RESPAWN } from '../sim/state.js';
import { createRacers } from '../sim/racers.js';
import { createAi } from '../sim/ai.js';
import { createActions } from '../core/actions.js';
import { qaxis, qmul } from '../core/math.js';
import { makeFeel, DT } from './sim-fake.mjs';
import { makeRace, threshold } from './sim-racers-harness.mjs';

const TR = threshold();
const FR = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] };

// ---- one full race of 8 on Threshold, fixed seed, run once and checked by several tests ----
const R = makeRace({ seed: 1, track: TR });
let railBreaches = 0;
const done = () => !!R.standings.result && R.racers.list.every((r) => r.finished);
for (let i = 0; i < 60 * 260 && !done(); i++) {
  R.tick();
  if (i % 6) continue;
  for (const r of R.racers.list) {
    const st = r.st;
    if (!st[S.grounded] || st[S.respawnPhase] !== RESPAWN.NONE) continue;
    const q = TR.query(st[S.posX], st[S.posY], st[S.posZ], st[S.seg] | 0);
    if (q.railed && q.edgeDist < -0.05) railBreaches++;
  }
}

test('race: all 7 bots finish 3 laps of Threshold on their own, at most 2 falls each', () => {
  for (const r of R.racers.list.slice(1)) {
    assert.equal(r.finished, true, r.meta.id + ' finished');
    assert.equal(r.lapTimes.length, TR.laps, r.meta.id + ' laps');
    assert.ok(r.falls <= 2, r.meta.id + ' falls ' + r.falls);
    for (const t of r.lapTimes) assert.ok(t > 30 && t < 75, r.meta.id + ' lap time ' + t);
  }
});

test('race: sane standings and one result', () => {
  const res = R.standings.result;
  assert.ok(res, 'result');
  assert.equal(R.events('race-result').length, 1);
  assert.equal(res.rows.length, 8);
  assert.deepEqual(res.rows.map((r) => r.place), [1, 2, 3, 4, 5, 6, 7, 8]);
  assert.deepEqual(new Set(res.rows.map((r) => r.i)).size, 8);
  for (let k = 1; k < 8; k++) assert.ok(res.rows[k].timeS >= res.rows[k - 1].timeS, 'times ascend');
  assert.equal(res.rows.filter((r) => r.me).length, 1);
  assert.equal(res.me.place, res.rows.find((r) => r.me).place);
  // home before the result: its exact time; still driving: the estimate lands near its real finish (a spread field)
  for (const row of res.rows) {
    const fin = R.racers.list[row.i].finishS;
    if (row.finished) assert.ok(Math.abs(row.timeS - fin) < 1e-6);
    else assert.ok(Math.abs(row.timeS - fin) < fin * 0.08, row.i + " estimate " + row.timeS.toFixed(1) + " vs " + fin.toFixed(1));
  }
  // live places agree with the result once everyone is home
  assert.deepEqual(R.standings.order, res.rows.map((r) => r.i));
  // a race, not a procession: places changed hands
  assert.ok(R.events('place-change').length > 0 && R.events('overtake').length > 0);
});

test('race: bots hop, drift and cash drift boosts, bump and slipstream; never through a rail', () => {
  const ev = (n) => R.botEvents(n);
  assert.ok(ev('hop').length > 0, 'hops');
  assert.ok(ev('drift-start').length > 0, 'drifts');
  assert.ok(ev('boost-start').some((e) => e.payload.p.kind === 'drift'), 'drift boosts');
  assert.ok(R.events('bump').length > 0, 'bumps');
  assert.ok(R.events('slipstream').some((e) => e.payload.state === 'fire'), 'slipstream fired');
  assert.equal(railBreaches, 0, 'grounded past a rail');
});

test('determinism: the same seed drives the same race', () => {
  const a = makeRace({ seed: 3, track: TR }), b = makeRace({ seed: 3, track: TR });
  a.run(12); b.run(12);
  for (let i = 0; i < 8; i++) assert.equal(a.racers.list[i].st[S.posX], b.racers.list[i].st[S.posX]);
});

// ---- unit checks on drive() ----
function rig({ count = 3, over = {}, seed = 1 } = {}) {
  const feel = makeFeel(over), q = { emit() {} };
  const racers = createRacers(TR, feel, q, { count, seed }), ai = createAi(TR, feel, { seed });
  const race = { phase: 'racing', timeS: 10, lapStartS: 0 };
  return { feel, racers, ai, race, out: Object.assign(createActions(), { item: false, back: false }) };
}

test('rubber band: soft, up when behind the player, down when ahead, only on top speed', () => {
  const { racers, ai, race, out, feel } = rig();
  const bot = racers.list[1], ctx = { racers: racers.list, race };
  racers.player.progressM = 500; bot.progressM = 0;
  for (let i = 0; i < 600; i++) ai.drive(bot, ctx, DT, out);
  assert.ok(bot.rubber > 1 + (feel.get('ai.rubberUpMul') - 1) * 0.6 && bot.rubber <= feel.get('ai.rubberUpMul') + 1e-9, 'behind ' + bot.rubber);
  racers.player.progressM = 0; bot.progressM = 500;
  for (let i = 0; i < 600; i++) ai.drive(bot, ctx, DT, out);
  assert.ok(bot.rubber < 1 - (1 - feel.get('ai.rubberDownMul')) * 0.6 && bot.rubber >= feel.get('ai.rubberDownMul') - 1e-9, 'ahead ' + bot.rubber);
  racers.player.finished = true;
  for (let i = 0; i < 900; i++) ai.drive(bot, ctx, DT, out);
  assert.ok(Math.abs(bot.rubber - 1) < 0.01, 'off once the player is home');
});

test('blind: steer noise and a lifted throttle', () => {
  const { racers, ai, race, out, feel } = rig();
  const bot = racers.list[1], ctx = { racers: racers.list, race };
  ai.drive(bot, ctx, DT, out);
  const clear = out.throttle;
  bot.mods.blind = 1;
  ai.drive(bot, ctx, DT, out);
  assert.ok(out.throttle <= clear * feel.get('ai.blindThrottle') + 1e-9);
});

test('items: hold, then fire at a kart ahead; cooldown; never before the hold time', () => {
  const { racers, ai, race, out, feel } = rig({ over: { 'ai.itemUseChancePerS': 50 } });
  const bot = racers.list[1], ctx = { racers: racers.list, race };
  bot.progressM = 100; racers.list[2].progressM = 130; racers.player.progressM = 0;
  bot.item.id = 'flash';
  const presses = [];
  for (let i = 0; i < 60 * 12; i++) { ai.drive(bot, ctx, DT, out); if (out.item) presses.push({ t: i * DT, back: out.back }); }
  assert.ok(presses.length >= 2, 'fired');
  assert.ok(presses[0].t >= feel.get('ai.itemHoldMinS') - 1e-9, 'held first');
  for (let k = 1; k < presses.length; k++) assert.ok(presses[k].t - presses[k - 1].t >= feel.get('ai.itemCooldownS') - DT);
  assert.ok(presses.every((p) => !p.back), 'a target ahead: throw forward');
});

test('items: a leader drops throws behind, lays traps only with someone behind, never Shatter near an open edge', () => {
  const { racers, ai, race, out } = rig({ over: { 'ai.itemUseChancePerS': 50, 'ai.itemDropChance': 1 } });
  const bot = racers.list[1], ctx = { racers: racers.list, race };
  bot.progressM = 500; bot.place = 1; racers.list[2].progressM = 480; racers.player.progressM = 0;
  bot.item.id = 'spiral';
  let back = 0;
  for (let i = 0; i < 60 * 3; i++) { ai.drive(bot, ctx, DT, out); if (out.item && out.back) back++; }
  assert.ok(back >= 1, 'dropped behind');
  const r2 = rig({ over: { 'ai.itemUseChancePerS': 50 } }), b2 = r2.racers.list[1];
  b2.item.id = 'melt'; b2.progressM = 500; r2.racers.list[2].progressM = 900; r2.racers.player.progressM = 900;
  let fired = 0;
  for (let i = 0; i < 60 * 4; i++) { r2.ai.drive(b2, { racers: r2.racers.list, race: r2.race }, DT, r2.out); if (r2.out.item) fired++; }
  assert.equal(fired, 0, 'no trap with nobody behind');
  const r3 = rig({ over: { 'ai.itemUseChancePerS': 50 } }), b3 = r3.racers.list[1];
  b3.item.id = 'shatter'; b3.progressM = 100; r3.racers.list[2].progressM = 120;
  const items = { nearOpenEdge: () => true, view: () => ({ traps: [] }) };
  fired = 0;
  for (let i = 0; i < 60 * 4; i++) { r3.ai.drive(b3, { racers: r3.racers.list, race: r3.race, items }, DT, r3.out); if (r3.out.item) fired++; }
  assert.equal(fired, 0, 'Shatter held near an open edge');
});

test('launch: by ai.launchChance at go', () => {
  const all = makeRace({ seed: 2, track: TR, feelOver: { 'ai.launchChance': 1 } });
  all.run(3);
  assert.equal(all.botEvents('launch-boost').length, 7);
  const none = makeRace({ seed: 2, track: TR, feelOver: { 'ai.launchChance': 0 } });
  none.run(3);
  assert.equal(none.botEvents('launch-boost').length, 0);
});

test('recovery: a bot facing the wrong way turns round and drives on', () => {
  const W = makeRace({ seed: 4, track: TR, count: 2 });
  W.run(2.5);                                         // through the countdown and the start ghost
  const bot = W.racers.list[1], st = bot.st;
  const up = TR.frameAt(st[S.s], FR).n.slice(), q = qmul([0, 0, 0, 1], qaxis([0, 0, 0, 1], up, Math.PI), [st[S.quatX], st[S.quatY], st[S.quatZ], st[S.quatW]]);
  st[S.quatX] = q[0]; st[S.quatY] = q[1]; st[S.quatZ] = q[2]; st[S.quatW] = q[3];
  st[S.velX] = st[S.velY] = st[S.velZ] = 0; st[S.engineV] = 0;
  const p0 = bot.progressM;
  W.run(12);
  assert.ok(bot.progressM > p0 + 100, 'moving forward again ' + (bot.progressM - p0));
});
