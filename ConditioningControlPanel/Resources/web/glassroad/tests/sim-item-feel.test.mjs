import { test } from 'node:test';
import assert from 'node:assert/strict';
import { fakeTrack, place, makeFeel, recorder, acts, DT } from './sim-fake.mjs';
import { S } from '../sim/state.js';
import { createItems } from '../sim/items.js';

function setup(a = { s: 100 }, b = { s: 180, lateral: 12 }, tuning = {}) {
  const track = fakeTrack(), feel = makeFeel(tuning), events = recorder();
  const racers = [a, b].map((p, i) => ({ i, st: place(track, p), input: acts(),
    item: { id: null, rolling: false, rollT: 0, heldT: 0 }, mods: {}, fxv: {}, finished: false }));
  const items = createItems(track, feel, events);
  const standings = { order: [1, 0], progressOf: i => racers[i].st[S.s], placeOf: i => 2 - i, gapToLeader: () => 80 };
  function tick(n = 1, dt = DT) { for (let i = 0; i < n; i++) items.update(dt, racers, standings, { phase: 'racing' }); }
  function fire(kind) { racers[0].item.id = kind; racers[0].input.item = true; tick(); racers[0].input.item = false; }
  function move(i, p) { racers[i].st = place(track, p); }
  return { track, feel, events, racers, items, tick, fire, move };
}

test('homing is bounded, readable and stops chasing once a target is behind', () => {
  const t = setup(); t.fire('flash');
  let pr = t.items.view().projectiles[0], oldX = pr.x;
  assert.equal(pr.homing, true);
  assert.equal(pr.radiusM, 1.05);
  assert.ok(pr.v.every(Number.isFinite));
  t.tick(); pr = t.items.view().projectiles[0];
  assert.ok(pr.x > oldX && pr.x - oldX <= t.feel.get('items.homeLateralMps') * DT + 1e-6);
  oldX = pr.x;
  t.move(1, { s: 95, lateral: -12 }); t.tick();
  pr = t.items.view().projectiles[0];
  assert.equal(pr.homing, false);
  assert.equal(pr.x, oldX);
});

test('fast projectiles sweep contact instead of skipping racers between ticks', () => {
  const t = setup({ s: 100 }, { s: 120 }, { 'items.throwExtraMps': 500 });
  t.fire('flash'); t.tick(1, 0.1);
  assert.equal(t.events.all('item-hit').length, 1);
  assert.equal(t.events.first('item-hit').payload.how, 'throw');
  assert.equal(t.items.view().projectiles.length, 0);
  const launch = t.events.first('item-fire').payload;
  assert.equal(typeof launch.projectileId, 'number');
  assert.ok(launch.launchP.every(Number.isFinite));
  assert.ok(launch.direction.every(Number.isFinite));
});

test('lob commits its terminal dive and can miss a late dodge', () => {
  const t = setup({ s: 100 }, { s: 300 }); t.fire('blackout');
  t.tick(83);
  let pr = t.items.view().projectiles[0];
  assert.ok(pr.progress >= 0.85 && !pr.homing);
  const previous = pr.p.slice();
  t.move(1, { s: 450, lateral: 12 }); t.tick();
  pr = t.items.view().projectiles[0];
  assert.ok(Math.hypot(...pr.p.map((v, k) => v - previous[k])) < 6, 'no teleport on target dodge');
  t.tick(20);
  assert.equal(t.items.view().projectiles.length, 0);
  assert.equal(t.events.all('item-hit').length, 0);
  assert.ok(t.items.view().pops.some(p => p.at === 'fizzle'));
});

test('lob retains its travel direction across half-lap target retargeting', () => {
  const t = setup({ s: 100 }, { s: 20090 }); t.fire('blackout');
  const before = t.items.view().projectiles[0].p.slice();
  t.move(1, { s: 20120 }); t.tick();
  const after = t.items.view().projectiles[0].p;
  assert.ok(Math.hypot(...after.map((v, k) => v - before[k])) < 250, 'one flight step, no half-lap reversal');
});

test('large puddle footprint hits its visible shoulder and isolates stacked levels', () => {
  const t = setup({ s: 100 }, { s: 95.2, lateral: 3.3 }); t.fire('melt');
  assert.equal(t.events.first('item-hit').payload.kind, 'melt');
  const u = setup({ s: 100 }, { s: 95.2, lateral: 3.3, height: 9 });
  u.fire('melt');
  assert.equal(u.events.all('item-hit').length, 0);
  assert.equal(u.items.view().traps[0].radiusM, 3.6);
  u.racers[1].st[S.grounded] = 1; u.racers[1].st[S.height] = 0; u.tick();
  assert.equal(u.events.all('item-hit').length, 0, 'upper road grounded flag cannot hit lower trap');
});

test('finished target fizzles its lob without applying a view effect', () => {
  const t = setup({ s: 100 }, { s: 300 }); t.fire('blackout');
  t.racers[1].finished = true; t.tick();
  assert.equal(t.items.view().projectiles.length, 0);
  assert.equal(t.events.all('item-hit').length, 0);
});

test('puddles do not hit outside their round visible footprint', () => {
  const t = setup({ s: 100 }, { s: 98.2, lateral: 3 }); t.fire('melt');
  assert.equal(t.events.all('item-hit').length, 0, 'square corner lies outside the puddle');
  assert.equal(t.items.view().traps.length, 1);
  t.move(1, { s: 97.2, lateral: 2.8 }); t.tick();
  assert.equal(t.events.all('item-hit').length, 1, 'round shoulder remains hittable');
});

test('projectile sweep includes a round end cap rather than square corner hits', () => {
  const t = setup({ s: 100 }, { s: 105.8, lateral: 2.1 });
  // Keep this shot unguided to isolate the sweep geometry.
  t.racers[1].finished = true; t.fire('flash'); t.racers[1].finished = false;
  t.tick();
  assert.equal(t.events.all('item-hit').length, 0);
  t.tick(6);
  assert.equal(t.events.all('item-hit').length, 1);
});

test('laying an enlarged puddle clears its owner even after arming', () => {
  const t = setup({ s: 100 }, { s: 900 }); t.fire('melt'); t.tick(60);
  assert.equal(t.events.all('item-hit').length, 0);
  assert.equal(t.items.view().traps[0].armed, true);
  t.move(0, { s: t.items.view().traps[0].s }); t.tick();
  assert.equal(t.events.first('item-hit').payload.victim, 0, 'returning owner can hit an armed trap');
});

test('a zero-delta visual refresh keeps projectile velocity finite', () => {
  const t = setup(); t.fire('flash'); t.tick(1, 0);
  assert.ok(t.items.view().projectiles[0].v.every(Number.isFinite));
});
