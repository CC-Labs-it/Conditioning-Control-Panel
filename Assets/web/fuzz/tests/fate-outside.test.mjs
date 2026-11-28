// Go Outside fate cards (handoff-1110/TC-FATE-CARDS-BRIEF.md): A Window's sky by the hour, Touch Grass's cooldowns,
// Fresh Air's gust timing and Door Ajar's width. The widgets live in ui/unlocks/fate/outside/.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { skyAt, phaseAt, hourOf, mix } from '../ui/unlocks/fate/outside/sky.js';
import * as G from '../ui/unlocks/fate/outside/grass-model.js';
import { DOOR, doorWidth, buysAfter, gustDelay, between, BREEZE } from '../ui/unlocks/fate/outside/rules.js';
import { GROUPS } from '../ui/unlocks/index.js';
import { BY_ID } from '../data/items.js';
import { FATE_BY_ID } from '../data/fate.js';

const lum = (c) => { const [r, g, b] = [1, 3, 5].map((i) => parseInt(c.slice(i, i + 2), 16)); return 0.2126 * r + 0.7152 * g + 0.0722 * b; };
const seq = (vals) => { let i = 0; return () => vals[i++ % vals.length]; };

test('the four Go Outside cards are registered in their own group, and nothing else is', () => {
  const group = GROUPS['fate-outside'];
  assert.deepEqual(Object.keys(group).sort(), ['ft_air', 'ft_door', 'ft_grass', 'ft_window']);
  for (const [id, entry] of Object.entries(group)) {
    assert.equal(typeof entry.mount, 'function', id);
    assert.equal(BY_ID[id].fate, 'outside', id);
  }
  assert.deepEqual(FATE_BY_ID.outside.cards, ['ft_window', 'ft_air', 'ft_grass', 'ft_door']);
});

test('A Window: the phase follows the real hour, round the clock', () => {
  assert.equal(phaseAt(0), 'night');
  assert.equal(phaseAt(3.5), 'night');
  assert.equal(phaseAt(5.5), 'dawn');
  assert.equal(phaseAt(7.4), 'dawn');
  assert.equal(phaseAt(12), 'day');
  assert.equal(phaseAt(17.4), 'day');
  assert.equal(phaseAt(18.5), 'dusk');
  assert.equal(phaseAt(20.4), 'dusk');
  assert.equal(phaseAt(22), 'night');
  assert.equal(phaseAt(24 + 12), 'day', 'hours wrap');
  assert.equal(phaseAt(-1), 'night');
  assert.equal(phaseAt(NaN), 'day', 'a bad hour falls back to noon');
});

test('A Window: noon is bright with the sun up and no stars; midnight is dark with the moon and stars', () => {
  const noon = skyAt(12.5), night = skyAt(0.5), dusk = skyAt(18.4);
  assert.ok(lum(noon.top) > lum(night.top) + 80, 'day sky is far brighter than night sky');
  assert.ok(noon.sun.up && !noon.moon.up);
  assert.ok(night.moon.up && !night.sun.up);
  assert.equal(noon.stars, 0);
  assert.equal(night.stars, 1);
  assert.ok(dusk.stars > 0 && dusk.stars < 1, 'stars come out through dusk');
  assert.ok(dusk.sun.up && dusk.sun.y > noon.sun.y, 'the evening sun sits lower than noon');
  for (const h of [0, 4.9, 6.1, 9, 13, 17.9, 19.1, 21, 23.99]) {
    const s = skyAt(h);
    for (const k of ['top', 'bottom', 'hill', 'cloud']) assert.match(s[k], /^#[0-9a-f]{6}$/, `${h} ${k}`);
    for (const b of [s.sun, s.moon]) if (b.up) { assert.ok(b.x >= 0 && b.x <= 1); assert.ok(b.y >= 0 && b.y <= 1); }
  }
});

test('A Window: the sky changes gradually, a minute never jumps', () => {
  for (let m = 0; m < 24 * 60; m += 1) {
    const a = skyAt(m / 60), b = skyAt((m + 1) / 60);
    assert.ok(Math.abs(lum(a.top) - lum(b.top)) < 6, `top at minute ${m}`);
    assert.ok(Math.abs(a.stars - b.stars) < 0.05, `stars at minute ${m}`);
  }
  assert.equal(mix('#000000', '#ffffff', 0.5), '#808080');
  assert.equal(hourOf(new Date(2026, 10, 10, 13, 30, 0)), 13.5);
});

test('Touch Grass: blades fit the width, fewer on a phone', () => {
  assert.equal(G.bladeCount(0), 0);
  assert.equal(G.bladeCount(390, true), 30);
  assert.equal(G.bladeCount(3000, true), G.LITE_MAX_BLADES);
  assert.equal(G.bladeCount(5000), G.MAX_BLADES);
  const blades = G.makeBlades(260, 50, seq([0.5]));
  assert.equal(blades.length, 20);
  for (const b of blades) { assert.ok(b.x >= 0 && b.x <= 260); assert.ok(b.h > 0 && b.h <= 50); }
});

test('Touch Grass: a blade pays once, then rests for the cooldown, then pays again', () => {
  const blades = G.makeBlades(130, 50, seq([0.5]));
  const x = blades[3].x;
  const first = G.brush(blades, x, 10, 8, 1000);
  assert.ok(first.includes(3), 'the blade under the pointer pays');
  assert.deepEqual(G.brush(blades, x, 10, 8, 1000 + G.COOLDOWN_MS - 1), [], 'still resting: nothing pays');
  assert.ok(blades[3].v !== 0, 'a resting blade still sways when brushed');
  assert.ok(G.brush(blades, x, 10, 8, 1000 + G.COOLDOWN_MS).includes(3), 'rested: pays again');
  /* a frantic back-and-forth for the whole cooldown pays each touched blade exactly once */
  const row = G.makeBlades(130, 50, seq([0.5]));
  let paid = 0;
  for (let t = 0; t < G.COOLDOWN_MS; t += 16) paid += G.brush(row, 40 + Math.sin(t / 50) * 30, 10, 12, t).length;
  assert.ok(paid > 0 && paid <= row.length, `paid ${paid} of ${row.length}`);
});

test('Touch Grass: only the blades the pointer reaches, and not above their tips', () => {
  const blades = G.makeBlades(260, 50, seq([0.5]));
  const b = blades[5];
  assert.ok(G.touched(blades, b.x, 5).includes(5));
  assert.ok(!G.touched(blades, b.x, b.h + 20).includes(5), 'above the tip is not grass');
  assert.ok(!G.touched(blades, b.x + G.REACH + 1, 5).includes(5));
  assert.equal(G.touchPay(1000), 1000 * G.PAY_SECONDS);
  assert.equal(G.touchPay(0), 1, 'never pays nothing');
});

test('Touch Grass: the springs settle, and a gust leans the blades its way only while it passes', () => {
  const blades = G.makeBlades(130, 50, seq([0.5]));
  blades[0].v = 3;
  let moving = 1;
  for (let i = 0; i < 400; i++) moving = G.step(blades, 1 / 60, 0);
  assert.ok(moving < 0.01, 'calm again');
  assert.ok(Math.abs(blades[0].a - blades[0].base) < 0.01);
  assert.equal(G.gustPush(100, 1000, 1, 0), 0, 'the front has not reached x yet');
  assert.ok(G.gustPush(100, 1000, 1, 240 + 800) > 0, 'leans with a rightward gust');
  assert.ok(G.gustPush(900, 1000, -1, 240 + 800) < 0, 'and the other way with a leftward one');
  assert.equal(G.gustPush(100, 1000, 1, 10000), 0, 'passed: upright again');
});

test('Fresh Air: the gust front reaches the near side first; gaps stay in their range', () => {
  assert.equal(gustDelay(0, 1000, 1), 0);
  assert.equal(gustDelay(1000, 1000, 1), BREEZE.crossMs);
  assert.equal(gustDelay(1000, 1000, -1), 0);
  assert.equal(gustDelay(-50, 1000, 1), 0, 'clamped');
  assert.equal(between(BREEZE.gapMs, 0), BREEZE.gapMs[0]);
  assert.equal(between(BREEZE.gapMs, 1), BREEZE.gapMs[1]);
  assert.ok(BREEZE.gapMs[0] >= 30000, 'a breeze is now and then, not constant');
});

test('Door Ajar: wider with every later purchase, and never past its cap', () => {
  assert.equal(buysAfter(['a', 'ft_door'], 'ft_door'), 0);
  assert.equal(buysAfter(['a', 'ft_door', 'b', 'c'], 'ft_door'), 2);
  assert.equal(buysAfter(['a'], 'ft_door'), 0);
  assert.equal(doorWidth(0), DOOR.base);
  let last = 0;
  for (let n = 0; n < 200; n++) {
    const w = doorWidth(n);
    assert.ok(w >= last, 'never narrows');
    assert.ok(w <= DOOR.cap);
    last = w;
  }
  assert.equal(doorWidth(500), DOOR.cap);
  assert.equal(doorWidth(500, true), DOOR.liteCap, 'a phone keeps a thinner door');
});
