import { test } from 'node:test';
import assert from 'node:assert/strict';
import { keyIntent, padIntent, stickIntent, swipeIntent } from '../scene/intents.js';

const type = (x) => x && x.type;

test('arrows and WASD move, Q/E switch tabs, Enter/Space select, Escape backs out', () => {
  const cases = { ArrowLeft: 'left', KeyA: 'left', ArrowRight: 'right', KeyD: 'right', ArrowUp: 'up', KeyW: 'up',
    ArrowDown: 'down', KeyS: 'down', KeyQ: 'tabPrev', KeyE: 'tabNext', Enter: 'select', Space: 'select', Escape: 'back' };
  for (const [code, want] of Object.entries(cases)) assert.equal(type(keyIntent({ code })), want, code);
  assert.equal(type(keyIntent({ key: 'e' })), 'tabNext');
  assert.equal(type(keyIntent({ key: ' ' })), 'select');
  assert.equal(keyIntent({ code: 'KeyZ', key: 'z' }), null);
  assert.equal(keyIntent(), null);
});

test('held keys repeat directions only', () => {
  assert.equal(type(keyIntent({ code: 'ArrowRight', repeat: true })), 'right');
  assert.equal(keyIntent({ code: 'Enter', repeat: true }), null);
  assert.equal(keyIntent({ code: 'KeyE', repeat: true }), null);
});

test('standard gamepad: A select, B back, bumpers tabs, d-pad moves', () => {
  assert.deepEqual([0, 1, 4, 5, 12, 13, 14, 15].map((i) => type(padIntent(i))),
    ['select', 'back', 'tabPrev', 'tabNext', 'up', 'down', 'left', 'right']);
  assert.equal(padIntent(9), null);
  assert.equal(stickIntent(0.2, 0.3), null);
  assert.equal(type(stickIntent(-0.9, 0.2)), 'left');
  assert.equal(type(stickIntent(0.1, 0.8)), 'down');
});

test('a swipe drags the shelves; a short move is a tap', () => {
  assert.equal(swipeIntent(10, 5), null);
  assert.equal(type(swipeIntent(-120, 10)), 'right');
  assert.equal(type(swipeIntent(120, -10)), 'left');
  assert.equal(type(swipeIntent(5, -90)), 'down');
  assert.equal(type(swipeIntent(5, 90)), 'up');
});
