// node --test smoke/cup-replay.test.mjs - the host-handed replay: UCI records, the reel's moments,
// the seat's pacing off update(dt), and a boot that is the old boot when nobody asks for a replay.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { readHostReplay, buildSteps, endOf, pickMoments, captionOf } from '../game/cup-replay.js';
import { createReplaySeat, PACE } from '../net/replay-seat.js';
import { createBus } from '../game/events.js';

const rec = (moves, extra = {}) => readHostReplay({ record: { match_id: 'm1', white_name: 'Ada', black_name: 'Bo', result: '1-0', reason: 'checkmate', start_fen: null, moves, round: 2, board: 3, ...extra }, reel: !!extra.reel }).record;

// Real-looking games. SCHOLAR: a four-move mate. LEGAL: Legal's mate, a queen given up on the way.
const SCHOLAR = 'e2e4 e7e5 f1c4 b8c6 d1h5 g8f6 h5f7'.split(' ');
const LEGAL = 'e2e4 e7e5 g1f3 d7d6 f1c4 c8g4 b1c3 g7g6 f3e5 g4d1 c4f7 e8e7 c3d5'.split(' ');
// Castles both ways, an en passant and a promotion.
const BUSY = ('e2e4 e7e5 g1f3 b8c6 f1c4 f8c5 e1g1 d7d6 d2d4 c8g4 d4d5 c6d4 c2c3 d8d7 c3d4 e8c8 d4c5 f7f5 e4f5 g7g5 f5g6 '
  + 'h7h6 g6g7 g4f3 g7h8q').split(' ');

test('a host object reads as a record, anything else as nothing', () => {
  assert.equal(readHostReplay(null), null);
  assert.equal(readHostReplay({ record: { moves: [] } }), null);
  assert.equal(readHostReplay({ record: { moves: 'e2e4' } }), null);
  const host = readHostReplay({ record: { match_id: 'm9', white_name: ' Ada ', moves: ['E2E4'], result: 'nope' }, reel: 1 });
  assert.deepEqual([host.reel, host.record.white, host.record.black, host.record.moves[0], host.record.result], [false, 'Ada', 'Black', 'e2e4', null]);
});

test('UCI moves become board steps: castling both sides, en passant, promotion, mate', () => {
  const busy = buildSteps(rec(BUSY));
  assert.equal(busy.steps.length, BUSY.length, 'every move is legal');
  assert.equal(busy.sans[6], 'O-O');
  assert.equal(busy.sans[15], 'O-O-O');
  const ep = busy.steps[20];
  assert.ok(String(ep.flags).includes('e') && ep.captured === 'p', 'f5xg6 is en passant');
  const promo = busy.steps[busy.steps.length - 1];
  assert.equal(promo.promotion, 'q');
  assert.deepEqual(busy.plays[busy.plays.length - 1], { from: 'g7', to: 'h8', promotion: 'q' });
  const mate = buildSteps(rec(SCHOLAR));
  assert.ok(mate.mate && mate.sans[6] === 'Qxf7#');
  // a damaged tail stops at the last legal position
  assert.equal(buildSteps(rec(['e2e4', 'e7e5', 'e4e5', 'g1f3'])).steps.length, 2);
  assert.equal(buildSteps(rec(['e2e4', 'bad'])).steps.length, 1);
  // a start position of its own
  assert.equal(buildSteps(rec(['a7a8q'], { start_fen: '7k/P7/8/8/8/8/8/K7 w - - 0 1' })).sans[0], 'a8=Q+');
});

test('the end comes from the board when it is a mate, else from the record', () => {
  assert.deepEqual(endOf(rec(SCHOLAR), { result: 'checkmate', winner: 'w', reason: 'checkmate' }).winner, 'w');
  assert.deepEqual(endOf(rec(['e2e4'], { result: '0-1', reason: 'resign' })), { result: 'resign', winner: 'b', reason: 'resign' });
  assert.equal(endOf(rec(['e2e4'], { result: '1/2-1/2', reason: 'agreement' })).winner, null);
});

test('the reel picks the end, promotions and the big captures, in game order', () => {
  const legal = pickMoments(buildSteps(rec(LEGAL)).steps);
  assert.equal(legal[legal.length - 1].kind, 'end');
  assert.equal(legal[legal.length - 1].to, LEGAL.length);
  assert.ok(legal[legal.length - 1].from <= 9, 'the queen coming off (ply 10) is folded into the mate that follows it');
  const busy = pickMoments(buildSteps(rec(BUSY)).steps);
  assert.ok(busy.length <= 4 && busy.length >= 2);
  assert.equal(busy[busy.length - 1].kind, 'end');
  for (let k = 0; k < busy.length; k++) {
    assert.ok(busy[k].from < busy[k].to && busy[k].from >= 0, 'every moment plays at least one move');
    if (k) assert.ok(busy[k].from >= busy[k - 1].to, 'moments never overlap or run backwards');
  }
  // an even trade is not a moment; a short game is just its end
  const trade = pickMoments(buildSteps(rec('e2e4 d7d5 e4d5 d8d5 b1c3 d5a5 d2d4 c7c6 g1f3 g8f6 f1c4 c8f5'.split(' '))).steps);
  assert.deepEqual(trade.map((m) => m.kind), ['end']);
  assert.deepEqual(pickMoments([]), []);
});

test('graded tracks, when given, put the fall in the reel (door/fall.js)', () => {
  const steps = buildSteps(rec(LEGAL)).steps;
  const iq = { b: { start: 140, moves: [{ ply: 6, side: 'b', san: 'Bg4', best: 'Nf6', cp: 300, loss: 12, value: 128 }] } };
  const fall = pickMoments(steps, { iq }).find((m) => m.kind === 'fall');
  assert.ok(fall && fall.from === 5 && fall.to >= 7, 'from before the worst move through its punishment');
});

test('the caption names the round, the board, both players and the move', () => {
  assert.deepEqual(captionOf(rec(SCHOLAR), 7), { seat: 'Round 2, Board 3', names: 'Ada vs Bo', move: 'Move 4' });
  assert.equal(captionOf(rec(SCHOLAR, { round: 'Final', board: '' }), 1).seat, 'Final');
});

function stage(moves, extra, anim = null) {
  const bus = createBus(), events = [], done = [], calls = [];
  for (const name of ['local', 'players', 'turn', 'capture', 'check', 'gameover', 'resync']) bus.on(name, (p) => events.push([name, p]));
  const pieces = { setPosition: (p) => calls.push(['set', Object.keys(p).length]), move: (a, b) => calls.push(['move', a, b]), remove: (s) => calls.push(['remove', s]) };
  let paused = false;
  const seat = createReplaySeat({ bus, board: { pieces, setSide() {}, anim }, record: rec(moves, extra), held: () => paused, onDone: (id) => done.push(id) });
  const run = (seconds, dt = 0.1) => { for (let t = 0; t < seconds; t += dt) seat.update(dt); };
  return { seat, events, done, calls, run, pause: (p) => { paused = p; }, names: () => events.map((e) => e[0]) };
}

test('the whole game plays off update(dt), pauses, and says done once', () => {
  const s = stage(SCHOLAR);
  s.seat.deal(); s.seat.start(); s.seat.play();
  assert.deepEqual(s.events[0][1].graded, ['w', 'b']);
  assert.deepEqual(s.events[0][1].sides, []);
  assert.equal(s.events[1][1].white.name, 'Ada');
  s.run(PACE.first + 0.2);
  assert.equal(s.seat.plies(), 1);
  s.pause(true); s.run(10);
  assert.equal(s.seat.plies(), 1, 'the pause card holds the replay');
  s.pause(false); s.run(SCHOLAR.length * (PACE.gap + 0.2));
  assert.ok(s.seat.isOver());
  assert.deepEqual(s.seat.result(), { result: 'checkmate', winner: 'w', reason: 'checkmate' });
  assert.deepEqual(s.done, ['m1']);
  assert.equal(s.names().filter((n) => n === 'gameover').length, 1);
  // still interactive: back one, then to the end again, and done is not said twice
  s.seat.step(-1);
  assert.ok(!s.seat.isOver() && s.seat.plies() === SCHOLAR.length - 1);
  s.seat.step(1);
  assert.ok(!s.seat.isOver(), 'a step onto the last move lets that move land first');
  s.run(PACE.end + 0.2);
  assert.ok(s.seat.isOver());
  s.seat.seek(3);
  assert.equal(s.seat.rules.chess.history().length, 3);
  assert.deepEqual(s.done, ['m1']);
  assert.equal(s.seat.canPick('e2'), false);
  assert.equal(s.seat.tryMove('e2', 'e4'), null);
});

test('en passant lifts the pawn it passed and a castle moves the rook', () => {
  const s = stage(BUSY);
  s.seat.start(); s.seat.seek(6); s.calls.length = 0; s.seat.step(1);
  assert.deepEqual(s.calls.filter((c) => c[0] === 'move'), [['move', 'e1', 'g1'], ['move', 'h1', 'f1']]);
  s.seat.seek(20); s.calls.length = 0; s.seat.step(1);
  assert.deepEqual(s.calls[0], ['remove', 'g5']);
});

test('a resigned game ends on the record, and the reel holds its last frame', () => {
  const s = stage(LEGAL.slice(0, 10), { result: '0-1', reason: 'resign', reel: true });
  s.seat.deal(); s.seat.start(); s.seat.startReel();
  s.run(60);
  assert.deepEqual(s.done, ['m1']);
  assert.deepEqual(s.seat.result(), { result: 'resign', winner: 'b', reason: 'resign' });
  assert.equal(s.seat.plies(), 10);
  const before = s.events.length;
  s.seat.play(); s.seat.toggle(); s.run(5);
  assert.equal(s.events.length, before, 'a reel has no controls');
  assert.ok(s.seat.state().caption.move.startsWith('Move '));
});

// The cup helper's report (2026-11-13): the resign fall finished before the last move had. A move is
// "landing" for `slide` seconds after pieces.move; gameover must come after it, a PACE.end beat later.
function landing(slide) {
  let left = 0;
  return { anim: { busy: () => left > 0, skip() { left = 0; } }, moved() { left = slide; }, tick(dt) { left = Math.max(0, left - dt); } };
}
function stageLanding(moves, extra, slide) {
  const l = landing(slide);
  const s = stage(moves, extra, l.anim);
  s.seat.start();
  const log = [];   // [time, what]
  let now = 0;
  const step = (dt = 0.05) => {
    const before = s.calls.length, ev = s.events.length;
    s.seat.update(dt); l.tick(dt); now += dt;
    for (const c of s.calls.slice(before)) if (c[0] === 'move') { l.moved(); log.push([now, 'move']); }
    for (const e of s.events.slice(ev)) if (e[0] === 'gameover') log.push([now, 'gameover']);
  };
  return { ...s, log, step, run: (sec) => { for (let t = 0; t < sec; t += 0.05) step(); }, landing: l };
}

test('the end waits for the last move to land: whole game, resigned', () => {
  const slide = 2.5;   // longer than PACE.gap, like a capture's signature move
  const s = stageLanding(LEGAL.slice(0, 10), { result: '0-1', reason: 'resign' }, slide);
  s.seat.deal(); s.seat.play();
  s.run(10 * (PACE.gap + slide) + 5);
  assert.ok(s.seat.isOver());
  const moves = s.log.filter((e) => e[1] === 'move'), over = s.log.filter((e) => e[1] === 'gameover');
  assert.equal(moves.length, 10);
  assert.equal(over.length, 1);
  for (let k = 1; k < moves.length; k++) assert.ok(moves[k][0] - moves[k - 1][0] >= slide, 'each move waits for the one before it to land');
  assert.ok(over[0][0] - moves[9][0] >= slide + PACE.end - 0.06, 'the resign comes after the last move has landed, a beat later');
  assert.deepEqual(s.done, ['m1'], 'done is said after the end, once');
});

test('the end waits for the last move to land: highlights reel', () => {
  const slide = 2.5;
  const s = stageLanding(LEGAL.slice(0, 10), { result: '0-1', reason: 'resign', reel: true }, slide);
  s.seat.deal(); s.seat.startReel();
  let doneAt = null;
  for (let t = 0; t < 80 && doneAt == null; t += 0.05) { s.step(); if (s.done.length) doneAt = t; }
  const moves = s.log.filter((e) => e[1] === 'move'), over = s.log.filter((e) => e[1] === 'gameover');
  assert.equal(over.length, 1);
  const last = moves[moves.length - 1][0];
  assert.ok(over[0][0] - last >= slide + PACE.end - 0.06, 'the resign comes after the last move has landed');
  assert.ok(doneAt != null && doneAt + 0.05 - over[0][0] >= PACE.hold - 0.06, 'the reel holds the fallen king before it is done');
});

test('a seek to the end ends at once: nothing is animating', () => {
  const s = stage(SCHOLAR);
  s.seat.start(); s.seat.seek(SCHOLAR.length);
  assert.ok(s.seat.isOver());
  s.seat.seek(2);
  s.run(5);
  assert.ok(!s.seat.isOver(), 'a seek back clears an owed end');
});

test('no PBP_REPLAY: the boot path is the one it always was', () => {
  const boot = readFileSync(new URL('../boot.js', import.meta.url), 'utf8');
  assert.ok(boot.includes('window.PBP_CAPS = { replay: 1 };'));
  // the replay modules are only ever imported inside startHostReplay, and that is only reached with an ask
  for (const mod of ['./game/cup-replay.js', './net/replay-seat.js', './ui/replay-bar.js']) {
    assert.equal(boot.split(mod).length, 2, mod + ' is imported in one place');
    assert.ok(!boot.split('\n').some((line) => line.startsWith('import ') && line.includes(mod)), mod + ' is never a static import');
  }
  assert.ok(boot.includes("if (replayAsk && await startHostReplay(replayAsk, { bus, board, game })) return null;"));
  assert.ok(boot.includes("attachEffects(bus, board, replayAsk ? new URLSearchParams('media=none') : params)"));
  assert.ok(boot.includes('if (window.PBP_REPLAY) return Promise.resolve(null);'));
  assert.ok(boot.includes('if (window.PBP.game?.isReplay) {'));
  assert.equal(boot.split('replayAsk').length, 5, 'read once, used at the door and nowhere else');
});
