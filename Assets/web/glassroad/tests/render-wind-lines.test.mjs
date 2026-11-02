// tests/render-wind-lines.test.mjs - playtest 2026-10-28 (lane drift): wind lines in the speed field.
// None below WIND_LOOK.fromPct of top speed, more with speed, more under boost; Less halves, Off and Gentle remove
// them; they are born at the screen edges; the frame cost stays uniforms only (no attribute upload).
import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
const hooks = registerHooks({ resolve(s, c, next) {
  const files = { 'three': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js', 'three/webgpu': 'three.webgpu.min.js' };
  if (s.startsWith('three/addons/')) return { url: new URL('../vendor/' + s, import.meta.url).href, shortCircuit: true };
  return files[s] ? { url: new URL('../vendor/three/build/' + files[s], import.meta.url).href, shortCircuit: true } : next(s, c);
} });
const { createSpeedLines, windAmount, windBirth, WIND_LOOK: W } = await import('../render/speedlines.js');
const { createState, S, BOOST } = await import('../sim/state.js');
hooks.deregister();

test('wind: none below 70 percent of top speed, whatever the boost', () => {
  for (const pct of [0, 0.3, 0.5, 0.69]) for (const b of [0, 1]) assert.equal(windAmount(pct, b), 0, pct + ' ' + b);
  assert.ok(W.fromPct >= 0.65 && W.fromPct <= 0.75);
});

test('wind: climbs with speed above the threshold and again under boost, capped at 1', () => {
  let last = -1;
  for (const pct of [0.7, 0.8, 0.9, 1, 1.1, 1.3]) { const v = windAmount(pct, 0); assert.ok(v >= last, 'rises at ' + pct); last = v; }
  assert.ok(windAmount(1, 0) > 0.2, 'top speed shows lines');
  assert.ok(windAmount(1, 1) > windAmount(1, 0) + 0.2, 'boost is clearly stronger');
  assert.ok(windAmount(1.3, 1) <= 1);
});

test('wind: motion Less halves it, Off and Gentle remove it', () => {
  const full = windAmount(1, 0.5);
  assert.ok(Math.abs(windAmount(1, 0.5, { motion: 'less' }) - full * 0.5) < 1e-9);
  assert.equal(windAmount(1, 1, { motion: 'off' }), 0);
  assert.equal(windAmount(1.3, 1, { gentle: true }), 0);
});

test('wind: born deep at the screen edges, outside the centre', () => {
  for (let i = 0; i < 200; i++) {
    const e = windBirth(Math.random(), Math.random(), Math.random(), i & 1 ? 1 : -1);
    assert.ok(Math.abs(e.nx) >= 0.5 && Math.abs(e.nx) <= 1, 'edge ' + e.nx);
    assert.equal(Math.sign(e.nx), i & 1 ? 1 : -1);
    assert.ok(e.depth >= W.nearM && e.depth <= W.farM && W.nearM >= 12, 'deep, never near the lens');
  }
});

test('wind: the field drives them through uniforms only, in the same single mesh', () => {
  const lines = createSpeedLines(), st = createState();
  const geo = lines.object.geometry, versions = () => Object.values(geo.attributes).map((a) => a.version).join();
  const before = versions();
  st[S.speedPct] = 0.3;
  lines.update(0.1, st, { speedLines: 0 }, null, 1);
  assert.equal(lines.object.visible, false, 'slow and no boost: nothing at all');
  st[S.speedPct] = 1.1; st[S.boostKind] = BOOST.DRIFT3;
  for (let i = 0; i < 20; i++) lines.update(0.05, st, { speedLines: 1 }, null, 1);
  assert.equal(lines.object.visible, true);
  lines.setMotion('off');
  st[S.speedPct] = 0.75; st[S.boostKind] = 0;
  for (let i = 0; i < 40; i++) lines.update(0.05, st, { speedLines: 0 }, null, 1);
  assert.equal(versions(), before, 'no attribute was re-uploaded');
  lines.dispose();
});
