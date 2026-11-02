/* tests/net-clock.test.mjs - server clock (NET.md section 2): best-of-8 by rtt, slew, ping plan, ticks. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createServerClock } from '../net/clock.js';

test('offset comes from the smallest rtt of the last 8 pongs', () => {
  let t = 1000;
  const c = createServerClock({ now: () => t });
  // server is 5000 ms ahead; a slow sample has a skewed half-rtt guess
  c.pong(1000, 6000 + 150, 1400);      // rtt 400, offset 4950
  assert.equal(c.offset, 4950);
  c.pong(2000, 7000 + 20, 2040);       // rtt 40, offset 5000: better, within 250 ms: slewed, not jumped
  assert.equal(c.offset, 4950);
  t = 2040 + 1000; c.slew(t);           // 20 ms per second
  assert.ok(Math.abs(c.offset - 4970) < 1e-9);
  t += 5000; c.slew(t);
  assert.equal(c.offset, 5000);
  assert.equal(c.serverNow(t), t + 5000);
});

test('a big change is adopted at once and old samples roll off after 8', () => {
  let t = 0;
  const c = createServerClock({ now: () => t });
  c.pong(0, 100, 10);
  for (let i = 1; i <= 8; i++) c.pong(i * 1000, i * 1000 + 900 + 15, i * 1000 + 30);   // offset 900, rtt 30
  assert.equal(c.samples, 8);
  assert.equal(c.offset, 900);
});

test('ticks and tick times run on the room t0 at 60 Hz', () => {
  let t = 10_000;
  const c = createServerClock({ now: () => t });
  c.setRoom(9_000, 10_000, t);
  assert.equal(c.roomMs(), 1000);
  assert.equal(c.tick(), 60);
  assert.equal(c.tickMs(120), 11_000);
});

test('ping plan: one at once, 1 s apart for 5, then every 15 s', () => {
  let t = 0;
  const c = createServerClock({ now: () => t });
  const at = [];
  for (t = 0; t <= 40_000; t += 100) if (c.pingDue(t)) at.push(t);
  assert.deepEqual(at.slice(0, 7), [0, 1000, 2000, 3000, 4000, 5000, 20000]);
  assert.equal(at[7], 35000);
});

test('median rtt is 999 under three samples', () => {
  const c = createServerClock({ now: () => 0 });
  c.pong(0, 0, 50); c.pong(0, 0, 70);
  assert.equal(c.medianRtt(), 999);
  c.pong(0, 0, 60);
  assert.equal(c.medianRtt(), 60);
});
