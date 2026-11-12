import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as E from '../sim/economy.js';
import { BY_ID, FORKS } from '../data/items.js';
import { createJourney } from '../sim/journey.js';
import { NEXT_MAX, SECRET, ghostsFor, plainPeek, stripKey, trunkAfter } from '../ui/shop-next-model.js';

const ids = (list) => list.map((it) => it.id);
const shelfIds = (w) => [w.goal?.id, ...w.rows.map((it) => it.id)].filter(Boolean);

test('Journey peek lists planned rows after the shelf, each once, and changes nothing', () => {
  const s = E.createRun(), shop = createJourney(s);
  const before = JSON.stringify(s), shelf = shelfIds(shop.shop(6));
  const next = shop.peek(3);
  assert.ok(next.length > 0 && next.length <= 3);
  assert.equal(JSON.stringify(s), before);
  for (const id of ids(next)) assert.ok(!shelf.includes(id), `${id} is already on the shelf`);
  assert.equal(new Set(ids(next)).size, next.length);
  assert.deepEqual(shelfIds(shop.shop(6)), shelf);
});

test('Journey peek stops at the first fork, so nothing past a choice shows', () => {
  const s = E.createRun(), shop = createJourney(s);
  const next = shop.peek(50);
  const at = next.findIndex((it) => it.kind === 'fork');
  assert.ok(at >= 0, 'a fresh run reaches Watch or Listen before 50 rows');
  assert.equal(at, next.length - 1);
});

test('Journey peek moves on as the player buys', () => {
  const s = E.createRun(), shop = createJourney(s);
  E.lump(s, 1e6);
  const first = ids(shop.peek(3));
  for (let n = 0; n < 4; n += 1) { const g = shop.shop(6).goal; if (g.kind === 'fork') break; assert.ok(shop.buy(g.id).ok); }
  const later = ids(shop.peek(3));
  assert.notDeepEqual(later, first);
  for (const id of later) assert.ok(!shelfIds(shop.shop(6)).includes(id));
});

test('ghost tiles: at most three, device rows left out, a fork is a signpost and ends the strip', () => {
  const fork = BY_ID.f1, plain = [BY_ID.stray, BY_ID.rounded, BY_ID.fpsv, BY_ID.title];
  assert.equal(ghostsFor(plain).length, NEXT_MAX);
  const g = ghostsFor([BY_ID.stray, fork, BY_ID.rounded]);
  assert.deepEqual(g.map((x) => x.kind), ['item', 'fork']);
  assert.equal(g[1].key, 'next-fork');
  const noStray = ghostsFor(plain, 3, (it) => it.id !== 'stray');
  assert.ok(!noStray.some((x) => x.item.id === 'stray'));
  assert.equal(noStray.length, 3);
  assert.deepEqual(ghostsFor([]), []);
  assert.equal(stripKey(ghostsFor([BY_ID.stray, BY_ID.drop])), 'stray|next-end');
});

test('the secret tiles never name what is behind them', () => {
  for (const f of Object.keys(FORKS)) {
    for (const opt of Object.values(FORKS[f].opts)) {
      const names = [opt?.name, opt?.label, typeof opt === 'string' ? opt : null].filter(Boolean);
      for (const n of names) assert.ok(!SECRET.fork.what.includes(n) && !SECRET.fork.name.includes(n));
    }
  }
  for (const t of [SECRET.fork.name, SECRET.fork.what, SECRET.end.name, SECRET.end.what]) assert.doesNotMatch(t, /[!–—]/);
});

test('plain shop peek skips what the window shows, then names the main path row after the goal', () => {
  const s = E.createRun();
  const win = E.shopWindow(s, null, 6);
  const next = plainPeek(s, win, 3);
  const shown = shelfIds(win);
  for (const id of ids(next)) assert.ok(!shown.includes(id));
  const after = trunkAfter(s, win.goal.id);
  assert.ok(after, 'the main path goes on after the first goal');
  assert.notEqual(after.id, win.goal.id);
  s.ended = true;
  assert.deepEqual(plainPeek(s, win, 3), []);
});
