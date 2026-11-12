// Renewal Sky, Ghost Run lane (handoff-1109/TC-PRESTIGE-WIRE-BRIEF.md): the recorder runs every run; ghost stars race it.
import test from 'node:test';
import assert from 'node:assert/strict';
import * as G from '../sim/ghost.js';
import entries from '../ui/unlocks/prestige/ghost.js';

/** A 90 minute run: a purchase about every 36 s, a Golden Button every 4 min, totals rising. */
function fakeRun(minutes = 90, every = 36, seed = 7) {
  const rec = G.createRec(seed);
  let total = 0;
  for (let t = 0; t <= minutes * 60; t++) {
    total += 1 + t * t * 0.01;
    G.sample(rec, t * 1000, total);
    if (t && t % every === 0) G.noteBuy(rec, t * 1000, `item_${t % 97}`);
    if (t && t % 240 === 0) G.noteGold(rec, t * 1000);
    if (t && t % 780 === 0) G.noteChapter(rec, t * 1000, t / 780);
  }
  return { rec, total };
}

test('a 90 minute run packs into a few KB and unpacks the same', () => {
  const { rec } = fakeRun();
  const p = G.finish(rec, 90 * 60 * 1000, 'drop');
  const bytes = JSON.stringify(p).length;
  assert.ok(bytes < 4000, `packed ${bytes} bytes`);
  const u = G.unpack(p);
  assert.equal(u.ms, 90 * 60 * 1000);
  assert.equal(u.end, 'drop');
  assert.equal(u.buys.length, rec.b.length);
  assert.deepEqual(u.buys.map((b) => b.t), rec.bt);
  assert.equal(u.totals.length, rec.n);
  assert.equal(u.g.length, rec.g.length);
  assert.equal(u.ch[1], 780);
  // samples rise and stay within the 12% quantisation step
  for (let i = 1; i < u.totals.length; i++) assert.ok(u.totals[i] >= u.totals[i - 1]);
});

test('samples land every 5 s of play, capped', () => {
  const rec = G.createRec(1);
  assert.equal(G.sample(rec, 0, 0), 1);
  assert.equal(G.sample(rec, 4999, 10), 0);
  assert.equal(G.sample(rec, 5000, 10), 1);
  assert.equal(G.sample(rec, 30000, 10), 5);
  G.sample(rec, 99 * 3600 * 1000, 1e300);
  assert.equal(rec.n, G.MAX_SAMPLES);
});

test('a big jump in total uses the escape and still round-trips', () => {
  const rec = G.createRec(1);
  G.sample(rec, 0, 0);
  G.sample(rec, 5000, 1e40);
  const u = G.unpack(G.finish(rec, 6000));
  assert.ok(Math.abs(Math.log10(u.totals[1] + 1) - 40) < 0.05);
});

test('the worst case is capped at MAX_BYTES', () => {
  const rec = G.createRec(1);
  for (let i = 0; i < G.MAX_BUYS + 50; i++) G.noteBuy(rec, i * 999000, 'a_really_long_item_name_' + (i % 9));
  for (let t = 0; t < G.MAX_SAMPLES; t++) { rec.q += '~0zz'; rec.n++; }
  const p = G.finish(rec, 1000);
  assert.ok(JSON.stringify(p).length <= G.MAX_BYTES);
  assert.ok(G.unpack(p));
});

test('keep holds the last finished run and the fastest one', () => {
  const ps = {};
  const run = (ms) => { const r = G.createRec(); G.noteBuy(r, 1000, 'start2'); return G.finish(r, ms); };
  G.keep(ps, run(5000e3));
  G.keep(ps, run(4000e3));
  G.keep(ps, run(6000e3));
  assert.equal(ps.last.ms, 6000e3);
  assert.equal(ps.best.ms, 4000e3);
  assert.equal(ps.runs, 3);
  assert.equal(G.chooseGhost(ps, false).ms, 6000e3);
  assert.equal(G.chooseGhost(ps, true).ms, 4000e3);
  assert.equal(G.chooseGhost({}, true), null);
  assert.ok(JSON.stringify(ps).length < 2 * G.MAX_BYTES + 100);
});

test('unpack and parseRec never throw on junk', () => {
  for (const junk of [null, 1, 'x', [], { v: 1, ms: 5, b: 7, bt: {}, q: 3, ch: 'a', g: 'b' }, { v: 1, ms: 5, b: 'a,b', bt: 'zz.!!', q: '~~~' }]) {
    assert.doesNotThrow(() => G.unpack(junk));
    assert.doesNotThrow(() => G.parseRec(junk, 3));
  }
  // a recording from another run (other seed) starts fresh
  const r = G.createRec(5); G.noteBuy(r, 1000, 'x');
  assert.equal(G.parseRec(JSON.parse(JSON.stringify(r)), 5).b.length, 1);
  assert.equal(G.parseRec(JSON.parse(JSON.stringify(r)), 6).b.length, 0);
});

test('ahead and behind are measured on purchases', () => {
  const ghost = { ms: 600e3, buys: [{ t: 10, id: 'a' }, { t: 20, id: 'b' }, { t: 60, id: 'c' }], ch: [0], g: [] };
  assert.equal(G.gapSeconds(ghost, 1, 4), 6);      // first buy at 4 s, the ghost took 10
  assert.equal(G.gapSeconds(ghost, 1, 15), 0);     // between its first and second
  assert.equal(G.gapSeconds(ghost, 1, 30), -10);   // the ghost made its second at 20
  assert.equal(G.gapSeconds(ghost, 3, 50), 10);
  assert.equal(G.gapSeconds(ghost, 3, 700), -100); // the ghost finished at 600
  assert.equal(G.gapLine(80), '1:20 ahead of your ghost');
  assert.equal(G.gapLine(-5), '0:05 behind your ghost');
  assert.equal(G.gapLine(0), 'Level with your ghost');
  assert.equal(G.fmtGap(3725), '1:02:05');
  assert.doesNotMatch(G.gapLine(80) + G.gapLine(-80), /[–—!]/);
});

test('chapters, hints and golds', () => {
  const ghost = { ms: 600e3, buys: [{ t: 10, id: 'a' }, { t: 20, id: 'b' }], ch: [0, 100, null], g: [30, 90] };
  assert.equal(G.beatChapter(ghost, 1, 99), true);
  assert.equal(G.beatChapter(ghost, 1, 101), false);
  assert.equal(G.beatChapter(ghost, 2, 999), true);   // the ghost never got there
  assert.equal(G.beatChapter(ghost, 0, 0), false);
  assert.equal(G.ghostNext(ghost, { a: 1 }), 'b');
  assert.equal(G.ghostNext(ghost, { a: 1, b: 1 }), null);
  assert.equal(G.goldsBetween(ghost, 29, 30), 1);
  assert.equal(G.goldsBetween(ghost, 30, 89), 0);
  assert.deepEqual(G.ghostBuysAt(ghost, 15).map((b) => b.id), ['a']);
});

test('stars map to perks', () => {
  assert.deepEqual(G.perks(0), { race: false, beat: 0, taps: 0, hints: false, best: false, replay: false });
  assert.equal(G.perks(1).race, true);
  assert.equal(G.perks(2).beat, 0.05);
  assert.equal(G.perks(3).beat, 0.1);
  assert.equal(G.perks(4).taps, 1);
  assert.equal(G.perks(5).beat, 0.15);
  assert.equal(G.perks(6).hints, true);
  assert.equal(G.perks(7).best, true);
  assert.equal(G.perks(8).beat, 0.2);
  assert.equal(G.perks(9).taps, 2);
  assert.equal(G.perks(10).replay, true);
});

/* ---- the entry, on a fake ctx ---- */

function fakeEl() {
  const el = { style: {}, children: [], className: '', textContent: '', innerHTML: '', attrs: {}, parentNode: null, isConnected: true,
    classList: { set: new Set(), toggle(c, on) { on ? this.set.add(c) : this.set.delete(c); }, contains(c) { return this.set.has(c); } },
    setAttribute(k, v) { this.attrs[k] = v; }, appendChild(c) { c.parentNode = el; el.children.push(c); return c; } };
  return el;
}
function fakeCtx({ level = 0, pstore = {}, seed = 1 } = {}) {
  const subs = {}, frames = [], timers = [], mods = [], emitted = [], store = {}, toasts = [], layers = { meters: fakeEl() };
  let taps = 0;
  const state = { seed, clock: 0, total: 0, buys: 0, picks: {}, owned: { start: 1 }, order: ['start'], ended: null, store: {} };
  const ctx = {
    state, pstore, gentle: true, reduced: false,
    prestige: { level: (lane) => (lane === 'ghost' ? level : 0) },
    store: { get: (k) => store[k], set: (k, v) => { store[k] = JSON.parse(JSON.stringify(v)); } },
    on: (e, fn) => { (subs[e] ||= []).push(fn); },
    emit: (e, d) => { emitted.push([e, d]); for (const fn of subs[e] || []) fn(d); },
    every: (ms, fn) => { timers.push({ ms, fn }); }, frame: (fn) => frames.push(fn), mod: (m) => mods.push(m),
    css() {}, anim() {}, toast: (t) => toasts.push(t),
    el: (tag, css, text, parent) => { const e = fakeEl(); e.textContent = text || ''; const p = typeof parent === 'string' ? layers[parent] : parent; p?.appendChild(e); return e; },
    button: { press: () => { taps++; return 1; } },
  };
  const run = {
    buy(id) { state.buys++; state.owned[id] = 1; state.order.push(id); ctx.emit('buy', { item: { id } }); },
    play(sec) { for (let i = 0; i < sec; i++) { state.clock += 1000; state.total += 10; for (const f of frames) f(1, state.clock); } },
    end(id) { state.ended = { id, at: state.clock }; ctx.emit('ending', { id }); },
  };
  return { ctx, run, store, mods, emitted, toasts, timers, layers, get taps() { return taps; } };
}

test('the recorder runs on a first run with no stars and keeps the finished run', () => {
  assert.equal(entries.ghost.always, true);
  const f = fakeCtx({ level: 0 });
  entries.ghost.mount(f.ctx);
  f.run.play(30); f.run.buy('stray'); f.run.play(30); f.run.buy('rounded');
  f.ctx.emit('golden', {});
  f.run.play(10);
  f.run.end('drop');
  assert.equal(f.layers.meters.children.length, 0, 'no chip without a ghost star');
  assert.equal(f.mods.length, 0);
  const g = G.unpack(f.ctx.pstore.last);
  assert.deepEqual(g.buys.map((b) => [b.t, b.id]), [[30, 'stray'], [60, 'rounded']]);
  assert.deepEqual(g.g, [60]);
  assert.equal(g.end, 'drop');
  assert.equal(g.totals.length, 15);
  assert.equal(f.ctx.pstore.best.ms, 70000);
});

test('a reload keeps the recording of the same run', () => {
  const f = fakeCtx({ seed: 9 });
  entries.ghost.mount(f.ctx);
  f.run.play(20); f.run.buy('stray');
  const saved = f.store.rec;
  const g = fakeCtx({ seed: 9 });
  g.ctx.store.set('rec', saved);
  g.ctx.state.clock = 20000;
  entries.ghost.mount(g.ctx);
  g.run.buy('rounded');
  assert.deepEqual(g.store.rec.b, ['stray', 'rounded']);
});

test('with stars: the chip races the ghost, chapter pay, hands, hints and replayed golds', () => {
  const ps = {};
  const first = fakeCtx({ pstore: ps });
  entries.ghost.mount(first.ctx);
  first.run.play(60); first.run.buy('stray'); first.run.play(60); first.run.buy('rounded');
  first.ctx.emit('golden', {});
  first.run.play(30); first.run.end('drop');

  const f = fakeCtx({ level: 10, pstore: ps, seed: 2 });
  entries.ghost.mount(f.ctx);
  const chip = f.layers.meters.children[0];
  assert.ok(chip, 'chip drawn');
  f.run.play(20); f.run.buy('stray');
  f.run.play(1);
  assert.match(chip.children[0].textContent, /^0:39 ahead of your ghost$/);
  assert.ok(chip.classList.contains('ahead'));
  assert.equal(f.mods.length, 1);
  assert.ok(f.timers.some((t) => t.ms === 500), 'Busy Ghost taps twice a second');
  f.timers[0].fn();
  assert.equal(f.taps, 1);
  // Replay: the ghost caught a Golden Button at 120 s
  f.run.play(100);
  assert.equal(f.emitted.filter(([e]) => e === 'ghost-gold').length, 1);
  assert.match(chip.children[0].textContent, /behind your ghost$/);
  // the guide map hook: marker on the ghost's last buy, hint on what it bought next
  const world = fakeEl();
  f.ctx.emit('guide-paint', { world, nodes: [{ id: 'start', x: 1, y: 1 }, { id: 'stray', x: 10, y: 20 }, { id: 'rounded', x: 30, y: 40 }] });
  const mark = world.children.find((c) => c.className === 'gh-mark');
  const hint = world.children.find((c) => c.className === 'gh-hint');
  assert.equal(mark.style.left, '30px');
  assert.equal(hint.style.left, '30px');
  assert.equal(hint.textContent, 'Your ghost bought this next');
});

test('reaching a chapter before the ghost pays more for that chapter', async () => {
  const { JOURNEY_CHAPTERS, journeyChapter } = await import('../sim/journey-effects.js');
  const need = JOURNEY_CHAPTERS[0][1];
  const ps = {};
  const first = fakeCtx({ pstore: ps });
  entries.ghost.mount(first.ctx);
  for (let i = 0; i < need; i++) { first.run.play(60); first.run.buy('x' + i); }
  first.run.play(5); first.run.end('drop');
  assert.equal(G.unpack(ps.last).ch[1], need * 60);

  const f = fakeCtx({ level: 3, pstore: ps, seed: 3 });
  entries.ghost.mount(f.ctx);
  for (let i = 0; i < need; i++) { f.run.play(30); f.run.buy('x' + i); }
  assert.equal(journeyChapter(f.ctx.state), 1);
  assert.deepEqual(f.toasts, ['Ahead of your ghost: +10% this chapter']);
  assert.ok(Math.abs(f.mods[0].fps(100, f.ctx.state) - 110) < 1e-9);
  assert.equal(f.mods[0].timed, true);
  assert.equal(f.store.beat, 1);
});
