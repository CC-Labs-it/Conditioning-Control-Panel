import test from 'node:test';
import assert from 'node:assert/strict';
import { chosenWave, ownedWave, SHOP_WAVES } from '../ui/wave-ribbon.js';
import { KINETIC_STUDIES } from '../ui/kinetic-waves.js';
import { buttonShopReady, FACE_STYLES } from '../ui/button-shop.js';
import { COPY_PRICES } from '../ui/bouncing-art.js';

test('every ribbon the Button Shop sells has a price, the kinetic studies included', () => {
  for (const w of SHOP_WAVES) assert.ok(Number.isFinite(w.price), w.id);
  for (const k of KINETIC_STUDIES) assert.ok(k.price > 0, k.id);
});

test('a bought kinetic ribbon is owned and worn on the real button', () => {
  assert.equal(ownedWave({}, 'chroma'), false);
  assert.equal(ownedWave({ waveStyles: { chroma: true } }, 'chroma'), true);
  assert.equal(chosenWave({ waveStyle: 'harp', waveStyles: { harp: true } }), 'harp');
  assert.equal(chosenWave({ waveStyle: 'harp' }), 'classic', 'an unbought ribbon never shows');
  assert.equal(chosenWave({ waveStyle: 'nope', waveStyles: { nope: true } }), 'classic');
});

test('the door dot shows only for an affordable thing not yet owned', () => {
  /* Every shop gate open here (ui/shop-gates.js); the gates have their own test. */
  const ctx = (fuzz, bubble = {}) => ({ state: { fuzz, store: { bubble }, owned: { bubble: 1, f5: 1, f3: 1, depth: 1, glossy: 1 } } });
  const human = FACE_STYLES.find((s) => s.id === 'human-check').price;
  const cheapestWave = Math.min(...SHOP_WAVES.filter((w) => w.price > 0).map((w) => w.price));
  const copies = Object.entries(COPY_PRICES).flatMap(([f, row]) => Object.entries(row).filter(([, p]) => p > 0).map(([v, p]) => [f + ':' + v, p]));
  const copyOwned = Object.fromEntries(copies.map(([k]) => [k, true]));
  assert.equal(buttonShopReady(ctx(Math.min(human, cheapestWave, ...copies.map(([, p]) => p)) - 1), []), false);
  assert.equal(buttonShopReady(ctx(human), []), true);
  const allWaves = Object.fromEntries(SHOP_WAVES.map((w) => [w.id, true]));
  assert.equal(buttonShopReady(ctx(1e9, { faces: { 'human-check': true, emi: true }, waveStyles: allWaves, copyOwned }), []), false);
  assert.equal(buttonShopReady(ctx(1e9, { faces: { 'human-check': true }, waveStyles: allWaves }), ['human-check', 'emi']), true,
    'the wardrobe list always names the sold faces; only a purchase owns them');
});

test('Human Check stays with a save that already wore it', async () => {
  const { grandfatherFaces } = await import('../ui/button-shop.js');
  const kept = grandfatherFaces({ choice: 'human-check', faces: { x: true } });
  assert.deepEqual(kept, { x: true, 'human-check': true });
  assert.equal(grandfatherFaces({ choice: 'human-check', faces: kept }), null, 'only once');
  assert.equal(grandfatherFaces({ choice: 'bubble' }), null);
  assert.equal(grandfatherFaces(undefined), null);
});

test('Big and Tiny are sizes: the pick wins while its row is owned and on, else the latest size row', async () => {
  const { buttonSize } = await import('../ui/button-shop.js');
  const run = (owned, pick, off = {}) => ({ owned, off, store: pick ? { bubble: { size: pick } } : {} });
  assert.equal(buttonSize(run({})), '');
  assert.equal(buttonSize(run({ bigbtn: 1 })), 'big');
  assert.equal(buttonSize(run({ bigbtn: 1, tinybtn: 1 })), 'tiny');
  assert.equal(buttonSize(run({ bigbtn: 1, tinybtn: 1 }, 'big')), 'big');
  assert.equal(buttonSize(run({ bigbtn: 1, tinybtn: 1 }, 'normal')), '');
  assert.equal(buttonSize(run({ bigbtn: 1 }, 'tiny')), 'big', 'an unowned pick falls back');
  assert.equal(buttonSize(run({ bigbtn: 1, tinybtn: 1 }, 'big', { bigbtn: 1 })), 'tiny', 'a switched-off pick falls back');
});

test('the shop lists every theme face from the data and never a size', async () => {
  const { FACE_STYLES } = await import('../ui/button-shop.js');
  const { THEME_TASTES } = await import('../ui/unlocks/theme-skins.js');
  const { FACE_HTML } = await import('../ui/button-face-art.js');
  const ids = new Set(FACE_STYLES.map((s) => s.id));
  for (const t of Object.values(THEME_TASTES)) if (t.skin && Object.hasOwn(FACE_HTML, t.skin)) assert.ok(ids.has(t.skin), t.skin);
  assert.ok(!ids.has('big') && !ids.has('tiny'));
  for (const s of FACE_STYLES) assert.ok(s.name && s.hint, s.id);
});
