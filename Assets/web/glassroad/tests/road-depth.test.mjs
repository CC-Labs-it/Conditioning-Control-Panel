import test from 'node:test';
import assert from 'node:assert/strict';
import { createRoadDepth } from '../track/roadDepth.js';

const frame = (s, y = 0) => ({ p: [0, y, s], b: [1, 0, 0], n: [0, 1, 0], t: [0, 0, 1], w: 20 });
test('road slabs gain depth without changing their support frames', () => {
  const track = { lengthM: 100, frameAt: s => frame(s) };
  const before = track.frameAt(40), depth = createRoadDepth(track);
  assert.equal(depth(40), 1.1);
  assert.equal(depth(140), depth(40));
  assert.deepEqual(track.frameAt(40), before);
  assert.equal(createRoadDepth({ ...track, id: 'goonworks' })(40), 1.8);
});
test('road slab depth yields to a lower fork with limited clearance', () => {
  const track = { lengthM: 100, frameAt: s => frame(s, 5.2),
    forks: [{ id: 'lower', lengthM: 100, frameAt: s => frame(s) }] };
  const depth = createRoadDepth(track)(40);
  assert.ok(depth >= .25 && depth <= .71, String(depth));
});
