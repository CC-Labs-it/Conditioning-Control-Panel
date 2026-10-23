// tests/render-water.test.mjs - water wave 1 (RENDER lane): the pure shapes behind the water material,
// the waterfall sheet and the wakes, plus the module shape (no three.js in the math; the TSL modules
// are read as source).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { WATER_LOOK, WATER_CUES, SURFACE_CODE, isWaterCode, flowDir, rippleScroll, foamWeight, gradeDeg, sheetRuns,
  waterTable, tableCode, wakeOnWater, splashRingM } from '../render/waterMath.js';

const src = (p) => readFileSync(new URL(p, import.meta.url), 'utf8');
const close = (a, b, e = 1e-9) => assert.ok(Math.abs(a - b) < e, `${a} != ${b}`);

test('surface codes follow the mesh contract and only stream / pool read as water', () => {
  assert.deepEqual(SURFACE_CODE, { glass: 0, grip: 1, haze: 2, stream: 3, pool: 4 });
  assert.ok(isWaterCode(3) && isWaterCode(4) && isWaterCode(3.4));
  assert.ok(!isWaterCode(0) && !isWaterCode(1) && !isWaterCode(2) && !isWaterCode(2.4));
});

test('scroll direction follows the flow (lateral = u, along = v) and still water still drifts', () => {
  assert.deepEqual(flowDir([5, 0]).dir, [0, 1]);
  assert.deepEqual(flowDir([0, -3]).dir, [-1, 0]);
  close(flowDir([3, 4]).mag, 5);
  assert.deepEqual(flowDir(null), { dir: [0, 1], mag: 0 });
  const [a, b] = rippleScroll([5, 0], 1);
  close(a[0], 0); close(a[1], WATER_LOOK.baseScrollMps + 5 * WATER_LOOK.rippleSpeedMul[0]);
  assert.ok(Math.hypot(...b) < Math.hypot(...a), 'field B scrolls slower');
  assert.ok(Math.abs(b[0]) > 1e-3, 'field B is turned off the flow axis');
  const still = rippleScroll([0, 0], 2);
  close(Math.hypot(...still[0]), WATER_LOOK.baseScrollMps * 2);
  const [l] = rippleScroll([5, -2.5], 1);
  assert.ok(l[0] < 0 && l[1] > 0, 'lateral flow toward the left scrolls the field left');
});

test('Gentle halves the scroll and never pulses (same direction, half the distance)', () => {
  const fast = rippleScroll([8, 0], 3), slow = rippleScroll([8, 0], 3, { gentle: true });
  for (let i = 0; i < 2; i++) { close(slow[i][0], fast[i][0] * WATER_LOOK.gentleScrollMul); close(slow[i][1], fast[i][1] * WATER_LOOK.gentleScrollMul); }
  assert.equal(WATER_LOOK.gentleScrollMul, 0.5);
});

test('foam gathers toward the rails, more on a pool, a touch on the side a lateral current runs into, none on glass', () => {
  assert.equal(foamWeight(0.2, 0), 0);
  assert.equal(foamWeight(0.2, 2), 0);
  const rail = foamWeight(0.2, 3), mid = foamWeight(5, 3);
  assert.ok(rail > 0.9 && mid === 0, `rail ${rail} mid ${mid}`);
  assert.ok(foamWeight(1.4, 3) > 0 && foamWeight(1.4, 3) < rail);
  assert.ok(foamWeight(1.4, 4) > foamWeight(1.4, 3), 'a pool edge foams more');
  assert.ok(foamWeight(1.4, 3, { u: -3, flow: [5, -2.5] }) > foamWeight(1.4, 3, { u: 3, flow: [5, -2.5] }));
  for (let e = 0; e < 4; e += 0.1) assert.ok(foamWeight(e, 4) <= 1 && foamWeight(e, 4) >= 0);
});

test('sheet placement: a chute run is the steep stream stretch, a pool or a flat stream ends it', () => {
  close(gradeDeg(10, 10), 45); close(gradeDeg(-10, 10), 45); assert.equal(gradeDeg(1, 0), 0);
  const samples = [];
  for (let s = 0; s <= 100; s += 2) {
    const chute = s >= 30 && s <= 54, pool = s > 54 && s <= 94;
    samples.push({ s, y: chute ? 21 - (s - 30) * 14 / 24 : s < 30 ? 21 : 7, surface: chute || s >= 20 && s < 30 ? 'stream' : pool ? 'pool' : 'glass' });
  }
  const runs = sheetRuns(samples);
  assert.equal(runs.length, 1);
  assert.equal(runs[0].s0, 30); assert.equal(runs[0].s1, 54);
  // the gentle approach stream (flat, s 20..30) is not a chute; a steep GLASS stretch is not one either
  const glassDrop = samples.map((p) => ({ ...p, surface: p.surface === 'stream' ? 'glass' : p.surface }));
  assert.deepEqual(sheetRuns(glassDrop), []);
  const shallow = samples.map((p) => ({ ...p, y: p.y * 0.3 }));
  assert.deepEqual(sheetRuns(shallow), []);
  assert.equal(WATER_LOOK.chuteGradeDeg, 25);
});

test('the water table answers by s with one read and wraps the lap', () => {
  const t = waterTable((s) => (s >= 10 && s < 20 ? 'stream' : s >= 30 && s < 40 ? 'pool' : 'glass'), 50);
  assert.equal(t.length, 50);
  assert.equal(tableCode(t, 15, 50), 3); assert.equal(tableCode(t, 35, 50), 4); assert.equal(tableCode(t, 5, 50), 0);
  assert.equal(tableCode(t, 65, 50), 3); assert.equal(tableCode(t, -35, 50), 3);
  assert.equal(tableCode(null, 15, 50), 0);
});

test('wakes on water are wider and whiter; the splash ring grows with the fall', () => {
  assert.deepEqual(wakeOnWater(0), { widthMul: 1, white: 0 });
  const w = wakeOnWater(3);
  assert.ok(w.widthMul > 1.4 && w.white > 0.4);
  assert.deepEqual(wakeOnWater(4), w);
  close(splashRingM(0), WATER_LOOK.splashRingM[0]); close(splashRingM(20), WATER_LOOK.splashRingM[1]);
  assert.ok(splashRingM(6) > splashRingM(3));
});

test('module shape: the road reads the attributes defensively, water is one branch, the sheet is one batch', () => {
  const road = src('../render/materials/road.js'), water = src('../render/materials/water.js'), fall = src('../render/waterfall.js');
  const scene = src('../render/scene.js'), cues = src('../fx/cues.js'), wakes = src('../render/wakes.js');
  assert.ok(water.includes("attribute('surface', 'float')") && water.includes("attribute('flow', 'vec2')"));
  assert.ok(road.includes('createWaterSurface(') && road.includes('waterAttributes()'));
  assert.ok(!/#[0-9a-f]{6}/i.test(water), 'the water takes the section palette, never a fixed colour');
  assert.ok(water.includes('pal.col('));
  assert.ok(scene.includes("g.setAttribute('surface'") && scene.includes("g.setAttribute('flow'"), 'an old mesh reads as glass');
  assert.ok(scene.includes("on('splash'") && scene.includes("on('chute'"));
  assert.ok(fall.includes('new Mesh(g, mat)') && (fall.match(/new Mesh\(/g) || []).length === 1, 'one sheet mesh');
  assert.ok(!fall.includes('requestAnimationFrame') && !fall.includes('setInterval'), 'no clock of its own');
  for (const k of ['splash(fx', 'surface(fx', 'chute(fx']) assert.ok(cues.includes(k), k);
  assert.ok(cues.includes('fx.gentle.kick(W.splashDipDeg') && cues.includes('fx.gentle.kick(W.chuteLeanDeg'));
  assert.ok(wakes.includes('wakeOnWater('));
  assert.ok(WATER_CUES.splashDipDeg < 0 && WATER_CUES.streamLiftDeg > 0 && WATER_CUES.chuteLeanDeg > 0);
});
