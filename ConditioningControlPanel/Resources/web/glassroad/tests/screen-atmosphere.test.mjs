import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { ATMOSPHERE as A, fogFill, tunnelRadius, createAtmosphereClock } from '../render/screen-atmosphere.js';

test('fog takes seconds to close over the road, fades with the effect and stays bounded', () => {
  assert.equal(fogFill(1, 0), 0);
  assert.ok(fogFill(1, 0.25) < 0.02, 'not an immediate uniform wash at the hit');
  assert.equal(fogFill(1, A.fogFillS / 2), 0.5);
  assert.equal(fogFill(1, A.fogFillS), 1);
  assert.equal(fogFill(0.3, 10), 0.3, 'old fog follows OUT envelope');
  assert.equal(fogFill(1, -10), 0);
  assert.equal(fogFill(5, 100), 1);
});

test('fog clock holds exactly on pause, resets after clearing and preserves age while replaced', () => {
  const clock = createAtmosphereClock(), pink = { kind: 'pink', params: { seed: 1 } };
  const mid = clock.update(pink, 1, A.fogFillS / 2);
  for (let i = 0; i < 500; i++) assert.equal(clock.update(pink, 1, 0), mid);
  const replaced = { kind: 'melt', params: { seed: 2 } };
  assert.equal(clock.update(replaced, 0.5, 0), mid * 0.5);
  assert.equal(clock.update(replaced, 0, 0), 0);
  assert.equal(clock.update(pink, 1, 0), 0, 'restart has no lingering mist');
  clock.update(pink, 1, 10);
  assert.equal(clock.update({ kind: 'pink', params: { seed: 3 } }, 1, 0), 0, 'new hit rolls in anew');
});

test('Gentle fog builds at half speed and half density', () => {
  const screen = { kind: 'pink', params: { seed: 1 } };
  const normal = createAtmosphereClock().update(screen, 1, 1.8, 1);
  const gentle = createAtmosphereClock().update(screen, 0.5, 1.8, 0.5);
  assert.ok(gentle < normal / 2);
});

test('the default three-second Pink reaches full fog before its hold ends', () => {
  const clock = createAtmosphereClock(), s = { kind: 'pink', durS: 3, params: { seed: 1 } };
  const early = clock.update(s, 1, 0.3);
  assert.ok(early > 0 && early < 0.1);
  assert.equal(clock.update(s, 1, 1.8), 1, 'fully covered for the final 0.9 seconds');
  assert.equal(clock.update(s, 0.5, 0.9), 0.5, 'OUT restores visibility');
});

test('blackout is a small circular tunnel, keeping a dim surround and a softer Gentle aperture', () => {
  assert.equal(tunnelRadius(0), 1.3);
  assert.ok(Math.abs(tunnelRadius(1) - 0.19) < 1e-9);
  assert.ok(tunnelRadius(0.5) > 0.7);
  for (const aspect of [16 / 9, 4 / 3, 21 / 9]) {
    const openShare = Math.PI * tunnelRadius(1) ** 2 / aspect;
    assert.ok(openShare < 0.09, 'most of the landscape view is dark');
  }
  assert.ok(A.tunnelFloor > 0 && A.tunnelFloor < 0.05);
});

test('reset removes bloom suppression even when the shader uniforms retain the last tunnel', () => {
  const src = readFileSync(new URL('../render/screenfx.js', import.meta.url), 'utf8');
  const gate = src.slice(src.indexOf('function bloomGate()'), src.indexOf('function node('));
  assert.match(gate, /mix\(float\(1\),[^;]+u\.any\)/);
  assert.ok(src.indexOf('u.any.value = any ? 1 : 0;') < src.indexOf('if (!any) { clock = 0; return; }'));
});
