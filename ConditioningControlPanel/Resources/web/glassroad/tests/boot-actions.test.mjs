// tests/boot-actions.test.mjs - the B1 input seam (integrator): item edge and back, latched like the hop edge.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createActionMap, createEdgeLatch, createHopLatch } from '../core/actions.js';
import { createKeyboard } from '../input/keyboard.js';
import { createGamepad } from '../input/gamepad.js';

const src = (o) => ({ poll: (into) => Object.assign(into, o), dispose() {} });

test('action map with items: item ORs as an edge, back as a held flag; Phase A shape without it', () => {
  const map = createActionMap([src({ throttle: 1 }), src({ item: true }), src({ back: true })], { items: true });
  const a = map.sample();
  assert.equal(a.item, true); assert.equal(a.back, true); assert.equal(a.throttle, 1);
  const plain = createActionMap([src({ item: true })]).sample();
  assert.equal('item' in plain, false, 'no items option keeps the Phase A fields only');
});

test('edge latch: an item press seen on a frame with no sim step reaches the next step once', () => {
  const latch = createEdgeLatch('item'), a = { item: true };
  latch.hold(a); a.item = false; latch.hold(a);
  assert.equal(a.item, true, 'still pending on the next frame');
  latch.spent(a); assert.equal(a.item, false); latch.hold(a); assert.equal(a.item, false);
  const hop = createHopLatch(), b = { hopPressed: true };
  hop.hold(b); hop.clear(); b.hopPressed = false; hop.hold(b); assert.equal(b.hopPressed, false);
});

test('keyboard: E is one item edge per press, S / Down hold back', () => {
  const target = new EventTarget();
  const key = (type, code, extra = {}) => { const e = new Event(type, { cancelable: true }); Object.assign(e, { code }, extra); target.dispatchEvent(e); return e; };
  const kb = createKeyboard({ target }), into = {};
  key('keydown', 'KeyE'); key('keydown', 'ArrowDown');
  kb.poll(into);
  assert.equal(into.item, true); assert.equal(into.back, true);
  key('keydown', 'KeyE', { repeat: true }); kb.poll(into);
  assert.equal(into.item, false, 'a held key repeats no edge');
  key('keyup', 'ArrowDown'); kb.poll(into); assert.equal(into.back, false);
});

test('gamepad: X item edge, stick down holds back, takeNav gives one card edge per press', () => {
  const pad = { connected: true, mapping: 'standard', axes: [0, 0], buttons: Array.from({ length: 17 }, () => ({ value: 0, pressed: false })) };
  const gp = createGamepad({ nav: { getGamepads: () => [pad] } }), into = {};
  pad.buttons[2].value = 1; pad.axes[1] = 0.8;
  gp.poll(into);
  assert.equal(into.item, true); assert.equal(into.back, true);
  assert.equal(gp.takeNav(), 'down'); assert.equal(gp.takeNav(), null);
  gp.poll(into); assert.equal(into.item, false, 'held X is one edge'); assert.equal(gp.takeNav(), null, 'held stick is one nav edge');
  pad.buttons[2].value = 0; pad.axes[1] = 0; pad.buttons[1].value = 1;
  gp.poll(into); assert.equal(gp.takeNav(), 'back');
});
