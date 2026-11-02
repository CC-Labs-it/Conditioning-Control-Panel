import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createFeel } from '../core/feel.js';

test('feel.get caches resolved paths; set and reset still show through', () => {
  const f = createFeel({ a: { b: 1, c: [10, 20] } });
  assert.equal(f.get('a.b'), 1);
  assert.equal(f.get('a.b'), 1);
  assert.equal(f.set('a.b', 5), true);
  assert.equal(f.get('a.b'), 5);
  f.reset();
  assert.equal(f.get('a.b'), 1);
  assert.deepEqual(f.get('a.c'), [10, 20]);
});

test('feel.get keeps throwing for a missing key, the same message every time', () => {
  const f = createFeel({ a: { b: 1 } });
  assert.throws(() => f.get('a.nope'), /missing key a.nope/);
  assert.throws(() => f.get('a.nope'), /missing key a.nope/);
  assert.throws(() => f.get('x.y.z'), /missing key x.y.z/);
});
