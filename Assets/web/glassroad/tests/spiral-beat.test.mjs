import test from 'node:test';
import assert from 'node:assert/strict';
import { createSpiralBeatClock } from '../render/spiralBeat.js';
import { loomVariation } from '../render/loomVariety.js';
import { bandHz, screenRateMul, gateParams } from '../render/spiralMath.js';
import { LOOM_PROGRAMS, PROGRAMS, loomParams, spinOf } from '../render/mediaPlaceholders.js';
import { LOOM_STYLES } from '../../arcademy/engine/loom/loomField.js';
import { registerHooks } from 'node:module';

const song = { bpm: 96.75, downbeatS: .22, beats: [] };
const frame = { song, songS: .22, audioReactive: { active: true, bass: 1, mid: .3 } };

test('spiral motion answers the heard downbeat, then eases to idle when muted or silent', () => {
  const beat = createSpiralBeatClock();
  assert.equal(beat.update(.1, { song, songS: 90 }), 1);
  const hit = beat.update(.05, frame);
  assert.ok(hit > 1.5 && hit < 2);
  assert.equal(beat.update(.1, { ...frame, paused: true }), hit);
  assert.equal(beat.update(.1, { ...frame, audioReactive: { frozen: true } }), hit);
  const out = beat.update(.05, { ...frame, audioReactive: { active: false } });
  assert.ok(out > 1 && out < hit, 'muting decays speed without resetting the visible phase');
  for (let i = 0; i < 100; i++) beat.update(.1);
  assert.ok(beat.update(.1) < 1.00001);
});

test('Gentle answers smoothly and every authored Loom remains inside its band rate cap at maximum beat speed', () => {
  const beat = createSpiralBeatClock();
  assert.ok(beat.update(.1, frame, true) < 1.06);
  for (let i = 0; i < 6; i++) {
    const p = loomVariation(i);
    assert.ok(bandHz(p) * screenRateMul(p, { speed: 2 }) <= 2.5 + 1e-9);
    assert.ok(bandHz(p) * screenRateMul(p, { speed: 2, gentle: true }) <= 1.25 + 1e-9);
  }
});

test('world, pad and HUD library uses all actual Loom silhouettes and removes every centerpiece', () => {
  const styles = LOOM_PROGRAMS.map(i => loomParams(PROGRAMS[i].id, []));
  assert.deepEqual(new Set(styles.map(p => p.layer.style)), new Set(LOOM_STYLES));
  assert.ok(new Set(styles.map(p => p.layer.arms)).size >= 5);
  for (const p of styles) assert.equal(p.centerpiece.kind, 'none');
  for (const i of LOOM_PROGRAMS) assert.ok(Math.abs(spinOf(i)) * 2 * PROGRAMS[i].arms / (Math.PI * 2) < 2.5);
  assert.equal(gateParams({ loom: { centerpiece: { kind: 'star' } } }).centerpiece.kind, 'none');
  assert.deepEqual(loomParams('loom-style-0', [[1,0,0],[0,0,1],[1,1,0]]).layer.colors, ['#ff0000','#0000ff','#ffff00']);
});

test('gear paint shares a bounded real Loom atlas and only decorates an existing cap material', async t => {
  const hooks = registerHooks({ resolve(specifier, context, next) {
    const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
    return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
  } });
  t.after(() => hooks.deregister());
  const previous = globalThis.OffscreenCanvas;
  const ctx = new Proxy({}, { get: (o, k) => k in o ? o[k] : /Gradient/.test(k) ? () => ({ addColorStop() {} }) : () => {} });
  globalThis.OffscreenCanvas = class { constructor(w,h) { this.width=w; this.height=h; } getContext(kind) { return kind === '2d' ? ctx : null; } };
  t.after(() => { globalThis.OffscreenCanvas = previous; });
  const { MeshBasicNodeMaterial } = await import('three/webgpu');
  const { createGearSpiralPaint } = await import('../render/gearSpirals.js');
  const { acquireLoomAtlas } = await import('../render/loomAtlas.js');
  const mat = new MeshBasicNodeMaterial({ color: '#ff60c0' }), original = mat.color.clone();
  const paint = createGearSpiralPaint(mat, 15.5, 2), a = acquireLoomAtlas(), b = acquireLoomAtlas();
  assert.equal(a.texture, b.texture); assert.equal(a.texture.image.width, 768); assert.equal(a.texture.image.height, 512);
  assert.ok(mat.colorNode); assert.deepEqual(mat.color, original);
  paint.dispose(); a.dispose(); b.dispose(); mat.dispose();
});
