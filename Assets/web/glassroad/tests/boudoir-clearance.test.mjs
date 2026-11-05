import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { boudoirBoxesIntersect, corridorClearance } from '../render/boudoirMedia.js';

// ccp-bugs #1543: the Boudoir plan's corridor check runs through a grid; it must answer exactly as the full scan.
test('corridorClearance answers exactly as testing every corridor', () => {
  let s = 7; const rnd = () => ((s = (s * 16807) % 2147483647) / 2147483647);
  const axes = () => { const a = rnd() * Math.PI * 2; return [[Math.cos(a), 0, -Math.sin(a)], [0, 1, 0], [Math.sin(a), 0, Math.cos(a)]]; };
  const box = (spread, big = 1) => ({ p: [rnd() * spread, rnd() * 40, rnd() * spread], size: [1 + rnd() * 20 * big, 1 + rnd() * 16, 1 + rnd() * 8 * big], axes: axes() });
  const corridors = Array.from({ length: 400 }, () => box(600));
  const clear = corridorClearance(corridors);
  let blocked = 0;
  for (let i = 0; i < 3000; i++) {
    const row = i % 50 === 0 ? { ...box(600, 60) } : i % 7 === 0 ? { ...box(600), axes: undefined } : box(600);
    const want = !corridors.some(c => boudoirBoxesIntersect(row, c));
    assert.equal(clear(row), want, 'row ' + i);
    if (!want) blocked++;
  }
  assert.ok(blocked > 100 && blocked < 2900, 'the sample exercises both answers (' + blocked + ')');
});

test('the Boudoir plan asks the grid, not a scan of every corridor', () => {
  const src = readFileSync(new URL('../render/boudoirMedia.js', import.meta.url), 'utf8');
  assert.match(src, /const clear = corridorClearance\(corridors\);/);
});
