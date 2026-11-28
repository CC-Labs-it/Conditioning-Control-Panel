/* Phone hard caps in the small edition (lane S5, 2026-11-22): bouncing buttons and bouncing words stop at their cap on a
 * phone, a desktop window or the full edition is uncapped, and income is the same with and without the cap. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as Ed from '../ui/edition.js';
import * as E from '../sim/economy.js';
import { PHONE_CAPS } from '../data/editions.js';
import { PHONE_QUERY } from '../ui/phone.js';
import { capPlan, capsFor, wordPlan, markStandIn } from '../ui/phone-caps.js';
import { createLife } from '../sim/save.js';
import { createEngine } from '../ui/engine.js';

const small = Ed.readEdition({ caps: { edition: 'small', platform: 'web' } }, { hostname: 'tc.cclabs.app', hosted: true });
const win = (phone) => ({ matchMedia: (q) => ({ matches: phone && q === PHONE_QUERY }) });
const phone = win(true), desk = win(false);

test('the caps apply to a small run on a phone only', () => {
  assert.deepEqual(capsFor(E.createRun({ ed: small }), phone), PHONE_CAPS);
  assert.equal(capsFor(E.createRun({ ed: small }), desk), null, 'a desktop window is uncapped');
  assert.equal(capsFor(E.createRun(), phone), null, 'the full edition is uncapped');
  assert.deepEqual(PHONE_CAPS, { bouncingButtons: 4, bouncingWords: 1, pictureFrames: 1, adBanners: 1 });
});

test('bouncing buttons stop at four, each drawn one standing for its share', () => {
  assert.deepEqual(capPlan(12, 4), { drawn: 4, times: 3 });
  assert.deepEqual(capPlan(5, 4), { drawn: 4, times: 1.25 });
  assert.deepEqual(capPlan(3, 4), { drawn: 3, times: 1 });
  assert.deepEqual(capPlan(12, null), { drawn: 12, times: 1 }, 'uncapped draws every copy');
  assert.deepEqual(capPlan(0, 4), { drawn: 0, times: 1 });
  for (let n = 1; n <= 12; n++) {
    const p = capPlan(n, 4);
    assert.ok(p.drawn <= 4);
    assert.ok(Math.abs(p.drawn * p.times - n) < 1e-9, `${n}: drawn x times = owned`);
  }
});

test('one bouncing word is drawn of the three a run can own, and it pays for all', () => {
  const owns = (...ids) => (id) => ids.includes(id);
  const all = wordPlan(owns('e_bounce', 'e_words'), 1);
  assert.equal(all.drawn, 1); assert.equal(all.times, 3); assert.equal(all.owned, 3);
  assert.equal(all.draws('e_bounce', 0), true);
  assert.equal(all.draws('e_words', 0), false);
  assert.equal(all.draws('e_words', 1), false);
  const pair = wordPlan(owns('e_words'), 1);
  assert.equal(pair.draws('e_words', 0), true); assert.equal(pair.draws('e_words', 1), false); assert.equal(pair.times, 2);
  const free = wordPlan(owns('e_bounce', 'e_words'), null);
  assert.equal(free.drawn, 3); assert.equal(free.times, 1);
  for (const [id, i] of [['e_bounce', 0], ['e_words', 0], ['e_words', 1]]) assert.equal(free.draws(id, i), true);
  const el = { dataset: {} };
  markStandIn(el, { owned: 3, drawn: 1, first: true });
  assert.equal(el.dataset.standsFor, '×3', 'the drawn one wears the owned count');
  markStandIn(el, { owned: 1, drawn: 1, first: true });
  assert.equal(el.dataset.standsFor, undefined);
});

test('income is the same with and without the cap', () => {
  const ui = () => ({ doc: {}, buttonFor: () => ({}), layer: () => ({}) });
  const run = () => { const s = E.createRun({ ed: small }); E.grant(s, 'stray'); s.owned.stray = 12; E.recalc(s); return s; };
  const free = run(), capped = run();
  const a = createEngine({ run: free, life: createLife(), ui: ui(), registry: {}, prestigeRegistry: {} });
  const b = createEngine({ run: capped, life: createLife(), ui: ui(), registry: {}, prestigeRegistry: {} });
  const plan = capPlan(12, PHONE_CAPS.bouncingButtons);
  for (let k = 0; k < 10; k++) {
    for (let i = 0; i < 12; i++) a.wallHit({ x: 0, y: 0 });
    for (let i = 0; i < plan.drawn; i++) b.wallHit({ x: 0, y: 0, times: plan.times });
  }
  assert.ok(free.total > 0);
  assert.ok(Math.abs(free.total - capped.total) < 1e-6 * free.total, `${free.total} vs ${capped.total}`);
  assert.equal(free.walls, capped.walls);
  assert.ok(Math.abs(free.side.budget - capped.side.budget) < 1e-6 * free.side.budget + 1e-9, 'side money fed the same');
  assert.deepEqual(E.rates(free).fps, E.rates(capped).fps, 'the sim pays for the owned count either way');
});
