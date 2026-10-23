import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createScreenDeal } from '../screen-deal.js';
import { screenTransition } from '../screen-transition.js';

// ccp-bugs #1451: the same GIF on several Back Room screens at once while other dealt pictures sat unused.
// The room below follows room/screens.js: each screen holds one face per turn (its turn index plus the
// half-way step of the static), a wall shows 1 to 3 panels by the first picture's shape, the ceiling
// projector shows six tiles.
const ASPECT = 2.12 / 1.22;
const panelsFor = (ratio, pool) => (ratio < ASPECT * .8 ? Math.min(pool, 3, Math.max(2, Math.round(ASPECT / ratio))) : 1);

function room(screens, ratios, deal = createScreenDeal(ratios.length)) {
  return (t) => {
    const shown = [];
    screens.forEach((s, i) => { if (s.up === false) deal.release(i); });
    screens.forEach((s, i) => {
      if (s.up === false) return;
      const state = screenTransition(t, s.turn || 9, false, ratios.length, i);
      const need = (k) => panelsFor(ratios[k], ratios.length);
      const face = deal.hold(i, state.index + state.mix, s.grid ? 6 : need);
      const items = face.slice(0, s.grid ? 6 : need(face[0]));
      shown.push(...items);
      s.last = items;
    });
    return shown;
  };
}

const walls = (n) => Array.from({ length: n }, () => ({}));
const WIDE = 16 / 9, SQUARE = 1, TALL = 9 / 16;

test('screens up at once never share a picture while the deal has a free one (#1451)', () => {
  // Eight dealt pictures, one square (two panels): the old (turn + i) rule put that square picture's
  // second panel on the neighbouring screen, so two walls showed the same GIF.
  const ratios = [SQUARE, WIDE, WIDE, WIDE, WIDE, WIDE, WIDE, WIDE];
  const tick = room(walls(4), ratios);
  const seen = new Set();
  for (let t = 0; t < 400; t += .05) {
    const shown = tick(t);
    assert.equal(new Set(shown).size, shown.length, `a picture twice at ${t.toFixed(2)} s: ${shown}`);
    shown.forEach((k) => seen.add(k));
  }
  assert.equal(seen.size, ratios.length, 'every dealt picture reaches a screen');
});

test('portrait pictures fill their panels with pictures no other screen is showing', () => {
  const ratios = [TALL, WIDE, WIDE, WIDE, WIDE, WIDE, WIDE, WIDE];
  const tick = room(walls(2), ratios);
  for (let t = 0; t < 300; t += .05) {
    const shown = tick(t);
    assert.equal(new Set(shown).size, shown.length, `a picture twice at ${t.toFixed(2)} s: ${shown}`);
  }
});

test('every screen still turns to a new picture when the deal is no bigger than the room', () => {
  const screens = walls(4), tick = room(screens, [WIDE, WIDE, WIDE, WIDE]);
  const before = screens.map(() => null), turns = screens.map(() => 0);
  let face = screens.map(() => null);
  for (let t = 0; t < 200; t += .05) {
    tick(t);
    screens.forEach((s, i) => {
      const state = screenTransition(t, 9, false, 4, i), now = state.index + state.mix;
      if (face[i] !== null && now !== face[i]) { assert.notEqual(s.last[0], before[i], `screen ${i} kept its picture`); turns[i]++; }
      face[i] = now; before[i] = s.last[0];
    });
  }
  assert.ok(turns.every((n) => n >= 20), 'screens turned: ' + turns);
});

test('a room with more screens than pictures spreads them evenly', () => {
  // Four walls, both extra packs (four and six) and the ceiling projector's six tiles: 20 places, 8 pictures.
  const screens = [...walls(14), { grid: true, turn: 4.5 }];
  const ratios = Array(8).fill(WIDE), tick = room(screens, ratios);
  for (let t = 0; t < 300; t += .05) {
    const counts = Array(8).fill(0);
    tick(t).forEach((k) => counts[k]++);
    // 20 places over 8 pictures is 2 or 3 each. A face that turns has to leave its own pictures (the
    // projector leaves six at once), so until the next screens turn a share may sit further off that,
    // but no picture is ever left out while another shows twice.
    assert.ok(Math.min(...counts) >= 1 && Math.max(...counts) - Math.min(...counts) <= 3,
      `uneven at ${t.toFixed(2)} s: ${counts}`);
  }
});

test('a screen that goes dark gives its pictures back to the screens still up', () => {
  const screens = walls(8), ratios = Array(6).fill(WIDE), tick = room(screens, ratios);
  const crowded = tick(0);
  assert.equal(crowded.length, 8);
  for (const s of screens.slice(4)) s.up = false;
  for (let t = 0; t < 120; t += .05) {
    const shown = tick(t);
    assert.equal(shown.length, 4);
    assert.equal(new Set(shown).size, 4, `a picture twice at ${t.toFixed(2)} s: ${shown}`);
  }
});

test('the same turn keeps its pictures; a panel count that grows keeps the first one', () => {
  const deal = createScreenDeal(8);
  const first = deal.hold(0, 3, 1).slice();
  assert.deepEqual(deal.hold(0, 3, 1), first);
  const grown = deal.hold(0, 3, () => 3);
  assert.equal(grown.length, 3);
  assert.equal(grown[0], first[0]);
  assert.equal(new Set(grown).size, 3);
  assert.equal(deal.hold(0, 3, 2).length, 2);
  assert.equal(deal.counts().reduce((a, b) => a + b, 0), 2);
});

test('a new gallery drops every hold; an empty one holds nothing', () => {
  const deal = createScreenDeal(4);
  deal.hold(0, 0, 2); deal.hold(1, 0, 2);
  deal.reset(3);
  assert.deepEqual(deal.counts(), [0, 0, 0]);
  assert.equal(deal.hold(0, 0, 1)[0] < 3, true);
  deal.reset(0);
  assert.deepEqual(deal.hold(0, 0, 3), []);
  // A face bigger than the deal repeats rather than leaving a tile empty.
  const small = createScreenDeal(2);
  const tiles = small.hold(0, 0, 6);
  assert.equal(tiles.length, 6);
  assert.deepEqual([...new Set(tiles)].sort(), [0, 1]);
});
