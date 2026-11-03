import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createBreakout, stepBreakout, snapshot, rewardSeconds, WIDTH, PADDLE_Y } from '../ui/unlocks/arcade/breakout-core/game.js';
import { rotatedBrickContact, paddleBounce } from '../ui/unlocks/arcade/breakout-core/contact.js';

test('the station contact maths resolve the real brick face and a ball inside it', () => {
  const brick = { x: 10, y: 10, w: 30, h: 12 };
  assert.equal(rotatedBrickContact({ x: 4, y: 16, r: 3 }, brick), null);
  const hit = rotatedBrickContact({ x: 25, y: 9, r: 3 }, brick);
  assert.equal(hit.ny, -1);
  assert.equal(hit.depth, 2);
  const inside = rotatedBrickContact({ x: 25, y: 16, r: 3 }, brick);
  assert.ok(Number.isFinite(inside.nx) && Number.isFinite(inside.ny) && inside.depth > 0);
});

test('the real paddle response preserves speed and sends every tip hit upwards', () => {
  for (const offset of [-1, -.5, 0, .5, 1]) {
    const hit = paddleBounce(offset, 100, 130);
    assert.ok(hit.vy < 0);
    assert.ok(Math.abs(Math.hypot(hit.vx, hit.vy) - 130) < 1e-9);
  }
});

test('autoplay clears successive boards with bounded coordinates and no stuck tail', () => {
  const s = createBreakout();
  const clears = [];
  for (let i = 0; i < 60 * 240; i++) {
    clears.push(...stepBreakout(s, 1 / 60).filter((e) => e.type === 'clear'));
    assert.ok(Number.isFinite(s.ball.x) && Number.isFinite(s.ball.y));
    assert.ok(s.paddle >= 31 && s.paddle <= WIDTH - 31);
  }
  assert.ok(clears.length >= 4, `only ${clears.length} clears in four minutes`);
  assert.equal(new Set(clears.map((e) => e.board)).size, clears.length);
});

test('a clear is emitted once, and its saved waiting board cannot pay again after reload', () => {
  const s = createBreakout();
  s.bricks.forEach((b) => { b.alive = false; });
  const brick = s.bricks[0]; brick.alive = true;
  s.ball = { x: brick.x + brick.w / 2, y: brick.y + brick.h + 4, vx: 0, vy: -118, r: 3.6 };
  const cleared = stepBreakout(s, .05).filter((e) => e.type === 'clear');
  assert.equal(cleared.length, 1);
  assert.equal(s.board, 1);
  const restored = createBreakout(snapshot(s));
  assert.equal(restored.wait > 0, true);
  const events = [];
  for (let i = 0; i < 70; i++) events.push(...stepBreakout(restored, 1 / 60));
  assert.equal(events.filter((e) => e.type === 'clear').length, 0);
});

test('manual returns earn more; hovering without returning the ball earns no bonus', () => {
  const s = createBreakout();
  s.ball = { x: s.paddle, y: PADDLE_Y - 9, vx: 0, vy: 118, r: 3.6 };
  const events = stepBreakout(s, .04, { manual: true, x: s.paddle });
  assert.ok(events.some((e) => e.type === 'paddle' && e.manual));
  assert.equal(s.manualReturns, 1);
  assert.equal(rewardSeconds(0, 4), 8);
  assert.equal(rewardSeconds(2, 4), 10);
  assert.equal(rewardSeconds(4, 4), 12);
});

test('save and restore keep the next physics step identical', () => {
  const s = createBreakout();
  for (let i = 0; i < 180; i++) stepBreakout(s, 1 / 60);
  const restored = createBreakout(snapshot(s));
  assert.deepEqual(stepBreakout(s, .03), stepBreakout(restored, .03));
  assert.deepEqual(snapshot(s), snapshot(restored));
});
