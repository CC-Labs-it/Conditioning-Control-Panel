// The card melt (owner, 2026-10-13): never on a timed question, and it scores nothing.
// Run: node --test Resources/web/intake/tests/melt.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import { meltAllowed, isTimedBeat, isMeltSkip } from '../core/melt.js';
import { makeAnswerEvent, Mechanic } from '../core/contracts.js';
import { createEngine } from '../core/engine.js';

const read = (rel) => readFileSync(fileURLToPath(new URL(rel, import.meta.url)), 'utf8');

test('a timed question never melts', () => {
  const timed = { mechanic: Mechanic.MC4, timeoutMs: 6000 };
  assert.equal(isTimedBeat(timed), true);
  assert.equal(meltAllowed(timed, {}), false);
  assert.equal(meltAllowed(timed), false);
});

test('an untimed question card may melt, once, and only while it is free', () => {
  const beat = { mechanic: Mechanic.MC4, timeoutMs: 0 };
  assert.equal(meltAllowed(beat, {}), true);
  assert.equal(meltAllowed(beat, { meltUsed: true }), false);
  assert.equal(meltAllowed(beat, { committed: true }), false);
  assert.equal(meltAllowed(beat, { gateFrozen: true }), false);
  assert.equal(meltAllowed(beat, { corruptLive: true }), false);
  assert.equal(meltAllowed({ mechanic: Mechanic.MC4 }, {}), true, 'a missing timeout is untimed');
});

test('interludes and the bubble field never melt', () => {
  assert.equal(meltAllowed({ mechanic: Mechanic.Interlude, timeoutMs: 0 }, {}), false);
  assert.equal(meltAllowed({ mechanic: Mechanic.BubblePop, timeoutMs: 0 }, {}), false);
  assert.equal(meltAllowed(null, {}), false);
});

test('a melt event is neither an answer nor a timeout', () => {
  const ev = makeAnswerEvent({ latencyMs: 900, mechanic: Mechanic.MC4, melted: true, timedOut: true });
  assert.equal(ev.melted, true);
  assert.equal(ev.timedOut, false);
  assert.equal('chosenIndex' in ev, false);
  assert.equal('value' in ev, false);
  assert.equal(isMeltSkip(ev), true);
  const plain = makeAnswerEvent({ chosenIndex: 1, latencyMs: 900, mechanic: Mechanic.MC4 });
  assert.equal('melted' in plain, false, 'the key is only present on a melt');
  assert.equal(isMeltSkip(plain), false);
  assert.equal(isMeltSkip(null), false);
});

// Drive a whole run through the real engine, answering every beat with `answer(beat)`.
async function runEngine(answer) {
  const engine = createEngine({ config: { seed: 7 } });
  let step = await engine.next();
  let guard = 0;
  while (!step.done) {
    if (++guard > 2000) throw new Error('engine never finished');
    step = await engine.next(answer(step.beat));
  }
  return step.result;
}

test('a run where every card melts scores nothing at all', async () => {
  const result = await runEngine((beat) =>
    makeAnswerEvent({ latencyMs: 1200, mechanic: beat.mechanic, melted: true }));
  assert.equal(result.totalScore, 0);
  assert.equal(result.maxScore, 0, 'a melt must not add to the maximum either (that would be a miss)');
  assert.equal(result.trajectory.length, 0, 'no record of a question that was never answered');
  assert.deepEqual(result.tagTallies, {});
});

test('the old skip path did count as a miss, so the test above can fail', async () => {
  // What the melt used to send: no value, timedOut. The engine grades that as a miss.
  const result = await runEngine((beat) =>
    makeAnswerEvent({ latencyMs: 1200, mechanic: beat.mechanic, timedOut: true }));
  assert.ok(result.maxScore > 0);
  assert.ok(result.trajectory.some((t) => t.correct === false));
});

test('the card click handler asks the rule and resolves through the melt path', () => {
  const src = read('../render/beats.js');
  const at = src.indexOf('const onCardClick');
  assert.ok(at > 0, 'onCardClick not found');
  const body = src.slice(at, src.indexOf("card.addEventListener('click', onCardClick)", at));
  assert.match(body, /meltAllowed\(beat,/);
  assert.match(body, /finalizeMelt\(\)/);
  assert.doesNotMatch(body, /finalize\(undefined, undefined, true\)/);
  // The melt path itself never rewards, never touches the streak count.
  const fm = src.slice(src.indexOf('function finalizeMelt'), src.indexOf('function finalize('));
  assert.match(fm, /melted: true/);
  assert.doesNotMatch(fm, /reward\.resolve|effects\.play|streakCount\s*=|streakCount\s*\+/);
});
