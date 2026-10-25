import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { holeDistance } from '../track/courts.js';
import { showcaseLayout, SHOWCASE_KIT } from '../render/showcaseDressingMath.js';
import { courtTileLayouts, musicTileMask, ROAD_HALF_WIDTH_RANGE, encodeRoadHalfWidth } from '../render/musicTilesMath.js';

// Use the shipped renderer classes, without a DOM, adapter or GPU context.
const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true }
    : next(specifier, context);
} });
const { courtLayout, buildCourt, COURT_KIT } = await import('../render/court.js');
hooks.deregister();
const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8')));
const holes = track.courts.flatMap((c) => c.holes), layout = courtLayout(track);

test('hole rims and shafts stay outside the actual openings, including their face interiors', () => {
  assert.equal(holes.length, 2);
  for (const row of Object.values(layout).flat().filter((r) => r.hole)) {
    // Corners alone would miss an opaque bar or floor spanning the opening.
    for (const a of [-0.5, -0.25, 0, 0.25, 0.5]) for (const b of [-0.5, -0.25, 0, 0.25, 0.5]) {
      const s = row.s + row.size[2] * a, x = row.x + row.size[0] * b;
      for (const h of holes) assert.ok(holeDistance(h, s, x) >= -1e-7, `${row.hole} blocks ${h.id}`);
    }
  }
  for (const row of layout.shaft.filter((r) => r.hole)) {
    assert.ok(Math.abs(row.h - row.size[1] / 2 + COURT_KIT.depth) < 1e-9);
    assert.ok(Math.abs(row.h + row.size[1] / 2 - (COURT_KIT.wallTop - COURT_KIT.collarH)) < 1e-9);
  }
  for (const row of layout.rim.filter((r) => r.hole)) assert.equal(row.h + row.size[1] / 2, COURT_KIT.rimH);
});

test('destination pylons remain outside the road and construction never bridges an aperture', () => {
  for (const row of Object.values(layout).flat().filter((r) => r.destination)) {
    const f = track.frameAt(row.s);
    assert.ok(Math.abs(row.x) - row.size[0] / 2 > f.w / 2 + 0.4);
  }
  for (const row of showcaseLayout(track).sleeper) for (const h of holes) {
    assert.ok(row.s + SHOWCASE_KIT.sleeperDepth / 2 < h.s || row.s - SHOWCASE_KIT.sleeperDepth / 2 > h.s + h.len);
  }
});

test('court layout follows a moved track frame and has no world-up assumptions', () => {
  const turn = ([x, y, z]) => [z, x, y], offset = [20, -14, 80];
  const moved = { ...track, frameAt(s) {
    const f = track.frameAt(s);
    return { ...f, p: turn(f.p).map((v, i) => v + offset[i]), b: turn(f.b), n: turn(f.n), t: turn(f.t) };
  } };
  const original = Object.values(layout).flat(), transformed = Object.values(courtLayout(moved)).flat();
  original.forEach((r, i) => {
    assert.ok(Math.hypot(...turn(r.p).map((v, j) => v + offset[j] - transformed[i].p[j])) < 1e-9);
    assert.deepEqual(transformed[i].n, turn(r.n));
  });
});

test('court tiles announce the opening early, cover broad choices and preserve shoulders and exit boost', () => {
  const [tiles] = courtTileLayouts(track), [court] = track.courts;
  assert.ok(tiles.forward[0] < -0.99, 'bevels follow the return direction');
  assert.equal(musicTileMask(tiles, holes[0].s - 6, 0, track.frameAt(holes[0].s - 6).w / 2), 1);
  for (const x of [-12, 0, 12]) assert.equal(musicTileMask(tiles, holes[1].s, x, 17), 1);
  assert.equal(musicTileMask(tiles, holes[1].s, 16, 17), 0);
  assert.equal(musicTileMask(tiles, court.fromS - 1, 0, 17), 0);
  for (const pad of tiles.pads) assert.equal(musicTileMask(tiles, tiles.startS + pad.s, pad.x, 17), 0);
});

test('road width encoding preserves the 34 m court without saturation or material edge drift', () => {
  for (const half of [7, 8, 8.75, 12, 17, 20]) {
    const byte = encodeRoadHalfWidth(half);
    assert.ok(byte < 255);
    assert.ok(Math.abs(byte / 255 * ROAD_HALF_WIDTH_RANGE - half) <= ROAD_HALF_WIDTH_RANGE / 510 + 1e-9);
  }
  assert.equal(encodeRoadHalfWidth(-1), 0);
  assert.equal(encodeRoadHalfWidth(240), 255);
});

test('court rendering stays in three small batches and releases its resources exactly once', () => {
  const court = buildCourt(track, null), meshes = court.group.children;
  assert.equal(meshes.length, 3);
  assert.ok(Object.values(court.counts).reduce((a, b) => a + b, 0) * 12 < 600);
  let geometryDisposed = 0, materialsDisposed = 0, batchesDisposed = 0;
  meshes[0].geometry.addEventListener('dispose', () => geometryDisposed++);
  for (const mesh of meshes) {
    assert.ok(mesh.boundingSphere.radius > 0);
    mesh.material.addEventListener('dispose', () => materialsDisposed++);
    mesh.addEventListener('dispose', () => batchesDisposed++);
  }
  court.dispose(); court.dispose();
  assert.equal(geometryDisposed, 1); assert.equal(materialsDisposed, 3); assert.equal(batchesDisposed, 3);
  const absent = buildCourt({ courts: [] }, null);
  assert.equal(absent.group.children.length, 0); absent.dispose();
});
