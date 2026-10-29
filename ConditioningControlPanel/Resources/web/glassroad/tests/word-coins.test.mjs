// sim/wordCoins.js + hud/wordCount.js: road words as coins (playtest fix 2026-10-28, lane words).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fakeTrack } from './sim-fake.mjs';
import { createFeel } from '../core/feel.js';
import { coinTuning, pickCoins, coinLane, underTrap, coinLit, coinTouch, coinCrossing, createWordCoins } from '../sim/wordCoins.js';
import { coinFrames } from '../hud/wordCount.js';

const feelJson = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
const C = coinTuning(createFeel(feelJson));

test('feel.json carries the wordCoins group: solo only, cap 10, about +9% at the cap, a hit drops 3', () => {
  assert.equal(C.on, true);
  assert.equal(C.online, false);                       // online keeps today's rules until the room rollout
  assert.equal(C.cap, 10);
  assert.equal(C.hitDrop, 3);
  const atCap = C.cap * C.topPctPer;
  assert.ok(atCap >= 0.08 && atCap <= 0.1, 'cap buys 8-10% top speed, got ' + atCap);
  assert.equal(feelJson.items.bubbleAmount, 0.05, 'pickup bubbles retain the validated boost');
  assert.equal(feelJson.items.bubbleHoldS, 0.35);
});

test('coinTuning falls back to defaults on an older feel', () => {
  const c = coinTuning(createFeel({ speed: { topMps: 30 } }));
  assert.equal(c.cap, 10); assert.equal(c.on, true);
  assert.equal(coinTuning(null).hitDrop, 3);
});

test('a phrase without semantic identities gets one coin: shortest with enough letters, else longest', () => {
  const sc = [
    { phrase: 0, chars: 2 }, { phrase: 0, chars: 6 }, { phrase: 0, chars: 4 }, { phrase: 0, chars: 4 },
    { phrase: 1, chars: 1 }, { phrase: 1, chars: 2 },
    { phrase: 2, chars: 5 },
  ];
  assert.deepEqual([...pickCoins(sc, C)], [0, 0, 1, 0, 0, 1, 1]);
  assert.equal(pickCoins([], C).length, 0);
});

test('a coin sits on the inside of the bend, a side by phrase on a straight', () => {
  const bend = fakeTrack({ nodes: 60, spacing: 10, w: 14 });        // a tight circle: the centre is +b
  const x = coinLane(bend, 100, 0, C);
  assert.ok(x > 0, 'inside is +b on this circle, got ' + x);
  assert.ok(Math.abs(x - C.laneShare * (14 / 2 - C.edgeMarginM)) < 1e-9);
  const flipped = { ...bend, frameAt: (s, out = {}) => { const f = bend.frameAt(s, out); f.b = f.b.map((v) => -v); return f; } };
  assert.ok(coinLane(flipped, 100, 0, C) < 0, 'the inside follows the bend, not a fixed side');
  const straight = fakeTrack({ nodes: 4000, spacing: 10, w: 14 }); // R ~ 6.4 km: no measurable bend
  const sides = new Set([0, 1, 2, 3, 4, 5].map((p) => Math.sign(coinLane(straight, 500, p, C))));
  assert.deepEqual([...sides].sort(), [-1, 1], 'straights alternate sides by phrase');
});

test('never under a moving trap row', () => {
  const tr = { ...fakeTrack({ nodes: 400, spacing: 10 }), moving: { traps: [{ s: 1000, len: 6 }] } };
  assert.equal(underTrap(tr, 1000, 10, C), true);
  assert.equal(underTrap(tr, 1000 + 5 + 3 + C.trapClearM - 0.1, 10, C), true);
  assert.equal(underTrap(tr, 1000 + 5 + 3 + C.trapClearM + 0.1, 10, C), false);
  assert.equal(underTrap(tr, 2000, 10, C), false);
  assert.equal(underTrap({ ...tr, moving: null }, 1000, 10, C), false);
});

test('lit while its word is sung, with a little slack each side', () => {
  assert.equal(coinLit(10 - C.litPreS - 0.01, 10, 10.3, C), false);
  assert.equal(coinLit(10 - C.litPreS + 0.01, 10, 10.3, C), true);
  assert.equal(coinLit(10.3 + C.litPostS - 0.01, 10, 10.3, C), true);
  assert.equal(coinLit(10.3 + C.litPostS + 0.01, 10, 10.3, C), false);
});

test('touch: the kart must be on the painted word, along and across', () => {
  const len = 10.2, lane = 3, w = 4;
  assert.equal(coinTouch(0, len, lane, w, 3, C), true);
  assert.equal(coinTouch(len / 2 + C.touchSlackM + 0.1, len, lane, w, 3, C), false);
  assert.equal(coinTouch(0, len, lane, w, lane - w / 2 - C.kartHalfM + 0.05, C), true);
  assert.equal(coinTouch(0, len, lane, w, lane - w / 2 - C.kartHalfM - 0.05, C), false);
  assert.equal(coinTouch(0, len, lane, w, -3, C), false, 'the other side of the road misses it');
});

const marking = { lenM: 2, widthM: 2, lane: 3, startS: 10, endS: 10.1 };
const contact = (dS, tS = 10, extra = {}) => ({ dS, tS, lat: 3, grounded: true, airborne: false, respawn: false, height: 0, ...extra });

test('crossing catches a skipped footprint only when longitudinal and lateral contact coincide', () => {
  assert.equal(coinCrossing(contact(4, 9.95), contact(-4, 10.05), marking, C), true);
  assert.equal(coinCrossing(contact(4, 9.95, { lat: 0 }), contact(-4, 10.05, { lat: 6 }), marking, C), true);
  assert.equal(coinCrossing(contact(4, 9.95, { lat: -5 }), contact(-4, 10.05, { lat: 0 }), marking, C), false);
  assert.equal(coinCrossing(null, contact(0), marking, C), true, 'first sample can touch directly');
});

test('crossing requires grounded contact on this road deck', () => {
  for (const invalid of [{ grounded: false }, { airborne: true }, { respawn: true }, { respawn: 1 },
    { height: 1.21 }, { height: -1.21 }, { height: NaN }]) {
    assert.equal(coinCrossing(null, contact(0, 10, invalid), marking, C), false, JSON.stringify(invalid));
    assert.equal(coinCrossing(contact(4, 9.95, invalid), contact(-4, 10.05), marking, C), false, 'no sweep from invalid previous contact');
  }
  assert.equal(coinCrossing(null, contact(0, 10, { height: 1.2 }), marking, C), true);
  assert.equal(coinCrossing(contact(4, 9.95, { airborne: true }), contact(0), marking, C), true, 'landing on a lit footprint is direct contact');
});

test('swept contact uses the lit time at the crossing, not only the current time', () => {
  const narrow = { ...C, litPreS: 0, litPostS: 0 };
  const shortWord = { ...marking, startS: 10, endS: 10.02 };
  assert.equal(coinCrossing(contact(4, 9.95), contact(-4, 10.09), shortWord, narrow), true, 'lit during crossing, dark by current sample');
  assert.equal(coinCrossing(contact(4, 9.89), contact(-4, 10.03), shortWord, narrow), false, 'word lights after the kart left');
  assert.equal(coinCrossing(contact(4, 10.03), contact(-4, 10.12), shortWord, narrow), false, 'word already ended');
});

test('seeks, pauses, long frame gaps and teleports never sweep a pickup', () => {
  for (const [previous, current] of [
    [contact(4, 10.1), contact(-4, 10)],
    [contact(4, 10), contact(-4, 10)],
    [contact(4, 9.8), contact(-4, 10)],
    [contact(8, 9.95), contact(-8, 10.05)],
  ]) assert.equal(coinCrossing(previous, current, marking, C), false);
  assert.equal(coinCrossing(contact(4, 20), contact(0, 10), marking, C), true, 'seek falls back to current contact');
});

test('the count caps, a hit drops a few, top speed follows the count', () => {
  const c = createWordCoins(C);
  assert.equal(c.topMul(), 1);
  for (let i = 0; i < 14; i++) c.add();
  assert.equal(c.count, C.cap);
  assert.equal(c.add(), false);
  assert.ok(Math.abs(c.topMul() - (1 + C.cap * C.topPctPer)) < 1e-12);
  assert.equal(c.hit(), 3); assert.equal(c.count, 7);
  c.reset(); c.add(); assert.equal(c.hit(), 1); assert.equal(c.count, 0); assert.equal(c.hit(), 0);
});

test('the HUD count obeys motion and Gentle', () => {
  assert.equal(coinFrames(1, 'off'), null);
  assert.equal(coinFrames(0, 'full'), null);
  assert.ok(coinFrames(1, 'full').some((k) => k.transform));
  assert.ok(!coinFrames(1, 'less').some((k) => k.transform), 'less: no scale');
  assert.ok(!coinFrames(1, 'full', true).some((k) => k.transform), 'Gentle: no scale');
  assert.ok(!coinFrames(-1, 'full').some((k) => k.transform), 'a loss only dims');
});
