// tests/hud-playtest.test.mjs - playtest fixes 2026-10-28 (lane hud): the fire key on a held item and the one-time
// first-pickup tip (hud/labels.js itemKey / itemTip), the opt-in km/h readout (the speedo option, absent = hidden)
// and the Autopilot chip driven by the bus 'autopilot' event.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { createBus } from '../core/events.js';
import { itemKey, itemTip } from '../hud/labels.js';

test('itemKey names the fire control for the device used last', () => {
  assert.equal(itemKey({ last: 'keyboard' }), 'E');
  assert.equal(itemKey({}), 'E');
  assert.equal(itemKey({ last: 'pad', pad: { family: 'xbox' } }), 'X');
  assert.equal(itemKey({ last: 'pad', pad: { family: 'ps' } }), 'Square');
  assert.equal(itemKey({ last: 'touch' }), 'Throw');
});

test('itemTip names both moves, with no dash and no exclamation mark', () => {
  assert.equal(itemTip({ last: 'keyboard' }), 'E throw. Hold S + E drop behind');
  assert.equal(itemTip({ last: 'pad', pad: { family: 'xbox' } }), 'X throw. Stick down + X drop behind');
  assert.match(itemTip({ last: 'touch' }), /Throw/);
  for (const last of ['keyboard', 'pad', 'touch']) assert.doesNotMatch(itemTip({ last }), /[-–—!]/);
});

const moduleUrl = (src) => 'data:text/javascript;base64,' + Buffer.from(src).toString('base64');
const mocks = {
  './cards.js': moduleUrl('export function createCards(){return{get options(){return globalThis.__hudOpts||null;},dispose(){},show(){},refreshDevice(){}};}'),
  './minimap.js': moduleUrl('export const createMinimap=()=>({draw(){},setTrack(){}});'),
  './kit.js': moduleUrl('export const createKit=()=>({dispose(){},group:()=>false,ok:()=>false,url:()=>""});export const placeArt=()=>null;'),
  './itemFeel.js': moduleUrl('export const ITEM_FEEL_MARKUP="";export const createItemFeel=()=>({reset(){},dispose(){},hit(){},hook(){},standings(){}});'),
};
const hooks = registerHooks({ resolve(specifier, context, next) {
  if (context.parentURL?.endsWith('/hud/hud.js') && mocks[specifier]) return { url: mocks[specifier], shortCircuit: true };
  return next(specifier, context);
} });
const { createHud } = await import('../hud/hud.js'); hooks.deregister();

function element() {
  const nodes = new Map(), cls = new Set(), attrs = {};
  return {
    dataset: {}, style: { setProperty() {} }, hidden: false, textContent: '', offsetWidth: 0,
    classList: { add: (...c) => c.forEach((x) => cls.add(x)), remove: (...c) => c.forEach((x) => cls.delete(x)),
      toggle: (c, on) => { if (on === undefined ? !cls.has(c) : on) cls.add(c); else cls.delete(c); }, contains: (c) => cls.has(c) },
    setAttribute: (k, v) => { attrs[k] = v; }, getAttribute: (k) => attrs[k], addEventListener() {}, appendChild() {},
    querySelector(key) { if (!nodes.has(key)) nodes.set(key, element()); return nodes.get(key); },
    querySelectorAll: () => [],
  };
}
function rig({ opts = null, last = 'keyboard' } = {}) {
  globalThis.__hudOpts = opts;
  let t = 1000;
  const root = element(), bus = createBus();
  const feel = { get: (k) => ({ 'gentle.on': false, 'speed.topMps': 30, 'speed.shownTopMps': 30, 'items.rollS': 1 })[k] };
  const hud = createHud({ root, card: element() }, feel, bus,
    { doc: { createElement: element }, now: () => t, devices: { last, pad: null, on: () => () => {} } });
  return { hud, root, bus, q: (s) => root.querySelector(s), tick: (ms) => { t += ms; } };
}

test('the km/h readout hides unless the speedo option is on', () => {
  for (const [opts, shown] of [[null, false], [{ motion: 'full' }, false], [{ speedo: false }, false], [{ speedo: true }, true]]) {
    const r = rig({ opts });
    r.hud.update({ speedPct: 0.5 });
    assert.equal(r.q('.hud-speed').hidden, !shown, JSON.stringify(opts));
    assert.equal(r.q('.hud-speed b').textContent, shown ? '54' : '');
    r.hud.dispose();
  }
});

test('a held item shows its fire key; the first pickup tips once a session', () => {
  const r = rig();
  const key = r.q('.hud-item-key'), toast = r.q('.hud-toast');
  r.hud.update({ item: null });
  assert.equal(key.hidden, true);
  r.hud.update({ item: { id: 'disc' } });
  assert.equal(key.hidden, false); assert.equal(key.textContent, 'E');
  assert.equal(toast.textContent, 'E throw. Hold S + E drop behind');
  assert.equal(toast.dataset.at, 'slot', 'the tip hangs under the slot it explains');
  r.tick(10000); r.bus.emit('slipstream', { racer: 0, state: 'fire' });
  assert.equal(toast.textContent, 'Slipstream');
  assert.equal(toast.dataset.at, undefined, 'race notices sit in the race band, clear of the slot');
  r.bus.emit('item-fire', { racer: 0, mode: 'throw' });
  r.hud.update({ item: null });
  assert.equal(key.hidden, true);
  r.tick(5000); r.hud.update({ item: null });
  toast.textContent = '';
  r.hud.update({ item: { id: 'disc' } });
  assert.equal(key.hidden, false);
  assert.equal(toast.textContent, '', 'the tip plays once');
  r.hud.update({ item: { id: 'disc' }, finished: true });
  assert.equal(key.hidden, true, 'no key after the finish');
  r.hud.dispose();
});

test('the Autopilot chip follows the bus event and leaves on a new race', () => {
  const r = rig({ opts: { motion: 'less' } });
  const chip = r.q('.hud-auto');
  assert.equal(chip.classList.contains('on'), false);
  r.bus.emit('autopilot', { on: true, kind: 'auto' });
  assert.equal(chip.classList.contains('on'), true);
  assert.equal(chip.dataset.motion, 'less');
  assert.equal(chip.getAttribute('aria-hidden'), 'false');
  r.bus.emit('autopilot', { on: false, kind: null });
  assert.equal(chip.classList.contains('on'), false);
  r.bus.emit('autopilot', { on: true, kind: 'cruise' });
  r.hud.setCard('start');
  assert.equal(chip.classList.contains('on'), false);
  r.hud.dispose();
});

test('styles: the chip and the key have IN and OUT, Gentle and Motion keep a fade only', () => {
  const css = readFileSync(new URL('../styles.css', import.meta.url), 'utf8');
  assert.match(css, /\.hud-speed\[hidden\] \{ display: none; \}/);
  assert.match(css, /\.hud-auto \{[^}]*transition: opacity 0\.22s, transform 0\.22s/);
  assert.match(css, /\.hud-auto\[data-motion="off"\], \.hud\.gentle \.hud-auto \{ transform: translate\(-50%, 0\); transition: opacity 0\.45s; \}/);
  assert.match(css, /\.gr-touch-on \.hud-auto \{/);
});
