import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { screenAmounts, irisRadius, flashRect, shatterTilt, SCREEN_KINDS } from '../render/items/fxmath.js';
import { SCREEN_LOOK as L } from '../render/items/look.js';

test('screenfx: the browser module parses before its optional loading boundary', () => {
  const input = readFileSync(new URL('../render/screenfx.js', import.meta.url), 'utf8');
  assert.doesNotThrow(() => execFileSync(process.execPath, ['--input-type=module', '--check'], { input, encoding: 'utf8' }));
});

const scr = (kind, k = 1, extra = {}, params = {}) => ({ kind, t: 0.5, durS: 2, k, strength: 1, prevKind: null, prevK: 0,
  ...extra, params: { gentle: false, corner: false, dimOnly: false, blurMul: 1, flicker: true, cover: 0.4, seed: 1, ...params } });

test('screenfx: idle costs nothing (no screen, none, or a finished fade)', () => {
  assert.equal(screenAmounts(null).any, false);
  assert.equal(screenAmounts(scr('none', 0)).any, false);
  const a = screenAmounts(scr('none', 0, { prevKind: 'melt', prevK: 0 }));
  assert.equal(a.any, false);
  for (const k of SCREEN_KINDS) assert.equal(a[k], 0);
});

test('screenfx: one kind at full strength drives only its own amount', () => {
  for (const kind of SCREEN_KINDS) {
    const a = screenAmounts(scr(kind));
    assert.equal(a[kind], 1, kind);
    for (const k of SCREEN_KINDS) if (k !== kind) assert.equal(a[k], 0, kind + ' leaks into ' + k);
  }
});

test('screenfx: a replace fades the old kind out while the new one comes in, never over 1', () => {
  const a = screenAmounts(scr('spiral', 0.3, { prevKind: 'flash', prevK: 0.7 }));
  assert.ok(Math.abs(a.spiral - 0.3) < 1e-9 && Math.abs(a.flash - 0.7) < 1e-9);
  const b = screenAmounts(scr('melt', 1, { prevKind: 'melt', prevK: 1 }));
  assert.equal(b.melt, 1);
});

test('screenfx: Gentle halves the amounts and slows motion; corner Flash and Spiral dim keep theirs', () => {
  const g = { gentle: true, blurMul: 0.5 };
  assert.equal(screenAmounts(scr('melt', 1, {}, g)).melt, L.gentleMul);
  assert.equal(screenAmounts(scr('blackout', 1, {}, g)).blackout, L.gentleMul);
  const f = screenAmounts(scr('flash', 1, { strength: 0.35 }, { ...g, corner: true }));
  assert.equal(f.flash, 1);
  assert.equal(f.corner, true);
  assert.equal(f.cornerScale, 0.35);
  const s = screenAmounts(scr('spiral', 1, { strength: 0.35 }, { ...g, dimOnly: true }));
  assert.equal(s.spiral, 0.35);
  assert.equal(s.dimOnly, true);
  assert.equal(s.timeMul, L.gentleTimeMul);
  assert.equal(s.blurMul, 0.5 / L.gentleMul);
});

test('screenfx: Gentle blur radius (amount x blurMul) is blurMul of normal, halved once', () => {
  for (const kind of ['brainDrain', 'melt', 'freeze']) {
    const n = screenAmounts(scr(kind, 0.8));
    const g = screenAmounts(scr(kind, 0.8, {}, { gentle: true, blurMul: 0.5 }));
    const rn = n[kind] * n.blurMul, rg = g[kind] * g.blurMul;
    assert.ok(Math.abs(rg - rn * 0.5) < 1e-9, kind + ' radius halved once');
  }
});

test('screenfx: Blackout never closes all the way', () => {
  assert.ok(irisRadius(0) >= 1, 'no closure at rest');
  assert.ok(irisRadius(1) >= 0.2, 'a circle stays');
  assert.ok(irisRadius(0.5) > irisRadius(1));
  assert.equal(irisRadius(5), irisRadius(1));
});

test('screenfx: the Flash card covers its share of the view and stays inside it', () => {
  for (const aspect of [16 / 9, 4 / 3, 21 / 9, 9 / 19.5]) {
    const r = flashRect(0.4, aspect, { seed: 3 });
    const area = r.hw * 2 * r.hh * 2;
    assert.ok(area <= 0.4 + 1e-9 && area > 0.15, 'area ' + area + ' at ' + aspect);
    assert.ok(r.cx - r.hw >= 0 && r.cx + r.hw <= 1 && r.cy - r.hh >= 0 && r.cy + r.hh <= 1, 'inside at ' + aspect);
  }
  const c = flashRect(0.4, 16 / 9, { corner: true, scale: 0.35 });
  assert.ok(c.hw * c.hh * 4 < 0.06, 'the Gentle corner card is small');
  assert.ok(c.cx - c.hw >= 0, 'and inside the view');
});

test('screenfx: Shatter tilts briefly and settles, no tilt without an amount', () => {
  let peak = 0;
  for (let t = 0; t < 3; t += 0.01) peak = Math.max(peak, Math.abs(shatterTilt(t, 1)));
  assert.ok(peak > 0 && peak <= L.shatterTiltRad);
  assert.ok(Math.abs(shatterTilt(3, 1)) < 0.01 * L.shatterTiltRad + 1e-4);
  assert.equal(shatterTilt(0.2, 0), 0);
});

test('screenfx: never blind (a floor of the image, highlights, capped overlays)', () => {
  assert.ok(L.keepImage > 0 && L.keepBrightGain >= 0.5);
  assert.ok(L.flashAlpha < 1 && L.spiralAlpha <= 0.5 && L.frostAlpha < 1);
  assert.ok(L.irisTo >= 0.2 && L.pinkContrast < 1);
});
