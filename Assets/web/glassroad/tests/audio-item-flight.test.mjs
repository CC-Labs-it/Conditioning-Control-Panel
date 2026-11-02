import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createItemPassTracker, toyLaunchPlan, playToyLaunch, playItemPass } from '../audio/itemFlight.js';
import { sfxPlan, SFX_GAIN } from '../audio/sfxmap.js';

const listener = { p: [0, 0, 0], right: [1, 0, 0] };
const shot = (p, t = 1, id = 1, kind = 'disc') => ({ id, kind, p, t });
function tracker() { const hits = []; return { hits, observer: createItemPassTracker(p => hits.push(p)) }; }

test('flyby samples swept relative motion and emits exactly once with physical world position', () => {
  const { hits, observer } = tracker();
  observer.update([shot([2, 1, -6])], listener, 0);
  observer.update([shot([2, 1, 4], 1.1)], listener, .1);
  assert.equal(hits.length, 1);
  assert.deepEqual(hits[0].p, [2, 1, 0]);
  assert.deepEqual(hits[0].v, [0, 0, 100]);
  assert.equal(hits[0].distanceM, Math.sqrt(5));
  assert.ok(hits[0].volume > .2 && hits[0].volume < .62);
  assert.ok(hits[0].pan > 0);
  for (let i = 2; i < 8; i++) observer.update([shot([2, 1, i % 2 ? -5 : 5], 1 + i / 10)], listener, i / 10);
  assert.equal(hits.length, 1, 'a bouncing or homing projectile has one passing sound');
});

test('flyby uses listener motion, pans left/right and rejects other road levels and distant passes', () => {
  for (const side of [-1, 1]) {
    const { hits, observer } = tracker();
    observer.update([shot([side * 3, 0, 0])], { ...listener, p: [0, 0, -4] }, 0);
    observer.update([shot([side * 3, 0, 0], 1.1)], { ...listener, p: [0, 0, 4] }, .1);
    assert.equal(hits.length, 1); assert.equal(Math.sign(hits[0].pan), side);
  }
  for (const p of [[13, 0, -4], [0, 13, -4]]) {
    const { hits, observer } = tracker();
    observer.update([shot(p)], listener, 0);
    observer.update([shot([p[0], p[1], 4], 1.1)], listener, .1);
    assert.equal(hits.length, 0, 'world distance includes height');
  }
});

test('stationary, departing, teleporting and long frame gaps cannot invent a flyby', () => {
  for (const [before, after, dt] of [
    [[2, 0, -.2], [2, 0, .2], .1], [[2, 0, 1], [2, 0, 5], .1],
    [[2, 0, -50], [2, 0, 50], .1], [[2, 0, -4], [2, 0, 4], 2],
  ]) {
    const { hits, observer } = tracker();
    observer.update([shot(before)], listener, 0);
    observer.update([shot(after, 1 + dt)], listener, dt);
    assert.equal(hits.length, 0);
  }
});

test('pause, finish, restart, missing shots and reused ids clear or preserve the right observer state', () => {
  const { hits, observer } = tracker();
  observer.update([shot([2, 0, -4])], listener, 0);
  observer.update([shot([2, 0, 4], 1.1)], listener, .1, { paused: true });
  assert.equal(hits.length, 0);
  observer.update([shot([2, 0, 4], 1.1)], listener, .1);
  assert.equal(hits.length, 1);
  observer.update([], listener, .2); assert.equal(observer.size, 0);
  observer.update([shot([2, 0, -4], 0)], listener, .3);
  observer.update([shot([2, 0, 4], .1)], listener, .4);
  assert.equal(hits.length, 2, 'new shot with reused id can pass');
  observer.update([], listener, .5, { active: false }); assert.equal(observer.size, 0);
  observer.update([shot([2, 0, 4])], listener, .6); assert.equal(hits.length, 2);
  observer.reset(); assert.equal(observer.size, 0);
});

test('dense volleys stay bounded and choose the loudest close pass without delayed queues', () => {
  const { hits, observer } = tracker();
  const batch = (z, t) => Array.from({ length: 100 }, (_, i) => shot([2 + i * .01, 0, z], t, i));
  observer.update(batch(-4, 1), listener, 0); assert.equal(observer.size, 64);
  observer.update(batch(4, 1.1), listener, .1); assert.equal(hits.length, 1);
  assert.equal(hits[0].projectileId, 0);
  observer.update(batch(-4, 1.2), listener, .2);
  observer.update(batch(4, 1.3), listener, .3);
  assert.equal(hits.length, 1);
});

test('all seven toys have distinct bounded release figures using existing samples and synth fallback', () => {
  const plans = new Set(), voices = new Set();
  for (const id of ['pendulum', 'disc', 'tether', 'bigSpiral', 'melt', 'snap', 'surge']) {
    const plan = toyLaunchPlan(id); plans.add(JSON.stringify(plan));
    assert.ok(plan.length <= 3);
    assert.ok(plan.every(p => SFX_GAIN[p.id] && p.vol > 0 && p.vol <= 1 && p.at <= .08));
    assert.deepEqual(sfxPlan('item-fire', { racer: 0, id, mode: 'throw' }), plan);
    const notes = [], syn = { semis: n => n, noise: n => notes.push(n), tone: (f, n) => notes.push([f, n]) };
    assert.equal(playToyLaunch(syn, id), true); assert.ok(notes.length <= 3);
    voices.add(JSON.stringify(notes));
  }
  assert.equal(plans.size, 7); assert.equal(voices.size, 7);
  assert.equal(toyLaunchPlan('unknown'), null);
});

test('flyby figure stays short, bounded and sweeps using the listener side without altering shared synth state', () => {
  const notes = [], syn = { pan: .4, semis: n => 220 * 2 ** (n / 12),
    noise(n) { notes.push({ ...n, pan: this.pan }); }, tone(f, n) { notes.push({ ...n, pan: this.pan }); } };
  playItemPass(syn, { kind: 'disc', panFrom: -.9, panTo: .7 });
  assert.equal(notes.length, 3); assert.equal(syn.pan, .4);
  assert.equal(notes[0].pan, -.9); assert.equal(notes[2].pan, .7);
  assert.ok(notes.every(n => n.dur <= .22 && n.vol <= .13));
});
