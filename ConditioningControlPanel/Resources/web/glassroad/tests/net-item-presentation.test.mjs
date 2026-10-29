import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createNetItems } from '../net/items.js';
import { fakeTrack, makeFeel, place, acts, recorder, DT } from './sim-fake.mjs';
import { S } from '../sim/state.js';

function setup() {
  const track = fakeTrack(), feel = makeFeel(), rec = recorder(), sent = [];
  const list = [200, 100].map((s, i) => ({ i, kind: i ? 'remote' : 'player', meta: { seat: i, isBot: false },
    st: place(track, { s }), input: acts(), item: {}, mods: {}, fxv: {}, finished: false }));
  const items = createNetItems(track, feel, rec, { send: (m) => sent.push(m) });
  const tick = (dt = DT) => items.update(dt, list, { order: [0, 1] }, { phase: 'racing' });
  tick();
  const fire = (mode, item = 'flash', sid = 1) => items.onFired({ sid, seat: 1, target: 0, s: 100, x: 0, mode, item });
  return { track, feel, rec, list, items, tick, fire, sent };
}

test('online throws expose the same visual metadata and bounded steering without predicting damage', () => {
  const t = setup(); t.list[0].st[S.lateral] = 10;
  t.fire('throw'); t.tick();
  const pr = t.items.view().projectiles[0];
  assert.equal(pr.by, 1); assert.equal(pr.homing, true);
  assert.equal(pr.radiusM, t.feel.get('items.projectileRadiusM'));
  assert.ok(pr.progress > 0 && pr.progress < 1);
  assert.ok(pr.x > 0 && pr.x <= t.feel.get('items.homeLateralMps') * DT + 1e-8);
  assert.ok(pr.v.every(Number.isFinite) && Math.hypot(...pr.v) > 0);
  for (let i = 0; i < 200; i++) t.tick();
  assert.equal(t.rec.all('item-hit').length, 0, 'only a room verdict may hit');
  assert.equal(t.items.state(0).eff, null);
  assert.equal(t.sent.length, 0);
});

test('online lob commits its dive and does not teleport after the target moves', () => {
  const t = setup(); t.fire('lob', 'blackout');
  while (t.items.view().projectiles[0].t / t.feel.get('items.lobFlightS') < 0.9) t.tick();
  const before = { ...t.items.view().projectiles[0] };
  assert.equal(before.homing, false);
  assert.equal(before.radiusM, t.feel.get('items.lobRadiusM'));
  t.list[0].st = place(t.track, { s: 5000, lateral: 12 });
  t.tick();
  const next = t.items.view().projectiles[0];
  assert.equal(next.homing, false);
  assert.ok(Math.abs(next.s - before.s) < 5, 'no target-following teleport');
  while (t.items.view().projectiles[0].progress < 1) t.tick();
  const end = { ...t.items.view().projectiles[0] }; t.tick(0.1);
  assert.equal(t.items.view().projectiles[0].s, end.s);
  assert.equal(t.items.view().projectiles[0].x, end.x);
  assert.equal(t.rec.all('item-hit').length, 0);
});

test('room hits land on the victim even when cosmetic pursuit diverges', () => {
  const t = setup(); t.fire('lob', 'blackout'); t.tick();
  t.list[0].st = place(t.track, { s: 800, lateral: 7 });
  t.items.onHit({ sid: 1, victim: 0, by: 1, item: 'blackout', verdict: 'hit', durationS: 2 });
  assert.equal(t.items.view().projectiles.length, 0);
  assert.equal(t.items.state(0).eff.kind, 'blackout');
  const hit = t.rec.first('item-hit').payload;
  assert.equal(hit.how, 'lob');
  assert.deepEqual(hit.p, [t.list[0].st[S.posX], t.list[0].st[S.posY], t.list[0].st[S.posZ]]);
  const burst = t.items.view().pops.at(-1).p, f = t.track.frameAt(800);
  assert.ok(Math.hypot(...burst.map((x, i) => x - hit.p[i] - f.n[i])) < 0.0001);
});

test('online traps show larger footprints and reset/fizzle only clear presentation', () => {
  const t = setup(); t.fire('drop', 'melt'); t.fire('drop', 'freeze', 2);
  assert.deepEqual(t.items.view().traps.map((p) => p.radiusM), [t.feel.get('items.meltRadiusM'), t.feel.get('items.trapRadiusM')]);
  t.items.onFizzle({ sid: 1 });
  assert.equal(t.items.view().traps.length, 1);
  assert.equal(t.items.state(0).eff, null);
  t.items.reset();
  assert.equal(t.items.view().traps.length, 0);
  assert.equal(t.items.view().projectiles.length, 0);
});
