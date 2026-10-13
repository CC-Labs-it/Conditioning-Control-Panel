// The dock toys wake on their own (game/toyProcs.js).
import test from 'node:test';
import assert from 'node:assert/strict';
import { TOY_PROCS, PROC_EVENTS, PITY_STEP, PITY_CAP, baseChance, procChance, listensFor, rollProc, restFor, procLine }
  from '../game/toyProcs.js';

test('every toy listens for a known event with sane numbers', () => {
  for (const [id, p] of Object.entries(TOY_PROCS)) {
    assert.ok(PROC_EVENTS.includes(p.on), id + ' event');
    assert.ok(p.chance.length >= 1, id);
    for (const c of p.chance) assert.ok(c > 0 && c <= 1, id + ' chance');
    for (let i = 1; i < p.chance.length; i++) assert.ok(p.chance[i] >= p.chance[i - 1], id + ' never falls with level');
    assert.ok(p.rest >= 0, id);
  }
  assert.equal(TOY_PROCS.e_stim, undefined, 'E-Stim arcs inline, it has no window');
});

test('levels past the table keep the last chance', () => {
  assert.equal(baseChance('the_wand', 1), 0.12);
  assert.equal(baseChance('the_wand', 3), 0.20);
  assert.equal(baseChance('the_wand', 9), 0.20);
  assert.equal(baseChance('nope', 1), 0);
});

test('pity: misses raise the chance, up to the cap, and a wake clears it', () => {
  const base = baseChance('freeze_trigger', 1);
  assert.equal(procChance('freeze_trigger', 1, 0), base);
  assert.ok(Math.abs(procChance('freeze_trigger', 1, 2) - base * (1 + 2 * PITY_STEP)) < 1e-9);
  assert.ok(Math.abs(procChance('freeze_trigger', 1, 99) - base * PITY_CAP) < 1e-9);
  const toy = { id: 'freeze_trigger', level: 1, misses: 0 };
  assert.equal(rollProc(toy, () => 0.99), false);
  assert.equal(rollProc(toy, () => 0.99), false);
  assert.equal(toy.misses, 2);
  assert.equal(rollProc(toy, () => 0), true);
  assert.equal(toy.misses, 0);
});

test('a crafted item wakes for certain', () => {
  const toy = { id: 'rubber_up', level: 1, misses: 0 };
  assert.equal(rollProc(toy, () => 0.999999), true);
  assert.equal(restFor('rubber_up'), 0);
});

test('a toy only hears its own event, and a big-moment toy waits for it', () => {
  assert.equal(listensFor('the_pump', 'expire'), true);
  assert.equal(listensFor('the_pump', 'pop'), false);
  assert.equal(listensFor('slippery_slope', 'milestone', { streak: 20 }), false);
  assert.equal(listensFor('slippery_slope', 'milestone', { streak: 30 }), true);
  assert.equal(listensFor('porn_dvd', 'milestone', { streak: 10 }), true);
  assert.equal(listensFor('e_stim', 'pop'), false);
});

test('the tooltip line reads plainly', () => {
  assert.equal(procLine('freeze_trigger', 2), 'wakes on 14% of the live ones you defuse');
  assert.equal(procLine('vibe_popping', 2), 'wakes on 2.5% of the treats you pop');
  assert.equal(procLine('rubber_up', 1), 'wakes once, on your first detonation');
  assert.equal(procLine('nope', 1), '');
});
