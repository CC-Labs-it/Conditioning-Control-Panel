import test from 'node:test';
import assert from 'node:assert/strict';
import { createGarageNav } from '../hud/garageNav.js';
import { createGamepad } from '../input/gamepad.js';
import { createDevices } from '../input/device.js';

function surface() {
  const doc = { activeElement: null }, selected = [], actions = [], trackEvents = [];
  const button = name => ({ name, disabled: false, hidden: false, getClientRects: () => [1],
    focus() { doc.activeElement = this; }, click() { actions.push(name); } });
  const menu = ['race', 'tracks', 'online', 'customize', 'options', 'controls'].map(button);
  const custom = ['done', 'outfit', 'palette'].map(button);
  const state = { custom: false, visible: true, busy: false, picker: false };
  const root = { ownerDocument: doc, querySelectorAll: selector => selector.includes('g-custom') ? custom : menu };
  const nav = createGarageNav({ root, select: dir => selected.push(dir),
    customize: on => { state.custom = on; }, isCustom: () => state.custom,
    isVisible: () => state.visible, isBusy: () => state.busy,
    trackPicking: () => state.picker, trackNav: dir => trackEvents.push(dir) });
  return { doc, menu, custom, state, nav, selected, actions, trackEvents };
}

test('controller stick and d-pad navigate menu rows, confirm Race and never select a kart vertically', () => {
  const ui = surface(), pad = { connected: true, mapping: 'standard', id: 'Xbox', index: 0,
    axes: [0, 0, 0, 0], buttons: Array.from({ length: 17 }, () => ({ pressed: false, value: 0 })) };
  const source = createGamepad({ nav: { getGamepads: () => [pad] }, devices: createDevices() });
  const poll = () => { source.poll({}); const dir = source.takeNav(); if (dir) ui.nav(dir); };
  try {
    ui.doc.activeElement = { name: 'carousel-dot' };
    pad.axes[1] = -1; poll(); assert.equal(ui.doc.activeElement.name, 'race');
    poll(); assert.equal(ui.doc.activeElement.name, 'race', 'held direction has one edge');
    pad.axes[1] = 0; poll(); pad.buttons[13].pressed = true; poll();
    assert.equal(ui.doc.activeElement.name, 'tracks');
    pad.buttons[13].pressed = false; poll(); pad.buttons[12].pressed = true; poll();
    assert.equal(ui.doc.activeElement.name, 'race');
    pad.buttons[12].pressed = false; poll(); pad.buttons[0].pressed = true; poll();
    assert.deepEqual(ui.actions, ['race']); assert.deepEqual(ui.selected, []);
    pad.buttons[0].pressed = false; poll(); pad.axes[0] = 1; poll();
    assert.deepEqual(ui.selected, [1], 'horizontal input selects the next kart');
  } finally { source.dispose(); }
});

test('navigation excludes unavailable rows, scopes customization, and respects blocked surfaces', () => {
  const ui = surface(); ui.menu[2].hidden = true; ui.menu[3].disabled = true;
  ui.menu[1].focus(); ui.nav('down'); assert.equal(ui.doc.activeElement.name, 'options');
  ui.state.custom = true; ui.nav('down'); assert.equal(ui.doc.activeElement.name, 'done');
  ui.nav('right'); assert.equal(ui.doc.activeElement.name, 'outfit');
  ui.nav('ok'); assert.deepEqual(ui.actions, ['outfit']); assert.deepEqual(ui.selected, []);
  ui.nav('back'); assert.equal(ui.state.custom, false);
  ui.state.visible = false; ui.nav('ok');
  ui.state.visible = true; ui.state.busy = true; ui.nav('ok');
  assert.deepEqual(ui.actions, ['outfit']);
  ui.state.busy = false; ui.state.picker = true; ui.nav('down');
  assert.deepEqual(ui.trackEvents, ['down']);
});
