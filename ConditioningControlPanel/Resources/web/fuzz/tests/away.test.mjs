import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as E from '../sim/economy.js';
import { CONFIG } from '../data/config.js';
import { createAwayClock } from '../ui/away.js';
import { createEngine } from '../ui/engine.js';
import { createLife } from '../sim/save.js';

const HOUR = 3600 * 1000;

function paidRun(fps = 10) {
  const run = E.createRun();
  run.base.fps = fps;
  return run;
}
// base.fps is set by hand: nothing is bought, so recalc never rebuilds it.
const near = (a, b) => assert.ok(Math.abs(a - b) < 1e-6, `${a} vs ${b}`);

test('away pays per-second income for the time, and nothing else moves', () => {
  const run = paidRun(10);
  const before = { clock: run.clock, budget: run.side.budget, taps: run.taps };
  const got = E.away(run, null, 60000);
  near(got, 600);
  near(run.fuzz, 600);
  near(run.sources.fps, 600);
  assert.equal(run.clock, before.clock, 'away is not play: the run clock stays');
  assert.equal(run.side.budget, before.budget, 'no side money budget from away pay');
  assert.equal(run.taps, before.taps);
});

test('one away credit is capped at AWAY_CAP_MS, and bad input pays nothing', () => {
  const run = paidRun(1);
  near(E.away(run, null, 100 * HOUR), CONFIG.AWAY_CAP_MS / 1000);
  assert.equal(E.away(run, null, -5000), 0);
  assert.equal(E.away(run, null, NaN), 0);
  assert.equal(E.away(run, null, Infinity), 0);
});

test('timed buffs and bonuses that end on the run clock never pay away time; a growing bonus holds', () => {
  const run = paidRun(10);
  const golden = { timed: true, fps: (v) => v * 5 };
  const graded = { fps: (v, s) => (s.clock < 30000 ? v * 2 : v) };
  near(E.awayRate(run, [golden]), 10);
  near(E.awayRate(run, [graded]), 10);
  const glow = { fps: (v, s) => v * (1 + Math.min(1, s.clock / 40000)) };  // climbs while untouched
  run.clock = 10000;
  near(E.awayRate(run, [glow]), 12.5);
  const steady = { fps: (v) => v * 1.5 };
  near(E.awayRate(run, [steady, golden]), 15);
});

test('an ended run earns nothing away', () => {
  const run = paidRun(10);
  run.ended = { id: 'x', at: 0 };
  assert.equal(E.away(run, null, 60000), 0);
});

function clockWith() {
  let t = 1000;
  const paid = [], notes = [];
  const c = createAwayClock({ now: () => t, credit: (ms) => { paid.push(ms); return ms / 1000; }, onBack: (g, ms) => notes.push([g, ms]) });
  return { c, paid, notes, at: (v) => { t = v; }, get t() { return t; } };
}

test('frames under the gap pay through the normal step; a long gap pays away once', () => {
  const k = clockWith();
  k.at(1016); assert.equal(k.c.frame(), 16);
  k.at(2500); assert.equal(k.c.frame(), 1484, 'a hitch under AWAY_MIN_MS stays a normal step');
  k.at(2500 + 30000); assert.equal(k.c.frame(), 0, 'the step gets nothing: no double pay');
  assert.deepEqual(k.paid, [30000]);
  assert.deepEqual(k.notes, [[30, 30000]]);
  k.at(2500 + 30016); assert.equal(k.c.frame(), 16);
  assert.equal(k.paid.length, 1);
});

test('beats pay while frames are stopped; the return frame pays only the rest and notes the whole absence', () => {
  const k = clockWith();
  k.at(6000); assert.equal(k.c.beat(), 5);
  k.at(7000); assert.equal(k.c.beat(), 0, 'no beat pay under AWAY_MIN_MS');
  k.at(11000); assert.equal(k.c.beat(), 5);
  k.at(12000); assert.equal(k.c.frame(), 1000, 'the rest of the gap is a normal step');
  assert.deepEqual(k.paid, [5000, 5000]);
  assert.equal(k.notes.length, 1);
  assert.equal(k.notes[0][0], 10);
  assert.equal(k.notes[0][1], 11000);
});

test('a short absence pays but says nothing', () => {
  const k = clockWith();
  k.at(1000 + 5000); k.c.frame();
  assert.deepEqual(k.paid, [5000]);
  assert.equal(k.notes.length, 0);
});

test('one absence is capped however many beats it takes', () => {
  const k = clockWith();
  let t = 1000;
  for (let i = 0; i < 20; i++) { t += HOUR; k.at(t); k.c.beat(); }
  k.at(t + 16); k.c.frame();
  assert.equal(k.paid.reduce((a, b) => a + b, 0), CONFIG.AWAY_CAP_MS);
  assert.equal(k.notes[0][0], CONFIG.AWAY_CAP_MS / 1000);
});

test('a clock that runs backwards pays nothing and does not bank the jump', () => {
  const k = clockWith();
  k.at(-50000); assert.equal(k.c.frame(), 0);
  k.at(-20000); assert.equal(k.c.frame(), 0);
  k.at(1016); assert.equal(k.c.frame(), 16);
  assert.deepEqual(k.paid, []);
});

test('not present (a host focus suspend) pays every frame as away', () => {
  const k = clockWith();
  k.at(1016); assert.equal(k.c.frame(false), 0);
  k.at(1032); k.c.frame(false);
  assert.deepEqual(k.paid, [16, 16]);
});

function engine(fps = 10) {
  const run = paidRun(fps);
  const ui = { doc: {}, buttonFor: () => ({}), layer: () => ({}) };
  return createEngine({ run, life: createLife(), ui, registry: {} });
}

test('the engine pays away time only when the player could be playing', () => {
  const g = engine(10);
  near(g.away(10000), 100);
  g.setPaused(true);
  assert.equal(g.away(10000), 0, 'pause: nothing accrues');
  g.setPaused(false);
  g.setSuspended(true, 'panic');
  assert.equal(g.away(10000), 0, 'panic: nothing accrues');
  g.setSuspended(false, 'panic');
  const events = [];
  g.subscribe('suspend', e => events.push(e));
  g.setSuspended(true, 'focus');
  near(g.away(10000), 100, 'a host focus suspend is away, not a stop');
  g.setSuspended(true, 'panic');
  g.setSuspended(true, 'panic');
  assert.deepEqual(events, [{ on: true, reason: 'focus' }, { on: true, reason: 'panic' }], 'the ending hears a panic that supersedes a focus hold exactly once');
  assert.equal(g.away(10000), 0, 'panic during a focus suspend wins');
  g.setSuspended(false);
  near(g.run.fuzz, 200);
});

test('a save taken after away pay holds it once; a reload credits nothing again', async () => {
  const { serialize, parse } = await import('../sim/save.js');
  const g = engine(10);
  g.away(60000);
  const text = serialize(g.run, g.life, {});
  const back = parse(text);
  assert.ok(back, 'save parses');
  const run = back.run || back;
  near(run.fuzz, 600);
  assert.equal(run.clock, 0, 'no wall time in the save to pay twice');
});
