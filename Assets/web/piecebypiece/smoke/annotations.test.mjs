// node --test smoke/annotations.test.mjs - the player's arrows and circles: right drag draws an arrow,
// right click a circle, the same again takes it off, a left click wipes them all, and the camera keeps
// every press that is not one of these (off the board, middle button, shift for pan).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createAnnotations, createAnnotator } from '../board/annotations.js';

/** A canvas stand-in: the square under a pointer is just carried on the event. */
function rig({ blocked = false } = {}) {
  const target = new EventTarget();
  const win = new EventTarget();   // the page's window, for blur
  const draws = [];
  const camera = [];
  const hand = createAnnotator({
    target,
    win,
    squareAt: (ev) => ev.square ?? null,
    onChange: (shapes, preview) => draws.push({ shapes, preview }),
    blocked: () => blocked,
  });
  // the camera listens after us, the way scene.js does
  target.addEventListener('pointerdown', (ev) => camera.push(ev.button));
  const fire = (type, props) => {
    const ev = new Event(type, { cancelable: true });
    Object.assign(ev, { pointerId: 1, pointerType: 'mouse', button: 0, shiftKey: false, ...props });
    target.dispatchEvent(ev);
    return ev;
  };
  const right = (from, to = from, extra = {}) => {
    fire('pointerdown', { button: 2, square: from, ...extra });
    if (to !== from) fire('pointermove', { button: -1, square: to });
    fire('pointerup', { button: 2, square: to });
  };
  return { hand, draws, camera, fire, right, win };
}

const keys = (list) => list.map(s => `${s.kind}:${s.from}${s.to === s.from ? '' : '-' + s.to}`).sort();

test('the shapes: a circle, an arrow, the same again takes it off, a bad square is nothing', () => {
  const s = createAnnotations();
  assert.equal(s.toggle('e4'), 'added');
  assert.equal(s.toggle('e2', 'e4'), 'added');
  assert.equal(s.toggle('e4', 'e2'), 'added', 'the other way round is its own arrow');
  assert.deepEqual(keys(s.list()), ['arrow:e2-e4', 'arrow:e4-e2', 'circle:e4']);
  assert.equal(s.toggle('e2', 'e4'), 'removed');
  assert.equal(s.toggle('e4', 'e4'), 'removed');
  assert.equal(s.toggle('z9'), null);
  assert.equal(s.toggle('e2', null), null);
  assert.deepEqual(keys(s.list()), ['arrow:e4-e2']);
  assert.equal(s.clear(), true);
  assert.equal(s.clear(), false, 'nothing left to wipe');
});

test('right drag square to square draws an arrow, through anything, and shows it while drawing', () => {
  const { hand, draws, right } = rig();
  right('a1', 'h8');   // through every man on the long diagonal, legal or not
  assert.deepEqual(keys(hand.list()), ['arrow:a1-h8']);
  assert.ok(draws.some(d => d.preview && d.preview.kind === 'arrow' && d.preview.to === 'h8'), 'a preview while the button is down');
  assert.equal(draws.at(-1).preview, null, 'no preview once it is let go');
  right('g1', 'f3');
  assert.deepEqual(keys(hand.list()), ['arrow:a1-h8', 'arrow:g1-f3']);
});

test('right click without a drag draws a circle; the same right click again takes it off', () => {
  const { hand, right } = rig();
  right('d4');
  assert.deepEqual(keys(hand.list()), ['circle:d4']);
  right('e2', 'e4');
  right('d4');
  assert.deepEqual(keys(hand.list()), ['arrow:e2-e4'], 'circle off, arrow kept');
  right('e2', 'e4');
  assert.equal(hand.size(), 0, 'the same arrow again takes it off');
});

test('a left click wipes them all and still reaches the board underneath', () => {
  const { hand, camera, fire, right } = rig();
  right('e2', 'e4');
  right('c5');
  camera.length = 0;
  const ev = fire('pointerdown', { button: 0, square: 'e2' });
  assert.equal(hand.size(), 0);
  assert.equal(ev.defaultPrevented, false, 'the click is left for drag.js');
  assert.deepEqual(camera, [0], 'later listeners still hear the left press');
});

test('the camera keeps its right drag off the board, its middle drag and shift + right pan', () => {
  const { hand, camera, fire, right } = rig();
  right('e2', 'e4');
  assert.deepEqual(camera, [], 'a right press on a square is ours alone');
  camera.length = 0;
  fire('pointerdown', { button: 2, square: null });   // off the board: orbit as before
  fire('pointerup', { button: 2, square: 'e4' });
  fire('pointerdown', { button: 1, square: 'e4' });   // middle: orbit anywhere
  fire('pointerdown', { button: 2, square: 'e4', shiftKey: true });   // shift: pan
  assert.deepEqual(camera, [2, 1, 2]);
  assert.deepEqual(keys(hand.list()), ['arrow:e2-e4'], 'none of those drew anything');
});

test('a busy board, a touch and a cancelled press draw nothing', () => {
  const busy = rig({ blocked: true });
  busy.right('e2', 'e4');
  assert.equal(busy.hand.size(), 0);
  assert.deepEqual(busy.camera, [2], 'the press goes on to the camera');

  const { hand, fire } = rig();
  fire('pointerdown', { button: 2, pointerType: 'touch', square: 'e2' });
  fire('pointerup', { button: 2, pointerType: 'touch', square: 'e4' });
  fire('pointerdown', { button: 2, square: 'e2' });
  fire('pointercancel', {});
  fire('pointerup', { button: 2, square: 'e4' });
  assert.equal(hand.size(), 0);
  assert.equal(hand.isDrawing(), false);
});

test('released off the board, the arrow ends on the last square the pointer was over', () => {
  const { hand, fire } = rig();
  fire('pointerdown', { button: 2, square: 'b1' });
  fire('pointermove', { square: 'c3' });
  fire('pointermove', { square: null });
  fire('pointerup', { button: 2, square: null });
  assert.deepEqual(keys(hand.list()), ['arrow:b1-c3']);
});

// ccp-bugs #1625: the right button let go where the board never heard it (alt-tab, an overlay, outside
// the window). The open press used to stay open, so the next LEFT click closed it and drew an arrow.
test('a lost right release: the next left click draws nothing and wipes the board', () => {
  const { hand, fire, right } = rig();
  right('d4');
  fire('pointerdown', { button: 2, square: 'c3' });
  fire('pointermove', { button: -1, square: 'f6' });   // no pointerup ever comes
  const down = fire('pointerdown', { button: 0, square: 'f6' });
  fire('pointerup', { button: 0, square: 'f6' });
  assert.deepEqual(keys(hand.list()), [], 'no c3-f6 arrow, and the old circle is wiped');
  assert.equal(hand.isDrawing(), false);
  assert.equal(down.defaultPrevented, false, 'the click still goes on to drag.js');
});

test('a lost right release: a new right press starts over from its own square', () => {
  const { hand, camera, fire, right } = rig();
  fire('pointerdown', { button: 2, square: 'c3' });   // released unheard
  right('e2', 'e4');
  assert.deepEqual(keys(hand.list()), ['arrow:e2-e4'], 'not c3-e4');
  assert.deepEqual(camera, [], 'and the camera did not take the second press');
});

test('lost capture, a window blur, or a move with the right button up ends the open arrow', () => {
  for (const end of ['lostpointercapture', 'blur', 'move']) {
    const { hand, draws, fire, win } = rig();
    fire('pointerdown', { button: 2, square: 'c3' });
    fire('pointermove', { button: -1, buttons: 2, square: 'f6' });
    assert.equal(hand.isDrawing(), true, end);
    if (end === 'lostpointercapture') fire('lostpointercapture', {});
    else if (end === 'blur') win.dispatchEvent(new Event('blur'));
    else fire('pointermove', { button: -1, buttons: 0, square: 'g7' });
    assert.equal(hand.isDrawing(), false, end);
    assert.equal(draws.at(-1).preview, null, `${end}: the faint arrow is gone`);
    fire('pointerup', { button: 2, square: 'g7' });
    fire('pointerdown', { button: 0, square: 'g7' });
    fire('pointerup', { button: 0, square: 'g7' });
    assert.equal(hand.size(), 0, `${end}: nothing drawn afterwards`);
  }
});

test('only the right button lets go of an arrow', () => {
  const { hand, fire } = rig();
  fire('pointerdown', { button: 2, square: 'b1' });
  fire('pointermove', { button: -1, buttons: 2, square: 'c3' });
  fire('pointerup', { button: 0, square: 'c3' });
  fire('pointerup', { button: 1, square: 'c3' });
  assert.equal(hand.size(), 0, 'a left or middle release adds nothing');
  fire('pointerup', { button: 2, square: 'c3' });
  assert.deepEqual(keys(hand.list()), ['arrow:b1-c3']);
});

test('local only: nothing in the shapes code can reach the bus, the host or the wire', () => {
  for (const file of ['../board/annotations.js', '../board/annotate.js']) {
    const src = readFileSync(new URL(file, import.meta.url), 'utf8');
    assert.doesNotMatch(src, /\bemit\(|postToHost|fetch\(|localStorage|sessionStorage|from '\.\.\/net\//, file);
  }
  const boot = readFileSync(new URL('../boot.js', import.meta.url), 'utf8');
  const wiring = boot.split('\n').filter(l => l.includes('annotate'));
  assert.ok(wiring.length >= 3, 'boot builds it');
  for (const line of wiring) assert.doesNotMatch(line, /emit\(|postToHost|save/, line);
});
