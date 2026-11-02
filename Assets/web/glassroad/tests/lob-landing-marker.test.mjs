import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fakeTrack, makeFeel, place, recorder, acts, DT } from './sim-fake.mjs';
import { createItems } from '../sim/items.js';
import { createNetItems } from '../net/items.js';
import { loadTrack } from '../track/load.js';
import { S } from '../sim/state.js';

const distance = (a, b) => Math.hypot(...a.map((v, j) => v - b[j]));
function setup(track = fakeTrack({ rollDeg: 25 }), online = false) {
  const feel = makeFeel(), events = recorder();
  const list = [100, 300].map((s, i) => ({ i, kind: i ? 'remote' : 'player', meta: { seat: i, isBot: false },
    st: place(track, { s }), input: acts(), item: { id: null, rolling: false }, mods: {}, fxv: {}, finished: false }));
  const items = online ? createNetItems(track, feel, events) : createItems({ ...track, items: [] }, feel, events);
  const standings = { order: [1, 0], progressOf: i => list[i].st[S.s], placeOf: i => 2 - i, gapToLeader: () => 200 };
  const tick = () => items.update(DT, list, standings, { phase: 'racing' });
  tick();
  const fire = () => {
    if (online) items.onFired({ sid: 1, seat: 0, target: 1, s: list[0].st[S.s], x: 0, mode: 'lob', item: 'blackout' });
    else { list[0].item.id = 'blackout'; list[0].input.item = true; tick(); list[0].input.item = false; }
  };
  const commit = () => { while (items.view().projectiles[0].progress < 0.88) tick(); };
  return { track, feel, list, items, events, tick, fire, commit };
}

for (const online of [false, true]) test(`${online ? 'online' : 'offline'} landing metadata guides, commits and stays fixed through a target dodge`, () => {
  const t = setup(undefined, online); t.fire(); t.tick();
  const initial = t.items.view().projectiles[0].landingP.slice();
  t.list[1].st = place(t.track, { s: 306, lateral: 2 }); t.tick();
  let pr = t.items.view().projectiles[0];
  assert.ok(distance(pr.landingP, initial) > 0.1, 'guidance updates the predicted landing');
  t.commit(); pr = t.items.view().projectiles[0];
  const landing = pr.landingP.slice(), normal = pr.landingN.slice(), pooled = pr.landingP;
  assert.equal(pr.homing, false);
  assert.ok(Math.abs(normal[1] - Math.cos(25 * Math.PI / 180)) < 0.000001, 'marker follows the bank normal');
  assert.ok(Math.abs(normal[2]) > 0.4, 'normal is not global up');
  t.list[1].st = place(t.track, { s: 600, lateral: -12 }); t.tick();
  pr = t.items.view().projectiles[0];
  assert.strictEqual(pr.landingP, pooled, 'view metadata reuses its vector');
  assert.deepEqual(pr.landingP, landing);
  assert.deepEqual(pr.landingN, normal);
  assert.ok(distance(pr.landingP, Array.from(t.list[1].st.slice(S.posX, S.posX + 3))) > 200);
  assert.equal(t.events.all('item-hit').length, 0, 'presentation does not create a hit');
});

test('fork landing marker uses the real branch aim and preserves it through a late path switch', () => {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/boudoir.json', import.meta.url), 'utf8')));
  const fork = track.forks[2], local = fork.lengthM * 0.4, t = setup(track);
  const target = place({ frameAt: fork.frameAt }, { s: local, lateral: 1 });
  target[S.s] = fork.mainS(local); t.list[1].st = target;
  t.list[0].st = place(track, { s: fork.mainS(local) - 40 }); t.fire();
  let pr = t.items.view().projectiles[0];
  const worldTarget = Array.from(target.slice(S.posX, S.posX + 3));
  assert.ok(distance(pr.landingP, worldTarget) < 0.00001);
  assert.ok(distance(pr.landingP, track.frameAt(target[S.s]).p) > 10, 'mapped main road is not the branch landing');
  const expected = track.query(...worldTarget, target[S.seg]).normal;
  assert.ok(distance(pr.landingN, expected) < 0.00001);
  t.commit(); pr = t.items.view().projectiles[0];
  const fixed = pr.landingP.slice(), fixedN = pr.landingN.slice();
  t.list[1].st = place(track, { s: target[S.s] }); t.tick();
  assert.deepEqual(t.items.view().projectiles[0].landingP, fixed);
  assert.deepEqual(t.items.view().projectiles[0].landingN, fixedN);
});

test('a reused projectile view row cannot leave a stale landing marker on a throw', () => {
  const t = setup(); t.fire(); const row = t.items.view().projectiles[0];
  assert.ok(row.landingP); t.items.reset();
  t.list[0].item.id = 'flash'; t.list[0].input.item = true; t.tick(); t.list[0].input.item = false;
  const pr = t.items.view().projectiles[0];
  assert.strictEqual(pr, row);
  assert.equal(pr.mode, 'throw'); assert.equal(pr.landingP, null); assert.equal(pr.landingN, null);
});
