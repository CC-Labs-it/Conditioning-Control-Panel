import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { MUSIC_TILES as T, TILE_COLOURS, musicTileLayout, tileDistance, musicTileMask, musicTileColour, musicTileJoint } from '../render/musicTilesMath.js';

const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8')));
const layout = musicTileLayout(track);
const close = (a, b, eps = 1e-9) => assert.ok(Math.abs(a - b) < eps, `${a} != ${b}`);
const luminance = (c) => c[0] * 0.2126 + c[1] * 0.7152 + c[2] * 0.0722;

test('P6 panes cover the terrace approach and finish before the first bend', () => {
  assert.ok(layout);
  assert.equal(layout.fromM, -48);
  assert.equal(layout.toM, 84);
  assert.equal(musicTileMask(layout, 32, 4.5, 8), 1);
  assert.equal(musicTileMask(layout, 46, 4.5, 8), 1);
  for (const s of [90, 110, 500, 1000, track.lengthM - 60]) assert.equal(musicTileMask(layout, s, 3, 8), 0);
  for (let s = -48; s <= 84; s += 2) {
    const f = track.frameAt(s);
    assert.ok(f.t[0] > 0.993 && Math.abs(f.p[1]) < 0.8, `not on the straight at ${s}`);
    assert.ok(!track.nodeFlags(f.seg).includes('open'));
  }
});

test('start-relative metres and coverage are continuous at the lap seam and independent of spawn offsets', () => {
  for (const d of [-40, -0.00001, 0, 0.00001, 40]) {
    close(tileDistance(d, 0, track.lengthM), d);
    close(tileDistance(d + track.lengthM, 0, track.lengthM), d);
    close(musicTileMask(layout, d, 4.5, 8), musicTileMask(layout, d + track.lengthM, 4.5, 8));
    const shifted = { ...layout, startS: 250 };
    close(musicTileMask(shifted, d + 250, 4.5, 8), musicTileMask(layout, d, 4.5, 8));
  }
});

test('the entire boost pad and outer shoulder retain their original road treatment', () => {
  const pad = track.boosts[0];
  for (const ds of [-pad.len / 2, 0, pad.len / 2]) for (const x of [-pad.w / 2, 0, pad.w / 2]) {
    assert.equal(musicTileMask(layout, pad.s + ds, pad.x + x, 8), 0);
  }
  for (const x of [-8, -7.5, -6.2, 6.2, 7.5, 8]) assert.equal(musicTileMask(layout, 32, x, 8), 0);
  assert.ok(musicTileMask(layout, 32, 6, 8) > 0 && musicTileMask(layout, 32, 6, 8) < 1);
});

test('layout stops early if a future course puts a turn or opening on the approach', () => {
  const synthetic = (opening) => ({ lengthM: 500, nodeCount: 2, nodeS: [100, 120], boosts: [],
    nodeFlags: (i) => i === 0 ? ['start'] : opening ? ['open'] : ['rail'],
    frameAt: (s) => ({ seg: s >= 120 ? 1 : 0, p: [s, 0, 0], b: [0, 0, 1], n: [0, 1, 0],
      t: !opening && s >= 138 ? [0.9, 0, 0.435] : [1, 0, 0], w: 16 }) });
  assert.equal(musicTileLayout(synthetic(false)).toM, 36);
  assert.equal(musicTileLayout(synthetic(true)).toM, 18);
  assert.equal(musicTileLayout(null), null);
  assert.equal(musicTileLayout({ ...synthetic(false), nodeFlags: () => [] }), null);
});

test('all panes keep the same brightness as beat and bar colours change', () => {
  for (const c of TILE_COLOURS) close(luminance(c), T.luminance);
  for (let beat = 0; beat < 32; beat += 0.19) for (const gentle of [false, true]) {
    const c = musicTileColour(beat, beat / 4, true, gentle, -2, 7);
    close(luminance(c), T.luminance);
    assert.ok(c.every((v) => v >= 0 && v <= 1));
  }
});

test('no-song panes are static; Gentle ignores fast beat changes but retains slow bar colour', () => {
  assert.deepEqual(musicTileColour(0, 0, false, false), musicTileColour(82.5, 29.2, false, false));
  assert.deepEqual(musicTileColour(0, 1, true, true), musicTileColour(0.5, 1, true, true));
  assert.notDeepEqual(musicTileColour(0, 0, true, true), musicTileColour(0, 2, true, true));
  assert.notDeepEqual(musicTileColour(0, 1, true, false), musicTileColour(0.5, 1, true, false));
  const before = musicTileColour(3.99999, 0.9999975, true, false), after = musicTileColour(4.00001, 1.0000025, true, false);
  before.forEach((v, i) => close(v, after[i], 1e-4));
});

test('thin joints conserve contrast at distance and cross-road joints fade under the kart', () => {
  const crisp = musicTileJoint(1.5, 0, 0.001, 0.001, 15);
  const subpixel = musicTileJoint(1.5, 0, 0.45, 0.45, 15);
  assert.ok(crisp > 0.99 && subpixel < 0.11);
  close(musicTileJoint(0, 3, 0.001, 0.001, 2), 0);
  assert.ok(musicTileJoint(0, 3, 0.001, 0.001, 15) > 0.99);
  for (const x of [0, 0.3, 1.5]) for (const s of [0, 1, 3]) {
    close(musicTileJoint(x, s, 20, 20, 15), T.jointM / T.cellX);
  }
});
