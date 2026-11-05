import { test } from 'node:test';
import assert from 'node:assert/strict';
import { springStep, resting, fxLevel } from '../fx/spring.js';
import { createShelfFx, slotsOf, SHELF } from '../fx/shelves.js';
import { createEmi, createSlam } from '../fx/emi.js';
import { createSfx } from '../fx/sfx.js';

// Plain stand-ins for three objects: just the fields the juice touches.
const obj = (name = '', x = 0, y = 0, children = []) => ({ name, children, visible: true, position: { x, y, z: 0 }, rotation: { x: 0, y: 0, z: 0 },
  scale: { x: 1, y: 1, z: 1, set(a, b, c) { this.x = a; this.y = b; this.z = c; } } });
const shelf = (x) => obj('Scene', x, 4, [obj('shelf_unit', 0, 0, ['r0c0', 'r0c1', 'r1c2'].map((s, i) => obj('slot_' + s, 0, -1 - i, [obj('item')])).concat(obj('slot_r2c2', 0, -3)))]);
const seeded = (s = 7) => () => ((s = (s * 16807) % 2147483647) / 2147483647);
function run(fx, seconds, dt = 1 / 60) { for (let t = 0; t < seconds; t += dt) fx.step(dt); }
const juice = (opts, log = []) => { const fx = createShelfFx({ level: () => fxLevel(opts), rng: seeded(), on: (type, d) => log.push({ type, at: fx.clock, ...d }) }); return fx; };

test('spring settles within 2 s of frames and the exact step never explodes at big dt', () => {
  const s = { x: 0, v: 3 };
  let frames = 0;
  while (!resting(s) && frames < 1000) { springStep(s, 18, 0.3, 1 / 60); frames++; }
  assert.ok(frames < 120, `took ${frames} frames`);
  for (const dt of [0.1, 0.5, 2, 10]) {
    const b = { x: 0.2, v: 5 };
    for (let i = 0; i < 50; i++) springStep(b, 26, 0.18, dt);
    assert.ok(Number.isFinite(b.x) && Math.abs(b.x) < 0.25 && Math.abs(b.v) < 5, `dt ${dt}: ${b.x} ${b.v}`);
  }
  const c = springStep({ x: 1, v: 0 }, 10, 1.5, 0.05);   // overdamped falls back to critical, still finite
  assert.ok(c.x > 0 && c.x < 1);
});

test('levels: less is half, reduced motion caps at less, off cuts, Gentle fades with no dust or shake', () => {
  assert.deepEqual(['full', 'less', 'off'].map((motion) => fxLevel({ motion }).amp), [1, 0.5, 0]);
  assert.equal(fxLevel({ motion: 'full', reducedMotion: true }).mode, 'less');
  const g = fxLevel({ gentle: true });
  assert.deepEqual([g.amp, g.dust, g.shake, g.fade], [0, false, false, true]);
  assert.equal(fxLevel({ motion: 'off', gentle: true }).mode, 'off');
});

test('only filled slots hop', () => assert.deepEqual(slotsOf(shelf(0)).map((s) => s.name), ['slot_r0c0', 'slot_r0c1', 'slot_r1c2']));

test('shelves drop left to right from above the frame, land, rattle out of step and come to rest exactly', () => {
  const log = [], fx = juice({ motion: 'full' }, log), units = [shelf(1.55), shelf(-4.65), shelf(-1.55)];
  assert.equal(fx.swap({ outgoing: [], incoming: units }), undefined);
  assert.ok(units.every((u) => !u.visible && u.position.y > 4 + SHELF.dropH - 0.01));
  run(fx, 0.15);
  assert.ok(units.every((u) => !u.visible), 'the first beat holds the empty counter');
  run(fx, 1.2);
  assert.deepEqual(log.filter((e) => e.type === 'land').map((e) => e.node.position.x), [-4.65, -1.55, 1.55]);
  assert.equal(log.filter((e) => e.type === 'rattle').length, 9);
  assert.equal(new Set(log.filter((e) => e.type === 'rattle').map((e) => e.at.toFixed(4))).size >= 6, true, 'rattles out of step');
  run(fx, 4);
  assert.equal(fx.busy(), false);
  for (const u of units) { assert.equal(u.position.y, 4); assert.equal(u.rotation.z, 0); slotsOf(u).forEach((s, i) => { assert.equal(s.position.y, -1 - i); assert.equal(s.rotation.z, 0); }); }
});

test('a huge frame never throws a shelf through the counter', () => {
  const fx = juice({ motion: 'full' }), u = shelf(0);
  fx.swap({ incoming: [u] });
  for (let i = 0; i < 80; i++) { fx.step(0.1); assert.ok(u.position.y > 3.6 && Number.isFinite(u.rotation.z), `y ${u.position.y}`); }
  assert.equal(fx.busy(), false);
});

test('the lift accelerates up and out, then its promise resolves and the new shelves drop', async () => {
  const log = [], fx = juice({ motion: 'less' }, log), old = [shelf(0), shelf(3)];
  fx.swap({ incoming: old }); run(fx, 5);
  const fresh = [shelf(0), shelf(3)], done = fx.swap({ outgoing: old, incoming: fresh });
  assert.ok(done && typeof done.then === 'function');
  let resolved = false; done.then(() => { resolved = true; });
  const ys = [];
  for (let i = 0; i < 20; i++) { fx.step(1 / 60); ys.push(old[0].position.y); }
  assert.ok(ys[19] - ys[18] > ys[2] - ys[1], 'accelerates');
  assert.ok(fresh.every((u) => !u.visible), 'the new shelves wait while the old ones are in frame');
  run(fx, 1); await done;
  assert.ok(resolved && old.every((u) => !u.visible));
  assert.equal(log.filter((e) => e.type === 'lift').length, 1);
  run(fx, 3);
  assert.equal(log.filter((e) => e.type === 'land').length, 4);
});

test('motion off cuts: at rest at once, no promise, one land each for the thud', () => {
  const log = [], fx = juice({ motion: 'off' }, log), old = [shelf(0)], fresh = [shelf(0)];
  assert.equal(fx.swap({ outgoing: old, incoming: fresh }), undefined);
  assert.deepEqual([fresh[0].visible, fresh[0].position.y, fx.busy()], [true, 4, false]);
  assert.deepEqual(log.map((e) => e.type), ['land']);
});

test('Gentle fades only: no drop, no swing, a fade out then a fade in', async () => {
  const log = [], fx = juice({ gentle: true }, log), old = [shelf(0)];
  fx.swap({ incoming: old }); run(fx, 1);
  const fresh = [shelf(0)], done = fx.swap({ outgoing: old, incoming: fresh });
  for (let t = 0; t < 1.5; t += 1 / 60) { fx.step(1 / 60); [...old, ...fresh].forEach((u) => { assert.equal(u.position.y, 4); assert.equal(u.rotation.z, 0); }); }
  await done;
  const fades = log.filter((e) => e.type === 'fade');
  assert.ok(fades.some((e) => e.node === old[0] && e.end && e.a === 0) && fades.some((e) => e.node === fresh[0] && e.end && e.a === 1));
  assert.equal(log.filter((e) => e.type === 'rattle').length, 0);
});

test('EMI cheers in and out of her rest pose, holds still when motion is off', () => {
  const names = ['emi_attendant', 'shoulderL', 'shoulderR', 'ant0', 'ant1', 'ant2', 'ballpiv'];
  const parts = Object.fromEntries(names.map((n) => [n, obj(n, 0, 0.5)]));
  const root = { getObjectByName: (n) => parts[n] };
  let L = fxLevel({ motion: 'full' });
  const emi = createEmi(root, () => L, seeded());
  emi.step(0.2); emi.cheer();
  let peak = 0;
  for (let i = 0; i < 30; i++) { emi.step(1 / 60); peak = Math.max(peak, parts.shoulderL.position.y - 0.5); }
  assert.ok(peak > 0.02 && parts.shoulderL.rotation.z < 0 && parts.shoulderR.rotation.z > 0);
  for (let i = 0; i < 60; i++) emi.step(1 / 60);
  assert.equal(emi.cheering, false);
  assert.ok(Math.abs(parts.shoulderL.rotation.z) < 0.05);
  L = fxLevel({ motion: 'off' }); emi.cheer(); emi.step(1 / 60);
  names.forEach((n) => { assert.equal(parts[n].position.y, 0.5); assert.equal(parts[n].rotation.z, 0); });
  assert.equal(createEmi({ getObjectByName: () => undefined }, () => L), null);
});

test('the stamp slams in big, hits once, dips under size and settles at 1; off is a cut', () => {
  const s = obj('stamp'); let hits = 0, low = 9;
  const slam = createSlam(s, () => fxLevel({ motion: 'full' }), { hit: () => hits++ });
  assert.equal(s.scale.x, 2);
  let done = false;
  for (let i = 0; i < 120 && !done; i++) { done = slam.step(1 / 60); low = Math.min(low, s.scale.x); }
  assert.deepEqual([done, hits, s.scale.x], [true, 1, 1]);
  assert.ok(low < 0.95, 'overshoots under its size');
  const cut = obj('stamp'), c = createSlam(cut, () => fxLevel({ motion: 'off' }), { hit: () => hits++ });
  assert.deepEqual([cut.scale.x, hits, c.step(1 / 60)], [1, 2, true]);
  const fades = [], g = createSlam(obj('stamp'), () => fxLevel({ gentle: true }), { fade: (a) => fades.push(a) });
  while (!g.step(1 / 60));
  assert.deepEqual([fades[0], fades[fades.length - 1]], [0, 1]);
});

test('the sfx bus is silent until a gesture, then plays and ducks', () => {
  const made = [], node = () => ({ connect: (n) => n || node(), start() {}, stop() {}, gain: { value: 1, setValueAtTime() {}, exponentialRampToValueAtTime() {}, setTargetAtTime(v) { this.value = v; } },
    frequency: { value: 0, setValueAtTime() {}, exponentialRampToValueAtTime() {} }, Q: { value: 0 } });
  class Ctx { constructor() { made.push(this); this.currentTime = 0; this.sampleRate = 8000; this.destination = node(); this.made = 0; }
    createGain() { this.made++; return (this.bus = this.bus ? node() : node()); } createOscillator() { this.made++; return node(); } createBiquadFilter() { return node(); }
    createBufferSource() { return node(); } createBuffer(c, n) { return { getChannelData: () => new Float32Array(n) }; } resume() {} close() {} }
  const target = new EventTarget(), sfx = createSfx({ target, Ctx });
  sfx.thud(); sfx.rattle();
  assert.deepEqual([made.length, sfx.live], [0, false]);
  target.dispatchEvent(new Event('keydown'));
  assert.equal(sfx.live, true);
  const before = made[0].made;
  sfx.thud(); sfx.slam(); sfx.whoosh(); sfx.rattle();
  assert.ok(made[0].made > before);
  sfx.duck(true);
  target.dispatchEvent(new Event('keydown'));
  assert.equal(made.length, 1, 'one context only');
  sfx.dispose();
});
