import { test } from 'node:test';
import assert from 'node:assert/strict';
import { guideButton } from '../ui/unlocks/wave2/meta.js';

function harness(options = {}) {
  const events = {}, cleanups = [], animations = [], vars = new Map();
  let doc, presses = 0;
  class Element {
    constructor() { this.ownerDocument = doc; this.children = []; this.dataset = {}; this.attrs = {}; this.listeners = {}; }
    appendChild(el) { this.children.push(el); el.parentElement = this; return el; }
    insertBefore(el, before) { this.children.splice(this.children.indexOf(before), 0, el); el.parentElement = this; }
    setAttribute(key, value) { this.attrs[key] = value; }
    addEventListener(key, fn) { this.listeners[key] = fn; }
    remove() { this.parentElement.children = this.parentElement.children.filter(el => el !== this); }
    getBoundingClientRect() { return { bottom: 96 }; }
  }
  const nodes = [];
  const make = () => { const el = new Element(); nodes.push(el); return el; };
  doc = {
    createElement: make, createElementNS: make, addEventListener() {},
    getElementById: id => nodes.find(el => el.id === id && el.parentElement),
    documentElement: { style: { setProperty: (key, value) => vars.set(key, value), removeProperty: key => vars.delete(key) } },
    defaultView: { addEventListener() {}, removeEventListener() {} },
  };
  doc.head = make();
  const root = make(), controls = make(); controls.id = 'game-controls'; root.appendChild(controls);
  for (const id of ['account-chip', 'usage-ring', 'dock-lock', 'ctl-panel-btn', 'audio-controls', 'wake']) {
    const el = make(); el.id = id; controls.appendChild(el);
  }
  const ctx = {
    button: { el: make() }, item: { id: 'guide' }, state: { order: ['guide'], ended: false },
    paused: false, restored: false, gentle: false, reduced: false, ...options,
    css() {}, cleanup: fn => cleanups.push(fn), on: (event, fn) => { events[event] = fn; },
    anim: (node, frames, opts) => { animations.push({ node, frames, opts }); },
  };
  const button = guideButton(ctx, () => { presses++; });
  return { ctx, button, controls, events, cleanups, animations, vars, presses: () => presses };
}

test('the Guide unlock adds one persistent map button immediately before audio', () => {
  const h = harness();
  assert.deepEqual(h.controls.children.map(el => el.id), ['account-chip', 'usage-ring', 'dock-lock', 'ctl-panel-btn', 'guide-btn', 'audio-controls', 'wake']);
  assert.equal(h.button.attrs['aria-label'], 'Guide');
  assert.equal(h.button.attrs['aria-haspopup'], 'dialog');
  h.button.listeners.click();
  assert.equal(h.presses(), 1);
  assert.equal(h.animations.length, 1);
  assert.equal(h.vars.get('--game-controls-bottom'), '104px');
  h.cleanups.forEach(fn => fn());
  assert.ok(!h.controls.children.includes(h.button));
  assert.equal(h.vars.has('--game-controls-bottom'), false);
});

test('restoring the Guide does not repeat its unlock nudge', () => {
  assert.equal(harness({ restored: true }).animations.length, 0);
});

test('the Guide reveal only fades under Gentle or reduced motion', () => {
  for (const options of [{ gentle: true }, { reduced: true }]) {
    const { animations } = harness(options);
    assert.equal(animations.length, 1);
    assert.deepEqual(animations[0].frames, [{ opacity: 0 }, { opacity: 1 }]);
  }
});

test('pause, suspend and the ending prevent the fixed Guide door from opening', () => {
  const h = harness();
  for (const event of ['pause', 'suspend']) {
    h.ctx.paused = true; h.events[event](); h.button.listeners.click();
    assert.equal(h.button.disabled, true);
    assert.equal(h.presses(), 0);
    h.ctx.paused = false; h.events.resume();
    assert.equal(h.button.disabled, false);
  }
  h.ctx.state.ended = true; h.events.ending(); h.button.listeners.click();
  assert.equal(h.button.disabled, true);
  assert.equal(h.presses(), 0);
});
