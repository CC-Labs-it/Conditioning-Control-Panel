import test from 'node:test';
import assert from 'node:assert/strict';
import { bubblePlan, popTime, nextTakes, nearestDist, POP_CAP_S, newBubble, stepBubble, popOrder, swallowStyle, WAKE_MIN_SCALE, parseTranslate, parseScale } from '../ui/unlocks/themes-a/bimbodoll.js';

const SCREENS = [
  { name: 'desktop', w: 1920, h: 1080, cx: 960, cy: 420 },
  { name: 'laptop', w: 1366, h: 768, cx: 683, cy: 300 },
  { name: 'phone', w: 390, h: 844, cx: 195, cy: 300 },
];

for (const s of SCREENS) {
  const plan = bubblePlan({ ...s, r0: 50 });
  test(`${s.name}: an untouched bubble pops in about 90 s`, () => {
    const t = popTime(plan, 0);
    assert.ok(t > 85 && t < 95, `popped at ${t.toFixed(1)} s`);
  });
  test(`${s.name}: a hammering player pops it in 30-40 s`, () => {
    for (const perS of [3, 5, 8]) {
      const t = popTime(plan, perS);
      assert.ok(t >= 30 && t <= 40, `${perS}/s popped at ${t.toFixed(1)} s`);
    }
  });
  test(`${s.name}: the pop needs the furthest corner covered`, () => {
    const far = Math.max(Math.hypot(s.cx, s.cy), Math.hypot(s.w - s.cx, s.h - s.cy), Math.hypot(s.w - s.cx, s.cy), Math.hypot(s.cx, s.h - s.cy));
    assert.equal(plan.rPop, far);
  });
}

test('the pop comes by 100 s whatever the radius', () => {
  const plan = { ...bubblePlan({ w: 1920, h: 1080, cx: 960, cy: 540, r0: 50 }), floor: 0 };
  const b = newBubble(plan);
  while (!b.popped) stepBubble(b, plan, 1 / 30);
  assert.ok(Math.abs(b.t - POP_CAP_S) < 0.05, `popped at ${b.t}`);
  assert.ok(b.r < plan.rPop);
});

const rect = (x, y, w = 40, h = 20) => ({ left: x, top: y, right: x + w, bottom: y + h });

test('nearest point distance', () => {
  assert.equal(nearestDist(rect(0, 0, 100, 100), 50, 50), 0);
  assert.equal(nearestDist(rect(110, 50), 100, 60), 10);
});

/** Grows the bubble from 0 and returns the ids in the order they were taken. */
function order(cands, cx, cy, max = 2000) {
  const seq = [];
  for (let r = 0; r <= max && seq.length < cands.filter((c) => !c.wake).length; r += 5) {
    for (const c of nextTakes(cands, cx, cy, r)) { c.taken = true; seq.push(c.id); }
  }
  return seq;
}

test('nearest first, the counter second to last, Wake Up never', () => {
  const cands = [
    { id: 'counter', kind: 'counter', rect: rect(480, 380) },   // right next to the button
    { id: 'near', rect: rect(560, 400) },
    { id: 'mid', rect: rect(800, 400) },
    { id: 'far', rect: rect(1500, 900) },
    { id: 'wake', wake: true, rect: rect(495, 395) },
  ];
  assert.deepEqual(order(cands, 500, 400), ['near', 'mid', 'counter', 'far']);
});

test('the counter waits even when the last two arrive together', () => {
  const cands = [
    { id: 'counter', kind: 'counter', rect: rect(480, 380) },
    { id: 'a', rect: rect(700, 400) },
    { id: 'b', rect: rect(702, 400) },
  ];
  const seq = order(cands, 500, 400);
  assert.equal(seq.indexOf('counter'), 1);
  assert.equal(seq.length, 3);
});

test('a forced candidate (the last terms card) goes at once, wherever it is', () => {
  const cands = [{ id: 'card', force: true, rect: rect(1800, 1000) }, { id: 'x', rect: rect(900, 400) }];
  assert.deepEqual(nextTakes(cands, 0, 0, 10).map((c) => c.id), ['card']);
});

test('Wake Up is never taken while the bubble grows, not even forced or at distance 0', () => {
  const cands = [{ id: 'wake', wake: true, force: true, rect: rect(0, 0) }];
  assert.deepEqual(nextTakes(cands, 10, 10, 5000), []);
});

test('at the pop Wake Up goes in last, after everything still out', () => {
  const cands = [
    { id: 'wake', wake: true, rect: rect(0, 0) },
    { id: 'a', rect: rect(10, 10) },
    { id: 'gone', taken: true, rect: rect(20, 20) },
    { id: 'b', kind: 'counter', rect: rect(30, 30) },
  ];
  assert.deepEqual(popOrder(cands).map((c) => c.id), ['a', 'b', 'wake']);
  assert.deepEqual(popOrder([{ id: 'wake', wake: true, taken: true }]), []);
});

test('Wake Up inside the bubble stays usable: scale floor 0.55, pointer events kept, full opacity', () => {
  const w = swallowStyle({ wake: true });
  assert.equal(WAKE_MIN_SCALE, 0.55);
  assert.equal(w.minScale, 0.55);
  assert.equal(w.pointerEvents, null);   // null = its own pointer events are left alone
  assert.equal(w.opacity, 1);
  const o = swallowStyle({ kind: 'obj' });
  assert.equal(o.pointerEvents, 'none');
  assert.ok(o.minScale < 0.55 && o.opacity < 1);
});

test('a node placed with its own translate/scale keeps it: the swallow composes, never replaces', () => {
  assert.deepEqual(parseTranslate('none'), [0, 0]);
  assert.deepEqual(parseTranslate(''), [0, 0]);
  assert.deepEqual(parseTranslate('412.5px -30px'), [412.5, -30]);
  assert.deepEqual(parseTranslate('12px'), [12, 0]);
  assert.deepEqual(parseTranslate('50% 10%', 200, 100), [100, 10]);
  assert.deepEqual(parseScale('none'), [1, 1]);
  assert.deepEqual(parseScale('1.5'), [1.5, 1.5]);
  assert.deepEqual(parseScale('1 0.5'), [1, 0.5]);
});
