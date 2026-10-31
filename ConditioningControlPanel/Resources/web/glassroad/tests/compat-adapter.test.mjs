import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { isCompatOnly, compatLimits, COMPAT_DEAD_ERRORS, noteCompatLoss, webgl2Died, DEAD_BACKEND_MS } from '../render/compatAdapter.js';
import { trackTableWidth, setTrackTableLimit } from '../render/spectrum.js';

const src = readFileSync(new URL('../render/renderer.js', import.meta.url), 'utf8');
const boot = readFileSync(new URL('../boot.js', import.meta.url), 'utf8');

test('only an adapter without core features is compat-only', () => {
  assert.equal(isCompatOnly({ features: new Set(['core-features-and-limits']) }), false);
  assert.equal(isCompatOnly({ features: new Set() }), true);
  assert.equal(isCompatOnly(null), false);
});

test('a compat device asks by name for the 8192 texel tables, and only for limits the adapter names', () => {
  const limits = compatLimits({ limits: { maxTextureDimension2D: 8192, maxColorAttachments: 8, minUniformBufferOffsetAlignment: 256 } });
  assert.deepEqual(limits, { maxTextureDimension2D: 8192, maxColorAttachments: 8 });
  assert.ok(limits.maxTextureDimension2D >= trackTableWidth(1e6), 'the widest course table fits');
  assert.deepEqual(compatLimits(null), {});
});

test('renderer: WebGL2 goes first on a compat adapter, WebGPU keeps the raised limits, a refusal says so', () => {
  assert.match(src, /const compat = !webgl2 && isCompatOnly\(adapter\)/);
  assert.match(src, /if \(context\) \{ webgl2 = true; gpu = ''; \}/);
  assert.match(src, /compat && !webgl2 \? \{ requiredLimits: compatLimits\(adapter\) \} : \{\}/);
  assert.match(src, /if \(!context\) throw new Error\(\(force === 'webgl2' \|\| firefox \? 'the browser refused a WebGL2 context on this device'/);
});

test('a compat device that keeps refusing frames ends on the failure card; a core device is only counted', () => {
  assert.ok(COMPAT_DEAD_ERRORS > 1, 'one stray error is not a dead world');
  assert.match(src, /three\.backend\.compatibilityMode && gpuErrors\.count === COMPAT_DEAD_ERRORS/);
  assert.match(boot, /renderer\.onCompatDead\(\(message\) => \{\s*noteCompatLoss\('webgpu'\);\s*if \(isCurrent\(\)\) fail\(/);
});

// DrankoCZ, moto g54 5G, 2026-10-31: the WebGL2 cube spins, the game's WebGL2 does not get through. The one rebuild takes the other route.
const memory = () => { const m = new Map(); return { getItem: (k) => (m.has(k) ? m.get(k) : null), setItem: (k, v) => m.set(k, v), removeItem: (k) => m.delete(k) }; };

test('a compat phone whose WebGL2 died stays off it for a day; WebGPU dying too clears that', () => {
  const s = memory();
  assert.equal(webgl2Died(1000, s), false);
  noteCompatLoss('webgl2', 1000, s);
  assert.equal(webgl2Died(1000 + DEAD_BACKEND_MS - 1, s), true);
  assert.equal(webgl2Died(1000 + DEAD_BACKEND_MS, s), false);
  noteCompatLoss('webgpu', 2000, s);
  assert.equal(webgl2Died(2001, s), false, 'neither route is stuck');
  s.setItem('glassroad.deadBackend', 'not json');
  assert.equal(webgl2Died(2001, s), false);
  assert.equal(webgl2Died(2001, null), false);
});

test('renderer and boot: only a compat adapter switches, and a hidden page never counts as the route failing', () => {
  assert.match(src, /if \(compat && force !== 'webgpu' && !webgl2Died\(\)\)/);
  assert.match(src, /compatOnly: compat,/);
  assert.match(boot, /if \(renderer\.compatOnly && !document\.hidden\) noteCompatLoss\(renderer\.backend\);/);
  assert.match(boot, /renderer\.onCompatDead\(\(message\) => \{\s*noteCompatLoss\('webgpu'\);/);
});

test('a refused WebGL2 gets a fresh canvas and the defaults, and the card carries the browser reason', () => {
  assert.match(src, /const fresh = replaceCanvas\(c\), again = askGl\(fresh\);/);
  assert.match(src, /webglcontextcreationerror/);
  assert.match(src, /\(glRefusal \? ' \(' \+ glRefusal \+ '\)' : ''\)/);
});

test('the compat WebGPU route never multisamples: RGBA16Float refuses it there, whatever the tier', () => {
  const post = readFileSync(new URL('../render/post.js', import.meta.url), 'utf8');
  assert.match(post, /samples: Q\.antialias && !\w+\.backend\?\.compatibilityMode \? 4 : 0/);
});

test('the course tables follow the GPU limit, and a bad reading falls back to the default', () => {
  try {
    setTrackTableLimit(4096);
    assert.equal(trackTableWidth(8373), 4096);
    assert.equal(trackTableWidth(1200), 1200);
    setTrackTableLimit(16384);
    assert.equal(trackTableWidth(1e6), 8192, 'never past the WebGPU default');
    setTrackTableLimit(undefined);
    assert.equal(trackTableWidth(1e6), 8192);
  } finally { setTrackTableLimit(8192); }
});
