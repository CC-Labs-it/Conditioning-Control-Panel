/* The phone in portrait: one thumb holds the hat across a gap, the same thumb reaches the item (owner 2026-10-30). */
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createTouch, JUMP_LABEL, JUMP_CUE_LABEL } from '../input/touch.js';
import { holdCueAt } from '../input/holdCue.js';
import { controlRows } from '../hud/labels.js';
import { createActionMap } from '../core/actions.js';
import { loadTrack } from '../track/load.js';
import { getKeepLayout } from '../track/keep-layout.js';
import { S, RESPAWN } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { makeFeel, place, recorder, DT } from './sim-fake.mjs';

const WIDE = 390;   // portrait: the split sits at 214 px
class Node {
  constructor() { this.listeners = new Map(); this.children = []; this.style = {}; this.attrs = {}; this.classes = new Set(); this.classList = { add: c => this.classes.add(c), remove: c => this.classes.delete(c) }; }
  addEventListener(name, fn) { const list = this.listeners.get(name) || new Set(); list.add(fn); this.listeners.set(name, list); }
  removeEventListener(name, fn) { this.listeners.get(name)?.delete(fn); }
  emit(name, props = {}) { const e = { target: this, cancelable: true, preventDefault() {}, stopPropagation() {}, ...props }; for (const fn of this.listeners.get(name) || []) fn(e); }
  appendChild(n) { n.parentNode = this; this.children.push(n); }
  removeChild(n) { this.children = this.children.filter(c => c !== n); n.parentNode = null; }
  setAttribute(k, v) { this.attrs[k] = v; }
  closest() { return this.className?.includes('gr-touch-btn') ? this : null; }
  getBoundingClientRect() { return { left: 0, top: 0, width: WIDE, height: 844 }; }
  setPointerCapture() {}
  releasePointerCapture() {}
}
function rig() {
  const root = new Node(), win = new Node(), doc = { createElement: () => new Node() };
  win.location = { search: '?touch=1' }; win.document = doc;
  const touch = createTouch({ root, win, doc });
  const el = touch.el, down = (id, x) => {
    el.emit('touchstart', { changedTouches: [{ identifier: id, clientX: x }] });
    el.emit('pointerdown', { pointerId: id, pointerType: 'touch', clientX: x, clientY: 700 });
  };
  const fingers = new Map();
  const land = (id, x) => { fingers.set(id, x); down(id, x); };
  const slide = (id, x) => { fingers.set(id, x); el.emit('pointermove', { pointerId: id, clientX: x }); };
  const lift = (id) => {
    fingers.delete(id);
    el.emit('touchend', { target: el, changedTouches: [{ identifier: id }], touches: [...fingers].map(([identifier, clientX]) => ({ identifier, clientX, target: el })) });
    win.emit('pointerup', { pointerId: id });
  };
  return { touch, el, land, slide, lift, into: {} };
}

test('a thumb that slides over the split keeps holding when another finger lifts', () => {
  const { touch, land, slide, lift, into } = rig();
  land(1, 80); land(2, 300);
  slide(2, 180);                                   // the hold thumb creeps left of 214
  lift(1);                                         // the steering thumb re-grips: before, this dropped the hat mid gap
  touch.poll(into);
  assert.equal(into.hop, true, 'the hold survives');
  lift(2); touch.poll(into);
  assert.equal(into.hop, false, 'and ends with its own lift');
  touch.dispose();
});

test('a steering thumb past the split keeps steering when the hold lifts', () => {
  const { touch, land, slide, lift, into } = rig();
  land(1, 170); land(2, 320);
  slide(1, 170 + 72);                              // full lock from a centre grip crosses 214 in portrait
  touch.poll(into); assert.equal(into.steer, 1);
  lift(2); touch.poll(into);
  assert.equal(into.steer, 1, 'steering holds');
  assert.equal(into.hop, false);
  touch.dispose();
});

test('a swallowed pointerup still ends the hold through the touchend reconcile', () => {
  const { touch, el, land, into } = rig();
  land(2, 300);
  el.emit('touchend', { target: el, changedTouches: [{ identifier: 2 }], touches: [] });
  touch.poll(into);
  assert.equal(into.hop, false);
  touch.dispose();
});

test('the Jump ring is paint: never a button, lights while held, reads Hold on the cue', () => {
  const { touch, el, land, lift, into } = rig();
  const ring = el.children.find(n => n.className === 'gr-touch-jump');
  assert.ok(ring); assert.equal(ring.closest(), null, 'a press falls through to the hop layer');
  assert.equal(ring.textContent, JUMP_LABEL);
  touch.poll(into); assert.equal(ring.classes.has('on'), false);
  land(2, 320); touch.poll(into); assert.equal(ring.classes.has('on'), true); assert.equal(into.hopPressed, true);
  lift(2); touch.poll(into); assert.equal(ring.classes.has('on'), false);
  touch.setHoldCue(true); assert.equal(ring.textContent, JUMP_CUE_LABEL); assert.equal(ring.classes.has('cue'), true);
  touch.setHoldCue(false); assert.equal(ring.textContent, JUMP_LABEL); assert.equal(ring.classes.has('cue'), false);
  touch.dispose();
});

const keep = loadTrack(JSON.parse(readFileSync(new URL('../tracks/keep.json', import.meta.url), 'utf8')), { moving: true });
const bridge = getKeepLayout(keep).drawbridge;

test('hold cue: the authored launch only, because the words catch every aerial gap', () => {
  const gap = keep.aerials[0];
  assert.equal(holdCueAt(bridge, bridge.rampS0), true);
  assert.equal(holdCueAt(bridge, bridge.rampS0 - 50), true, 'in time to find the ring');
  assert.equal(holdCueAt(bridge, bridge.gapS1 + 1), false, 'off once over the far side');
  assert.equal(holdCueAt(bridge, gap.gapS0 + 10), false, 'a word gap needs no hold');
  assert.equal(holdCueAt(null, 5), false, 'no launch, no cue');
});

test('a thumb holds the hat across a gap: real touch events through the action map into the sim, with a re-grip mid flight', () => {
  const gap = keep.aerials[0], feel = makeFeel();
  const fly = (hold) => {
    const { touch, land, slide, lift } = rig();
    const actions = createActionMap([touch], { items: true, camera: true, feel });
    const st = place(keep, { s: gap.gapS0 - 26, speed: 45 }), ev = recorder();
    let t = 0, tLip = null, stage = 0;
    land(1, 80);                                   // the left thumb on the wheel: auto throttle starts
    for (let i = 0; i < 1000; i++) {
      t += DT;
      if (tLip === null && st[S.s] >= gap.gapS0) tLip = t;
      const rel = tLip === null ? -1 : t - tLip;
      if (hold && stage === 0 && st[S.s] >= gap.gapS0 - 6) { land(2, 320); stage = 1; }
      if (hold && stage === 1 && rel > .5) { slide(2, 180); lift(1); land(1, 90); stage = 2; }
      const a = actions.sample({ nowS: t });
      const was = st[S.airborne];
      step(st, a, keep, feel, DT, ev);
      if (st[S.respawnPhase] !== RESPAWN.NONE) { actions.dispose(); return { fell: true }; }
      if (tLip !== null && was && !st[S.airborne] && st[S.s] > gap.gapS1 - 2) { actions.dispose(); return { fell: false, s: st[S.s] }; }
    }
    actions.dispose();
    return { fell: true, stuck: true };
  };
  assert.equal(fly(false).fell, true, 'with no word run bound, no hold is no crossing');
  const crossed = fly(true);
  assert.equal(crossed.fell, false, 'the hold crosses');
  assert.ok(crossed.s > gap.gapS1, 'and lands on the far side');
});

test('portrait moves the slot, Jump and Drop into the thumb zone, in CSS alone; landscape rules stay put', () => {
  const css = readFileSync(new URL('../styles.css', import.meta.url), 'utf8').replace(/\r\n/g, '\n');
  const at = css.indexOf('@media (orientation: portrait)');
  assert.ok(at > 0);
  const block = css.slice(at, css.indexOf('\n}\n', at));
  assert.match(block, /\.gr-touch-on \.hud-item \{ top: auto; left: auto;/);
  assert.match(block, /\.gr-touch-on \.gr-touch-item \{ display: none; \}/, 'one fire control');
  assert.match(block, /\.gr-touch-jump \{ display: grid;[\s\S]*pointer-events: none;/);
  assert.match(css.slice(0, at), /\.gr-touch-jump \{ display: none; \}/, 'no ring outside portrait');
  assert.match(css.slice(0, at), /\.hud-item \{ position: absolute; top: max\(12px, env\(safe-area-inset-top\)\); left: 50%;/, 'the base slot stays top centre');
  const html = readFileSync(new URL('../index.html', import.meta.url), 'utf8');
  assert.match(html, /#hud \{ z-index: 13; \}/, 'the slot takes taps over the touch layer (z 12)');
  const rows = controlRows();
  assert.ok(rows.some(r => r[0] === 'Hop and drift' && /Jump/.test(r[3])));
  assert.ok(rows.some(r => r[0] === 'Drop it behind' && /Drop/.test(r[3])));
});
