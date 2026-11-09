import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { BY_ID } from '../data/items.js';
import { CLAUSES } from '../data/content/clauses.js';
import { LEAD_OF, FOLD } from '../data/folds.js';
import { ICON_CATALOG } from '../ui/icon-catalog.js';
import { REGISTRY } from '../ui/unlocks/index.js';
import { handFor, copyHandFor } from '../ui/hands.js';
import * as E from '../sim/economy.js';

const ev = (o) => ({ type: 'pointerdown', button: 0, buttons: 1, isTrusted: true, pointerType: 'mouse', ...o });

test('Double Wielding is a single mid-game row after Second Theme, with a clause, an icon and a mount', () => {
  const it = BY_ID.dualwield;
  assert.ok(it, 'row exists');
  assert.equal(it.name, 'Double Wielding');
  assert.deepEqual(it.req, ['taste2']);
  assert.ok(it.cost > BY_ID.taste2.cost && it.cost < BY_ID.overlay.cost, 'between Second Theme and Spiral Overlay');
  assert.ok(!LEAD_OF[it.id] && !FOLD[it.id], 'never folded into a bundle');
  assert.equal(CLAUSES.dualwield.length, 1);
  assert.doesNotMatch(it.what + it.line + CLAUSES.dualwield[0], /[!—–]/);
  assert.equal(ICON_CATALOG.dualwield.shape, 'cursors');
  assert.equal(typeof REGISTRY.dualwield.mount, 'function');
  const run = E.createRun();
  assert.equal(E.isOpen(run, it), false, 'closed before Second Theme');
  run.owned.taste2 = 1;
  assert.equal(E.isOpen(run, it), true);
});

test('before Double Wielding only the left button presses; a right click does nothing', () => {
  assert.equal(handFor(ev({}), false), 'left');
  assert.equal(handFor(ev({ isTrusted: false }), false), 'left', 'the left button keeps its old path');
  assert.equal(handFor(ev({ button: 2, buttons: 2 }), false), '');
  assert.equal(handFor(ev({ button: 1, buttons: 4 }), false), '');
  assert.equal(handFor(ev({ type: 'pointermove', button: 2, buttons: 3 }), false), '', 'no chord before it is owned');
});

test('owned: a trusted right press counts, a synthetic one never does', () => {
  assert.equal(handFor(ev({ button: 2, buttons: 2 }), true), 'right');
  assert.equal(handFor(ev({ button: 2, buttons: 2, isTrusted: false }), true), '');
  assert.equal(handFor(ev({ button: 1, buttons: 4 }), true), '', 'the middle button never presses');
  assert.equal(copyHandFor(ev({ button: 2, buttons: 2 }), true), 'right');
  assert.equal(copyHandFor(ev({ button: 2, buttons: 2 }), false), '');
  assert.equal(copyHandFor(ev({ isTrusted: false }), true), '', 'a copy needs a trusted press, either hand');
});

test('owned: the second button of a chord is a second press, a release or a plain move is not', () => {
  const chord = (o) => ev({ type: 'pointermove', ...o });
  assert.equal(handFor(chord({ button: 2, buttons: 3 }), true), 'right', 'right joins a held left');
  assert.equal(handFor(chord({ button: 0, buttons: 3 }), true), 'left', 'left joins a held right');
  assert.equal(handFor(chord({ button: 2, buttons: 1 }), true), '', 'right released');
  assert.equal(handFor(chord({ button: -1, buttons: 3 }), true), '', 'moving while both are held');
  assert.equal(handFor(chord({ button: 2, buttons: 3, isTrusted: false }), true), '');
  assert.equal(handFor(chord({ button: 2, buttons: 3, pointerType: 'touch' }), true), '');
});

test('the button wires the press rule and keeps the context menu off only on itself', () => {
  const app = readFileSync(new URL('../ui/app.js', import.meta.url), 'utf8');
  assert.match(app, /handFor\(ev, hands\.length > 0\)/);
  assert.match(app, /el\.btn\.addEventListener\('contextmenu', \(ev\) => ev\.preventDefault\(\)\)/);
  assert.doesNotMatch(app, /doc\.addEventListener\('contextmenu'/);
});
