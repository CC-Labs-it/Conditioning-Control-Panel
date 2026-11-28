import { test } from 'node:test';
import assert from 'node:assert/strict';
import basics from '../ui/unlocks/basics/side.js';
import { collectionBeats, createCollectionFeel } from '../ui/unlocks/basics/collect-feel.js';
import { COLLECT_MAX_SECONDS, xpNeed } from '../sim/levels.js';

function harness(xp = 0) {
  const made = [], timers = new Set(), cleanups = [], frames = [], pays = [], bag = { lvl: 1, xp };
  const make = (tag = 'div') => {
    const e = { tag, style: {}, dataset: {}, listeners: {}, classList: { toggle() {} },
      addEventListener(k, fn) { this.listeners[k] = fn; },
      appendChild() {}, remove() { this.removed = true; },
      getBoundingClientRect: () => ({ left: 100, top: 100, width: 100, height: 25 }),
    };
    made.push(e); return e;
  };
  const root = make(), button = make('button');
  button.ownerDocument = { createElement: make };
  const ctx = {
    paused: false, gentle: false, reduced: false, restored: true,
    button: { el: button }, parts: { counter: make() }, state: { order: ['levels'] }, item: { id: 'levels' },
    store: { get: (k) => bag[k], set: (k, v) => { bag[k] = v; } },
    has: () => false, switches: { isOn: () => true }, css() {}, layer: () => root,
    el: (tag) => make(tag), fmt: (n) => String(n), bounds: () => ({ w: 390, h: 800 }),
    rates: () => ({ fps: 20, click: 5 }), rng: () => .5, on() {}, every() {}, say() {},
    pay: (n, source) => { pays.push({ n, source }); ctx.reenter?.(); return 123; },
    frame: (fn) => frames.push(fn), cleanup: (fn) => cleanups.push(fn),
    after: (ms, fn) => { const t = { ms, fn }; timers.add(t); return () => timers.delete(t); },
    anim: () => ({ cancel() { this.oncancel?.(); } }),
  };
  return { ctx, made, timers, cleanups, frames, pays, bag };
}

test('huge banks remain a bounded short phrase', () => {
  const beats = collectionBeats(1000000);
  assert.equal(beats.length, 6);
  assert.ok(beats.at(-1).at <= 600);
  assert.ok(beats.every((b, i) => i === 0 || b.note > beats[i - 1].note));
  assert.equal(collectionBeats(1).length, 1);
});

test('collect consumes the bank once, pays through the original cap and shows the credited amount', () => {
  const h = harness(Array.from({ length: 100 }, (_, i) => xpNeed(i + 1)).reduce((a, b) => a + b, 0) + 5);
  basics.levels.mount(h.ctx);
  const collect = h.made.find((e) => 'collectLevels' in e.dataset);
  h.ctx.reenter = () => collect.listeners.click();
  collect.listeners.click();
  collect.listeners.click();
  assert.deepEqual(h.pays, [{ n: 20 * COLLECT_MAX_SECONDS, source: 'levels' }]);
  assert.equal(h.bag.lvl, 101);
  assert.ok(Math.abs(h.bag.xp - 5) < 1e-7);
  assert.equal(h.made.find((e) => 'collectionReceipt' in e.dataset).textContent, '+123');
});

test('pause does not collect, and a restored mount does not celebrate', () => {
  const h = harness(xpNeed(1) + 3);
  basics.levels.mount(h.ctx);
  assert.equal(h.timers.size, 0);
  h.ctx.paused = true;
  h.made.find((e) => 'collectLevels' in e.dataset).listeners.click();
  assert.equal(h.pays.length, 0);
  assert.equal(h.bag.lvl, 1);
});

test('replacement and disposal cancel the previous show and all pending notes', () => {
  const h = harness();
  const feel = createCollectionFeel(h.ctx);
  feel.show({ levels: 6, amount: 50, x: 150, y: 100 });
  const first = h.made.find((e) => 'collectionReceipt' in e.dataset);
  feel.show({ levels: 1, amount: 4, x: 150, y: 100 });
  assert.equal(first.removed, true);
  assert.equal(h.timers.size, 2);
  h.cleanups.forEach((fn) => fn());
  assert.equal(h.timers.size, 0);
  assert.ok(h.made.filter((e) => 'collectionReceipt' in e.dataset).every((e) => e.removed));
});

test('switching Gentle on replaces travel with one still receipt', () => {
  const h = harness();
  const feel = createCollectionFeel(h.ctx);
  feel.show({ levels: 6, amount: 50, x: 150, y: 100 });
  h.ctx.gentle = true;
  h.frames.forEach((fn) => fn());
  assert.equal(h.timers.size, 1);
  assert.equal([...h.timers][0].ms, 2400);
});
