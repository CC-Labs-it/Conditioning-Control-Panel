/* ============================================================================
 * smoke/intro-smoke.mjs - the board intro's clock (board/intro-plan.js).
 *
 *   A  order: rooks first, pawns from the rook files in, the court after
 *   B  the whole intro fits 2.0-2.6 s at full motion; reduced is short and calm
 *   C  a skip lands every man at once and the brass is gone fast
 *   D  levels: off never plays, nor does a game with moves on it
 *   E  the ratchet climbs in clicks and the rooks tick once per click
 *   F  wiring: boot hands the draw through intro.render, Escape is never heard
 *
 *   node smoke/intro-smoke.mjs        (no server, no browser)
 * ==========================================================================*/

import { readFileSync } from 'node:fs';
import { INTRO, introPlan, introLevel, shouldPlay, manPose, gearState, wheelState, ratchet, clicksBetween, introOver } from '../board/intro-plan.js';

let failed = 0;
let passed = 0;
function expect(cond, name) {
  if (cond) { passed++; console.log('  ok   ' + name); }
  else { failed++; console.error('  FAIL ' + name); }
}

// the opening position
const BACK = 'rnbqkbnr';
const men = [];
for (let f = 0; f < 8; f++) {
  const file = 'abcdefgh'[f];
  men.push({ sq: file + '1', type: BACK[f] }, { sq: file + '2', type: 'p' }, { sq: file + '7', type: 'p' }, { sq: file + '8', type: BACK[f] });
}
const by = (plan, sq) => plan.men.find(m => m.sq === sq);

/* ---- A: order ------------------------------------------------------------------ */
{
  const plan = introPlan(men, 'full');
  expect(plan.men.length === 32, 'every man has a place in the plan');
  const rookStart = Math.max(...plan.men.filter(m => m.type === 'r').map(m => m.start));
  const others = plan.men.filter(m => m.type !== 'r');
  expect(others.every(m => m.start > rookStart), 'the rooks rise before anyone else');
  expect(by(plan, 'a2').start < by(plan, 'b2').start && by(plan, 'b2').start < by(plan, 'd2').start, 'pawns come in from the rook files');
  expect(by(plan, 'a2').start === by(plan, 'h2').start && by(plan, 'a2').start === by(plan, 'a7').start, 'both sides and both wings move together');
  expect(by(plan, 'b1').start < by(plan, 'c1').start && by(plan, 'c1').start < by(plan, 'e1').start, 'the court comes in from the knights');
  expect(by(plan, 'd1').start === by(plan, 'e1').start, 'the queen and the king rise together');
  expect(by(plan, 'a1').dir !== by(plan, 'b1').dir && by(plan, 'a1').dir !== by(plan, 'a2').dir, 'neighbouring gears turn against each other');
  expect(plan.men.every(m => m.start >= INTRO.gearIn), 'every gear is up before its man starts');
}

/* ---- B: length ------------------------------------------------------------------ */
{
  const full = introPlan(men, 'full');
  expect(full.total >= 2.0 && full.total <= 2.6, `full intro lasts 2.0-2.6 s (${full.total.toFixed(2)})`);
  expect(full.end < full.total, 'the brass goes out after the last man lands');
  const reduced = introPlan(men, 'reduced');
  expect(reduced.total > 0 && reduced.total <= 0.7, `reduced intro is short (${reduced.total.toFixed(2)})`);
  expect(reduced.men.every(m => manPose(m, m.start + m.dur * 0.5, 'reduced').spin === 0), 'reduced: nobody spins');
  expect(clicksBetween(reduced, 0, 10) === 0, 'reduced: no ticks');
  const mid = full.men.find(m => m.type === 'n');
  expect(Math.abs(manPose(mid, mid.start + mid.dur * 0.3, 'full').spin) > 0, 'full: a man turns on his way up');
  expect(introOver(full, full.total) && !introOver(full, full.total - 0.05), 'the intro is over at its total and not before');
  expect(full.men.every(m => gearState(m, full.total).k === 0), 'no gear is left up at the end');
  expect(wheelState(full, full.total).k < 0.001, 'the big wheels are down at the end');
  expect(wheelState(full, 1.0).k > 0.9, 'the big wheels are up while the men rise');
}

/* ---- C: skip ------------------------------------------------------------------- */
{
  const plan = introPlan(men, 'full');
  const skipAt = 0.4;
  const t = skipAt + 0.001;
  expect(plan.men.every(m => { const p = manPose(m, t, 'full', skipAt); return p.lift === 1 && p.spin === 0 && !p.hidden; }), 'a skip lands every man on his square at once');
  expect(plan.men.every(m => gearState(m, skipAt + INTRO.skipOut, skipAt).k === 0), 'a skip takes every gear out within skipOut');
  expect(wheelState(plan, skipAt + INTRO.skipOut, skipAt).k === 0, 'a skip takes the wheels out within skipOut');
  expect(INTRO.skipOut <= 0.25, 'skipOut is fast');
  expect(introOver(plan, skipAt + INTRO.skipOut, skipAt) && !introOver(plan, skipAt + 0.05, skipAt), 'a skipped intro is over once the brass is out');
  const before = manPose(by(plan, 'e1'), 0.1, 'full');
  expect(before.hidden && before.lift === 0, 'before his start a man is all the way down and hidden');
}

/* ---- D: levels ----------------------------------------------------------------- */
{
  expect(introLevel({}) === 'full', 'on by default at full motion');
  expect(introLevel({ reduced: true }) === 'reduced', 'reduced motion plays the calm lift');
  expect(introLevel({ enabled: false }) === 'off' && introLevel({ enabled: false, reduced: true }) === 'off', 'the Board intro box off means no intro');
  expect(shouldPlay({ level: 'full', plies: 0 }) && shouldPlay({ level: 'reduced', plies: 0 }), 'a new game plays it');
  expect(!shouldPlay({ level: 'full', plies: 6 }), 'a game picked up mid-way never plays it');
  expect(!shouldPlay({ level: 'off', plies: 0 }), 'off never plays it');
  const off = introPlan(men, 'off');
  expect(off.men.length === 0 && off.total === 0, 'off has nothing to draw');
}

/* ---- E: ratchet and ticks ------------------------------------------------------ */
{
  expect(ratchet(0, 6) === 0 && ratchet(1, 6) === 1, 'the ratchet runs 0 to 1');
  const steps = [];
  for (let i = 0; i <= 600; i++) steps.push(ratchet(i / 600, 6));
  expect(steps.every((v, i) => i === 0 || v >= steps[i - 1] - 1e-12), 'the ratchet never winds back');
  // holds: the last stretch of each click stands still
  expect(Math.abs(ratchet(0.16, 6) - ratchet(0.13, 6)) < 1e-9, 'each click ends in a hold');
  const plan = introPlan(men, 'full');
  expect(clicksBetween(plan, -1, plan.total) === INTRO.rookClicks, 'the rooks tick once per click, never once per man');
  let total = 0;
  for (let t = 0; t < plan.total; t += 1 / 60) total += clicksBetween(plan, t, t + 1 / 60);
  expect(total === INTRO.rookClicks, 'frame by frame the ticks add up the same');
}

/* ---- F: wiring ----------------------------------------------------------------- */
{
  const boot = readFileSync(new URL('../boot.js', import.meta.url), 'utf8');
  expect(/import\('\.\/board\/intro\.js'\)/.test(boot), 'boot loads the intro late and guarded');
  expect(/board\.intro\.render\(renderBase\)/.test(boot), 'the frame is drawn through intro.render');
  const intro = readFileSync(new URL('../board/intro.js', import.meta.url), 'utf8');
  expect(/e\.key !== 'Escape'/.test(intro), 'Escape never skips (it is the pause and panic key)');
  expect(!/preventDefault|stopPropagation|stopImmediatePropagation/.test(intro), 'the intro never swallows input');
  expect(/passive: true/.test(intro), 'its listeners are passive');
  expect(/p\.mode === 'watch'/.test(intro) && /p\.ply === 0/.test(intro), 'the stands arm only on a tape that starts at ply 0');
  expect(/\.dispose\(\)/.test(intro) && /scene\.remove\(/.test(intro), 'it takes its brass out of the scene and disposes it');
  const prefs = readFileSync(new URL('../game/preferences.js', import.meta.url), 'utf8');
  expect(/boardIntro: saved\.boardIntro !== false/.test(prefs), 'Board intro is on unless the player turned it off');
  const html = readFileSync(new URL('../index.html', import.meta.url), 'utf8');
  expect(/id="game-board-intro" checked/.test(html), 'Options carries the Board intro box');
}

console.log(`\nintro smoke: ${passed} passed, ${failed} failed`);
if (failed) process.exit(1);
