import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { isCompatOnly, compatLimits, COMPAT_DEAD_ERRORS } from '../render/compatAdapter.js';
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
  assert.match(boot, /renderer\.onCompatDead\(\(message\) => \{\s*if \(isCurrent\(\)\) fail\(/);
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
