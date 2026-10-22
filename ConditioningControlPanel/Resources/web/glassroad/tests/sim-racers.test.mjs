import { test } from 'node:test';
import assert from 'node:assert/strict';
import { S, RESPAWN } from '../sim/state.js';
import { createRacers, createFeelOverlay } from '../sim/racers.js';
import { createRace } from '../sim/race.js';
import { createActions } from '../core/actions.js';
import { makeFeel, fakeTrack, DT } from './sim-fake.mjs';
import { threshold } from './sim-racers-harness.mjs';

const queue = () => { const q = { list: [], emit(name, payload) { q.list.push({ name, payload }); } }; return q; };
const go = (race, a, st) => { race.start(); for (let i = 0; i < 400 && race.phase !== 'racing'; i++) race.update(DT, a, st); };

test('overlay: own values first, the base for the rest, writes pass through', () => {
  const feel = makeFeel(), o = createFeelOverlay(feel);
  o.over['speed.topMps'] = 12;
  assert.equal(o.get('speed.topMps'), 12);
  assert.equal(o.get('speed.reverseMps'), feel.get('speed.reverseMps'));
  assert.ok(Math.abs(o.rad('steer.yawRateDegps') - feel.rad('steer.yawRateDegps')) < 1e-9);
  o.set('speed.reverseMps', 7);
  assert.equal(feel.get('speed.reverseMps'), 7);
  assert.throws(() => o.get('nope.key'));
});

test('grid: the player takes ai.playerSlot, bots fill the rest in order', () => {
  const feel = makeFeel(), tr = threshold(), r = createRacers(tr, feel, queue(), { count: 8 });
  assert.equal(r.list.length, 8);
  assert.equal(r.player, r.list[0]);
  const ps = feel.get('ai.playerSlot');
  assert.equal(r.player.meta.slot, ps);
  assert.deepEqual(r.list.slice(1).map((x) => x.meta.slot), [0, 1, 2, 3, 4, 5, 6, 7].filter((k) => k !== ps));
  assert.deepEqual(r.list.map((x) => x.meta.id), ['you', 'bot1', 'bot2', 'bot3', 'bot4', 'bot5', 'bot6', 'bot7']);
  assert.ok(r.list.slice(1).every((x) => x.meta.isBot && x.meta.name === 'EMI bot'));
  for (const x of r.list) {
    const p = tr.spawnPose(x.meta.slot);
    assert.ok(Math.hypot(x.st[S.posX] - p.p[0], x.st[S.posY] - p.p[1], x.st[S.posZ] - p.p[2]) < 1e-3, 'on its grid pose');
  }
});

test('step: nobody moves before go; mods shape act; the overlay carries cap and assist', () => {
  const feel = makeFeel(), tr = threshold(), q = queue(), r = createRacers(tr, feel, q);
  const race = createRace(tr, feel, q), a = Object.assign(createActions(), { throttle: 1, steer: 0.5 });
  const before = r.list.map((x) => x.st[S.posX]);
  race.start();
  r.step(DT, a, race, {});
  assert.deepEqual(r.list.map((x) => x.st[S.posX]), before, 'countdown: still');
  const m = r.player.mods;
  m.steerMul = 0.4; m.steerAdd = 0.1; m.throttleMul = 0.5; m.capMul = 0.8; m.gripMul = 6;
  r.setAssist(true);
  r.step(DT, a, race, {});
  assert.ok(Math.abs(r.player.act.steer - (0.5 * 0.4 + 0.1)) < 1e-9);
  assert.equal(r.player.act.throttle, 0.5);
  assert.equal(r.player.feel.get('speed.topMps'), feel.get('speed.topMps') * 0.8);
  assert.equal(r.player.feel.get('gentle.on'), true);
  assert.equal(feel.get('gentle.on'), false, 'assist never leaks into the base feel');
  r.setAssist(false);
  r.step(DT, a, race, {});
  assert.equal(r.player.feel.get('gentle.on'), false);
});

test('bots: pace from ai.paceMul x difficulty; events re-emitted as racer-event; laps recorded', () => {
  const feel = makeFeel({ 'ai.difficulty': 2 }), tr = fakeTrack({ nodes: 400, checkpoints: [0, 100, 200, 300], laps: 1 });
  const q = queue(), r = createRacers(tr, feel, q, { count: 3 }), race = createRace(tr, feel, q);
  const a = Object.assign(createActions(), { throttle: 1 });
  // a stand-in driver: full throttle, straight (the fake road is a gentle circle)
  const ai = { drive(x, ctx, dt, out) { Object.assign(out, createActions(), { throttle: 1, item: false, back: false }); } };
  go(race, a, r.player.st);
  r.step(DT, a, race, { ai });
  const top = feel.get('speed.topMps'), mul = feel.get('ai.difficultyMul')[2];
  assert.ok(Math.abs(r.list[1].feel.get('speed.topMps') - top * feel.get('ai.paceMul')[0] * mul) < 1e-6);
  assert.ok(Math.abs(r.list[2].feel.get('speed.topMps') - top * feel.get('ai.paceMul')[1] * mul) < 1e-6);
  for (let i = 0; i < 60 * 200 && !r.list.every((x) => x.finished); i++) { race.update(DT, a, r.player.st); r.step(DT, a, race, { ai }); }
  assert.ok(r.list.every((x) => x.finished), 'all three home');
  assert.ok(r.list.every((x) => x.lapTimes.length === 1 && x.bestLapS > 0 && x.finishS > 0));
  const botEv = q.list.filter((e) => e.name === 'racer-event');
  assert.ok(botEv.some((e) => e.payload.racer === 1 && e.payload.name === 'finish'));
  assert.equal(q.list.filter((e) => e.name === 'finish').length, 1, 'only the player emits Phase A events directly');
  assert.ok(r.clockS >= r.list[1].finishS, 'the clock runs on after the player is home');
});

test('place() resets laps, items, mods, fxv and the clock', () => {
  const feel = makeFeel(), tr = threshold(), r = createRacers(tr, feel, queue());
  const x = r.list[3];
  x.lapTimes.push(40); x.bestLapS = 40; x.finished = true; x.item.id = 'flash'; x.mods.capMul = 0.5; x.fxv.shield = 'glass';
  x.st[S.respawnPhase] = RESPAWN.HOVER; r.clockS = 99;
  r.place();
  assert.deepEqual([x.lapTimes.length, x.bestLapS, x.finished, x.item.id, x.mods.capMul, x.fxv.shield, x.st[S.respawnPhase], r.clockS],
    [0, 0, false, null, 1, null, RESPAWN.NONE, 0]);
});

test('nearby: closest rivals first, pan + to the player\'s right', () => {
  const feel = makeFeel(), tr = threshold(), r = createRacers(tr, feel, queue());
  const near = r.nearby(3, 80);
  assert.equal(near.length, 3);
  assert.ok(near[0].distM <= near[1].distM && near[1].distM <= near[2].distM);
  // the grid alternates sides: a kart beside the player sits on the other side (grid x -3 / +3)
  const ps = tr.spawnPose(r.player.meta.slot);
  for (const n of near) {
    const o = r.list[n.i], lat = tr.spawnPose(o.meta.slot).lateral - ps.lateral;
    if (Math.abs(lat) > 1) assert.equal(Math.sign(n.pan), Math.sign(lat), 'pan side');
  }
  assert.equal(r.nearby(3, 1).length, 0);
});
