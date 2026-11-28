import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { POP_PICTURE_OPACITY, POP_PICTURE_ATTR, markPopPicture } from '../ui/pop-picture.js';
import { drawPhotoPrint } from '../ui/unlocks/arcade/slot-flight.js';

const read = (p) => readFileSync(new URL(`../${p}`, import.meta.url), 'utf8');

test('popped pictures are click-through and drawn at the one opacity', () => {
  assert.equal(POP_PICTURE_OPACITY, 0.8);
  const css = read('styles.css');
  assert.match(css, /\[data-pop-picture\],\s*\[data-pop-picture\] \*\s*\{\s*pointer-events:\s*none !important;/);
  const rule = css.match(/\[data-pop-picture\] img, \[data-pop-picture\] video \{ opacity: ([\d.]+); \}/);
  assert.ok(rule, 'img/video opacity rule');
  assert.equal(Number(rule[1]), POP_PICTURE_OPACITY);
  const attrs = {}, el = { style: {}, setAttribute(k, v) { attrs[k] = v; } };
  assert.equal(markPopPicture(el), el);
  assert.equal(attrs[POP_PICTURE_ATTR], '');
  assert.equal(el.style.pointerEvents, 'none');
});

test('every pop-over-the-page picture surface wears the mark', () => {
  for (const file of ['ui/unlocks/basics/side2.js', 'ui/unlocks/arcade/slot-effects.js']) assert.match(read(file), /markPopPicture\(/, file);
});

test('a print multiplies its fade by 0.8 and keeps its credit at the fade alone', () => {
  const alphas = {};
  const g = new Proxy({
    measureText: () => ({ width: 40 }),
    drawImage() { alphas.photo = g.globalAlpha; },
    fillText() { alphas.credit = g.globalAlpha; },
  }, {
    get(t, k) { return k in t ? t[k] : () => {}; },
    set(t, k, v) { t[k] = v; return true; },
  });
  const frame = { x: 0, y: 0, w: 300, h: 360, angle: 0, opacity: 0.5 };
  drawPhotoPrint(g, frame, { image: { width: 400, height: 500 }, credit: 'via Scrolller' });
  assert.equal(alphas.photo, 0.5 * POP_PICTURE_OPACITY);
  assert.equal(alphas.credit, 0.5);
});
