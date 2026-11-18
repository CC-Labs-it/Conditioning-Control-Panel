// The sheet's juice (sheet/juice.js): the motion levels as numbers, the open stagger's order, the wallet's digit roll,
// the hung tag's swing, the saving-for bar's overshoot, and the UI sounds staying under the bells. On the fake DOM the
// juice is inert and the page works the same.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { TIMING, levelOf, durOf, fadeOnly, staggerDelays, rollSteps, changedDigits, meterFrames, swingAngles, pctOf, installJuice } from '../sheet/juice.js';
import { SOFT } from '../sheet/sound.js';

test('levels: full moves, less is half the travel, Gentle is fades only with a 300 ms floor, off is nothing', () => {
  assert.deepEqual(['full', 'less', 'off'].map((motion) => levelOf({ motion }).amp), [1, 0.5, 0]);
  assert.equal(levelOf({ motion: 'off', gentle: true }).on, false, 'off wins over Gentle');
  const g = levelOf({ gentle: true });
  assert.deepEqual([g.on, g.amp, g.fade], [true, 0, true]);
  assert.equal(durOf(100, g), TIMING.gentleFloor);
  assert.equal(durOf(400, levelOf({ motion: 'off' })), 0);
  assert.equal(durOf(400, levelOf({ motion: 'less' })), Math.round(400 * TIMING.lessTime));
  assert.deepEqual(fadeOnly([{ translate: '0 9px', opacity: 0 }, { scale: '1.1', opacity: 1, offset: 0.5 }]), [{ opacity: 0 }, { opacity: 1, offset: 0.5 }]);
  assert.equal(fadeOnly([{ scale: '1' }, { scale: '1.2' }]), null, 'a pure movement has nothing left under Gentle');
});

test('the open stagger: head, then tabs close together, then the spotlight, the shelves, the fitting room', () => {
  const kinds = ['room', 'sec', 'spot', 'head', 'tab', 'tab', 'tab', 'sec'];
  const d = staggerDelays(kinds, { step: 60, span: 10000 });
  const at = (k) => kinds.map((x, i) => [x, d[i]]).filter(([x]) => x === k).map(([, v]) => v);
  assert.equal(at('head')[0], 0);
  assert.deepEqual(at('tab'), [60, 81, 102], 'tabs ripple at a third of a step');
  assert.ok(at('spot')[0] > Math.max(...at('tab')) && Math.min(...at('sec')) > at('spot')[0] && at('room')[0] > Math.max(...at('sec')));
  assert.deepEqual(at('sec'), [...at('sec')].sort((a, b) => a - b), 'shelves keep page order');
  const many = staggerDelays(Array(40).fill('sec'));
  assert.equal(Math.max(...many), TIMING.arriveSpan, 'the whole stagger is capped');
});

test('the wallet rolls down digit by digit: fast first, slowing, no repeats, ends on the new balance', () => {
  const s = rollSteps(410, 50);
  assert.equal(s.at(-1), 50);
  assert.ok(s.length > 4 && s.length <= TIMING.rollSteps);
  for (let i = 1; i < s.length; i++) assert.ok(s[i] < s[i - 1], 'strictly down');
  assert.ok(410 - s[0] > s.at(-2) - s.at(-1), 'big steps first, small at the end');
  assert.deepEqual(rollSteps(7, 7), [7]);
  assert.deepEqual(rollSteps(3, 1), [2, 1]);
  assert.deepEqual(changedDigits(410, 395), [0, 1, 2]);
  assert.deepEqual(changedDigits(410, 400), [1]);
  assert.deepEqual(changedDigits(1050, 990), [0, 1]);
  assert.deepEqual(changedDigits(95, 105), [0, 1], 'a new leading digit counts');
});

test('a hung tag swings big first, the other way each time, and rests; the bar overshoots and settles', () => {
  const a = swingAngles(10, 1, 4);
  assert.deepEqual([a[0], a.at(-1)], [0, 0]);
  for (let i = 2; i < a.length - 1; i++) assert.ok(Math.sign(a[i]) === -Math.sign(a[i - 1]) && Math.abs(a[i]) < Math.abs(a[i - 1]));
  assert.ok(swingAngles(10, 0.5)[1] === 5, 'less: half the swing');
  assert.ok(swingAngles(10, 0).every((x) => x === 0), 'Gentle: no swing');
  const m = meterFrames(40, 80, 1);
  assert.equal(m[0].scale, '0.5 1');
  assert.equal(m.at(-1).scale, '1 1');
  assert.ok(Math.max(...m.map((f) => parseFloat(f.scale))) > 1, 'it overshoots');
  assert.equal(meterFrames(10, 0)[0].scale, '1 1');
  assert.equal(pctOf('width:55%'), 55);
  assert.equal(pctOf(null), null);
});

test('the UI sounds sit in A major pentatonic, well under the bells, short', () => {
  const PENT = [440, 493.88, 554.37, 659.25, 739.99].flatMap((f) => [f / 4, f / 2, f, f * 2, f * 4]);
  const inKey = (f) => PENT.some((p) => Math.abs(p - f) < 0.5);
  const all = Object.entries(SOFT).flatMap(([k, v]) => (Array.isArray(v) ? v.map((x) => [k, x]) : [[k, v]]));
  for (const [k, s] of all) {
    assert.ok(inKey(s.f) && (s.f0 == null || inKey(s.f0)), `${k} in key`);
    assert.ok(s.vol <= 0.08, `${k} peak ${s.vol} is under the bells (0.1 to 0.12)`);
    assert.ok(s.len <= 1.2, `${k} is short`);
  }
});

test('on a page without Web Animations the juice is inert: paint passes through untouched', () => {
  let painted = 0;
  const root = { children: [], classList: { contains: () => false } };
  const view = { root, paint: () => { painted++; } };
  const j = installJuice(view, { motion: 'full', doc: {}, win: {} });
  view.paint({ status: 'ready', sp: 5, tabs: [] }, {});
  assert.equal(painted, 1);
  assert.equal(j.state.arrived, false);
  assert.equal(installJuice({ root, paint() {} }, { motion: 'off', doc: {}, win: {} }).level.on, false);
});

test('the juice styles only movement: no colour, border, font or size on the page parts it borrows', () => {
  const css = readFileSync(new URL('../sheet-juice.css', import.meta.url), 'utf8');
  const own = /^\.(j-|meter i\.j-shine|sheet \{|shroot)/;
  for (const rule of css.replace(/\/\*[\s\S]*?\*\//g, '').split('}')) {
    const [sel, body] = rule.split('{');
    if (!body || !sel.trim() || sel.includes('@keyframes') || /^\s*(from|to|\d)/.test(sel.trim())) continue;
    if (own.test(sel.trim())) continue;
    assert.doesNotMatch(body, /(^|[;\s])(color|background|border|font|width|height|padding|margin)\s*:/, `${sel.trim()} sets a look`);
  }
  for (const f of ['../sheet.html', '../mock/sheet.html']) assert.match(readFileSync(new URL(f, import.meta.url), 'utf8'), /sheet\.css">\s*<link rel="stylesheet" href="\.{1,2}\/sheet-juice\.css">/);
});
