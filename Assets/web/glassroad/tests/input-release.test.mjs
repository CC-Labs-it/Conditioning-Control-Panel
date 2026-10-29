import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createKeyboard } from '../input/keyboard.js';
import { createTouch } from '../input/touch.js';

function emit(target, type, props = {}) {
  const event = new Event(type, { cancelable: true });
  Object.assign(event, props);
  target.dispatchEvent(event);
  return event;
}

test('keyboard releases driving keys even when a modifier is now held', () => {
  for (const modifier of ['ctrlKey', 'altKey', 'metaKey']) {
    const target = new EventTarget(), keyboard = createKeyboard({ target }), into = {};
    try {
      for (const code of ['KeyW', 'KeyD', 'Space']) emit(target, 'keydown', { code });
      keyboard.poll(into);
      for (const code of ['KeyW', 'KeyD', 'Space']) emit(target, 'keyup', { code, [modifier]: true });
      keyboard.poll(into);
      assert.equal(into.throttle, 0, modifier);
      assert.equal(into.steer, 0, modifier);
      assert.equal(into.hop, false, modifier);
      emit(target, 'keydown', { code: 'KeyW', [modifier]: true });
      keyboard.poll(into);
      assert.equal(into.throttle, 0, 'shortcuts do not start driving');
    } finally { keyboard.dispose(); }
  }
});

test('keyboard releases a driving key after focus moves into a field', () => {
  const target = new EventTarget(), keyboard = createKeyboard({ target }), into = {};
  try {
    emit(target, 'keydown', { code: 'KeyW' });
    const field = { tagName: 'INPUT', isContentEditable: false };
    const release = new Event('keyup');
    Object.defineProperties(release, { code: { value: 'KeyW' }, target: { value: field } });
    target.dispatchEvent(release);
    keyboard.poll(into);
    assert.equal(into.throttle, 0);
  } finally { keyboard.dispose(); }
});

class Element extends EventTarget {
  constructor(tagName = 'DIV') { super(); this.tagName = tagName; this.style = {}; this.children = []; }
  appendChild(child) { child.parentNode = this; this.children.push(child); }
  removeChild(child) { this.children = this.children.filter(c => c !== child); child.parentNode = null; }
  setAttribute() {}
  getBoundingClientRect() { return { left: 0, top: 0, width: 1000, height: 600 }; }
  closest() { return this.className?.includes('gr-touch-btn') ? this : this.parentNode?.closest(); }
  setPointerCapture() {}
  releasePointerCapture() {}
}

function touchSetup() {
  const win = new EventTarget(), root = new Element();
  win.location = { search: '?touch=1' };
  const doc = new EventTarget();
  doc.createElement = tag => new Element(tag.toUpperCase());
  const touch = createTouch({ root, win, doc });
  const into = {};
  const pointer = (type, x, pointerId = 1, pointerType = 'touch') => emit(touch.el, type,
    { pointerId, pointerType, clientX: x, clientY: 300 });
  return { touch, win, doc, pointer, into };
}

test('a mouse on a touch laptop cannot latch touch throttle or drift', () => {
  const { touch, pointer, into } = touchSetup();
  try {
    const pause = touch.el.children.find(c => c.className?.includes('gr-touch-pause'));
    emit(pause, 'pointerdown', { pointerType: 'mouse' });
    touch.poll(into);
    assert.equal(into.pause, true, 'mouse can still pause');
    assert.equal(into.throttle, 0, 'pausing does not enable touch throttle');
    pointer('pointerdown', 850, 1, 'mouse');
    touch.poll(into);
    assert.equal(into.throttle, 0);
    assert.equal(into.hop, false);
    assert.equal(into.hopPressed, false);
    pointer('pointerdown', 200, 1, 'mouse');
    pointer('pointermove', 300, 1, 'mouse');
    touch.poll(into);
    assert.equal(into.steer, 0);
    pointer('pointerdown', 850, 2);
    touch.poll(into);
    assert.equal(into.throttle, 1, 'real touch still accelerates');
    assert.equal(into.hopPressed, true);
  } finally { touch.dispose(); }
});

test('losing focus releases touch steering, drift, rear view and pending edges', () => {
  const { touch, win, pointer, into } = touchSetup();
  try {
    pointer('pointerdown', 200);
    pointer('pointermove', 300);
    pointer('pointerdown', 850, 2);
    const rear = touch.el.children.find(c => c.className?.includes('gr-touch-rear'));
    const view = touch.el.children.find(c => c.className?.includes('gr-touch-view'));
    emit(rear, 'pointerdown', { pointerId: 4 });
    emit(view, 'pointerdown', { pointerId: 5 });
    emit(win, 'blur');
    touch.poll(into);
    assert.equal(into.steer, 0);
    assert.equal(into.hop, false);
    assert.equal(into.hopPressed, false);
    assert.equal(into.pause, false);
    assert.equal(into.rearView, false);
    assert.equal(into.cameraPressed, false);
    pointer('pointerdown', 850, 3);
    touch.poll(into);
    assert.equal(into.hopPressed, true, 'a new finger can take over');
    touch.dispose();
    assert.doesNotThrow(() => emit(win, 'blur'));
  } finally { touch.dispose(); }
});

test('hiding the page without a blur releases touch holds', () => {
  const { touch, doc, pointer, into } = touchSetup();
  try {
    pointer('pointerdown', 200);
    pointer('pointermove', 300);
    pointer('pointerdown', 850, 2);
    const rear = touch.el.children.find(c => c.className?.includes('gr-touch-rear'));
    emit(rear, 'pointerdown', { pointerId: 4 });
    doc.hidden = true;
    emit(doc, 'visibilitychange');
    touch.poll(into);
    assert.equal(into.steer, 0);
    assert.equal(into.hop, false);
    assert.equal(into.hopPressed, false);
    assert.equal(into.rearView, false);
  } finally { touch.dispose(); }
});
