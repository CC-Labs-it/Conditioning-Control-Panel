/* ============================================================================
 * smoke/open-intro-smoke.mjs - the card the page opens on (ui/open-intro-plan.js)
 * and the Gentle speed both intros share (board/intro-plan.js gentleSpeed).
 *
 *   A  timeline: about 3 s, never over 4 s; gears up first, outer letters first,
 *      the name lands, the line, the stamp, then the card and the dark go together
 *   B  a skip takes everything out fast and freezes the picture where it was
 *   C  levels: off plays nothing; reduced is a 0.6 s card fade with nothing turning
 *   D  gentle: the full version, slower; the player's own box or the OS still win
 *   E  a spectate open is short: the name lands, then the card goes
 *   F  wiring: boot, Options, Escape kept, board intro's clock scaled
 *
 *   node smoke/open-intro-smoke.mjs        (no server, no browser)
 * ==========================================================================*/

import { readFileSync } from 'node:fs';
import {
  OPEN, openLevel, openPlan, hurried, letterPose, gearTrain, lineState, stampState, cardState, backdrop,
  clicksBetween, cuesBetween, openOver, skipFade,
} from '../ui/open-intro-plan.js';
import { introLevel, introPlan, gentleSpeed } from '../board/intro-plan.js';

let failed = 0;
let passed = 0;
function expect(cond, name) {
  if (cond) { passed++; console.log('  ok   ' + name); }
  else { failed++; console.error('  FAIL ' + name); }
}
const near = (a, b, e = 1e-6) => Math.abs(a - b) <= e;

// "Piece by Piece" without its spaces, each letter's middle across the word
const TITLE = 'Piece by Piece';
const letters = [...TITLE].map((ch, i) => ({ ch, at: (i + 0.5) / TITLE.length })).filter(l => l.ch !== ' ');
const full = openPlan(letters, 'full');

/* ---- A: timeline ----------------------------------------------------------------- */
{
  expect(full.total <= OPEN.max, `the card is never longer than ${OPEN.max} s (${full.total.toFixed(2)})`);
  expect(full.total >= 2.6 && full.total <= 3.2, `about 3 s (${full.total.toFixed(2)})`);
  expect(full.letters.length === 12, 'every letter has a place, spaces never move');
  const first = full.letters[0], mid = full.letters[5], last = full.letters[11];
  const starts = full.letters.map(l => l.start);
  expect(near(first.start, last.start) && near(first.start, Math.min(...starts)) && near(mid.start, Math.max(...starts)), 'the outer letters start first, together; the middle last');
  expect(gearTrain(full, first.start).k > 0.2, 'the gears are on the way up before the first letter moves');
  expect(full.letters.every(l => letterPose(l, OPEN.land + 0.001).drop === 0), 'every letter is home when the name lands');
  expect(full.letters.every(l => letterPose(l, l.start - 0.01).hidden), 'nothing shows before its start');
  const p = letterPose(mid, mid.start + OPEN.rise + 0.03);
  expect(p.sx > 1 && p.sy < 1, 'a letter squashes as it lands');
  expect(gearTrain(full, OPEN.outAt).k === 0, 'the gears have sunk before the card goes');
  expect(lineState(full, OPEN.lineAt - 0.01).k === 0 && lineState(full, OPEN.lineAt + OPEN.lineIn).k === 1, 'the line wipes in after the name');
  expect(stampState(full, OPEN.stampAt).a === 0 && stampState(full, OPEN.stampAt + OPEN.stampIn).scale === 1, 'the stamp slams in and lands at its size');
  expect(stampState(full, OPEN.stampAt + 0.01).scale > 1.3, 'the stamp comes in big');
  expect(cardState(full, OPEN.outAt).a === 1 && cardState(full, full.total).a === 0, 'the card is up until its OUT, gone at the end');
  expect(cardState(full, OPEN.outAt + OPEN.out * 0.5).lift > 0, 'the OUT lifts the card away');
  expect(backdrop(full, 0) === 1 && backdrop(full, OPEN.outAt) === 1, 'the dark is there from the first frame until the OUT');
  expect(backdrop(full, full.total) === 0, 'the dark is gone at the end: the page is simply there');
  // the dark and the card go out together, so the page never shows through an empty card or flashes blank
  let together = true;
  for (let t = OPEN.outAt; t <= full.total; t += 0.02) if (!near(backdrop(full, t), cardState(full, t).a, 1e-9)) together = false;
  expect(together, 'the dark and the card fade out as one');
  expect(clicksBetween(full, 0, full.total) === OPEN.clicks, 'one quiet tick per ratchet click, never one per letter');
  expect(JSON.stringify(cuesBetween(full, 0, full.total)) === '["land","stamp"]', 'two cues: the name landing, the stamp');
  expect(!openOver(full, full.total - 0.01) && openOver(full, full.total), 'over at its total and not before');
}

/* ---- B: skip ---------------------------------------------------------------------- */
{
  const at = 0.9;
  expect(openOver(full, at + OPEN.skipOut, at) && !openOver(full, at + OPEN.skipOut - 0.01, at), 'a skip is gone in about 0.2 s');
  expect(OPEN.skipOut <= 0.25, 'the skip OUT is fast');
  expect(cardState(full, at + OPEN.skipOut, at).a === 0 && backdrop(full, at + OPEN.skipOut, at) === 0, 'card and dark both go');
  expect(near(cardState(full, at + 0.1, at).a, 0.5) && near(skipFade(at + 0.1, at), 0.5), 'halfway out halfway through');
  const l = full.letters[0];
  expect(letterPose(l, at + 0.15, 'full', at).drop === letterPose(l, at).drop, 'the letters hold where they were while it fades');
  expect(gearTrain(full, at + OPEN.skipOut, at).k === 0, 'the gears go with it');
  const r = openPlan(letters, 'reduced');
  expect(openOver(r, 0.1 + OPEN.skipOut, 0.1), 'the calm card skips too');
}

/* ---- C: levels -------------------------------------------------------------------- */
{
  expect(openLevel({ enabled: false }) === 'off' && openLevel({ enabled: false, gentle: 0.5 }) === 'off', 'Options off plays nothing, gentle or not');
  expect(openLevel({ reduced: true }) === 'reduced' && openLevel({}) === 'full', 'reduced motion is the calm card, else the clockwork');
  const off = openPlan(letters, 'off');
  expect(off.total === 0 && openOver(off, 0), 'off is over before it starts');
  const r = openPlan(letters, 'reduced');
  expect(near(r.total, 0.6), `reduced is a 0.6 s fade (${r.total.toFixed(2)})`);
  expect(gearTrain(r, 0.3).k === 0, 'reduced: no gears');
  expect(r.letters.every(l => letterPose(l, 0.1, 'reduced').drop === 0 && letterPose(l, 0.1, 'reduced').sx === 1), 'reduced: nothing rises, nothing squashes');
  expect(clicksBetween(r, 0, 1) === 0 && cuesBetween(r, 0, 1).length === 0, 'reduced: no ticks, no cues');
  expect(cardState(r, 0).a === 0 && cardState(r, 0.3).a === 1 && cardState(r, r.total).a === 0, 'reduced: the card fades in and out');
  expect(stampState(r, 0.3).scale === 1 && cardState(r, 0.3).lift === 0, 'reduced: no slam, no lift');
}

/* ---- D: gentle -------------------------------------------------------------------- */
{
  expect(gentleSpeed({ host: 0.5 }) === 0.5, 'the host speed is used as sent');
  expect(gentleSpeed({ host: 0 }) === 0 && gentleSpeed({}) === 0 && gentleSpeed({ host: 'x' }) === 0, 'no gentle unless the host says');
  expect(gentleSpeed({ host: 0.5, ownReduced: true }) === 0, "the page's own reduce-motion box wins");
  expect(gentleSpeed({ host: 0.5, osReduced: true }) === 0, 'the OS asking for reduced motion wins');
  expect(gentleSpeed({ host: 0.2 }) === 0.5 && gentleSpeed({ host: 3 }) === 1, 'clamped to 0.5..1');
  expect(openLevel({ reduced: true, gentle: 0.5 }) === 'full', 'gentle plays the full card');
  expect(introLevel({ reduced: true, gentle: 0.5 }) === 'full' && introLevel({ reduced: true }) === 'reduced', 'gentle plays the full board clockwork too');
  expect(introLevel({ enabled: false, gentle: 0.5 }) === 'off', 'the Board intro box still turns it off');
  // the clock runs at the speed: wall time = intro time / speed, the 4 s cap applies before scaling
  const slow = full.total / gentleSpeed({ host: 0.5 });
  expect(near(slow, full.total * 2) && full.total <= OPEN.max, `gentle 0.5 is twice as long (${slow.toFixed(2)} s on the wall)`);
  const men = [];
  for (const f of 'abcdefgh') men.push({ sq: f + '1', type: 'r' }, { sq: f + '2', type: 'p' });
  expect(introPlan(men, introLevel({ reduced: true, gentle: 0.5 })).level === 'full', 'the board plan under gentle is the full clockwork');
}

/* ---- E: a spectate open ------------------------------------------------------------ */
{
  const h = hurried(full, 0.3);
  expect(h.total < full.total && h.outAt >= OPEN.land, `short: the name lands, then it goes (${h.total.toFixed(2)} s)`);
  expect(!cuesBetween(h, 0, h.total).includes('stamp') && stampState(h, h.total).a === 0, 'no stamp on the short card');
  expect(hurried(full, 2.2).outAt === 2.2, 'a card already past the name goes out from where it is');
  const r = openPlan(letters, 'reduced');
  expect(hurried(r, 0.1) === r, 'the calm card is already short');
}

/* ---- F: wiring --------------------------------------------------------------------- */
{
  const src = (p) => readFileSync(new URL(p, import.meta.url), 'utf8');
  const boot = src('../boot.js'), intro = src('../ui/open-intro.js'), prefs = src('../game/preferences.js');
  const html = src('../index.html'), hud = src('../hud.js'), board = src('../board/intro.js');
  expect(/import\('\.\/ui\/open-intro\.js'\)/.test(boot), 'boot loads the card late and guarded');
  expect(boot.indexOf('createHostEscape()') < boot.indexOf('startOpenIntro()'), "the host's Escape listener is made before the card's");
  expect(/q\.has\('hotseat'\)/.test(boot), 'harness pages that deal at once never get the card');
  expect(/addEventListener\('keydown', onKey, true\)/.test(intro) && /stopImmediatePropagation\(\)/.test(intro), 'the card keeps every key while it is up, Escape included');
  expect(/if \(e\.key === 'Escape' \|\|[^\n]*\) e\.preventDefault\(\)/.test(intro), 'an Escape on the card never leaves the page, an Enter never presses the door under it');
  expect(!/postToHost/.test(intro), 'the card never talks to the host');
  expect(/spectateMatchId\) hurry\(\)/.test(intro), 'a spectate open shortens the card');
  expect(/play\('tick'\)/.test(intro) && /'promote'/.test(intro), 'the sting is from the page sound kit');
  expect(/id="game-open-intro" checked> Opening intro</.test(html) && html.indexOf('game-open-intro') < html.indexOf('game-board-intro'), 'Options: "Opening intro" next to "Board intro", on by default');
  expect(/setPresentation\(\{ openIntro: openIntro\.checked \}\)/.test(hud), 'the box saves the choice');
  expect(/openIntro: saved\.openIntro !== false/.test(prefs) && /introSpeed: gentleSpeed\(/.test(prefs) && /ownReduced: !!saved\.reducedMotion/.test(prefs), 'preferences keep the open intro, the own box and the gentle speed apart');
  expect(/gentle: speedNow\(\)/.test(board) && /run\.speed/.test(board), "the board intro's level and clock follow gentle");
  expect(/url\(\$\{new URL\('\.\.\/assets\/fonts\/fredoka-latin\.woff2'/.test(intro), 'the name uses the font carried in the page');
}

/* ---- G: preferences.js, run the way the page loads it ----------------------------- */
{
  const store = new Map();
  globalThis.localStorage = { getItem: (k) => store.get(k) ?? null, setItem: (k, v) => store.set(k, String(v)) };
  let hostSays = null;
  let osReduce = false;
  globalThis.window = {
    chrome: { webview: { addEventListener: (t, fn) => { if (t === 'message') hostSays = fn; }, postMessage() {} } },
    matchMedia: () => ({ get matches() { return osReduce; }, addEventListener() {} }),
  };
  const { presentation, setPresentation } = await import('../game/preferences.js');
  window.PBP = { settings: {} };
  expect(presentation().openIntro === true && presentation().introSpeed === 0, 'on by default, no gentle before the host speaks');
  hostSays({ data: { type: 'pbp:settings', reducedMotion: true, gentleIntroSpeed: 0.5 } });
  const p = presentation();
  expect(p.reducedMotion === true && p.introSpeed === 0.5 && p.ownReduced === false, 'Gentle: reduced for the rest, the intros at half speed');
  setPresentation({ reducedMotion: true });
  expect(presentation().introSpeed === 0 && presentation().ownReduced === true, "the player's own box turns gentle off");
  setPresentation({ reducedMotion: false });
  osReduce = true;
  expect(presentation().introSpeed === 0 && presentation().osReduced === true, 'the OS asking turns gentle off');
  osReduce = false;
  hostSays({ data: { type: 'pbp:settings', reducedMotion: true, gentleIntroSpeed: 0 } });
  expect(presentation().introSpeed === 0 && presentation().reducedMotion === true, 'another reason on the host: the calm versions');
  setPresentation({ openIntro: false });
  expect(presentation().openIntro === false && JSON.parse(store.get('pbp-presentation-v1')).openIntro === false, 'the Options box is saved');
}

console.log(`\n${passed} passed, ${failed} failed`);
process.exit(failed ? 1 : 0);
