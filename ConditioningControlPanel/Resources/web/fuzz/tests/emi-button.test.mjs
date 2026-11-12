import test from 'node:test';
import assert from 'node:assert/strict';
import { PET_GAP_MS, PET_MOODS, STEP_MS, petSettle, petStart, petTap, rungFor } from '../ui/emi-pet.js';
import { FACE_STYLES } from '../ui/button-shop.js';
import { FACE_HTML } from '../ui/button-face-art.js';
import { SKIN_NAMES, wardrobeSkins } from '../ui/button-faces.js';
import { SHOP_GOODS } from '../ui/guide-world-model.js';

const mood = (s) => PET_MOODS[s.rung].id;
function run(gap, taps, start = 1000) {
  let s = petStart(), t = start;
  const seen = [];
  for (let i = 0; i < taps; i++) { s = petTap(s, t); seen.push(mood(s)); t += gap; }
  return { s, seen, t: t - gap };
}

test('EMI is a sold style with real art, a name and a guide stop at the same price', () => {
  const emi = FACE_STYLES.find((s) => s.id === 'emi');
  const human = FACE_STYLES.find((s) => s.id === 'human-check');
  assert.ok(emi.price > human.price, 'priced above Human Check');
  assert.ok(Object.hasOwn(FACE_HTML, 'emi') && FACE_HTML.emi.includes('assets/img/emi/'));
  assert.equal(SKIN_NAMES.emi, 'EMI');
  assert.equal(SHOP_GOODS.find((g) => g.id === 'shop_face_emi').cost, emi.price);
  const own = SHOP_GOODS.find((g) => g.id === 'shop_face_emi').own;
  assert.equal(own({ store: { bubble: {} } }), false);
  assert.equal(own({ store: { bubble: { faces: { emi: true } } } }), true);
  assert.ok(wardrobeSkins({ owned: {}, off: {}, store: {} }, false).includes('emi'), 'equippable once the shop says so');
  assert.doesNotMatch(emi.hint + PET_MOODS.map((m) => m.tag).join(' '), /[!—]/, 'no exclamation marks or em dashes');
});

test('poke ladder: 1-2 pet, 3 glee, 4 annoyed, 5+ shocked, like the desktop EMI', () => {
  assert.deepEqual([0, 1, 2, 3, 4, 5, 9].map(rungFor), [0, 1, 1, 2, 3, 4, 4]);
  assert.deepEqual(run(600, 6).seen, ['pet', 'pet', 'glee', 'annoyed', 'rage', 'rage']);
});

test('a fast clicker sees every mood, one rung per beat, never a per-frame flicker', () => {
  const { seen } = run(60, 30);
  const changes = seen.filter((m, i) => i && m !== seen[i - 1]).length;
  assert.deepEqual([...new Set(seen)], ['pet', 'glee', 'annoyed', 'rage']);
  assert.equal(changes, 3);
  const firstRage = seen.indexOf('rage') * 60;
  assert.ok(firstRage >= 3 * STEP_MS && firstRage < 4 * STEP_MS + 60, String(firstRage));
});

test('slow pets stay happy; a pause settles her back to idle', () => {
  assert.deepEqual([...new Set(run(PET_GAP_MS + 50, 8).seen)], ['pet']);
  const { s, t } = run(100, 20);
  assert.equal(mood(s), 'rage');
  assert.equal(mood(petSettle(s, t + 500)), 'rage', 'still cross right after');
  assert.equal(mood(petSettle(s, t + PET_MOODS[4].calm)), 'idle');
  assert.equal(mood(petTap(petSettle(s, t + 5000), t + 5000)), 'pet', 'a fresh pet starts over');
});
