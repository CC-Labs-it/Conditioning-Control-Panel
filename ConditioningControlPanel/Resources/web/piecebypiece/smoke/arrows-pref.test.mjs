// node --test smoke/arrows-pref.test.mjs - the "Right-click arrows" switch (Options > Camera).
// On by default; off hands every right press back to the camera and wipes what was drawn.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createAnnotator } from '../board/annotations.js';

const store = new Map();
globalThis.localStorage = { getItem: (k) => store.get(k) ?? null, setItem: (k, v) => store.set(k, String(v)) };
globalThis.window = { chrome: { webview: { addEventListener() {}, postMessage() {} } } };
const { presentation, setPresentation } = await import('../game/preferences.js');

const read = (rel) => readFileSync(new URL(rel, import.meta.url), 'utf8');

test('arrows are on by default, the choice saves, and nothing else moves with it', () => {
  assert.equal(presentation().arrows, true);
  const before = presentation();
  setPresentation({ arrows: false });
  assert.equal(presentation().arrows, false);
  assert.equal(JSON.parse(store.get('pbp-presentation-v1')).arrows, false);
  assert.deepEqual({ ...presentation(), arrows: true }, before);
  setPresentation({ arrows: 'yes' });
  assert.equal(presentation().arrows, false, 'only a boolean is taken');
  setPresentation({ arrows: true });
  assert.equal(presentation().arrows, true);
});

test('with the switch off a right press on a square goes to the camera and draws nothing', () => {
  const target = new EventTarget();
  const camera = [];
  const hand = createAnnotator({
    target, win: new EventTarget(), squareAt: (ev) => ev.square ?? null,
    blocked: () => !presentation().arrows,
  });
  target.addEventListener('pointerdown', (ev) => camera.push(ev.button));
  const fire = (type, props) => {
    const ev = new Event(type, { cancelable: true });
    Object.assign(ev, { pointerId: 1, pointerType: 'mouse', button: 2, shiftKey: false, ...props });
    target.dispatchEvent(ev);
  };
  const drag = () => { fire('pointerdown', { square: 'e2' }); fire('pointermove', { button: -1, buttons: 2, square: 'e4' }); fire('pointerup', { square: 'e4' }); };

  setPresentation({ arrows: false });
  drag();
  assert.equal(hand.size(), 0);
  assert.deepEqual(camera, [2], 'the camera heard the press');

  setPresentation({ arrows: true });
  drag();
  assert.equal(hand.size(), 1);
  assert.deepEqual(camera, [2], 'and not this one');
});

test('the page wires it: a box in Options, the hud saves it, boot.js blocks and wipes on it', () => {
  assert.match(read('../index.html'), /<input type="checkbox" id="game-arrows" checked> Right-click arrows</);
  const hud = read('../hud.js');
  assert.ok(hud.includes("setPresentation({ arrows: arrows.checked })"));
  assert.ok(hud.includes('if (arrows) arrows.checked = p.arrows;'));
  const boot = read('../boot.js');
  assert.ok(boot.includes('isBusy: () => !presentation().arrows ||'));
  assert.ok(boot.includes('if (!p.arrows && (board.annotate.size() || board.annotate.isDrawing())) board.annotate.clear();'));
});
