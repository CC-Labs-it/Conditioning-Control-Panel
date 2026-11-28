// Owner survey 2026-11-09: EMI stays happy in her sweet spot, old phone saves reopen their theme screens once,
// Toy Link sells on its own.
import test from 'node:test';
import assert from 'node:assert/strict';
import { PET_MOODS, SWEET_RUNG, petStart, petTap } from '../ui/emi-pet.js';
import { REOPEN_KEY, reopenOnce } from '../ui/unlocks/themes-a/theme-screens.js';
import { ridersOf } from '../data/folds.js';

const run = (sweet, gapMs, taps = 20) => {
  let s = petStart(), t = 1000;
  for (let i = 0; i < taps; i++) { s = petTap(s, t, sweet); t += gapMs; }
  return PET_MOODS[s.rung].id;
};

test('in the sweet spot EMI tops out at glee; off it the ladder still climbs to HEY', () => {
  assert.equal(PET_MOODS[SWEET_RUNG].id, 'glee');
  assert.equal(run(true, 182), 'glee', '5.5 taps a second in the zone');
  assert.equal(run(false, 182), 'rage', 'the same pace without the zone flag');
  assert.equal(run(false, 70), 'rage', 'mashing past the zone');
});

test('a sweet press brings an angry EMI back down to glee', () => {
  let s = petStart(), t = 0;
  for (let i = 0; i < 8; i++) { s = petTap(s, t); t += 450; }   // a rung at most every STEP_MS (420)
  assert.equal(PET_MOODS[s.rung].id, 'rage');
  s = petTap(s, t, true);
  assert.equal(PET_MOODS[s.rung].id, 'glee');
});

const store = () => { const m = new Map(); return { get: (k) => m.get(k), set: (k, v) => m.set(k, v) }; };

test('folded theme screens reopen once per save, on phones only, never with No Pictures', () => {
  const phone = store();
  assert.equal(reopenOnce(phone, { width: 412, blind: false }), true);
  assert.equal(phone.get(REOPEN_KEY), true);
  assert.equal(reopenOnce(phone, { width: 412, blind: false }), false, 'only once');
  assert.equal(reopenOnce(store(), { width: 1400, blind: false }), false, 'desktop keeps its layout');
  const blind = store();
  assert.equal(reopenOnce(blind, { width: 412, blind: true }), false);
  assert.equal(blind.get(REOPEN_KEY), undefined, 'a blind run does not spend the reopen');
});

test('Toy Link sells on its own', () => {
  assert.deepEqual(ridersOf('toy'), []);
  for (const id of ['opinions', 'listen', 'remote', 'inbox', 'drain']) assert.ok(!ridersOf(id).includes('toy'), `${id} carries no Toy Link`);
});
