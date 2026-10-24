import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as loom from '../shared/loomField.js';
import * as vendored from '../../arcademy/engine/loom/loomField.js';

test('canonical and vendored Loom expose the renderer API needed by Racing Thoughts', () => {
  for (const key of ['LOOM_GOLDEN_K', 'loomSpanRad', 'loomLayerRotationRad', 'loomHueRows',
    'loomStyleT', 'loomStyleR', 'loomHelix', 'loomBandAt', 'loomPulseZoom', 'loomWobble', 'loomHueRad']) {
    assert.ok(key in loom, key);
    assert.ok(key in vendored, key);
  }
  const source = readFileSync(new URL('../shared/loomField.js', import.meta.url), 'utf8');
  const copy = readFileSync(new URL('../../arcademy/engine/loom/loomField.js', import.meta.url), 'utf8');
  const bandApi = text => text.slice(text.indexOf('// ---- band maths for other renderers'), text.indexOf('// ---- color helpers'));
  assert.equal(bandApi(copy), bandApi(source));
});

test('renderer helpers preserve radial mappings and neutral sidecar behavior', () => {
  const q = loom.normalizeParams2({ schema: 1, arms: 6, style: 'arch', speed: 2, colors: ['#ff0000'], bg: '#102030' });
  assert.deepEqual([q.layer.arms, q.layer.style, q.speed, q.bg.color], [6, 'arch', 2, '#102030']);
  assert.equal(loom.loomPulseZoom(q, .25), 1);
  assert.equal(loom.loomWobble(q, .5, .25), 0);
  assert.equal(loom.loomHueRad(q, .25), 0);
  for (const style of loom.LOOM_STYLES) {
    for (const radius of [0, .1, .5, .9, 1]) {
      assert.ok(Math.abs(loom.loomStyleR(loom.loomStyleT(radius, style), style) - radius) < 1e-6, style);
    }
  }
});
