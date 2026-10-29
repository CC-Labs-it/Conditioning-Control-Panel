import test from 'node:test';
import assert from 'node:assert/strict';
import { spatialCandidates } from '../render/spatialCandidates.js';

test('spatial candidates exactly match ordered brute-force spheres, including large bodies', () => {
  let seed = 125;
  const random = () => ((seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0) / 4294967296);
  const rows = Array.from({ length: 1800 }, (_, id) => ({ id, p: [random() * 4000 - 2000, random() * 800 - 400, random() * 4000 - 2000], radius: random() * 70 }));
  rows.push({ id: 'large', p: [10, -100, 30], radius: 750 });
  const query = spatialCandidates(rows);
  for (let i = 0; i < 180; i++) {
    const p = [random() * 4000 - 2000, random() * 800 - 400, random() * 4000 - 2000], radius = i === 179 ? 5000 : random() * 180;
    const expected = rows.filter(row => Math.hypot(...row.p.map((v, j) => v - p[j])) <= row.radius + radius);
    assert.deepEqual(query(p, radius), expected);
  }
});

test('cell edges, touching spheres and repeated queries never omit or duplicate a body', () => {
  const rows = [-128, -64, 0, 64, 128].map(x => ({ p: [x, 0, 0], radius: 32 }));
  const query = spatialCandidates(rows);
  for (let n = 0; n < 3; n++) {
    assert.deepEqual(query([0, 0, 0], 32), rows.slice(1, 4));
    assert.deepEqual(query([128, 0, 0], 0), [rows[4]]);
    assert.deepEqual(query([1000, 0, 0], 1), []);
  }
  assert.deepEqual(spatialCandidates([])([0, 0, 0], 5), []);
});
