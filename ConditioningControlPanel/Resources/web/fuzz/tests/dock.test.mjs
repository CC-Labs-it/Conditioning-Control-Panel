import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createDock, canMoveFrom, scrollContainer, migrateLayout, clampWindow, freeObjectSpot, extentOf, EDGE_BLEED, HOLD_MS, HOLD_SLOP, TOUCH_HOLD_SLOP, LAYOUT_VERSION, LOCK_COPY } from '../ui/dock.js';

function harness(W = 1280, H = 800, opts = {}) {
  let clock = 0, nextTimer = 1;
  const timers = new Map(), frames = [], events = [], listeners = {}, pause = { hidden: true };
  let doc;
  class Element {
    constructor(tag) {
      this.tagName = tag.toUpperCase(); this.children = []; this.style = {}; this.listeners = {}; this.attributes = {};
      this.ownerDocument = doc; this.classes = new Set(); this.dataset = {}; this.scrollLeft = 0; this.hidden = false;
      this.clientHeight = 100; this.scrollHeight = 100; this.clientWidth = 220; this.scrollWidth = 220;
      this.classList = { add: c => this.classes.add(c), remove: c => this.classes.delete(c), contains: c => this.classes.has(c),
        toggle: (c, on = !this.classes.has(c)) => { on ? this.classes.add(c) : this.classes.delete(c); return on; } };
    }
    set className(v) { this.classes = new Set(v.split(' ')); }
    get childElementCount() { return this.children.length; }
    append(...els) { els.forEach(e => this.appendChild(e)); }
    appendChild(el) { this.children.push(el); el.parentElement = this; return el; }
    insertBefore(el, before) { if (el.parentElement) el.remove(); const i = before ? this.children.indexOf(before) : this.children.length; this.children.splice(i, 0, el); el.parentElement = this; }
    remove() { if (this.parentElement) this.parentElement.children = this.parentElement.children.filter(e => e !== this); }
    setAttribute(k, v) { this.attributes[k] = String(v); }
    hasAttribute(k) { return k in this.attributes; }
    addEventListener(k, fn) { (this.listeners[k] ||= []).push(fn); }
    closest(selector) {
      const matches = el => selector.split(',').some(raw => {
        const part = raw.trim();
        if (part[0] === '[') {
          const m = part.match(/^\[([^=\]]+)(?:="([^"]*)")?\]$/);
          return m && el.hasAttribute(m[1]) && (m[2] == null || el.attributes[m[1]] === m[2]);
        }
        return el.tagName === part.toUpperCase();
      });
      for (let el = this; el; el = el.parentElement) if (matches(el)) return el;
      return null;
    }
    getBoundingClientRect() {
      const isChip = this.classes.has('dk-chip');
      const sorted = this.parentElement?.children.slice().sort((a, b) => +a.style.order - +b.style.order);
      const left = isChip ? sorted.indexOf(this) * 100 - (this.parentElement?.scrollLeft || 0) : parseFloat(this.style.left) || 0;
      const top = isChip || this === strip ? H - 40 - (this.safeInset || 0) : parseFloat(this.style.top) || 0;
      const width = isChip ? 100 : this === strip ? W - 16 : parseFloat(this.style.width) || 220;
      const height = this.classes.has('min') ? 0 : this === strip ? 40 : 120;
      return { left, x: left, top, y: top, width, height, right: left + width, bottom: top + height };
    }
    setPointerCapture(id) { this.capture = id; }
    hasPointerCapture(id) { return this.capture === id; }
    releasePointerCapture() { this.capture = null; }
    focus() { doc.activeElement = this; }
    scrollIntoView() {}
    fire(type, extra = {}) {
      const ev = { target: this, pointerId: 1, pointerType: 'mouse', button: 0, detail: 1, clientX: 0, clientY: 0,
        defaultPrevented: false, stopped: false, preventDefault() { this.defaultPrevented = true; },
        stopPropagation() {}, stopImmediatePropagation() { this.stopped = true; }, ...extra };
      for (const fn of this.listeners[type] || []) { fn(ev); if (ev.stopped) break; }
      return ev;
    }
  }
  const win = { innerWidth: W, innerHeight: H, requestAnimationFrame: fn => frames.push(fn), performance: { now: () => clock },
    setTimeout: (fn, ms) => { const id = nextTimer++; timers.set(id, { fn, at: clock + ms }); return id; }, clearTimeout: id => timers.delete(id),
    addEventListener: (n, fn) => { (listeners[n] ||= []).push(fn); }, getComputedStyle: el => el.style,
    CustomEvent: class { constructor(type, init) { this.type = type; Object.assign(this, init); } } };
  doc = { defaultView: win, createElement: t => new Element(t), getElementById: () => pause, dispatchEvent: e => events.push(e) };
  doc.body = new Element('body'); doc.documentElement = { dataset: {} };
  const strip = new Element('div'), rails = { left: new Element('div'), right: new Element('div') };
  const lock = typeof opts.lock === 'function' ? opts.lock(tag => new Element(tag)) : opts.lock;
  const dock = createDock({ doc, strip, rails, ...(lock ? { lock } : {}) });
  const advance = ms => { clock += ms; for (const [id, t] of timers) if (t.at <= clock) { timers.delete(id); t.fn(); } };
  return { dock, win, doc, strip, pause, events, advance, make: tag => new Element(tag),
    flush: () => { while (frames.length) frames.shift()(); }, event: (name, ev = {}) => { for (const f of listeners[name] || []) f(ev); } };
}

test('a dock that can sit under the tabs can cross the viewport bottom and still be pulled back', () => {
  const h = harness(390, 650);
  const s = h.dock.add({ min: false, allowUnderStrip: true, layout: { x: 30, y: 9999 } });
  h.flush();
  assert.equal(s.root.getBoundingClientRect().bottom, 650 + EDGE_BLEED);
  s.root.fire('pointerdown', { clientY: 570 });
  s.root.fire('pointermove', { clientY: 400 });
  s.root.fire('pointerup');
  assert.equal(s.root.getBoundingClientRect().bottom, 650 + EDGE_BLEED - 170);
  const button = h.make('button'); s.body.appendChild(button);
  s.root.fire('pointerdown', { target: button });
  assert.equal(s.root.fire('click', { target: button }).defaultPrevented, false, 'the next tap can take control immediately');
});

test('legacy object layouts retain visibility and position but lose obsolete empty window dimensions', () => {
  const old = { x: 80, y: 100, w: 700, h: 600, min: true, z: 4 };
  assert.deepEqual(migrateLayout(old), { ...old, w: null, h: null, version: LAYOUT_VERSION });
  assert.equal(migrateLayout({ ...old, version: LAYOUT_VERSION, w: 164 }).w, 164);
  const h = harness(), s = h.dock.add({ width: 164, layout: old }); h.flush();
  assert.equal(s.root.style.width, '164px'); assert.equal(s.root.style.height, '');
  assert.equal(s.min, true); assert.equal(s.x, 80); assert.equal(s.y, 100);
});

test('more than twelve player-positioned objects stay open; each tab always toggles', () => {
  const h = harness(), list = Array.from({ length: 20 }, (_, i) => h.dock.add({ title: `Toy ${i}`, layout: { x: 10 + i * 8, y: 60 + i * 8 } })); h.flush();
  assert.ok(list.every(s => !s.min));
  list[0].chip.fire('click'); assert.equal(list[0].min, false, 'a covered object comes up on the first press');
  assert.ok(list[0].z > list.at(-1).z); assert.ok(list.slice(1).every(s => !s.min));
  list[0].chip.fire('click'); assert.equal(list[0].min, true, 'on top, the next press folds it');
  list[0].chip.fire('click'); assert.equal(list[0].min, false); assert.ok(list[0].z > list.at(-1).z);
});

test('fresh objects fill free side rectangles before arriving tucked, never disturbing restored objects', () => {
  assert.deepEqual(freeObjectSpot({ W: 1000, H: 700, w: 200, h: 200, preferred: 'left' }, [{ x: 8, y: 48, w: 200, h: 500 }]), { x: 792, y: 48 });
  assert.equal(freeObjectSpot({ W: 390, H: 650, w: 280, h: 200, phone: true, top: 480 }), null, 'no phone pile over the shop');
  const h = harness(1280, 400), old = h.dock.add({ title: 'My layout', width: 220, layout: { x: 8, y: 48 } });
  const fresh = Array.from({ length: 15 }, (_, i) => h.dock.add({ title: `Fresh ${i}` })); h.flush();
  assert.equal(old.min, false); assert.equal(old.x, 8); assert.equal(old.y, 48);
  assert.ok(fresh.some(s => s.min));
  const visible = [old, ...fresh].filter(s => !s.min);
  for (const a of visible) for (const b of visible) if (a !== b) {
    assert.ok(a.x + 220 <= b.x || b.x + 220 <= a.x || a.y + 120 <= b.y || b.y + 120 <= a.y, 'fresh placement does not overlap');
  }
});

test('growing toys reserve placement room without adding visible height or shifting restored coordinates', () => {
  const h = harness(1280, 800), quiz = h.dock.add({ title: 'Quiz', edge: 'left', reserveHeight: 280 });
  const next = h.dock.add({ title: 'Next', edge: 'left' }); h.flush();
  assert.equal(quiz.root.style.height, '');
  assert.ok(next.x !== quiz.x || next.y >= quiz.y + 292, 'another object never fills the quiz answer space');
  const restored = h.dock.add({ title: 'Saved', reserveHeight: 280, layout: { x: 300, y: 240 } }); h.flush();
  assert.equal(restored.x, 300); assert.equal(restored.y, 240);
});

test('full side columns use the free center below shop content before a fresh feature is tucked', () => {
  const sides = [{ x: 8, y: 48, w: 230, h: 700 }, { x: 1042, y: 48, w: 230, h: 700 }];
  const room = { W: 1280, H: 800, w: 230, h: 180, centerTop: 430 };
  const slot = freeObjectSpot(room, sides);
  assert.deepEqual(slot, { x: 525, y: 430 });
  const second = freeObjectSpot({ ...room, w: 164 }, [...sides, { ...slot, w: 230, h: 180 }]);
  assert.ok(second && second.y >= 430 && (second.x >= slot.x + 242 || second.x + 176 <= slot.x || second.y >= slot.y + 192));
  assert.equal(freeObjectSpot({ ...room, centerTop: 650 }, sides), null, 'a tall shop still protects its occupied content');
});

test('short press does not move; stationary hold moves and suppresses the release click', () => {
  const h = harness(), saves = [], s = h.dock.add({ title: 'Toy', onLayout: v => saves.push(v) }); h.flush();
  const old = s.layout();
  s.root.fire('pointerdown', { target: s.body, clientX: 20, clientY: 60 }); h.advance(HOLD_MS - 1);
  s.root.fire('pointermove', { clientX: 25, clientY: 64 }); assert.equal(s.x, old.x);
  s.root.fire('pointerup'); assert.equal(s.body.dock.moving, false);
  s.root.fire('pointerdown', { target: s.body, clientX: 20, clientY: 60 }); h.advance(HOLD_MS);
  assert.equal(s.body.dock.moving, true);
  s.root.fire('pointermove', { clientX: 320, clientY: 160 }); s.root.fire('pointerup');
  assert.equal(s.x, old.x + 300); assert.equal(s.y, old.y + 100);
  assert.equal(saves.at(-1).x, s.x); assert.ok(h.events.some(e => e.type === 'fz-dock-move'));
  assert.equal(s.root.fire('click').defaultPrevented, true);
});

test('mouse drag starts immediately, captures before leaving the casing, and persists without a click', () => {
  const h = harness(), saves = [], s = h.dock.add({ min: false, onLayout: v => saves.push(v) }); h.flush();
  const before = s.layout();
  s.root.fire('pointerdown', { target: s.body, pointerType: 'mouse', clientX: 20, clientY: 60 });
  assert.equal(s.root.capture, 1, 'capture starts before the pointer can leave the casing');
  s.root.fire('pointermove', { pointerType: 'mouse', clientX: 140, clientY: 160 });
  assert.equal(s.body.dock.moving, true);
  assert.equal(s.x, before.x + 120); assert.equal(s.y, before.y + 100);
  s.root.fire('pointerup');
  assert.equal(s.root.capture, null); assert.equal(saves.at(-1).x, s.x);
  assert.equal(s.root.fire('click').defaultPrevented, true);
  h.advance(HOLD_MS + 10); assert.equal(s.body.dock.moving, false, 'no delayed hold after release');
});

test('touch motion before the hold cancels movement rather than turning a swipe into an object drag', () => {
  const h = harness(), s = h.dock.add({ min: false }); h.flush(); const before = s.layout();
  s.root.fire('pointerdown', { target: s.body, pointerType: 'touch' });
  s.root.fire('pointermove', { pointerType: 'touch', clientX: 50 }); h.advance(HOLD_MS + 10);
  s.root.fire('pointermove', { pointerType: 'touch', clientX: 100 });
  assert.equal(s.x, before.x); assert.equal(s.body.dock.moving, false);
});

test('toy buttons, canvases, custom gestures and scrollable text keep their own pointer', () => {
  const h = harness(), s = h.dock.add({ min: false }); h.flush();
  for (const tag of ['button', 'input', 'canvas', 'select', 'textarea']) {
    const control = h.make(tag); s.body.appendChild(control);
    s.root.fire('pointerdown', { target: control }); h.advance(HOLD_MS);
    assert.equal(s.gesture, null, tag);
  }
  const custom = h.make('div'), child = h.make('span'); custom.setAttribute('data-dock-interactive', ''); custom.appendChild(child); s.body.appendChild(custom);
  assert.equal(canMoveFrom(child, s.root), false);
  const scroll = h.make('div'); scroll.style.overflowY = 'auto'; scroll.scrollHeight = 300; s.body.appendChild(scroll);
  assert.equal(canMoveFrom(scroll, s.root), false);
  const casing = h.make('div'); casing.setAttribute('data-dock-surface', ''); s.body.appendChild(casing);
  assert.equal(canMoveFrom(casing, s.root), true);
  const cabinet = h.make('canvas'); cabinet.setAttribute('data-dock-surface', ''); s.body.appendChild(cabinet);
  assert.equal(canMoveFrom(cabinet, s.root), true, 'an explicit canvas casing can move');
  s.root.fire('pointerdown', { target: cabinet }); s.body.dock.cancelMove(); h.advance(HOLD_MS);
  assert.equal(s.gesture, null, 'a canvas control cancels the casing gesture before claiming its pointer');
});

test('a label in a scrolling utility leaves quick swipes native but held movement owns the touch', () => {
  const h = harness(), s = h.dock.add({ min: false }); h.flush();
  const scroll = h.make('div'); scroll.setAttribute('data-dock-scroll', ''); s.body.appendChild(scroll);
  const label = h.make('label'); label.setAttribute('data-dock-surface', ''); scroll.appendChild(label);
  assert.equal(scrollContainer(label, s.root), scroll);
  assert.equal(canMoveFrom(label, s.root), true, 'the explicit label remains a move surface');
  s.root.fire('pointerdown', { target: label, pointerType: 'touch' });
  assert.equal(s.root.fire('touchstart', { target: label, touches: [{}] }).defaultPrevented, false);
  s.root.fire('pointermove', { pointerType: 'touch', clientY: 50 });
  assert.equal(s.root.fire('touchmove', { target: label }).defaultPrevented, false);
  s.root.fire('pointerdown', { target: label, pointerType: 'touch' }); h.advance(HOLD_MS);
  assert.equal(s.root.fire('touchmove', { target: label }).defaultPrevented, true);
  assert.equal(s.body.dock.moving, true);
});

test('pause, blur and pointer cancellation clear held gestures and release capture', () => {
  const h = harness(), s = h.dock.add({}); h.flush();
  s.root.fire('pointerdown', { target: s.body }); h.advance(HOLD_MS); assert.equal(s.root.capture, 1);
  h.pause.hidden = false; s.root.fire('pointermove', { clientX: 90 });
  assert.equal(s.gesture, null); assert.equal(s.root.capture, null);
  h.pause.hidden = true; s.root.fire('pointerdown', { target: s.body }); h.advance(HOLD_MS); h.event('blur'); assert.equal(s.gesture, null);
  s.root.fire('pointerdown', { target: s.body }); h.advance(HOLD_MS); s.body.dock.cancelMove(); assert.equal(s.root.capture, null);
});

test('readiness stays visible on a hidden feature and clears without awarding or opening it', () => {
  const h = harness(), s = h.dock.add({ title: 'Slots', min: true }); h.flush();
  s.body.dock.setReady(true, 'Coin available');
  assert.equal(s.dot.hidden, false); assert.equal(s.chip.attributes['data-ready'], 'true');
  assert.equal(s.chip.attributes['aria-label'], 'Slots: Coin available'); assert.equal(s.min, true);
  s.body.dock.setReady(false); assert.equal(s.dot.hidden, true); assert.equal(s.chip.attributes['aria-label'], 'Slots');
});

test('mouse tab drag reorders and persists without toggling, and saved order restores', () => {
  const h = harness(), saves = {};
  const a = h.dock.add({ title: 'A', onLayout: v => { saves.a = v; } }), b = h.dock.add({ title: 'B', onLayout: v => { saves.b = v; } }); h.flush();
  a.chip.fire('pointerdown', { clientX: 50, clientY: 780 });
  a.chip.fire('pointermove', { clientX: 175, clientY: 780 }); a.chip.fire('pointerup'); a.chip.fire('click');
  assert.ok(a.tabOrder > b.tabOrder); assert.equal(a.min, false);
  assert.equal(saves.a.tabOrder, 1); assert.equal(saves.b.tabOrder, 0);
  assert.deepEqual(h.strip.children, [b.chip, a.chip], 'native keyboard tab order follows the visual order after release');
  const restored = harness(), ar = restored.dock.add({ title: 'A', layout: saves.a }), br = restored.dock.add({ title: 'B', layout: saves.b }); restored.flush();
  assert.ok(+ar.chip.style.order > +br.chip.style.order);
});

test('touch tab swipes remain scrolling gestures; held touch reorders', () => {
  const h = harness(390), a = h.dock.add({ title: 'A' }), b = h.dock.add({ title: 'B' }); h.flush();
  a.chip.fire('pointerdown', { pointerType: 'touch', clientX: 50 });
  a.chip.fire('pointermove', { pointerType: 'touch', clientX: 175 }); h.advance(HOLD_MS);
  assert.ok(a.tabOrder < b.tabOrder);
  a.chip.fire('pointerdown', { pointerType: 'touch', clientX: 50 }); h.advance(HOLD_MS);
  a.chip.fire('pointermove', { pointerType: 'touch', clientX: 175 }); a.chip.fire('pointerup');
  assert.ok(a.tabOrder > b.tabOrder); assert.equal(a.min, true);
});

test('keyboard tabs retain reorder, focus and object movement alongside the resize grip', () => {
  const h = harness(), a = h.dock.add({ title: 'A' }), b = h.dock.add({ title: 'B' }); h.flush();
  a.chip.fire('keydown', { key: 'ArrowRight' }); assert.equal(h.doc.activeElement, b.chip);
  a.chip.fire('keydown', { key: 'ArrowRight', altKey: true }); assert.ok(a.tabOrder > b.tabOrder);
  assert.deepEqual(h.strip.children, [b.chip, a.chip]);
  const oldX = a.x; a.chip.fire('keydown', { key: 'ArrowRight', ctrlKey: true, shiftKey: true }); assert.equal(a.x, oldX + 32);
  assert.equal(a.root.children.length, 2, 'feature body plus transient resize grip');
  h.pause.hidden = false; a.chip.fire('keydown', { key: 'ArrowRight', ctrlKey: true }); assert.equal(a.x, oldX + 32);
});

test('clamping keeps compact objects reachable above the strip on a small viewport', () => {
  assert.deepEqual(clampWindow({ x: 999, y: 999 }, { w: 164, h: 180, W: 390, H: 600, bottom: 46 }), { x: 258, y: 406 });
  const h = harness(390, 600), s = h.dock.add({ width: 164, min: false, layout: { x: 200, y: 100 } }); h.flush();
  s.body.dock.setVisible(false); assert.equal(s.body.dock.visible, false);
  s.body.dock.setVisible(true); assert.equal(s.root.style.width, '164px'); assert.equal(s.body.dock.visible, true);
  h.strip.safeInset = 34;
  s.chip.fire('keydown', { key: 'ArrowDown', ctrlKey: true, shiftKey: true });
  for (let i = 0; i < 20; i++) s.chip.fire('keydown', { key: 'ArrowDown', ctrlKey: true, shiftKey: true });
  assert.ok(s.y + 120 <= h.strip.getBoundingClientRect().top - 8 + EDGE_BLEED, 'only the allowed overhang can pass behind the tab row');
});

test('restored toys clamp using intrinsic content height after artwork grows', () => {
  const h = harness(1280, 800), s = h.dock.add({ min: false, layout: { x: 8, y: 610 } });
  h.flush();
  s.body.scrollHeight = 290;
  h.event('resize'); h.flush();
  assert.ok(s.y + 290 <= 752 + EDGE_BLEED, 'a taller toy stays within the allowed edge overhang');
  assert.equal(s.x, 8, 'horizontal placement stays owned by the player');
});

test('corner resize scales content, anchors the opposite edge and restores its saved width', () => {
  const h = harness(), saved = [];
  const s = h.dock.add({ title:'Slot', width:230, layout:{x:400,y:300}, onLayout:v=>saved.push(v) }); h.flush();
  s.handle.fire('pointerdown', {clientX:400,clientY:300});
  s.handle.fire('pointermove', {clientX:350,clientY:275});
  assert.ok(s.w > 230); assert.ok(Number(s.body.style.zoom) > 1);
  assert.ok(Math.abs(s.x + s.w - 630) <= 1, 'bottom-right stays anchored');
  s.handle.fire('pointerup');
  assert.equal(s.gesture, null); assert.equal(s.handle.capture, null);
  assert.equal(saved.at(-1).w,s.w);
  const restored=h.dock.add({width:230,layout:saved.at(-1)});h.flush();
  assert.equal(restored.body.style.zoom,s.body.style.zoom);
});

test('touch resize is immediate, bounded, cancelled on pause and keyboard reset remains available', () => {
  const h=harness(),s=h.dock.add({width:230,layout:{x:400,y:300}});h.flush();
  s.handle.fire('pointerdown',{pointerType:'touch',clientX:400,clientY:300});
  s.handle.fire('pointermove',{pointerType:'touch',clientX:490,clientY:380});
  assert.ok(s.w<230);assert.ok(s.w>=115);
  const resized=s.w;h.pause.hidden=false;
  s.handle.fire('pointermove',{pointerType:'touch',clientX:510,clientY:400});
  assert.equal(s.w,resized);assert.equal(s.gesture,null);
  s.handle.fire('keydown',{key:'Home'});assert.equal(s.w,resized);
  h.pause.hidden=true;s.handle.fire('keydown',{key:'Home'});
  assert.equal(s.w,230);assert.equal(s.body.style.zoom,'1');
});

test('objects can cross the top and side edges slightly but remain recoverable', () => {
  const bounds={w:230,h:230,W:390,H:844,bottom:46};
  assert.deepEqual(clampWindow({x:-999,y:-999},bounds),{x:-32,y:-32});
  assert.deepEqual(clampWindow({x:999,y:999},bounds),{x:192,y:600});
  const h=harness(),s=h.dock.add({width:230,layout:{x:-20,y:-20}});h.flush();
  assert.equal(s.x,-20);assert.equal(s.y,-20,'saved overhang survives reload');
  s.chip.fire('keydown',{key:'ArrowDown',ctrlKey:true,shiftKey:true});
  assert.equal(s.y,12,'keyboard movement brings an edge-tucked feature back');
});

test('a tab a purchase adds stays NEW across reloads until the player meets it, then stays quiet', () => {
  const h = harness(), saves = [];
  const s = h.dock.add({ title: 'Slots', onLayout: v => saves.push(v) }); h.flush();
  assert.equal(s.chip.classes.has('dk-new'), false, 'tabs are never NEW on their own');
  assert.equal('seen' in s.layout(), false, 'older saves carry no seen key');
  assert.equal(h.dock.markNew(s.chip), true);
  assert.equal(s.chip.classes.has('dk-new'), true); assert.equal(s.tag.hidden, false);
  assert.equal(s.dot.hidden, true, 'the red ready dot keeps its own meaning');
  assert.equal(saves.at(-1).seen, false);
  const again = harness(), r = again.dock.add({ title: 'Slots', layout: saves.at(-1) }); again.flush();
  assert.equal(r.chip.classes.has('dk-new'), true, 'a reload before meeting it keeps the nudge');
  r.chip.fire('pointerenter'); again.advance(100); r.chip.fire('pointerleave'); again.advance(1000);
  assert.equal(r.chip.classes.has('dk-new'), true, 'passing over it does not count');
  r.chip.fire('pointerenter'); again.advance(400);
  assert.equal(r.chip.classes.has('dk-new'), false, 'a short hover clears it'); assert.equal(r.tag.hidden, true);
  assert.equal(r.layout().seen, true);
  const later = harness(), q = later.dock.add({ title: 'Slots', layout: r.layout() }); later.flush();
  assert.equal(q.chip.classes.has('dk-new'), false);
  assert.equal(later.dock.markNew(q.chip), false, 'a tab met before is not NEW again in a new run');
});

test('a touch, key or click on a NEW tab clears it at once and still does its normal job', () => {
  for (const [type, extra] of [['pointerdown', { pointerType: 'touch' }], ['keydown', { key: 'Tab' }], ['pointerdown', {}]]) {
    const h = harness(), s = h.dock.add({ title: 'Toy' }); h.flush();
    h.dock.markNew(s.chip);
    s.chip.fire(type, extra);
    assert.equal(s.chip.classes.has('dk-new'), false, type);
    assert.equal(s.layout().seen, true, type);
  }
  const h = harness(), s = h.dock.add({ title: 'Toy' }); h.flush();
  h.dock.markNew(s.chip); const was = s.min;
  s.chip.fire('pointerdown'); s.chip.fire('pointerup'); s.chip.fire('click');
  assert.equal(s.min, !was, 'the first click also shows or hides the object');
});

/* ---- feedback wave 2026-11-09 (lane A) ---- */

test('phone: an object that asks to open opens in free room, otherwise waits as a NEW tab; others stay folded', () => {
  const h = harness(412, 892);
  const plain = h.dock.add({ title: 'Toy' });
  const pic = h.dock.add({ title: 'Picture 1', min: false }); h.flush();
  assert.equal(plain.min, true, 'other objects keep the phone default');
  assert.equal(pic.min, false, 'the picture screen opens in free room');
  assert.equal(pic.chip.classes.has('dk-new'), false, 'an open screen needs no NEW nudge from the dock');
  const tight = harness(412, 260);
  const a = tight.dock.add({ title: 'Picture 1', min: false }), b = tight.dock.add({ title: 'Picture 2', min: false }); tight.flush();
  const folded = [a, b].filter(s => s.min);
  assert.ok(folded.length >= 1, 'no room: at least one waits as a tab');
  for (const s of folded) { assert.equal(s.chip.classes.has('dk-new'), true); assert.equal(s.layout().seen, false); }
  const met = harness(412, 260), m1 = met.dock.add({ title: 'P', min: false }), m2 = met.dock.add({ title: 'Q', min: false, layout: { seen: true } }); met.flush();
  if (m2.min) assert.equal(m2.chip.classes.has('dk-new'), false, 'a tab met before stays quiet');
  assert.ok(m1);
});

test('a tab press on an open object under another one raises it; on top it folds; apart it folds', () => {
  const h = harness(), a = h.dock.add({ title: 'A', layout: { x: 100, y: 100 } }), b = h.dock.add({ title: 'B', layout: { x: 150, y: 140 } });
  const c = h.dock.add({ title: 'C', layout: { x: 800, y: 100 } }); h.flush();
  assert.ok(b.z > a.z);
  a.chip.fire('click'); assert.equal(a.min, false); assert.ok(a.z > b.z, 'raised above the object covering it');
  a.chip.fire('click'); assert.equal(a.min, true, 'now on top, it folds');
  b.chip.fire('click'); assert.equal(b.min, true, 'the top object folds as before');
  c.chip.fire('click'); assert.equal(c.min, true, 'lower z but nothing overlapping: folds');
});

test('layout lock: a HUD button beside the speaker; locked casings never move or resize, tabs still work, it survives reload', () => {
  let stored = false, host;
  const lock = make => {
    host = make('div'); const speaker = make('div'); speaker.id = 'audio-controls'; host.appendChild(speaker);
    return { host, read: () => stored, write(on) { stored = on; } };
  };
  const h = harness(1280, 800, { lock }), s = h.dock.add({ title: 'Toy', layout: { x: 300, y: 200 } }), t = h.dock.add({ title: 'Two' }); h.flush();
  const btn = h.dock.lockButton;
  assert.equal(host.children[0], btn, 'the lock sits just before the sound controls');
  assert.equal(btn.attributes['aria-label'], LOCK_COPY.lock); assert.equal(btn.attributes['aria-pressed'], 'false');
  btn.fire('click');
  assert.equal(h.dock.locked, true); assert.equal(stored, true, 'saved with the layout preferences');
  assert.equal(btn.attributes['aria-label'], LOCK_COPY.unlock); assert.equal(btn.attributes['aria-pressed'], 'true');
  assert.equal(s.root.classes.has('locked'), true); assert.equal(h.doc.documentElement.dataset.dockLocked, '');
  s.root.fire('pointerdown', { target: s.body, pointerType: 'mouse', clientX: 320, clientY: 220 });
  s.root.fire('pointermove', { pointerType: 'mouse', clientX: 520, clientY: 320 }); s.root.fire('pointerup');
  assert.equal(s.x, 300); assert.equal(s.y, 200);
  s.root.fire('pointerdown', { target: s.body, pointerType: 'touch' }); h.advance(HOLD_MS + 10);
  assert.equal(s.body.dock.moving, false, 'no hold to move either');
  const w = s.w; s.handle.fire('pointerdown', { clientX: 300, clientY: 200 }); s.handle.fire('pointermove', { clientX: 200, clientY: 150 });
  assert.equal(s.w, w, 'the corner does nothing'); s.handle.fire('keydown', { key: 'ArrowUp' }); assert.equal(s.w, w);
  s.chip.fire('keydown', { key: 'ArrowRight', ctrlKey: true }); assert.equal(s.x, 300, 'keyboard moves are locked too');
  s.chip.fire('click'); assert.equal(s.min, true, 'tabs still fold'); s.chip.fire('click'); assert.equal(s.min, false);
  s.chip.fire('keydown', { key: 'ArrowRight', altKey: true }); assert.ok(s.tabOrder > t.tabOrder, 'tabs still reorder');
  const again = harness(1280, 800, { lock }); assert.equal(again.dock.locked, true, 'a reload keeps it locked');
  again.dock.lockButton.fire('click'); assert.equal(stored, false);
  const r = again.dock.add({ title: 'Toy', layout: { x: 300, y: 200 } }); again.flush();
  r.root.fire('pointerdown', { target: r.body, pointerType: 'mouse', clientX: 320, clientY: 220 });
  r.root.fire('pointermove', { pointerType: 'mouse', clientX: 420, clientY: 220 }); assert.equal(r.x, 400, 'unlocked moves again');
});

test('the full extent is the union of every drawn part, cut to clipping boxes inside the object', () => {
  const box = (left, top, width, height, style = {}, children = []) => ({ style, children,
    getBoundingClientRect: () => ({ left, top, width, height, right: left + width, bottom: top + height }) });
  const toys = box(20, 230, 240, 90);
  const hidden = box(500, 500, 40, 40, { display: 'none' });
  const scrolled = box(30, 100, 50, 900);
  const list = box(30, 100, 200, 100, { overflowY: 'auto' }, [scrolled]);
  const root = box(10, 50, 260, 170, {}, [box(10, 50, 260, 170, {}, [list, toys, hidden])]);
  const r = extentOf(root, el => el.style);
  assert.deepEqual([r.left, r.top, r.right, r.bottom], [10, 50, 270, 320], 'toys below the box count; a scrolled-away list and a hidden node do not');
});

test('hovering the resize corner with a mouse draws the dashed extent; touch never does', () => {
  const h = harness(), s = h.dock.add({ title: 'Toy Box', width: 230, layout: { x: 400, y: 300 } }); h.flush();
  for (const el of [s.body, s.handle]) el.getBoundingClientRect = () => s.root.getBoundingClientRect();
  s.handle.fire('pointerenter', { pointerType: 'mouse' });
  const box = h.doc.body.children.find(c => c.classes.has('dk-extent'));
  assert.ok(box, 'outline shown'); assert.equal(box.style.left, '397px'); assert.equal(box.style.width, '236px');
  s.handle.fire('pointerleave', { pointerType: 'mouse' });
  assert.equal(h.doc.body.children.some(c => c.classes.has('dk-extent')), false, 'gone on leave');
  s.handle.fire('pointerenter', { pointerType: 'touch' });
  assert.equal(h.doc.body.children.some(c => c.classes.has('dk-extent')), false);
});

test('touch hold slop: a settling fingertip keeps the hold, a real swipe still cancels it; mouse keeps its own threshold', () => {
  assert.equal(HOLD_SLOP, 8); assert.equal(TOUCH_HOLD_SLOP, 14); assert.equal(HOLD_MS, 320, 'the hold is not lengthened');
  const drift = [[0, 0], [3, 2], [6, 4], [8, 6], [9, 7]];   // about 11 px of settle in the first 200 ms
  const h = harness(), s = h.dock.add({ min: false, layout: { x: 300, y: 200 } }); h.flush();
  s.root.fire('pointerdown', { target: s.body, pointerType: 'touch', clientX: 320, clientY: 220 });
  for (const [dx, dy] of drift) { h.advance(40); s.root.fire('pointermove', { pointerType: 'touch', clientX: 320 + dx, clientY: 220 + dy }); }
  h.advance(HOLD_MS); assert.equal(s.body.dock.moving, true, 'the old 8 px slop dropped this press');
  s.root.fire('pointermove', { pointerType: 'touch', clientX: 420, clientY: 220 }); s.root.fire('pointerup');
  assert.equal(s.x, 400);
  s.root.fire('pointerdown', { target: s.body, pointerType: 'touch', clientX: 420, clientY: 220 });
  s.root.fire('pointermove', { pointerType: 'touch', clientX: 440, clientY: 220 }); h.advance(HOLD_MS + 10);
  assert.equal(s.body.dock.moving, false, 'a 20 px swipe before the hold is still not a move');
  s.root.fire('pointerdown', { target: s.body, pointerType: 'mouse', clientX: 420, clientY: 220 });
  s.root.fire('pointermove', { pointerType: 'mouse', clientX: 429, clientY: 220 });
  assert.equal(s.body.dock.moving, true, 'a mouse drag still starts past 8 px'); s.root.fire('pointerup');
});

test('a touch that drags a casing without holding reports a hold miss once per try; a scrolling list does not', () => {
  const h = harness(), s = h.dock.add({ min: false, layout: { x: 300, y: 200 } }); h.flush();
  s.root.fire('pointerdown', { target: s.body, pointerType: 'touch', clientX: 320, clientY: 220 });
  h.advance(80); s.root.fire('pointermove', { pointerType: 'touch', clientX: 360, clientY: 230 });
  const miss = h.events.filter(e => e.type === 'fz-dock-hold-miss');
  assert.equal(miss.length, 1); assert.equal(miss[0].detail.root, s.root); assert.equal(miss[0].detail.after, 80);
  const scroll = h.make('div'); scroll.setAttribute('data-dock-scroll', ''); const label = h.make('label'); label.setAttribute('data-dock-surface', '');
  scroll.appendChild(label); s.body.appendChild(scroll);
  s.root.fire('pointerdown', { target: label, pointerType: 'touch', clientX: 320, clientY: 220 });
  s.root.fire('pointermove', { pointerType: 'touch', clientX: 320, clientY: 280 });
  assert.equal(h.events.filter(e => e.type === 'fz-dock-hold-miss').length, 1, 'a swipe in a list is a scroll');
  s.root.fire('pointerdown', { target: s.body, pointerType: 'mouse', clientX: 320, clientY: 220 });
  s.root.fire('pointermove', { pointerType: 'mouse', clientX: 380, clientY: 220 }); s.root.fire('pointerup');
  assert.equal(h.events.filter(e => e.type === 'fz-dock-hold-miss').length, 1, 'a mouse never misses');
});
