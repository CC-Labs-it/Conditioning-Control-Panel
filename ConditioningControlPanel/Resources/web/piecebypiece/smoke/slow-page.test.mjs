import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { frameTime } from '../board/frame-time.js';
import { startPress, isClick } from '../board/hand.js';

const source = path => readFileSync(new URL(path, import.meta.url), 'utf8');

test('slow frames advance move animations by elapsed time', () => {
  const frames = [0, 200, 400, 600];
  const times = frames.slice(1).map((now, i) => frameTime(now, frames[i]));
  assert.ok(Math.abs(times.reduce((sum, t) => sum + t.elapsed, 0) - 0.6) < 1e-9);
  assert.ok(Math.abs(times.reduce((sum, t) => sum + t.dt, 0) - 0.3) < 1e-9);
  const boot = source('../boot.js');
  assert.match(boot, /anim\.update\(elapsed\)/);
  const match = source('../net/match.js');
  assert.ok(match.indexOf('board.settleMove?.()') < match.indexOf("bus.emit('turn', { side: played.turn"));
});

test('delayed handlers retain input time and a 120ms still press is a click', () => {
  const down = { clientX: 100, clientY: 100, timeStamp: 1000 };
  const up = { clientX: 103, clientY: 102, timeStamp: 1120 };
  const press = startPress(down);
  assert.equal(up.timeStamp - press.t, 120);
  assert.equal(isClick(press, up.clientX, up.clientY, 12), true);
  assert.equal(isClick({ ...press, far: 20 }, up.clientX, up.clientY, 12), false);
  assert.match(source('../board/drag.js'), /press = startPress\(ev\)/);
});
