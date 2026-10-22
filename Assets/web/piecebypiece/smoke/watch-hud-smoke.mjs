/* ============================================================================
 * smoke/watch-hud-smoke.mjs - the stands' furniture (ui/watch-hud.js), the crowd
 * (ui/reactions.js) and the watcher's own effects choice (game/preferences.js),
 * in node, no browser:
 *
 *   node smoke/watch-hud-smoke.mjs
 *
 * Fake bus events stand in for lane WATCH (watch-info, fx-heat, watch-reactions).
 * Checked: the words ("15 s behind", "N watching", the seat swap); the plates'
 * heat follows fx-heat, the mover's drops on the move; watch-effects goes out
 * on every watch deal and on a change of the watcher's choice, never for a
 * seat; the no-pictures check (host flag first, the choice as a fallback, no
 * host = no nag); the six presets and the float plan; watchEffects / watchAmount
 * persist through pbp:setting and default to on / normal.
 * ==========================================================================*/

import { createBus } from '../game/events.js';
import { delayWord, watchersWord, seatWord, mediaReady, onlineEmpty, noMediaLine, createWatchHud } from '../ui/watch-hud.js';
import { setShown, isShown } from '../ui/fade.js';
import { REACTION_IDS, cleanCounts, floatPlan, stillLine, createReactions } from '../ui/reactions.js';
import { presentation, setPresentation, onPresentation } from '../game/preferences.js';

let passed = 0;
const failures = [];
const ok = (what, cond) => { if (cond) passed++; else failures.push(what); };
const eq = (what, got, want) => ok(`${what} (got ${JSON.stringify(got)}, wanted ${JSON.stringify(want)})`, JSON.stringify(got) === JSON.stringify(want));
const DASH = /[–—]|\s-\s|!/;

// ---- words
eq('the delay', delayWord(15000), '15 s behind');
eq('the delay default', delayWord(undefined), '15 s behind');
eq('watchers', watchersWord(3), '3 watching');
eq('nobody watching says nothing', watchersWord(0), '');
eq('the seat swap from White', seatWord('w'), 'Watch from Black');
eq('the seat swap from Black', seatWord('b'), 'Watch from White');
for (const line of [delayWord(15000), watchersWord(2), seatWord('w'), noMediaLine(true), noMediaLine(false)]) ok(`no dash or bang: ${line}`, !DASH.test(line));

// ---- the no-pictures check
eq('the host flag wins (false)', mediaReady(false, { online: true, flavour: 'pink', library: true }), false);
eq('the host flag wins (true)', mediaReady(true, null), true);
eq('no host, no word', mediaReady(null, null), null);
eq('a flavour picked is a source', mediaReady(null, { online: true, flavour: 'pink', library: false, canOnline: true }), true);
eq('no flavour and no library: none', mediaReady(null, { online: true, flavour: '', library: false, canOnline: true }), false);
eq('online off and no library: none', mediaReady(null, { online: false, flavour: 'pink', library: false }), false);
eq('own library only: a source', mediaReady(null, { online: false, library: true }), true);
eq('niches whose online answer came back empty are no source', mediaReady(null, { online: true, flavour: 'pink', library: false, canOnline: true }, true), false);
eq('an empty answer', [onlineEmpty({ state: 'empty' }), onlineEmpty({ state: 'off' }), onlineEmpty({ state: 'ready', have: 0 }), onlineEmpty({ state: 'ready', have: 12 }), onlineEmpty({ state: 'loading' })], [true, true, true, false, false]);
{ const n = { hidden: true }; setShown(n, true); ok('a fake node shows', isShown(n)); setShown(n, false); ok('...and hides', !isShown(n)); }

// ---- reactions, pure
eq('six presets', REACTION_IDS, ['fire', 'wow', 'laugh', 'clap', 'heart', 'eyes']);
eq('counts: known ids, positive ints', cleanCounts({ fire: 2, wow: 0, laugh: -1, spam: 9, clap: 1.7, heart: '3' }), { fire: 2, clap: 1, heart: 3 });
{
  let s = 1;
  const rand = () => { s = (s * 16807) % 2147483647; return (s - 1) / 2147483646; };
  const plan = floatPlan({ fire: 300, eyes: 1 }, { cap: 14, rand });
  eq('the plan is capped', plan.length, 14);
  ok('every id gets a float', plan.some((f) => f.id === 'eyes') && plan.some((f) => f.id === 'fire'));
  ok('floats are spread over the bucket', plan.every((f) => f.atMs >= 0 && f.atMs < 3000) && new Set(plan.map((f) => f.atMs)).size > 5);
  eq('a small bucket floats one each', floatPlan({ wow: 2, clap: 1 }, { rand }).length, 3);
  eq('an empty bucket floats nothing', floatPlan({}, { rand }), []);
  ok('the still row', stillLine({ eyes: 1, fire: 4 }).startsWith('\u{1F525} 4'));
}

// ---- preferences: the watcher's own effects
{
  const seen = [];
  const off = onPresentation((p) => seen.push(p));
  eq('watch effects on by default', presentation().watchEffects, true);
  eq('watch amount normal by default', presentation().watchAmount, 'normal');
  setPresentation({ watchEffects: false, watchAmount: 'more' });
  eq('watch effects saved', [presentation().watchEffects, presentation().watchAmount], [false, 'more']);
  eq('the player experience is untouched', presentation().experience, 'distraction');
  setPresentation({ watchAmount: 'loud' });
  eq('an unknown amount is ignored', presentation().watchAmount, 'more');
  setPresentation({ watchEffects: true, watchAmount: 'normal' });
  ok('listeners hear the change', seen.length >= 3);
  off();
}

// ---- the watch HUD on the bus (no DOM: every node optional)
{
  const bus = createBus();
  const sent = { fx: [], seat: [], leave: 0 };
  bus.on('watch-effects', (p) => sent.fx.push(p));
  bus.on('watch-seat', (p) => sent.seat.push(p));
  const host = new Set();
  const onHost = (fn) => { host.add(fn); return () => host.delete(fn); };
  const tellHost = (m) => { for (const fn of host) fn(m); };
  let choice = null;
  const pictures = { available: () => !!choice, state: () => choice, manage: async () => true };
  const game = { turn: () => 'w', watchers: () => 0, current: { delayMs: () => 15000, behindMs: () => 17400 } };
  // two fake nodes: the tag's words and the seat swap's click
  const node = () => { const n = { textContent: '', hidden: false, handlers: [], addEventListener(t, h) { n.handlers.push(h); }, removeEventListener() {}, click() { for (const h of n.handlers) h({ preventDefault() {} }); } }; return n; };
  const tag = node(), flip = node();
  const turned = [];
  const board = { setSide: (s) => turned.push(s) };
  const hud = createWatchHud({ bus, game, board, pictures, prefs: { presentation, onPresentation }, onHost, el: { tag, flip } });
  eq('watch-effects goes out at boot', sent.fx.length, 1);

  bus.emit('local', { mode: 'online', sides: ['w'] });
  eq('a seat is not the stands', hud.debug().watching, false);
  bus.emit('watchers', { count: 4 });
  eq('a seat still counts watchers', hud.debug().watching, false);

  bus.emit('local', { mode: 'watch', sides: [], graded: ['w', 'b'] });
  eq('the stands', hud.debug().watching, true);
  eq('a watch deal sends the effects', sent.fx.at(-1), { on: true, amount: 'normal', media: true });
  eq('the tag starts from the session lag', tag.textContent, '17 s behind');
  bus.emit('watch-info', { delayMs: 15000, behindMs: 15400, watchers: 3, players: { w: { display_name: 'A', rating: 1500 }, b: { display_name: 'B', rating: 1400 } }, seat: 'w' });
  eq('the tag says the true lag, not the delay', tag.textContent, '15 s behind');
  bus.emit('watch-info', { delayMs: 15000, behindMs: 16600, seat: 'w' });
  eq('...rounded to whole seconds', tag.textContent, '17 s behind');
  eq('the swap reads from White', flip.textContent, 'Watch from Black');
  flip.click();
  eq('the swap asks the session', sent.seat, [{ seat: 'b' }]);
  eq('...and never turns the board itself', turned, []);
  eq('the label waits for watch-info', flip.textContent, 'Watch from Black');
  bus.emit('watch-info', { behindMs: 15000, seat: 'b' });
  eq('watch-info seat relabels the swap', flip.textContent, 'Watch from White');
  bus.emit('fx-heat', { side: 'w', heat: 0.62 });
  bus.emit('fx-heat', { side: 'b', heat: 3 });
  eq('heat per plate, clamped', hud.debug().heat, { w: 0.62, b: 1 });
  bus.emit('turn', { side: 'b', ply: 1 });
  eq("the mover's heat lets go on the move", hud.debug().heat.w, 0);
  eq('the other plate keeps its own', hud.debug().heat.b, 1);

  setPresentation({ watchAmount: 'less' });
  eq('a change of amount goes out', sent.fx.at(-1), { on: true, amount: 'less', media: true });
  const n = sent.fx.length;
  setPresentation({ volume: 0.5 });
  eq('an unrelated change is not a resend', sent.fx.length, n);

  // the host says there is nothing to show
  tellHost({ type: 'pbp:settings', mediaAvailable: false });
  eq('no pictures: media false', hud.debug().media, false);
  eq('...and the effects say so', sent.fx.at(-1).media, false);
  choice = { online: true, flavour: 'pink', library: false, canOnline: true, chosen: true };
  tellHost({ type: 'pbp:media-state', mediaAvailable: true, flavour: 'pink' });
  eq('a pick clears it', hud.debug().media, true);
  // the host's word wins over the online answer; without a word the empty answer counts
  tellHost({ type: 'pbp:online-media', state: 'empty', have: 0 });
  eq('the host flag still wins over an empty answer', hud.debug().media, true);
  const before = sent.fx.length;
  tellHost({ type: 'pbp:media-state', mediaAvailable: true, flavour: 'pink' });
  eq('every pbp:media-state resends the effects', sent.fx.length, before + 1);
  setPresentation({ watchEffects: false });
  eq('effects off goes out', sent.fx.at(-1).on, false);
  setPresentation({ watchEffects: true, watchAmount: 'normal' });

  bus.emit('gameover', { result: 'checkmate', winner: 'b' });
  eq('the game over drops both bars', hud.debug().heat, { w: 0, b: 0 });
  hud.dispose();
  ok('dispose lets go of the host', host.size === 0);
}

// ---- the crowd on the bus, no DOM
{
  const bus = createBus();
  const crowd = createReactions({ bus });
  crowd.setWatching(true);
  let threw = false;
  try { bus.emit('watch-reactions', { counts: { fire: 3 } }); } catch { threw = true; }
  ok('a crowd with no nodes stays quiet', !threw && crowd.debug().watching);
  crowd.dispose();
}

if (failures.length) {
  console.error(`watch-hud-smoke: ${failures.length} failed, ${passed} passed`);
  for (const f of failures) console.error('  FAIL ' + f);
  process.exit(1);
}
console.log(`watch-hud-smoke: ${passed} passed`);
