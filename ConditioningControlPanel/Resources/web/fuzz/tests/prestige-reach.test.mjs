// Reach lane (wave 2): Trigger Words Everywhere on the slot reels and podcast captions, Autopilot for the Wheel,
// Mini Slots, Prize Dispenser and Race bonus. Brief: handoff-1109/TC-PRESTIGE-WIRE-BRIEF.md.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import trig, { canCatchEverywhere, catchEverywhere, wordsForEverywhere } from '../ui/unlocks/prestige/trig.js';
import { AUTOPILOT_KEY, SLOT_MEAN, autopilot, autopilotStep, wheelMean } from '../ui/unlocks/arcade/autopilot.js';
import { SECTORS } from '../sim/prize-wheel.js';
import { WORD_BANK } from '../data/prestige.js';

const prestige = (stars, words = []) => ({ has: (id) => stars.includes(id), next: { words }, level: () => 0 });
const read = (p) => readFileSync(new URL(p, import.meta.url), 'utf8');

test('Everywhere words: none without trig_7, the run words with it', () => {
  assert.deepEqual(wordsForEverywhere({}), []);
  assert.deepEqual(wordsForEverywhere({ prestige: prestige(['trig_1']) }), []);
  assert.deepEqual(wordsForEverywhere({ prestige: prestige(['trig_1', 'trig_7']) }), [WORD_BANK[0]]);
  const two = wordsForEverywhere({ prestige: prestige(['trig_1', 'trig_5', 'trig_7'], ['obey', 'sink', 'pink']) });
  assert.deepEqual(two, ['obey', 'sink']);
});

/* A tiny DOM, just enough for the trig entry to mount. */
function fakeDoc() {
  const doc = {
    createElement() {
      const el = { style: {}, children: [], attrs: {}, className: '', textContent: '', ownerDocument: doc,
        appendChild(c) { this.children.push(c); return c; }, setAttribute(k, v) { this.attrs[k] = v; },
        addEventListener() {}, remove() {}, querySelector() { return null; }, querySelectorAll() { return []; },
        getBoundingClientRect() { return { left: 0, top: 0, width: 0, height: 0, right: 0, bottom: 0 }; } };
      return el;
    },
    defaultView: {},
  };
  return doc;
}

function trigCtx(stars, words) {
  const doc = fakeDoc(), cleanups = [], paid = [], store = {};
  const ctx = {
    prestige: prestige(stars, words), pstore: {}, button: { el: { ownerDocument: doc } },
    css() {}, rng: () => 0.5, gentle: false, reduced: false, paused: false,
    store: { get: (k) => store[k], set: (k, v) => { store[k] = v; } },
    rates: () => ({ fps: 10, click: 1 }), bounds: () => ({ w: 800, h: 600 }),
    layer: () => doc.createElement('div'), cleanup: (f) => cleanups.push(f),
    anim: () => ({ pause() {}, cancel() {} }), after: () => () => {}, float() {}, say() {}, voice() {},
    pay: (n, src, at) => { paid.push({ n, src, at }); return n; }, has: () => false, switches: { isOn: () => false },
    fmt: String,
  };
  return { ctx, paid, unmount: () => cleanups.forEach((f) => f()) };
}

test('a word caught elsewhere pays like the floating word, only while the lane is mounted', () => {
  assert.equal(canCatchEverywhere(), false);
  assert.equal(catchEverywhere('sink', { x: 1, y: 2 }), false);
  const f = trigCtx(['trig_1', 'trig_7'], ['sink']);
  trig.trig.mount(f.ctx);
  assert.equal(canCatchEverywhere(), true);
  assert.equal(catchEverywhere('pink'), false, 'a word the player did not pick is not theirs');
  assert.equal(catchEverywhere('SINK', { x: 10, y: 20 }), true);
  assert.equal(f.paid.length, 1);
  assert.equal(f.paid[0].src, 'trig');
  assert.equal(f.paid[0].n, 10 * 5, 'a bare catch asks CATCH_S seconds of income');
  assert.deepEqual(f.paid[0].at, { x: 10, y: 20 });
  assert.equal(f.ctx.pstore.catches.sink, 1, 'counted into Conditioned like the floating word');
  f.unmount();
  assert.equal(canCatchEverywhere(), false);
});

test('without trig_7 the lane offers no outside catch', () => {
  const f = trigCtx(['trig_1'], ['sink']);
  trig.trig.mount(f.ctx);
  assert.equal(canCatchEverywhere(), false);
  f.unmount();
});

test('autopilot clock: due once per window of idle time, never while taken', () => {
  assert.deepEqual(autopilotStep(0, 1000, false, 5000), { ms: 0, due: 0 });
  assert.deepEqual(autopilotStep(4000, 500, true, 5000), { ms: 4500, due: 0 });
  assert.deepEqual(autopilotStep(4500, 900, true, 5000), { ms: 400, due: 1 });
  assert.deepEqual(autopilotStep(0, 1e9, true, 5000), { ms: 0, due: 1 }, 'a huge step pays one, not a pile');
  assert.deepEqual(autopilotStep(NaN, NaN, true, 5000), { ms: 0, due: 0 });
});

test('autopilot needs idle_6, keeps its clock in the toy store and pays half', () => {
  const store = {};
  const make = (stars) => autopilot({ prestige: prestige(stars), store: { get: (k) => store[k], set: (k, v) => { store[k] = v; } } }, { dueMs: 3000 });
  const off = make([]);
  assert.equal(off.share, 0);
  assert.equal(off.step(5000, true), false);
  const on = make(['idle_6']);
  assert.equal(on.share, 0.5);
  assert.equal(on.step(2000, true), false);
  assert.equal(store[AUTOPILOT_KEY], 2000);
  assert.equal(make(['idle_6']).step(1000, true), true, 'a remount picks the clock up from the store');
  on.reset();
  assert.equal(store[AUTOPILOT_KEY], 0);
});

test('average rewards the autopilot halves', () => {
  assert.ok(Math.abs(wheelMean(SECTORS) - 1.5) < 1e-9, 'the wheel prints a 1.5x average');
  assert.ok(Math.abs(SLOT_MEAN.fps - 18.75) < 1e-9);
  assert.ok(Math.abs(SLOT_MEAN.click - 100 / 3) < 1e-9);
});

test('hooks are wired and marked in the toy files', () => {
  const wheel = read('../ui/unlocks/basics/prize-wheel.js');
  assert.match(wheel, /autopilot\(ctx, \{ dueMs: WHEEL_WAIT \* 1000 \}\)/);
  assert.match(wheel, /auto\.reset\(\)/);
  const slots = read('../ui/unlocks/arcade/slots.js');
  assert.match(slots, /wordsForEverywhere\(ctx\)/);
  assert.match(slots, /word:reelWord/);
  assert.match(slots, /auto\.step\(step,s\.coins===3&&!s\.pending\)/);
  for (const f of ['../ui/unlocks/arcade/dispenser.js', '../ui/unlocks/arcade/race.js'])
    assert.match(read(f), /prestige hook: Autopilot \(idle_6\)/, f);
  const pod = read('../ui/unlocks/podcast-player.js');
  assert.match(pod, /wordsForEverywhere\(ctx\)/);
  assert.match(pod, /catchEverywhere\(word, at\)/);
});
