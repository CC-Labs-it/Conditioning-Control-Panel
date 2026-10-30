import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createCourseController} from '../core/courseSession.js';
import {createLifetime, scopedBus, scopedBridge} from '../core/lifetime.js';
import {createBus} from '../core/events.js';

const deferred = () => { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no; }); return {promise, resolve, reject}; };
const tick = () => new Promise(resolve => setImmediate(resolve));

test('course selection serializes GPU opens, skips obsolete pending selections and rejects stale readiness', async () => {
  const gate = deferred(), calls = [], cleaned = [], checks = new Map(); let active = 0, peak = 0;
  const controller = createCourseController({async open(id, options, life, isCurrent) {
    calls.push(id); active++; peak = Math.max(peak, active); checks.set(id, isCurrent);
    life.defer(() => cleaned.push(id));
    if (id === 'first') await gate.promise;
    active--;
  }});
  const first = controller.select('first'); await tick();
  const skipped = controller.select('middle'), latest = controller.select('latest');
  assert.equal(checks.get('first')(), false, 'pending selection invalidates stale callbacks before GPU work finishes');
  assert.deepEqual(calls, ['first']); assert.deepEqual(cleaned, [], 'in-flight open is not torn down by another open');
  gate.resolve();
  assert.equal(await first, false); assert.equal(await skipped, false); assert.equal(await latest, true);
  assert.deepEqual(calls, ['first', 'latest']); assert.deepEqual(cleaned, ['first']); assert.equal(peak, 1);
  assert.equal(controller.active, 'latest'); assert.equal(controller.busy, false);
  assert.equal(checks.get('latest')(), true); controller.dispose(); controller.dispose();
  assert.deepEqual(cleaned, ['first', 'latest']); assert.equal(checks.get('latest')(), false);
});

test('a ready course is not rebuilt unless forced, and forced replacement releases its predecessor once', async () => {
  let opened = 0, cleaned = 0;
  const controller = createCourseController({async open(id, options, life) { opened++; life.defer(() => cleaned++); }});
  assert.equal(await controller.select('keep'), true);
  assert.equal(await controller.select('keep'), false); assert.equal(opened, 1); assert.equal(cleaned, 0);
  assert.equal(await controller.select('keep', {force: true}), true); assert.equal(opened, 2); assert.equal(cleaned, 1);
  controller.dispose(); assert.equal(cleaned, 2);
});

test('failed opening cleans partial resources, reports the current failure and permits retry', async () => {
  const failure = Error('compile failed'), errors = []; let attempts = 0, cleaned = 0;
  const controller = createCourseController({fail: error => errors.push(error), async open(id, options, life) {
    life.defer(() => cleaned++); if (++attempts === 1) throw failure;
  }});
  assert.equal(await controller.select('boudoir'), false); assert.equal(controller.active, null);
  assert.equal(controller.busy, false); assert.deepEqual(errors, [failure]); assert.equal(cleaned, 1);
  assert.equal(await controller.select('boudoir'), true); assert.equal(attempts, 2);
  controller.dispose(); assert.equal(cleaned, 2);
});

test('an obsolete failure does not replace the newest selection with an error', async () => {
  const gate = deferred(), errors = [], opened = [];
  const controller = createCourseController({fail: error => errors.push(error), async open(id) {
    opened.push(id); if (id === 'old') await gate.promise;
  }});
  const old = controller.select('old'); await tick(); const next = controller.select('new');
  gate.reject(Error('old compile failed')); assert.equal(await old, false); assert.equal(await next, true);
  assert.deepEqual(errors, []); assert.deepEqual(opened, ['old', 'new']); controller.dispose();
});

test('shutdown invalidates running callbacks, skips pending opens and releases late deferred resources', async () => {
  const gate = deferred(); let cleaned = 0, opened = 0, isCurrent;
  const controller = createCourseController({async open(id, options, life, check) {
    opened++; isCurrent = check; life.defer(() => cleaned++);
    await gate.promise; life.defer(() => cleaned++);
  }});
  const first = controller.select('first'); await tick(); const pending = controller.select('pending');
  controller.dispose(); controller.dispose();
  assert.equal(isCurrent(), false); assert.equal(controller.active, null); assert.equal(controller.busy, false); assert.equal(cleaned, 1);
  gate.resolve(); assert.equal(await first, false); assert.equal(await pending, false);
  assert.equal(opened, 1); assert.equal(cleaned, 2); assert.equal(await controller.select('later'), false);
});

test('lifetime removes DOM listeners and disposes in reverse order once, even after a throwing cleanup', () => {
  const life = createLifetime(), target = new EventTarget(), log = [], warn = console.warn; let calls = 0;
  const listener = () => calls++;
  life.listen(target, 'pulse', listener); target.dispatchEvent(new Event('pulse')); assert.equal(calls, 1);
  life.defer(() => log.push(['first', life.alive])); life.defer(() => { throw Error('cleanup failure'); });
  life.defer(() => log.push(['last', life.alive]));
  try { console.warn = () => {}; life.dispose(); life.dispose(); } finally { console.warn = warn; }
  assert.deepEqual(log, [['last', false], ['first', false]]);
  target.dispatchEvent(new Event('pulse')); life.listen(target, 'pulse', listener); target.dispatchEvent(new Event('pulse'));
  assert.equal(calls, 1); life.defer(() => log.push(['late', life.alive])); assert.deepEqual(log.at(-1), ['late', false]);
});

test('scoped bus off maps the original callback, preserves foreign subscriptions and matches duplicate removal order', () => {
  const parent = createBus(), life = createLifetime(), bus = scopedBus(parent, life); let calls = 0, removed = 0;
  const originalOn = parent.on; parent.on = (...args) => { const off = originalOn(...args); return () => { removed++; off(); }; };
  const fn = () => calls++;
  parent.on('event', fn); const first = bus.on('event', fn), second = bus.on('event', fn);
  bus.off('other', fn); bus.off('event', () => {}); parent.emit('event'); assert.equal(calls, 3);
  bus.off('event', fn); first(); parent.emit('event'); assert.equal(calls, 5); assert.equal(removed, 1);
  second(); second(); bus.off('event', fn); parent.emit('event'); assert.equal(calls, 6); assert.equal(removed, 2);
  bus.on('event', fn); life.dispose(); parent.emit('event'); assert.equal(calls, 7); assert.equal(removed, 3);
  bus.on('event', fn); bus.emit('event'); assert.equal(calls, 7, 'dead scopes cannot subscribe or emit');
});

test('scope death silences bus callbacks already snapshotted for an event and preserves the host bridge', () => {
  const parent = createBus(), life = createLifetime(), bus = scopedBus(parent, life), seen = [];
  let hostDisposed = 0;
  const bridge = scopedBridge({...parent, dispose() { hostDisposed++; }}, life);
  bus.on('tick', () => { seen.push('first'); life.dispose(); });
  bus.on('tick', () => seen.push('late')); bridge.on('host', () => seen.push('host'));
  parent.emit('tick'); parent.emit('host'); bridge.dispose();
  assert.deepEqual(seen, ['first']); assert.equal(hostDisposed, 0);
});
