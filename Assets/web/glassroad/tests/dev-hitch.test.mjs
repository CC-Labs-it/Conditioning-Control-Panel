import { test } from 'node:test';
import assert from 'node:assert/strict';
import { pct, summarize, spikesOf, topSection, attribute } from '../dev/hitchMath.mjs';

const row = (gap, extra = {}) => ({ at: 0, gap, work: 2, phase: 'racing', lap: 1, s: 10, song: null, tier: 'high', scale: 1, heap: 0,
  gl: { link: 0, linkN: 0, up: 0, upN: 0, upBytes: 0, draw: 0, drawN: 0, big: [] }, h: null, ...extra });

test('pct is nearest rank and 0 when empty', () => {
  assert.equal(pct([], 0.5), 0);
  assert.equal(pct([5, 1, 3], 0.5), 3);
  assert.equal(pct([1, 2, 3, 4], 0.99), 4);
});

test('summarize counts frames over 25 and 50 ms', () => {
  const s = summarize([row(16), row(17), row(30), row(80)]);
  assert.equal(s.frames, 4);
  assert.equal(s.over25, 2);
  assert.equal(s.over50, 1);
  assert.equal(s.max, 80);
});

test('spikesOf keeps the row before for deltas', () => {
  const rows = [row(16, { at: 100 }), row(40, { at: 140 }), row(16, { at: 156 })];
  const sp = spikesOf(rows, 25);
  assert.equal(sp.length, 1);
  assert.equal(sp[0].prev, rows[0]);
  assert.equal(sp[0].t0, 100);
});

test('topSection picks the biggest timer', () => {
  assert.equal(topSection(null), null);
  assert.deepEqual(topSection({ sim: 1, fx: 0, audio: 0, cam: 0, world: 9, render: 3, hud: 0 }), ['world', 9]);
});

test('attribute names a tier step, a link, an upload, a gc, a JS section, else outside', () => {
  const prev = row(16);
  assert.equal(attribute({ row: row(60, { tier: 'mid' }), prev, t0: 0 }).cause, 'tier-step');
  assert.equal(attribute({ row: row(60, { gl: { link: 40, linkN: 3, up: 0, draw: 0, big: [] } }), prev, t0: 0 }).cause, 'link');
  assert.equal(attribute({ row: row(60, { gl: { link: 0, linkN: 0, up: 12, upN: 1, draw: 0, big: [] } }), prev, t0: 0 }).cause, 'upload');
  assert.equal(attribute({ row: row(60, { heap: 10 * 1048576 }), prev: row(16, { heap: 20 * 1048576 }), t0: 0 }).cause, 'gc');
  assert.equal(attribute({ row: row(60, { work: 50, h: { sim: 1, fx: 1, audio: 1, cam: 1, world: 40, render: 5, hud: 1, steps: 1 } }), prev, t0: 0 }).cause, 'js-world');
  const out = attribute({ row: row(60), prev, t0: 0 });
  assert.equal(out.cause, 'outside');
  assert.match(out.line, /60ms/);
});
