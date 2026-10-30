// polish 6 PERF2: the living icon atlas only animates what shows (render/items/fxmath.js iconFps / iconLive),
// and the hot pure helpers it touched keep their results while allocating less (audio mixGet, engineLayers out,
// sim/steer.js yawParams / bendSpeed, track/query.js scratch).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { iconFps, iconLive, ICON_FPS_GL2, ICON_LIVE_FRAC } from '../render/items/fxmath.js';
import { createFeel } from '../core/feel.js';
import { mixGet, MIX } from '../audio/mix.js';
import { engineLayers } from '../audio/sfxmap.js';
import { yawParams, bendSpeed } from '../sim/steer.js';

const feel = createFeel(JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8')));

test('icon fps: webgl2 runs at most 15, other backends keep the tier rate', () => {
  assert.equal(ICON_FPS_GL2, 15);
  assert.equal(iconFps(30, 'webgl2'), 15);
  assert.equal(iconFps(10, 'webgl2'), 10);
  assert.equal(iconFps(30, 'webgpu'), 30);
  assert.equal(iconFps(30, ''), 30);
});

test('icon live: near and in front animates; far, tiny or behind does not; no camera numbers = live', () => {
  assert.equal(iconLive(0.5, 6, 1, 60), true);
  assert.equal(iconLive(0.5, 400, 1, 60), false, 'a few pixels tall at 400 m');
  assert.equal(iconLive(0.5, 6, -0.9, 60), false, 'behind the camera');
  assert.equal(iconLive(2, 1, -0.9, 60), true, 'the camera inside the bubble');
  assert.equal(iconLive(0.5, 0, 1, 60), true);
  assert.equal(iconLive(0.5, 6, 1, 0), true);
  // the threshold sits at ICON_LIVE_FRAC of the half view height
  const d = 0.5 / (ICON_LIVE_FRAC * Math.tan(Math.PI / 6));
  assert.equal(iconLive(0.5, d * 0.99, 1, 60), true);
  assert.equal(iconLive(0.5, d * 1.01, 1, 60), false);
});

test('mixGet: feel first, MIX for a key feel lacks, MIX without a feel; same answers on repeat', () => {
  for (const k of Object.keys(MIX).slice(0, 12)) {
    const a = mixGet(feel, k), b = mixGet(feel, k);
    assert.equal(a, b);
    assert.equal(mixGet(null, k), MIX[k]);
  }
});

test('engineLayers out: fills the record in place with the same numbers as a fresh call', () => {
  const out = { idle: -1, cruise: -1, air: -1 };
  for (const p of [0, 0.2, 0.5, 0.8, 1, 1.2]) for (const boosting of [false, true]) {
    const fresh = engineLayers(p, { boosting }, feel);
    const r = engineLayers(p, { boosting }, feel, out);
    assert.equal(r, out);
    assert.deepEqual({ ...out }, fresh);
  }
});

test('bendSpeed: a feel or its yawParams record give the same speed', () => {
  const rec = yawParams(feel, {});
  for (const k of [0.5, 1, 2, 4, 8]) for (const share of [0.6, 1]) assert.equal(bendSpeed(k, feel, share), bendSpeed(k, rec, share));
});
