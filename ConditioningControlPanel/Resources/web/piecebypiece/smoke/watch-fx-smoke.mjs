/* ============================================================================
 * smoke/watch-fx-smoke.mjs - Distraction in the stands (ramp/ replay mode) and
 * the player's side of it (ramp-heat / ramp-fired), driven through attachRamp
 * in node with hand-run frames, no DOM:
 *
 *   node smoke/watch-fx-smoke.mjs
 *
 * THE STANDS: a `local` with mode 'watch' puts the ramp in replay. Nothing is
 * scheduled; the heat eases toward the tape's fx-heat samples, fx-fire pops in
 * the spectator's OWN pictures through the Amount dial, only the turn player's
 * record counts, a move is the breath, watch-effects switches it, and with no
 * pictures at all the media layers stay silent (no blank card, no noise tile).
 * THE PLAYER: ramp-heat once a second during their own think, ramp-fired for
 * every picture or card the ramp fires.
 * ==========================================================================*/

import { createBus } from '../game/events.js';

let clock = 1000;
const frames = [];
globalThis.performance = { now: () => clock };
globalThis.requestAnimationFrame = (fn) => { frames.push(fn); return frames.length; };
globalThis.cancelAnimationFrame = () => {};
const game = { side: 'w', over: false, turn() { return this.side; }, isOver() { return this.over; } };
globalThis.window = { PBP: { game, isPaused: () => false, door: { isUp: () => false }, board: { director: { holding: () => false } } } };

const { attachRamp } = await import('../ramp/index.js');
const { createFixtureMedia } = await import('../ramp/media.js');
const { setPresentation } = await import('../game/preferences.js');
// These checks are about the players' own records; the stands' ambient for older pages has its own smoke.
setPresentation({ watchAmbient: false });

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

const warn = console.warn;
console.warn = () => {};
const pictures = createFixtureMedia([
  { kind: 'image', url: 'a.jpg' }, { kind: 'image', url: 'b.jpg' }, { kind: 'gif', url: 'c.gif' }, { kind: 'video', url: 'd.mp4' },
]);

/* ---- the stands, with pictures ---------------------------------------------- */
{
  const bus = createBus();
  const ramp = attachRamp({ bus, seed: 'watch-fx', media: pictures });
  const dbg = () => ramp.debug();
  const counts = () => dbg().layers.counts;
  bus.emit('local', { sides: [], graded: ['w', 'b'], mode: 'watch' });
  bus.emit('turn', { side: 'w', ply: 0, clocks: { w: 300000, b: 300000 }, total: 300000 });
  check('a watched game is replay mode, on by default', dbg().replay && dbg().enabled, JSON.stringify(dbg().replay));
  wait(20000);
  check('nothing climbs on its own in the stands', dbg().replay.heat === 0 && (counts().flash || 0) === 0);

  bus.emit('fx-heat', { side: 'w', heat: 0.8 });
  wait(200);
  const mid = dbg().replay.heat;
  check('the heat eases toward the tape\'s sample, not a step', mid > 0.05 && mid < 0.8, String(mid));
  wait(4000);
  check('and gets there', Math.abs(dbg().replay.heat - 0.8) < 0.02, String(dbg().replay.heat));
  bus.emit('fx-heat', { side: 'b', heat: 0.1 });
  wait(1000);
  check('the other side\'s record is ignored: only the turn player counts', Math.abs(dbg().replay.heat - 0.8) < 0.01);

  bus.emit('fx-fire', { side: 'w', kind: 'flash', holdMs: 0 });
  bus.emit('fx-fire', { side: 'w', kind: 'gifRain', holdMs: 0 });
  check('a fired picture pops in the spectator\'s own pictures', counts().flash === 1 && counts().gifRain === 1, JSON.stringify(counts()));
  bus.emit('fx-fire', { side: 'b', kind: 'flash' });
  check('a fire from the side not on the move is dropped', counts().flash === 1);
  bus.emit('fx-fire', { side: 'w', kind: 'video', holdMs: 9000 });
  check('a card fires too', counts().videoCard === 1);
  bus.emit('fx-fire', { side: 'w', kind: 'spiral' });
  bus.emit('fx-fire', { side: 'w', kind: 'nonsense' });
  check('a sustained or unknown kind is not a one-shot', counts().flash === 1 && counts().gifRain === 1 && counts().videoCard === 1);

  bus.emit('watch-effects', { on: true, amount: 'less' });
  for (let i = 0; i < 4; i++) bus.emit('fx-fire', { side: 'w', kind: 'flash' });
  check('Amount less: about half the fires', counts().flash === 3, String(counts().flash));
  bus.emit('watch-effects', { on: true, amount: 'more' });
  for (let i = 0; i < 5; i++) bus.emit('fx-fire', { side: 'w', kind: 'flash' });
  check('Amount more: more pops than fires', counts().flash === 3 + 8, String(counts().flash));
  bus.emit('watch-effects', { on: true, amount: 'normal', media: false });
  const held = { ...counts() };
  for (const kind of ['flash', 'gifRain', 'video']) bus.emit('fx-fire', { side: 'w', kind, holdMs: 8000 });
  check('media:false (the host has no source picked): the media layers stay silent', counts().flash === held.flash && counts().gifRain === held.gifRain && counts().videoCard === held.videoCard);
  check('while the stands stay on for the media-free layers', dbg().enabled && !dbg().replay.pictures);
  bus.emit('watch-effects', { on: true, amount: 'normal', media: true });

  bus.emit('turn', { side: 'b', ply: 1, clocks: { w: 290000, b: 300000 }, total: 300000 });
  check('a move is the breath in the stands too', dbg().replay.heat === 0 && dbg().replay.turn === 'b');
  bus.emit('turn', { side: 'b', ply: 1, clocks: { w: 290000, b: 300000 }, total: 300000 });
  check('a resync repeating the turn is not a move', dbg().replay.turn === 'b');

  setPresentation({ experience: 'classic' });
  check('the spectator\'s own Experience pick does not switch the stands off', dbg().enabled);
  setPresentation({ experience: 'distraction' });
  bus.emit('watch-effects', { on: false, amount: 'normal' });
  check('the stands\' own switch does', !dbg().enabled);
  const before = counts().flash;
  bus.emit('fx-fire', { side: 'b', kind: 'flash' });
  check('and nothing fires while it is off', counts().flash === before);
  bus.emit('watch-effects', { on: true });
  check('and back on', dbg().enabled);
  bus.emit('gameover', { result: 'resign', winner: 'w' });
  check('the end goes quiet in the stands: no fall, it was not our loss', !dbg().enabled && !dbg().surge);

  // the person's own Strength never reaches the stands (the slider is seat-only)
  setPresentation({ strength: 0.3 });
  bus.emit('local', { sides: [], graded: ['w', 'b'], mode: 'watch' });
  check('the stands use the default Strength, not the saved one', dbg().replay && dbg().replay.strength === 1 && dbg().dials.strength === 0.3, JSON.stringify(dbg().dials));

  // out of the stands, three ways: the ramp is back on the person's own Experience
  for (const [how, emitLeave] of [
    ['Leave', () => bus.emit('watch-leave')],
    ['a closed watch', () => bus.emit('watch-closed', { matchId: 'm', reason: 'not_watchable' })],
    ['a new deal', () => bus.emit('newgame', { ply: 0 })],
  ]) {
    setPresentation({ experience: 'classic' });
    bus.emit('local', { sides: [], graded: ['w', 'b'], mode: 'watch' });
    bus.emit('turn', { side: 'w', ply: 0, clocks: { w: 300000, b: 300000 }, total: 300000 });
    bus.emit('fx-heat', { side: 'w', heat: 0.9 });
    wait(2000);
    check(`in the stands with Classic picked, the stands still run (${how})`, dbg().enabled && dbg().replay.heat > 0.5);
    emitLeave();
    check(`after ${how} replay is off and zeroed`, dbg().replay === null);
    check(`after ${how} the ramp is idle for Classic`, !dbg().enabled);
    const before = dbg().layers.counts.flash || 0;
    bus.emit('fx-fire', { side: 'w', kind: 'flash' });
    wait(3000);
    check(`after ${how} a stray fx does nothing`, (dbg().layers.counts.flash || 0) === before && dbg().meter === 0);
  }
  setPresentation({ experience: 'distraction', strength: 1 });
  ramp.dispose();
}

/* ---- a seat with reduced motion: nothing drawn, everything told ----------------- */
{
  const bus = createBus();
  const ramp = attachRamp({ bus, seed: 'watch-fx-still', media: pictures });
  const fired = [];
  bus.on('ramp-fired', (p) => fired.push(p.kind));
  setPresentation({ reducedMotion: true });
  game.side = 'w';
  bus.emit('local', { sides: ['w'], mode: 'online' });
  bus.emit('turn', { side: 'w', ply: 0, clocks: { w: 300000, b: 300000 }, total: 300000 });
  wait(45000);
  const c = ramp.debug().layers.counts;
  check('reduced motion draws no picture', !c.flash && !c.gifRain && !c.videoCard, JSON.stringify(c));
  check('but the stands are still told what the schedule fired', fired.some((k) => k === 'flash' || k === 'gifRain'), JSON.stringify(fired.slice(0, 4)));
  check('the card included', fired.includes('video'), JSON.stringify(fired));
  setPresentation({ reducedMotion: false });
  ramp.dispose();
}

/* ---- the stands, with no pictures picked ----------------------------------------- */
{
  const bus = createBus();
  const ramp = attachRamp({ bus, seed: 'watch-fx-none', media: createFixtureMedia([]) });
  const dbg = () => ramp.debug();
  bus.emit('local', { sides: [], graded: ['w', 'b'], mode: 'watch' });
  bus.emit('turn', { side: 'w', ply: 0, clocks: { w: 300000, b: 300000 }, total: 300000 });
  bus.emit('fx-heat', { side: 'w', heat: 1 });
  wait(3000);
  check('no pictures: the heat still drives the media-free layers', dbg().replay.heat > 0.95 && !dbg().replay.pictures);
  for (const kind of ['flash', 'gifRain', 'video']) bus.emit('fx-fire', { side: 'w', kind, holdMs: 8000 });
  const c = dbg().layers.counts;
  check('no pictures: the media layers stay silent (never a blank card or a noise tile)', !c.flash && !c.gifRain && !c.videoCard, JSON.stringify(c));
  ramp.dispose();
}

/* ---- the player: what the stands will see ------------------------------------------ */
{
  const bus = createBus();
  const ramp = attachRamp({ bus, seed: 'watch-fx-player', media: pictures });
  const heat = [];
  const fired = [];
  bus.on('ramp-heat', (p) => heat.push({ at: clock, heat: p.heat }));
  bus.on('ramp-fired', (p) => fired.push(p));
  game.side = 'w';
  bus.emit('local', { sides: ['w'], mode: 'online' });
  bus.emit('turn', { side: 'w', ply: 0, clocks: { w: 300000, b: 300000 }, total: 300000 });
  check('a player\'s page is not replay mode', !ramp.debug().replay);
  wait(10000);
  check('ramp-heat about once a second during the player\'s own think', heat.length >= 9 && heat.length <= 11, String(heat.length));
  check('rising', heat[heat.length - 1].heat > heat[0].heat);
  wait(35000);
  check('every picture the ramp fires is told', fired.some((f) => f.kind === 'flash' || f.kind === 'gifRain'), JSON.stringify(fired.slice(0, 3)));
  check('only kinds the server accepts', fired.every((f) => ['flash', 'gifRain', 'video'].includes(f.kind)));
  const card = fired.find((f) => f.kind === 'video');
  check('a card says how long it holds', !card || card.holdMs > 0, JSON.stringify(card));
  game.side = 'b';
  bus.emit('turn', { side: 'b', ply: 1, clocks: { w: 280000, b: 300000 }, total: 300000 });
  const n = heat.length;
  wait(5000);
  check('nothing is sampled on the opponent\'s move', heat.length === n);
  ramp.dispose();
}

console.warn = warn;
const total = passed + failures.length;
if (failures.length) {
  console.error(`FAIL  ${failures.length}/${total} watch-fx checks failed:`);
  for (const f of failures) console.error('  - ' + f);
  process.exit(1);
}
console.log(`ok  ${passed}/${total} watch-fx checks passed`);
