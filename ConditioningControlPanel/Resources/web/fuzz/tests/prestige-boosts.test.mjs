/* Boost lanes (ui/unlocks/prestige/boosts.js): Pay, Idle Perks, Active Play, Lucky Breaks. Pure helpers first, then the
 * entries through a stub ctx (ctx.prestige comes from the Core lane; stubbed here) on a fake game clock. */
import test from 'node:test';
import assert from 'node:assert/strict';
import lanes, {
  payMult, idleMult, awayMult, awayCapMs, tapMult, prizeMult, peekRate, boxHoldMs, critMult, goldGapMult, goldStay,
  frenzyMult, autopilotShare, comboBonus, countVerdict, cheapestAffordable, skipNothing, wheelDraw, wheelMultAt,
  JACKPOT_KEY, LET_GO_MS, COUNT_FROM, COUNT_BEAT_MS,
} from '../ui/unlocks/prestige/boosts.js';
import { LANES, STAR_BY_ID } from '../data/prestige.js';
import { SECTORS } from '../sim/prize-wheel.js';
import { CONFIG } from '../data/config.js';

const P = (stars = [], dust = 0) => { const s = new Set(stars); return { depth: 10, dust, has: (id) => s.has(id), level: () => 0, next: { rules: [], words: [], keep: [] } }; };
const lane = (id, upto = 10) => Array.from({ length: upto }, (_, k) => `${id}_${k + 1}`);
const near = (a, b) => assert.ok(Math.abs(a - b) < 1e-9, `${a} != ${b}`);

test('every star the helpers read exists in data/prestige.js', () => {
  for (const id of [...lane('pay'), ...lane('idle'), ...lane('active'), ...lane('luck')]) assert.ok(STAR_BY_ID[id], id);
  assert.deepEqual(Object.keys(lanes).sort(), ['active', 'idle', 'luck', 'pay']);
  for (const l of ['pay', 'idle', 'active', 'luck']) assert.ok(LANES.some((x) => x.id === l));
});

test('no stars read as plain numbers', () => {
  for (const p of [null, undefined, {}, P()]) {
    assert.equal(payMult(p), 1); assert.equal(idleMult(p), 1); assert.equal(awayMult(p), 1); assert.equal(tapMult(p), 1);
    assert.equal(prizeMult(p), 1); assert.equal(peekRate(p), 1); assert.equal(boxHoldMs(p), 0); assert.equal(critMult(p), 1);
    assert.equal(goldGapMult(p), 1); assert.equal(goldStay(p), 1); assert.equal(frenzyMult(p), 1); assert.equal(autopilotShare(p), 0);
    assert.equal(awayCapMs(p), CONFIG.AWAY_CAP_MS);
  }
});

test('Pay: x1.25, the +% stars add, Overflow doubles; Compound and Interest are capped', () => {
  near(payMult(P(['pay_1'])), 1.25);
  near(payMult(P(['pay_1', 'pay_2', 'pay_3'])), 1.25 * 1.2);
  near(payMult(P(lane('pay'), 0)), 1.25 * 1.9 * 2);
  near(payMult(P(['pay_4'], 40)), 1.2);
  near(payMult(P(['pay_4'], 5000)), 1.5);
  near(payMult(P(['pay_7']), 10 * 60000), 1.1);
  near(payMult(P(['pay_7']), 3 * 3600e3), 1.3);
  near(payMult(P(lane('pay'), 1e6), 1e9), 1.25 * 2.7 * 2);
});

test('Idle, Active and Luck lanes: rates and the small stars', () => {
  near(idleMult(P(lane('idle'))), 1.25 * 1.45);
  near(awayMult(P(['idle_2'])), 1.25); near(awayMult(P(['idle_2', 'idle_9'])), 1.75);
  assert.equal(awayCapMs(P(['idle_4'])), 16 * 3600e3);
  assert.equal(autopilotShare(P(['idle_6'])), 0.5);
  near(tapMult(P(lane('active'))), 1.25 * 1.45);
  near(critMult(P(['active_3'])), 1.05);
  near(goldGapMult(P(['active_4'])), 0.8);
  assert.equal(goldStay(P(['active_9'])), 1.5);
  assert.equal(frenzyMult(P(['active_10'])), 2);
  near(prizeMult(P(lane('luck'))), 1.25 * 1.45);
  near(peekRate(P(['luck_2', 'luck_5'])), 1.25);
  assert.equal(boxHoldMs(P(['luck_8'])), 2000);
});

test('Combo: +2% a tap, capped at +50%', () => {
  assert.equal(comboBonus(0), 0); near(comboBonus(10), 0.2); assert.equal(comboBonus(25), 0.5); assert.equal(comboBonus(400), 0.5);
  assert.equal(comboBonus(NaN), 0);
});

test('Countdown: perfect = all ten, no extra tap', () => {
  assert.equal(countVerdict(10, 0).perfect, true);
  assert.equal(countVerdict(10, 1).perfect, false);
  assert.equal(countVerdict(9, 0).perfect, false);
});

test('Sleepwalk picks the cheapest plain row the bank covers', () => {
  const rows = [{ id: 'a', kind: 'item' }, { id: 'b', kind: 'item' }, { id: 'f', kind: 'fork' }, { id: 'e', kind: 'end' }, null];
  const cost = { a: 50, b: 20, f: 1, e: 1 };
  assert.equal(cheapestAffordable(rows, (id) => cost[id], 100).id, 'b');
  assert.equal(cheapestAffordable(rows, (id) => cost[id], 10), null);
  assert.equal(cheapestAffordable([], () => 1, 10), null);
});

test('No Nothing never lands on the Nothing wedge and keeps every other wedge reachable', () => {
  const nothing = SECTORS.find((s) => s.id === 'nothing');
  const seen = new Set();
  for (let i = 0; i < 2000; i++) {
    const r = skipNothing(i / 2000);
    assert.ok(r >= 0 && r < 1);
    const deg = r * 360;
    assert.ok(!(deg >= nothing.start && deg < nothing.end), `landed on nothing at ${deg}`);
    seen.add(SECTORS.find((s) => deg < s.end).id);
  }
  assert.deepEqual([...seen].sort(), SECTORS.filter((s) => s.id !== 'nothing').map((s) => s.id).sort());
});

test('Second Spin keeps the better wedge; Jackpot is the top wedge once a run', () => {
  const seq = (vals) => { let i = 0; return () => vals[i++ % vals.length]; };
  // Without the star the first draw stands.
  assert.equal(wheelDraw(P(), seq([0.4, 0.8])), 0.4);
  // With Second Spin: 0.4 is Nothing (x0), 0.8 is the 10x jackpot.
  assert.equal(wheelMultAt(wheelDraw(P(['luck_4']), seq([0.4, 0.8]))), 10);
  assert.equal(wheelMultAt(wheelDraw(P(['luck_4']), seq([0.8, 0.4]))), 10);
  const store = new Map(), st = { get: (k) => store.get(k), set: (k, v) => store.set(k, v) };
  const top = Math.max(...SECTORS.map((s) => s.mult));
  assert.equal(wheelMultAt(wheelDraw(P(['luck_10']), seq([0.1]), st)), top);
  assert.equal(store.get(JACKPOT_KEY), true);
  assert.equal(wheelDraw(P(['luck_10']), seq([0.1]), st), 0.1);  // the second spin is a normal draw
});

/* ---------- entries through a stub ctx ---------- */
function fakeEl(tag = 'div') {
  const listeners = {};
  const el = {
    tag, children: [], style: {}, className: '', textContent: '', parent: null, ownerDocument: null,
    addEventListener(t, fn) { (listeners[t] ||= []).push(fn); },
    removeEventListener(t, fn) { listeners[t] = (listeners[t] || []).filter((f) => f !== fn); },
    fire(t, ev = {}) { for (const fn of [...(listeners[t] || [])]) fn({ preventDefault() {}, ...ev }); },
    listeners,
    getBoundingClientRect: () => ({ left: 0, top: 0, width: 100, height: 100 }),
    remove() { if (el.parent) el.parent.children = el.parent.children.filter((c) => c !== el); },
    animate() { return { cancel() {} }; },
  };
  return el;
}

function fakeCtx(stars = [], { fps = 10, fuzz = 0 } = {}) {
  const prestige = P(stars);
  const handlers = {}, mods = [], frames = [], cleanups = [], toasts = [], pays = [], bought = [];
  let timers = [], now = 0;
  const doc = fakeEl('document');
  const btn = fakeEl('button'); btn.ownerDocument = doc;
  const layers = {};
  const state = { fuzz, clock: 0 };
  const shopRows = { goal: { id: 'g', kind: 'item', name: 'Goal' }, rows: [{ id: 'c', kind: 'item', name: 'Cheap' }] };
  const prices = { g: 1e9, c: 5 };
  const ctx = {
    prestige, state, gentle: false, reduced: false, paused: false,
    item: { id: 'x' }, brand: { text: (t) => t },
    button: { el: btn },
    has: () => false, switches: { isOn: () => true },
    audio: { play() {} },
    rng: () => 0.5,
    rates: () => ({ fps, click: 1 }),
    fmt: (n) => String(Math.round(n)),
    on(ev, fn) { (handlers[ev] ||= []).push(fn); },
    mod(m) { mods.push(m); },
    every(ms, fn) { timers.push({ at: now + ms, every: ms, fn }); },
    after(ms, fn) { timers.push({ at: now + ms, every: 0, fn }); },
    frame(fn) { frames.push(fn); },
    cleanup(fn) { cleanups.push(fn); },
    css() {}, toast(t) { toasts.push(t); }, float() {},
    anim() { const a = { cancel() {}, onfinish: null, oncancel: null }; return a; },
    el(tag, _css, text, parent) {
      const e = fakeEl(tag); if (text) e.textContent = text;
      const host = typeof parent === 'string' ? (layers[parent] ||= fakeEl('layer')) : parent;
      if (host) { e.parent = host; host.children.push(e); }
      return e;
    },
    pay(amount, source) { pays.push([amount, source]); return amount; },
    shop: () => shopRows, price: (id) => prices[id], buy(id) { bought.push(id); return { ok: true }; },
  };
  const fold = (field, v) => mods.reduce((acc, m) => (m[field] ? m[field](acc, state, now) : acc), v);
  /* Advance the game clock in 100 ms frames, firing timers and frames, like the engine does. */
  const advance = (ms, tick) => {
    for (let t = 0; t < ms; t += 100) {
      now += 100;
      for (const f of frames) f(0.1, now);
      tick?.(0.1, now);
      const due = timers.filter((x) => x.at <= now);
      timers = timers.filter((x) => x.at > now || x.every);
      for (const d of due) { d.fn(); if (d.every) d.at = now + d.every; }
    }
  };
  const fire = (ev, data) => { for (const fn of handlers[ev] || []) fn(data); };
  return { ctx, mods, fold, advance, fire, toasts, pays, bought, doc, layers, cleanups, get now() { return now; } };
}

test('pay entry: one post-knee mod on fps, click and wall', () => {
  const f = fakeCtx(['pay_1', 'pay_10']);
  lanes.pay.mount(f.ctx);
  assert.equal(f.mods.length, 1);
  assert.equal(f.mods[0].post, true);
  near(f.fold('fps', 10), 25); near(f.fold('click', 2), 5); near(f.fold('wall', 4), 10);
});

test('pay entry: Interest grows while the goal is out of reach, a buy resets it', () => {
  const f = fakeCtx(['pay_7']);
  lanes.pay.mount(f.ctx);
  f.advance(5 * 60000);
  near(f.fold('fps', 100), 105);
  f.fire('buy', {});
  near(f.fold('fps', 100), 100);
});

test('idle entry: Let Go turns on after 20 s alone and off at the next press', () => {
  const f = fakeCtx(['idle_10']);
  lanes.idle.mount(f.ctx);
  f.advance(LET_GO_MS - 500);
  near(f.fold('fps', 10), 10);
  f.advance(600);
  near(f.fold('fps', 10), 30);
  assert.ok(f.mods.some((m) => m.timed && m.fps), 'Let Go is a timed buff: never pays away time');
  f.doc.fire('pointerdown');
  near(f.fold('fps', 10), 10);
  f.advance(LET_GO_MS / 2); f.doc.fire('keydown'); f.advance(LET_GO_MS / 2 + 500);
  near(f.fold('fps', 10), 10);
  for (const c of f.cleanups) c();
  assert.equal(f.doc.listeners.pointerdown.length, 0);
});

test('idle entry: away fields and Sleepwalk', () => {
  const f = fakeCtx(['idle_2', 'idle_4', 'idle_7'], { fuzz: 10 });
  lanes.idle.mount(f.ctx);
  near(f.fold('away', 100), 125);
  assert.equal(f.fold('awayCap', CONFIG.AWAY_CAP_MS), 16 * 3600e3);
  f.advance(29900); assert.deepEqual(f.bought, []);
  f.advance(200); assert.deepEqual(f.bought, ['c']);
  assert.match(f.toasts.at(-1), /Cheap/);
});

test('active entry: tap pay and Combo (streak breaks after a second)', () => {
  const f = fakeCtx(['active_1', 'active_6']);
  const ret = lanes.active.mount(f.ctx);
  const tick = (dt, t) => ret.tick(dt, t);
  near(f.fold('click', 4), 5);
  for (let i = 0; i < 10; i++) { f.advance(200, tick); f.fire('tap', { auto: false }); }
  near(f.fold('click', 4), 5 * 1.2);
  f.fire('tap', { auto: true });
  near(f.fold('click', 4), 5 * 1.2);
  f.advance(1200, tick);
  near(f.fold('click', 4), 5);
});

test('active entry: a perfect Countdown pays a minute of per-second income, a sloppy one pays nothing', () => {
  for (const sloppy of [false, true]) {
    const f = fakeCtx(['active_7'], { fps: 7 });
    lanes.active.mount(f.ctx);
    f.advance(150 * 1000);  // rng 0.5 -> 225 s
    assert.equal(f.layers.over, undefined);
    f.advance(75 * 1000 + 100);
    const card = f.layers.over.children[0];
    assert.ok(card, 'the card is up');
    for (let n = 0; n < COUNT_FROM; n++) {
      card.fire('pointerdown');
      if (sloppy && n === 3) card.fire('pointerdown');
      f.advance(COUNT_BEAT_MS);
    }
    const counted = f.pays.filter(([, s]) => s === 'countdown');
    if (sloppy) { assert.equal(counted.length, 0); assert.match(f.toasts.at(-1), /Close/); }
    else { assert.deepEqual(counted, [[7 * 60, 'countdown']]); assert.match(f.toasts.at(-1), /Perfect/); }
  }
});

test('luck entry mounts with nothing to draw', () => {
  const f = fakeCtx(['luck_1']);
  assert.deepEqual(lanes.luck.mount(f.ctx), {});
  assert.equal(f.mods.length, 0);
});
