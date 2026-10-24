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

test('enlarged Melt discs isolate mapped paths and reject corners outside their visible circle', () => {
  for (const fork of track.forks) {
    const local = fork.lengthM * .5, centre = local - makeFeel().get('items.dropBehindM');
    const list = [racer(state(fork, local)), racer(state(null, fork.mainS(centre)), 1)], t = setup(list);
    t.fire(0, 'melt'); t.tick(.8);
    const trap = t.items.view().traps[0], frame = fork.frameAt(centre);
    assert.equal(trap.radiusM, 3.6);
    assert.equal(t.hits().length, 0, 'the main racer sharing lap progress stays outside the branch disc');
    // Place the victim using physical tangent-plane offsets, independent of the lap map.
    list[1].st = state(fork, centre);
    for (let j = 0; j < 3; j++) list[1].st[S.posX + j] += frame.t[j] * 3 + frame.b[j] * 3;
    t.tick();
    assert.equal(t.hits().length, 0, 'a square corner outside the 3.6 m disc cannot hit');
    list[1].st = state(fork, centre, 3.3); t.tick();
    assert.equal(t.hits().length, 1, 'the enlarged visible shoulder remains active');
    assert.equal(t.hits()[0].victim, 1);
    assert.ok(distance(t.hits()[0].p, frame.p) < .002);
  }
});

test('bounded homing selects the physical branch target and stops guiding after a path change', () => {
  for (const fork of track.forks) {
    const local = fork.lengthM * .35;
    const list = [racer(state(fork, local)), racer(state(null, fork.mainS(local + 5), -10), 1),
      racer(state(fork, local + 60, 6), 2)], t = setup(list);
    t.fire(0, 'freeze');
    assert.equal(t.rec.first('item-fire').payload.target, 2, 'a nearer lap-map position on the main road cannot steal targeting');
    const before = structuredClone(t.items.view().projectiles[0]);
    assert.equal(before.homing, true);
    assert.ok(before.x > 0 && before.x <= t.feel.get('items.homeLateralMps') * DT + 1e-6);
    list[2].st = state(null, fork.mainS(local + 60), -8);
    t.tick(.15);
    const after = t.items.view().projectiles[0];
    assert.ok(after); assert.equal(after.homing, false); assert.equal(after.x, before.x);
    assert.ok(Math.abs(fork.localS(after.s) - fork.localS(before.s) - t.feel.get('items.throwExtraMps') * .15) < 1e-6);
    const frame = fork.frameAt(fork.localS(after.s)), residual = after.p.map((v, j) => v - frame.p[j]);
    assert.ok(Math.abs(residual.reduce((sum, v, j) => sum + v * frame.b[j], 0) - after.x) < 1e-5);
    assert.equal(t.hits().length, 0);
  }
});

test('cross-path lob commits its world endpoint and a late fork exit cannot teleport the impact', () => {
  for (const fork of track.forks) {
    const local = fork.lengthM * .5, s = fork.mainS(local);
    const list = [racer(state(null, s - 80)), racer(state(fork, local), 1)], t = setup(list);
    const endpoint = Array.from(list[1].st.slice(S.posX, S.posX + 3));
    const flight = t.feel.get('items.lobFlightS'), commitTime = flight * t.feel.get('items.lobCommitFrac') + .01;
    t.fire(0, 'blackout'); const start = t.rec.first('item-fire').payload.launchP; t.tick(commitTime - DT);
    const committed = t.items.view().projectiles[0];
    assert.equal(committed.homing, false);
    list[1].st = state(null, s); t.tick(DT);
    const after = t.items.view().projectiles[0], k = after.progress;
    const expected = start.map((v, j) => v + (endpoint[j] - v) * k + (j === 1 ? t.feel.get('items.lobHeightM') * 4 * k * (1 - k) : 0));
    assert.ok(distance(after.p, expected) < 1e-5, 'the visible descent retains its committed physical endpoint');
    assert.ok(after.v.every(Number.isFinite));
    t.tick(flight - commitTime);
    assert.equal(t.items.view().projectiles.length, 0);
    assert.equal(t.hits().length, 0, 'leaving the branch during the committed descent dodges the hit');
    const impact = t.items.view().pops.find(p => p.at === 'fizzle');
    assert.ok(impact && distance(impact.p, endpoint) < .002, 'fizzle stays at the committed branch position');

    const controlList = [racer(state(null, s - 80)), racer(state(fork, local), 1)], control = setup(controlList);
    control.fire(0, 'blackout'); control.tick(flight);
    assert.equal(control.hits()[0].victim, 1, 'a target remaining on its branch is hit');
    assert.ok(distance(control.hits()[0].p, endpoint) < .002);
  }
});
