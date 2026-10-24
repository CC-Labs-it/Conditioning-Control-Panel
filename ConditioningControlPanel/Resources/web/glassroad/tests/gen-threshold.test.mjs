// tests/gen-threshold.test.mjs - polish 6 GEO: the Threshold generator builds the stacked course it promises
// (three decks, crossings, the wheel and its hoop, the dare route, every forward fall a registered route)
// and the committed tracks/threshold.json is exactly its output.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { build, checks, openEdgeReport, openSides, loopLocal, PIECES } from '../dev/gen-threshold.mjs';

const built = build(PIECES.map((p) => ({ ...p })));
const { errs, info } = checks(built);
const json = JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8'));

test('gen: the course passes every generator check', () => {
  assert.deepEqual(errs, []);
  assert.deepEqual(info.levels, [0, 14, 28]);
  assert.ok(info.crossings.length >= 2, 'crossings ' + info.crossings.length);
  assert.ok(info.lengthM >= 5000 && info.lengthM <= 6500, 'six-section lap length: ' + info.lengthM);
});

test('gen: tracks/threshold.json is the generator output', () => {
  assert.deepEqual(json.nodes, JSON.parse(JSON.stringify(built.track.nodes)));
  assert.deepEqual(json.routes, JSON.parse(JSON.stringify(built.track.routes)));
  assert.deepEqual(json.courts, JSON.parse(JSON.stringify(built.track.courts)));
  assert.deepEqual(json.boosts, JSON.parse(JSON.stringify(built.track.boosts)));
  assert.deepEqual(json.checkpoints, built.track.checkpoints);
});

test('gen: everyday lanes give slides room while the nested loops retain clearance', () => {
  const widths = id => built.track.nodes.filter((_, i) => PIECES[built.meta[i].piece].id === id).map(n => n.w);
  for (const id of ['sweepA', 'hairpin', 'sweepD', 'sweepE', 'sweepF']) assert.ok(Math.min(...widths(id)) >= 24.5, id);
  for (const id of ['start', 'home']) assert.ok(Math.min(...widths(id)) >= 28, id);
  assert.ok(Math.min(...widths('prism-run')) >= 34);
  assert.ok(Math.min(...widths('lens-gallery')) >= 38);
  for (const id of ['wheel', 'hoop', 'c1', 'c2']) assert.ok(Math.max(...widths(id)) <= 14, id + ' keeps its air gap');
});

test('gen: loop clearance uses its actual entry and exit widths', () => {
  const pieces = PIECES.map((p) => ({ ...p, ...(['wheel', 'link'].includes(p.id) ? { w: 17 } : {}) }));
  assert.ok(checks(build(pieces)).errs.some((e) => e.includes('loop wheel exit offset')));
  const inner = PIECES.find((p) => p.id === 'c1'), outer = PIECES.find((p) => p.id === 'c3');
  assert.equal(outer.r - inner.r - (inner.w + outer.w) / 2, 2.5, 'the dare still crosses a real gap');
});

test('gen: loops carry magnet and both rails, roll 0, and never hold a checkpoint, an item or a respawn grid', () => {
  const loops = PIECES.filter((p) => p.loop);
  assert.ok(loops.length >= 1);
  for (const p of loops) {
    assert.ok(p.r >= 14 && p.r <= 18, p.id + ' radius ' + p.r);
    assert.ok(Math.abs(p.off) >= 18, p.id + ' exits a road width aside');
    const a = loopLocal(p, 0), m = loopLocal(p, 0.5), z = loopLocal(p, 1);
    assert.ok(Math.abs(a.up) < 1e-9 && Math.abs(m.up - 2 * p.r) < 1e-9 && Math.abs(z.up) < 1e-9 && Math.abs(z.side - p.off) < 1e-9);
  }
  const mag = json.nodes.map((n) => n.flags.includes('magnet'));
  json.nodes.forEach((n, i) => { if (mag[i]) { assert.ok(n.flags.includes('rail'), 'rails both sides at ' + i); assert.equal(n.roll, 0); } });
  for (const c of json.checkpoints) assert.ok(!mag[c], 'checkpoint ' + c + ' in a loop');
  for (const it of json.items) assert.ok(!mag[it.node], 'item in a loop at ' + it.node);
  assert.ok(!mag[json.spawn.node]);
});

test('gen: every open edge over a road ahead is a registered route; the dare runs alongside, lower', () => {
  const rep = openEdgeReport(built.track, built.meta, built.lengthM);
  assert.ok(rep.some((e) => e.under >= 0 && e.ahead >= 0), 'a forward fall exists');
  assert.ok(rep.some((e) => e.under >= 0 && e.ahead < 0), 'a fall onto an earlier road exists');
  for (const e of rep) if (e.ahead >= 0) assert.ok(e.route, 'node ' + e.node + ' ' + e.side + ' falls ahead with no route');
  const dare = json.routes.find((r) => r.id === 'dare');
  assert.ok(dare, 'the dare route');
  for (let i = dare.from; i < dare.to; i++) {
    const e = rep.find((x) => x.node === i && x.side === dare.side);
    assert.ok(e && e.dy >= 3.5 && e.dy <= 16, 'dare drop at ' + i + ': ' + (e && e.dy));
  }
});

test('gen: open sides read the rail flags', () => {
  assert.deepEqual(openSides(['rail']), []);
  assert.deepEqual(openSides(['railL']), ['R']);
  assert.deepEqual(openSides(['railR', 'boost']), ['L']);
  assert.deepEqual(openSides(['open']), ['L', 'R']);
  assert.deepEqual(openSides(['jump']), []);
});
