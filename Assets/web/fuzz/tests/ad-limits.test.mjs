// Owner 2026-11-08: Basic and Prime players never get a CCP card; real ads open at most AD_TABS_PER_RUN tabs a run.
import test from 'node:test';
import assert from 'node:assert/strict';
import { createAds, subscriberPlan } from '../ui/ads.js';
import { CONFIG } from '../data/config.js';

test('subscriber plans are basic and prime only', () => {
  assert.equal(subscriberPlan({ plan: 'basic' }), true);
  assert.equal(subscriberPlan({ plan: 'prime' }), true);
  assert.equal(subscriberPlan({ plan: 'free' }), false);
  assert.equal(subscriberPlan({}), false);
  assert.equal(subscriberPlan(undefined), false);
});

test('a subscriber never gets a real card, even with jokes held off or ccpOnly', () => {
  const ads = createAds({ noCcp: () => true });
  ads.hold('x', true);
  assert.equal(ads.jokesOff, false);
  for (let i = 0; i < 200; i++) {
    for (const opts of [{}, { kind: 'banner' }, { kind: 'popup' }, { ccpOnly: true }, { theme: 'sissy' }]) {
      const ad = ads.next(opts);
      assert.ok(ad, 'always an ad');
      assert.ok(!ad.ccp, 'never a CCP card');
    }
  }
});

test('a free player still gets real cards, and jokes off leaves only real ones', () => {
  const ads = createAds({ noCcp: () => false });
  const seen = Array.from({ length: 80 }, () => ads.next({ kind: 'banner' }));
  assert.ok(seen.some((a) => a.ccp));
  ads.hold('x', true);
  assert.equal(ads.jokesOff, true);
  for (let i = 0; i < 40; i++) assert.ok(ads.next({}).ccp);
});

test('the per-run tab cap is one or two', () => {
  assert.ok(CONFIG.AD_TABS_PER_RUN >= 1 && CONFIG.AD_TABS_PER_RUN <= 2);
});
