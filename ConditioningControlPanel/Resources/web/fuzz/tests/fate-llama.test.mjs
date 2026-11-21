// The Panel (owner 2026-11-21, handoff-1121/TC-LLAMA-ROUND-BRIEF.md change 2): an extra open fate card, only for a full
// pen (life.llamas >= 12, the run's `herd`), now opening the Expo walk. Players who never touch the llamas lose nothing.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as E from '../sim/economy.js';
import { BY_ID, FORKS } from '../data/items.js';
import { FATE_SETS, HERD_SETS, HERD_ENDINGS, FATE_BY_ID, SEEDS, setsFor, offeredTo, routeFate, herdOpens } from '../data/fate.js';
import { ENDINGS } from '../data/content/endings.js';
import { TERMS } from '../data/content/terms.js';
import { STICKER_BY_ID } from '../data/content/stickers.js';
import { CLAUSES } from '../data/content/clauses.js';
import { ICON_CATALOG } from '../ui/icon-catalog.js';
import { met, syncLife } from '../sim/stickers.js';
import { createLife, recordEnding } from '../sim/save.js';
import { cardModel } from '../ui/adcard/model.js';
import { herdHtml } from '../ui/adcard/card.js';
import { GROUPS } from '../ui/unlocks/index.js';
import { expoEnding, EXPO_PAPER } from '../ui/unlocks/fate/llama/ending.js';
import { OWED } from '../ui/unlocks/fate/llama/art.js';
import { fateShut } from '../ui/guide-world-model.js';
import { createJourney, EXTRAS } from '../sim/journey.js';
import { allRoutes } from '../sim/route.js';
import { measureJourney } from '../dev/journey-timing.mjs';

const THEMES = Object.keys(FORKS.f5.opts);
const rich = (s) => { s.fuzz = 1e12; s.total = 1e12; return s; };
function atSecondTheme(theme = 'trance', herd = 0) {
  const s = rich(E.createRun({ herd }));
  E.grant(s, 'f4'); s.picks.f4 = 'sell';
  s.picks.f5 = theme;
  E.grant(s, 'taste2');
  return s;
}
const openSeeds = (s) => Object.values(BY_ID).filter((it) => it.step === 1 && E.isOpen(s, it)).map((it) => it.id);
const openEnds = (s) => E.openItems(s).filter((it) => it.kind === 'end').map((it) => it.id);

test('the set: one card, Section 17 word for word, its ending, kept apart from the six', () => {
  assert.equal(FATE_SETS.length, 6, 'the six sets are untouched');
  assert.deepEqual(HERD_SETS.map((x) => [x.id, x.end, x.theme, x.herd, [...x.cards]]), [['llama', 'll_end', null, 12, ['ft_panel']]]);
  const card = BY_ID.ft_panel, end = BY_ID.ll_end;
  assert.equal(card.name, 'Section 17: The Panel');
  assert.equal(card.what, 'Binding arbitration before three (3) llamas. Their decision is final and mostly chewing.');
  assert.deepEqual([card.lane, card.fate, card.step, card.kind], ['fate', 'llama', 1, 'item']);
  assert.deepEqual(card.req, ['taste2']);
  assert.equal(card.cost, BY_ID.ft_window.cost, 'a seed costs what every seed costs');
  assert.deepEqual([end.kind, end.fate, end.lane], ['end', 'llama', 'fate']);
  assert.deepEqual(end.req, ['ft_panel']);
  assert.ok(SEEDS.includes('ft_panel') && BY_ID.overlay.any.includes('ft_panel'), 'Spiral Overlay waits for this seed too');
  for (const id of ['ft_panel', 'll_end']) {
    assert.ok(ICON_CATALOG[id], `${id} icon`);
    assert.ok(CLAUSES[id]?.length, `${id} clause`);
    assert.ok(!/[—–!]/.test(card.name + card.what + card.line + end.name + end.what + end.line + CLAUSES[id].join(' ')), id);
  }
  assert.ok(TERMS.some((t) => t.includes('Their decision is final and mostly chewing.')), 'the card quotes Section 17');
});

test('offered only to a full pen: twelve llamas, on every theme, as an extra open card', () => {
  for (const theme of THEMES) {
    const base = setsFor(theme).map((x) => x.cards[0]).sort();
    assert.deepEqual(openSeeds(atSecondTheme(theme)).sort(), base, `${theme}: no herd, no Panel`);
    assert.deepEqual(openSeeds(atSecondTheme(theme, 11)).sort(), base, `${theme}: eleven is not enough`);
    assert.deepEqual(openSeeds(atSecondTheme(theme, 12)).sort(), [...base, 'ft_panel'].sort(), `${theme}: twelve adds it`);
  }
  assert.ok(!herdOpens(FATE_BY_ID.llama, 11) && herdOpens(FATE_BY_ID.llama, 12) && herdOpens(FATE_BY_ID.outside, 0));
  assert.ok(!offeredTo('llama', { f5: 'goon' }) && offeredTo('llama', { f5: 'goon' }, 12));
  /* the route harnesses never sample it, so the 192 route timings stay as they were */
  assert.ok(!allRoutes().some((p) => routeFate(p) === 'llama'));
});

test('picking it holds The Panel: the other seeds leave, no card 2 to 4, one ending, and it ends the run', () => {
  const s = atSecondTheme('bimbo', 12);
  assert.ok(E.buy(s, 'ft_panel').ok);
  assert.equal(E.fateOf(s), 'llama');
  assert.equal(E.heldEnding(s), 'll_end');
  assert.deepEqual(openSeeds(s), []);
  assert.ok(E.isOpen(s, BY_ID.overlay), 'the main path goes on');
  assert.ok(!E.isOpen(s, BY_ID.ll_end), 'not before the main path is done');
  E.grant(s, 'tunnel');
  assert.deepEqual(openEnds(s), ['ll_end']);
  assert.equal(E.goal(s).id, 'll_end');
  assert.ok(E.buy(s, 'll_end').ok);
  assert.equal(s.ended.id, 'll_end');
});

test('a run that picked it keeps it, whatever the herd reads later (an old save has none)', () => {
  const s = atSecondTheme('trance', 12);
  assert.ok(E.buy(s, 'ft_panel').ok);
  delete s.herd;
  E.grant(s, 'tunnel');
  assert.deepEqual(openEnds(s), ['ll_end']);
  /* and a herd run that took another card never sees it */
  const t = atSecondTheme('trance', 12);
  assert.ok(E.buy(t, 'ft_window').ok);
  E.grant(t, 'tunnel');
  assert.ok(!E.isOpen(t, BY_ID.ft_panel) && !E.isOpen(t, BY_ID.ll_end));
});

test('Journey: the Panel seed shares the seed shelf at the seed price; the plan ends on it', () => {
  const route = allRoutes().find((p) => p.f5 === 'trance');
  const s = E.createRun({ herd: 12 }), shop = createJourney(s);
  let seedShelf = null;
  for (let guard = 0; guard < 2000 && !s.ended; guard++) {
    const sh = shop.shop(2);
    const all = [sh.goal, ...sh.rows].filter((it) => it && !EXTRAS.includes(it.id));
    const it = all[0];
    if (!it) break;
    if (it.step === 1 && !E.fateOf(s)) {
      seedShelf = all.map((x) => x.id);
      assert.ok(seedShelf.includes('ft_panel'), seedShelf.join());
      assert.equal(new Set(seedShelf.map((id) => shop.price(id))).size, 1, 'one price');
    }
    const pickId = it.step === 1 && !E.fateOf(s) ? 'ft_panel' : it.id;
    const cost = shop.price(pickId);
    s.fuzz += cost; s.total += cost;
    const res = it.kind === 'fork' ? shop.choose(it.id, route[it.id]) : shop.buy(pickId, it.options?.[0]?.[0]);
    assert.ok(res.ok, `${pickId} ${res.reason}`);
  }
  assert.ok(seedShelf, 'the seeds came');
  assert.equal(s.ended?.id, 'll_end');
});

test('Journey reference player on The Panel: seed right after Second Theme, about 90 minutes, the ending last', () => {
  const route = { ...allRoutes().find((p) => p.f5 === 'drone'), fate: 'llama' };
  const rep = measureJourney(route, { herd: 12 });
  assert.equal(rep.s.ended.id, 'll_end');
  assert.ok(rep.minutes > 86 && rep.minutes < 94, `${rep.minutes}`);
  const at = (id) => rep.paid.find((x) => x.id === id).at / 60;
  assert.ok(at('ft_panel') > at('taste2') && at('ft_panel') - at('taste2') < 3, `seed at ${at('ft_panel')}`);
  assert.equal(rep.paid.at(-1).id, 'll_end');
});

test('the Expo keeps the ending identity and sticker, with no forced wall-clock exit', () => {
  const e = ENDINGS.ll_end;
  assert.ok(!e.link, 'no link out: the llamas are not a product');
  const group = GROUPS['fate-llama'];
  assert.deepEqual(Object.keys(group).sort(), ['ft_panel', 'll_end']);
  assert.equal(group.ll_end.ending, expoEnding);
  assert.equal(group.ll_end.fadeColour, EXPO_PAPER);
  assert.equal(group.ll_end.showMaxMs, null, 'the visitor decides when to leave');
  assert.ok(OWED.length >= 4 && OWED.every((o) => o.file && o.size && o.what && /^real/.test(o.now)), 'every owed picture is real now');

  const st = STICKER_BY_ID.e_ll_end;
  assert.ok(st && st.hidden && st.page === 'endings' && st.when === 'ending:ll_end' && st.scope === 'life');
  const life = createLife();
  life.llamas = 12;
  assert.ok(!met(st, {}, life));
  recordEnding(life, E.createRun({ herd: 12 }), 'll_end');
  assert.ok(life.stickers.e_ll_end, 'the sticker is in the book before the show starts');
  assert.ok(HERD_ENDINGS.includes('ll_end'));
});

test('Every Ending never needs The Panel', () => {
  const life = createLife();
  for (const set of FATE_SETS) life.endings[set.end] = 1;
  syncLife(life);
  assert.ok(life.stickers.e_all, 'the six on offer are every ending');
  const other = createLife();
  for (const set of FATE_SETS.slice(1)) other.endings[set.end] = 1;
  other.endings.ll_end = 1;
  syncLife(other);
  assert.ok(!other.stickers.e_all, 'five and The Panel are not');
});

test('the chair joins the Living Ad card: this run, and every card after', () => {
  const run = { picks: { f5: 'goon' }, seed: 7, total: 10, clock: 60000, store: {} };
  assert.equal(cardModel({ run, life: { llamas: 12 } }).chair, false);
  assert.equal(cardModel({ run: { ...run, ended: { id: 'll_end' } }, life: { llamas: 12 } }).chair, true);
  const later = cardModel({ run, life: { llamas: 12, endings: { ll_end: 1 } } });
  assert.equal(later.chair, true);
  const html = herdHtml(later);
  assert.equal((html.match(/\u{1F999}/gu) || []).length, 11, 'the chair is one of the herd, not an extra');
  assert.ok(html.startsWith('<span class="chair"'));
  assert.match(html, /^<span class="chair"[^>]*><img src="[^"]*assets\/adcard\/chair\.webp"/, 'the chair is Sir Reginald, round');
  assert.ok(!herdHtml({ llamas: 3, chair: false }).includes('chair'));
});

test('the guide map shows the card shut without a full pen', () => {
  const s = atSecondTheme('trance');
  assert.ok(fateShut(s, BY_ID.ft_panel));
  const t = atSecondTheme('trance', 12);
  assert.ok(!fateShut(t, BY_ID.ft_panel));
  assert.ok(E.buy(t, 'ft_panel').ok);
  assert.ok(fateShut(t, BY_ID.ft_window) && !fateShut(t, BY_ID.ll_end));
});
