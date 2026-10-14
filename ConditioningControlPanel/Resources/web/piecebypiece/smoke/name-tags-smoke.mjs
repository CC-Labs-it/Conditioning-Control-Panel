/* ============================================================================
 * smoke/name-tags-smoke.mjs - the floating name tags' rules (board/name-tags-plan.js).
 *
 *   A  labels: names capped, ratings only when real, every deal shape named
 *   B  the bus folded: who hops, who flinches, checks in a row, the end
 *   C  the check effects grow with the run and are gone at zero
 *   D  springs settle (every kick has its way out); the anchors sit off the board
 *   E  wiring: Options has the box, the preference defaults on
 *
 *   node smoke/name-tags-smoke.mjs        (no server, no browser)
 * ==========================================================================*/

import { readFileSync } from 'node:fs';
import { TAG, capName, labelFor, namesFor, anchorFor, tagState, reduce, inCheck, endBeat, spring, trembleAmp, tremble, sweatRate, bob, checkTint } from '../board/name-tags-plan.js';

let failed = 0;
let passed = 0;
function expect(cond, name) {
  if (cond) { passed++; console.log('  ok   ' + name); }
  else { failed++; console.error('  FAIL ' + name); }
}
const here = (p) => new URL('../' + p, import.meta.url);

/* ---- A: labels ------------------------------------------------------------------ */
{
  expect(capName('velvet') === 'velvet', 'a short name stays whole');
  const long = capName('an extremely long display name');
  expect(Array.from(long).length === TAG.nameMax && long.endsWith('\u2026'), 'a long name is cut with an ellipsis');
  expect(capName('  two   spaces ') === 'two spaces', 'spaces are tidied');
  expect(labelFor({ display_name: 'velvet', rating: 1240.4 }).rating === 1240, 'a server player carries a whole rating');
  expect(labelFor({ name: 'moth', rating: null }).rating === null, 'an unrated player shows no number');
  expect(labelFor({ name: 'moth', rating: 'x' }).rating === null, 'a junk rating shows no number');
  expect(labelFor(null, 'White').name === 'White', 'an unnamed seat takes its colour');
  expect(labelFor('rose').name === 'rose', 'a bare string is a name');

  const hot = namesFor({ mode: 'hotseat', sides: ['w', 'b'] });
  expect(hot.w.name === 'White' && hot.b.name === 'Black', 'hotseat: the colours');
  const solo = namesFor({ mode: 'solo', sides: ['b'], players: { b: 'Me', w: 'Clockwork' } });
  expect(solo.b.name === 'Me' && solo.w.name === 'Clockwork', 'solo: the deal names both');
  const online = namesFor({ mode: 'online', match: { side: 'b', opponent: { name: 'rose', rating: 1198 } } }, { own: 'velvet' });
  expect(online.b.name === 'velvet' && online.w.name === 'rose' && online.w.rating === 1198, 'online: my seat is me, the other is the opponent');
  const later = namesFor({ mode: 'online', match: { side: 'b' } }, { players: { white: { display_name: 'rose', rating: 1198 }, black: { display_name: 'velvet', rating: 1240 } } });
  expect(later.w.name === 'rose' && later.b.rating === 1240, 'online: the server\'s players win');
  const stands = namesFor({ mode: 'watch', sides: [], match: { id: 'm_1' } }, { players: { w: { name: 'a', rating: 1500 }, b: { name: 'b', rating: null } } });
  expect(stands.w.name === 'a' && stands.w.rating === 1500 && stands.b.rating === null, 'the stands: names and ratings from the watch session');
  const bare = namesFor({ mode: 'watch', sides: [], match: { id: 'm_1' } });
  expect(bare.w.name === 'White' && bare.b.name === 'Black', 'the stands before the head lands: the colours');
}

/* ---- B: the bus ----------------------------------------------------------------- */
const run = (events) => {
  let st = tagState();
  const all = [];
  for (const [type, p] of events) { const out = reduce(st, type, p); st = out.state; all.push(...out.beats); }
  return { st, beats: all };
};
const has = (beats, side, kind) => beats.some((b) => b.side === side && b.kind === kind);
{
  const deal = [['local', { mode: 'hotseat' }], ['newgame', { ply: 0 }], ['turn', { side: 'w', ply: 0 }]];
  const a = run(deal);
  expect(a.st.dealt && a.st.toMove === 'w', 'a deal: tags up, white to move');
  expect(!a.beats.some((b) => b.kind === 'hop'), 'the deal itself moves nobody');
  const b = run([...deal, ['turn', { side: 'b', ply: 1 }]]);
  expect(has(b.beats, 'w', 'hop') && b.st.toMove === 'b', 'white moved: white hops, black to move');
  const c = run([...deal, ['turn', { side: 'b', ply: 1 }], ['capture', { by: 'b', victimSide: 'w' }], ['turn', { side: 'w', ply: 2 }]]);
  expect(has(c.beats, 'w', 'flinch') && has(c.beats, 'b', 'proud'), 'a capture: the loser flinches, the taker swells');
  const r = run([...deal, ['turn', { side: 'b', ply: 5 }]]);
  expect(!has(r.beats, 'w', 'hop'), 'a resync jump moves nobody');
  const rc = run([...deal, ['turn', { side: 'b', ply: 7, inCheck: true }]]);
  expect(inCheck(rc.st, 'b') && rc.st.run.b === 1, 'a resync into check: the referee\'s word holds');

  // three checks in a row against black, a move out between each
  const checks = [...deal];
  let ply = 0;
  for (let i = 0; i < 3; i++) {
    checks.push(['check', { side: 'b' }], ['turn', { side: 'b', ply: ++ply }]);
    checks.push(['turn', { side: 'w', ply: ++ply }]);
  }
  const k = run(checks);
  expect(k.st.run.b === 3, 'three checks in a row count three');
  expect(!inCheck(k.st, 'b'), 'out of check while it is not black\'s move');
  expect(has(k.beats, 'w', 'gotcha'), 'giving check leans in');
  const k2 = run([...checks, ['check', { side: 'b' }], ['turn', { side: 'b', ply: ++ply }]]);
  expect(inCheck(k2.st, 'b') && k2.st.run.b === 4, 'the fourth check: in check, run of four');
  const quiet = run([...checks, ['turn', { side: 'b', ply: ++ply }]]);
  expect(quiet.st.run.b === 0 && !inCheck(quiet.st, 'b'), 'a quiet move breaks the run');

  const end = run([...deal, ['check', { side: 'b' }], ['gameover', { result: 'checkmate', winner: 'w' }]]);
  expect(has(end.beats, 'w', 'win') && has(end.beats, 'b', 'lose'), 'mate: one wins, one loses');
  expect(!inCheck(end.st, 'b'), 'no sweating once it is over');
  const draw = run([...deal, ['gameover', { result: 'stalemate', winner: null }]]);
  expect(has(draw.beats, 'w', 'draw') && has(draw.beats, 'b', 'draw'), 'a draw: both shrug');
  expect(endBeat('timeout', 'b', 'b') === 'win' && endBeat('timeout', 'b', 'w') === 'lose', 'the clock ends it the same way');
  const again = run([...deal, ['gameover', { result: 'checkmate', winner: 'w' }], ['local', { mode: 'hotseat' }]]);
  expect(!again.st.over && again.beats.filter((b) => b.kind === 'reset').length >= 2, 'a new deal sends everything home');
}

/* ---- C: the check effects ------------------------------------------------------- */
{
  expect(trembleAmp(0) === 0 && sweatRate(0) === 0 && checkTint(0) === 0, 'calm at zero: no shake, no sweat, no tint');
  expect(trembleAmp(2) > trembleAmp(1) && trembleAmp(3) > trembleAmp(2), 'the shake grows with every check in a row');
  expect(sweatRate(2) > sweatRate(1) && sweatRate(3) > sweatRate(2), 'so does the sweat');
  expect(trembleAmp(50) === TAG.trembleMax && sweatRate(50) === TAG.sweatMax, 'both have a ceiling');
  expect(checkTint(1) > 0 && checkTint(9) <= 1, 'the calm cue tints, never past full');
  const t0 = tremble(0.37, 0);
  expect(t0.x === 0 && t0.y === 0 && t0.rz === 0, 'no tremble without a check');
  let peak = 0;
  for (let t = 0; t < 2; t += 0.004) peak = Math.max(peak, Math.abs(tremble(t, 3).x));
  expect(peak > 0 && peak <= trembleAmp(3) * 1.01, 'the tremble stays inside its amplitude');
  expect(Math.abs(bob(0.5, 'w') - bob(0.5, 'b')) > 1e-3, 'the two tags bob out of step');
}

/* ---- D: springs and anchors ----------------------------------------------------- */
{
  let x = 0, v = TAG.winHop;
  let top = 0;
  for (let i = 0; i < 300; i++) { [x, v] = spring(x, v, 0, TAG.springK * 0.55, TAG.springC * 0.5, 1 / 60); top = Math.max(top, x); }
  expect(top > 0.5 && Math.abs(x) < 0.01 && Math.abs(v) < 0.05, 'the win jump goes up and comes home inside five seconds');
  let y = 0, w = 0;
  for (let i = 0; i < 30; i++) [y, w] = spring(y, w, 1, TAG.slowK, TAG.slowC, 0.1);
  expect(Math.abs(y - 1) < 0.02, 'a slow spring reaches its target on a coarse clock');
  for (const aspect of [16 / 9, 0.46]) {
    const a = anchorFor('w', aspect), b = anchorFor('b', aspect);
    expect(a.x === -b.x && a.z === -b.z && a.y === b.y, 'the anchors are a half turn apart at aspect ' + aspect.toFixed(2));
    expect(Math.max(Math.abs(a.x), Math.abs(a.z)) > 4.5 && a.y - TAG.height / 2 > 0.2, 'the tags float off the squares, over the plinth at aspect ' + aspect.toFixed(2));
    expect(a.z > 0 && b.z < 0, 'white at the white end, black at the black end');
  }
}

/* ---- E: wiring ------------------------------------------------------------------ */
{
  const html = readFileSync(here('index.html'), 'utf8');
  const box = html.match(/<label[^>]*><input type="checkbox" id="game-name-tags" checked> Name tags<\/label>/);
  expect(!!box, 'Options has a Name tags box, ticked');
  expect(box && !/data-seat-only/.test(box[0]), 'the box is there for the stands too');
  const prefs = readFileSync(here('game/preferences.js'), 'utf8');
  expect(/nameTags: saved\.nameTags !== false/.test(prefs), 'Name tags are on unless the player turned them off');
  expect(/typeof patch\.nameTags === 'boolean'/.test(prefs), 'the box saves');
  for (const [name, text] of [['name-tags-plan.js', readFileSync(here('board/name-tags-plan.js'), 'utf8')]]) {
    expect(!/[\u2013\u2014]/.test(text) && !text.startsWith('\uFEFF'), name + ': no long dashes, no BOM');
  }
}

console.log(`\nname tags: ${passed} passed, ${failed} failed`);
process.exit(failed ? 1 : 0);
