/* tests/hud-sounds.test.mjs - polish 5 S4: which sound a card press, a seats update and the grid count ask for. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { actSound, seatSound, nextGridBell, UI_SOUNDS } from '../hud/sounds.js';
import { applySeats, applySeat } from '../hud/lobbyMath.js';

test('actSound: confirms, toggles and backs; ready waits for the seat update', () => {
  for (const a of ['race', 'online', 'options', 'controls', 'resume', 'restart', 'again', 'start', 'howto', 'lb-join', 'lb-quick', 'lb-start']) assert.equal(actSound(a), 'menu-ok', a);
  for (const a of ['toggle', 'cycle', 'howto-off', 'layout', 'lb-hstep', 'lb-lstep']) assert.equal(actSound(a), 'menu-toggle', a);
  for (const a of ['back', 'quit', 'menu', 'lb-back', 'lb-exit', 'lb-leave', 'lb-home']) assert.equal(actSound(a), 'menu-back', a);
  assert.equal(actSound('lb-ready'), null);
  assert.equal(actSound(''), null);
  assert.equal(actSound(undefined), null);
  for (const a of ['race', 'toggle', 'back']) assert.ok(UI_SOUNDS.includes(actSound(a)));
});

test('seatSound: in, out, ready, unready in that weight; quiet changes stay quiet', () => {
  const base = applySeats([{ seat: 0, kind: 'human', name: 'Me', host: true }, { seat: 1, kind: 'bot' }]);
  assert.equal(seatSound(base, base), null);
  const joined = applySeats([{ seat: 0, kind: 'human', name: 'Me', host: true }, { seat: 1, kind: 'bot' }, { seat: 2, kind: 'human', name: 'Ana' }]);
  assert.equal(seatSound(base, joined), 'seat-in');
  assert.equal(seatSound(joined, base), 'seat-out');
  const ready = applySeat(joined, { seat: 2, ready: true });
  assert.equal(seatSound(joined, ready), 'ready-on');
  assert.equal(seatSound(ready, joined), 'ready-off');
  const left = applySeat(ready, { seat: 2, left: true });
  assert.equal(seatSound(ready, left), 'seat-out', 'a seat that left is out, not unready');
  const idle = applySeat(joined, { seat: 2, idle: true });
  assert.equal(seatSound(joined, idle), null);
  // a join and a ready together: the join wins
  const both = applySeat(applySeats([{ seat: 0, kind: 'human', name: 'Me', host: true, ready: true }, { seat: 1, kind: 'bot' }, { seat: 2, kind: 'human', name: 'Ana' }]), { seat: 0, ready: true });
  assert.equal(seatSound(base, both), 'seat-in');
  assert.equal(seatSound(null, base), null);
});

test('nextGridBell: each whole second before countAt, on server time', () => {
  const at = 100000;
  assert.deepEqual(nextGridBell(at - 2500, at), { atMs: at - 2000, waitMs: 500, left: 2 });
  assert.deepEqual(nextGridBell(at - 2000, at), { atMs: at - 1000, waitMs: 1000, left: 1 });
  assert.deepEqual(nextGridBell(at - 1001, at), { atMs: at - 1000, waitMs: 1, left: 1 });
  assert.equal(nextGridBell(at - 999, at), null, 'the last second belongs to the race count');
  assert.equal(nextGridBell(at + 5, at), null);
  assert.equal(nextGridBell(at - 2500, 0), null);
  const b = nextGridBell(at - 2999.5, at);
  assert.equal(b.left, 2); assert.ok(Math.abs(b.waitMs - 999.5) < 1e-9);
});
