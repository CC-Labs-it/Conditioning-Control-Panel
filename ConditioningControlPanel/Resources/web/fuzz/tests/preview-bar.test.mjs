import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as E from '../sim/economy.js';
import { createJourney } from '../sim/journey.js';
import { createPrestige } from '../sim/prestige.js';
import { STAR_BY_ID } from '../data/prestige.js';
import { measureJourney } from '../dev/journey-timing.mjs';
import { previewBarAllowed, startsOpen, warpSlices, runClock, playAhead, journeyShop, plainShop, depthSetup, defaultPicks, STEP_MAX_MS, FRAME_MAX_MS } from '../ui/preview-bar-model.js';

const read = (p) => readFileSync(new URL(p, import.meta.url), 'utf8');
const at = (href) => new URL(href);

test('the preview bar shows on local and Cloudflare preview hosts only', () => {
  for (const h of ['http://localhost:8899/fuzz/', 'http://127.0.0.1:8811/fuzz/index.html?fresh=1', 'https://abc123.tc-preview.pages.dev/fuzz/']) {
    assert.equal(previewBarAllowed(at(h)), true, h);
  }
  for (const h of ['https://tc.cclabs.app/fuzz/', 'https://app.cclabs.app/tc', 'https://pages.dev/fuzz/', 'https://evilpages.dev/fuzz/',
    'https://pages.dev.example.com/', 'https://ccp.game/fuzz/index.html', 'https://localhost.example.com/']) {
    assert.equal(previewBarAllowed(at(h)), false, h);
  }
  assert.equal(previewBarAllowed(null), false);
});

test('never framed by cclabs.app, never in a CCP host, ?bar=0 hides it, a driven browser only on request', () => {
  const loc = at('https://x.pages.dev/fuzz/');
  assert.equal(previewBarAllowed(loc, { framed: true, framedBy: 'https://app.cclabs.app' }), false);
  assert.equal(previewBarAllowed(loc, { framed: true, framedBy: 'https://cclabs.app' }), false);
  assert.equal(previewBarAllowed(at('http://localhost:1/fuzz/?bar=1'), { framed: true, framedBy: 'https://app.cclabs.app' }), false, '?bar=1 never opens it under cclabs.app');
  assert.equal(previewBarAllowed(at('http://localhost:8817/fuzz/'), { framed: true, framedBy: 'http://localhost:8817' }), true, 'dev/play-ending.html frames it on localhost');
  assert.equal(previewBarAllowed(loc, { framed: true }), false, 'an unnamed parent');
  assert.equal(previewBarAllowed(at('https://x.pages.dev/fuzz/?bar=1'), { framed: true }), true);
  assert.equal(previewBarAllowed(loc, { hosted: true }), false);
  assert.equal(previewBarAllowed(at('http://localhost/fuzz/?bar=0')), false);
  assert.equal(previewBarAllowed(at('http://localhost/fuzz/'), { automated: true }), false);
  assert.equal(previewBarAllowed(at('http://localhost/fuzz/?bar=1'), { automated: true }), true);
  assert.equal(startsOpen(at('http://localhost/fuzz/?bar=1')), true);
  assert.equal(startsOpen(at('http://localhost/fuzz/')), false);
});

test('main.js loads the bar only behind the gate, and the bar is never part of the save', () => {
  const main = read('../main.js');
  assert.match(main, /previewBarAllowed\(location,/);
  assert.match(main, /import\('\.\/ui\/preview-bar\.js'\)/);
  assert.doesNotMatch(main, /from '\.\/ui\/preview-bar\.js'/, 'no static import: production never fetches it');
  const bar = read('../ui/preview-bar.js');
  assert.doesNotMatch(bar, /extras\./, 'nothing written into the save extras');
  assert.match(bar, /data-html2canvas-ignore/);
  assert.match(bar, /stopPropagation/);
  for (const src of [bar, read('../ui/preview-bar-model.js')]) {
    const words = [...src.matchAll(/(?:textContent = |b\(|say\()[`'"]([^`'"]*)/g)].map((m) => m[1]).join(' ');
    assert.doesNotMatch(words, /[\u2013\u2014!]/, 'no dashes or exclamation marks in the bar copy');
  }
});

/* The reference player of dev/journey-timing.mjs on a fresh Journey run, with its fixed mods. */
const MODS = [{ fps: (v, s) => (s.owned.depth ? v * 1.1 : v), click: (v, s) => (s.owned.f1 ? v * 1.5 : v) }];
const fresh = (herd = 0) => { const s = E.createRun({ seed: 11, herd }); return { s, shop: journeyShop(createJourney(s), MODS) }; };

test('time warp multiplies the frame and cuts it into steps the engine takes whole', () => {
  assert.deepEqual(warpSlices(16, 1), [16]);
  assert.deepEqual(warpSlices(16, 4), [64]);
  assert.deepEqual(warpSlices(16, 60), [960]);
  assert.deepEqual(warpSlices(50, 60), [1000, 1000, 1000]);
  assert.deepEqual(warpSlices(33, 16), [528]);
  const big = warpSlices(5000, 60);
  assert.equal(big.reduce((a, b) => a + b, 0), FRAME_MAX_MS, 'a stalled frame is capped');
  assert.ok(big.every((d) => d <= STEP_MAX_MS));
  assert.deepEqual(warpSlices(16, 7), [16], 'an unknown warp plays at 1x');
  assert.deepEqual(warpSlices(-5, 4), [0]);
});

test('income follows the warp: 60 frames at 4x pay what 240 frames at 1x pay', () => {
  const start = fresh();
  playAhead(start.s, start.shop, { ms: 5 * 60000, mods: MODS });   // a run with per-second pay
  const pay = (frames, warp) => {
    const s = structuredClone(start.s);
    for (let i = 0; i < frames; i++) for (const d of warpSlices(16, warp)) E.tick(s, d, MODS);
    return { clock: s.clock, total: s.total - start.s.total };
  };
  const a = pay(240, 1), b = pay(60, 4);
  assert.ok(a.total > 0, 'it earns');
  assert.equal(a.clock, b.clock);
  assert.ok(Math.abs(a.total - b.total) < 1e-6 * Math.max(1, a.total), `${a.total} vs ${b.total}`);
});

test('the run clock reads mm:ss and rolls hours into minutes', () => {
  assert.equal(runClock(0), '00:00');
  assert.equal(runClock(65_000), '01:05');
  assert.equal(runClock(92 * 60_000 + 5_400), '92:05');
});

test('skip plays the given minutes and is the same every time', () => {
  const runA = fresh(), runB = fresh();
  const a = playAhead(runA.s, runA.shop, { ms: 15 * 60000, mods: MODS });
  const b = playAhead(runB.s, runB.shop, { ms: 15 * 60000, mods: MODS });
  assert.equal(a.reason, 'time');
  assert.ok(Math.abs(runA.s.clock - 15 * 60000) < 1, `clock ${runA.s.clock}`);
  assert.deepEqual(a.bought, b.bought);
  assert.deepEqual(a.forks, b.forks);
  assert.equal(runA.s.fuzz, runB.s.fuzz);
  assert.ok(a.bought.length > 15, `${a.bought.length} buys in 15 minutes`);
  assert.ok(!runA.s.ended);
  /* Two +5 skips land where one +10 lands. */
  const c = fresh(), d = fresh();
  playAhead(c.s, c.shop, { ms: 5 * 60000, mods: MODS }); playAhead(c.s, c.shop, { ms: 5 * 60000, mods: MODS });
  playAhead(d.s, d.shop, { ms: 10 * 60000, mods: MODS });
  assert.equal(c.s.order.join(), d.s.order.join());
});

test('each jump stops on its salient point, before buying it', () => {
  const t = fresh();
  const theme = playAhead(t.s, t.shop, { until: 'theme', mods: MODS });
  assert.equal(theme.reason, 'target');
  assert.equal(t.s.picks.f5, undefined, 'the theme is left to the player');
  assert.equal(t.shop.shop(8).goal.id, 'f5');
  assert.ok(t.s.clock < 12 * 60000, `Pick a Theme at ${t.s.clock / 60000} min`);
  assert.equal(playAhead(t.s, t.shop, { until: 'theme', mods: MODS }).reason, 'target', 'standing on it stays there');

  const f = fresh(12);
  const fate = playAhead(f.s, f.shop, { until: 'fate', mods: MODS });
  assert.equal(fate.reason, 'target');
  const shelf = f.shop.shop(8);
  const seeds = [shelf.goal, ...shelf.rows].map((it) => it.id);
  assert.ok(seeds.includes('ft_panel'), `the Panel card with a full herd: ${seeds}`);
  assert.equal(E.fateOf(f.s), null);
  const min = f.s.clock / 60000;
  assert.ok(min > 25 && min < 50, `fate cards at ${min} min`);
  assert.equal(playAhead(f.s, f.shop, { until: 'theme', mods: MODS }).reason, 'past');

  const n = fresh();
  playAhead(n.s, n.shop, { until: 'fate', mods: MODS });
  const plain = n.shop.shop(8);
  assert.ok(![plain.goal, ...plain.rows].some((it) => it.id === 'ft_panel'), 'three llamas: no Panel card');

  const s2 = fresh();
  assert.equal(playAhead(s2.s, s2.shop, { until: 'taste2', mods: MODS }).reason, 'target');
  assert.ok([s2.shop.shop(8).goal, ...s2.shop.shop(8).rows].some((it) => it.id === 'taste2'));
});

test('the end jump lands where the reference player reaches its ending', () => {
  const picks = defaultPicks({ picks: {} });
  const ref = measureJourney(picks, { taps: 4, share: .125, option: 0 });
  const e = fresh();
  const res = playAhead(e.s, e.shop, { until: 'end', mods: MODS, picks });
  assert.equal(res.reason, 'target');
  const sh = e.shop.shop(8);
  const tile = [sh.goal, ...sh.rows].find((it) => it.kind === 'end');
  assert.ok(tile, 'an ending tile on the shelf');
  assert.ok(e.s.fuzz >= e.shop.price(tile.id), 'and affordable');
  assert.ok(Math.abs(e.s.clock / 60000 - ref.minutes) < 3, `bar ${e.s.clock / 60000} min, reference ${ref.minutes} min`);
  assert.ok(e.shop.buy(tile.id).ok);
  assert.equal(e.s.ended.id, tile.id);
});

test('the plain shop steps too, and never buys an ending', () => {
  const s = E.createRun({ seed: 5 });
  const res = playAhead(s, plainShop(s), { ms: 30 * 60000 });
  assert.ok(res.bought.length > 5);
  assert.ok(!s.ended);
  assert.ok(!res.bought.some((id) => id === 'drop'));
});

test('a Renewal Depth comes with stars no deeper than itself and some Stardust', () => {
  assert.equal(Object.keys(depthSetup(0, createPrestige()).own).length, 0);
  for (const d of [1, 4, 10]) {
    const p = depthSetup(d, createPrestige());
    assert.equal(p.depth, d);
    assert.ok(p.dust > 0);
    const ids = Object.keys(p.own);
    assert.ok(ids.length >= d, `${ids.length} stars at depth ${d}`);
    for (const id of ids) assert.ok(STAR_BY_ID[id] && STAR_BY_ID[id].depth <= d, id);
    assert.ok(!p.own.head_9, 'never Skip Ahead');
  }
  assert.equal(depthSetup(99, createPrestige()).depth, 10);
});
