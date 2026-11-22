import { test } from 'node:test';
import { existsSync } from 'node:fs';
import assert from 'node:assert/strict';
import { ADS } from '../data/ads.js';
import { CCP_THEME_ADS } from '../data/content/ads.js';
import { adPool, createAds } from '../ui/ads.js';
import { createEngine } from '../ui/engine.js';
import * as E from '../sim/economy.js';
import { createLife } from '../sim/save.js';

const draw = (ads, n, o) => Array.from({ length: n }, () => ads.next(o));

test('no theme is the plain pool; a theme with no cards of its own is too', () => {
  assert.equal(adPool(null), ADS);
  assert.equal(adPool('trance'), ADS);
});

test('a theme mixes in all its cards and keeps the ratio of jokes to real', () => {
  const pool = adPool('drone');
  assert.equal(pool.length, ADS.length);
  assert.equal(pool.filter((a) => a.ccp).length, ADS.filter((a) => a.ccp).length);
  for (const card of CCP_THEME_ADS.drone) assert.ok(pool.some((a) => a.id === card.id && a.ccp === true), card.id);
  assert.equal(pool.some((a) => CCP_THEME_ADS.bimbo.some((b) => b.id === a.id)), false, 'no other theme leaks in');
  assert.ok(pool.some((a) => a.ccp && !CCP_THEME_ADS.drone.some((c) => c.id === a.id)), 'plain CCP cards stay in the mix');
});

test('every theme card can be reached by its kind', () => {
  const ads = createAds();
  for (const theme of Object.keys(CCP_THEME_ADS)) {
    const seen = new Set(draw(ads, 400, { theme }).map((a) => a.id));
    for (const card of CCP_THEME_ADS[theme]) assert.ok(seen.has(card.id), `${theme} ${card.id}`);
  }
});

test('ccpOnly never answers a joke, whatever the kind', () => {
  const ads = createAds();
  for (const kind of ['banner', 'popup', 'no-such-kind', undefined]) assert.ok(draw(ads, 60, { kind, ccpOnly: true }).every((a) => a.ccp), String(kind));
});

test('a kind is honoured, and a kind with no match falls back to any', () => {
  const ads = createAds();
  assert.ok(draw(ads, 30, { kind: 'popup' }).every((a) => a.kind === 'popup'));
  assert.ok(ads.next({ kind: 'no-such-kind' }));
});

function engineWith(picks) {
  let a, b;
  const run = E.createRun();
  for (const id of picks) E.grant(run, id);
  const heard = [];
  const ui = { doc: {}, buttonFor: () => ({}), layer: () => ({}) };
  const engine = createEngine({ run, life: createLife(), ui, registry: {
    stray: { mount(c) { a = c; c.on('ads', (e) => heard.push(e.jokesOff)); return {}; } },
    rounded: { mount(c) { b = c; return {}; } },
  } });
  engine.mount('stray');
  engine.mount('rounded');
  return { engine, heard, get a() { return a; }, get b() { return b; } };
}

test('ctx.ads.next defaults to the picked theme; null asks for none', () => {
  const t = engineWith(['stray', 'rounded', 'dr_head']);
  assert.equal(E.pick(t.engine.run, 'f5'), 'drone');
  const ids = new Set(Array.from({ length: 300 }, () => t.a.ads.next().id));
  assert.ok(CCP_THEME_ADS.drone.some((c) => ids.has(c.id)));
  const plain = new Set(Array.from({ length: 300 }, () => t.a.ads.next({ theme: null }).id));
  assert.equal(CCP_THEME_ADS.drone.some((c) => plain.has(c.id)), false);
});

test('jokesOff binds every slot in every lane and ends with its holder', () => {
  const t = engineWith(['stray', 'rounded']);
  assert.ok(Array.from({ length: 30 }, () => t.a.ads.next()).some((x) => !x.ccp));
  t.b.ads.jokesOff();
  assert.equal(t.a.ads.jokesAreOff, true);
  assert.ok(Array.from({ length: 60 }, () => t.a.ads.next()).every((x) => x.ccp), 'another unlock\'s slot obeys');
  assert.deepEqual(t.heard, [true]);
  t.b.ads.jokesOff();
  assert.deepEqual(t.heard, [true], 'no event when nothing changed');
  t.engine.unmount('rounded');
  assert.equal(t.a.ads.jokesAreOff, false);
  assert.deepEqual(t.heard, [true, false]);
});

test('all eight illustrated campaigns deal their three files before repeating', () => {
  const ads = createAds();
  for (const [ccpOnly, count] of [[false, 6], [true, 2]]) {
    const options = { illustrated: true, kind: 'banner', ccpOnly };
    const seen = new Set();
    for (let round = 0; round < 3; round++) {
      const cards = draw(ads, count, options);
      assert.equal(new Set(cards.map(ad => ad.id)).size, count);
      for (const ad of cards) {
        assert.equal(ad.ccp, ccpOnly);
        assert.equal(!!ad.url, ccpOnly);
        assert.ok(ad.caption);
        assert.ok(existsSync(new URL('../' + ad.illustration, import.meta.url)), ad.illustration);
        seen.add(ad.illustration);
      }
    }
    assert.equal(seen.size, count * 3);
    assert.ok(seen.has(ads.next(options).illustration));
  }
});

test('illustrated slots respect plan changes and Joke Ads Gone, independently of banner rotation', () => {
  let subscriber = false;
  const ads = createAds({ noCcp: () => subscriber });
  const banners = createAds();
  const options = { illustrated: true };
  ads.hold('premium', true);
  assert.ok(draw(ads, 3, options).every(ad => ad.ccp));
  subscriber = true;
  assert.ok(draw(ads, 3, { ...options, ccpOnly: true }).every(ad => !ad.ccp));
  subscriber = false;
  ads.hold('premium', false);
  assert.ok(draw(ads, 3, options).every(ad => !ad.ccp));
  assert.equal(ads.next({kind: 'banner'}).id, banners.next({kind: 'banner'}).id);
});
