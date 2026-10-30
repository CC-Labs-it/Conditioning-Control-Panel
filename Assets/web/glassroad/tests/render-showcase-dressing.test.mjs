import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { showcaseLayout, SHOWCASE_KIT } from '../render/showcaseDressingMath.js';

const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8')));
const dot = (a, b) => a.reduce((s, v, i) => s + v * b[i], 0);
const sub = (a, b) => a.map((v, i) => v - b[i]);
const rows = (layout) => Object.values(layout).flat();

test('construction leaves every real jump empty and stays inside the static geometry budget', () => {
  const layout = showcaseLayout(track), all = rows(layout);
  assert.ok(all.length > 100);
  assert.ok(all.length * 12 < 35000, 'three box batches remain below 35k triangles');
  for (const r of all) {
    assert.ok(!track.frameAt(r.s).flags.includes('jump'));
    for (const jump of track.jumps) assert.ok(r.s < jump.s || r.s > jump.landS, 'nothing spanning a jump');
    assert.ok([...r.p, ...r.b, ...r.n, ...r.t, ...r.size].every(Number.isFinite));
    assert.ok(r.size.every((v) => v > 0));
  }
});

test('fascia and trim stay below the real road instead of becoming new collision barriers', () => {
  const layout = showcaseLayout(track);
  for (const r of [...layout.fascia, ...layout.trim]) {
    const f = track.frameAt(r.s), h = dot(sub(r.p, f.p), f.n);
    const top = h + Math.abs(dot(r.n, f.n)) * r.size[1] * 0.5;
    assert.ok(top < -0.02, 'top below road at ' + r.s.toFixed(1) + ': ' + top.toFixed(3));
  }
});

function loopTrack(rotation = (p) => p, translate = [0, 0, 0], R = 16) {
  const lengthM = 2 * Math.PI * R;
  return { lengthM, frameAt(s) {
    const a = s / R, p = [0, R * (1 - Math.cos(a)), -R * Math.sin(a)];
    return { p: rotation(p).map((v, i) => v + translate[i]),
      n: rotation([0, Math.cos(a), Math.sin(a)]), b: rotation([1, 0, 0]),
      t: rotation([0, Math.sin(a), -Math.cos(a)]), w: 12, flags: ['magnet', 'rail'] };
  } };
}

test('construction follows a vertical loop through the inverted crown without a world-up flip', () => {
  const loop = loopTrack(), layout = showcaseLayout(loop);
  const crown = layout.fascia.filter((r) => Math.abs(r.s - loop.lengthM / 2) < 8);
  assert.ok(crown.length >= 4);
  for (const r of crown) assert.ok(r.n[1] < -0.8);
  for (const r of [...layout.fascia, ...layout.trim]) {
    const f = loop.frameAt(r.s), h = dot(sub(r.p, f.p), f.n);
    assert.ok(h + r.size[1] / 2 < -0.02, 'loop body stays behind the surface');
    assert.ok(Math.abs(dot(r.b, r.n)) < 1e-10);
    assert.ok(Math.abs(dot(r.t, r.n)) < 1e-10);
  }
});

test('oriented box corners and face centres clear a 14 m loop and tight spline-join curvature', () => {
  // A circular loop gives an analytic surface instead of comparing with a flat midpoint plane.
  // P6 also has tighter local spline curvature: its face centres pierced the road even though
  // every corner cleared it. The 8 m stress fixture reproduces that failure with the old depth.
  for (const radius of [14, 8]) {
    const layout = showcaseLayout(loopTrack(undefined, undefined, radius));
    let points = 0;
    for (const r of [...layout.fascia, ...layout.trim]) {
      for (const x of [-1, 0, 1]) for (const y of [-1, 1]) for (const z of [-1, -0.5, 0, 0.5, 1]) {
        const p = r.p.map((v, i) => v + r.b[i] * x * r.size[0] * 0.5
          + r.n[i] * y * r.size[1] * 0.5 + r.t[i] * z * r.size[2] * 0.5);
        const height = radius - Math.hypot(p[1] - radius, p[2]);
        assert.ok(height < -0.02, 'box surface below curved road: radius ' + radius + ', height ' + height);
        points++;
      }
    }
    assert.ok(points > 1000, 'all eight corners plus upper/lower face stations sampled');
  }
});

test('a rebuilt or moved course gets the same kit entirely in its own frame', () => {
  const turn = ([x, y, z]) => [z, y, -x], offset = [120, 28, -75];
  const a = rows(showcaseLayout(loopTrack())), b = rows(showcaseLayout(loopTrack(turn, offset)));
  assert.equal(a.length, b.length);
  a.forEach((r, i) => {
    const expected = turn(r.p).map((v, j) => v + offset[j]);
    assert.ok(Math.hypot(...sub(expected, b[i].p)) < 1e-9);
    assert.ok(Math.hypot(...sub(turn(r.n), b[i].n)) < 1e-9);
  });
});

test('invalid track makes an empty kit and very long tracks have a bounded instance allocation', () => {
  assert.equal(rows(showcaseLayout(null)).length, 0);
  const long = { lengthM: 1000000, frameAt: (s) => ({ p: [0, 0, -s], t: [0, 0, -1], n: [0, 1, 0], b: [1, 0, 0], w: 14, flags: ['rail'] }) };
  assert.ok(rows(showcaseLayout(long)).length <= SHOWCASE_KIT.maxSegments * 5);
});
