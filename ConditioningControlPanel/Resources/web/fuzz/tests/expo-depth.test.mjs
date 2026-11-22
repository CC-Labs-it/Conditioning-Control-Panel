import test from 'node:test';
import assert from 'node:assert/strict';
import { createDepth, depthAt, phaseAt } from '../ui/expo/depth.js';

test('the dome reaches every phase on the approved active-time schedule', () => {
  const phases = [[0, 0], [20, .1], [45, .25], [90, .5], [150, .75], [210, 1]];
  for (const [phase, [seconds, depth]] of phases.entries()) {
    assert.equal(depthAt(seconds), depth);
    assert.equal(phaseAt(seconds), phase);
    if (seconds) assert.equal(phaseAt(seconds - .001), phase - 1);
  }
  let previous = 0;
  for (let seconds = 0; seconds <= 240; seconds += .25) {
    const depth = depthAt(seconds);
    assert.ok(depth >= previous && depth <= 1, `monotonic, bounded at ${seconds}s`);
    assert.ok(depth - previous < .003, 'no visible jump at a phase boundary');
    previous = depth;
  }
});

test('staying in the dome holds the final phase while the visit clock keeps counting', () => {
  const dome = createDepth();
  assert.equal(dome.step(20, true).entered, true);
  assert.equal(dome.phase, 1);
  dome.step(400, true);
  assert.equal(dome.depth, 1);
  assert.equal(dome.phase, 5);
  assert.equal(dome.visitSeconds, 420);
  assert.equal(dome.step(0, true).entered, false);
  assert.equal(dome.visitSeconds, 420, 'a paused clock cannot deepen the room');
});

test('leaving records the actual visit and reentry starts a new visit without trapping progress', () => {
  const dome = createDepth();
  dome.step(120, true);
  assert.equal(dome.step(0, false).left, true);
  assert.equal(dome.lastVisitSeconds, 120);
  const depthOnExit = dome.depth;
  dome.step(20, false);
  assert.ok(dome.depth < depthOnExit && dome.depth > 0);
  assert.equal(dome.lastVisitSeconds, 120, 'time outside is not reported as time in the room');
  assert.equal(dome.step(3, true).entered, true);
  assert.equal(dome.visitSeconds, 3);
  dome.step(10000, false);
  assert.equal(dome.depth, 0);
  assert.equal(dome.phase, 0);
});

test('invalid or negative frame durations never advance or poison dome depth', () => {
  const dome = createDepth();
  dome.step(50, true);
  const before = [dome.depth, dome.visitSeconds];
  for (const dt of [-1, NaN, Infinity, -Infinity]) dome.step(dt, true);
  assert.deepEqual([dome.depth, dome.visitSeconds], before);
});
