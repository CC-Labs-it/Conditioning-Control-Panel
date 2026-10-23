// tests/track-water.test.mjs - water wave 1, TRACK lane: the schema accepts stream and pool nodes and bounds a
// flow, the query hands back an interpolated flow, the road mesh carries surface + flow attributes, and the
// Threshold generator keeps the chute, the pool and the stream bend within the brief's limits.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { validate, SURFACES, surfaceId, FLOW_MAX } from '../track/schema.js';
import { flowAt, makeQuery, copyQuery } from '../track/query.js';
import { loadTrack } from '../track/load.js';
import { build, checks, PIECES } from '../dev/gen-threshold.mjs';

function ring(n = 12, r = 30) {
  const nodes = [];
  for (let i = 0; i < n; i++) {
    const a = (i / n) * Math.PI * 2;
    nodes.push({ p: [Math.cos(a) * r, 0, Math.sin(a) * r], roll: 0, w: 12, flags: ['rail'], speed: 1, surface: 'glass' });
  }
  nodes[0].flags = ['start', 'rail'];
  return {
    format: 'glassroad-track', version: 1, id: 'ring', name: { en: 'Ring' }, author: 'ccl', created: '2026-10-23',
    loop: true, laps: 1, units: 'm', nodes,
    sections: [{ id: 'a', from: 0, to: n, act: 'induction', palette: 'dawn', sky: 'spiral-slow', bandSpeed: 0.6 }],
    checkpoints: [0, n >> 1], spawn: { node: 0, grid: [[-3, -4], [3, -6]] },
    items: [], boosts: [], jumps: [], hazards: [], media: [],
    ai: { lines: [], pace: [1] }, beat: { bpm: null, offsetMs: 0, map: [] },
    limits: { fallY: -40, edgeGraceM: 1.2, edgeGraceS: 0.8 },
  };
}

test('schema: stream and pool are surfaces, ids 0..4 in order', () => {
  assert.deepEqual([...SURFACES], ['glass', 'grip', 'haze', 'stream', 'pool']);
  assert.deepEqual(['glass', 'grip', 'haze', 'stream', 'pool'].map(surfaceId), [0, 1, 2, 3, 4]);
  assert.equal(surfaceId('nope'), 0);
  assert.deepEqual([...FLOW_MAX], [12, 6]);
});

test('schema: accepts a stream with a flow and a pool, warns on a flowless stream', () => {
  const t = ring();
  t.nodes[3].surface = 'stream'; t.nodes[3].flow = [5, -2.5];
  t.nodes[4].surface = 'stream'; t.nodes[4].flow = [12, 6];
  t.nodes[5].surface = 'pool';
  let r = validate(t);
  assert.equal(r.ok, true, r.errors.join('; '));
  assert.ok(!r.warnings.some((w) => /flow/.test(w)));
  t.nodes[6].surface = 'stream';
  r = validate(t);
  assert.equal(r.ok, true);
  assert.ok(r.warnings.some((w) => /nodes\[6\] stream node with no flow/.test(w)), r.warnings.join('; '));
});

test('schema: rejects a flow off a stream, out of bounds or malformed', () => {
  const bad = (mut, re) => {
    const t = ring(); mut(t);
    const r = validate(t);
    assert.equal(r.ok, false);
    assert.ok(r.errors.some((e) => re.test(e)), r.errors.join('; '));
  };
  bad((t) => { t.nodes[2].flow = [3, 0]; }, /flow is only legal on a stream/);
  bad((t) => { t.nodes[2].surface = 'pool'; t.nodes[2].flow = [3, 0]; }, /flow is only legal on a stream/);
  bad((t) => { t.nodes[2].surface = 'stream'; t.nodes[2].flow = [12.5, 0]; }, /flow outside/);
  bad((t) => { t.nodes[2].surface = 'stream'; t.nodes[2].flow = [0, -6.5]; }, /flow outside/);
  bad((t) => { t.nodes[2].surface = 'stream'; t.nodes[2].flow = [1]; }, /flow must be/);
  bad((t) => { t.nodes[2].surface = 'stream'; t.nodes[2].flow = [1, 'x']; }, /flow must be/);
  bad((t) => { t.nodes[2].surface = 'lake'; }, /surface must be one of/);
});

test('query: flowAt lerps along a stream run and is zero off it; the result owns its array', () => {
  const nodes = [
    { surface: 'glass' }, { surface: 'stream', flow: [2, -1] }, { surface: 'stream', flow: [6, -3] }, { surface: 'stream', flow: [6, -3] }, { surface: 'pool' }, { surface: 'glass' },
  ];
  assert.deepEqual(flowAt(nodes, 0, 0.5), [0, 0]);
  assert.deepEqual(flowAt(nodes, 4, 0.5), [0, 0]);
  assert.deepEqual(flowAt(nodes, 1, 0), [2, -1]);
  assert.deepEqual(flowAt(nodes, 1, 0.5), [4, -2]);
  assert.deepEqual(flowAt(nodes, 2, 0.25), [6, -3]);
  assert.deepEqual(flowAt(nodes, 3, 0.5), [3, -1.5]);   // eases out toward the pool
  assert.deepEqual(flowAt(nodes, 3, 1), [0, 0]);
  const q = makeQuery();
  assert.deepEqual(q.flow, [0, 0]);
  const o = makeQuery();
  q.flow[0] = 4; q.flow[1] = 1;
  copyQuery(o, q);
  assert.deepEqual(o.flow, [4, 1]);
  assert.notEqual(o.flow, q.flow);
  const legacy = { ...makeQuery() }; delete legacy.flow;
  assert.deepEqual(copyQuery(makeQuery(), legacy).flow, [0, 0]);
});

test('query: a loaded ring reports stream flow between its nodes and none elsewhere', () => {
  const t = ring();
  for (const i of [3, 4, 5]) { t.nodes[i].surface = 'stream'; t.nodes[i].flow = [4, 2]; }
  t.nodes[6].surface = 'pool';
  const tr = loadTrack(t);
  const probe = (i, u) => { const f = tr.frameAt(tr.nodeS[i] + (tr.nodeS[i + 1] - tr.nodeS[i]) * u); return tr.query(f.p[0], f.p[1] + 0.2, f.p[2], i); };
  let q = probe(1, 0.5);
  assert.equal(q.surface, 'glass'); assert.deepEqual(q.flow, [0, 0]);
  q = probe(4, 0.5);
  assert.equal(q.surface, 'stream');
  assert.ok(Math.abs(q.flow[0] - 4) < 1e-6 && Math.abs(q.flow[1] - 2) < 1e-6, 'full flow mid run ' + q.flow);
  q = probe(5, 0.5);
  assert.ok(Math.abs(q.flow[0] - 2) < 0.05 && Math.abs(q.flow[1] - 1) < 0.05, 'half flow on the last stream segment ' + q.flow);
  q = probe(6, 0.3);
  assert.equal(q.surface, 'pool'); assert.deepEqual(q.flow, [0, 0]);
  const fo = [9, 9];
  assert.equal(tr.flowAt(4, 0.5, fo), fo);
  assert.deepEqual(fo, [4, 2]);
  assert.equal(tr.nodeSurface(6), 'pool');
});

const json = JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8'));
const tr = loadTrack(json);

test('mesh: the road carries surface and flow per vertex, after the existing attributes, triangle count unchanged', () => {
  const m = tr.meshes.road, V = m.positions.length / 3;
  assert.deepEqual(Object.keys(m).slice(0, 4), ['positions', 'normals', 'uvs', 'indices']);
  assert.ok(m.surface instanceof Float32Array && m.surface.length === V, 'surface attribute');
  assert.ok(m.flow instanceof Float32Array && m.flow.length === V * 2, 'flow attribute');
  for (const k of ['rails', 'pads', 'jumps', 'edgeStrip', 'edgeHaze', 'railCaps', 'routeEdge', 'routeLand']) assert.equal(tr.meshes[k].surface, undefined, k + ' keeps four arrays');
  let stream = 0, pool = 0, flowing = 0;
  for (let v = 0; v < V; v++) {
    const sf = m.surface[v];
    assert.ok(sf >= 0 && sf <= 4 && Number.isInteger(sf), 'surface id ' + sf);
    const q = tr.query(m.positions[v * 3], m.positions[v * 3 + 1], m.positions[v * 3 + 2]);
    if (Math.abs(q.height) > 0.3) continue;      // the underside and skirts share their row's values
    if (q.segT < 1e-3 || q.segT > 1 - 1e-3) continue;   // a row on a node boundary belongs to the segment ahead
    assert.equal(sf, surfaceId(q.surface), 'vertex ' + v + ' surface ' + q.surface + ' at s ' + q.s.toFixed(1));
    if (sf === 3) { stream++; assert.ok(Math.abs(m.flow[v * 2] - q.flow[0]) < 0.05 && Math.abs(m.flow[v * 2 + 1] - q.flow[1]) < 0.05, 'flow at s ' + q.s.toFixed(1) + ': ' + m.flow[v * 2] + ',' + m.flow[v * 2 + 1] + ' vs ' + q.flow); }
    if (sf === 4) pool++;
    if (m.flow[v * 2] || m.flow[v * 2 + 1]) { flowing++; assert.equal(sf, 3, 'flow only on stream vertices'); }
  }
  assert.ok(stream > 40 && pool > 20 && flowing > 40, 'stream ' + stream + ' pool ' + pool + ' flowing ' + flowing);
});

test('gen: Threshold water invariants (stream bend, chute, pool, closure, the dare and the court)', () => {
  const built = build(PIECES.map((p) => ({ ...p })));
  const { errs, info } = checks(built);
  assert.deepEqual(errs, []);
  assert.ok(info.water.chuteGradeDeg <= 45 && info.water.chuteGradeDeg > 35, 'chute grade ' + info.water.chuteGradeDeg);
  const ids = built.meta.map((m) => built.pieces[m.piece].id), nodes = built.track.nodes;
  const of = (id) => nodes.map((n, i) => (ids[i] === id ? n : null)).filter(Boolean);
  // the first sweep from u 0.15 and all of L2 are a stream with a ramped flow, the hairpin stays glass
  const sweep = built.meta.map((m, i) => (ids[i] === 'sweepA' ? [m.u, nodes[i]] : null)).filter(Boolean);
  for (const [u, n] of sweep) assert.equal(n.surface, u >= 0.15 ? 'stream' : 'glass', 'sweepA u ' + u);
  for (const n of of('L2')) assert.equal(n.surface, 'stream');
  for (const n of of('hairpin')) assert.equal(n.surface, 'glass');
  const run = nodes.filter((n, i) => (ids[i] === 'sweepA' || ids[i] === 'L2') && n.surface === 'stream').map((n) => n.flow);
  assert.equal(run.length, 7);
  assert.deepEqual(run[0], [1.67, -0.83]); assert.deepEqual(run[1], [3.33, -1.67]); assert.deepEqual(run[2], [5, -2.5]);
  assert.deepEqual(run[run.length - 2], [3.33, -1.67]); assert.deepEqual(run[run.length - 1], [1.67, -0.83]);
  // the chute: stream, flow [8, 0], speed 1.3, falls 14 m; the pool: 20 m wide, still, at least 35 m; L4b closes
  for (const n of of('chute')) { assert.equal(n.surface, 'stream'); assert.deepEqual(n.flow, [8, 0]); assert.equal(n.speed, 1.3); }
  for (const n of of('pool')) { assert.equal(n.surface, 'pool'); assert.equal(n.w, 20); assert.equal(n.flow, undefined); }
  const chute = PIECES.find((p) => p.id === 'chute'), pool = PIECES.find((p) => p.id === 'pool');
  assert.equal(chute.len, 24); assert.ok(pool.len >= 35);
  assert.ok(built.closure.closeErr < 0.01 && Math.abs(built.closure.headErr % 360) < 0.01);
  const l4b = built.pieces.find((p) => p.id === 'L4b');
  assert.ok(l4b.auto && l4b.len >= 30, 'L4b ' + l4b.len);
  assert.ok(info.lengthM <= 1687 && info.lengthM >= 1150, 'length ' + info.lengthM);
  // the dare still runs c1 -> c3 and the court sits on L4d; no item sits on the chute
  const dare = built.track.routes.find((r) => r.id === 'dare');
  assert.ok(dare && ids[dare.from] === 'c1' && ids[dare.land.from] === 'c3');
  assert.ok(built.track.courts[0].from === ids.indexOf('L4d'));
  for (const it of built.track.items) assert.notEqual(ids[it.node], 'chute', 'item on the chute at node ' + it.node);
  // the committed file is this output
  assert.deepEqual(json.nodes, JSON.parse(JSON.stringify(nodes)));
});

test('gen: the water checks fire on a steep chute, a short pool and a flowless stream', () => {
  const steep = PIECES.map((p) => (p.id === 'chute' ? { ...p, len: 14 } : { ...p }));
  assert.ok(checks(build(steep)).errs.some((e) => /chute grade/.test(e)));
  const short = PIECES.map((p) => (p.id === 'pool' ? { ...p, len: 30 } : { ...p }));
  assert.ok(checks(build(short)).errs.some((e) => /pool 30 m is under/.test(e)));
  const dry = PIECES.map((p) => (p.id === 'chute' ? { ...p, flow: undefined } : { ...p }));
  assert.ok(checks(build(dry)).errs.some((e) => /has no flow/.test(e)));
});
