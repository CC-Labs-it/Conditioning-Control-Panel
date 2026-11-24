/* The small edition holds ONE ending (owner 2026-11-24, the funnel, handoff-1124/tc-small-funnel/BRIEF.md): only the
 * fate set data/editions.js SMALL_ENDING_SET (Go Outside) opens; the other sets a full run would be offered are LOCKED,
 * listed by sim/economy.js teasedSets, and sleep when a full save already owns them. The full edition is untouched. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as E from '../sim/economy.js';
import * as Ed from '../ui/edition.js';
import { ITEMS, BY_ID, FORKS } from '../data/items.js';
import { ALL_SETS, FATE_SETS, SEEDS, CARD_IDS, FATE_ENDINGS, setOf } from '../data/fate.js';
import { SMALL_ENDING_SET, SMALL_CUT_IDS } from '../data/editions.js';
import { allRoutes, playRoute } from '../sim/route.js';
import { createJourney } from '../sim/journey.js';
import { measureJourney } from '../dev/journey-timing.mjs';
import { createLife, parse, serialize, sleepsSomething } from '../sim/save.js';

const HOSTED = { hostname: 'tc.cclabs.app', hosted: true };
const small = Ed.readEdition({ caps: { edition: 'small' } }, HOSTED);
const full = Ed.readEdition({ caps: { edition: 'full' } }, HOSTED);
const THEMES = Object.keys(FORKS.f5.opts);
const rich = (s) => { s.fuzz = 1e12; s.total = 1e12; return s; };
/** A run past Pick a Theme with this first theme, at the seed shelf, rich. */
function atSeeds(ed, theme = 'trance', herd = 0) {
  const s = rich(E.createRun({ ed, herd }));
  E.grant(s, 'f4'); s.picks.f5 = theme;
  E.grant(s, 'taste2');
  assert.equal(s.picks.f5, theme);
  return s;
}
const openSeeds = (s) => ITEMS.filter((it) => it.step === 1 && E.isOpen(s, it)).map((it) => it.id);
const LOCKED_ROWS = [...CARD_IDS, ...FATE_ENDINGS].filter((id) => setOf(id) !== SMALL_ENDING_SET);

test('the rule: only Go Outside is open in small, every other fate row is locked, and the cut list does not move', () => {
  assert.equal(SMALL_ENDING_SET, 'outside');
  assert.equal(SMALL_CUT_IDS.length, 56, 'the 56 cut rows stay cut, no more');
  for (const set of ALL_SETS) {
    assert.equal(Ed.endingSetOpen(set.id, small), set.id === 'outside', set.id);
    assert.equal(Ed.endingSetOpen(set.id, full), true, set.id);
  }
  for (const id of LOCKED_ROWS) {
    assert.equal(Ed.fateLocked(id, small), true, id);
    assert.equal(Ed.sleepsHere(id, small), true, id);
    assert.equal(Ed.fateLocked(id, full), false, id);
    assert.equal(Ed.sleepsHere(id, full), false, id);
  }
  for (const id of [...FATE_SETS[0].cards, 'outside', 'start', 'taste2', 'overlay']) {
    assert.equal(Ed.fateLocked(id, small), false, id);
    assert.equal(Ed.sleepsHere(id, small), false, id);
  }
  /* A locked row is not a cut row: lists keep showing it (locked, by name); only ft_panel and ll_end are both. */
  assert.deepEqual(LOCKED_ROWS.filter((id) => Ed.isCut(id, small)).sort(), ['ft_panel', 'll_end']);
  for (const id of SMALL_CUT_IDS) assert.equal(Ed.sleepsHere(id, small), true, id);
  /* Spiral Overlay waits for a seed: in small only Go Outside's seed can open it. */
  assert.deepEqual(Ed.reqFor('overlay', small).any, ['ft_window']);
  assert.deepEqual([...Ed.reqFor('overlay', full).any].sort(), [...SEEDS].sort());
});

test('in small only Go Outside\'s seed is ever offered, on every theme, and nothing locked is bought or granted', () => {
  for (const theme of THEMES) {
    const s = atSeeds(small, theme, 12);
    assert.deepEqual(openSeeds(s), ['ft_window'], theme);
    assert.equal(E.fateOf(s), null);
    for (const id of LOCKED_ROWS) {
      assert.equal(E.isOpen(s, BY_ID[id]), false, `${id} on ${theme}`);
      assert.equal(E.buy(s, id).ok, false, `${id} bought on ${theme}`);
      assert.deepEqual(E.grant(s, id), [], `${id} granted on ${theme}`);
      assert.equal(s.owned[id], undefined, id);
    }
    /* The dev door to the ending grants Go Outside's chain, and the run holds Go Outside. */
    E.grant(s, 'outside');
    assert.equal(E.fateOf(s), 'outside');
    assert.equal(E.heldEnding(s), 'outside');
    assert.equal(s.owned.outside, 1);
  }
  /* Spiral Overlay's grant picks Go Outside's seed in small. */
  const s = rich(E.createRun({ ed: small }));
  E.grant(s, 'overlay');
  assert.equal(s.owned.ft_window, 1);
  assert.ok(!SEEDS.some((id) => id !== 'ft_window' && s.owned[id]), 'no other seed granted');
});

test('the full edition still offers every set it always did', () => {
  for (const theme of THEMES) {
    const s = atSeeds(null, theme);
    const want = ['ft_window', 'go_tx', 'ft_depth', ...FATE_SETS.filter((x) => x.theme === theme).map((x) => x.cards[0])];
    assert.deepEqual(openSeeds(s).sort(), want.sort(), theme);
    const f = atSeeds(full, theme);
    assert.deepEqual(openSeeds(f).sort(), want.sort(), `${theme} with a full reading`);
  }
});

test('teasedSets lists what a full run would offer and small keeps locked, from the data', () => {
  const fresh = E.createRun({ ed: small });
  assert.deepEqual(E.teasedSets(fresh), [
    { id: 'emi', end: 'go_end', seed: 'go_tx' },
    { id: 'button', end: 'o_out', seed: 'ft_depth' },
  ], 'before Pick a Theme: the two open sets');
  for (const theme of THEMES) {
    const s = atSeeds(small, theme, 12);
    const want = FATE_SETS.filter((x) => x.id !== 'outside' && (!x.theme || x.theme === theme)).map((x) => ({ id: x.id, end: x.end, seed: x.cards[0] }));
    const got = E.teasedSets(s);
    assert.deepEqual(got, want, theme);
    for (const t of got) {
      assert.ok(Object.isFrozen(t), 'frozen');
      assert.equal(BY_ID[t.end]?.kind, 'end', t.end);
      assert.equal(E.isOpen(s, BY_ID[t.seed]), false, `${t.seed} is locked, not offered`);
    }
    assert.ok(!got.some((t) => t.id === 'llama'), 'the Expo is a cut part, never a teased ending, even with 12 llamas');
  }
  assert.equal(E.teasedSets(atSeeds(small, 'bimbo')).at(-1).end, 'bi_end');
  /* Full, or no reading at all: nothing is teased. An explicit reading wins over the run's own. */
  assert.deepEqual(E.teasedSets(atSeeds(null, 'drone')), []);
  assert.deepEqual(E.teasedSets(atSeeds(full, 'drone')), []);
  assert.deepEqual(E.teasedSets(atSeeds(null, 'drone'), small).map((t) => t.id), ['emi', 'button', 'unit']);
  assert.deepEqual(E.teasedSets(atSeeds(small, 'drone'), full), []);
  assert.deepEqual(E.teasedSets(null, small).map((t) => t.id), ['emi', 'button']);
});

test('lists keep a locked fate row (asleep = cut only); the run rules sleep it (sleeps)', () => {
  const s = E.createRun({ ed: small });
  for (const id of ['go_tx', 'go_end', 'bi_end', 'o_out']) {
    assert.equal(E.asleep(s, id), false, `${id} stays in lists`);
    assert.equal(E.sleeps(s, id), true, `${id} sleeps`);
  }
  assert.equal(E.sleeps(s, 'outside'), false);
  assert.equal(E.sleeps(s, 'll_end'), true);
  assert.equal(E.asleep(s, 'll_end'), true);
  const f = E.createRun({ ed: full });
  assert.equal(E.sleeps(f, 'go_end'), false);
  /* The endings gallery still lists the locked endings as goals: the page draws them locked. */
  const ends = ['outside', 'go_end', 'o_out', 'll_end'];
  assert.deepEqual(E.galleryIds(ends, new Set(), s), ['outside', 'go_end', 'o_out']);
});

test('every small route and the small Journey end on Go Outside, whatever fate the route samples', () => {
  const all = allRoutes();
  for (const i of [0, 7, 39, 84, 101, 125, 159, 190]) {
    const r = playRoute(all[i], { side: true, ed: small });
    assert.equal(r.stuck, false, `route ${i} stuck`);
    assert.equal(r.fate, 'outside', `route ${i}`);
    assert.equal(r.state.ended?.id, 'outside', `route ${i} ended on ${r.state.ended?.id}`);
    assert.ok(!LOCKED_ROWS.some((id) => r.state.owned[id]), `route ${i} owns no locked row`);
    /* An explicit locked fate is still Go Outside in small, and plays as asked in full. */
    assert.equal(playRoute(all[i], { side: true, ed: small, fate: 'emi', maxS: 1 }).fate, 'outside');
  }
  assert.equal(playRoute(all[0], { side: true, fate: 'emi' }).state.ended?.id, 'go_end', 'full plays the fate it is given');
  for (const [k, i] of [0, 39, 84, 125, 159, 190].entries()) {
    const r = measureJourney(all[i], { taps: 4, share: .125, option: k % 2, ed: small });
    assert.equal(r.s.ended?.id, 'outside', `journey ${i}`);
    assert.ok(!LOCKED_ROWS.some((id) => r.s.owned[id]), `journey ${i} owns no locked row`);
  }
});

test('the small Journey shelf offers only Go Outside\'s seed, and its plan is Go Outside\'s', () => {
  const s = atSeeds(small, 'goon');
  const shop = createJourney(s);
  for (let n = 0; n < 400 && !E.fateOf(s); n++) {
    const shelf = shop.shop(8);
    const rows = [shelf.goal, ...shelf.rows].filter(Boolean);
    const seeds = rows.filter((it) => it.step === 1 && it.fate);
    if (seeds.length) {
      assert.deepEqual(seeds.map((it) => it.id), ['ft_window']);
      assert.ok(shop.buy('ft_window').ok);
      break;
    }
    const it = rows.find((x) => x.kind !== 'fork') || rows[0];
    const res = it.kind === 'fork' ? shop.choose(it.id, s.picks[it.id] || Object.keys(FORKS[it.id].opts)[0]) : shop.buy(it.id, it.options?.[0]?.[0]);
    assert.ok(res.ok, `bought ${it.id}`);
  }
  assert.equal(E.fateOf(s), 'outside');
  assert.equal(E.heldEnding(s), 'outside');
});

test('SLEEP: a full save that picked another set falls to Go Outside in small, and keeps every row for full', () => {
  /* A full run that picked EMI Takeover and bought two of its cards. */
  const f = atSeeds(null, 'bimbo');
  E.grant(f, 'go_will');
  assert.equal(E.fateOf(f), 'emi');
  const raw = serialize(f, createLife(), {});
  const here = parse(raw, { ed: small });
  const s = here.run;
  assert.equal(sleepsSomething(here, small), true, 'the save-sleep note has something to say');
  assert.equal(sleepsSomething(parse(raw, { ed: full }), full), false);
  assert.equal(s.owned.go_tx, 1, 'kept');
  assert.equal(s.owned.go_will, 1, 'kept');
  assert.equal(E.has(s, 'go_tx'), false, 'asleep');
  assert.equal(E.count(s, 'go_will'), 0, 'asleep');
  assert.equal(E.fateOf(s), null, 'the sleeping seed picks nothing');
  assert.equal(E.heldEnding(s), null);
  rich(s);
  assert.deepEqual(openSeeds(s), ['ft_window'], 'the run falls to Go Outside');
  assert.equal(E.isOpen(s, BY_ID.go_corp), false, 'the sleeping set never opens its next card');
  assert.ok(E.buy(s, 'ft_window').ok);
  assert.equal(E.heldEnding(s), 'outside');
  E.grant(s, 'outside');
  assert.equal(E.has(s, 'outside'), true, 'Go Outside is reached');
  /* Back in full: nothing was deleted. The first seed bought still names the run's fate there (go_tx came first). */
  const back = parse(serialize(s, here.life, here.extras), { ed: full });
  for (const id of ['go_tx', 'go_will', 'ft_window']) assert.equal(back.run.owned[id], 1, `${id} kept`);
  assert.equal(E.fateOf(back.run), 'emi');
});

test('SLEEP: a full run that ended on a locked ending loads in small as a plain ended run', () => {
  const f = atSeeds(null, 'trance');
  E.grant(f, 'o_out');
  f.ended = { id: 'o_out', at: 1000 };
  const here = parse(serialize(f, createLife(), {}), { ed: small });
  assert.equal(here.run.ended?.id, 'o_out', 'the ending is kept');
  assert.equal(E.sleepingEnd(here.run), true, 'shown plain, left only by the player\'s own New run');
  const back = parse(serialize(here.run, here.life, here.extras), { ed: full });
  assert.equal(E.sleepingEnd(back.run), false, 'awake again in full');
  assert.equal(back.run.ended?.id, 'o_out');
  /* Go Outside itself is never a sleeping ending in small. */
  const g = atSeeds(small, 'trance');
  E.grant(g, 'outside');
  g.ended = { id: 'outside', at: 1000 };
  assert.equal(E.sleepingEnd(g), false);
});
