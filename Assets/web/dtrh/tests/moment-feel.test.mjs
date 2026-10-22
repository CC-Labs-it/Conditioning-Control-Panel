import test from 'node:test';
import assert from 'node:assert/strict';
import {
  BLOOM_MAX, BLOOM_BASE, RECAP_ROWS_DELAY_MS, RECAP_ROW_STAGGER_MS,
  easeOutCubic, countValue, bloomPeak, rowDelay, countDetail,
} from '../game/momentFeel.js';

test('count-up: starts at zero, never overshoots, lands exactly on the real value', () => {
  for (const total of [1, 7, 133, 4821, 1000000]) {
    assert.equal(countValue(total, 0), 0);
    let prev = 0;
    for (let i = 0; i <= 40; i++) {
      const v = countValue(total, i / 40);
      assert.ok(v >= prev && v <= total);
      prev = v;
    }
    assert.equal(countValue(total, 1), total);
    assert.equal(countValue(total, 1.4), total);
  }
  assert.equal(countValue(undefined, 0.5), 0);
});

test('ease-out: clamped, monotonic, fast first', () => {
  assert.equal(easeOutCubic(-1), 0);
  assert.equal(easeOutCubic(2), 1);
  assert.ok(easeOutCubic(0.5) > 0.5);
});

test('edge bloom: only streak moments bloom, deeper streaks bloom more, never past the ceiling', () => {
  assert.equal(bloomPeak('benign-popped', {}), 0);
  assert.equal(bloomPeak('detonated', { combo: 40 }), 0);
  assert.equal(bloomPeak('combo-milestone', { combo: 10 }), BLOOM_BASE);
  assert.ok(bloomPeak('combo-milestone', { combo: 40 }) > bloomPeak('combo-milestone', { combo: 20 }));
  assert.ok(bloomPeak('combo-big', { threshold: 100 }) > bloomPeak('combo-big', { threshold: 25 }));
  assert.ok(bloomPeak('combo-big', { threshold: 25 }) > bloomPeak('combo-milestone', { combo: 20 }));
  for (const c of [10, 100, 1000, 100000]) assert.ok(bloomPeak('combo-milestone', { combo: c }) <= BLOOM_MAX);
  assert.ok(bloomPeak('combo-big', { threshold: 9999 }) <= BLOOM_MAX);
});

test('recap rows: one after another from the base delay', () => {
  assert.equal(rowDelay(0), RECAP_ROWS_DELAY_MS);
  assert.equal(rowDelay(3), RECAP_ROWS_DELAY_MS + 3 * RECAP_ROW_STAGGER_MS);
  assert.equal(rowDelay(2, 0), 2 * RECAP_ROW_STAGGER_MS);
  assert.equal(rowDelay(-4, -10), 0);
});

test('countdown beats map to a count detail', () => {
  assert.deepEqual(countDetail('3'), { kind: 'count', n: 3, go: false });
  assert.deepEqual(countDetail('1'), { kind: 'count', n: 1, go: false });
  assert.deepEqual(countDetail('GO!'), { kind: 'count', n: 0, go: true });
});
