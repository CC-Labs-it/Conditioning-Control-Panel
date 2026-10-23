// tests/render-fxp5.test.mjs - polish 5 wave 1 FX lane: the clear lane and the Flash panes (V2), the spiral gate
// lens fade (V3), the particle birth, spark width and the ring high-water mark (V4).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { laneMask, laneKeep, flashPanes, flashRect } from '../render/items/fxmath.js';
import { SCREEN_LOOK as L } from '../render/items/look.js';
import { BIRTH_LOOK as B, birthK, birthSize, sparkWidth, ringSlot, GATE_LENS as G, gateLensK, gateLensSize } from '../render/atlas.js';

const src = (f) => readFileSync(new URL('../render/' + f, import.meta.url), 'utf8');
// a uv point (x of width, y of height, y down) to view heights from the centre
const toView = (u, v, aspect) => [(u - 0.5) * aspect, v - 0.5];

test('lane: clear from the kart down to the bottom edge and up to the next bend, effects whole at the frame', () => {
  for (const [x, y] of [[0, 0.3], [0, 0.45], [0, 0], [0, -0.08], [0.15, 0.35]]) assert.ok(laneMask(x, y) > 0.95, 'clear at ' + x + ',' + y);
  for (const aspect of [16 / 9, 21 / 9, 4 / 3]) {
    for (const [u, v] of [[0, 0], [1, 0], [0, 1], [1, 1], [0.05, 0.5], [0.95, 0.5], [0.5, 0.02]]) {
      const [x, y] = toView(u, v, aspect);
      assert.equal(laneKeep(x, y), 1, 'whole at ' + u + ',' + v + ' at ' + aspect);
    }
  }
  assert.ok(Math.abs(laneKeep(0, 0.3) - (1 - L.lane.clear)) < 1e-9, 'the lane keeps 1 - clear');
  assert.ok(L.lane.clear >= 0.8 && L.lane.clear <= 1);
  // soft: no step anywhere along a row across the lane
  let prev = laneMask(-0.9, 0.2);
  for (let x = -0.9; x <= 0.9; x += 0.01) { const m = laneMask(x, 0.2); assert.ok(Math.abs(m - prev) < 0.15, 'soft at ' + x); prev = m; }
});

test('flash panes: two soft panes at the sides, clear of the lane middle, inside the view, never more than the cover', () => {
  for (const aspect of [16 / 9, 4 / 3, 21 / 9, 9 / 19.5]) {
    for (const seed of [0, 1, 3, 7]) {
      const p = flashPanes(0.4, aspect, { seed });
      assert.equal(p.mirror, true);
      assert.ok(p.cx - p.hw >= 0 && p.cx + p.hw < 0.5, 'left pane left of the middle at ' + aspect);
      assert.ok(p.cy - p.hh >= 0 && p.cy + p.hh <= 1, 'inside at ' + aspect);
      assert.ok(2 * (p.hw * 2 * p.hh * 2) <= 0.4 + 1e-9, 'two panes within the cover');
      const [x, y] = toView(p.cx, p.cy, aspect);
      // a portrait phone is nearly all lane; on a landscape view the panes sit outside it
      if (aspect > 1) assert.ok(laneMask(x, y) < 0.05, 'pane centre outside the lane at ' + aspect);
    }
  }
  // the Gentle corner card stays one small card (flashRect's)
  const c = flashPanes(0.4, 16 / 9, { corner: true, scale: 0.35 });
  assert.equal(c.mirror, false);
  const r = flashRect(0.4, 16 / 9, { corner: true, scale: 0.35 });
  assert.equal(c.cx, r.cx); assert.equal(c.hw, r.hw);
  const [x, y] = toView(c.cx, c.cy, 16 / 9);
  assert.ok(laneMask(x, y) < 0.05, 'the corner card sits outside the lane');
});

test('screenfx: Flash, Spiral, Pink and Blackout take their lane share; no hard card edge, no drop shadow, OUT still a fade', () => {
  const s = src('screenfx.js');
  assert.ok(s.includes('const keep = '), 'one lane share');
  for (const k of ['u.flash.mul(keep)', 'u.pink.mul(keep)', 'reveal.mul(u.spiral).mul(keep)', 'mul(min(u.black.mul(3), 1)).mul(keep)']) assert.ok(s.includes(k), k);
  assert.ok(!s.includes('smoothstep(0, 0.004, d)'), 'no 4 thousandths edge on the card');
  assert.ok(!s.includes('L.flashShadow'), 'no drop shadow');
  assert.ok(s.includes('flashPanes('), 'panes, not the middle card');
});

test('birth: alpha from 0 and size from about a third, open within 40 to 60 ms, eased out', () => {
  assert.ok(B.birthS >= 0.04 && B.birthS <= 0.06);
  assert.ok(B.sizeFrom >= 0.3 && B.sizeFrom <= 0.4);
  assert.equal(birthK(0), 0);
  assert.equal(birthK(B.birthS), 1);
  assert.equal(birthK(5), 1);
  assert.ok(birthK(B.birthS / 2) > 0.5, 'eased out: more than half open at half the birth');
  let prev = -1;
  for (let t = 0; t <= B.birthS; t += B.birthS / 20) { const k = birthK(t); assert.ok(k >= prev); prev = k; }
  assert.equal(birthSize(0), B.sizeFrom);
  assert.equal(birthSize(1), 1);
  assert.equal(birthK(1, { birthS: 0, sizeFrom: 1 }), 1);
});

test('birth: drift sparks are fatter at tier 1, never thinner than before', () => {
  assert.ok(sparkWidth(1) > sparkWidth(2) && sparkWidth(2) >= sparkWidth(3));
  assert.ok(sparkWidth(1) > 1.2 && sparkWidth(1) < 1.6);
  for (const t of [0, 1, 2, 3, 4]) assert.ok(sparkWidth(t) >= 1);
  assert.equal(sparkWidth(0), 1);
});

test('ring: grows into fresh space while the head slot lives, reuses dead slots, wraps over the cap when full', () => {
  const cap = 6, life = new Float32Array(cap), alive = (i) => life[i] > 0;
  let head = 0, hw = 0;
  const take = (lifeS) => { const r = ringSlot(head, hw, cap, alive); head = r.head; hw = r.hw; life[r.i] = lifeS; return r.i; };
  assert.deepEqual([take(1), take(1), take(1)], [0, 1, 2], 'fresh slots in order');
  assert.equal(hw, 3);
  life[0] = 0;                                  // slot 0 dies
  assert.equal(take(1), 0, 'a dead slot under the head is reused');
  assert.equal(hw, 3, 'the mark does not grow');
  for (let k = 0; k < 3; k++) take(1);          // all alive: grow to the cap
  assert.equal(hw, cap);
  const order = [take(1), take(1)];
  assert.ok(order.every((i) => i >= 0 && i < cap), 'full: the ring overwrites the oldest within the cap');
  // a quiet cloud: a smaller cap (tier step) clamps the mark
  const r = ringSlot(5, 6, 4, () => false);
  assert.ok(r.hw <= 4 && r.i < 4);
});

test('particles: born clear, birth applied in update, high-water count and range uploads', () => {
  const s = src('particles.js');
  assert.ok(s.includes('cl[i * 4 + 3] = 0;'), 'born with no alpha');
  assert.ok(s.includes('birthK(maxLife[i] - life[i], BL)'), 'birth from the age');
  assert.ok(s.includes('birthSize(bk, BL)'), 'size opens with the birth');
  assert.ok(s.includes('sprite.count = hw'), 'draws to the high-water mark');
  assert.ok(s.includes('addUpdateRange(0, was * a.itemSize)'), 'uploads only the live range');
  assert.ok(!/needsUpdate = true; aCol\.needsUpdate/.test(s), 'no full-cap upload left');
});

test('gates: particles fade and shrink over the last metres before the lens, whole far away', () => {
  assert.ok(G.toM >= 8 && G.toM <= 12, 'the fade ends 8 to 12 m out');
  assert.ok(G.fromM < 3);
  assert.equal(gateLensK(0.5), 0);
  assert.equal(gateLensK(G.toM), 1);
  assert.equal(gateLensK(60), 1);
  assert.ok(gateLensK(5) < 0.35, 'a gate 5 m out is mostly gone ' + gateLensK(5));
  let prev = -1;
  for (let d = 0; d <= 15; d += 0.25) { const k = gateLensK(d); assert.ok(k >= prev); prev = k; }
  assert.equal(gateLensSize(1), 1);
  assert.equal(gateLensSize(0), G.shrinkTo);
  const s = src('spirals3d.js');
  assert.ok(s.includes('smoothstep(GATE_LENS.fromM, GATE_LENS.toM, camD)') && s.includes('.mul(lensK)'), 'the shader uses the lens share');
});
