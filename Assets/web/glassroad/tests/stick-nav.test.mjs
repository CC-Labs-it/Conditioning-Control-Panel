import test from 'node:test';
import assert from 'node:assert/strict';
import { createStickNav, NAV_DEADZONE, NAV_DELAY_MS, NAV_REPEAT_MS } from '../input/stickNav.js';
import { createGamepad } from '../input/gamepad.js';
import { createDevices } from '../input/device.js';

const rest = { up: false, down: false, left: false, right: false };

test('stick nav: dead zone, one step on the crossing, then a held repeat', () => {
  const n = createStickNav();
  assert.equal(NAV_DEADZONE, 0.5);
  assert.equal(n.step(rest, 0, 0.45, 0), null, 'inside the dead zone');
  assert.equal(n.step(rest, 0, 0.55, 10), 'down', 'the crossing steps at once');
  assert.equal(n.step(rest, 0, 0.9, 20), null);
  assert.equal(n.step(rest, 0, 0.9, 10 + NAV_DELAY_MS - 1), null, 'no repeat before the delay');
  assert.equal(n.step(rest, 0, 0.9, 10 + NAV_DELAY_MS), 'down', 'first repeat after the delay');
  assert.equal(n.step(rest, 0, 0.9, 10 + NAV_DELAY_MS + NAV_REPEAT_MS - 1), null);
  assert.equal(n.step(rest, 0, 0.9, 10 + NAV_DELAY_MS + NAV_REPEAT_MS), 'down', 'then every repeat interval');
  assert.equal(n.step(rest, 0, 0, 900), null, 'release');
  assert.equal(n.step(rest, 0, -0.8, 910), 'up', 'a fresh push steps at once');
});

test('stick nav: a hover at the line or a diagonal wobble never skips', () => {
  const n = createStickNav();
  let steps = 0;
  for (const [x, y, t] of [[0, 0.52, 0], [0, 0.48, 16], [0, 0.51, 32], [0, 0.45, 48]]) if (n.step(rest, x, y, t)) steps++;
  assert.equal(steps, 1, 'chatter around 0.5 is one step');
  const d = createStickNav(), seen = [];
  for (const [x, y, t] of [[-0.3, 0.7, 0], [-0.62, 0.58, 16], [-0.7, 0.5, 32], [-0.4, 0.7, 48]]) { const s = d.step(rest, x, y, t); if (s) seen.push(s); }
  assert.deepEqual(seen, ['down'], 'the diagonal keeps the direction it started with');
});

test('stick nav: the d-pad takes the same rule and wins over the stick', () => {
  const n = createStickNav();
  assert.equal(n.step({ ...rest, left: true }, 0, 0.9, 0), 'left');
  assert.equal(n.step({ ...rest, left: true }, 0, 0.9, 100), null);
  assert.equal(n.step({ ...rest, left: true }, 0, 0.9, NAV_DELAY_MS), 'left', 'a held d-pad repeats too');
});

test('stick nav: block() makes a held direction wait for a release', () => {
  const n = createStickNav();
  n.block();
  assert.equal(n.step(rest, -0.9, 0, 0), null);
  assert.equal(n.step(rest, -0.9, 0, 2000), null, 'never repeats while blocked');
  assert.equal(n.step(rest, 0, 0, 2016), null);
  assert.equal(n.step(rest, -0.9, 0, 2032), 'left', 'released, the next push counts');
});

function rig() {
  let t = 0;
  const pad = { connected: true, mapping: 'standard', id: 'Xbox', index: 0, axes: [0, 0, 0, 0],
    buttons: Array.from({ length: 17 }, () => ({ pressed: false, value: 0 })) };
  const gp = createGamepad({ nav: { getGamepads: () => [pad] }, devices: createDevices(), choices: {}, now: () => t });
  const frame = (ms, menuOpen) => { t += ms; const into = {}; gp.poll(into); return { into, nav: gp.takeNav(menuOpen) }; };
  return { pad, frame };
}

test('gamepad: the left stick walks a card with repeat; A and B are one edge per press', () => {
  const { pad, frame } = rig();
  frame(16, true);
  pad.axes[1] = 0.8;
  const got = [];
  for (let i = 0; i < 40; i++) { const { nav } = frame(16, true); if (nav) got.push(nav); }   // 640 ms held
  assert.ok(got.length >= 3 && got.every((d) => d === 'down'), 'step, then repeats: ' + got.join(','));
  pad.axes[1] = 0; frame(16, true);
  pad.buttons[0].pressed = true;
  assert.equal(frame(16, true).nav, 'ok');
  assert.equal(frame(16, true).nav, null, 'A held is one press');
  pad.buttons[0].pressed = false; pad.buttons[1].pressed = true;
  assert.equal(frame(16, true).nav, 'back');
});

test('gamepad: driving never walks a card and a card stick never steers', () => {
  const { pad, frame } = rig();
  pad.axes[0] = -0.9;
  let r = frame(16, false);
  assert.equal(r.nav, null, 'no card: no nav');
  assert.ok(r.into.steer < -0.5, 'steering while racing');
  r = frame(16, true);                                      // the pause card opens with the stick still held
  for (let i = 0; i < 40; i++) assert.equal(frame(16, true).nav, null, 'a held steer never walks the card');
  pad.axes[0] = 0; frame(16, true);
  pad.axes[0] = -0.9;
  assert.equal(frame(16, true).nav, 'left', 'a fresh push on the card counts');
  r = frame(16, false);                                     // the card closes, stick still held from the card
  assert.equal(r.into.steer, 0, 'the card stick steers nothing until it rests');
  pad.axes[0] = 0; frame(16, false);
  pad.axes[0] = -0.9;
  assert.ok(frame(16, false).into.steer < -0.5, 'back to rest, it steers again');
});
