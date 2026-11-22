/* The small edition's balance (lane S8, owner target: a 45-minute Journey, give or take 5): one table in data/editions.js,
 * read only for a small run. The full game does not move by a second, the small run keeps an even beat (no wait past
 * twice the full run's longest) and a first five minutes at least as generous, and saves cross both ways. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { measureJourney } from '../dev/journey-timing.mjs';
import { allRoutes } from '../sim/route.js';
import * as Ed from '../ui/edition.js';
import * as E from '../sim/economy.js';
import { createJourney } from '../sim/journey.js';
import { createLife, parse, serialize } from '../sim/save.js';
import { SMALL_BALANCE } from '../data/editions.js';
import { T_RUN } from '../data/prestige.js';

const HOSTED = { hostname: 'tc.cclabs.app', hosted: true };
const small = Ed.readEdition({ caps: { edition: 'small' } }, HOSTED);
const full = Ed.readEdition({ caps: { edition: 'full' } }, HOSTED);
const routes = allRoutes().filter((_, i) => [0, 39, 84, 125, 159, 190].includes(i));
const measure = (ed) => routes.map((r, i) => measureJourney(r, { taps: 4, share: .125, option: i % 2, ed }));
const fullRuns = measure(null), smallRuns = measure(small);

test('one table, read only in small', () => {
  assert.deepEqual(Object.keys(SMALL_BALANCE), ['minutes']);
  assert.ok(Object.isFrozen(SMALL_BALANCE));
  assert.equal(Ed.smallBalance(small), SMALL_BALANCE);
  assert.equal(Ed.smallBalance(full), null);
  assert.equal(Ed.smallBalance(null), null);
});

test('the full Journey stays at 90 minutes, to the tenth', () => {
  for (const r of fullRuns) {
    assert.ok(r.minutes >= 89.85 && r.minutes <= 90.05, `full ${r.minutes.toFixed(2)} min`);
    assert.equal(r.buys, 144);
  }
  assert.equal(T_RUN[0], 90);
});

test('the small Journey reads 45 minutes, give or take 5, and still ends', () => {
  for (const r of smallRuns) {
    assert.ok(r.minutes >= 40 && r.minutes <= 50, `small ${r.minutes.toFixed(1)} min on ${Object.values(r.picks).join('/')}`);
    assert.equal(E.runEdition(r.s)?.edition, 'small');
    assert.ok(r.s.ended, 'an ending is reached');
  }
});

test('the even beat: no small wait past twice the full run\'s longest, and the first five minutes as generous', () => {
  const fullMax = Math.max(...fullRuns.map((r) => r.max));
  for (const r of smallRuns) assert.ok(r.max <= 2 * fullMax, `small wait ${r.max.toFixed(1)} s against full ${fullMax.toFixed(1)} s`);
  const first5 = (r) => r.paid.filter((p) => p.at <= 300).length;
  const fullFirst = Math.min(...fullRuns.map(first5));
  for (const r of smallRuns) assert.ok(first5(r) >= fullFirst, `small buys ${first5(r)} in the first five minutes, full ${fullFirst}`);
});

test('saves cross both ways with the balance on: only what happens next changes', () => {
  /* A full run part-played, then opened in small: the small plan, nothing deleted. */
  const picks = routes[1];
  const fullRun = measureJourney(picks, { taps: 4, share: .125, ed: null }).s;
  const raw = serialize(fullRun, createLife(), {});
  const here = parse(raw, { ed: small });
  assert.equal(E.runEdition(here.run)?.edition, 'small');
  const shop = createJourney(here.run);
  assert.ok(Array.isArray(shop.shop(8).rows), 'the small shelf opens on a full save');
  const back = parse(serialize(here.run, here.life, here.extras));
  for (const [id, n] of Object.entries(fullRun.owned)) assert.equal(back.run.owned[id], n, `${id} kept`);
  assert.deepEqual(back.run.store.journey, fullRun.store.journey, 'the journey state is what the save held');
  /* A small run opened in full plays the full plan from where it stands. */
  const s = E.createRun({ ed: small });
  const j = createJourney(s);
  E.lump(s, 1e6);
  const shelf = j.shop(8);
  const first = [shelf.goal, ...shelf.rows].find((it) => it && it.kind === 'item');
  assert.ok(first && j.buy(first.id).ok);
  const inFull = parse(serialize(s, createLife(), {}));
  assert.equal(E.runEdition(inFull.run), null);
  assert.equal(inFull.run.owned[first.id], s.owned[first.id]);
  const fullShelf = createJourney(inFull.run).shop(8);
  assert.ok([fullShelf.goal, ...fullShelf.rows].some(Boolean), 'the full shelf opens on a small save');
});
