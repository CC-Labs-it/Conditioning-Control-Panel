/* ============================================================================
 * smoke/ambient-smoke.mjs - the stands' own calm Distraction for players whose
 * page sends no effects record (ramp/ambient.js), pure and through attachRamp:
 *
 *   node smoke/ambient-smoke.mjs
 *
 * Owner run 2026-10-14: a watched game between two released 7.1.8 pages carried
 * no fx at all on the tape, so the stands showed no pictures. Now, until a side
 * sends its first record, the stands run a low heat and a picture on captures,
 * checks, every few plies and long thinks, in the watcher's own pictures.
 * ==========================================================================*/

import { createBus } from '../game/events.js';
import { createAmbient, AMBIENT_TUNING } from '../ramp/ambient.js';

let clock = 1000;
const frames = [];
globalThis.performance = { now: () => clock };
globalThis.requestAnimationFrame = (fn) => { frames.push(fn); return frames.length; };
globalThis.cancelAnimationFrame = () => {};
const game = { side: 'w', over: false, turn() { return this.side; }, isOver() { return this.over; } };
globalThis.window = { PBP: { game, isPaused: () => false, door: { isUp: () => false }, board: { director: { holding: () => false } } } };

const { attachRamp } = await import('../ramp/index.js');
const { createFixtureMedia } = await import('../ramp/media.js');
const { setPresentation, presentation } = await import('../game/preferences.js');

let passed = 0;
const failures = [];
const check = (name, cond, detail) => { if (cond) passed += 1; else failures.push(name + (detail ? ' -> ' + detail : '')); };

function wait(ms) {
  const end = clock + ms;
  while (clock < end) {
    clock = Math.min(end, clock + 90);
    for (const fn of frames.splice(0)) fn();
  }
}

/* ---- the plan, pure ------------------------------------------------------------- */
{
  const a = createAmbient();
  a.reset(0, 'w');
  check('fresh: both sides run the ambient', a.active('w') && a.active('b'));
  check('the heat starts at nothing', a.heat(0) === 0);
  check('climbs to a low ceiling and stops there', Math.abs(a.heat(AMBIENT_TUNING.climbMs * 3) - AMBIENT_TUNING.heatMax) < 1e-9 && AMBIENT_TUNING.heatMax <= 0.35);
  check('a quiet move owes nothing', a.move('b', 1000) === 0);
  a.capture('b');
  check('a capture owes one picture to its move', a.move('w', 2000) === 1);
  a.check('b');   // white gives check: black is in check
  check('a check owes one too', a.move('b', 3000) === 1);
  check('the pacing: every fourth ply a picture, capture or not', a.move('w', 4000) === 1);
  check('no long-think picture early', a.tick(4000 + 1000) === 0);
  check('one on a long think', a.tick(4000 + AMBIENT_TUNING.thinkEveryMs) === 1);
  check('and not again at once', a.tick(4000 + AMBIENT_TUNING.thinkEveryMs + 90) === 0);
  a.noteFx('w');
  check('a record from white ends white\'s ambient only', !a.active('w') && a.active('b'));
  check('no heat of ours on white\'s turn now', a.heat(9000) === null);
  a.capture('w');
  check('white\'s capture owes nothing now', a.move('b', 10000) === 0);
  check('black\'s turn still climbs', a.heat(10000 + AMBIENT_TUNING.climbMs) > 0.29);
  a.reset(0);
  check('a new match forgets the records', a.active('w') && a.active('b'));
  a.noteFx('x');
  check('a nonsense side changes nothing', a.active('w') && a.active('b'));
}

/* ---- through the ramp: the stands with pictures ------------------------------------ */
const warn = console.warn;
console.warn = () => {};
const pictures = createFixtureMedia([
  { kind: 'image', url: 'a.jpg' }, { kind: 'image', url: 'b.jpg' }, { kind: 'gif', url: 'c.gif' },
]);
check('on by default', presentation().watchAmbient === true);
const turn = (bus, side, ply) => bus.emit('turn', { side, ply, clocks: { w: 300000, b: 300000 }, total: 300000 });
{
  const bus = createBus();
  const ramp = attachRamp({ bus, seed: 'ambient', media: pictures });
  const dbg = () => ramp.debug();
  const flashes = () => dbg().layers.counts.flash || 0;
  bus.emit('local', { sides: [], graded: ['w', 'b'], mode: 'watch' });
  turn(bus, 'w', 0);
  check('the stands know the ambient is on and running', dbg().replay.ambient.on && dbg().replay.ambient.active, JSON.stringify(dbg().replay.ambient));
  wait(20000);
  const h = dbg().replay.heat;
  check('no record: the heat climbs on its own, gently', h > 0.05 && h <= AMBIENT_TUNING.heatMax + 1e-9, String(h));
  check('nothing pops before a long think', flashes() === 0);
  wait(6000);
  check('one picture on a long think (25 s)', flashes() === 1, String(flashes()));
  // a capture with check, the way net/watch.js says it: capture, check, then the turn
  bus.emit('capture', { by: 'w', piece: 'n', square: 'f6', victimSide: 'b' });
  bus.emit('check', { side: 'b' });
  turn(bus, 'b', 1);
  check('a move is still the breath', dbg().replay.heat === 0);
  check('the capture and the check each pop a picture after it', flashes() === 3, String(flashes()));
  turn(bus, 'w', 2);
  turn(bus, 'b', 3);
  check('quiet moves pop nothing', flashes() === 3, String(flashes()));
  turn(bus, 'w', 4);
  check('the fourth ply brings one', flashes() === 4, String(flashes()));
  bus.emit('watch-effects', { on: true, amount: 'less' });
  bus.emit('capture', { by: 'w', piece: 'p', square: 'e5', victimSide: 'b' });
  turn(bus, 'b', 5);
  bus.emit('capture', { by: 'b', piece: 'p', square: 'e5', victimSide: 'w' });
  turn(bus, 'w', 6);
  check('the Amount dial counts: less is about half', flashes() === 5, String(flashes()));
  bus.emit('watch-effects', { on: true, amount: 'normal' });

  // white's page starts sending records (a newer version): white's turns are the real thing from now on
  bus.emit('fx-heat', { side: 'w', heat: 0.7 });
  wait(4000);
  check('a real record wins: the tape\'s heat, not ours', dbg().replay.heat > 0.6 && !dbg().replay.ambient.active, String(dbg().replay.heat));
  bus.emit('capture', { by: 'w', piece: 'b', square: 'c6', victimSide: 'b' });
  turn(bus, 'b', 7);
  check('white\'s capture pops nothing of ours', flashes() === 5, String(flashes()));
  check('black still sends none: the ambient runs on black\'s turn', dbg().replay.ambient.active);

  setPresentation({ watchAmbient: false });
  wait(3000);
  check('Options off: no heat of ours', dbg().replay.heat < 0.01, String(dbg().replay.heat));
  bus.emit('capture', { by: 'b', piece: 'q', square: 'd1', victimSide: 'w' });
  turn(bus, 'w', 8);
  wait(30000);
  check('Options off: no pictures of ours', flashes() === 5, String(flashes()));
  setPresentation({ watchAmbient: true });

  bus.emit('local', { sides: [], graded: ['w', 'b'], mode: 'watch' });
  turn(bus, 'w', 0);
  check('a new watch starts the ambient again for both sides', dbg().replay.ambient.active && !dbg().replay.ambient.recorded.w);
  ramp.dispose();
}

/* ---- reduced motion, no pictures, and a seat: nothing pops ------------------------- */
{
  const bus = createBus();
  const ramp = attachRamp({ bus, seed: 'ambient-still', media: pictures });
  setPresentation({ reducedMotion: true });
  bus.emit('local', { sides: [], graded: ['w', 'b'], mode: 'watch' });
  turn(bus, 'w', 0);
  bus.emit('capture', { by: 'w', piece: 'n', square: 'f6', victimSide: 'b' });
  turn(bus, 'b', 1);
  wait(30000);
  check('reduced motion: no picture pops', !(ramp.debug().layers.counts.flash || 0));
  setPresentation({ reducedMotion: false });
  ramp.dispose();
}
{
  const bus = createBus();
  const ramp = attachRamp({ bus, seed: 'ambient-none', media: createFixtureMedia([]) });
  bus.emit('local', { sides: [], graded: ['w', 'b'], mode: 'watch' });
  turn(bus, 'w', 0);
  bus.emit('capture', { by: 'w', piece: 'n', square: 'f6', victimSide: 'b' });
  turn(bus, 'b', 1);
  wait(30000);
  check('no pictures picked: silent, never a blank card', !(ramp.debug().layers.counts.flash || 0));
  ramp.dispose();
}
{
  const bus = createBus();
  const ramp = attachRamp({ bus, seed: 'ambient-seat', media: pictures });
  game.side = 'w';
  bus.emit('local', { sides: ['w'], mode: 'online' });
  turn(bus, 'w', 0);
  check('a player\'s own page never runs the stands\' ambient', ramp.debug().replay === null);
  ramp.dispose();
}

console.warn = warn;
const total = passed + failures.length;
if (failures.length) {
  console.error(`FAIL  ${failures.length}/${total} ambient checks failed:`);
  for (const f of failures) console.error('  - ' + f);
  process.exit(1);
}
console.log(`ok  ${passed}/${total} ambient checks passed`);
