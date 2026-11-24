// Feedback wave 2026-11-09 lane C: hidden tap-speed achievements, the EMI bell curve, the sell-all joke, the ad accident card.
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { BURST_HOLD_MS, EMI_LIFT, EMI_SWEET, TAP_BURSTS, burstTiers, createTapWindow, emiBell, inEmiSweet } from '../sim/tap-rate.js';
import * as E from '../sim/economy.js';
import { ACHIEVEMENTS } from '../data/content/index.js';
import { SELL_HOLD_MS, SELL_RISE_MS, sellAllShown, SELL_ALL_AD } from '../ui/sell-all.js';
import { AD_ASK, adLinkStep } from '../ui/ads.js';
import { ADS } from '../data/ads.js';
import { CONFIG } from '../data/config.js';

const src = (p) => fs.readFileSync(new URL(p, import.meta.url), 'utf8');

/** Presses at `perSec` from start for ms; answers the window and the last press time. */
function presses(perSec, ms, start = 1000, win = createTapWindow(BURST_HOLD_MS)) {
  const gap = 1000 / perSec;
  let t = start;
  const tiers = new Set();
  for (; t <= start + ms; t += gap) { win.push(t); for (const x of burstTiers(win, t)) tiers.add(x); }
  return { win, t: t - gap, tiers: [...tiers] };
}

test('speed tiers are 8, 12 and 15 a second, each held for 3 seconds', () => {
  assert.deepEqual(TAP_BURSTS, [8, 12, 15]);
  assert.equal(BURST_HOLD_MS, 3000);
  assert.deepEqual(presses(8.2, 3100).tiers, [8]);
  assert.deepEqual(presses(12.5, 3100).tiers, [8, 12]);
  assert.deepEqual(presses(16, 3100).tiers, [8, 12, 15]);
  assert.deepEqual(presses(7, 10000).tiers, [], '7 a second never reaches the first tier');
  assert.deepEqual(presses(9, 2000).tiers, [], 'two seconds of 9 a second is not three seconds');
  assert.deepEqual(presses(20, 1000).tiers, [], 'a one second burst does not count');
});

test('the speed achievements are hidden, plain and read only trusted manual presses', () => {
  const speed = ACHIEVEMENTS.filter((a) => a.when.startsWith('tapburst'));
  assert.deepEqual(speed.map((a) => a.when), ['tapburst8:1', 'tapburst12:1', 'tapburst15:1']);
  assert.ok(speed.every((a) => a.hidden === true));
  assert.ok(ACHIEVEMENTS.filter((a) => !a.when.startsWith('tapburst')).every((a) => a.hidden === false));
  for (const a of speed) assert.doesNotMatch(a.name + a.line, /[!–—]/);
  const trunk = src('../ui/unlocks/basics/trunk.js');
  assert.match(trunk, /via !== 'hand'/, 'the tally reads only via hand');
  const app = src('../ui/app.js');
  assert.match(app, /const via = !ev\.isTrusted \? '' : hand === 'left' \? 'hand' : 'right'/, 'hand = trusted left press on #btn');
  assert.match(app, /via: 'copy'/, 'bouncing copies are tagged apart');
  assert.match(app, /via: 'key'/, 'the keyboard is tagged apart');
  assert.match(src('../ui/engine.js'), /via: auto \? '' : via/, 'auto clicks never carry a via');
});

test('tap window rate: steady rhythm reads true, a single press reads 0', () => {
  const w = createTapWindow(3000);
  w.push(0);
  assert.equal(w.rate(0), 0);
  let t = 0;
  for (let i = 0; i < 12; i++) { t += 200; w.push(t); }
  assert.ok(Math.abs(w.rate(t) - 5) < 0.01);
  assert.equal(w.count(t + 5000), 0, 'old presses drop out');
});

test('EMI bell: peaks at the sweet spot, falls both ways, never under the base', () => {
  assert.equal(EMI_SWEET, 5.5);
  assert.ok(Math.abs(emiBell(EMI_SWEET) - (1 + EMI_LIFT)) < 1e-9);
  assert.equal(emiBell(0), 1);
  assert.equal(emiBell(NaN), 1);
  for (let r = 0.1; r < 30; r += 0.1) {
    const m = emiBell(r);
    assert.ok(m >= 1 && m <= 1 + EMI_LIFT);
    if (r < EMI_SWEET - 0.1) assert.ok(emiBell(r + 0.1) >= m, 'rises to the peak');
    if (r > EMI_SWEET + 0.1) assert.ok(emiBell(r + 0.1) <= m, 'falls after it');
  }
  assert.ok(inEmiSweet(emiBell(5)) && inEmiSweet(emiBell(6.5)));
  assert.ok(!inEmiSweet(emiBell(3)) && !inEmiSweet(emiBell(9)));
});

test('economy tap: the bell multiplies manual taps only, clamped to 1..1+lift', () => {
  const base = E.tap(E.createRun());
  assert.equal(E.tap(E.createRun(), null, 0, false, 1.4), base * 1.4);
  assert.equal(E.tap(E.createRun(), null, 0, false, 0.2), base, 'never under the base');
  assert.equal(E.tap(E.createRun(), null, 0, false, 9), base * (1 + EMI_LIFT), 'capped at the peak');
  assert.equal(E.tap(E.createRun(), null, 0, true, 1.4), E.tap(E.createRun(), null, 0, true), 'auto clicks ignore it');
  assert.equal(E.tap(E.createRun(), null, 0, false, Infinity), base);
});

test('sell-all joke: 0 for ten seconds, then counts back to the live wallet, display only', () => {
  assert.ok(ADS.some((a) => a.id === SELL_ALL_AD && !a.ccp), 'the joke ad exists');
  assert.equal(SELL_HOLD_MS, 10000);
  assert.equal(sellAllShown(5000, 0), 0);
  assert.equal(sellAllShown(5000, SELL_HOLD_MS - 1), 0);
  const mid = sellAllShown(5000, SELL_HOLD_MS + SELL_RISE_MS / 2);
  assert.ok(mid > 0 && mid < 5000);
  assert.equal(sellAllShown(5000, SELL_HOLD_MS + SELL_RISE_MS), null, 'over: the real counter is back');
  const code = src('../ui/sell-all.js');
  assert.doesNotMatch(code, /\.spend\(|\.pay\(|fuzz\s*[-+]?=|state\.fuzz\s*=/, 'never touches the wallet');
  assert.match(code, /ctx\.on\('pause', end\)/);
  assert.match(code, /'suspend'/);
});

test('ad accident card: the first real press opens, the second asks, the cap still holds', () => {
  const used = { n: 0 };
  assert.equal(adLinkStep(used, 2), 'open');
  used.n = 1;
  assert.equal(adLinkStep(used, 2), 'ask');
  used.n = 2;
  assert.equal(adLinkStep(used, 2), 'none', 'past the cap nothing opens or asks');
  assert.equal(adLinkStep({ n: 0, clicks: 0 }, 0), 'none');
  const fresh = { n: 0 };
  adLinkStep(fresh, 2);
  assert.equal(adLinkStep(fresh, 2), 'ask', 'asks even if the first press opened nothing');
  assert.deepEqual([AD_ASK.title, AD_ASK.open, AD_ASK.stay], ['Open the ad in a new tab?', 'Open', 'Stay here']);
  assert.ok(CONFIG.AD_TABS_PER_RUN >= 1);
  const ctxSrc = src('../ui/context.js');
  assert.match(ctxSrc, /adLinkStep\(used, CONFIG\.AD_TABS_PER_RUN\)/);
  assert.match(ctxSrc, /if \(used\.n >= CONFIG\.AD_TABS_PER_RUN\) return false;/, 'Open rechecks the cap');
});
