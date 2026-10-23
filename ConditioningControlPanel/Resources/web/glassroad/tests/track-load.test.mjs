import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';

const json = JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8'));
const tr = loadTrack(json);
const N = json.nodes.length;
const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const dist = (a, b) => Math.hypot(a[0] - b[0], a[1] - b[1], a[2] - b[2]);
const DEG = Math.PI / 180;

test('track: length and node arc lengths', () => {
  assert.equal(tr.nodeCount, N);
  assert.equal(tr.nodeS.length, N + 1);
  assert.equal(tr.nodeS[0], 0);
  assert.equal(tr.nodeS[N], tr.lengthM);
  for (let i = 0; i < N; i++) assert.ok(tr.nodeS[i + 1] > tr.nodeS[i] + 5, 'node ' + i);
  // polish 6: the stacked course, within +-25% of the 1350 m ring it replaced
  assert.ok(tr.lengthM > 1150 && tr.lengthM < 1650, 'length ' + tr.lengthM);
  for (let i = 0; i < N; i++) assert.ok(dist(tr.frameAt(tr.nodeS[i]).p, json.nodes[i].p) < 1e-6, 'passes node ' + i);
});

test('track: loop closes', () => {
  const a = tr.frameAt(0), b = tr.frameAt(tr.lengthM), c = tr.frameAt(tr.lengthM - 0.01), d = tr.frameAt(-0.01);
  assert.ok(dist(a.p, b.p) < 1e-9);
  assert.ok(dist(c.p, d.p) < 1e-9);
  assert.ok(dist(a.p, c.p) < 0.02);
  assert.ok(dot(a.n, c.n) > 0.9999 && dot(a.t, c.t) > 0.9999);
});

test('track: frames orthonormal and continuous, no flips', () => {
  // the gap arc is never driven: it bends hard from the kicker to the landing, so it only must not flip
  const j = tr.jumps[0];
  let prev = null;
  for (let s = 0; s <= tr.lengthM; s += 0.25) {
    const f = tr.frameAt(s), lim = Math.cos((s > j.s && s < j.landS + 0.25 ? 12 : 3) * DEG);
    for (const v of [f.t, f.n, f.b]) assert.ok(Math.abs(Math.hypot(...v) - 1) < 1e-9);
    assert.ok(Math.abs(dot(f.t, f.n)) < 1e-9 && Math.abs(dot(f.t, f.b)) < 1e-9 && Math.abs(dot(f.n, f.b)) < 1e-9);
    // a vertical loop (magnet) turns the road over; everywhere else it faces up
    const loop = [-1, 0, 1].some((d) => tr.nodeFlags(f.seg + d).includes('magnet'));
    if (!loop) assert.ok(f.n[1] > 0.6, 'road faces up at s ' + s);
    if (prev) {
      assert.ok(dot(prev.t, f.t) > lim, 'tangent jump at s ' + s);
      assert.ok(dot(prev.n, f.n) > lim, 'normal jump at s ' + s);
      assert.ok(dist(prev.p, f.p) < 0.3, 'position jump at s ' + s);
    }
    prev = { t: f.t.slice(), n: f.n.slice(), p: f.p.slice() };
  }
});

test('track: roll honoured, right edge low on positive roll, flat when roll is 0', () => {
  json.nodes.forEach((nd, i) => {
    const f = tr.frameAt(tr.nodeS[i]);
    assert.ok(Math.abs(f.roll - nd.roll * DEG) < 1e-9, 'roll at node ' + i);
    if (Math.abs(f.t[1]) < 0.05) assert.ok(Math.abs(Math.asin(-f.b[1]) - nd.roll * DEG) < 0.5 * DEG, 'bank at node ' + i);
    assert.equal(f.w, nd.w);
  });
  // roll between nodes never overshoots its neighbours
  for (let i = 0; i < N; i++) {
    const a = json.nodes[i].roll * DEG, b = json.nodes[(i + 1) % N].roll * DEG;
    for (let k = 1; k < 8; k++) {
      const r = tr.frameAt(tr.nodeS[i] + (tr.nodeS[i + 1] - tr.nodeS[i]) * k / 8).roll;
      assert.ok(r >= Math.min(a, b) - 1e-9 && r <= Math.max(a, b) + 1e-9, 'roll overshoot in segment ' + i);
    }
  }
});

test('track: derived lists', () => {
  assert.deepEqual(tr.checklines.map((c) => c.node), json.checkpoints);
  tr.checklines.forEach((c, i) => { assert.equal(c.idx, i); assert.equal(c.s, tr.nodeS[c.node]); assert.ok(c.w > 0); });
  assert.equal(tr.sections.length, 3);
  assert.equal(tr.sectionAt(0), 0);
  for (const [k, sec] of json.sections.entries()) assert.equal(tr.sectionAt(tr.nodeS[Math.floor((sec.from + sec.to) / 2)]), k);
  assert.equal(tr.sectionAt(tr.lengthM + 1), 0);
  assert.equal(tr.boosts[0].s, tr.nodeS[json.boosts[0].node]);
  const j = tr.jumps[0];
  assert.equal(j.s, tr.nodeS[json.jumps[0].node]);
  assert.equal(j.landS, tr.nodeS[json.jumps[0].node + 1]);
  assert.equal(tr.limits.fallY, json.limits.fallY);
  assert.deepEqual(tr.nodeFlags(N + 1), json.nodes[1].flags);
});

test('track: spawn poses on the grid, facing travel', () => {
  for (let slot = 0; slot < 8; slot++) {
    const sp = tr.spawnPose(slot), g = json.spawn.grid[slot];
    const q = tr.query(sp.p[0], sp.p[1], sp.p[2], sp.seg);
    assert.ok(Math.abs(q.lateral - g[0]) < 1e-6 && Math.abs(q.height) < 1e-6);
    assert.ok(Math.abs(sp.s - (tr.lengthM + g[1])) < 1e-6);
    // local -Z rotated by q equals t
    const [x, y, z, w] = sp.q;
    const fwd = [-(2 * (x * z + w * y)), -(2 * (y * z - w * x)), -(1 - 2 * (x * x + y * y))];
    assert.ok(dot(fwd, tr.frameAt(sp.s).t) > 0.9999);
  }
});

test('track: a vertical loop turns the road over and back, square across the wheel (polish 6)', () => {
  const loops = [];
  for (let i = 0; i < N; i++) if (json.nodes[i].flags.includes('magnet') && !json.nodes[(i - 1 + N) % N].flags.includes('magnet')) loops.push(i);
  assert.ok(loops.length >= 1, 'at least one loop');
  for (const i0 of loops) {
    let i1 = i0;
    while (json.nodes[(i1 + 1) % N].flags.includes('magnet')) i1++;
    const s0 = tr.nodeS[i0], s1 = tr.nodeS[i1 + 1];
    let minNy = 1, maxBy = 0, maxY = -Infinity;
    for (let s = s0; s <= s1; s += 0.5) {
      const f = tr.frameAt(s);
      minNy = Math.min(minNy, f.n[1]); maxBy = Math.max(maxBy, Math.abs(f.b[1])); maxY = Math.max(maxY, f.p[1]);
    }
    assert.ok(minNy < -0.95, 'upside down at the top of the loop at node ' + i0 + ': ' + minNy);
    assert.ok(maxBy < 0.35, 'the road stays square across the wheel (no roll): ' + maxBy);
    assert.ok(maxY - json.nodes[i0].p[1] > 24, 'the wheel stands tall');
    const a = tr.frameAt(s0 - 2), b = tr.frameAt(s1 + 2);
    assert.ok(a.n[1] > 0.95 && b.n[1] > 0.95, 'level in and out');
    assert.ok(Math.hypot(a.p[0] - b.p[0], a.p[2] - b.p[2]) > 16, 'the exit sits a road width aside');
  }
});

test('track: loadTrack throws on a broken track', () => {
  const bad = structuredClone(json);
  bad.nodes[3].w = 2;
  bad.checkpoints = [1, 2];
  assert.throws(() => loadTrack(bad), /track invalid: .*nodes\[3\]\.w/);
  assert.doesNotThrow(() => loadTrack(bad, { validate: false }));
});
