/* The theme fate cards' pure rules (ui/unlocks/fate/themes/logic.js): Little Bubble size, Scan Line order and gap,
 * Name Tag words and spot; and that the three cards are registered and EMI's colour tiles carry no fixed goon amber. */
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  laterBuys, bubbleSize, BUBBLE_BASE, BUBBLE_BASE_LITE, BUBBLE_CAP, BUBBLE_CAP_LITE,
  planScan, lineSegments, scanPay, SCAN_CAP, SCAN_EVERY_MS, tagLabel, placeTag,
} from '../ui/unlocks/fate/themes/logic.js';
import { BY_ID } from '../data/items.js';

const rect = (left, top, w, h) => ({ left, top, right: left + w, bottom: top + h, width: w, height: h });

test('Little Bubble: counts only the purchases after it, grows a little each, and stops at its cap', () => {
  assert.equal(laterBuys(['a', 'b'], 'ft_bubble'), 0);
  assert.equal(laterBuys(['a', 'ft_bubble'], 'ft_bubble'), 0);
  assert.equal(laterBuys(['a', 'ft_bubble', 'c', 'd'], 'ft_bubble'), 2);
  assert.equal(bubbleSize(0), BUBBLE_BASE);
  assert.equal(bubbleSize(0, true), BUBBLE_BASE_LITE);
  assert.ok(bubbleSize(1) > bubbleSize(0), 'one buy makes it bigger');
  assert.ok(bubbleSize(10) - bubbleSize(0) < BUBBLE_BASE * 0.2, 'ten buys: still a little bubble');
  for (let n = 0; n < 400; n += 7) assert.ok(bubbleSize(n + 7) >= bubbleSize(n), 'never shrinks');
  assert.equal(bubbleSize(10000), Math.round(BUBBLE_BASE * BUBBLE_CAP));
  assert.equal(bubbleSize(10000, true), Math.round(BUBBLE_BASE_LITE * BUBBLE_CAP_LITE));
  assert.ok(bubbleSize(10000) <= 100 && bubbleSize(10000, true) <= 66, 'a corner, never the page');
});

test('Scan Line: crosses top to bottom, drops what is off screen, tiny or clipped away, and never the tag', () => {
  const w = 800, h = 600;
  const tag = rect(100, 100, 120, 70);
  const cands = [
    { rect: rect(300, 400, 80, 40) },          // 0 low
    { rect: rect(300, 50, 200, 40) },          // 1 high
    { rect: rect(-200, 200, 100, 40) },        // 2 off screen to the left
    { rect: rect(400, 300, 6, 40) },           // 3 too thin
    { rect: rect(150, 120, 40, 20) },          // 4 under the tag
    { rect: rect(500, 700, 80, 80) },          // 5 below the page
    { rect: rect(600, 200, 60, 60), clip: rect(0, 240, 800, 300) },   // 6 half scrolled away: counts its visible part
    { rect: rect(600, 100, 60, 60), clip: rect(0, 240, 800, 300) },   // 7 scrolled out of its shop
  ];
  const plan = planScan(cands, { w, h, skip: tag });
  assert.deepEqual(plan.map((p) => p.i), [1, 6, 0]);
  assert.equal(plan[1].rect.top, 240, 'clipped to its scrolling parent');
  for (let i = 1; i < plan.length; i++) assert.ok(plan[i].y >= plan[i - 1].y, 'sorted top to bottom');
});

test('Scan Line: over the cap the biggest things win, still in order', () => {
  const cands = Array.from({ length: 40 }, (_, i) => ({ rect: rect(10, 10 + i * 14, 20 + i, 12) }));
  const plan = planScan(cands, { w: 900, h: 900, cap: 5 });
  assert.equal(plan.length, 5);
  assert.deepEqual(plan.map((p) => p.i), [35, 36, 37, 38, 39]);
  assert.equal(planScan(cands, { w: 900, h: 900 }).length, Math.min(40, SCAN_CAP));
});

test('Scan Line: the line leaves a gap round the tag while it passes, whole elsewhere', () => {
  const tag = { left: 100, right: 220, top: 100, bottom: 170 };
  assert.deepEqual(lineSegments(50, 800, tag), [[0, 800]]);
  assert.deepEqual(lineSegments(130, 800, tag), [[0, 92], [228, 800]]);
  assert.deepEqual(lineSegments(130, 800, { left: 2, right: 120, top: 100, bottom: 170 }), [[128, 800]]);
  assert.deepEqual(lineSegments(130, 800, null), [[0, 800]]);
  assert.equal(SCAN_EVERY_MS, 60000, 'once a minute');
  assert.ok(scanPay(0) >= 1 && scanPay(1000) < 1000, 'a tick, never zero, never a lump');
});

test('Name Tag: the unit number once Unit Number renamed the player, else the signed-in name', () => {
  assert.equal(tagLabel({ displayName: 'Rory', hasSerial: false }), 'Rory');
  assert.equal(tagLabel({ displayName: '  Rory  ' }), 'Rory');
  assert.equal(tagLabel({ displayName: 'Rory', hasSerial: true, serial: 4017 }), 'UNIT 4017');
  assert.equal(tagLabel({ hasSerial: true, serial: 4017 }), 'UNIT 4017');
  assert.equal(tagLabel({ seed: 12345 }), 'UNIT ' + (1000 + 12345 % 9000), 'no name: the number Unit Complete would print');
});

test('Name Tag: a saved spot stays whole on a smaller page', () => {
  assert.deepEqual(placeTag({ fx: 0.5, fy: 0.5 }, { w: 1000, h: 800, tw: 100, th: 60 }), { x: 500, y: 400 });
  assert.deepEqual(placeTag({ fx: 0.98, fy: 0.99 }, { w: 390, h: 844, tw: 100, th: 60 }), { x: 284, y: 778 });
  assert.deepEqual(placeTag({ fx: -1, fy: -1 }, { w: 390, h: 844, tw: 100, th: 60 }), { x: 6, y: 6 });
});

test('the three theme fate cards have entries, and the rows are the brief\'s', async () => {
  const { default: group } = await import('../ui/unlocks/fate/themes.js');
  for (const id of ['ft_bubble', 'ft_scan', 'ft_tag']) {
    assert.equal(typeof group[id]?.mount, 'function', id);
    assert.equal(BY_ID[id]?.lane, 'fate', id);
  }
  assert.equal(BY_ID.ft_bubble.fate, 'doll');
  assert.equal(BY_ID.ft_scan.fate, 'unit');
  assert.equal(BY_ID.ft_tag.fate, 'unit');
});

test('Unit Complete saves the pinned Name Tag for last when the run has one', () => {
  const src = readFileSync(new URL('../ui/unlocks/themes-a/unit-complete-name.js', import.meta.url), 'utf8');
  assert.match(src, /\[data-ft-tag\]/);
  const take = readFileSync(new URL('../ui/unlocks/themes-a/unit-complete.js', import.meta.url), 'utf8');
  assert.match(take, /identity\.source\]/, 'the tag stays on screen until the name shot lifts it');
});

test('EMI is on every route: her plain colour tiles come from the run palette, no fixed goon amber', () => {
  const src = readFileSync(new URL('../ui/unlocks/themes-b/emi-kit.js', import.meta.url), 'utf8');
  const line = src.split('\n').find((l) => l.includes('export const tilesOf'));
  assert.ok(line && !/f2a33c/i.test(line), line);
});
