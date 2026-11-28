// Journey game upgrades, lane GAMES-B (owner 2026-11-10): Mini Breakout, The Deep End, Racing Thoughts, Prize Dispenser.
// Rank 1 and rank 2 of each game are named upgrades; the games read the rank live through ui/unlocks/arcade/game-rank.js.
import test from 'node:test';
import assert from 'node:assert/strict';
import { gameRank } from '../ui/unlocks/arcade/game-rank.js';
import { createBreakout, stepBreakout, snapshot, ballsFor, paddleWidthFor, PADDLE_W, PADDLE_Y, WIDTH } from '../ui/unlocks/arcade/breakout-core/game.js';
import { createBoard, restoreBoard, snapshot as boardSnapshot, slide, growBoard, sizeFor, chainUnits, emptyCells } from '../ui/unlocks/arcade/deep-end/board.js';
import { restoreRace, advanceRace, raceTimerRate, raceSecondsLeft, nextCourse, RACE_BONUS_MS } from '../ui/unlocks/arcade/race-model.js';
import { dispenserState, dispense, dispenserStep, catchPrize, DROP_MS, CATCH_MS } from '../sim/arcade.js';
import { catchWindowFor, dropsFor, secondState, dropSecond, stepSecond, catchSecond, SECOND_DELAY_MS, WIDE_CATCH_MS } from '../ui/unlocks/arcade/dispenser-model.js';

test('a game rank reads live from the Journey store and is 0 outside Journey', () => {
  const state = { store: {} };
  assert.equal(gameRank(state, 'mini_race'), 0);
  state.store.journey = { ranks: { mini_race: 2 } };
  assert.equal(gameRank(state, 'mini_race'), 2);
  assert.equal(gameRank(state, 'mini_deep'), 0);
  assert.equal(gameRank({ store: { journey: { ranks: { x: 'junk' } } } }, 'x'), 0);
  assert.equal(gameRank(null, 'x'), 0);
});

/* ---- Mini Breakout ---------------------------------------------------------------------------------------------- */
function dealt(input) {
  const s = createBreakout({ board: 1, wait: .05 });
  const events = stepBreakout(s, .06, input);
  return { s, events };
}

test('Multiball: rank 1 starts each board with three balls at one speed, all leaving upward', () => {
  assert.equal(ballsFor(0), 1); assert.equal(ballsFor(1), 3); assert.equal(ballsFor(4), 3);
  const { s, events } = dealt({ balls: ballsFor(1) });
  assert.ok(events.some((e) => e.type === 'deal' && e.balls === 3));
  assert.equal(s.extras.length, 2);
  const speed = Math.hypot(s.ball.vx, s.ball.vy);
  const dirs = new Set();
  for (const b of [s.ball, ...s.extras]) {
    assert.ok(b.vy < 0);
    assert.ok(Math.abs(Math.hypot(b.vx, b.vy) - speed) < 1e-6);
    dirs.add(Math.round(Math.atan2(b.vx, b.vy) * 100));
  }
  assert.equal(dirs.size, 3);
  assert.equal(dealt({}).s.extras.length, 0, 'without the rank a board starts with one ball');
});

test('Multiball: a lost extra just leaves, only the last ball is served again', () => {
  const { s } = dealt({ balls: 3 });
  s.extras[0] = { x: 14, y: 194, vx: 0, vy: 118, r: 3.6 };
  let events = stepBreakout(s, .05, { balls: 3, manual: true, x: 200 });
  assert.ok(events.some((e) => e.type === 'lost'));
  assert.ok(!events.some((e) => e.type === 'miss'));
  assert.equal(s.extras.length, 1);
  s.ball = { x: 14, y: 194, vx: 0, vy: 118, r: 3.6 };
  events = stepBreakout(s, .05, { balls: 3, manual: true, x: 200 });
  assert.ok(events.some((e) => e.type === 'lost') && !events.some((e) => e.type === 'miss'));
  assert.equal(s.extras.length, 0, 'the extra took over as the main ball');
  s.ball = { x: 14, y: 194, vx: 0, vy: 118, r: 3.6 };
  events = stepBreakout(s, .05, { balls: 3, manual: true, x: 200 });
  assert.ok(events.some((e) => e.type === 'miss'), 'the last ball is served again');
});

test('Multiball survives a reload; a waiting board drops the extras', () => {
  const { s } = dealt({ balls: 3 });
  for (let i = 0; i < 30; i++) stepBreakout(s, 1 / 60, { balls: 3 });
  const restored = createBreakout(snapshot(s));
  assert.equal(restored.extras.length, s.extras.length);
  assert.deepEqual(stepBreakout(s, .03, { balls: 3 }), stepBreakout(restored, .03, { balls: 3 }));
  assert.equal(createBreakout({ ...snapshot(s), wait: 1 }).extras.length, 0);
});

test('Wide Paddle: rank 2 makes the paddle 50% wider and it returns balls the plain paddle misses', () => {
  assert.equal(paddleWidthFor(1), PADDLE_W);
  assert.equal(paddleWidthFor(2), PADDLE_W * 1.5);
  const returned = (paddleW) => {
    const s = createBreakout();
    const x = 140, edge = x + PADDLE_W / 2 + 12;
    s.paddle = x;
    s.ball = { x: edge, y: PADDLE_Y - 9, vx: 0, vy: 118, r: 3.6 };
    return stepBreakout(s, .04, { manual: true, x, paddleW }).some((e) => e.type === 'paddle');
  };
  assert.equal(returned(paddleWidthFor(0)), false);
  assert.equal(returned(paddleWidthFor(2)), true);
});

test('with both upgrades autoplay still clears boards with every ball in bounds', () => {
  const s = createBreakout();
  const input = { balls: 3, paddleW: paddleWidthFor(2) };
  let clears = 0;
  for (let i = 0; i < 60 * 240; i++) {
    clears += stepBreakout(s, 1 / 60, input).filter((e) => e.type === 'clear').length;
    for (const b of [s.ball, ...s.extras]) assert.ok(Number.isFinite(b.x) && Number.isFinite(b.y));
    assert.ok(s.paddle >= input.paddleW / 2 && s.paddle <= WIDTH - input.paddleW / 2);
    assert.ok(s.extras.length <= 2);
  }
  assert.ok(clears >= 4, `only ${clears} clears in four minutes`);
});

/* ---- The Deep End ----------------------------------------------------------------------------------------------- */
test('Bigger Board: rank 1 grows the board to 5x5 in place and the rules run on five lanes', () => {
  assert.equal(sizeFor(0), 4); assert.equal(sizeFor(1), 5);
  const b = createBoard(4);
  b.tiles = [{ id: 1, tier: 2, r: 3, c: 3 }, { id: 2, tier: 2, r: 3, c: 0 }];
  b.nextId = 3;
  growBoard(b, sizeFor(1));
  assert.equal(b.n, 5);
  assert.deepEqual(b.tiles.map((t) => [t.r, t.c]), [[3, 3], [3, 0]]);
  assert.equal(emptyCells(b).length, 23);
  const out = slide(b, 'right');
  assert.equal(out.merges.length, 1);
  assert.deepEqual(b.tiles.map((t) => [t.tier, t.r, t.c]), [[3, 3, 4]]);
  growBoard(b, 4);
  assert.equal(b.n, 5, 'a board never shrinks mid-dive');
  const back = restoreBoard(JSON.parse(JSON.stringify(boardSnapshot(b))), 5);
  assert.equal(back.n, 5);
  assert.equal(back.tiles.length, 1);
});

test('Chains: rank 2 pays every extra match in one move 1.5x the one before, never less than before', () => {
  assert.deepEqual([1, 2, 3, 4].map((k) => chainUnits(k, false)), [0, 0, 3, 4]);
  assert.deepEqual([1, 2, 3, 4].map((k) => chainUnits(k, true)), [0, 2.5, 4.75, 8.125]);
  for (let k = 0; k <= 12; k++) assert.ok(chainUnits(k, true) >= chainUnits(k, false));
  for (let k = 4; k <= 8; k++) {
    const extra = chainUnits(k, true) - chainUnits(k - 1, true), before = chainUnits(k - 1, true) - chainUnits(k - 2, true);
    assert.ok(Math.abs(extra / before - 1.5) < 1e-9);
  }
});

/* ---- Racing Thoughts -------------------------------------------------------------------------------------------- */
test('Nitro: rank 2 fills the race reward timer 30% faster and the button counts real seconds', () => {
  assert.equal(raceTimerRate(1), 1);
  assert.equal(raceTimerRate(2), 1.3);
  const plain = restoreRace(), nitro = restoreRace();
  const ms = Math.ceil(RACE_BONUS_MS / 1.3);
  advanceRace(plain, ms * raceTimerRate(0));
  assert.equal(advanceRace(nitro, ms * raceTimerRate(2)), true);
  assert.ok(plain.leftMs > 0);
  assert.equal(raceSecondsLeft({ leftMs: RACE_BONUS_MS }, 1), 35);
  assert.equal(raceSecondsLeft({ leftMs: RACE_BONUS_MS }, 1.3), 27);
});

test('Pick the Course: the picked course plays next and keeps playing; a resting clip falls back to the rotation', () => {
  assert.equal(nextCourse(0, null, 5), 1);
  assert.equal(nextCourse(4, null, 5), 0);
  assert.equal(nextCourse(0, 3, 5), 3);
  assert.equal(nextCourse(3, 3, 5), 3);
  assert.equal(nextCourse(0, 3, 5, (i) => i === 3), 1);
  assert.equal(nextCourse(0, 9, 5), 1, 'a junk pick is ignored');
  assert.equal(nextCourse(0, 2, 5, () => true), -1);
});

/* ---- Prize Dispenser -------------------------------------------------------------------------------------------- */
test('Wide Catch: rank 1 gives 4 seconds to catch instead of 2.5', () => {
  assert.equal(catchWindowFor(0), CATCH_MS);
  assert.equal(catchWindowFor(1), WIDE_CATCH_MS);
  for (const [window, late, ok] of [[CATCH_MS, 3000, false], [WIDE_CATCH_MS, 3000, true], [WIDE_CATCH_MS, 3999, true]]) {
    const s = dispenserState();
    dispense(s, 30000, () => .2);
    dispenserStep(s, DROP_MS + late, window);
    assert.equal(catchPrize(s), ok, `${late} ms late in a ${window} ms window`);
  }
  const s = dispenserState();
  dispense(s, 30000, () => .2);
  dispenserStep(s, DROP_MS + WIDE_CATCH_MS, WIDE_CATCH_MS);
  assert.equal(s.missed, 1);
});

test('Double Drop: rank 2 drops a second capsule with its own roll and its own catch', () => {
  assert.equal(dropsFor(1), 1); assert.equal(dropsFor(2), 2);
  const s = dispenserState(), c = secondState();
  const rolls = [.2, .99];
  const rng = () => rolls.shift();
  dispense(s, 30000, rng);
  assert.equal(dropSecond(c, rng), true);
  assert.equal(s.prize, 'red'); assert.equal(c.prize, 'gold');
  assert.equal(dropSecond(c, () => 0), false, 'one second capsule per crank');
  stepSecond(c, DROP_MS); dispenserStep(s, DROP_MS);
  assert.equal(s.phase, 'catch'); assert.equal(c.phase, 'drop', 'the second lands a moment later');
  assert.equal(catchSecond(c), false);
  assert.equal(catchPrize(s), true);
  stepSecond(c, SECOND_DELAY_MS);
  assert.equal(c.phase, 'catch');
  const restored = secondState(JSON.parse(JSON.stringify(c)));
  assert.equal(catchSecond(restored), true);
  assert.equal(catchSecond(restored), false, 'each capsule pays once');
  // Missing the second costs nothing and never touches the first.
  const missed = stepSecond(c, CATCH_MS);
  assert.equal(missed, true);
  stepSecond(c, 650);
  assert.equal(c.phase, 'idle');
  assert.equal(s.caught, 1); assert.equal(s.missed, 0);
});

test('Double Drop keeps the dispenser odds on the second capsule', () => {
  const counts = { red: 0, cyan: 0, purple: 0, gold: 0 };
  for (let i = 0; i < 10000; i++) {
    const c = secondState();
    dropSecond(c, () => (i + .5) / 10000);
    counts[c.prize]++;
  }
  assert.deepEqual(counts, { red: 4500, cyan: 4500, purple: 800, gold: 200 });
});
