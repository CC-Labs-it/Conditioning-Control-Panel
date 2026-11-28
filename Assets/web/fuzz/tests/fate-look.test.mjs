// Fate card look in the shop (owner 2026-11-10, handoff-1110/TC-FATE-CARDS-BRIEF.md): pure helpers of ui/fate-look.js.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as E from '../sim/economy.js';
import { BY_ID, FORKS } from '../data/items.js';
import { FATE_SETS, SEEDS, CARD_IDS, FATE_ENDINGS } from '../data/fate.js';
import { FATE_CAPTION, FATE_CSS, FATE_GUIDE_CSS, FATE_STAMP, fateClasses, fateShelf, stampedSeeds, isFate } from '../ui/fate-look.js';

const it = (id) => BY_ID[id];

test('every fate card and ending wears the look; seeds and endings add their own class; other rows none', () => {
  for (const id of CARD_IDS) {
    const c = fateClasses(it(id));
    assert.ok(c.includes('fate'), id);
    assert.equal(c.includes('fate-seed'), SEEDS.includes(id), id);
    assert.ok(!c.includes('fate-end'), id);
  }
  for (const id of FATE_ENDINGS) assert.deepEqual(fateClasses(it(id)), ['fate', 'fate-end'], id);
  for (const id of ['start', 'overlay', 'taste2', 'f5', 'o_emi', 'drop']) {
    if (!it(id)) continue;
    assert.deepEqual(fateClasses(it(id)), [], id);
    assert.equal(isFate(it(id)), false, id);
  }
  assert.deepEqual(fateClasses(null), []);
});

test('the shelf takes every seed out of the goal slot and the rows, in set order, and leaves the rest alone', () => {
  const plain = it('overlay');
  // Journey: the first seed is the goal, the others are rows, an extra rides behind them.
  const win = { goal: it('ft_ticket'), rows: [it('ft_window'), plain, it('go_tx'), it('ft_depth')], more: 0 };
  const out = fateShelf(win, null, win, SEEDS);
  assert.deepEqual(out.seeds.map((x) => x.id), ['ft_window', 'go_tx', 'ft_depth', 'ft_ticket']);
  assert.equal(out.goal, null);
  assert.deepEqual(out.rows.map((x) => x.id), ['overlay']);
  // Classic: a small window cut a seed off; the whole offer still puts all of them on the shelf.
  const small = { goal: null, rows: [it('go_tx'), plain], more: 4 };
  const pool = { goal: null, rows: [it('go_tx'), plain, it('ft_window'), it('ft_depth')] };
  const cut = fateShelf(small, null, pool, SEEDS);
  assert.deepEqual(cut.seeds.map((x) => x.id), ['ft_window', 'go_tx', 'ft_depth']);
  assert.deepEqual(cut.rows.map((x) => x.id), ['overlay']);
  assert.equal(cut.more, 2, 'seeds moved to the shelf no longer count as waiting');
  // After a pick the window is untouched and the shelf empty.
  const after = fateShelf(win, 'outside', win, SEEDS);
  assert.deepEqual(after.seeds, []);
  assert.equal(after.goal, win.goal);
  assert.equal(after.rows, win.rows);
  // No seed on offer: nothing moves.
  const none = { goal: plain, rows: [], more: 0 };
  assert.equal(fateShelf(none, null, none, SEEDS).goal, plain);
});

test('a real run: at Second Theme the shelf holds the open seeds plus the theme seed, nothing else', () => {
  for (const theme of Object.keys(FORKS.f5.opts)) {
    const s = E.createRun();
    s.fuzz = 1e12; s.total = 1e12;
    E.grant(s, 'f4'); s.picks.f4 = 'sell';
    s.picks.f5 = theme;
    E.grant(s, 'taste2');
    const win = E.shopWindow(s, null, 6), pool = E.shopWindow(s, null, 99);
    const out = fateShelf(win, E.fateOf(s), pool, SEEDS);
    const want = FATE_SETS.filter((x) => !x.theme || x.theme === theme).map((x) => x.cards[0]);
    assert.deepEqual(out.seeds.map((x) => x.id), want, theme);
    assert.ok(!out.rows.some((x) => SEEDS.includes(x.id)), theme);
    assert.notEqual(out.goal?.step, 1, theme);
  }
});

test('the stamp: only seeds that were on the shelf, never the bought one, never without a pick', () => {
  const shown = ['ft_window', 'go_tx', 'ft_depth', 'ft_scan'];
  assert.deepEqual(stampedSeeds(shown, [], 'go_tx'), ['ft_window', 'ft_depth', 'ft_scan']);
  assert.deepEqual(stampedSeeds(shown, [], null), []);
  assert.deepEqual(stampedSeeds(shown, ['ft_depth'], 'go_tx'), ['ft_window', 'ft_scan']);
  // After a reload the shelf is empty, so nothing is stamped again.
  assert.deepEqual(stampedSeeds([], [], 'go_tx'), []);
});

test('the card never reads a theme colour or the gold can-afford fill', () => {
  const card = FATE_CSS.split('\n').filter((l) => /\.fate/.test(l) && !/fate-shelf/.test(l)).join('\n');
  for (const v of ['--accent', '--niche', '--hue-', '--fill', '--buy-', '--panel', '--btn', '--ready']) {
    assert.ok(!card.includes('var(' + v), 'fate tile CSS reads ' + v);
    assert.ok(!FATE_GUIDE_CSS.includes('var(' + v), 'fate guide CSS reads ' + v);
  }
  assert.match(FATE_CSS, /#shop \.row\.fate\.buyable/);
  assert.ok(!/[–—!]/.test(FATE_CAPTION + FATE_STAMP), 'copy: no dashes, no exclamation marks');
});
