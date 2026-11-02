import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { LOOK } from '../render/look.js';
import {
  WORLD_DEFAULTS, worldLook, sampleTrack, bodySize, bodyClear, segDist, capsulesApart, placeColossi,
  scatterClear, placeScatter, shadowFit, inShadowBox, shadowMode, shadowSize, capsuleShade, shadeRanges, V3,
} from '../render/colossiMath.js';

const json = JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8'));
const layout = JSON.parse(readFileSync(new URL('../tracks/threshold.colossi.json', import.meta.url), 'utf8'));
const manifest = JSON.parse(readFileSync(new URL('../art/manifest.json', import.meta.url), 'utf8'));
const rowOf = (id) => manifest.find((r) => r.id === id) || null;
const tr = loadTrack(json);
const W = worldLook();
const samples = sampleTrack(tr, W.stepM);
const fine = sampleTrack(tr, 2);
const FINE = { ...W, stepM: 2, axisStepM: 2 };
const placed = placeColossi(layout.colossi, tr, rowOf, W, samples);
const near = (a, b, e = 1e-6) => Math.abs(a - b) < e;

test('world: the layout names approved lod0 chess art, a watch and a cabinet, 5 to 7 bodies', () => {
  assert.equal(layout.track, 'threshold');
  assert.ok(layout.colossi.length >= 5 && layout.colossi.length <= 7);
  const ids = layout.colossi.map((c) => c.id);
  for (const id of ['chess-king-lod0', 'chess-knight-lod0', 'chess-queen-lod0', 'pocket-watch']) assert.ok(ids.includes(id), id);
  assert.ok(ids.includes('slot-cabinet-lod0'));
  for (const c of layout.colossi) {
    assert.ok(rowOf(c.id), 'manifest row ' + c.id);
    assert.ok(!/lod1/.test(c.id), 'lod0 only');
    assert.ok(c.heightM >= 80 && c.heightM <= 300, c.id + ' height ' + c.heightM);
    assert.ok(c.kind === 'arch' || c.kind === 'tower');
  }
});

test('world: every colossus places and clears the road tube of every stretch, checked finer than the fitter', () => {
  assert.equal(placed.length, layout.colossi.length, 'all placed: ' + placed.map((p) => p.id).join(' '));
  for (const ax of placed) {
    const c = bodyClear(ax, fine, FINE);
    assert.ok(c.ok, ax.id + ' clips the road at 2 m sampling');
    // an independent check by plain distance: every road edge and centre point stays clearM (14 m)
    // outside the capsule, whatever the frames say
    for (const f of fine) {
      for (const x of [-0.5, 0, 0.5]) {
        const P = V3.add(f.p, V3.mul(f.b, x * f.w));
        const d = segDist(ax.B, ax.top, P, P);
        assert.ok(d >= ax.r + W.clearM - 1e-6, ax.id + ' passes ' + (d - ax.r).toFixed(1) + ' m from the road at s ' + f.s.toFixed(0));
      }
    }
  }
});

test('world: several arches lean across the road at least clearM over it; towers stand beside', () => {
  const arches = placed.filter((p) => p.spec.kind === 'arch'), towers = placed.filter((p) => p.spec.kind === 'tower');
  // polish 3 review: the bishop arch is gone (it read as a tentacle) and the king stands as a tower so its
  // crown reads; the watch still lies across the road
  assert.ok(arches.length >= 1, 'arches ' + arches.length);
  for (const a of arches) {
    const c = bodyClear(a, fine, FINE);
    assert.ok(c.crosses, a.id + ' crosses over the road');
    assert.ok(c.minAboveM >= W.clearM, a.id + ' lowest body over the road ' + c.minAboveM.toFixed(1) + ' m');
    // diagonal: the lean is well off vertical and swung off square to the road
    assert.ok(a.spec.leanDeg >= 45 && Math.abs(a.spec.yawDeg) >= 15, a.id + ' leans diagonally');
  }
  for (const t of towers) assert.ok(!bodyClear(t, fine, FINE).crosses, t.id + ' stays beside the road');
  assert.ok(towers.length >= 1);
});

test('world: bodies keep apart, and the model basis is orthonormal with Y along the axis', () => {
  for (let i = 0; i < placed.length; i++) {
    for (let j = i + 1; j < placed.length; j++) assert.ok(capsulesApart(placed[i], placed[j], W.colossusGapM), placed[i].id + ' vs ' + placed[j].id);
    const a = placed[i];
    assert.ok(near(V3.len(a.X), 1) && near(V3.len(a.Y), 1) && near(V3.len(a.Z), 1));
    assert.ok(near(V3.dot(a.X, a.Y), 0) && near(V3.dot(a.Y, a.Z), 0) && near(V3.dot(a.X, a.Z), 0));
    assert.ok(V3.dot(V3.cross(a.X, a.Y), a.Z) > 0.999, 'right handed');
    const s = bodySize(rowOf(a.id), a.spec.heightM);
    assert.ok(near(a.scale, s.scale) && near(a.r, s.r));
    assert.ok(near(V3.len(V3.sub(a.top, a.B)), a.spec.heightM, 1e-6));
  }
});

test('world: the fitter only raises an arch or steps a tower out, and gives up cleanly', () => {
  for (const a of placed) {
    const raw = layout.colossi.find((c) => c.id === a.id);
    if (raw.kind === 'arch') assert.ok(a.spec.crossUpM >= raw.crossUpM);
    else assert.ok(a.spec.offM >= raw.offM);
  }
  // an arch through the road with no tries left is refused
  const spec = { ...layout.colossi.find((c) => c.kind === 'arch'), crossUpM: 0 };
  assert.equal(placeColossi([spec], tr, rowOf, { ...W, fitTries: 0 }, samples).length, 0);
  // a missing art row skips that body
  assert.equal(placeColossi([{ ...spec, id: 'nope' }], tr, rowOf, W, samples).length, 0);
});

test('world: the scatter never clips any stretch, overlaps nothing, clears the colossi', () => {
  const kinds = W.scatterKinds.filter((k) => rowOf(k.id));
  const SC = { ...FINE, sideM: W.scatterMarginM, underM: W.scatterMarginM, clearM: W.scatterAirM };
  for (const seed of [1, 7, 12345, 99991]) {
    const sc = placeScatter(tr, kinds, rowOf, seed, { avoid: placed, W, samples });
    assert.ok(sc.length > W.scatter * 0.85 && sc.length <= W.scatter, 'seed ' + seed + ' count ' + sc.length);
    for (const x of sc) {
      assert.ok(bodyClear(x.body, fine, SC).ok, 'seed ' + seed + ' ' + x.k.id + ' clips the road at 2 m sampling');
      for (const c of placed) assert.ok(capsulesApart(x.body, c, 0), x.k.id + ' touches ' + c.id);
      if (x.k.id.startsWith('chess-')) assert.equal(x.pose, 'upright');
    }
    for (let i = 0; i < sc.length; i++) for (let j = i + 1; j < sc.length; j++) assert.ok(capsulesApart(sc[i].body, sc[j].body, 0));
    // seeded: the same seed lays the same scatter
    assert.deepEqual(placeScatter(tr, kinds, rowOf, seed, { avoid: placed, W, samples }).map((x) => x.p), sc.map((x) => x.p));
  }
});

test('world: segDist and the tube test agree with hand cases', () => {
  assert.ok(near(segDist([0, 0, 0], [10, 0, 0], [5, 3, 0], [5, 3, 0]), 3));
  assert.ok(near(segDist([0, 0, 0], [10, 0, 0], [0, 2, -5], [0, 2, 5]), 2));
  assert.ok(near(segDist([0, 0, 0], [1, 0, 0], [3, 0, 0], [5, 0, 0]), 2));
  const f = samples[10];
  // a sphere on the road surface clips; one high above or far beside does not
  assert.equal(scatterClear(f.p, 1, samples, W), false);
  assert.equal(scatterClear(V3.add(f.p, V3.mul(f.n, 200)), 1, samples, W), true);
});

test('world: shadow frustum snaps to whole texels and covers the player area', () => {
  const dir = LOOK.lightDir, { map, halfM } = shadowSize('high', W);
  assert.equal(map, W.shadowMap.high);
  const a = shadowFit([10.3, 1, -4.7], dir, halfM, map, W.shadowDistM);
  const b = shadowFit([10.3 + a.texel * 0.3, 1, -4.7], dir, halfM, map, W.shadowDistM);
  for (const fit of [a, b]) {
    const kx = V3.dot(fit.target, fit.right) / fit.texel, ky = V3.dot(fit.target, fit.up) / fit.texel;
    assert.ok(near(kx, Math.round(kx), 1e-6) && near(ky, Math.round(ky), 1e-6), 'snapped');
    assert.ok(near(V3.len(V3.sub(fit.pos, fit.target)), W.shadowDistM, 1e-6));
    assert.ok(near(V3.dot(fit.right, fit.up), 0) && near(V3.dot(fit.right, V3.norm(dir)), 0));
  }
  // across the light the box only ever moves by whole texels (no crawl); along it, depth is free
  for (const ax of ['right', 'up']) {
    const d = Math.abs(V3.dot(b.target, a[ax]) - V3.dot(a.target, a[ax])) / a.texel;
    assert.ok(near(d, 0, 1e-6) || near(d, 1, 1e-6), ax + ' moved ' + d + ' texels');
  }
  // the karts around the player and the road under a colossus overhead are in the box
  for (const P of [[10.3, 1, -4.7], [40, 0, 20], [-20, 3, -30], [10, -6, 40], [60, 2, -40]]) {
    assert.ok(inShadowBox(P, a, halfM, W.shadowFarM), 'in box ' + P);
  }
  // casters high above (a 300 m colossus top) sit inside the depth range
  assert.ok(inShadowBox(V3.add([10.3, 1, -4.7], V3.mul(V3.norm(dir), 400)), a, halfM, W.shadowFarM));
  assert.ok(!inShadowBox([10.3 + 3 * halfM, 1, -4.7], a, halfM, W.shadowFarM));
});

test('world: shadow modes per tier and option, map sizes per tier', () => {
  assert.equal(shadowMode(undefined, 'high'), 'map');
  assert.equal(shadowMode(undefined, 'mid'), 'map');
  assert.equal(shadowMode(undefined, 'phone'), 'blob');
  assert.equal(shadowMode('map', 'mid', false), 'blob');
  assert.equal(shadowMode('off', 'high'), 'off');
  assert.equal(shadowMode('blob', 'high'), 'blob');
  assert.equal(shadowSize('mid', W).map * 2, shadowSize('high', W).map);
  assert.equal(shadowSize('phone', W).map, shadowSize('mid', W).map);
  assert.deepEqual(Object.keys(WORLD_DEFAULTS.shadowMap).sort(), ['high', 'mid']);
});

test('world: phone tier capsule shadows land under the arches and fade at the edge', () => {
  const dir = V3.norm(LOOK.lightDir);
  for (const a of placed.filter((p) => p.spec.kind === 'arch')) {
    const mid = V3.add(a.B, V3.mul(a.D, a.len / 2));
    const ground = V3.sub(mid, V3.mul(dir, 300));
    assert.ok(near(capsuleShade(ground, dir, a), 1), 'core');
    const side = V3.norm(V3.cross(a.D, dir));
    assert.equal(capsuleShade(V3.add(ground, V3.mul(side, a.r * 2)), dir, a), 0, 'outside');
    const edge = capsuleShade(V3.add(ground, V3.mul(side, a.r * 0.9)), dir, a);
    assert.ok(edge > 0 && edge < 1, 'soft edge ' + edge);
  }
  const all = placed.flatMap((p) => shadeRanges(p, dir, samples));
  assert.ok(all.length >= 3, 'colossi shade the road somewhere');
  for (const [s0, s1] of all) assert.ok(s1 > s0);
});
