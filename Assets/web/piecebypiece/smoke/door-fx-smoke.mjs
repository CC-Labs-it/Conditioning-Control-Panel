/* ============================================================================
 * smoke/door-fx-smoke.mjs - the front door's shared Back button and screen
 * motion, in node, no browser:
 *
 *   node smoke/door-fx-smoke.mjs
 *
 * The Back button: a real <button>, an icon and the word, Esc named in its
 * label; wireBack fires once per press on any [data-back] inside the root,
 * after any re-render, and never for a click elsewhere. The motion: children
 * stagger in within the house timings (entrances 150-350 ms, exits quicker),
 * reduced motion is a plain fade with no travel, the stagger is capped, the
 * stat roll lands on its exact number and leaves "26:17" and "pawn" alone.
 * The door itself: every screen that used to print "esc - back" now wears
 * the button, and no copy says "esc" any more.
 * ==========================================================================*/

import { readFileSync } from 'node:fs';
import { backButton, wireBack } from '../door/back-button.js';
import { FX, inPlan, outPlan, staggerAt, rollValue, rollMs, splitCount, screenIn, screenOut, KICKS } from '../door/screen-fx.js';

let passed = 0;
const failures = [];
const ok = (what, cond) => { if (cond) passed++; else failures.push(what); };
const eq = (what, got, want) => ok(`${what} (got ${JSON.stringify(got)}, wanted ${JSON.stringify(want)})`, JSON.stringify(got) === JSON.stringify(want));

/* ---------------------------------------------------------------- the button */
{
  const h = backButton();
  ok('a real button', /^<button type="button" class="door-back" data-back /.test(h));
  ok('icon and word', /<svg[^>]*aria-hidden="true"/.test(h) && /<span>Back<\/span>/.test(h));
  ok('Esc is named for the screen reader and the tooltip', /aria-label="Back \(Esc\)"/.test(h) && /title="Back \(Esc\)"/.test(h));
  ok('a label of its own', /<span>Leave<\/span>/.test(backButton('Leave')));
  ok('the label is escaped', !/<b>/.test(backButton('<b>x')));
  ok('no label is Back', /<span>Back<\/span>/.test(backButton('')));
}

/* ---------------------------------------------------------------- the wire */
{
  // the smallest root that delegation needs
  const mk = (back, disabled = false) => ({ closest: (sel) => (sel === '[data-back]' && back ? btn : null) });
  const btn = { disabled: false, closest: () => btn };
  let listener = null;
  const root = { addEventListener: (t, f) => { if (t === 'click') listener = f; }, removeEventListener: (t, f) => { if (listener === f) listener = null; }, contains: (n) => n === btn };
  let hits = 0, prevented = 0;
  const off = wireBack(root, () => { hits++; });
  const ev = (target) => ({ target, preventDefault: () => { prevented++; }, stopPropagation: () => {} });
  listener(ev(mk(true)));
  listener(ev(mk(false)));
  eq('a press on Back calls back once, a click elsewhere never', hits, 1);
  eq('and the press is the button\'s, not the page\'s', prevented, 1);
  btn.disabled = true; listener(ev(mk(true))); btn.disabled = false;
  eq('a disabled Back does nothing', hits, 1);
  off();
  eq('unwired, the listener is gone', listener, null);
  eq('no root is a no-op unwire', typeof wireBack(null, () => {}), 'function');
}

/* ---------------------------------------------------------------- the motion */
{
  ok('entrance inside the house range', FX.inMs >= 150 && FX.inMs <= 350);
  ok('exit quicker than the entrance, inside 120-250', FX.outMs < FX.inMs && FX.outMs >= 120 && FX.outMs <= 250);
  eq('the stagger', [0, 1, 2].map(staggerAt), [0, FX.staggerMs, 2 * FX.staggerMs]);
  eq('the stagger is capped', staggerAt(40), FX.staggerCap * FX.staggerMs);
  ok('the last child still lands inside about half a second', staggerAt(99) + FX.inMs <= 600);
  const full = inPlan(3), calm = inPlan(3, true);
  ok('full motion travels', /translateY/.test(full.frames[0].transform || ''));
  ok('reduced motion does not travel', calm.frames.every((f) => !('transform' in f)));
  eq('reduced motion is a plain 120 ms fade, no stagger', [calm.timing.duration, calm.timing.delay], [120, 0]);
  ok('the overshoot is small (THUD, toned down)', /cubic-bezier\(\.2, 1\.25/.test(FX.easeIn));
  ok('an exit goes somewhere (lifts)', /translateY\(-/.test(outPlan().frames[1].transform));
  ok('a calm exit is a fade', outPlan(true).frames.every((f) => !('transform' in f)));
  ok('the kicks stay small (pop under 1.15, knock under 6 px)', KICKS.pop.frames.every((f) => !/scale\(1\.(1[5-9]|[2-9])/.test(f.transform)) && KICKS.knock.ms <= 350);
}

/* ---------------------------------------------------------------- the roll */
{
  eq('lands exactly', rollValue(5000, 74), 74);
  eq('starts at nothing', rollValue(0, 74), 0);
  ok('races first (ease-out)', rollValue(rollMs(100) / 4, 100) > 25);
  ok('never past the number', [0, 50, 100, 200, 400, 800].every((t) => rollValue(t, 20) <= 20));
  ok('short numbers roll short', rollMs(1) < rollMs(500) && rollMs(500) <= 900);
  eq('zero does not roll', rollMs(0), 0);
  eq('"74 moves" keeps its word', splitCount('74 moves'), { pre: '', n: 74, post: ' moves' });
  eq('"26:17" is not a count', splitCount('26:17'), null);
  eq('"pawn" is not a count', splitCount('pawn'), null);
  eq('"-" is not a count', splitCount('-'), null);
}

/* ---------------------------------------------------------------- no DOM, no throw */
{
  let threw = false;
  try { await screenIn(null); await screenOut({}); await screenIn({ animate: undefined }); } catch { threw = true; }
  ok('a missing element or an old engine resolves quietly', !threw);
}

/* ---------------------------------------------------------------- the door wears it */
{
  const door = readFileSync(new URL('../door/door.js', import.meta.url), 'utf8');
  ok('the door imports the shared button', /from '\.\/back-button\.js'/.test(door));
  ok('the door imports the shared motion', /from '\.\/screen-fx\.js'/.test(door));
  ok('no screen prints "esc - back" any more', !/esc - back/i.test(door));
  ok('no screen prints "esc leaves the board" any more', !/esc leaves the board/i.test(door));
  ok('the door calls the ui cues', /from '\.\.\/audio\/ui-cues\.js'/.test(door) && /ui\.open\(\)/.test(door) && /ui\.select\(\)/.test(door) && /ui\.hover\(\)/.test(door));
  for (const s of ['lobbyScreen', 'games', 'profile', 'replayScreen', 'end', 'found', 'menu']) {
    const body = door.split(`function ${s}()`)[1] || '';
    // bar() is the panel top, and it always leads with backButton()
    ok(`${s}: a Back button`, /backButton\(|\bbar\('[^']*', '[A-Z]/.test(body.split('\n  function ')[0]));
  }
  // the ladder screen (ui/ladder.js, sound lane) renders its own Back: the card's one delegated wire catches it
  ok('every Back on the card is wired once, to the step Escape takes', /wireBack\(card, \(\) => goBack\(\)\)/.test(door));
  const back = door.split('function goBack()')[1] || '';
  ok('the ladder\'s Back goes where its Escape goes', /screen === 'ladder'\) \{ show\(ladderFrom\);/.test(back.split('\n  }')[0]));
  ok('the menu\'s Back leaves the board', /screen === 'menu'\) \{ exitToHost\(\);/.test(back));
  ok('bar() leads with the shared button', /function bar\([^)]*\) \{\s+return `<div class="door-top">\$\{back \? backButton\(back\)/.test(door));
  {
  }
}

if (failures.length) {
  console.error(`door-fx-smoke: ${failures.length} FAILED, ${passed} passed`);
  for (const f of failures) console.error('  FAIL ' + f);
  process.exit(1);
}
console.log(`door-fx-smoke: ${passed} passed`);
