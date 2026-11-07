import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createGarageBubbles, popPitch, GARAGE_BUBBLES } from '../hud/garageBubbles.js';

function fakeEl() {
  const el = { children: [], parent: null, style: { setProperty() {} }, listeners: {}, className: '', clientWidth: 1600, clientHeight: 900,
    classList: { items: new Set(), add(c) { this.items.add(c); }, has(c) { return this.items.has(c); } },
    setAttribute() {},
    append(c) { c.parent = el; el.children.push(c); },
    prepend(c) { c.parent = el; el.children.unshift(c); },
    remove() { if (el.parent) el.parent.children = el.parent.children.filter(c => c !== el); el.parent = null; },
    addEventListener(t, fn) { el.listeners[t] = fn; },
    removeEventListener(t) { delete el.listeners[t]; } };
  return el;
}
const make = (o = {}) => {
  const root = fakeEl(), sounds = [];
  const kit = createGarageBubbles({ root, pop: s => sounds.push(s), rng: () => .5, doc: { createElement: fakeEl }, ...o });
  return { root, kit, sounds, bubbles: () => root.children[0].children };
};
const run = (kit, seconds, opts) => { for (let t = 0; t < seconds; t += .05) kit.update(.05, opts); };
const press = el => el.listeners.pointerdown({ button: 0, cancelable: true, preventDefault() {} });

test('garage bubbles: the layer goes behind the menu, is hidden from readers, and starts quickly', () => {
  const { root, kit } = make();
  assert.equal(root.children[0], kit.layer);
  assert.equal(kit.count, 0);
  run(kit, GARAGE_BUBBLES.firstS + .1);
  assert.equal(kit.count, 1);
});

test('garage bubbles: never more than the cap, phones fewer, and they leave at the top', () => {
  const desktop = make(); let most = 0;
  for (let t = 0; t < 40; t += .05) { desktop.kit.update(.05); most = Math.max(most, desktop.kit.count); }
  assert.ok(most <= GARAGE_BUBBLES.max && most > 1, 'desktop count ' + most);
  const phone = make(); phone.root.clientWidth = 412; phone.root.clientHeight = 915; most = 0;
  for (let t = 0; t < 40; t += .05) { phone.kit.update(.05); most = Math.max(most, phone.kit.count); }
  assert.ok(most <= GARAGE_BUBBLES.maxPhone, 'phone count ' + most);
  const risen = make(); risen.kit.update(.5); assert.equal(risen.kit.count, 1);
  run(risen.kit, 40, { spawn: false }); assert.equal(risen.kit.count, 0);
});

test('garage bubbles: a press pops once, plays once and removes the bubble, a second press is nothing', () => {
  const { kit, sounds, bubbles } = make();
  kit.update(.5);
  const el = bubbles()[0], first = el.listeners.pointerdown;
  press(el); first({ button: 0 });
  assert.equal(sounds.length, 1);
  assert.ok(el.classList.has('g-bubble-pop'));
  assert.equal(el.children.length, GARAGE_BUBBLES.drops);
  run(kit, GARAGE_BUBBLES.popS + .1, { spawn: false });
  assert.equal(kit.count, 0);
});

test('garage bubbles: right and middle buttons do not pop', () => {
  const { kit, sounds, bubbles } = make();
  kit.update(.5);
  bubbles()[0].listeners.pointerdown({ button: 2 });
  assert.equal(sounds.length, 0);
});

test('garage bubbles: Gentle pops as a plain fade of at least 300 ms, with fewer bubbles', () => {
  const { kit, sounds, bubbles } = make(); let most = 0;
  for (let t = 0; t < 60; t += .05) { kit.update(.05, { gentle: true }); most = Math.max(most, kit.count); }
  assert.ok(most <= GARAGE_BUBBLES.max * .5, 'gentle count ' + most);
  const el = bubbles()[0]; press(el);
  assert.ok(el.classList.has('g-bubble-soft')); assert.equal(el.children.length, 0);
  assert.equal(sounds.length, 1);
  assert.ok(GARAGE_BUBBLES.softPopS >= .3);
});

test('garage bubbles: reduced motion or Motion off clears them and starts none', () => {
  const { kit } = make();
  kit.update(.5); assert.equal(kit.count, 1);
  kit.update(.05, { off: true }); assert.equal(kit.count, 0);
  run(kit, 5, { off: true }); assert.equal(kit.count, 0);
});

test('garage bubbles: none start while the carousel is off screen', () => {
  const { kit } = make();
  run(kit, 5, { spawn: false }); assert.equal(kit.count, 0);
});

test('garage bubbles: they start right of the menu', () => {
  const { kit, bubbles } = make({ leftEdge: () => 400 });
  kit.update(.5);
  const m = /translate3d\(([-\d.]+)px/.exec(bubbles()[0].style.transform);
  assert.ok(Number(m[1]) >= 400 - GARAGE_BUBBLES.swayPx[1], 'x ' + m[1]);
});

test('garage bubbles: a phone menu is the floor they rise from', () => {
  const { kit, bubbles } = make({ floor: () => 500 });
  kit.update(.5);
  const m = /translate3d\([-\d.]+px,([-\d.]+)px/.exec(bubbles()[0].style.transform);
  assert.ok(Number(m[1]) <= 500 && Number(m[1]) > 400, 'y ' + m[1]);
});

test('garage bubbles: quick pops climb the ladder, a pause starts it again', () => {
  const streak = { rung: 0, t: -Infinity };
  const first = popPitch(streak, 10), second = popPitch(streak, 10.5), third = popPitch(streak, 11);
  assert.equal(first, 1);
  assert.ok(second > first && third > second);
  assert.equal(popPitch(streak, 20), 1);
});

test('garage bubbles: dispose leaves nothing behind', () => {
  const { root, kit } = make();
  kit.update(.5); kit.dispose(); kit.dispose();
  assert.equal(root.children.length, 0);
  kit.update(.05); assert.equal(kit.count, 0);
});
