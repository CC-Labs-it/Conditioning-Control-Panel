// Fate cards (owner 2026-11-10, handoff-1110/TC-FATE-CARDS-BRIEF.md): six ending sets, one picked early by its seed.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as E from '../sim/economy.js';
import { ITEMS, BY_ID, FORKS } from '../data/items.js';
import { FATE_SETS, FATE_BY_ID, SEEDS, CARD_IDS, FATE_ENDINGS, setOf, stepOf, setsFor, routeFate } from '../data/fate.js';
import { LEAD_OF, FOLD } from '../data/folds.js';
import { CLAUSES } from '../data/content/clauses.js';
import { ENDINGS } from '../data/content/endings.js';
import { STICKER_BY_ID } from '../data/content/stickers.js';
import { ICON_CATALOG } from '../ui/icon-catalog.js';
import { createJourney, EXTRAS } from '../sim/journey.js';
import { allRoutes, playRoute } from '../sim/route.js';
import { measureJourney } from '../dev/journey-timing.mjs';

const THEMES = Object.keys(FORKS.f5.opts);
const RETIRED = ['tr_end', 'si_end', 'lo_end', 'ce_end', 'in_end', 'p_life', 'drop', 'onemore'];
const rich = (s) => { s.fuzz = 1e12; s.total = 1e12; return s; };
/** A run at Second Theme with the given first theme, rich. */
function atSecondTheme(theme = 'trance', f4 = 'sell') {
  const s = rich(E.createRun());
  E.grant(s, 'f4'); s.picks.f4 = f4;
  s.picks.f5 = theme;
  E.grant(s, 'taste2');
  assert.equal(s.picks.f5, theme);
  return s;
}
const openSeeds = (s) => ITEMS.filter((it) => it.step === 1 && E.isOpen(s, it)).map((it) => it.id);
const openEnds = (s) => E.openItems(s).filter((it) => it.kind === 'end').map((it) => it.id);

test('the contract table: six sets, four cards each, lane fate, steps, parents, one seed price', () => {
  assert.deepEqual(FATE_SETS.map((x) => [x.id, x.end, x.theme]), [
    ['outside', 'outside', null], ['emi', 'go_end', null], ['button', 'o_out', null],
    ['doll', 'bi_end', 'bimbo'], ['unit', 'dr_end', 'drone'], ['museum', 'go_mus', 'goon'],
  ]);
  for (const set of FATE_SETS) {
    set.cards.forEach((id, k) => {
      const it = BY_ID[id];
      assert.ok(it, id);
      assert.equal(it.lane, 'fate', id);
      assert.equal(it.fate, set.id, id);
      assert.equal(it.step, k + 1, id);
      assert.equal(stepOf(id), k + 1, id);
      assert.equal(setOf(id), set.id, id);
      assert.deepEqual(it.req, k ? [set.cards[k - 1]] : ['taste2'], id);
      assert.equal(it.kind, 'item', id);
    });
    const end = BY_ID[set.end];
    assert.equal(end.kind, 'end');
    assert.equal(end.fate, set.id);
    assert.deepEqual(end.req, [set.cards[3]]);
  }
  assert.equal(new Set(SEEDS.map((id) => BY_ID[id].cost)).size, 1, 'every seed costs the same, so price never decides the ending');
  assert.equal(BY_ID.go_mus.name, 'Ending: Gooning Museum');
  assert.equal(BY_ID.go_mus.lane, 'goon');
  assert.ok(!/museum/i.test(SEEDS.map((id) => BY_ID[id].what).join(' ').replace('Museum Ticket', '')), 'seed copy does not name an ending');
  assert.deepEqual([...BY_ID.overlay.any].sort(), [...SEEDS].sort(), 'Spiral Overlay waits for any one seed');
  assert.equal(BY_ID.o_emi.lane, 'sell', 'Sponsored Mascot stays a plain More Ads row');
  assert.ok(!BY_ID.go_pals.fate, 'CCP Community stays the last plain goon row');
  assert.ok(!ITEMS.some((it) => it.req.includes('go_pals')), 'nothing needs CCP Community any more');
});

test('fate cards never fold, and each new id has a clause, an icon, and the museum its ending copy and sticker', () => {
  for (const id of [...CARD_IDS, ...FATE_ENDINGS]) {
    assert.ok(!LEAD_OF[id] && !FOLD[id], `${id} folds`);
    assert.ok(ICON_CATALOG[id], `${id} icon`);
    assert.ok(CLAUSES[id]?.length, `${id} clause`);
  }
  assert.ok(ENDINGS.go_mus && STICKER_BY_ID.e_go_mus);
  for (const id of RETIRED) assert.ok(E.OFF_SHELF.has(id), id);
});

test('seeds arrive together after Second Theme: three open sets, plus the theme set on Bimbo, Drone and Goon', () => {
  for (const theme of THEMES) {
    const s = atSecondTheme(theme);
    const seeds = openSeeds(s);
    const want = setsFor(theme).map((x) => x.cards[0]);
    assert.deepEqual(seeds.sort(), [...want].sort(), theme);
    assert.equal(seeds.length, ['bimbo', 'drone', 'goon'].includes(theme) ? 4 : 3, theme);
  }
  const before = rich(E.createRun());
  E.grant(before, 'glossy');
  assert.deepEqual(openSeeds(before), [], 'nothing before Second Theme');
});

test('Spiral Overlay waits for a seed: no goal and no overlay until one is bought', () => {
  const s = atSecondTheme('trance');
  assert.ok(!E.isOpen(s, BY_ID.overlay));
  assert.equal(E.goal(s), null);
  assert.equal(E.fateOf(s), null);
  assert.equal(E.heldEnding(s), null, 'no silent fallback ending');
  assert.ok(E.buy(s, 'ft_depth').ok);
  assert.ok(E.isOpen(s, BY_ID.overlay));
  assert.equal(E.goal(s).id, 'overlay');
});

test('the first seed locks the set: the other seeds leave, no other set ever opens, and one ending is held', () => {
  for (const set of FATE_SETS) {
    const s = atSecondTheme(set.theme || 'goon');
    assert.ok(E.buy(s, set.cards[0]).ok, set.id);
    assert.equal(E.fateOf(s), set.id);
    assert.equal(E.heldEnding(s), set.end);
    assert.deepEqual(openSeeds(s), [], 'every other seed is gone');
    for (const id of set.cards.slice(1)) assert.ok(E.buy(s, id).ok, id);
    E.grant(s, 'tunnel');
    /* Every other set: even with parents granted by hand, nothing of theirs opens. */
    for (const other of FATE_SETS.filter((x) => x !== set)) {
      for (const id of [...other.cards, other.end]) {
        const it = BY_ID[id];
        if (E.has(s, id)) continue;
        assert.ok(!E.isOpen(s, it), `${set.id} run offers ${id}`);
      }
    }
    assert.deepEqual(openEnds(s), [set.end], set.id);
    assert.equal(E.goal(s).id, set.end, 'the held ending takes the main path\'s last place');
    assert.ok(E.buy(s, set.end).ok, set.end);
    assert.equal(s.ended.id, set.end);
  }
});

test('a theme set never opens on another theme, Second Theme included', () => {
  const s = atSecondTheme('trance');
  s.store.taste2 = { theme: 'bimbo' };
  for (const id of ['ft_bubble', 'ft_scan', 'ft_ticket']) assert.ok(!E.isOpen(s, BY_ID[id]), id);
});

test('retired endings are never offered, even with their old chain owned', () => {
  const s = atSecondTheme('trance', 'premium');
  for (const id of ['tr_deep', 'p_remove', 'go_tx', 'go_will', 'go_corp', 'go_hive', 'tunnel']) E.grant(s, id);
  for (const id of RETIRED) assert.ok(!E.isOpen(s, BY_ID[id]), id);
  assert.deepEqual(openEnds(s), ['go_end']);
});

test('EMI Takeover works on a non-goon theme, and Living Button on the pay-to-remove Ads pick', () => {
  const emi = atSecondTheme('censored', 'premium');
  for (const id of FATE_BY_ID.emi.cards) assert.ok(E.buy(emi, id).ok, id);
  E.grant(emi, 'tunnel');
  assert.deepEqual(openEnds(emi), ['go_end']);
  assert.ok(E.buy(emi, 'go_end').ok);
  const button = atSecondTheme('locked', 'premium');
  assert.equal(button.picks.f4, 'premium');
  for (const id of FATE_BY_ID.button.cards) assert.ok(E.buy(button, id).ok, id);
  E.grant(button, 'tunnel');
  assert.deepEqual(openEnds(button), ['o_out']);
  assert.ok(E.buy(button, 'o_out').ok);
  assert.equal(button.ended.id, 'o_out');
});

test('a plain route never sees two endings at once, and ends on its sampled set', () => {
  const routes = allRoutes().filter((_, i) => i % 23 === 0);
  const seen = new Set();
  for (const p of routes) {
    const fate = routeFate(p);
    seen.add(fate);
    const r = playRoute(p, { side: true });
    assert.equal(r.stuck, false, Object.values(p).join('/'));
    assert.equal(r.state.ended.id, FATE_BY_ID[fate].end, Object.values(p).join('/'));
    assert.equal(E.fateOf(r.state), fate);
    for (const other of FATE_SETS.filter((x) => x.id !== fate)) for (const id of other.cards) assert.ok(!E.has(r.state, id), id);
  }
  assert.ok(seen.size >= 3, [...seen].join());
  /* every set is sampled somewhere among the 192 routes */
  assert.deepEqual(new Set(allRoutes().map(routeFate)), new Set(FATE_SETS.map((x) => x.id)));
});

test('Journey: the seeds share one shelf at one price; the pick rebuilds the plan, which ends on that set alone', () => {
  const route = allRoutes().find((p) => p.f5 === 'drone');
  const s = E.createRun(), shop = createJourney(s);
  let seedShelf = null;
  for (let guard = 0; guard < 2000 && !s.ended; guard++) {
    const sh = shop.shop(2);
    const all = [sh.goal, ...sh.rows].filter((it) => it && !EXTRAS.includes(it.id));
    const ends = all.filter((it) => it.kind === 'end');
    assert.ok(ends.length <= 1, ends.map((it) => it.id).join());
    if (!E.mainPathDone(s)) assert.equal(ends.length, 0, 'no ending before the main path is done');
    const it = all[0];
    if (!it) break;
    if (it.step === 1 && !E.fateOf(s)) {
      seedShelf = all.map((x) => x.id);
      assert.deepEqual([...seedShelf].sort(), setsFor('drone').map((x) => x.cards[0]).sort());
      assert.equal(sh.more, 0, 'every seed fits the smallest shop');
      assert.equal(new Set(seedShelf.map((id) => shop.price(id))).size, 1, 'one price');
      assert.ok(!shop.peek(3).some((x) => x.step === 1));
    }
    const pickId = it.step === 1 && !E.fateOf(s) ? 'ft_scan' : it.id;
    const cost = shop.price(pickId);
    s.fuzz += cost; s.total += cost;
    const res = it.kind === 'fork' ? shop.choose(it.id, route[it.id]) : shop.buy(pickId, it.options?.[0]?.[0]);
    assert.ok(res.ok, `${pickId} ${res.reason}`);
  }
  assert.ok(seedShelf, 'the seeds came');
  assert.equal(s.ended?.id, 'dr_end');
  for (const id of FATE_BY_ID.unit.cards) assert.ok(E.has(s, id), id);
});

test('Journey reference player: seeds right after Second Theme, cards near minutes 45, 60, 75, about 90 minutes', () => {
  for (const set of FATE_SETS) {
    const route = { ...allRoutes().find((p) => p.f5 === (set.theme || 'trance')), fate: set.id };
    const rep = measureJourney(route);
    const at = (id) => { const p = rep.paid.find((x) => x.id === id); assert.ok(p, `${set.id}: ${id} bought`); return p.at / 60; };
    assert.equal(rep.s.ended.id, set.end, set.id);
    assert.ok(rep.minutes > 86 && rep.minutes < 94, `${set.id} ${rep.minutes}`);
    const [m1, m2, m3, m4] = set.cards.map(at);
    assert.ok(m1 > at('taste2') && m1 - at('taste2') < 3, `${set.id} seed at ${m1}`);
    assert.ok(m1 < at('overlay'), 'seed before Spiral Overlay');
    assert.ok(Math.abs(m2 - 45) < 4 && Math.abs(m3 - 60) < 4 && Math.abs(m4 - 75) < 4, `${set.id} cards at ${[m2, m3, m4].map((m) => m.toFixed(1))}`);
    assert.equal(rep.paid.at(-1).id, set.end, 'the ending is the last buy');
  }
});
