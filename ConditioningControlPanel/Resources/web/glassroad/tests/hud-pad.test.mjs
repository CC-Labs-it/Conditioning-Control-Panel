// tests/hud-pad.test.mjs - polish 1 (HUDPAD): pad layouts and fallback, device detection, button
// label sets, the device-aware pause hint, and the gamepad's RB item, disconnect pause and notes.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { existsSync } from 'node:fs';
import { padFamily, guessProfile, profileFor, loadChoices, saveChoice, readPad, hatDir, nextProfile, PADMAP_KEY } from '../input/padmap.js';
import { createDevices } from '../input/device.js';
import { createGamepad } from '../input/gamepad.js';
import { createActionMap } from '../core/actions.js';
import { padLabels, controlRows, pauseHint, deviceView } from '../hud/labels.js';
import { placeArt, KIT_GROUPS } from '../hud/kit.js';

const buttons = (n) => Array.from({ length: n }, () => ({ value: 0, pressed: false }));
const mkPad = (o = {}) => ({ connected: true, mapping: '', id: 'USB Gamepad (Vendor: 0079 Product: 0006)', axes: [0, 0, 0, 0], buttons: buttons(12), ...o });
const memStore = () => { const m = new Map(); return { getItem: (k) => (m.has(k) ? m.get(k) : null), setItem: (k, v) => m.set(k, String(v)) }; };

test('device detection: PlayStation by id, Xbox by id or standard mapping, else generic', () => {
  for (const id of ['Wireless Controller (STANDARD GAMEPAD Vendor: 054c Product: 09cc)', 'DualSense Wireless Controller', 'Sony DUALSHOCK 4', '054c-0ce6-Wireless Controller'])
    assert.equal(padFamily({ id, mapping: '' }), 'ps', id);
  assert.equal(padFamily({ id: 'Xbox 360 Controller (XInput STANDARD GAMEPAD)', mapping: 'standard' }), 'xbox');
  assert.equal(padFamily({ id: '045e-02ea-Microsoft X-Box One S pad', mapping: '' }), 'xbox');
  assert.equal(padFamily({ id: 'Some Pad (STANDARD GAMEPAD Vendor: 1234)', mapping: 'standard' }), 'xbox');
  assert.equal(padFamily({ id: 'USB Gamepad', mapping: '' }), 'generic');
  assert.equal(padFamily(null), 'generic');
});

test('fallback layout: standard stays standard, Xbox-style untagged pads read xinput, other HID pads dinput', () => {
  assert.equal(guessProfile({ mapping: 'standard', id: 'x' }), 'standard');
  assert.equal(guessProfile({ mapping: '', id: '045e-028e-Microsoft X-Box 360 pad', axes: Array(8).fill(0), buttons: buttons(11) }), 'xinput');
  assert.equal(guessProfile({ mapping: '', id: 'mystery', axes: Array(8).fill(0), buttons: buttons(11) }), 'xinput', 'the xpad shape');
  assert.equal(guessProfile(mkPad({ axes: Array(10).fill(0), buttons: buttons(16) })), 'dinput');
  assert.equal(guessProfile(mkPad({ axes: [0, 0, 0, 0], buttons: buttons(17) })), 'standard', 'standard shaped but untagged');
  assert.equal(nextProfile('standard'), 'xinput'); assert.equal(nextProfile('dinput'), 'standard');
});

test('layout choice is kept per pad id, bad records and throwing storage read as the guess', () => {
  const s = memStore(), pad = mkPad();
  assert.equal(profileFor(pad, loadChoices(s)), 'dinput');
  assert.equal(saveChoice(pad.id, 'xinput', s), true);
  assert.equal(profileFor(pad, loadChoices(s)), 'xinput');
  assert.equal(profileFor(mkPad({ id: 'another pad' }), loadChoices(s)), 'dinput', 'only that id');
  assert.equal(profileFor({ ...pad, mapping: 'standard' }, loadChoices(s)), 'standard', 'a standard pad never remaps');
  s.setItem(PADMAP_KEY, '{"x":"nonsense","y":"dinput"}');
  assert.deepEqual(loadChoices(s), { y: 'dinput' });
  s.setItem(PADMAP_KEY, 'not json');
  assert.deepEqual(loadChoices(s), {});
  const bad = { getItem() { throw new Error('blocked'); }, setItem() { throw new Error('blocked'); } };
  assert.deepEqual(loadChoices(bad), {});
  assert.equal(saveChoice('a', 'xinput', bad), false);
  assert.equal(saveChoice('a', 'xinput', null), false);
});

test('hat axis: steps of 2/7 from up, rest and an idle 0 read as no direction', () => {
  assert.equal(hatDir(-1), 0); assert.equal(hatDir(-3 / 7), 2); assert.equal(hatDir(1 / 7), 4); assert.equal(hatDir(5 / 7), 6); assert.equal(hatDir(1), 7);
  assert.equal(hatDir(1.2857), -1); assert.equal(hatDir(0), -1); assert.equal(hatDir(NaN), -1);
});

test('readPad: dinput faces and hat, xinput axis triggers wait until first moved, axes never inverted', () => {
  const d = mkPad({ axes: [-0.7, 0, 0, 0, 0, 0, 0, 0, 0, 1.2857], buttons: buttons(12) });
  d.buttons[1].value = 1; d.buttons[9].pressed = true;
  let r = readPad(d, 'dinput');
  assert.equal(r.south, true, 'button 1 is south on a HID pad'); assert.equal(r.west, false);
  assert.equal(r.start, true, 'pressed without a value still counts'); assert.equal(r.x, -0.7);
  assert.equal(r.up || r.down || r.left || r.right, false, 'hat at rest');
  d.axes[9] = -3 / 7; r = readPad(d, 'dinput'); assert.equal(r.right, true); assert.equal(r.up, false);
  d.axes[9] = -1; r = readPad(d, 'dinput'); assert.equal(r.up, true);

  const x = mkPad({ axes: [0, 0.8, 0, 0, 0, 0, 0, 0], buttons: buttons(11) }), seen = {};
  r = readPad(x, 'xinput', seen);
  assert.equal(r.lt, 0); assert.equal(r.rt, 0, 'an untouched axis trigger reading 0 is not half throttle');
  assert.equal(r.y, 0.8);
  x.axes[5] = 1; r = readPad(x, 'xinput', seen); assert.equal(r.rt, 1);
  x.axes[5] = -1; r = readPad(x, 'xinput', seen); assert.equal(r.rt, 0);
  x.axes[5] = 0; r = readPad(x, 'xinput', seen); assert.equal(r.rt, 0.5, 'once seen, the axis is analog');
  x.axes[6] = -1; x.axes[7] = 1; r = readPad(x, 'xinput', seen); assert.equal(r.left, true); assert.equal(r.down, true);
  assert.equal(readPad(mkPad({ mapping: 'standard', buttons: buttons(17) }), 'unknown-name').south, false, 'an unknown layout reads standard');
});

test('label sets: Xbox and PlayStation names, generic button numbers through the layout', () => {
  assert.deepEqual([padLabels('xbox').south, padLabels('xbox').rb, padLabels('xbox').start], ['A', 'RB', 'Start']);
  const ps = padLabels('ps');
  assert.deepEqual([ps.south, ps.east, ps.west, ps.north, ps.rb, ps.start], ['Cross', 'Circle', 'Square', 'Triangle', 'R1', 'Options']);
  const g = padLabels('generic', 'dinput');
  assert.deepEqual([g.south, g.west, g.rb, g.start, g.name], ['Button 2', 'Button 1', 'Button 6', 'Button 10', 'Pad']);
  assert.equal(padLabels('generic', 'xinput').rt, 'Axis 6');
  assert.equal(padLabels('generic').start, 'Button 10');
  const rows = controlRows('ps');
  assert.equal(rows.find((r) => r[0] === 'Use item')[2], 'Square or R1');
  assert.equal(rows.find((r) => r[0] === 'Pause')[2], 'Options');
  assert.equal(rows.find((r) => r[0] === 'Pause')[1], 'Esc');
  assert.equal(controlRows('xbox').find((r) => r[0] === 'Hop and drift')[2], 'A, hold');
  for (const r of [...controlRows('xbox'), ...controlRows('generic', 'dinput')]) for (const c of r) assert.ok(![...c].some((ch) => ch === '!' || ch.charCodeAt(0) === 0x2013 || ch.charCodeAt(0) === 0x2014), c);
});

test('pause hint and device view follow the device used last', () => {
  assert.equal(pauseHint({ last: 'keyboard' }), 'Arrows move, Enter picks');
  assert.equal(pauseHint({ last: 'touch' }), '');
  assert.equal(pauseHint({ last: 'pad', pad: { family: 'xbox', profile: 'standard' } }), 'A picks, Start resumes');
  assert.equal(pauseHint({ last: 'pad', pad: { family: 'ps', profile: 'standard' } }), 'Cross picks, Options resumes');
  assert.equal(pauseHint({ last: 'pad', pad: { family: 'generic', profile: 'dinput' } }), 'Button 2 picks, Button 10 resumes');
  assert.deepEqual(deviceView({}), { last: 'keyboard', family: 'xbox', profile: 'standard', padName: 'Xbox' });
  assert.equal(deviceView({ last: 'pad', pad: { family: 'ps' } }).padName, 'PlayStation');
});

test('devices: use() tells listeners only on a change; lost drops back to keys when the pad was in use', () => {
  const saved = [];
  const dv = createDevices({ save: (id, p) => saved.push([id, p]) }), seen = [];
  dv.on((type, info) => seen.push(type + ':' + (info.kind || info.id)));
  dv.use('keyboard');
  dv.use('pad', { id: 'p1', family: 'ps', profile: 'standard', standard: true });
  dv.use('pad', { id: 'p1', family: 'ps', profile: 'standard', standard: true });
  assert.deepEqual(seen, ['use:pad']);
  dv.lost({ id: 'p1', active: true });
  assert.equal(dv.last, 'keyboard'); assert.equal(dv.pad.family, 'ps', 'the pad stays known for the labels');
  dv.setLayout('p1', 'dinput');
  assert.deepEqual(saved, [['p1', 'dinput']]); assert.equal(dv.pad.profile, 'dinput');
});

test('gamepad: a HID pad hops on its south button, RB is a second item button, LB does nothing', () => {
  const dv = createDevices({ save: () => true }), pad = mkPad({ index: 0 });
  const gp = createGamepad({ nav: { getGamepads: () => [pad] }, devices: dv, choices: {} }), into = {};
  pad.buttons[1].value = 1;
  gp.poll(into);
  assert.equal(into.hopPressed, true); assert.equal(dv.last, 'pad'); assert.equal(dv.pad.family, 'generic'); assert.equal(dv.pad.profile, 'dinput');
  pad.buttons[1].value = 0; pad.buttons[5].value = 1;
  gp.poll(into); assert.equal(into.item, true, 'RB uses the item');
  gp.poll(into); assert.equal(into.item, false, 'held RB is one edge');
  pad.buttons[5].value = 0; pad.buttons[4].value = 1;
  gp.poll(into); assert.equal(into.item, false); assert.equal(into.pause, false); assert.equal(into.hop, false);
  const std = { connected: true, mapping: 'standard', id: 'Xbox pad', axes: [0, 0], buttons: buttons(17) };
  const gp2 = createGamepad({ nav: { getGamepads: () => [std] }, devices: dv, choices: {} });
  std.buttons[5].value = 1; gp2.poll(into); assert.equal(into.item, true, 'RB on a standard pad');
});

test('gamepad: the pad in use leaving mid-race is one pause edge through the action map, never a Start edge', () => {
  const dv = createDevices({ save: () => true }), notes = [];
  dv.on((type, info) => { if (type !== 'use') notes.push(type + (info.active ? ':active' : '')); });
  const pad = { connected: true, mapping: 'standard', id: 'Xbox pad', index: 0, axes: [0, 0], buttons: buttons(17) };
  const idle = { connected: true, mapping: 'standard', id: 'Other pad', index: 1, axes: [0, 0], buttons: buttons(17) };
  let list = [pad, idle];
  const gp = createGamepad({ nav: { getGamepads: () => list }, devices: dv, choices: {} });
  let padStart = false;
  const map = createActionMap([{ poll(into) { gp.poll(into); padStart = !!into.pause; }, dispose() {} }], { items: true });
  pad.buttons[7].value = 1;                       // RT: the player is driving with this pad
  assert.equal(map.sample().pause, false);
  assert.deepEqual(notes, ['connect', 'connect']);
  list = [pad];                                   // the idle pad leaves: no pause
  assert.equal(map.sample().pause, false);
  list = [];                                      // the pad in use leaves
  const a = map.sample();
  assert.equal(a.pause, true); assert.equal(padStart, false, 'not a Start press, so a pause card never resumes on it');
  assert.equal('padLost' in a, false, 'padLost is never copied out');
  assert.equal(map.sample().pause, false, 'one edge');
  assert.deepEqual(notes, ['connect', 'connect', 'lost', 'lost:active']);
  assert.equal(dv.last, 'keyboard');
});

test('kit: place numerals 1 to 8 (first in pink), the groups name only files the kit ships', () => {
  assert.equal(placeArt(1), 'place-1-pink'); assert.equal(placeArt(5), 'place-5-white');
  assert.equal(placeArt(9), null); assert.equal(placeArt(0), null);
  assert.equal(KIT_GROUPS.place.length, 16);
  const names = Object.values(KIT_GROUPS).flat();
  assert.equal(names.length, 26);
  for (const n of names) assert.ok(existsSync(new URL('../art/ui/' + n + '.svg', import.meta.url)), n);
});
