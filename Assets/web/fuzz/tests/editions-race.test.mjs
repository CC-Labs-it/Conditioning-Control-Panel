/* Racing Thoughts in the free edition (owner 2026-11-24: "go put it in the free version too",
 * handoff-1124/tc-small-funnel/RACE-BRIEF.md, lane R). The one row that left the cut list: it is offered, bought and
 * planned in a small run on its catalog requirement (the Pictures fork, which small keeps), and the recap wall's
 * count of what small leaves out stops counting it, because the wall reads SMALL_CUT and nothing else. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as Ed from '../ui/edition.js';
import * as E from '../sim/economy.js';
import { SMALL_CUT, SMALL_CUT_IDS } from '../data/editions.js';
import { BY_ID } from '../data/items.js';
import { wallModel } from '../ui/small-funnel.js';
import { SMALL_WALL_COPY } from '../data/content/small-funnel.js';
import { buildPlan } from '../sim/journey.js';
import { allRoutes } from '../sim/route.js';
import { measureJourney } from '../dev/journey-timing.mjs';

const HOSTED = { hostname: 'tc.cclabs.app', hosted: true };
const small = Ed.readEdition({ caps: { edition: 'small' } }, HOSTED);

test('mini_race is a kept row in small, on its catalog requirement', () => {
  assert.equal(SMALL_CUT_IDS.includes('mini_race'), false);
  assert.equal(Ed.isCut('mini_race', small), false);
  assert.equal(Ed.sleepsHere('mini_race', small), false);
  assert.deepEqual(Ed.reqFor('mini_race', small).req, BY_ID.mini_race.req, 'no hand-down: its parent is kept');
  for (const q of BY_ID.mini_race.req) assert.equal(Ed.isCut(q, small), false, `${q} is kept`);
  const s = E.createRun({ ed: small });
  for (const q of BY_ID.mini_race.req) E.grant(s, q);
  E.lump(s, 1e9);
  assert.ok(E.isOpen(s, BY_ID.mini_race), 'offered once the Pictures fork is taken');
  assert.equal(E.buy(s, 'mini_race').ok, true);
});

test('the small Journey plans the race and a small run buys it', () => {
  const picks = allRoutes()[0];
  assert.ok(buildPlan(picks, 55, 1, undefined, small).some((p) => p.item?.id === 'mini_race'), 'the plan names mini_race');
  const buys = [];
  const r = measureJourney(picks, { taps: 4, share: .125, ed: small, trace: (e) => buys.push(e) });
  assert.ok(r.s.ended);
  assert.ok(buys.some((e) => e.type === 'buy' && e.id === 'mini_race'), 'the reference player buys the race');
});

test('the recap wall no longer counts the race among what small leaves out', () => {
  const arcade = wallModel().parts.find((p) => p.key === 'arcade');
  assert.equal(SMALL_CUT.arcade.includes('mini_race'), false);
  assert.equal(arcade.n, SMALL_CUT.arcade.filter((id) => BY_ID[id]).length);
  assert.equal(arcade.n, 6, 'toy box, breakout, the deep, three dispensers');
  for (const p of wallModel().parts) assert.doesNotMatch(p.label + ' ' + p.tag, /racing|race/i, p.key);
  assert.doesNotMatch(JSON.stringify(SMALL_WALL_COPY), /racing|race/i, 'the wall copy never names it');
});
