import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { createItems } from '../sim/items.js';
import { S } from '../sim/state.js';
import { makeFeel, place, recorder, acts, DT } from './sim-fake.mjs';

const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/boudoir.json', import.meta.url))));
const race = { phase: 'racing' }, distance = (a, b) => Math.hypot(...a.map((v, k) => v - b[k]));
function state(fork, localS, lateral = 0) {
  const st = place(fork ? { frameAt: fork.frameAt } : track, { s: localS, lateral });
  if (fork) st[S.s] = fork.mainS(localS);
  return st;
}
function racer(st, i = 0) { return { i, st, input: acts(), item: { id: null, rolling: false, rollT: 0, heldT: 0 }, mods: {}, fxv: {}, meta: { isBot: i > 0 }, finished: false }; }
function setup(list, pickups = [], overrides = {}) {
  const feel = makeFeel(), rec = recorder(), items = createItems({ ...track, ...overrides, items: pickups }, feel, rec);
  const standings = { get order() { return list.map(r => r.i).sort((a, b) => list[b].st[S.s] - list[a].st[S.s]); },
    progressOf: i => list[i].st[S.s], placeOf(i) { return this.order.indexOf(i) + 1; }, gapToLeader(i) { return list[this.order[0]].st[S.s] - list[i].st[S.s]; } };
  const tick = (dt = DT) => items.update(dt, list, standings, race);
  const fire = (i, id) => { list[i].item.id = id; list[i].input.item = true; tick(); list[i].input.item = false; };
  return { items, feel, rec, tick, fire, hits: () => rec.all('item-hit').map(e => e.payload) };
}

test('branch racers cannot collect remote main boxes or bubbles sharing mapped progress', () => {
  for (const fork of track.forks) for (const kind of ['box', 'bubble']) {
    const r = racer(state(fork, fork.lengthM / 2)), s = r.st[S.s], t = setup([r], [{ kind, s, x: 0 }]);
    const main = track.frameAt(s); assert.ok(distance(main.p, Array.from(r.st.slice(S.posX, S.posX + 3))) > 10, fork.id);
    t.tick(); assert.equal(t.rec.all(kind === 'box' ? 'item-box' : 'bubble-pop').length, 0, fork.id);
    assert.equal(t.items.view()[kind === 'box' ? 'boxes' : 'bubbles'][0].alive, true);
    r.st = state(null, s); t.tick();
    assert.equal(t.rec.all(kind === 'box' ? 'item-box' : 'bubble-pop').length, 1, fork.id);
  }
});

test('dropped traps stay on the physical branch and ignore racers on remote main road', () => {
  for (const fork of track.forks) {
    const local = fork.lengthM * .5, feel = makeFeel(), behind = local - feel.get('items.dropBehindM');
    const list = [racer(state(fork, local)), racer(state(null, fork.mainS(behind)), 1)], t = setup(list);
    t.fire(0, 'melt');
    const trap = t.items.trapRows()[0]; assert.ok(trap); assert.equal(track.forkAtSeg(trap.seg).id, fork.id);
    assert.ok(Math.abs(trap.s - fork.mainS(behind)) < .001);
    assert.ok(distance(t.items.view().traps[0].p, fork.frameAt(behind).p) < .002);
    list[0].st = state(fork, local + 20); t.tick(t.feel.get('items.trapArmS') + .1);
    assert.equal(t.hits().length, 0, 'main road racer is physically elsewhere');
    list[1].st = state(fork, behind); t.tick();
    assert.equal(t.hits().length, 1); assert.equal(t.hits()[0].victim, 1); assert.equal(t.items.view().traps.length, 0);
    assert.ok(distance(t.items.view().pops.find(p => p.at === 'trap').p, fork.frameAt(behind).p) < .002);
  }
});

test('main-road traps do not hit the branch at the same lap progress', () => {
  const fork = track.forks[1], local = fork.lengthM * .5, s = fork.mainS(local), feel = makeFeel();
  const list = [racer(state(null, s + feel.get('items.dropBehindM'))), racer(state(fork, local), 1)], t = setup(list);
  t.fire(0, 'melt'); list[0].st = state(null, s + 30); t.tick(t.feel.get('items.trapArmS') + .1);
  assert.equal(t.hits().length, 0); assert.equal(t.items.view().traps.length, 1);
  list[1].st = state(null, s); t.tick(); assert.equal(t.hits()[0].victim, 1);
});

test('throws move in physical metres on each branch and hit only the nearby racer', () => {
  for (const fork of track.forks) {
    const local = fork.lengthM * .45, list = [racer(state(fork, local)), racer(state(null, fork.mainS(local + 12)), 1)], t = setup(list);
    t.fire(0, 'freeze'); const projectile = { ...t.items.view().projectiles[0] }, localStart = fork.localS(projectile.s);
    const interval = .18; t.tick(interval); const next = t.items.view().projectiles[0];
    assert.ok(next); assert.equal(t.hits().length, 0);
    assert.ok(Math.abs(fork.localS(next.s) - localStart - t.feel.get('items.throwExtraMps') * interval) < 1e-6);
    const frame = fork.frameAt(fork.localS(next.s)), residual = next.p.map((v, k) => v - frame.p[k]);
    assert.ok(Math.abs(residual.reduce((sum, v, k) => sum + v * frame.b[k], 0)) < 1e-5);
    list[1].st = state(fork, fork.localS(next.s) + t.feel.get('items.throwExtraMps') * DT); t.tick();
    assert.equal(t.hits().length, 1); assert.equal(t.hits()[0].victim, 1); assert.equal(t.items.view().projectiles.length, 0);
  }
});

test('a lob bridges world space to a racer on another path and pause or reset leaves no ghosts', () => {
  const fork = track.forks[2], local = fork.lengthM * .4, s = fork.mainS(local);
  const list = [racer(state(fork, local)), racer(state(null, s + 50), 1)], t = setup(list);
  t.fire(0, 'blackout'); t.tick(t.feel.get('items.lobFlightS') / 2 - DT);
  const pr = t.items.view().projectiles[0], from = fork.frameAt(fork.localS(list[0].st[S.s])), start = from.p.map((v, k) => v + from.n[k] * t.feel.get('items.throwHeightM'));
  const target = Array.from(list[1].st.slice(S.posX, S.posX + 3));
  const expected = start.map((v, k) => (v + target[k]) / 2 + (k === 1 ? t.feel.get('items.lobHeightM') : 0));
  assert.ok(distance(pr.p, expected) < 1e-5, 'the visible lob follows the physical target, not the mapped main driveline');
  const before = structuredClone(t.items.view()); t.items.update(.5, list, {}, { phase: 'paused' }); assert.deepEqual(t.items.view(), before);
  t.tick(t.feel.get('items.lobFlightS') / 2 + DT); assert.equal(t.hits()[0].victim, 1);
  t.items.reset(); assert.equal(t.items.view().projectiles.length, 0); assert.equal(t.items.view().traps.length, 0); assert.equal(t.items.view().pops.length, 0);
});

test('Shatter uses the selected path rail safety instead of a distant main gap', () => {
  const fork = track.forks[1], local = fork.lengthM * .5, s = fork.mainS(local), mainSeg = track.frameAt(s).seg;
  const r = racer(state(fork, local));
  const t = setup([r], [], { nodeFlags: i => i === mainSeg ? ['open'] : ['rail'] });
  assert.equal(t.items.nearOpenEdge(s, 8), true);
  assert.equal(t.items.nearOpenEdge(s, 8, r.st[S.seg]), false);
  t.fire(0, 'shatter'); assert.equal(t.rec.all('item-refused').length, 0); assert.equal(t.items.view().projectiles.length, 1);
  const unsafe = { ...fork, frameAt(localS, out) { const f = fork.frameAt(localS, out); if (Math.abs(localS - local) < 4) f.flags = ['railL']; return f; } };
  const u = setup([racer(state(fork, local))], [], { nodeFlags: () => ['rail'], forkAtSeg: seg => track.forkAtSeg(seg)?.id === fork.id ? unsafe : track.forkAtSeg(seg) });
  assert.equal(u.items.nearOpenEdge(s, 8), false); assert.equal(u.items.nearOpenEdge(s, 8, r.st[S.seg]), true);
  u.fire(0, 'shatter'); assert.equal(u.rec.all('item-refused').length, 1); assert.equal(u.items.view().projectiles.length, 0);
});

test('a throw keeps physical speed when its lead or later flight crosses a fork merge', () => {
  for (const fork of track.forks) for (const remaining of [1, 10]) {
    const r = racer(state(fork, fork.lengthM - remaining)), actualRemaining = fork.lengthM - fork.localS(r.st[S.s]);
    const t = setup([r]); t.fire(0, 'freeze');
    const extraTime = remaining > 2.5 ? .4 : 0; if (extraTime) t.tick(extraTime);
    const pr = t.items.view().projectiles[0], afterMerge = 2.5 + t.feel.get('items.throwExtraMps') * (DT + extraTime) - actualRemaining;
    assert.ok(afterMerge > 0); assert.ok(Math.abs(pr.s - (fork.toS + afterMerge)) < 1e-6, fork.id);
    const f = track.frameAt(pr.s), delta = pr.p.map((v, k) => v - f.p[k]);
    assert.ok(Math.abs(delta.reduce((sum, v, k) => sum + v * f.t[k], 0)) < 1e-5);
    assert.ok(Math.abs(delta.reduce((sum, v, k) => sum + v * f.b[k], 0)) < 1e-5);
  }
});
