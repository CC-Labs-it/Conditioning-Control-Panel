/* ============================================================================
 * smoke/castle-calm.test.mjs - the castling hint, the readable board under the
 * Loom, and the Board motion switch (owner's cup thread, 2026-11-13).
 *
 *   node --test smoke/castle-calm.test.mjs
 *
 * No browser: the referee, the pure castle helper, the Loom's numbers and the
 * presentation store, plus source checks that the board wires them in. The
 * real board (drag, click, markers on screen, sway, Loom) is
 * smoke/castle-calm-shot.mjs.
 * ==========================================================================*/

import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createRules } from '../game/rules.js';
import { readCastles, castleAlias, castleRook } from '../game/castle-hint.js';
import { TURN_LOOM, turnOpacity, loomAlpha, readTurn } from '../game/turn-loom.js';
import { createClock } from '../game/clock.js';
import { presentation, setPresentation, boardMotionScale, BOARD_MOTION } from '../game/preferences.js';

const src = (path) => readFileSync(new URL(path, import.meta.url), 'utf8');
const castlesAt = (fen, square) => readCastles(createRules(fen).movesFrom(square));
const keys = (map) => [...map.keys()].sort();

test('both castles legal: the king gets both castle squares and both rooks', () => {
  const c = castlesAt('r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1', 'e1');
  assert.deepEqual(keys(c.targets), ['c1', 'g1']);
  assert.deepEqual(keys(c.rooks), ['a1', 'h1']);
  assert.equal(c.targets.get('g1'), 'h1');
  assert.equal(c.rooks.get('a1'), 'c1');
  const b = castlesAt('r3k2r/8/8/8/8/8/8/R3K2R b KQkq - 0 1', 'e8');
  assert.deepEqual(keys(b.rooks), ['a8', 'h8']);
});

test('king onto his own rook castles: the alias lands on the castle square, and the referee plays a castle', () => {
  const rules = createRules('r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1');
  const c = readCastles(rules.movesFrom('e1'));
  assert.equal(castleAlias(c, 'h1'), 'g1');
  assert.equal(castleAlias(c, 'a1'), 'c1');
  assert.equal(castleAlias(c, 'f1'), 'f1', 'any other square is left alone');
  const played = rules.move('e1', castleAlias(c, 'h1'));
  assert.equal(played.castle, 'k');
  const pos = rules.position();
  assert.deepEqual([pos.g1, pos.f1, pos.h1, pos.e1], [{ type: 'k', side: 'w' }, { type: 'r', side: 'w' }, undefined, undefined]);
});

test('the marker shows only while castling is legal', () => {
  // f8 rook covers f1: the king may not pass through it, so only the long castle remains
  const through = castlesAt('r3kr2/8/8/8/8/8/8/R3K2R w KQq - 0 1', 'e1');
  assert.deepEqual(keys(through.targets), ['c1']);
  assert.deepEqual(keys(through.rooks), ['a1']);
  assert.equal(castleAlias(through, 'h1'), 'h1', 'no alias for a castle that is not legal');
  // in check: no castle at all
  assert.equal(castlesAt('r3k2r/8/8/8/8/8/8/R3K1qR w KQkq - 0 1', 'e1').targets.size, 0);
  // rights gone (the king has moved before)
  assert.equal(castlesAt('r3k2r/8/8/8/8/8/8/R3K2R w - - 0 1', 'e1').targets.size, 0);
  // a man in the way (the opening position)
  assert.equal(castlesAt(undefined, 'e1').targets.size, 0);
  // a rook's own moves never read as a castle, and an empty list is empty
  assert.equal(castlesAt('r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1', 'h1').rooks.size, 0);
  assert.equal(readCastles().targets.size, 0);
  assert.equal(castleRook({ from: 'e1', to: 'f1', flags: 'n' }), null);
});

test('the board wires the castle hint into both the drag and the click', () => {
  const drag = src('../board/drag.js');
  assert.match(drag, /castleAlias\(castles, to\)/, 'a drop on the rook castles');
  assert.match(drag, /castleAlias\(selCastles, to\)/, 'a click on the rook castles');
  assert.match(drag, /play\(start, dest\)/);
  assert.match(drag, /'castle' : \(src\.castle\.rooks\.has\(sq\) \? 'castle-rook'/, 'own marker kinds');
  const markers = src('../board/markers.js');
  assert.match(markers, /kind === 'castle'\)/);
  assert.match(markers, /kind === 'castle-rook'\)/);
});

test('the Loom over the board is capped, so the squares stay readable', () => {
  assert.ok(TURN_LOOM.maxAlpha > 0 && TURN_LOOM.maxAlpha <= .6, 'cap at most 60%');
  assert.ok(TURN_LOOM.checker > 0 && TURN_LOOM.checker < 1);
  assert.equal(turnOpacity(90000), 1, 'the fade curve itself is unchanged');
  assert.equal(loomAlpha(9999), 0, 'still waits ten seconds');
  assert.equal(loomAlpha(16000), TURN_LOOM.maxAlpha / 2);
  assert.equal(loomAlpha(22000), TURN_LOOM.maxAlpha);
  assert.equal(loomAlpha(10 * 60 * 1000), TURN_LOOM.maxAlpha, 'a long think never covers the board');
  const clock = createClock({ perSideMs: 0, now: () => 0 }); clock.start('w');
  const rules = createRules();
  const game = { clock: { ...clock, isRunning: () => true, snapshot: () => ({ active: 'w' }), turnElapsedMs: () => 120000 },
    rules, current: null, isOver: () => false, turn: () => rules.turn(), plies: () => rules.ply() };
  assert.equal(readTurn(game, 'seed').alpha, TURN_LOOM.maxAlpha, 'what the board is handed is the capped value');
  const spiral = src('../board/turn-spiral.js');
  assert.match(spiral, /darkSquare \* \$\{TURN_LOOM\.checker/, 'dark squares are shaded inside the Loom');
  assert.match(spiral, /pbp-turn-loom-v2/, 'new shader, new program key');
});

test('Board motion: Full by default, Calm and Still lower it, junk is ignored', () => {
  assert.equal(presentation().boardMotion, 'full', 'default unchanged');
  assert.equal(boardMotionScale(), 1);
  assert.equal(setPresentation({ boardMotion: 'calm' }).boardMotion, 'calm');
  assert.equal(boardMotionScale(), BOARD_MOTION.calm);
  assert.ok(BOARD_MOTION.calm > 0 && BOARD_MOTION.calm < 1);
  assert.equal(setPresentation({ boardMotion: 'still' }).boardMotion, 'still');
  assert.equal(boardMotionScale(), 0);
  assert.equal(setPresentation({ boardMotion: 'spin-harder' }).boardMotion, 'still', 'an unknown value changes nothing');
  assert.equal(setPresentation({ boardMotion: 'full' }).boardMotion, 'full');
  assert.equal(boardMotionScale(), 1);
  assert.equal(boardMotionScale({}), 1, 'an older saved record reads as Full');
});

test('Board motion reaches the camera sway, the Loom turning and the Options panel', () => {
  assert.match(src('../ramp/index.js'), /setCameraSway\(clamp01\(m \* tuning\.swayScale \* boardMotionScale\(\)\)\)/);
  assert.match(src('../board/turn-spiral.js'), /const turning = still \? 0 : boardMotionScale\(pref\);/);
  const html = src('../index.html');
  for (const v of ['full', 'calm', 'still']) assert.match(html, new RegExp(`data-boardmotion="${v}"`));
  const hud = src('../hud.js');
  assert.match(hud, /setPresentation\(\{ boardMotion: button\.dataset\.boardmotion \}\)/);
  assert.match(hud, /button\.dataset\.boardmotion === p\.boardMotion/);
});
