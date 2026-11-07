import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createCounterDoor, createStampQueue, newlyOwned, stampSteps, COUNTER_TAB, STAMP_GAP_MS } from '../counter-door.js';

const snap = (owned, extra = {}) => ({ owned, racing: false, tracks: [], demo: false, ...extra });

test('the door asks the host for the counter window on the Back Room tab, once, and only on a host that has it', () => {
  const sent = []; let listener = null, holds = 0, enabled = true;
  const door = createCounterDoor({ send: (m) => sent.push(m), on: (type, fn) => { assert.equal(type, 'owned'); listener = fn; return () => { listener = null; }; },
    enabled: () => enabled, onOpen: () => holds++ });
  assert.equal(door.open(), true);
  assert.deepEqual(sent, [{ type: 'open-prize-counter', tab: COUNTER_TAB, from: 'backroom' }]);
  assert.equal(COUNTER_TAB, 'backroom');
  assert.equal(door.isOpen, true);
  assert.equal(door.open(), false, 'a second E while the window is up asks nothing');
  assert.equal(holds, 1);
  listener({ type: 'owned', grants: ['cos.x'] });
  assert.equal(door.isOpen, false);
  enabled = false;
  assert.equal(door.available(), false);
  assert.equal(door.open(), false, 'a host without the counter keeps the 2D station');
  door.dispose();
  assert.equal(listener, null);
});

test('the close comes back once with the grants frame, and a stray owned frame is still handed on', () => {
  let listener; const back = [];
  const door = createCounterDoor({ send: () => {}, on: (_t, fn) => { listener = fn; }, enabled: () => true, onReturn: (m, was) => back.push([m.grants.length, was]) });
  door.open();
  listener({ type: 'owned', grants: ['a', 'b'] });
  listener({ type: 'owned', grants: [] });
  assert.deepEqual(back, [[2, true], [0, false]]);
});

test('newly owned rows come out in shelf order, each once', () => {
  assert.deepEqual(newlyOwned(snap(['jackpot_remix']), snap(['bubbles_v2', 'jackpot_remix', 'high_roller', 'high_roller'])), ['high_roller', 'bubbles_v2']);
  assert.deepEqual(newlyOwned(null, snap(['flashes_v2'])), ['flashes_v2']);
  assert.deepEqual(newlyOwned(snap(['a']), snap(['a'])), []);
});

test('each stamp step owns one more fresh row; nothing new is one plain refresh', () => {
  const steps = stampSteps(snap(['jackpot_remix']), snap(['jackpot_remix', 'bubbles_v2', 'high_roller'], { racing: true }));
  assert.deepEqual(steps.map((s) => s.bought), ['high_roller', 'bubbles_v2']);
  assert.deepEqual(steps[0].snapshot.owned, ['jackpot_remix', 'high_roller']);
  assert.deepEqual(steps[1].snapshot.owned, ['jackpot_remix', 'high_roller', 'bubbles_v2']);
  assert.equal(steps[1].snapshot.racing, true);
  assert.deepEqual(stampSteps(snap(['a']), snap(['a'])), [{ snapshot: snap(['a']), bought: null }]);
  assert.deepEqual(stampSteps(snap([]), null), []);
});

test('the queue lands stamps a flight apart; a newer play drops the rest; still motion lands them at once', async () => {
  const applied = [], waits = [];
  let release;
  const q = createStampQueue({ apply: (s, b) => applied.push(b), wait: (ms) => { waits.push(ms); return new Promise((r) => { release = r; }); } });
  const first = q.play(snap([]), snap(['jackpot_remix', 'high_roller', 'flashes_v2']));
  assert.deepEqual(applied, ['jackpot_remix']);
  assert.deepEqual(waits, [STAMP_GAP_MS]);
  const second = q.play(snap([]), snap(['bubbles_v2']));
  release();
  assert.equal(await first, false, 'the older run stops');
  assert.equal(await second, true);
  assert.deepEqual(applied, ['jackpot_remix', 'bubbles_v2']);

  const still = [];
  const q2 = createStampQueue({ apply: (s, b) => still.push(b), still: () => true, wait: () => assert.fail('still never waits') });
  assert.equal(await q2.play(snap([]), snap(['jackpot_remix', 'high_roller'])), true);
  assert.deepEqual(still, ['jackpot_remix', 'high_roller']);
});

test('the room wires the door: the counter row opens the window, the 2D station stays for other hosts', () => {
  const main = readFileSync(new URL('../main.js', import.meta.url), 'utf8');
  assert.match(main, /row\?\.id === 'counter' && counterDoor\.available\(\)/);
  assert.match(main, /prizeCounter: init\.prizeCounter === true/);
  assert.match(main, /refreshPrizes\(true\)/, 'the close re-reads the counter and stamps');
  assert.match(main, /loader\.open\(row/, 'every other station (and the counter on the web) still opens through the loader');
});
