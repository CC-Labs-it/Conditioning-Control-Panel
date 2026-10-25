import test from 'node:test';
import assert from 'node:assert/strict';
import { createGamepad } from '../input/gamepad.js';
import { createDevices } from '../input/device.js';
import { createActionMap } from '../core/actions.js';

const pad = (index) => ({ index, id: 'pad-' + index, mapping: 'standard', connected: true,
  axes: [0, 0, 0, 0], buttons: Array.from({ length: 17 }, () => ({ value: 0, pressed: false })) });

test('an idle first controller cannot hide input from the second controller', () => {
  const first = pad(0), second = pad(1), devices = createDevices();
  second.buttons[7].value = .8;
  const input = createGamepad({ nav: { getGamepads: () => [first, second] }, devices });
  const map = createActionMap([input]);
  try {
    assert.equal(map.sample().throttle, .8);
    assert.equal(devices.pad.id, second.id);
  } finally { map.dispose(); }
});

test('a newly used controller gets its own button edge and navigation press', () => {
  const first = pad(0), second = pad(1);
  const input = createGamepad({ nav: { getGamepads: () => [first, second] }, devices: createDevices() });
  const map = createActionMap([input]);
  try {
    first.buttons[0].value = 1;
    assert.equal(map.sample().hopPressed, true);
    assert.equal(input.takeNav(), 'ok');
    first.buttons[0].value = 0; second.buttons[0].value = 1;
    assert.equal(map.sample().hopPressed, true, 'the new controller press is not an old held button');
    assert.equal(input.takeNav(), 'ok');
    assert.equal(map.sample().hopPressed, false, 'holding still produces just one edge');
  } finally { map.dispose(); }
});

test('steering above the dead zone marks a controller active and its removal pauses once', () => {
  const controller = pad(0), devices = createDevices();
  let list = [controller]; controller.axes[0] = .3;
  const input = createGamepad({ nav: { getGamepads: () => list }, devices });
  const map = createActionMap([input]);
  try {
    assert.ok(map.sample().steer > 0);
    assert.equal(devices.last, 'pad');
    list = [];
    assert.equal(map.sample().pause, true);
    assert.equal(map.sample().pause, false);
  } finally { map.dispose(); }
});
