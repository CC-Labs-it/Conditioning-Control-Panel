// A touch's click after the pointerup that opened the card lands on the scrim under the finger. It must not shut the card.
import test from 'node:test';
import assert from 'node:assert/strict';
import { createPressGuard } from '../state/press-guard.js';
import { createHud } from '../hud/index.js';
import { makeT } from '../state/strings.js';
import { fakeDocument } from './fake-dom.mjs';

function rig() {
  const doc = fakeDocument(), root = doc.createElement('main'), sent = [];
  const guard = createPressGuard({ now: () => 0, focused: true });
  const hud = createHud(root, { t: makeT({}), send: (i) => sent.push(i), goTab() {}, guard, motion: 'off' });
  const view = { status: 'ready', sp: 500, week: '2026-W44', tier: 1, tabs: ['kart'], tabId: 'kart', shelves: [], focus: null, card: null };
  hud.paint(view);
  const scrim = root.querySelectorAll('.pc-scrim').find((s) => !hud.guide.el.querySelectorAll('.pc-scrim').includes(s));
  const card = { face: 'buy', name: 'x', desc: '', priceSp: 10, after: 1, row: { id: 'kart.body.petal' } };
  return { hud, view, scrim, sent, open: () => hud.paint({ ...view, card }) };
}

test('the click that follows the opening tap leaves the card open', () => {
  const r = rig();
  r.open();
  r.scrim.onclick({ detail: 1 });
  assert.deepEqual(r.sent, []);
});

test('a real press on the scrim closes it, once', () => {
  const r = rig();
  r.open();
  r.scrim.onpointerdown({});
  r.scrim.onclick({ detail: 1 });
  r.scrim.onclick({ detail: 1 });
  assert.deepEqual(r.sent, [{ type: 'back' }]);
});

test('a press left over from before the card opened does not count', () => {
  const r = rig();
  r.scrim.onpointerdown({});
  r.open();
  r.scrim.onclick({ detail: 1 });
  assert.deepEqual(r.sent, []);
});
