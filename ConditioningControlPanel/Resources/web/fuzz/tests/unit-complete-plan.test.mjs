import test from 'node:test';
import assert from 'node:assert/strict';
import { scanPlan } from '../ui/unlocks/themes-a/unit-complete-plan.js';
import { nameTag } from '../ui/unlocks/themes-a/unit-complete-name.js';

test('each ordinary piece gets its own accelerating shot before the isolated name', () => {
  for (let count = 0; count <= 500; count++) {
    const { shots, nameHold, firstCut, secondCut, tracking, black, terminal, end } = scanPlan(count);
    assert.equal(shots.length, count);
    let stop = 0, previousDuration = Infinity;
    for (const shot of shots) {
      assert.ok(shot.start >= stop && shot.duration > 0);
      assert.ok(shot.duration < previousDuration);
      previousDuration = shot.duration;
      stop = shot.start + shot.duration;
    }
    assert.ok(stop <= 11.201 && nameHold >= stop);
    assert.ok(Math.abs(firstCut - nameHold - .46) < 1e-9);
    assert.ok(Math.abs(secondCut - firstCut - 1.17) < 1e-9);
    assert.ok(Math.abs(tracking - secondCut - 1.27) < 1e-9);
    assert.ok(Math.abs(black - tracking - .20) < 1e-9);
    assert.ok(tracking <= 14.101 && terminal > black && end < 22.51);
    assert.ok(Math.abs(end - terminal - 6.8) < 1e-9);
  }
});

test('crowded pages hold the three opening cuts and small pages finish without an artificial wait', () => {
  for (const count of [3, 30, 100, 500]) {
    const { shots } = scanPlan(count);
    assert.equal(shots[0].start, 0.28);
    assert.deepEqual(shots.slice(0, 3).map(shot => shot.duration), [1.36, 1.12, 0.92]);
    assert.ok(shots[1].start - shots[0].start - shots[0].duration > 0.13);
  }
  assert.ok(scanPlan(3).end < 15.3);
  const fullBoard = scanPlan(100).shots;
  assert.ok(fullBoard.at(-1).duration > 0.03, 'the board keeps a visible frame for its fastest sweep');
});

test('the last tag uses the full hosted name or the issued run number even if the HUD text is clipped', () => {
  const account = { textContent: 'Short…' }, serial = { textContent: 'YOUR NAME' };
  const doc = { getElementById: () => account, querySelector: () => serial };
  const ctx = { world: { ownerDocument: doc }, identity: { displayName: '  Long <tag> & name  ' },
    state: { seed: 21, store: { dr_serial: { serial: 4197 } } } };
  assert.deepEqual(nameTag(ctx), { source: account, label: 'Long <tag> & name' });
  doc.getElementById = () => null;
  assert.deepEqual(nameTag(ctx), { source: serial, label: 'Long <tag> & name' });
  ctx.identity = null;
  assert.deepEqual(nameTag(ctx), { source: serial, label: 'UNIT 4197' });
  ctx.state.store = {};
  doc.querySelector = () => null;
  assert.deepEqual(nameTag(ctx), { source: null, label: 'UNIT 1021' });
});
