// tests/fx-payoff.test.mjs - payoff words and flashes (fx/payoff.js, owner 2026-10-25): toy hits and course
// hazards on the local player pay a word cascade (+ a flash on spins and slows); pads sometimes pay one word.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { makeFeel } from './sim-fake.mjs';
import { createBus } from '../core/events.js';
import { createPayoff, screenFor } from '../fx/payoff.js';
import { subliminalCards } from '../fx/subliminalBurst.js';

function setup({ feel = {}, granted = true, roll = 0 } = {}) {
  const f = makeFeel(feel), bus = createBus(), asked = [];
  const photosafe = { request: (k) => { asked.push(k); return granted; } };
  const p = createPayoff(bus, f, { photosafe, rand: () => roll });
  const run = (sec, dt = 1 / 60) => { for (let t = 0; t < sec; t += dt) p.update(dt); };
  return { f, bus, p, asked, run };
}

test('a spin on you pays a word cascade and one flash; the cascade has several words', () => {
  const t = setup();
  t.bus.emit('item-hit', { victim: 0, by: 1, kind: 'disc', verdict: 'hit', durationS: 1 });
  assert.equal(t.p.words.kind, 'brainDrain');
  assert.equal(t.p.flash.kind, 'flash');
  assert.deepEqual(t.asked, ['flash']);
  assert.ok(t.p.words.durS >= t.f.get('payoff.cascadeMinS'));
  assert.ok(subliminalCards(t.p.words.durS * 0.9, 16 / 9, 0, false, t.p.words.durS).length >= 1);
  const maxWords = Math.max(...Array.from({ length: 60 }, (_, i) => subliminalCards(i * 0.05, 16 / 9, 0, false, t.p.words.durS)).map((c) => c.at(-1)?.index ?? -1)) + 1;
  assert.ok(maxWords >= 4, 'a cascade, not a single word: ' + maxWords);
  t.run(0.3);
  assert.ok(t.p.flash.k > 0.9);
  t.run(t.f.get('payoff.flashS') + 0.3);
  assert.equal(t.p.flash.kind, 'none');
  t.run(3);
  assert.equal(t.p.words.kind, 'none');
});

test('hits on others, mercy and dodges pay nothing; a Pendulum pays words without a flash', () => {
  const t = setup();
  t.bus.emit('item-hit', { victim: 1, by: 0, kind: 'disc', verdict: 'hit', durationS: 1 });
  t.bus.emit('item-hit', { victim: 0, by: 1, kind: 'disc', verdict: 'snapped', durationS: 0 });
  assert.equal(t.p.words.kind, 'none');
  t.bus.emit('item-hit', { victim: 0, by: 1, kind: 'pendulum', verdict: 'hit', durationS: 1.5 });
  assert.equal(t.p.words.kind, 'brainDrain');
  assert.equal(t.p.flash.kind, 'none');
});

test('course hazards (malus) pay a cascade and a flash, local racer only, with cooldowns', () => {
  const t = setup();
  t.bus.emit('scan-hit', { racer: 3 });
  assert.equal(t.p.words.kind, 'none');
  t.bus.emit('keep-chain-hit', { id: 2, stallS: 0.5 });
  assert.equal(t.p.words.kind, 'brainDrain'); assert.equal(t.p.flash.kind, 'flash');
  const seed = t.p.words.params.seed;
  t.run(0.5);
  t.bus.emit('goonworks-hit', { racer: 0 });
  assert.equal(t.p.words.params.seed, seed, 'malusCoolS holds the next cascade');
  t.run(t.f.get('payoff.malusCoolS'));
  t.bus.emit('goonworks-hit', { racer: 0 });
  assert.notEqual(t.p.words.params.seed, seed);
  assert.equal(t.p.stats.flashes, 1, 'flashCoolS holds the second flash');
});

test('photosafe refusal drops the flash, never the words', () => {
  const t = setup({ granted: false });
  t.bus.emit('keep-gate-hit', { id: 1 });
  assert.equal(t.p.words.kind, 'brainDrain');
  assert.equal(t.p.flash.kind, 'none');
  assert.equal(t.p.stats.refused, 1);
});

test('a pad pays one word by chance and cooldown; payoff.on false silences everything', () => {
  const t = setup({ roll: 0 });
  t.bus.emit('pad', { index: 0 });
  assert.equal(t.p.words.kind, 'brainDrain');
  assert.equal(t.p.words.durS, t.f.get('payoff.padWordS'));
  assert.equal(subliminalCards(0.3, 16 / 9, 0, false, t.p.words.durS).length, 1);
  t.run(2); t.bus.emit('pad', { index: 1 });
  assert.equal(t.p.stats.padWords, 1, 'padCoolS');
  const miss = setup({ roll: 0.99 }); miss.bus.emit('pad', { index: 0 });
  assert.equal(miss.p.words.kind, 'none');
  const off = setup({ feel: { 'payoff.on': false } });
  off.bus.emit('keep-gate-hit', {}); off.bus.emit('pad', {});
  off.bus.emit('item-hit', { victim: 0, kind: 'disc', verdict: 'hit', durationS: 1 });
  assert.equal(off.p.words.kind, 'none'); assert.equal(off.p.flash.kind, 'none');
});

test('Gentle turns the flash into a corner card; renderers keep the item screen while it shows that kind', () => {
  const t = setup({ feel: { 'gentle.on': true } });
  t.bus.emit('item-hit', { victim: 0, kind: 'tether', verdict: 'hit', durationS: 1 });
  assert.equal(t.p.flash.params.corner, true);
  assert.ok(t.p.flash.params.scale > 0);
  assert.equal(t.p.words.params.gentle, true);
  const item = { kind: 'flash', prevKind: 'none' };
  assert.equal(screenFor('flash', item, t.p.flash), item);
  assert.equal(screenFor('brainDrain', item, t.p.words), t.p.words);
});
