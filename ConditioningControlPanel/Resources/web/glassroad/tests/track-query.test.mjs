import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';

const json = JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8'));
const tr = loadTrack(json);
const L = tr.lengthM;
const sDiff = (a, b) => { const d = Math.abs(a - b) % L; return Math.min(d, L - d); };
const place = (s, lat, h) => {
  const f = tr.frameAt(s);
  return { f, P: [0, 1, 2].map((k) => f.p[k] + f.b[k] * lat + f.n[k] * h) };
};
// deterministic pseudo random
let seed = 7;
// polish 6: a flat railed node past the grid (no roll, no grade), the first open node (the open corner),
// and two nodes stacked in plan at least 12 m apart (the column climb ends over its own start)
const FLAT = json.nodes.findIndex((n, i) => i > 20 && n.roll === 0 && n.flags.length === 1 && n.flags[0] === 'rail' && Math.abs(tr.frameAt(tr.nodeS[i] + 3).t[1]) < 0.01);
const OPEN = json.nodes.findIndex((n) => n.flags.includes('open'));
let UP = -1, LOW = -1;
for (let i = 0; i < json.nodes.length && UP < 0; i++) for (let j = 0; j < json.nodes.length; j++) {
  const a = json.nodes[i].p, b = json.nodes[j].p;
  if (Math.hypot(a[0] - b[0], a[2] - b[2]) < 0.5 && a[1] - b[1] > 12 && !json.nodes[i].flags.includes('magnet')) { UP = i; LOW = j; break; }
}
const rnd = () => ((seed = (seed * 16807) % 2147483647) / 2147483647);

test('query: round trips frameAt points to s, lateral and height', () => {
  for (let i = 0; i < 3000; i++) {
    const s = rnd() * L, f0 = tr.frameAt(s), lat = (rnd() * 2 - 1) * f0.w * 0.5, h = rnd() * 3.5 - 0.5;
    const { f, P } = place(s, lat, h);
    for (const hint of [f.seg, (f.seg + 3) % tr.nodeCount, -1]) {
      const q = tr.query(P[0], P[1], P[2], hint);
      assert.ok(sDiff(q.s, s) < 1e-3, 's ' + q.s + ' vs ' + s + ' hint ' + hint);
      assert.ok(Math.abs(q.lateral - lat) < 1e-3, 'lateral ' + q.lateral + ' vs ' + lat);
      assert.ok(Math.abs(q.height - h) < 1e-3, 'height ' + q.height + ' vs ' + h);
      assert.ok(q.seg === f.seg || sDiff(q.s, tr.nodeS[q.seg]) < 1e-3 || sDiff(q.s, tr.nodeS[(q.seg + 1) % tr.nodeCount]) < 1e-3);
      assert.ok(Math.abs(q.edgeDist - (f.w / 2 - Math.abs(lat))) < 1e-3);
      assert.ok(Math.hypot(q.p[0] + q.normal[0] * h - P[0], q.p[1] + q.normal[1] * h - P[1], q.p[2] + q.normal[2] * h - P[2]) < 1e-3);
    }
  }
});

test('query: segT and surface data match frameAt', () => {
  const n = FLAT;
  const f = tr.frameAt(tr.nodeS[n] + 3);
  const q = tr.query(f.p[0], f.p[1] + 0.5, f.p[2], n);
  assert.equal(q.seg, n);
  assert.ok(Math.abs(q.segT - f.segT) < 1e-3);
  assert.equal(q.surface, 'glass');
  assert.deepEqual(q.flags, json.nodes[n].flags);
  assert.ok(Math.abs(q.normal[0] - f.n[0]) < 1e-3 && Math.abs(q.normal[1] - f.n[1]) < 1e-3);
});

test('query: wide start shoulder stays on its floor beneath the raised bridge', () => {
  const s = 62.62450764796011, lateral = 13.813753416675029, height = .21920925714970074;
  const { f, P } = place(s, lateral, height);
  for (const hint of [-1, f.seg, 31]) {
    const q = tr.query(...P, hint);
    assert.ok(sDiff(q.s, s) < 1e-3);
    assert.ok(Math.abs(q.height - height) < 1e-3);
    assert.equal(q.onRoad, true);
    assert.ok(Math.abs(q.p[1]) < 1e-3, 'the 11 m overhead bridge cannot own this floor contact');
  }
});

test('query: past the edge, open sides and rails', () => {
  const railS = tr.nodeS[20] + 4, openS = tr.nodeS[OPEN] + 4;
  for (const side of [-1, 1]) {
    const w = tr.frameAt(railS).w;
    let q = tr.query(...place(railS, side * (w / 2 - 0.5), 0.3).P, 20);
    assert.equal(q.onRoad, true); assert.equal(q.railed, true);
    q = tr.query(...place(railS, side * (w / 2 + 1), 0.3).P, 20);
    assert.equal(q.onRoad, false); assert.ok(Math.abs(q.edgeDist + 1) < 1e-3); assert.equal(q.railed, true);
    q = tr.query(...place(openS, side * (w / 2 + 1), -2).P, OPEN);
    assert.equal(q.onRoad, false); assert.equal(q.railed, false); assert.ok(q.height < -1.9);
    q = tr.query(...place(openS, 0, 0.2).P, OPEN);
    assert.equal(q.onRoad, true); assert.equal(q.railed, false);
  }
});

test('query: one-sided rails follow the side', () => {
  const one = structuredClone(json);
  one.nodes[20].flags = ['railL'];
  const t2 = loadTrack(one), s = t2.nodeS[20] + 4, f = t2.frameAt(s);
  const at = (lat) => t2.query(...[0, 1, 2].map((k) => f.p[k] + f.b[k] * lat + f.n[k] * 0.3), 20);
  assert.equal(at(-3).railed, true);
  assert.equal(at(3).railed, false);
});

test('query: the jump gap has no road', () => {
  const j = tr.jumps[0], mid = (j.s + j.landS) / 2;
  const q = tr.query(...place(mid, 0, 2).P, j.node);
  assert.equal(q.seg, j.node);
  assert.equal(q.onRoad, false);
  assert.equal(q.railed, false);
  assert.ok(Math.abs(q.height - 2) < 1e-3);
  assert.equal(tr.query(...place(j.s - 1, 0, 0.3).P, j.node - 1).onRoad, true);
  assert.equal(tr.query(...place(j.landS + 1, 0, 0.3).P, j.node + 1).onRoad, true);
});

test('query: stacked spiral passes resolve to the nearer one', () => {
  assert.ok(UP >= 0, 'a stacked pass exists');
  const up = tr.frameAt(tr.nodeS[UP]), low = tr.frameAt(tr.nodeS[LOW]);
  assert.ok(Math.hypot(up.p[0] - low.p[0], up.p[2] - low.p[2]) < 0.5 && up.p[1] - low.p[1] > 12, 'passes stacked');
  for (const [hu, hl] of [[-1, -1], [UP - 1, LOW + 1]]) {
    assert.ok(sDiff(tr.query(up.p[0] + up.n[0] * 0.5, up.p[1] + up.n[1] * 0.5, up.p[2] + up.n[2] * 0.5, hu).s, tr.nodeS[UP]) < 1e-3, 'upper, hint ' + hu);
    assert.ok(sDiff(tr.query(low.p[0] + low.n[0] * 0.5, low.p[1] + low.n[1] * 0.5, low.p[2] + low.n[2] * 0.5, hl).s, tr.nodeS[LOW]) < 1e-3, 'lower, hint ' + hl);
  }
});

test('query: a stale hint falls back to a full search; far points still get a segment', () => {
  const f = tr.frameAt(tr.nodeS[60] + 2);
  const q = tr.query(f.p[0], f.p[1] + 0.4, f.p[2], 5);
  assert.equal(q.seg, 60);
  const far = tr.query(5000, -300, 5000, 10);
  assert.ok(far.seg >= 0 && far.seg < tr.nodeCount && far.onRoad === false);
});

test('query: perf budget on threshold', () => {
  const pts = [];
  for (let i = 0; i < 4000; i++) { const s = (i * 0.337) % L, f = tr.frameAt(s); pts.push([f.p[0] + f.n[0], f.p[1] + f.n[1], f.p[2] + f.n[2], f.seg]); }
  const q = {}; Object.assign(q, tr.query(0, 0, 0));
  let t0 = performance.now();
  for (const p of pts) tr.query(p[0], p[1], p[2], p[3], q);
  const hinted = (performance.now() - t0) / pts.length;
  t0 = performance.now();
  for (const p of pts) tr.query(p[0], p[1], p[2], -1, q);
  const full = (performance.now() - t0) / pts.length;
  assert.ok(hinted < 0.05, 'hinted query ' + hinted + ' ms');
  assert.ok(full < 0.25, 'full query ' + full + ' ms');
});
