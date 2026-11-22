import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as W from '../sim/prestige-words.js';
import { WORD_BANK, STAR_BY_ID } from '../data/prestige.js';
import { CONFIG } from '../data/config.js';
import trig from '../ui/unlocks/prestige/trig.js';
import { PRESTIGE_REGISTRY } from '../ui/unlocks/prestige/index.js';

const hasOf = (...ids) => (id) => ids.includes(id);
const all = Array.from({ length: 10 }, (_, i) => `trig_${i + 1}`);

test('the lane registers a trig entry with a mount, and touches nothing at import', () => {
  assert.equal(typeof trig.trig?.mount, 'function');
  assert.equal(PRESTIGE_REGISTRY.trig, trig.trig);
  for (const id of all) assert.ok(STAR_BY_ID[id], id);
});

test('word slots follow trig_1, trig_5 and trig_8', () => {
  assert.equal(W.wordSlots(hasOf()), 0);
  assert.equal(W.wordSlots(hasOf('trig_1')), 1);
  assert.equal(W.wordSlots(hasOf('trig_1', 'trig_5')), 2);
  assert.equal(W.wordSlots(hasOf(...all)), 3);
});

test('run words: bank only, no repeats, capped, and the first bank word when none is picked', () => {
  assert.deepEqual(W.runWords([], 1), [WORD_BANK[0]]);
  assert.deepEqual(W.runWords(undefined, 2), [WORD_BANK[0]]);
  assert.deepEqual(W.runWords(['nonsense', 42], 1), [WORD_BANK[0]]);
  assert.deepEqual(W.runWords(['Sink', 'sink', 'obey', 'pink'], 2), ['sink', 'obey']);
  assert.deepEqual(W.runWords(['give in'], 3), ['give in']);
  assert.deepEqual(W.runWords(['sink'], 0), []);
});

test('gaps sit in 40 to 70 s of game time, Often shortens them by a quarter', () => {
  assert.equal(W.nextGapMs(0), 40000);
  assert.equal(W.nextGapMs(1), 70000);
  assert.equal(W.nextGapMs(0.5, { often: true }), Math.round(55000 / 1.25));
  assert.equal(W.nextGapMs(NaN), 55000);
});

test('Sharper and Sharper Still add up, Instant triples only with trig_10', () => {
  assert.equal(W.catchSeconds(hasOf('trig_1')), W.CATCH_S);
  assert.equal(W.catchSeconds(hasOf('trig_1', 'trig_3')), W.CATCH_S * 1.25);
  assert.equal(W.catchSeconds(hasOf('trig_1', 'trig_3', 'trig_6')), W.CATCH_S * 1.75);
  assert.equal(W.catchSeconds(hasOf('trig_1'), 0, true), W.CATCH_S, 'no star, no triple');
  assert.equal(W.catchSeconds(hasOf('trig_1', 'trig_10'), 0, true), W.CATCH_S * 3);
});

test('Conditioned grows 1% a catch, Deep Groove 2%, capped, and only with trig_4', () => {
  assert.equal(W.conditionMult(50), 1);
  assert.equal(W.conditionMult(50, { conditioned: true }), 1.5);
  assert.equal(W.conditionMult(50, { conditioned: true, deep: true }), 2);
  assert.equal(W.conditionMult(1e6, { conditioned: true }), 1 + W.GROW_CAP);
  assert.equal(W.catchSeconds(hasOf('trig_1', 'trig_4'), 10), W.CATCH_S * 1.1);
});

test('instant window opens when the word is readable and lasts half a second', () => {
  assert.ok(W.isInstant(1000, 1000 + W.FADE_IN_MS + 500));
  assert.ok(!W.isInstant(1000, 1000 + W.FADE_IN_MS + 501));
  assert.ok(W.isInstant(1000, 1000 + W.FADE_IN_GENTLE_MS + 500, true), 'Gentle fades in slower and keeps the full window');
  assert.ok(!W.isInstant(1000, 900));
});

test('the lane asks a meaningful but not dominant share of income', () => {
  const bare = W.askShare(hasOf('trig_1'));
  const full = W.askShare(hasOf(...all), 1e6);
  assert.ok(bare > 0.05 && bare < 0.15, `bare ${bare}`);
  // Fully built it asks less than half of income before Instant Response, so the side budget (a quarter of income
  // shared with every prize) still leaves the other prizes their part.
  assert.ok(full < 0.5, `full ${full}`);
  assert.ok(full > CONFIG.SIDE_SHARE, 'fully built it is worth chasing');
});

test('the life store repairs itself and counts catches per word', () => {
  assert.deepEqual(W.parseWordStore(null), { catches: {}, total: 0 });
  assert.deepEqual(W.parseWordStore({ catches: { sink: 3.7, bogus: 9, obey: -1, drop: 'x' }, total: 'many' }), { catches: { sink: 3 }, total: 0 });
  const s = W.parseWordStore({ catches: { sink: 2 }, total: 2 });
  assert.equal(W.recordCatch(s, 'sink'), 2);
  assert.equal(W.recordCatch(s, 'obey'), 0);
  assert.deepEqual(s, { catches: { sink: 3, obey: 1 }, total: 4 });
  // A store survives a JSON round trip (the life save) unchanged.
  assert.deepEqual(W.parseWordStore(JSON.parse(JSON.stringify(s))), s);
});

test('mount with no Trigger Word star draws nothing', () => {
  const ctx = { prestige: { has: () => false, next: { words: [] } } };
  assert.deepEqual(trig.trig.mount(ctx), {});
});
