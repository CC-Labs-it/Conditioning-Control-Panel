// tests/boot-actions.test.mjs - the B1 input seam (integrator): item edge and back, latched like the hop edge.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createActionMap, createEdgeLatch, createHopLatch, createItemLatch } from '../core/actions.js';
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

function keyboardRig() {
  const target = new EventTarget(), kb = createKeyboard({ target });
  const map = createActionMap([kb], { items: true }), latch = createItemLatch();
  const key = (type, code, extra = {}) => {
    const e = new Event(type, { cancelable: true });
    Object.assign(e, { code }, extra); target.dispatchEvent(e); return e;
  };
  return { target, kb, map, latch, key, sample: () => latch.hold(map.sample()) };
}

test('R / Backspace throw behind without brake and keep their direction across zero-step frames', () => {
  for (const code of ['KeyR', 'Backspace']) {
    const r = keyboardRig();
    r.key('keydown', 'ArrowUp'); r.key('keydown', code); r.key('keyup', code);
    let a = r.sample();
    assert.equal(a.item, true); assert.equal(a.back, true); assert.equal(a.brake, 0); assert.equal(a.throttle, 1);
    // The 144 Hz draw clock samples again before the next 60 Hz simulation step.
    a = r.sample(); assert.equal(a.item, true); assert.equal(a.back, true);
    r.latch.spent(a); a = r.sample(); assert.equal(a.item, false); assert.equal(a.back, false);
    r.key('keydown', 'KeyE'); a = r.sample();
    assert.equal(a.item, true); assert.equal(a.back, false, 'the next forward throw never inherits the rear direction');
    r.map.dispose();
  }
});

test('right-hand arrows, right Shift and Enter combine without braking or repeated throws', () => {
  const r = keyboardRig();
  for (const code of ['ArrowUp', 'ArrowRight', 'ShiftRight', 'Enter']) r.key('keydown', code);
  let a = r.sample();
  assert.equal(a.throttle, 1); assert.equal(a.brake, 0); assert.equal(a.hop, true); assert.equal(a.hopPressed, true);
  assert.equal(a.item, true); assert.equal(a.back, false);
  r.latch.spent(a); r.key('keydown', 'Enter', { repeat: true });
  a = r.sample(); assert.equal(a.item, false); assert.equal(a.hopPressed, false);
  r.target.dispatchEvent(new Event('blur')); r.latch.clear();
  a = r.sample(); assert.equal(a.throttle, 0); assert.equal(a.hop, false); assert.equal(a.item, false);
  r.map.dispose();
});

test('throw shortcuts leave typing, focused menu activation and browser modifiers alone', () => {
  const r = keyboardRig();
  for (const tag of ['INPUT', 'TEXTAREA', 'SELECT', 'BUTTON', 'A']) {
    r.target.tagName = tag;
    const e = r.key('keydown', 'Enter');
    assert.equal(e.defaultPrevented, false, tag); assert.equal(r.sample().item, false, tag);
  }
  r.target.tagName = 'DIV'; r.target.isContentEditable = true;
  assert.equal(r.key('keydown', 'Backspace').defaultPrevented, false); assert.equal(r.sample().item, false);
  r.target.isContentEditable = false;
  const picked = new Event('keydown', { cancelable: true });
  Object.assign(picked, { code: 'Enter' }); picked.preventDefault(); r.target.dispatchEvent(picked);
  assert.equal(r.sample().item, false, 'a menu-consumed Enter never becomes a throw');
  for (const modifier of ['ctrlKey', 'altKey', 'metaKey']) {
    const e = r.key('keydown', 'KeyR', { [modifier]: true });
    assert.equal(e.defaultPrevented, false); assert.equal(r.sample().item, false);
  }
  r.key('keydown', 'KeyR'); r.target.dispatchEvent(new Event('blur'));
  assert.equal(r.sample().item, false, 'blur clears an unsampled throw');
  r.map.dispose();
});

test('item latch snapshots forward direction and clears pending rear throws on pause', () => {
  const r = keyboardRig();
  r.key('keydown', 'Enter'); let a = r.sample(); assert.equal(a.back, false);
  r.key('keydown', 'ArrowDown'); a = r.sample();
  assert.equal(a.item, true); assert.equal(a.back, false, 'later braking cannot redirect a queued forward throw');
  r.latch.clear(); r.key('keyup', 'ArrowDown'); r.key('keyup', 'Enter');
  a = r.sample(); assert.equal(a.item, false); assert.equal(a.back, false);
  r.key('keydown', 'KeyR'); a = r.sample(); assert.equal(a.back, true);
  r.latch.clear(); r.key('keyup', 'KeyR');
  a = r.sample(); assert.equal(a.item, false); assert.equal(a.back, false);
  r.map.dispose();
});
