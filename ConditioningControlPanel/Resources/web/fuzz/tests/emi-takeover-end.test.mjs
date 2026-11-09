import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as T from '../sim/takeover.js';
import { CONFIG } from '../data/config.js';

const SCREENS = [
  { name: '1920x1080', w: 1920, h: 1080, controls: { x: 1920 - 330, y: 0, w: 330, h: 64 } },
  { name: '1440x900', w: 1440, h: 900, controls: { x: 1440 - 330, y: 0, w: 330, h: 64 } },
  { name: '1280x560', w: 1280, h: 560, controls: { x: 1280 - 330, y: 0, w: 330, h: 64 } },
  { name: '390x844', w: 390, h: 844, controls: { x: 390 - 250, y: 0, w: 250, h: 64 }, lite: true },
];
const hits = (a, b) => a.x < b.x + b.w && a.x + a.w > b.x && a.y < b.y + b.h && a.y + a.h > b.y;
let seed = 11;
const rng = () => { seed = (seed * 16807) % 2147483647; return seed / 2147483647; };
const src = (f) => readFileSync(new URL(`../ui/unlocks/themes-b/${f}`, import.meta.url), 'utf8');

test('the front grid fills the screen, leaves Wake Up\'s corner empty, its lit cells read DROP, and it comes row after row', () => {
  for (const s of SCREENS) {
    const g = T.dropGrid(s);
    assert.ok(g.readable, s.name);
    assert.ok(s.w - g.cols * g.pitch < g.pitch && s.h - g.rows * g.pitch < g.pitch, `${s.name} fills the screen`);
    for (const c of g.cells) assert.ok(!hits({ x: c.x, y: c.y, w: g.pitch, h: g.pitch }, s.controls), `${s.name} cell under the controls`);
    const mask = T.dropMask(g.stacked);
    const lit = g.cells.filter((c) => c.lit).map((c) => `${c.c - g.c0},${c.r - g.r0}`).sort();
    const want = mask.flatMap((line, r) => [...line].flatMap((ch, c) => (ch === '1' ? [`${c},${r}`] : []))).sort();
    assert.deepEqual(lit, want, s.name);
    /* raster order: never back up a row, left to right inside one */
    for (let i = 1; i < g.cells.length; i++) {
      const a = g.cells[i - 1], b = g.cells[i];
      assert.ok(b.r > a.r || (b.r === a.r && b.c > a.c), `${s.name} raster order at ${i}`);
    }
  }
  assert.equal(T.dropGrid(SCREENS[3]).stacked, true, 'a portrait phone stacks DR over OP');
  assert.equal(T.dropGrid(SCREENS[1]).stacked, false);
});

test('front pixels arrive evenly from the start to the full screen, about 12-18 s', () => {
  const n = 400;
  assert.equal(T.gridCount(0, n), 0);
  assert.equal(T.gridCount(T.TAKE.FILL_S, n), n);
  let last = 0;
  for (let t = 0; t <= T.TAKE.FILL_S + 0.1; t += 0.1) { const c = T.gridCount(t, n); assert.ok(c >= last); last = c; }
  const mid = T.gridCount((T.TAKE.GRID_START_S + T.TAKE.FILL_S) / 2, n);
  assert.ok(Math.abs(mid - n / 2) <= 2, `even: ${mid}`);
  assert.ok(T.gridAt(0, n) === T.TAKE.GRID_START_S && Math.abs(T.gridAt(n - 1, n) - T.TAKE.FILL_S) < 1e-9);
  const fill = T.phaseStarts().shut;
  assert.ok(fill >= 12 && fill <= 18, `fill ${fill} s`);
  assert.ok(T.TAKE.BACK_S <= T.TAKE.FILL_S);
});

test('the back pile starts with the swarm where it stood, varies in size, and leaves no gap outside Wake Up\'s corner', () => {
  for (const s of SCREENS) {
    const g = T.dropGrid(s);
    const seeds = [{ x: s.w * 0.3, y: s.h * 0.6, size: 90 }, { x: s.w * 0.7, y: s.h * 0.4, size: 60 }];
    const pile = T.backPile({ w: s.w, h: s.h, pitch: g.pitch, seeds, controls: s.controls, lite: !!s.lite, rng });
    assert.deepEqual(pile.slice(0, 2).map((p) => [p.x + p.size / 2, p.y + p.size / 2, p.size, p.seed]), seeds.map((q) => [q.x, q.y, q.size, true]), `${s.name} seeds first`);
    const sizes = pile.filter((p) => !p.seed).map((p) => p.size);
    assert.ok(Math.max(...sizes) / Math.min(...sizes) > 1.3, `${s.name} different sizes`);
    for (const p of pile.filter((q) => !q.seed)) assert.ok(!hits({ x: p.x, y: p.y, w: p.size, h: p.size }, s.controls), `${s.name} back EMI under the controls`);
    const far = Math.max(...sizes);
    const near = (x, y) => x > s.controls.x - far && y < s.controls.y + s.controls.h + far;
    let gaps = 0, pts = 0;
    for (let x = 0; x <= s.w; x += 9) for (let y = 0; y <= s.h; y += 9) { if (near(x, y)) continue; pts += 1; if (!T.covered(pile, x, y)) gaps += 1; }
    assert.equal(gaps, 0, `${s.name}: ${gaps} of ${pts} points show the page`);
    const max = s.lite ? 260 : 520;
    assert.ok(pile.length <= max, `${s.name}: ${pile.length} back EMIs`);
  }
  /* with no swarm it starts from the middle of the bottom */
  const g = T.dropGrid(SCREENS[1]);
  const pile = T.backPile({ w: 1440, h: 900, pitch: g.pitch, rng });
  assert.ok(pile.every((p) => !p.seed));
  assert.ok(pile[0].y + pile[0].size / 2 > 600, 'nearest the bottom first');
});

test('the back pile piles in: the swarm at once, then slow first and fast last, done by BACK_S', () => {
  const n = 300, seeds = 4;
  for (let k = 0; k < seeds; k++) assert.equal(T.backAt(k, n, seeds), 0);
  let last = 0;
  for (let k = seeds; k < n; k++) { const a = T.backAt(k, n, seeds); assert.ok(a >= last); last = a; }
  assert.ok(Math.abs(T.backAt(n - 1, n, seeds) - T.TAKE.BACK_S) < 1e-9);
  const half = T.backAt(seeds + Math.floor((n - seeds) / 2), n, seeds);
  assert.ok(half > T.TAKE.BACK_S * 0.5, `slow first: half the pile at ${half.toFixed(1)} s`);
});

test('the beats come in the brief\'s order and lengths, and the take fits the engine cap', () => {
  for (const calm of [false, true]) {
    const p = T.phaseStarts({ calm });
    const order = ['fill', 'shut', 'drop', 'error', 'frozen', 'giant', 'cheeky', 'close', 'end'];
    for (let i = 1; i < order.length; i++) assert.ok(p[order[i]] >= p[order[i - 1]], `${order[i]} after ${order[i - 1]}`);
    assert.ok(Math.abs(p.drop - p.shut - 1) < 0.25, 'shut about 1 s');
    assert.ok(Math.abs(p.error - p.drop - 3) < 0.5, 'DROP about 3 s');
    assert.ok(Math.abs(p.giant - p.frozen - 1.5) < 0.3, 'frozen about 1.5 s');
    assert.ok(Math.abs(p.cheeky - p.giant - 2.6) < 0.5, 'idle blinks about 2.5 s');
    assert.ok(Math.abs(p.close - p.cheeky - 1.5) < 0.3, 'cheeky about 1.5 s');
    assert.equal(T.phaseAt(p.drop + 0.01, { calm }), 'drop');
    assert.equal(T.phaseAt(p.end + 1, { calm }), 'end');
  }
  assert.equal(T.phaseStarts({ calm: true }).frozen, T.phaseStarts({ calm: true }).error, 'no shake under Gentle');
  assert.ok(Math.abs(T.TAKE.SHAKE_S - 0.3) < 0.05, 'a 300 ms shake');
  assert.ok(T.takeMaxS() * 1000 < CONFIG.ENDING_TAKE_MAX_MS - 20000, `${T.takeMaxS()} s`);
});

test('no screen flashes 3 times a second, and the giant EMI blinks two or three times inside her idle', () => {
  for (const lite of [false, true]) for (const back of [false, true]) for (let i = 0; i <= 100; i++) assert.ok(T.flickerGap(() => i / 100, lite, back) > 334);
  for (let r = 0; r < 200; r++) {
    const b = T.blinkTimes(rng);
    assert.ok(b.length >= 2 && b.length <= 3);
    for (let i = 0; i < b.length; i++) {
      assert.ok(b[i] >= T.TAKE.POP_S && b[i] + T.TAKE.BLINK_LEN_S <= T.TAKE.POP_S + T.TAKE.BLINK_S, `blink at ${b[i]}`);
      if (i) assert.ok(b[i] - b[i - 1] > 0.4, 'blinks apart');
    }
  }
});

test('the error dialog is short, says it is the game, never she/her toward the player', () => {
  const d = T.errorDialog();
  const all = [d.title, ...d.lines, d.button].join(' ');
  assert.ok(/This is the game/.test(all));
  assert.ok(d.lines.length <= 3 && d.lines.every((l) => l.length <= 60));
  assert.ok(!/\b(she|her)\b/i.test(all));
  assert.ok(![...String.fromCharCode(0x2013, 0x2014), "!"].some((ch) => all.includes(ch)), 'no dashes, no exclamation marks');
});

test('the take keeps its hard rules in the source', () => {
  const take = src('emi-takeover.js');
  assert.ok(!/speechSynthesis|SpeechSynthesisUtterance/.test(take));
  assert.ok(!/drawImage|getImageData|texImage2D/.test(take), 'pictures never go through a canvas');
  assert.ok(!/setTimeout|setInterval|requestAnimationFrame/.test(take), 'game clock only');
  assert.ok(!/getElementById\('wake'\)|#wake\b/.test(take), 'Wake Up is never touched');
  assert.match(take, /reason === 'panic'/);
  assert.match(take, /audio\.solo\(true\)/, 'the freeze silences the page');
  assert.match(take, /assets\/adcard\/logo\.webp/);
  assert.match(take, /body-smug/);
  assert.match(take, /'\^_~'/);
  assert.ok(!/boop|pointerdown|bubble\(|placeBubble|EMI_BARKS/.test(take), 'no click-to-spawn, no bubbles in the ending');
  assert.match(src('emi-kit.js'), /referrerPolicy = 'strict-origin'/);
  const goon = src('goon.js');
  const block = goon.slice(goon.indexOf('go_end: {'));
  assert.ok(!/ending\(ctx\)|Four hours|later/.test(block.slice(0, block.indexOf('\n  },'))), 'no "four hours later" card: straight to the recap');
});
