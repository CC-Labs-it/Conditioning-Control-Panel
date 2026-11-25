/* The Achievements row's notes (owner 2026-11-25): buying the row used to replay every sticker the run had already
 * earned, one card and one chime each, "about 15 sec of pings". Only a sticker earned NOW gets a card and a sound;
 * what was waiting goes in the book quietly, with at most one silent summary card. The corner card queue shows one at
 * a time and never holds more than three cards. A first-ever sticker in a save tells the host (`achievement`). */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createLife } from '../sim/save.js';
import { earnSticker, noteLife, syncLife } from '../sim/stickers.js';
import { STICKER_BY_ID, STICKER_NOTE_COPY } from '../data/content/stickers.js';
import { NOTE_QUEUE_MAX, NOTE_HOLD_MS, createNoteQueue, waitingCopy, moreCopy } from '../sim/sticker-notes.js';
import trunk from '../ui/unlocks/basics/trunk.js';

/* ---- a fake page, just enough for the Achievements row ---- */
function fakeEl(tag, doc) {
  const e = {
    tag, doc, children: [], parent: null, listeners: {}, dataset: {}, attrs: {}, hidden: false, id: '', type: '', src: '', alt: '', title: '',
    _text: '',
    style: { cssText: '', setProperty() {} },
    classList: { _s: new Set(), add(...c) { c.forEach((x) => this._s.add(x)); }, remove(...c) { c.forEach((x) => this._s.delete(x)); }, contains(c) { return this._s.has(c); }, toggle(c, on) { if (on ?? !this._s.has(c)) this._s.add(c); else this._s.delete(c); } },
    get textContent() { return this._text + this.children.map((c) => c.textContent).join(''); },
    set textContent(v) { this._text = String(v); this.children = []; },
    get ownerDocument() { return doc; },
    get className() { return [...this.classList._s].join(' '); },
    set className(v) { this.classList._s = new Set(String(v).split(' ').filter(Boolean)); },
    appendChild(c) { c.parent?.removeChild?.(c); c.parent = this; this.children.push(c); return c; },
    prepend(c) { c.parent?.removeChild?.(c); c.parent = this; this.children.unshift(c); return c; },
    removeChild(c) { const i = this.children.indexOf(c); if (i >= 0) this.children.splice(i, 1); c.parent = null; },
    remove() { this.parent?.removeChild(this); },
    setAttribute(k, v) { this.attrs[k] = String(v); },
    getAttribute(k) { return this.attrs[k] ?? null; },
    addEventListener(t, f) { (this.listeners[t] ||= []).push(f); },
    click() { for (const f of this.listeners.click || []) f({ isTrusted: true }); },
    getBoundingClientRect: () => ({ left: 10, top: 10, width: 100, height: 20, right: 110, bottom: 30 }),
    querySelector: () => null,
    querySelectorAll: () => [],
  };
  return e;
}
function fakeDoc() {
  const doc = { body: null, head: null, ids: new Map() };
  doc.createElement = (tag) => fakeEl(tag, doc);
  doc.getElementById = (id) => doc.ids.get(id) || null;
  doc.body = fakeEl('body', doc);
  doc.head = fakeEl('head', doc);
  return doc;
}
const all = (el) => [el, ...el.children.flatMap(all)];

/** A ctx for trunk.depth, on a manual clock. `run` = this run's numbers, `life` = the save, `store` = the row's own bag. */
function fakeRow({ run, life, store = {}, restored = false, order = ['depth'], cards = null }) {
  const doc = fakeDoc();
  if (cards) doc.ids.set('cards', cards);
  const layers = {};
  const layer = (n) => (layers[n] ||= doc.body.appendChild(fakeEl(`layer:${n}`, doc)));
  let now = 0;
  const timers = new Set();
  const subs = {};
  const sounds = [], frames = [], anims = [];
  const ctx = {
    item: { id: 'depth' },
    get state() { return { ...run, order }; },
    get life() { return life; },
    restored,
    gentle: false, reduced: false,
    paused: false,
    rng: () => 0.5,
    brand: { text: (s) => s },
    store: { get: (k) => store[k], set: (k, v) => { store[k] = v; } },
    has: (id) => id === 'sound',
    switches: { isOn: () => true },
    audio: { play: (url) => sounds.push(url) },
    host: (type, fields) => frames.push([type, fields]),
    button: { el: fakeEl('button', doc) },
    bounds: () => ({ w: 1280, h: 800 }),
    layer,
    el(tag, css = '', text = null, parent = null) {
      const e = doc.createElement(tag);
      e.style.cssText = css;
      if (text != null) e.textContent = text;
      (typeof parent === 'string' ? layer(parent) : (parent || layer('floaters'))).appendChild(e);
      return e;
    },
    css: () => fakeEl('style', doc),
    anim(el, kf, opts) { const a = { el, kf, opts, finished: Promise.resolve(), cancel() {}, pause() {} }; anims.push(a); return a; },
    every(ms, fn) { const t = { at: now + ms, every: ms, fn }; timers.add(t); return () => timers.delete(t); },
    after(ms, fn) { const t = { at: now + ms, every: 0, fn }; timers.add(t); return () => timers.delete(t); },
    on(ev, fn) { (subs[ev] ||= []).push(fn); },
    emit(ev, d) { for (const f of subs[ev] || []) f(d); },
    mod() {},
    float() {},
    stickers: Object.freeze({
      earn: (id) => earnSticker(life, id),
      note: (key, n = 1) => noteLife(life, key, n),
      sync: () => syncLife(life),
    }),
  };
  const flush = () => new Promise((r) => setImmediate(r));
  /** Moves the clock in 100 ms steps, running timers and letting animation promises settle. */
  async function tick(ms) {
    const end = now + ms;
    while (now < end) {
      now = Math.min(end, now + 100);
      for (const t of [...timers]) if (t.at <= now) { if (t.every) t.at = now + t.every; else timers.delete(t); t.fn(); }
      await flush();
    }
  }
  const notes = () => all(doc.body).filter((e) => e.classList.contains('stk-note'));
  const chimes = () => sounds.filter((u) => /chime/.test(u));
  return { ctx, tick, flush, sounds, chimes, frames, notes, store, doc, anims };
}

/* A run that already earned plenty before buying Achievements: 300 taps, 12 buys, 5,000 Fuzz, 11 minutes, 3 forks. */
const busyRun = () => ({ taps: 300, buys: 12, total: 5000, clock: 11 * 60000, picks: { f1: 'a', f2: 'b', f3: 'c' }, owned: { depth: 1 }, seed: 7 });

test('buying Achievements after a busy start: no card and no sound per old sticker, one quiet summary', async () => {
  const life = createLife();
  const row = fakeRow({ run: busyRun(), life });
  trunk.depth.mount(row.ctx);
  await row.tick(20000);   // under 30 s: thirty idle seconds earn Hands Off, a real sticker
  assert.deepEqual(row.chimes(), [], `the backlog chimed ${row.chimes().length} times`);
  assert.deepEqual(row.frames, [], 'the backlog never tells the host');
  const seen = row.notes();
  assert.ok(seen.length <= 1, `at most one card, saw ${seen.length}`);
  /* They are all on the meter and in the book. */
  const earned = row.store.earned || [];
  for (const id of ['ac_first', 'ac_100', 'ac_buy1', 'ac_buy10', 'ac_cur1k', 'ac_fork', 'ac_ten']) {
    assert.ok(earned.includes(id), `${id} on this run's meter`);
    assert.ok(life.stickers[id], `${id} in the book`);
  }
});

test('the summary card: silent, counted, on the fresh unlock only', async () => {
  const life = createLife();
  const row = fakeRow({ run: busyRun(), life });
  trunk.depth.mount(row.ctx);
  await row.tick(200);
  const [card] = row.notes();
  assert.ok(card, 'one summary card');
  const n = (row.store.earned || []).length;
  assert.match(card.textContent, new RegExp(waitingCopy(n).name.replace(/[+]/g, '.')));
  assert.deepEqual(row.chimes(), []);
  await row.tick(NOTE_HOLD_MS + 2000);
  assert.equal(row.notes().length, 0, 'it leaves on its own');
});

test('a reload or a loaded save notifies nothing that was already true', async () => {
  const life = createLife();
  const first = fakeRow({ run: busyRun(), life });
  trunk.depth.mount(first.ctx);
  await first.tick(10000);
  /* The save reloads with more done offline in the same run: 1,200 taps now. */
  const store = JSON.parse(JSON.stringify(first.store));
  const again = fakeRow({ run: { ...busyRun(), taps: 1200 }, life, store, restored: true, order: ['depth', 'f4'] });
  trunk.depth.mount(again.ctx);
  await again.tick(20000);
  assert.deepEqual(again.chimes(), []);
  assert.deepEqual(again.frames, []);
  assert.equal(again.notes().length, 0, 'no summary on a reload');
  assert.ok(again.store.earned.includes('ac_1k'), 'still earned, quietly');
});

test('a sticker earned NOW: one card, one sound, one host frame the first time in the save', async () => {
  const life = createLife();
  const run = busyRun();
  const row = fakeRow({ run, life });
  trunk.depth.mount(row.ctx);
  await row.tick(NOTE_HOLD_MS + 3000);   // the summary has come and gone
  run.taps = 1000;
  await row.tick(1100);
  const [card] = row.notes();
  assert.ok(card, 'a card');
  assert.match(card.textContent, new RegExp(STICKER_BY_ID.ac_1k.name));
  assert.equal(row.chimes().length, 1);
  assert.deepEqual(row.frames, [['achievement', { id: 'ac_1k' }]]);
  /* Next run, the same sticker again: a card (it fills the meter again), but the host already heard of it. */
  const run2 = { ...busyRun(), taps: 0, buys: 0, total: 0, clock: 0, picks: {} };
  const next = fakeRow({ run: run2, life, order: ['depth'] });
  trunk.depth.mount(next.ctx);
  await next.tick(1000);
  run2.taps = 1000;   // First Press, One Hundred and A Thousand Taps at once: three cards, in turn
  const seen = new Set();
  for (let t = 0; t < 3 * (NOTE_HOLD_MS + 1000); t += 200) { await next.tick(200); for (const c of next.notes()) seen.add(c.textContent); }
  assert.ok([...seen].some((s) => s.includes(STICKER_BY_ID.ac_1k.name)), 'a card again next run');
  assert.equal(next.chimes().length, 3);
  assert.ok(next.store.earned.includes('ac_1k'));
  assert.ok(!next.frames.some(([, f]) => f.id === 'ac_1k'), 'the host heard of ac_1k once per save');
});

test('a burst while playing never stacks a wall: one at a time, at most three cards waiting', async () => {
  const life = createLife();
  const run = { taps: 0, buys: 0, total: 0, clock: 0, picks: {}, owned: { depth: 1 }, seed: 3 };
  const row = fakeRow({ run, life });
  trunk.depth.mount(row.ctx);
  await row.tick(1000);
  assert.equal(row.notes().length, 0, 'nothing was waiting');
  Object.assign(run, { taps: 5000, buys: 100, total: 2e6, clock: 61 * 60000, picks: { f1: 'a', f2: 'b', f3: 'c', f4: 'd', f5: 'sissy' } });
  await row.tick(1000);
  const due = (row.store.earned || []).length;
  assert.ok(due >= 15, `a big burst (${due})`);
  let most = 0, cards = 0, last = null;
  const texts = [];
  for (let t = 0; t < 40000; t += 100) {
    await row.tick(100);
    const shown = row.notes();
    most = Math.max(most, shown.length);
    if (shown[0] && shown[0] !== last) { cards += 1; texts.push(shown[0].textContent); }
    last = shown[0] || null;
  }
  assert.equal(most, 1, 'one card on screen at a time');
  assert.ok(cards <= NOTE_QUEUE_MAX + 1, `${cards} cards for the burst`);
  assert.ok(texts.some((t) => t.includes(moreCopy(2).name.slice(0, 1))), 'a +N more card');
  assert.ok(row.chimes().length <= NOTE_QUEUE_MAX + 1, 'one sound per card at most');
  /* Every first-ever sticker told the host once: this run's meter plus the lifetime ones (the Sissy theme sticker). */
  assert.ok(row.frames.length >= due, `${row.frames.length} frames for ${due} stickers`);
  assert.ok(row.frames.every(([t]) => t === 'achievement'));
  assert.equal(new Set(row.frames.map(([, f]) => f.id)).size, row.frames.length, 'never twice');
});

test('the queue: one card per sticker up to the cap, then one +N more that swallows the rest', () => {
  const q = createNoteQueue();
  q.waiting(4);
  for (const id of ['a', 'b', 'c', 'd', 'e']) q.add({ id });
  const out = [];
  for (let c = q.next(); c; c = q.next()) out.push(c);
  assert.deepEqual(out.map((c) => c.kind), ['waiting', 'one', 'more']);
  assert.equal(out[0].n, 4);
  assert.equal(out[1].sticker.id, 'a');
  assert.deepEqual(out[2].stickers.map((s) => s.id), ['b', 'c', 'd', 'e']);
  const q2 = createNoteQueue();
  for (const id of ['a', 'b', 'c']) q2.add({ id });
  assert.deepEqual([q2.next(), q2.next(), q2.next()].map((c) => c.kind), ['one', 'one', 'one'], 'three fit');
  q2.add({ id: 'x' });
  assert.equal(q2.next().kind, 'one');
  assert.equal(q2.next(), null);
});

test('copy: one place, plain voice, no names in the more card', () => {
  for (const v of [STICKER_NOTE_COPY.kicker, STICKER_NOTE_COPY.kickerMany, STICKER_NOTE_COPY.close, waitingCopy(1).name, waitingCopy(1).line, waitingCopy(7).name, waitingCopy(7).line, moreCopy(3).name, moreCopy(3).line]) {
    assert.equal(typeof v, 'string');
    assert.doesNotMatch(v, /[!–—]/, v);
  }
  assert.match(waitingCopy(7).name, /7/);
  assert.match(moreCopy(3).name, /3/);
});

test('wiring: CONTRACT names the frame, the card sits under every answerable card and never takes clicks', () => {
  const contract = readFileSync(new URL('../CONTRACT.md', import.meta.url), 'utf8');
  assert.ok(contract.includes('| `achievement` | `id` |'), 'CONTRACT.md section 1 lists the frame');
  const view = readFileSync(new URL('../ui/unlocks/basics/sticker-toast.js', import.meta.url), 'utf8');
  assert.match(view, /pointer-events:\s*none/);
  assert.match(view, /\.stk-x[^}]*pointer-events:\s*auto/);
  assert.doesNotMatch(view, /speechSynthesis|SpeechSynthesis/);
  assert.doesNotMatch(view, /\.focus\(/, 'never takes focus');
});
