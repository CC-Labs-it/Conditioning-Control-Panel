import test from 'node:test';
import assert from 'node:assert/strict';
import { BY_ID } from '../data/items.js';
import { freshParts, gateLine, gateOf, gateOpen, markSeen, rowOpen, SHOP_GATES, takeToasts, toastLine } from '../ui/shop-gates.js';

const run = (owned = {}, store = {}, picks = {}) => ({ owned, store, picks });

test('every shop gate names a real item the map sells', () => {
  for (const [key, g] of Object.entries(SHOP_GATES)) {
    assert.ok(g.gate.length, key);
    for (const id of g.gate) assert.ok(BY_ID[id], key + ' -> ' + id);
    assert.match(gateLine(key), /^Unlocks with [A-Z]/);
    assert.ok(['button', 'cursor'].includes(g.shop), key);
  }
});

test('each row waits for its parent power-up', () => {
  assert.equal(gateOf('words', 'editor'), 'words');
  assert.equal(gateOf('wave', 'classic'), null, 'the plain ribbon comes with the door');
  assert.equal(gateOf('wave', 'aurora'), 'wave');
  assert.equal(gateOf('wave', 'chroma'), 'kinetic');
  assert.equal(gateOf('style', 'human-check'), null);
  assert.equal(gateOf('style', 'emi'), 'emi');
  assert.equal(gateOf('cursor', 'cursor_prism'), null);
  assert.equal(gateOf('cursor', 'cursor_nova'), 'nova');
  const fresh = run();
  assert.equal(gateOpen(fresh, 'words'), false, 'trail words need Word Comet');
  assert.equal(gateOpen(run({ trail: 1, starburst: 1 }), 'words'), false);
  assert.equal(gateOpen(run({ word_comet: 1 }), 'words'), true);
  assert.equal(gateOpen(run({ trail: 1 }), 'families'), false, 'families wait for Starburst');
  assert.equal(gateOpen(run({ starburst: 1 }), 'families'), true);
  assert.equal(gateOpen(run({ bubble: 1 }), 'wave'), false);
  assert.equal(gateOpen(run({ a_hum: 1 }), 'wave'), true, 'Low Hum opens the ribbons on the Listen route');
  assert.equal(gateOpen(run({ f3: 1 }), 'wave'), true);
  assert.equal(gateOpen(run({}, {}, { f5: 'bimbo' }), 'emi'), true, 'a fork counts once picked');
  assert.equal(gateLine('words'), 'Unlocks with Word Comet');
  assert.equal(gateLine('wave'), 'Unlocks with Pictures', 'the route-neutral gate is the one named');
});

test('a save that already owns or wears something keeps it', () => {
  assert.equal(gateOpen(run({}, { cursorKit: { wordEditor: true } }), 'words'), true);
  assert.equal(gateOpen(run({}, { cursorKit: { stages: { aurora: 1 } } }), 'families'), true);
  assert.equal(gateOpen(run({}, { cursorKit: { skins: { cursor_nova: true } } }), 'nova'), true);
  assert.equal(gateOpen(run({}, { bubble: { faces: { emi: true } } }), 'emi'), true);
  assert.equal(gateOpen(run({}, { bubble: { waveStyles: { harp: true } } }), 'kinetic'), true);
  assert.equal(gateOpen(run({}, { bubble: { waveStyles: { harp: true } } }), 'wave'), false, 'a kinetic ribbon does not open the plain ones');
  assert.equal(gateOpen(run({}, { bubble: { bouncer: 'dvd' } }), 'copies'), true);
  assert.equal(gateOpen(run({}, { bubble: { bouncer: 'auto' } }), 'copies'), false);
  assert.equal(rowOpen(run(), 'wave', 'loom', true), true, 'an owned row stays usable');
  assert.equal(rowOpen(run(), 'wave', 'loom'), false);
});

test('NEW: a part that opens is fresh until its shop is opened, and says so once', () => {
  const r = run({ bubble: 1 });
  const rec = {};
  assert.deepEqual(freshParts(r, 'button', rec), [], 'a first look counts what is open now as seen');
  r.owned.f5 = 1;
  assert.deepEqual(freshParts(r, 'button', rec), ['emi']);
  assert.deepEqual(takeToasts(r, 'button', rec), ['emi']);
  assert.deepEqual(takeToasts(r, 'button', rec), [], 'one toast only');
  assert.deepEqual(freshParts(r, 'button', rec), ['emi'], 'the door stays NEW until the shop is opened');
  assert.deepEqual(markSeen(r, 'button', rec), ['emi']);
  assert.deepEqual(freshParts(r, 'button', rec), []);
  assert.deepEqual(freshParts(r, 'cursor', rec), []);
  r.owned.cursor = 1;
  assert.deepEqual(freshParts(r, 'cursor', rec), ['cursors']);
  assert.deepEqual(takeToasts(r, 'cursor', rec), [], 'a part that opens with its door gets the tag, not a toast');
  r.owned.word_comet = 1;
  assert.deepEqual(takeToasts(r, 'cursor', rec), ['words']);
  assert.equal(toastLine('cursor', ['words']), 'New in the Cursor Shop: Trail words');
  const old = run({ bubble: 1, f5: 1, cursor: 1, word_comet: 1 });
  assert.deepEqual(freshParts(old, 'cursor', {}), [], 'an old save is not flooded with NEW');
});
