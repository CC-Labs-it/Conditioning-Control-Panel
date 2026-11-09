// The Go Outside meadow's pure parts: heightmap, world layout, meshing counts, walking clamps and the autopilot.
// The live scene itself is driven in a browser by dev/meadow-drive.mjs.
import test from 'node:test';
import assert from 'node:assert/strict';
import {
  MEADOW, TILE_NAMES, AIR, GRASS, LOG, heightAt, buildWorld, worldVolume, meshVoxels, tileFor, atlasRects,
  placeholderLayout, plantMesh, cloudVolume, stepWalk, groundAt, walkBounds, autopilotYaw, forwardOf, clearYaw,
} from '../ui/unlocks/extras/outside-meadow.js';

const rects = atlasRects(placeholderLayout());
const tinyVol = (cells, sx = 4, sy = 4, sz = 4) => ({
  sx, sy, sz,
  get(x, y, z) { return cells.has(`${x},${y},${z}`) ? GRASS : AIR; },
});

test('the heightmap rolls gently inside 3..9', () => {
  let lo = 99, hi = 0, steep = 0;
  for (let z = 0; z < 80; z += 1) for (let x = 0; x < 80; x += 1) {
    const h = heightAt(x, z);
    lo = Math.min(lo, h); hi = Math.max(hi, h);
    if (Math.abs(heightAt(x + 1, z) - h) > 1 || Math.abs(heightAt(x, z + 1) - h) > 1) steep += 1;
  }
  assert.ok(lo >= 3 && hi <= 9, `range ${lo}..${hi}`);
  assert.ok(hi - lo >= 3, 'not flat');
  assert.ok(steep < 80, `${steep} cliffs of 2+ blocks`);
});

test('the world is deterministic per seed', () => {
  const a = buildWorld({ seed: 7 }), b = buildWorld({ seed: 7 }), c = buildWorld({ seed: 8 });
  assert.deepEqual(a.trees, b.trees);
  assert.deepEqual(a.plants.slice(0, 20), b.plants.slice(0, 20));
  assert.notDeepEqual(a.trees, c.trees);
});

test('6-10 trees in the meadow, cows and trees in front of the start', () => {
  for (const lite of [false, true]) {
    const w = buildWorld({ lite });
    const inside = w.trees.filter((t) => t.x >= w.pad && t.x < w.pad + w.size && t.z >= w.pad && t.z < w.pad + w.size);
    assert.ok(inside.length >= 6 && inside.length <= 10, `${inside.length} trees`);
    const [fx, fz] = forwardOf(w.start.yaw);
    const ahead = (o, cone) => {
      const dx = o.x - w.start.x, dz = o.z - w.start.z, d = Math.hypot(dx, dz);
      return (dx * fx + dz * fz) / d > Math.cos(cone);
    };
    // A 390 px portrait sees about +-18 degrees: something has to be in that.
    assert.ok(w.cows.filter((c) => ahead(c, 0.3)).length >= 1, 'a cow in the narrow view');
    assert.ok(inside.filter((t) => ahead({ x: t.x + 0.5, z: t.z + 0.5 }, 0.3)).length >= 1, 'a tree in the narrow view');
    assert.equal(w.cows.length, lite ? MEADOW.cowsLite : MEADOW.cows);
    for (const c of w.cows) assert.ok(!w.trunks.has(Math.floor(c.z) * w.W + Math.floor(c.x)), 'cow inside a trunk');
  }
});

test('lite has fewer plants and a smaller field', () => {
  const full = buildWorld(), lite = buildWorld({ lite: true });
  assert.ok(lite.plants.length < full.plants.length * 0.6, `${lite.plants.length} vs ${full.plants.length}`);
  assert.ok(lite.W < full.W);
  const kinds = new Set(full.plants.map((p) => p.kind));
  for (const k of ['tall_grass', 'flower_a', 'flower_b', 'flower_c']) assert.ok(kinds.has(k), k);
  for (const p of full.plants) assert.ok(!full.trunks.has(Math.floor(p.z) * full.W + Math.floor(p.x)), 'plant in a trunk');
});

test('meshing culls hidden faces', () => {
  assert.equal(meshVoxels(tinyVol(new Set(['1,1,1'])), tileFor, rects).quads, 6);
  assert.equal(meshVoxels(tinyVol(new Set(['1,1,1', '2,1,1'])), tileFor, rects).quads, 10);
  const slab = new Set(['0,0,0', '1,0,0', '0,0,1', '1,0,1']);
  assert.equal(meshVoxels(tinyVol(slab), tileFor, rects).quads, 16);
  const m = meshVoxels(tinyVol(new Set(['1,1,1'])), tileFor, rects);
  assert.equal(m.positions.length, 6 * 4 * 3);
  assert.equal(m.indices.length, 6 * 6);
});

test('the floor reads solid, so the ground has no bottom faces', () => {
  const vol = tinyVol(new Set(['1,0,1']));
  const floored = { ...vol, get: (x, y, z) => (y < 0 ? GRASS : vol.get(x, y, z)) };
  assert.equal(meshVoxels(floored, tileFor, rects).quads, 5);
});

test('a lone block shades its top brightest and keeps uvs inside its tile', () => {
  const m = meshVoxels(tinyVol(new Set(['1,1,1'])), tileFor, rects);
  const top = m.colors[2 * 12], side = m.colors[0];
  assert.ok(top > side, 'top brighter than +x');
  const [u0, v0, u1, v1] = rects.grass_top;
  for (let k = 0; k < 4; k += 1) {
    const u = m.uvs[(2 * 4 + k) * 2], v = m.uvs[(2 * 4 + k) * 2 + 1];
    const e = 1e-6;
    assert.ok(u >= u0 - e && u <= u1 + e && v >= v0 - e && v <= v1 + e, `uv ${u},${v}`);
  }
});

test('the whole meadow fits a phone budget', () => {
  for (const lite of [false, true]) {
    const w = buildWorld({ lite });
    const m = meshVoxels(worldVolume(w), tileFor, rects);
    assert.ok(m.quads < (lite ? 9000 : 16000), `${lite ? 'lite' : 'full'} ${m.quads} quads`);
    assert.ok(m.quads > 3000, 'something was meshed');
    const p = plantMesh(w, rects);
    assert.equal(p.quads, w.plants.length * 2);
    assert.equal(p.sway.filter((s) => s === 1).length, p.quads * 2);
  }
});

test('trees are trunks under leaves', () => {
  const w = buildWorld();
  const vol = worldVolume(w);
  for (const t of w.trees) {
    assert.equal(vol.get(t.x, t.g, t.z), LOG);
    assert.equal(vol.get(t.x, t.g + t.trunk - 1, t.z), LOG);
  }
});

test('clouds wrap with no seam faces', () => {
  const vol = cloudVolume(7);
  const filled = vol.cells.filter((c) => c).length;
  assert.ok(filled > 48 * 48 * 0.08 && filled < 48 * 48 * 0.6, `${filled} cloud cells`);
  assert.equal(vol.get(48, 0, 3), vol.get(0, 0, 3));
  assert.equal(vol.get(-1, 0, 3), vol.get(47, 0, 3));
});

test('every contract tile has a placeholder slot', () => {
  const lay = placeholderLayout();
  for (const n of TILE_NAMES) assert.ok(lay.tiles[n], n);
  for (const n of ['grass_top', 'grass_side', 'dirt', 'log_side', 'log_top', 'leaves', 'flower_a', 'flower_b', 'flower_c',
    'tall_grass', 'cloud', 'sun', 'cow_head', 'cow_body', 'cow_leg']) assert.ok(TILE_NAMES.includes(n), n);
});

test('walking never leaves the meadow', () => {
  const w = buildWorld();
  const { lo, hi } = walkBounds(w);
  let pos = { x: w.start.x, z: w.start.z };
  for (let i = 0; i < 400; i += 1) pos = { ...stepWalk(w, { ...pos, feet: groundAt(w, pos.x, pos.z) }, 0.3, 0.25), feet: undefined };
  assert.ok(pos.x >= lo && pos.x <= hi && pos.z >= lo && pos.z <= hi, `${pos.x},${pos.z}`);
});

test('trunks, tall steps and obstacles block; flat ground does not', () => {
  const W = 20, heights = new Uint8Array(W * W).fill(4);
  heights[10 * W + 12] = 6;   // a 2-block step east of (10.5, 10.5)
  const w = { W, size: 16, pad: 2, heights, trunks: new Set([10 * W + 8]) };
  const start = { x: 10.5, z: 10.5, feet: 4 };
  assert.ok(stepWalk(w, start, 0.4, 0).x > 10.5, 'flat ground walks');
  let p = { ...start };
  for (let i = 0; i < 20; i += 1) p = { ...stepWalk(w, p, 0.1, 0), feet: 4 };
  assert.ok(p.x < 12 - 0.27, `walked into the step: ${p.x}`);
  p = { ...start };
  for (let i = 0; i < 20; i += 1) p = { ...stepWalk(w, p, -0.1, 0), feet: 4 };
  assert.ok(p.x > 9 + 0.27, `walked into the trunk: ${p.x}`);
  p = stepWalk(w, start, 0, -0.5, [{ x: 10.5, z: 9.6, r: 0.75 }]);
  assert.equal(p.z, 10.5, 'a cow blocks');
  const one = { ...w, heights: new Uint8Array(W * W).fill(4) };
  one.heights[10 * W + 11] = 5;
  assert.ok(stepWalk(one, start, 0.6, 0).x > 11, 'one block steps up');
});

test('the autopilot drifts for ten minutes without leaving or sticking', () => {
  const w = buildWorld();
  const { lo, hi } = walkBounds(w);
  let pos = { x: w.start.x, z: w.start.z }, yaw = 0, t = 0, path = 0;
  const dt = 1 / 30;
  for (let i = 0; i < 30 * 600; i += 1) {
    t += dt;
    const turn = Math.atan2(Math.sin(autopilotYaw(w, pos, yaw, t) - yaw), Math.cos(autopilotYaw(w, pos, yaw, t) - yaw));
    yaw += Math.max(-0.25, Math.min(0.25, turn * 0.5)) * dt;
    const [fx, fz] = forwardOf(yaw), s = MEADOW.autoSpeed * dt;
    const next = stepWalk(w, { ...pos, feet: groundAt(w, pos.x, pos.z) }, fx * s, fz * s);
    const moved = Math.hypot(next.x - pos.x, next.z - pos.z);
    if (moved < 0.2 * s) yaw += 0.8 * dt;
    path += moved;
    pos = { x: next.x, z: next.z };
    assert.ok(pos.x >= lo && pos.x <= hi && pos.z >= lo && pos.z <= hi);
  }
  assert.ok(path > MEADOW.autoSpeed * 600 * 0.6, `walked ${path.toFixed(0)} blocks`);
  const c = w.pad + w.size / 2;
  assert.ok(Math.hypot(pos.x - c, pos.z - c) < w.size * 0.5, 'stayed near the middle');
});

test('the autopilot veers away from a trunk ahead', () => {
  const w = buildWorld();
  const c = w.pad + w.size / 2;
  const right = { ...w, trees: [{ x: Math.floor(c) + 1, z: Math.floor(c) - 4 }] };   // ahead and to the right of yaw 0
  const left = { ...w, trees: [{ x: Math.floor(c) - 2, z: Math.floor(c) - 4 }] };
  const pos = { x: Math.floor(c) + 0.5, z: Math.floor(c) + 0.5 };
  assert.ok(autopilotYaw(right, pos, 0, 0) > 0.5, 'turns left');
  assert.ok(autopilotYaw(left, pos, 0, 0) < -0.5, 'turns right');
});

test('the calm view after the lock turns off a trunk in the face', () => {
  const w = buildWorld();
  const c = w.pad + w.size / 2, pos = { x: Math.floor(c) + 0.5, z: Math.floor(c) + 0.5 };
  const facing = { ...w, trees: [{ x: Math.floor(c), z: Math.floor(c) - 3 }] };
  const y = clearYaw(facing, pos, 0);
  assert.ok(Math.abs(y) >= 0.35 && Math.abs(y) <= 1.4, `yaw ${y}`);
  assert.equal(clearYaw({ ...w, trees: [] }, pos, 0.3), 0.3);
});
