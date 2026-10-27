import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

// render/roadWords.js: a marking whose footprint did not move is not re-sampled every frame just because the
// course has water; only a strip with a vertex on the liquid follows the moving surface (perf, Firefox).
test('a dry road-word strip with an unchanged footprint is not rewritten each frame', () => {
  const src = readFileSync(new URL('../render/roadWords.js', import.meta.url), 'utf8');
  assert.match(src, /const same = slotS\[slot\] === view\.s && slotW\[slot\] === wM && slotFoot\[slot\] === lane;/);
  assert.match(src, /if \(same && !force && !\(waterMoves && slotWet\[slot\]\)\) \{ slotLane\[slot\] = laneX; slotScale\[slot\] = scale; return false; \}/);
  assert.match(src, /field\.sample\(s, x, wetOut\)\.wet\) slotWet\[slot\] = 1;/, 'wetness is found while the strip is sampled');
  assert.match(src, /if \(writeStrip\(slot, v, lane, pose\.scale, false, !!waterVolume && !paused\)\) geoDirty = true;/);
  assert.match(src, /slotW\[slot\] = NaN; slotFoot\[slot\] = NaN; slotWet\[slot\] = 0;/, 'a freed slot forgets its footprint');
});
