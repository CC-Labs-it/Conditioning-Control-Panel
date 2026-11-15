/* ============================================================================
 * smoke/hand-aim.test.mjs - a slow or wobbly click never plays a man one
 * square back, and the square a click plays is the square that was lit
 * (ccp-bugs #1621, weekend cup round 1).
 *
 *   node --test smoke/hand-aim.test.mjs
 *
 * No browser: board/hand.js is three-free, the camera is the `normal` preset
 * from board/camera.js, squares are board/scene.js worldToSquare, and the
 * referee is game/rules.js. Source checks pin the wiring into board/drag.js.
 * ==========================================================================*/

import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { isClick, crossAt, holdOffset, heldAt, clickTarget } from '../board/hand.js';
import { createRules } from '../game/rules.js';

const src = (path) => readFileSync(new URL(path, import.meta.url), 'utf8');

// board/scene.js, SQUARE = 1
const toWorld = (sq) => ({ x: sq.charCodeAt(0) - 97 - 3.5, y: 0, z: 3.5 - (Number(sq[1]) - 1) });
const toSquare = (x, z) => {
  const f = Math.round(x + 3.5);
  const r = Math.round(3.5 - z);
  return f < 0 || f > 7 || r < 0 || r > 7 ? null : 'abcdefgh'[f] + (r + 1);
};

// board/camera.js `normal`: 9.7 out, 7 up, looking at y 0.10, from the mover's side.
// The fit can push the camera farther out on a narrow window, so try a few.
function camera(side, scale = 1) {
  const s = side === 'b' ? -1 : 1;
  return { x: 0, y: 0.10 + 7 * scale, z: 9.7 * scale * s };
}
const rayThrough = (o, p) => {
  const d = { x: p.x - o.x, y: p.y - o.y, z: p.z - o.z };
  const n = Math.hypot(d.x, d.y, d.z);
  return { x: d.x / n, y: d.y / n, z: d.z / n };
};

// board/drag.js TUNING
const LIFT = 0.52;
const STILL_PX = 12;

/**
 * Press a man on `sq` at height h on the side of his body facing the camera,
 * and do not move the pointer. Where does his base stand, the old way and the
 * new way?
 */
function stillPress(sq, side, h, scale) {
  const base = toWorld(sq);
  const toward = side === 'b' ? -1 : 1;
  const point = { x: base.x, y: h, z: base.z + 0.22 * toward };
  const o = camera(side, scale);
  const d = rayThrough(o, point);
  const holdY = LIFT + h;
  const hit = crossAt(o, d, holdY);
  const oldAt = { x: hit.x + (base.x - point.x), z: hit.z + (base.z - point.z) };  // before #1621
  const newAt = heldAt(holdOffset(base, o, d, holdY), o, d, holdY);
  return { old: toSquare(oldAt.x, oldAt.z), now: toSquare(newAt.x, newAt.z), newAt, base };
}

test('the bug, reproduced: a still press held past the click time stood the man one square back', () => {
  // The old offset, measured at the grabbed spot, moved a lifted man toward the camera.
  assert.equal(stillPress('e2', 'w', 0.4, 1).old, 'e1');
  assert.equal(stillPress('d4', 'w', 0.4, 1).old, 'd3');
  assert.equal(stillPress('e7', 'b', 0.4, 1).old, 'e8');
  // ... and e1 is a legal rook move there, so letting go played it.
  const rules = createRules('4k3/8/8/8/8/8/4R3/K7 w - - 0 1');
  assert.ok(rules.movesFrom('e2').some((m) => m.to === 'e1'));
});

test('a man lifted under a cursor that has not moved stays over his own square', () => {
  for (const [sq, side] of [['e2', 'w'], ['d4', 'w'], ['h8', 'w'], ['a1', 'w'], ['e7', 'b'], ['d5', 'b']]) {
    for (const h of [0.04, 0.2, 0.45, 0.75]) {
      for (const scale of [1, 1.3, 1.7]) {
        const r = stillPress(sq, side, h, scale);
        assert.equal(r.now, sq, `${sq} ${side} grab ${h} camera x${scale}`);
        assert.ok(Math.hypot(r.newAt.x - r.base.x, r.newAt.z - r.base.z) < 1e-9, 'exactly over his square');
      }
    }
  }
});

test('he still follows the cursor: moving the ray moves him by the same amount on the hold plane', () => {
  const base = toWorld('d4');
  const o = camera('w');
  const holdY = LIFT + 0.4;
  const d0 = rayThrough(o, { x: base.x, y: 0.4, z: base.z + 0.2 });
  const off = holdOffset(base, o, d0, holdY);
  const p0 = crossAt(o, d0, holdY);
  // aim the cursor one square toward the far side, on the hold plane
  const d1 = rayThrough(o, { x: p0.x, y: holdY, z: p0.z - 1 });
  const at = heldAt(off, o, d1, holdY);
  assert.equal(toSquare(at.x, at.z), 'd5');
  assert.ok(Math.abs(at.z - (base.z - 1)) < 1e-9);
});

test('a press that never travelled is a click, however long it lasted', () => {
  // Rook pressed on d4 and held still for 300 ms (past the old 250 ms click limit).
  assert.equal(isClick({ x: 400, y: 300, far: 0 }, 400, 300, STILL_PX), true);
  // a small wobble is still a click
  assert.equal(isClick({ x: 400, y: 300, far: 9 }, 405, 303, STILL_PX), true);
  assert.equal(isClick({ x: 400, y: 300, far: 0 }, 408, 308, STILL_PX), true);
  // a real drag is not
  assert.equal(isClick({ x: 400, y: 300, far: 0 }, 440, 300, STILL_PX), false);
  // nor a drag that came back to where it started (he springs back instead)
  assert.equal(isClick({ x: 400, y: 300, far: 60 }, 401, 300, STILL_PX), false);
  assert.equal(isClick(null, 0, 0, STILL_PX), false);
});

function seat(fen, from) {
  const rules = createRules(fen);
  const pos = rules.position();
  const legal = rules.movesFrom(from).map((m) => m.to);
  const side = fen.split(' ')[1];
  return { rules, legal, canPick: (sq) => !!pos[sq] && pos[sq].side === side };
}

test('click-click: a man in front cannot take the click meant for the lit square behind him', () => {
  // White rook a3 waits; a black knight on b2 covers part of b3 from white's camera.
  const s = seat('4k3/8/8/8/8/R7/1n6/4K3 w - - 0 1', 'a3');
  assert.ok(s.legal.includes('b3') && !s.legal.includes('b2'));
  // The ray hits the knight's body first, and the board at b3.
  const sq = clickTarget('b2', 'b3', s.legal, s.canPick);
  assert.equal(sq, 'b3', 'the lit square is played, the selection is not silently dropped');
  assert.ok(s.rules.move('a3', sq));
});

test('click-click: one rule for every other case', () => {
  // a man the waiting queen may take answers for himself
  const q = seat('4k3/8/8/8/Qp6/8/8/4K3 w - - 0 1', 'a4');
  assert.equal(clickTarget('b4', 'b5', q.legal, q.canPick), 'b4');
  // an empty legal square answers when no man is hit
  assert.equal(clickTarget(null, 'b5', q.legal, q.canPick), 'b5');
  // one of your own men answers for himself (the selection moves over), even over a legal square
  const own = seat('4k3/8/8/8/8/R7/1N6/4K3 w - - 0 1', 'a3');
  assert.equal(clickTarget('b2', 'b3', own.legal, own.canPick), 'b2');
  // the king's own rook answers for himself, which is how a click castles
  const c = seat('r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1', 'e1');
  assert.equal(clickTarget('h1', 'h2', c.legal, c.canPick), 'h1');
  // nothing legal under the cursor: the man hit is named, and the click lets go
  assert.equal(clickTarget('b2', 'c2', s0().legal, s0().canPick), 'b2');
  assert.equal(clickTarget(null, null, [], () => false), null);
});
const s0 = () => seat('4k3/8/8/8/8/R7/1n6/4K3 w - - 0 1', 'a3');

test('board/drag.js plays by these rules', () => {
  const drag = src('../board/drag.js');
  assert.match(drag, /from '\.\/hand\.js'/);
  assert.match(drag, /holdOffset\(base, ray\.ray\.origin, ray\.ray\.direction, holdY\)/, 'offset measured on the hold plane');
  assert.doesNotMatch(drag, /grab\.set\(base\.x - found\.point\.x/, 'no offset taken at the grabbed spot');
  assert.match(drag, /const tap = isClick\(press, ev\.clientX, ev\.clientY, T\.stillPx\)/);
  assert.match(drag, /if \(tap\) \{/, 'a click never plays a move, whatever square his base drifted over');
  assert.equal((drag.match(/targetFor\(/g) || []).length, 3, 'defined once, used by look() and onDown()');
  assert.match(drag, /targetFor\(manSq, squareUnder\(null\)\)/, 'look() lights the shared answer');
  assert.match(drag, /const under = targetFor\(/, 'onDown() plays the shared answer');
  // the scene's square maths is the one this test copies
  assert.match(src('../board/scene.js'), /Math\.round\(x \/ SQUARE \+ 3\.5\)/);
  assert.match(src('../board/scene.js'), /Math\.round\(3\.5 - z \/ SQUARE\)/);
  assert.match(src('../board/camera.js'), /normal: \{ phi: Math\.atan2\(9\.7, 7\), radius: Math\.hypot\(9\.7, 7\), target: \[0, 0\.10, 0\] \}/);
});
