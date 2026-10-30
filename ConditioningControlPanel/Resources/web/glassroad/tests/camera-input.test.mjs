import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createActionMap } from '../core/actions.js';
import { createKeyboard } from '../input/keyboard.js';
import { createGamepad } from '../input/gamepad.js';
import { createTouch } from '../input/touch.js';
import { createDevices } from '../input/device.js';
import { controlRows } from '../hud/labels.js';

test('camera actions are opt-in, additive and independent of item drop', () => {
  let input = { cameraPressed: true, rearView: true };
  const source = { poll: out => Object.assign(out, input) };
  const plain = createActionMap([source]).sample();
  assert.equal('cameraPressed' in plain, false); assert.equal('rearView' in plain, false);
  const map = createActionMap([source, { poll: out => { out.back = true; } }], { camera: true, items: true });
  let a = map.sample();
  assert.equal(a.cameraPressed, true); assert.equal(a.rearView, true); assert.equal(a.back, true);
  input = {}; a = map.sample();
  assert.equal(a.cameraPressed, false); assert.equal(a.rearView, false); assert.equal(a.back, true);
});

test('C toggles once, V releases after a focus change, blur clears camera inputs', () => {
  const target = new EventTarget(), into = {}, keyboard = createKeyboard({ target });
  const key = (type, code, more = {}) => {
    const e = new Event(type, { cancelable: true }); Object.assign(e, { code }, more); target.dispatchEvent(e);
  };
  key('keydown', 'KeyC'); key('keydown', 'KeyV'); keyboard.poll(into);
  assert.equal(into.cameraPressed, true); assert.equal(into.rearView, true); assert.equal(into.back, false);
  key('keydown', 'KeyC', { repeat: true }); keyboard.poll(into); assert.equal(into.cameraPressed, false);
  target.tagName = 'INPUT'; key('keyup', 'KeyV', { ctrlKey: true }); keyboard.poll(into);
  assert.equal(into.rearView, false);
  key('keyup', 'KeyC'); key('keydown', 'KeyC'); keyboard.poll(into);
  assert.equal(into.cameraPressed, false, 'typing does not toggle');
  target.tagName = ''; key('keydown', 'KeyC'); key('keydown', 'KeyV'); target.dispatchEvent(new Event('blur'));
  keyboard.poll(into); assert.equal(into.cameraPressed, false); assert.equal(into.rearView, false);
  keyboard.dispose();
});

test('stick clicks use each pad layout, stay separate from items and clear on disconnect', () => {
  for (const [profile, ls, rs, axes] of [['standard', 10, 11, 4], ['xinput', 9, 10, 8], ['dinput', 10, 11, 4]]) {
    const pad = { connected: true, index: 0, mapping: profile === 'standard' ? 'standard' : '', id: profile,
      axes: Array(axes).fill(0), buttons: Array.from({ length: 17 }, () => ({ value: 0 })) };
    let list = [pad];
    const gp = createGamepad({ nav: { getGamepads: () => list }, devices: createDevices(), choices: { [profile]: profile } });
    const map = createActionMap([gp], { camera: true, items: true });
    pad.buttons[ls].value = pad.buttons[rs].value = 1;
    let a = map.sample();
    assert.equal(a.cameraPressed, true, profile); assert.equal(a.rearView, true, profile);
    assert.equal(a.item, false); assert.equal(a.back, false); assert.equal(a.pause, false);
    a = map.sample(); assert.equal(a.cameraPressed, false); assert.equal(a.rearView, true);
    pad.buttons[ls].value = pad.buttons[rs].value = 0; map.sample();
    pad.buttons[rs].value = 1; assert.equal(map.sample().cameraPressed, true);
    list = []; a = map.sample();
    assert.equal(a.cameraPressed, false); assert.equal(a.rearView, false); assert.equal(a.pause, true);
    assert.equal(map.sample().pause, false); map.dispose();
  }
});

class Node {
  constructor() { this.listeners = new Map(); this.children = []; this.style = {}; this.attrs = {}; }
  addEventListener(name, fn) { const list = this.listeners.get(name) || new Set(); list.add(fn); this.listeners.set(name, list); }
  removeEventListener(name, fn) { this.listeners.get(name)?.delete(fn); }
  emit(name, props = {}) { const e = { target: this, cancelable: true, preventDefault() {}, stopPropagation() {}, ...props }; for (const fn of this.listeners.get(name) || []) fn(e); }
  appendChild(n) { n.parentNode = this; this.children.push(n); }
  removeChild(n) { this.children = this.children.filter(c => c !== n); n.parentNode = null; }
  setAttribute(k, v) { this.attrs[k] = v; }
  closest() { return this.className?.includes('gr-touch-btn') ? this : null; }
  getBoundingClientRect() { return { left: 0, top: 0, width: 400, height: 700 }; }
  setPointerCapture() {}
  releasePointerCapture() {}
}

test('touch view and rear do not hop, and rear clears on every release route', () => {
  const root = new Node(), win = new Node(), doc = { createElement: () => new Node() };
  win.location = { search: '?touch=1' }; win.document = doc;
  const touch = createTouch({ root, win, doc }), into = {};
  const view = touch.el.children.find(n => n.className?.includes('gr-touch-view'));
  const rear = touch.el.children.find(n => n.className?.includes('gr-touch-rear'));
  view.emit('pointerdown', { pointerId: 1 }); rear.emit('pointerdown', { pointerId: 2 }); touch.poll(into);
  assert.equal(into.cameraPressed, true); assert.equal(into.rearView, true); assert.equal(into.hop, false);
  assert.equal(into.throttle, 0, 'view controls alone do not start driving');
  touch.poll(into); assert.equal(into.cameraPressed, false); assert.equal(into.rearView, true);
  for (const route of ['pointerup', 'pointercancel', 'blur', 'lostpointercapture', 'touchend']) {
    rear.emit('pointerdown', { pointerId: 2 });
    if (route === 'lostpointercapture') rear.emit(route, { pointerId: 2 });
    else if (route === 'touchend') touch.el.emit(route, { touches: [] });
    else win.emit(route, { pointerId: 2 });
    touch.poll(into); assert.equal(into.rearView, false, route);
  }
  view.emit('click', { detail: 0 }); touch.poll(into); assert.equal(into.cameraPressed, true, 'keyboard activation');
  rear.emit('pointerdown', { pointerId: 2 }); touch.dispose(); touch.poll(into);
  assert.equal(into.rearView, false); assert.equal(root.children.length, 0);
});

test('controls card names camera buttons for all device families', () => {
  assert.deepEqual(controlRows('ps').find(r => r[0] === 'Chase / first person'), ['Chase / first person', 'C', 'R3', 'View button']);
  assert.deepEqual(controlRows('ps').find(r => r[0] === 'Look behind'), ['Look behind', 'Hold V', 'Hold L3', 'Hold Rear button']);
  assert.equal(controlRows('generic', 'xinput').find(r => r[0] === 'Look behind')[2], 'Hold Button 10');
});
