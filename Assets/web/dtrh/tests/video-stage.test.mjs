// The tube's video stage belongs to a descent (engine/videoStage.js, ccp-bugs #1470).
import test from 'node:test';
import assert from 'node:assert/strict';
import { stageClosedFor, createStageGate } from '../engine/videoStage.js';

test('the DtRH hub closes the stage, a descent opens it', () => {
  assert.equal(stageClosedFor(true, false), true);
  assert.equal(stageClosedFor(true, true), false);
});

test('the standalone Fall has no runs, so its stage never closes', () => {
  assert.equal(stageClosedFor(false, false), false);
  assert.equal(stageClosedFor(false, true), false);
});

test('a run ending with a clip on stage retires it', () => {
  const g = createStageGate();
  assert.equal(g.setClosed(true, true), true);
});

test('a run ending with nothing on stage has nothing to retire', () => {
  const g = createStageGate();
  assert.equal(g.setClosed(true, false), false);
});

test('a clip still on stage while closed is retired again on the next sync', () => {
  const g = createStageGate();
  g.setClosed(true, false);
  assert.equal(g.setClosed(true, true), true);
});

test('opening a descent retires nothing', () => {
  const g = createStageGate();
  g.setClosed(true, false);
  assert.equal(g.setClosed(false, true), false);
  assert.equal(g.isClosed(), false);
});

test('a stage asked for during a descent goes up', () => {
  const g = createStageGate();
  const t = g.ticket();
  assert.equal(g.mayOpen(t), true);
});

test('nothing goes up in the hub', () => {
  const g = createStageGate();
  g.setClosed(true, false);
  assert.equal(g.mayOpen(g.ticket()), false);
});

test('a clip asked for before the run ended is refused when it lands', () => {
  const g = createStageGate();
  const t = g.ticket();          // Private Show starts its acquire
  g.setClosed(true, false);      // the run stands down while the clip decrypts
  assert.equal(g.mayOpen(t), false);
});

test('...even when the next descent has already opened by then', () => {
  const g = createStageGate();
  const t = g.ticket();
  g.setClosed(true, false);
  g.setClosed(false, false);     // a fast Again
  assert.equal(g.mayOpen(t), false, 'the old ask belongs to the old run');
  assert.equal(g.mayOpen(g.ticket()), true, 'a fresh ask in the new run goes up');
});

test('repeated syncs while closed do not move the ticket', () => {
  const g = createStageGate();
  g.setClosed(true, false);
  const t = g.ticket();
  g.setClosed(true, false);
  g.setClosed(false, false);
  assert.equal(g.mayOpen(t), true);
});
