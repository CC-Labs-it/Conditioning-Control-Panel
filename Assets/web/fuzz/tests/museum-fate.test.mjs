// The Gooning Museum fate cards and ending (ui/unlocks/fate/museum.js): ticket serial, placard lines and the accepted
// clauses the narrator may read back.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ticketSerial, ticketDate, fill, placardLine, placardValues, placardCap } from '../ui/unlocks/fate/museum/lines.js';
import { acceptedClauseIds, noteAcceptedTerms, acceptedTerms, TERMS_KEY } from '../ui/unlocks/fate/museum/terms.js';
import { PLACARDS, TICKET, GIFT } from '../data/content/museum.js';
import { CLAUSES } from '../data/content/clauses.js';
import { NARRATION_CLIPS } from '../ui/documentary/narration-data.js';
import REGISTRY from '../ui/unlocks/fate/museum.js';
import { CONFIG } from '../data/config.js';

const fmt = (n) => Math.round(n).toLocaleString('en-US');

test('the ticket serial is six digits, fixed by the run seed', () => {
  assert.match(ticketSerial(1110), /^[0-9]{6}$/);
  assert.equal(ticketSerial(1110), ticketSerial(1110));
  assert.notEqual(ticketSerial(1110), ticketSerial(1111));
  assert.match(ticketSerial(undefined), /^[0-9]{6}$/);
  assert.match(ticketSerial(2 ** 32 - 1), /^[0-9]{6}$/);
});

test('the ticket date reads day, month, year with no locale', () => {
  assert.equal(ticketDate(new Date(2026, 10, 10)), '10 NOV 2026');
  assert.equal(ticketDate(new Date(2027, 0, 3)), '3 JAN 2027');
  assert.match(ticketDate(new Date('nonsense')), /^[0-9]{1,2} [A-Z]{3} [0-9]{4}$/);
});

test('placard lines fill live numbers and fall through when a field is missing', () => {
  const v = placardValues({ taps: 4112, fuzz: 2500000.7, buys: 88, clock: 95 * 60000 }, fmt, 'Fuzz');
  assert.equal(placardLine('button', 0, v), 'Button. Pressed 4,112 times.');
  assert.equal(placardLine('button', 0, placardValues({ taps: 1 }, fmt)), 'Button. Original. Still in use.');
  assert.equal(placardLine('counter', 1, v), 'Counter. Running for 95 minutes.');
  // Under two minutes the minutes line steps aside.
  assert.equal(placardLine('counter', 1, placardValues({ clock: 30000 }, fmt)), 'Counter. Do not tap the glass.');
  assert.equal(placardLine('object', 0, { ...v, name: 'Mini Breakout' }), 'Mini Breakout. Donated by the visitor.');
  // No name: every {name} line is skipped, the unknown object line stays.
  assert.equal(placardLine('object', 0, { ...v, name: null }), 'Unknown object. Donated by the visitor.');
  // No numbers at all: a plain line still comes out.
  assert.ok(placardLine('button', 0, {}).startsWith('Button.'));
  assert.equal(fill('{a} and {b}', { a: 1 }), null);
  assert.equal(placardCap(true), 3);
  assert.equal(placardCap(false), 5);
});

test('placard copy is about a dozen dry lines: no dashes, no exclamation marks', () => {
  const all = [...Object.values(PLACARDS).flat(), ...Object.values(TICKET), ...Object.values(GIFT)];
  assert.ok(Object.values(PLACARDS).flat().length >= 12);
  for (const line of all) {
    assert.ok(!/[!–—]/.test(line), line);
    assert.ok(!/ - /.test(line), line);
  }
});

test('accepted clause ids map shown lines back to recorded narrator clips', () => {
  const id = Object.keys(CLAUSES).find((k) => CLAUSES[k].length >= 3);
  assert.deepEqual(acceptedClauseIds(id, [], 1), [`term-${id}-1`, `term-${id}-2`, `term-${id}-3`]);
  assert.deepEqual(acceptedClauseIds(id, [], 3), [`term-${id}-1`]);
  const recorded = Object.keys(NARRATION_CLIPS).filter((k) => k.startsWith('term-'));
  assert.ok(recorded.length >= 60);
  // Every recorded clause names a real item and a real place in its list.
  for (const t of recorded) {
    const m = /^term-(.+)-([0-9]+)$/.exec(t);
    assert.ok(m && CLAUSES[m[1]] && CLAUSES[m[1]][Number(m[2]) - 1], t);
  }
});

test('accepted terms are saved once each on the run store and read back in order', () => {
  const run = { store: {} };
  const id = Object.keys(CLAUSES).find((k) => CLAUSES[k].length >= 2);
  noteAcceptedTerms(run, id, [], 1, false);
  noteAcceptedTerms(run, id, [], 1, false);
  assert.deepEqual(acceptedTerms(run), CLAUSES[id].slice(0, 3).map((_, n) => `term-${id}-${n + 1}`));
  assert.ok(!Array.isArray(run.store[TERMS_KEY]), 'a store entry must be a plain object (sim/save.js)');
  assert.deepEqual(acceptedTerms(null), []);
  assert.deepEqual(acceptedTerms({ store: {} }), []);
});

test('go_mus holds the screen for the whole ride, far past the usual ending cap', () => {
  const row = REGISTRY.go_mus;
  assert.equal(typeof row.ending, 'function');
  assert.ok(row.showMaxMs >= 4 * 3600e3 && row.showMaxMs > CONFIG.ENDING_SHOW_MAX_MS);
  assert.ok(row.showMaxMs < 2 ** 31 - 1, 'setTimeout must not overflow');
  for (const id of ['ft_ticket', 'ft_placard', 'ft_rope', 'ft_gift']) assert.equal(typeof REGISTRY[id].mount, 'function', id);
});
