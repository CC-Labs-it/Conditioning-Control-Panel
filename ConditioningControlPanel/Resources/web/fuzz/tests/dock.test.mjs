import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { DOCK_MIN_W, dockIsRail, emptierEdge, railWidth, slotOrder, clampWindow, windowSize, WINDOW_MIN, createDock } from '../ui/dock.js';

test('the rail width matches the sum in styles.css and leaves the stage its room', () => {
  assert.equal(railWidth(1099), 0);
  assert.equal(railWidth(1100), 200);
  assert.equal(railWidth(1280), 252);
  assert.equal(railWidth(1920), 300);
  for (const w of [1100, 1280, 1440, 1920]) assert.ok(w - 2 * railWidth(w) >= 700, `stage at ${w}`);
  const css = readFileSync(new URL('../styles.css', import.meta.url), 'utf8');
  assert.ok(css.includes('clamp(200px, calc((100vw - 776px) / 2), 300px)'));
  assert.ok(css.includes(`@media (min-width: ${DOCK_MIN_W}px)`));
  assert.ok(dockIsRail(DOCK_MIN_W) && !dockIsRail(DOCK_MIN_W - 1));
});

test('a new slot lands at the end, and a second one from an unlock sits right after its first', () => {
  assert.ok(slotOrder(3) < slotOrder(4));
  assert.ok(slotOrder(3, 1) > slotOrder(3));
  assert.ok(slotOrder(3, 99) < slotOrder(4));
  assert.ok(slotOrder(-1) > slotOrder(500));
});

test('an edge left out goes to the emptier rail', () => {
  assert.equal(emptierEdge(0, 0), 'left');
  assert.equal(emptierEdge(2, 1), 'right');
  assert.equal(emptierEdge(1, 2), 'left');
});

test('no unlock draws straight into a rail any more', () => {
  for (const f of ['basics/side.js', 'basics/trunk.js', 'extras/kit.js', 'themes-a/kit.js', 'themes-b/kit.js', 'themes-b/locked.js', 'routes/kit.js']) {
    const src = readFileSync(new URL(`../ui/unlocks/${f}`, import.meta.url), 'utf8');
    assert.ok(!/layer\('side'/.test(src), `${f} still reaches for a rail`);
  }
});

function harness(W = 1280, H = 800) {
  class Element {
    constructor(tag) {
      this.tagName = tag.toUpperCase(); this.children = []; this.style = {}; this.listeners = {}; this.attributes = {};
      this.classList = { add: (c) => this.classes.add(c), remove: (c) => this.classes.delete(c), contains: (c) => this.classes.has(c),
        toggle: (c, on = !this.classes.has(c)) => { on ? this.classes.add(c) : this.classes.delete(c); return on; } };
      this.classes = new Set();
    }
    set className(v) { this.classes = new Set(v.split(' ')); }
    get childElementCount() { return this.children.length; }
    append(...els) { els.forEach((e) => this.appendChild(e)); }
    appendChild(el) { this.children.push(el); el.parent = this; }
    remove() { this.parent.children = this.parent.children.filter((e) => e !== this); }
    setAttribute(k, v) { this.attributes[k] = v; }
    addEventListener(k, fn) { (this.listeners[k] ||= []).push(fn); }
    closest() { return this.tagName === 'BUTTON' ? this : null; }
    getBoundingClientRect() { return { width: parseFloat(this.style.width) || 240, height: this.classes.has('min') ? 34 : parseFloat(this.style.height) || 150 }; }
    fire(type, extra = {}) { for (const fn of this.listeners[type] || []) fn({ target: this, pointerId: 1, button: 0, clientX: 0, clientY: 0, preventDefault() {}, ...extra }); }
  }
  const frames = [], events = [], listeners = {}, pause = { hidden: true };
  const win = { innerWidth: W, innerHeight: H, requestAnimationFrame: (fn) => frames.push(fn), addEventListener: (n, fn) => { listeners[n] = fn; }, CustomEvent: class { constructor(type, init) { this.type = type; Object.assign(this, init); } } };
  const doc = { defaultView: win, createElement: (t) => new Element(t), getElementById: () => pause, dispatchEvent: (e) => events.push(e) };
  const strip = new Element('div'), rails = { left: new Element('div'), right: new Element('div') };
  const dock = createDock({ doc, strip, rails });
  return { dock, win, pause, events, flush: () => { while (frames.length) frames.shift()(); }, resize: () => listeners.resize() };
}

test('crowded windows overlap without folding, and a body press raises its saved z order', () => {
  const h = harness(1280, 400), saved = [];
  const windows = Array.from({ length: 12 }, (_, i) => h.dock.add({ title: `Window ${i}`, edge: 'left', onLayout: (v) => { saved[i] = v; } }));
  h.flush();
  assert.ok(windows.every((s) => !s.root.classList.contains('min') && !s.root.classList.contains('auto')));
  assert.ok(windows.every((s) => s.y >= 44 && s.y <= 352));
  const oldTop = windows.at(-1).z;
  windows[0].root.fire('pointerdown', { target: windows[0].body });
  assert.ok(saved[0].z > oldTop);
  windows[0].head.fire('click');
  assert.equal(windows[0].min, false, 'the title is a drag handle, not a fold switch');
  windows[0].fold.fire('click');
  assert.equal(saved[0].min, true);
});

test('title dragging persists on release and control buttons cannot start a drag', () => {
  const h = harness(), saves = [];
  const s = h.dock.add({ title: 'Toy', onLayout: (v) => saves.push(v) }); h.flush();
  const old = s.layout();
  s.head.fire('pointerdown', { target: s.fold });
  s.head.fire('pointermove', { clientX: 200, clientY: 100 });
  assert.equal(s.x, old.x);
  s.head.fire('pointerdown', { clientX: 10, clientY: 20 });
  s.head.fire('pointermove', { clientX: 410, clientY: 220 });
  assert.equal(s.x, old.x + 400);
  assert.equal(s.y, old.y + 200);
  s.head.fire('pointerup');
  assert.equal(saves.at(-1).x, s.x);
  assert.ok(h.events.some((e) => e.type === 'fz-dock-move' && e.detail.root === s.root));
  h.pause.hidden = false;
  s.fold.fire('click');
  assert.equal(s.min, false, 'pause blocks window controls');
});

test('saved positions and minimize preference survive reload and resize stays reachable', () => {
  const h = harness(), saved = { x: 980, y: 610, z: 45, min: true };
  const s = h.dock.add({ title: 'Saved', min: saved.min, layout: saved }); h.flush();
  assert.equal(s.x, 980); assert.equal(s.y, 610); assert.equal(s.z, 45); assert.equal(s.min, true);
  h.win.innerWidth = 390; h.win.innerHeight = 420; h.resize(); h.flush();
  assert.ok(s.x >= 0 && s.x + 360 <= 390);
  assert.ok(s.y >= 44 && s.y + 44 <= 420);
  assert.deepEqual(clampWindow({ x: NaN, y: Infinity }, { w: 360, W: 390, H: 420 }), { x: 8, y: 48 });
});

test('phone chips open independent windows and explicit minimize returns to the chip', () => {
  const h = harness(390, 800), a = h.dock.add({ title: 'A' }), b = h.dock.add({ title: 'B' }); h.flush();
  assert.ok(a.min && b.min);
  a.chip.fire('click'); b.chip.fire('click');
  assert.ok(!a.min && !b.min, 'opening B never closes A');
  a.fold.fire('click');
  assert.ok(a.min && !b.min);
  a.chip.fire('click');
  assert.ok(a.z > b.z);
});

test('panel bar raises a covered window before a second press minimizes it', () => {
  const h = harness(), a = h.dock.add({ title: 'A' }), b = h.dock.add({ title: 'B' }); h.flush();
  a.chip.fire('click');
  assert.ok(!a.min && !b.min && a.z > b.z);
  a.chip.fire('click');
  assert.ok(a.min && !b.min);
  a.chip.fire('click');
  assert.ok(!a.min && a.z > b.z);
});

test('a saved size clamps visually on a phone and recovers when the viewport grows', () => {
  const h = harness(), saves = [];
  const s = h.dock.add({ layout: { x: 80, y: 80, w: 600, h: 500 }, onLayout: (v) => saves.push(v) }); h.flush();
  assert.equal(s.root.style.width, '600px'); assert.equal(s.root.style.height, '500px');
  h.win.innerWidth = 390; h.win.innerHeight = 400; h.resize(); h.flush();
  assert.equal(s.root.style.width, '382px'); assert.equal(s.root.style.height, '312px');
  assert.equal(saves.at(-1).w, 600); assert.equal(saves.at(-1).h, 500, 'viewport clamp must not destroy the preference');
  h.win.innerWidth = 1280; h.win.innerHeight = 800; h.resize(); h.flush();
  assert.equal(s.root.style.width, '600px'); assert.equal(s.root.style.height, '500px');
  s.fold.fire('click'); assert.equal(s.root.style.height, '');
  s.fold.fire('click'); assert.equal(s.root.style.height, '500px', 'restore keeps the chosen height');
});

test('corner resizing supports touch, persists on release and respects minimum size', () => {
  const h = harness(), saves = [];
  const s = h.dock.add({ onLayout: (v) => saves.push(v) }); h.flush();
  const handle = s.handles[0], start = s.root.getBoundingClientRect();
  handle.fire('pointerdown', { pointerType: 'touch', clientX: 100, clientY: 100 });
  handle.fire('pointermove', { pointerType: 'touch', clientX: 220, clientY: 180 });
  assert.equal(s.w, start.width + 120); assert.equal(s.h, start.height + 80);
  handle.fire('pointerup'); assert.equal(saves.at(-1).w, s.w); assert.equal(saves.at(-1).h, s.h);
  handle.fire('pointerdown'); handle.fire('pointermove', { clientX: -1000, clientY: -1000 }); handle.fire('pointerup');
  assert.equal(s.w, WINDOW_MIN.w); assert.equal(s.h, WINDOW_MIN.h);
});

test('edge resizing changes one dimension, keyboard sizing and reset retain automatic height', () => {
  const h = harness(), s = h.dock.add({ width: 280 }); h.flush();
  const [corner, right, bottom] = s.handles;
  assert.equal(s.root.style.width, '280px'); assert.equal(s.root.style.height, '');
  right.fire('keydown', { key: 'ArrowRight', shiftKey: true });
  assert.equal(s.w, 312); assert.equal(s.h, null); assert.equal(s.root.style.height, '');
  bottom.fire('keydown', { key: 'ArrowDown' }); assert.equal(s.h, 158); assert.equal(s.w, 312);
  h.pause.hidden = false;
  corner.fire('keydown', { key: 'ArrowRight' }); assert.equal(s.w, 312, 'pause blocks resize');
  h.pause.hidden = true;
  corner.fire('keydown', { key: 'Home' });
  assert.equal(s.w, null); assert.equal(s.h, null); assert.equal(s.root.style.height, ''); assert.equal(s.root.style.width, '280px');
  assert.ok(corner.attributes['aria-label'].includes('Arrow keys'));
});

test('invalid saved sizes fall back and a tiny viewport never forces a giant window', () => {
  assert.deepEqual(windowSize({ w: NaN, h: -1 }, { W: 390, H: 600, y: 48, defaultW: 280 }), { w: 280, h: null });
  assert.deepEqual(windowSize({ w: 1, h: 1 }, { W: 120, H: 140, y: 44, defaultW: 280 }), { w: 112, h: 88 });
});

test('resizing one axis on a small viewport preserves the other desired dimension', () => {
  const h = harness(390, 500), s = h.dock.add({ min: false, layout: { x: 0, y: 48, w: 600, h: 700 } }); h.flush();
  s.handles[0].fire('keydown', { key: 'ArrowLeft' });
  assert.equal(s.w, 374); assert.equal(s.h, 700, 'horizontal keyboard resizing cannot erase the stored tall size');
  s.handles[0].fire('pointerdown');
  s.handles[0].fire('pointermove', { clientY: -30 }); s.handles[0].fire('pointerup');
  assert.equal(s.w, 374, 'a vertical-only drag retains the width');
  assert.equal(s.h, 414);
});

test('compact window controls are separate from the drag grip and body input cannot begin a drag', () => {
  const h = harness(), s = h.dock.add({ title: 'Compact' }); h.flush();
  assert.equal(s.fold.parent, s.root);
  assert.equal(s.head.children.includes(s.fold), false, 'collapse is outside the tiny upper-left grip');
  assert.equal(s.body.parent, s.root);
  s.body.fire('pointerdown'); assert.equal(s.drag, undefined);
  s.handles[0].fire('pointerdown'); assert.equal(s.drag.kind, 'resize');
  s.head.fire('pointermove', { clientX: 200, clientY: 200 });
  assert.equal(s.x, 6, 'a resize gesture never also moves the window');
  s.handles[0].fire('pointercancel');
  assert.equal(s.drag, null);
});
