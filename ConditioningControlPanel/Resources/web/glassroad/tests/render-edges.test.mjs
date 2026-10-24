/* tests/render-edges.test.mjs - the edge language (EDGES lane, owner 2026-10-23: "it's hard to tell where the
 * guardrails are and where they are not") and the drop shortcut's signposting, checked on the real Threshold
 * MeshData (track/mesh.js) and the pure numbers (render/edgesMath.js). Three-free.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { EDGE_MESH } from '../track/mesh.js';
import { EDGE_LOOK, EDGE_KIND, mergeEdgeData, stripeAt } from '../render/edgesMath.js';
import { LOOK } from '../render/look.js';
import { wrap } from '../core/math.js';

const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8')));
const M = track.meshes, L = track.lengthM, R0 = track.routes[0];
const verts = (m) => { const out = []; for (let i = 0; i < m.positions.length / 3; i++) out.push([m.positions[i * 3], m.positions[i * 3 + 1], m.positions[i * 3 + 2], m.uvs[i * 2], m.uvs[i * 2 + 1]]); return out; };
const railedAt = (q) => q.railed || (track.routes || []).some((r) => q.seg >= r.from && q.seg < r.to && (r.side === 'L' ? q.lateral < 0 : q.lateral >= 0) && (q.s < r.openS0 || q.s > r.openS1));

test('edges: the rail is a 1.2 m barrier and the render reads the same height', () => {
  assert.equal(LOOK.railHeightM, 1.2);
  let top = 0;
  for (const v of verts(M.rails)) top = Math.max(top, v[3]);
  assert.ok(Math.abs(top - 1.2) < 1e-4, 'rail top ' + top);
  const B = EDGE_LOOK.rail;
  assert.ok(B.bodyAlpha >= 0.25 && B.topAlpha >= 0.9 && B.postAlpha >= 0.7, 'a solid barrier');
  assert.ok(B.postM >= 2 && B.postM <= 4, 'posts at a steady rhythm');
});

test('edges: the hazard strip and lip lie on the road along open edges only, never beside a rail', () => {
  const v = verts(M.edgeStrip);
  assert.ok(v.length > 60);   // the stone field (wave 2) paints no strip along its 43 m: the lips are the edges there
  let openRun = 0;
  for (const [x, y, z, u] of v) {
    const q = track.query(x, y, z);
    assert.ok(Math.abs(q.height - 0.02) < 0.02, 'on the road ' + q.height.toFixed(3));
    assert.ok(q.edgeDist > -0.01 && q.edgeDist < EDGE_MESH.stripM + 0.01, 'within the strip of an edge ' + q.edgeDist.toFixed(2));
    assert.ok(Math.abs(q.edgeDist - u) < 0.02, 'u = metres in from the edge');
    const runEnd = Math.abs(q.s - R0.openS0) < 1 || Math.abs(q.s - R0.openS1) < 1;   // a run's last row meets the rail
    if (u < 0.01) { assert.ok(!railedAt(q) || runEnd || q.segT < 1e-3 || q.segT > 1 - 1e-3, 'strip beside a rail at s ' + q.s.toFixed(1)); openRun++; }
  }
  // the open corner both sides, and the drop shortcut's opening
  const lipS = v.filter((p) => p[3] < 0.01).map((p) => p[4]);
  assert.ok(lipS.some((s) => s >= R0.openS0 - 0.5 && s <= R0.openS1 + 0.5), 'the shortcut opening is an open edge');
  assert.ok(lipS.some((s) => s > track.jumps[0].landS && s < track.lengthM - 50), 'the open corner');
});

test('edges: a haze curtain falls away below every open lip', () => {
  const v = verts(M.edgeHaze);
  assert.equal(v.length, verts(M.edgeStrip).length, 'one curtain row per strip row');
  // measured in the frame of the row's own s (a plain probe from far below can find another ring of the road)
  for (const [x, y, z, u, s] of v) {
    const f = track.frameAt(s), d = [x - f.p[0], y - f.p[1], z - f.p[2]];
    const h = d[0] * f.n[0] + d[1] * f.n[1] + d[2] * f.n[2], lat = Math.abs(d[0] * f.b[0] + d[1] * f.b[1] + d[2] * f.b[2]);
    if (u === 0) assert.ok(Math.abs(h) < 0.01 && lat > f.w / 2 && lat < f.w / 2 + 0.1, 'hangs from the lip');
    else { assert.ok(Math.abs(u - EDGE_MESH.hazeM) < 1e-6); assert.ok(Math.abs(h + EDGE_MESH.hazeM) < 0.01 && lat > f.w / 2 + EDGE_MESH.hazeOutM - 0.1, 'falls away below the road'); }
  }
});

test('edges: a tall end post wherever a rail run stops, none at the shortcut opening (its arch posts stand there)', () => {
  const v = verts(M.railCaps), n = v.length / 20;
  assert.ok(n >= 6, 'end posts ' + n);
  for (let i = 0; i < n; i++) {
    const q = track.query(v[i * 20][0], v[i * 20][1], v[i * 20][2]);
    assert.ok(q.edgeDist < 0.2 && q.edgeDist > -0.5, 'on the rail line ' + q.edgeDist.toFixed(2));
    assert.ok(Math.abs(q.s - R0.openS0) > EDGE_MESH.capSkipM - 1 && Math.abs(q.s - R0.openS1) > EDGE_MESH.capSkipM - 1);
  }
  const ends = v.filter((p) => p[3] === 1).length;
  assert.ok(ends >= n * 4, 'a lit top on every post');
  assert.ok(EDGE_MESH.capH > LOOK.railHeightM * 1.5, 'taller than the rail');
});

test('edges: one draw for the whole language, kinds kept per vertex', () => {
  const d = mergeEdgeData(M);
  const V = d.positions.length / 3;
  assert.equal(d.kinds.length, V);
  assert.equal(d.indices.length / 3, d.counts.strip + d.counts.haze + d.counts.cap);
  for (const i of d.indices) assert.ok(i < V);
  const nStrip = M.edgeStrip.positions.length / 3;
  assert.equal(d.kinds[0], EDGE_KIND.strip);
  assert.equal(d.kinds[nStrip], EDGE_KIND.haze);
  assert.equal(d.kinds[V - 1], EDGE_KIND.cap);
  assert.equal(mergeEdgeData({}), null);
});

test('edges: the hazard pattern alternates at a steady period and stays calm near the eye and under Gentle', () => {
  const E = EDGE_LOOK;
  let swaps = 0, prev = stripeAt(0.6, 0);
  for (let s = 0.05; s <= E.stripePeriodM * 4; s += 0.05) { const k = stripeAt(0.6, s); if (k !== prev) swaps++; prev = k; }
  assert.equal(swaps, 8, 'two swaps a period');
  // a world-fixed pattern near the camera passes fast: its contrast is cut there, and melted where dense
  assert.ok(E.nearContrast <= 0.4 && E.nearM[1] >= 10);
  assert.ok(E.denseFrom[1] <= 0.5);
  assert.ok(E.hazeDriftMps / E.hazeStreakM < 1, 'haze streaks drift well under 1 Hz');
  assert.ok(E.gentleContrast >= 0.5 && E.gentleContrast < 1, 'Gentle is calmer, never hidden');
  assert.ok(E.rail.postDense[1] <= 0.5, 'far posts melt to their average instead of shimmering');
});

test('shortcut signs: floor chevrons lead in from about 80 m, an arch frames the opening, a kicker lip on the edge', () => {
  const v = verts(M.routeEdge);
  const side = R0.side === 'L' ? -1 : 1;
  const before = [], arch = [], lip = [];
  for (const [x, y, z, u, g] of v) {
    const q = track.query(x, y, z, R0.from);
    const d = wrap(R0.openS0 - q.s, L);
    if (d > 10 && d < 90 && Math.abs(q.height - 0.02) < 0.03) before.push([d, q.lateral]);
    if (q.s >= R0.openS0 - 0.1 && q.s <= R0.openS1 + 0.1 && q.height > 3.5) arch.push(q.height);
    if (q.s >= R0.openS0 - 0.1 && q.s <= R0.openS1 + 0.1 && q.height > 0.1 && q.height < 0.3 && Math.abs(q.edgeDist) < 0.05) lip.push(q.s);
    void u; void g;
  }
  const far = Math.max(...before.map((p) => p[0]));
  assert.ok(far >= 75, 'chevrons from ' + far.toFixed(0) + ' m out');
  // they drift toward the open side as the opening nears
  const nearest = before.filter((p) => p[0] < 25).map((p) => p[1] * side), farthest = before.filter((p) => p[0] > 70).map((p) => p[1] * side);
  assert.ok(Math.max(...nearest) > Math.max(...farthest) + 1, 'the chevrons lead out toward the opening');
  assert.ok(Math.max(...arch) > 5, 'the arch stands over the opening');
  assert.ok(Math.max(...lip) - Math.min(...lip) > (R0.openS1 - R0.openS0) * 0.8, 'the kicker lip runs the opening');
});
