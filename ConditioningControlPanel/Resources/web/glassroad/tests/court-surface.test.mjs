import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { validate } from '../track/schema.js';
import { holeAt, holeDistance, supportIntervals } from '../track/courts.js';
import { copyQuery, makeQuery } from '../track/query.js';
import { S, RESPAWN, createState, setPos, setVel, setQuat } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { qfromBasis } from '../core/math.js';
import { createActions } from '../core/actions.js';
import { makeFeel, DT } from './sim-fake.mjs';
import { fixtureJson } from './sim-fixture-p6.mjs';

const json = JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8'));
const track = loadTrack(json);
assert.ok(track.courts.length, 'the committed course carries the court fixture');
const h = track.courts[0].holes[0];
const point = (t, s, x, up = 0) => {
  const f = t.frameAt(s);
  return [0, 1, 2].map((i) => f.p[i] + f.b[i] * x + f.n[i] * up);
};

test('court holes remove support, preserve outer rail distance, and copy cleanly', () => {
  const s = h.s + h.len / 2, p = point(track, s, h.x), q = track.query(...p);
  assert.equal(q.hole.id, h.id);
  assert.equal(q.onRoad, false);
  assert.ok(q.edgeDist > 7, 'interior hole is not an outer road edge');
  assert.ok(q.holeDist < -2.9);
  const out = copyQuery(makeQuery(), q);
  assert.equal(out.hole, q.hole); assert.equal(out.holeDist, q.holeDist);
  for (const x of [h.x - h.w / 2 - 0.1, h.x + h.w / 2 + 0.1]) assert.ok(track.query(...point(track, s, x)).onRoad);
  for (const ss of [h.s - 0.1, h.s + h.len + 0.1]) assert.ok(track.query(...point(track, ss, h.x)).onRoad);
  assert.equal(track.query.below(...point(track, s, h.x, 0.5)), null, 'no hidden support under this opening');
});

test('court intervals reserve kart clearance and ordinary tracks stay unchanged', () => {
  const s = h.s + 2, half = track.frameAt(s).w / 2, intervals = supportIntervals(track.courts, s, half, 0.6);
  assert.equal(intervals.length, 2);
  assert.ok(intervals[0][1] <= h.x - h.w / 2 - 0.6);
  assert.ok(intervals[1][0] >= h.x + h.w / 2 + 0.6);
  assert.equal(holeAt(track.courts, h.s - 0.4, h.x, 0.6), h);
  assert.ok(holeDistance(h, h.s - 1, h.x) > 0);
  const clean = structuredClone(json); delete clean.courts;
  const t = loadTrack(clean);
  assert.deepEqual(t.courts, []);
  assert.equal(t.query(...point(t, s, h.x)).onRoad, true);
});

test('court schema and load reject malformed or out-of-bounds apertures', () => {
  const bad = structuredClone(json); bad.courts[0].holes[0].w = -1;
  assert.equal(validate(bad).ok, false);
  bad.courts[0].holes[0].w = 100;
  assert.throws(() => loadTrack(bad), /interior supported/);
  bad.courts[0].holes[0].w = 6; bad.courts[0].holes[0].len = 1000;
  assert.throws(() => loadTrack(bad), /outside its court/);
});

function contains(a, b, c, s, x) {
  const cross = (u, v, p) => (v[0] - u[0]) * (p[1] - u[1]) - (v[1] - u[1]) * (p[0] - u[0]);
  const d = [cross(a, b, [s, x]), cross(b, c, [s, x]), cross(c, a, [s, x])];
  return d.every((v) => v >= -1e-5) || d.every((v) => v <= 1e-5);
}

test('rendered top and underside have no triangles across either opening', () => {
  const m = track.meshes.road;
  for (const hole of track.courts.flatMap((c) => c.holes)) {
    let topBefore = false, bottomBefore = false;
    for (let i = 0; i < m.indices.length; i += 3) {
      const ids = [m.indices[i], m.indices[i + 1], m.indices[i + 2]];
      const verts = ids.map((v) => [m.uvs[v * 2 + 1] % track.lengthM, m.uvs[v * 2]]);
      const ny = ids.reduce((sum, v) => sum + m.normals[v * 3 + 1], 0) / 3;
      if (Math.abs(ny) < 0.95) continue;
      const [a, b, c] = verts;
      if (Math.max(a[0], b[0], c[0]) - Math.min(a[0], b[0], c[0]) > track.lengthM / 2) continue;
      if (contains(a, b, c, hole.s - 0.2, hole.x)) { if (ny > 0) topBefore = true; else bottomBefore = true; }
      if (Math.max(a[0], b[0], c[0]) < hole.s || Math.min(a[0], b[0], c[0]) > hole.s + hole.len) continue;
      for (let ds = 0.2; ds < hole.len; ds += 0.7) for (let dx = -hole.w / 2 + 0.2; dx < hole.w / 2; dx += 0.7)
        assert.equal(contains(a, b, c, hole.s + ds, hole.x + dx), false, 'triangle covers void at ' + (hole.s + ds));
    }
    assert.ok(topBefore && bottomBefore, 'both faces remain supported before the opening');
  }
});

function kartAt(t, s, x, speed) {
  const st = createState(), f = t.frameAt(s);
  setPos(st, point(t, s, x)); setVel(st, f.t.map((v) => v * speed));
  setQuat(st, qfromBasis([0, 0, 0, 1], f.b, f.n, f.t.map((v) => -v)));
  st[S.engineV] = speed; st[S.grounded] = 1; st[S.seg] = f.seg; st[S.latchSeg] = f.seg; st[S.s] = s; st[S.lateral] = x; st[S.hopT] = 100;
  return st;
}

test('driving into a hole drops and rescues promptly, including after passing the far lip', () => {
  for (const gentle of [false, true]) {
    const feel = makeFeel({ 'gentle.on': gentle }), st = kartAt(track, h.s - 0.5, h.x, 30), events = [];
    const a = createActions(); a.throttle = 1;
    for (let i = 0; i < 60 && st[S.respawnPhase] !== RESPAWN.PULL; i++) step(st, a, track, feel, DT, { emit: (name, p) => events.push({ name, p }) });
    assert.ok(events.some((e) => e.name === 'airborne'));
    assert.ok(events.some((e) => e.name === 'drop' && e.p.cause === 'hole'), JSON.stringify(events));
    assert.equal(st[S.respawnPhase], RESPAWN.PULL);
    assert.ok(st[S.s] > h.s + h.len, 'rescue remains armed after the far lip');
    assert.ok(!events.some((e) => e.name === 'wall'), 'hole is not a rail');
  }
});

test('lower-deck probe looks through an upper opening instead of returning its invisible floor', () => {
  const data = fixtureJson(), plain = loadTrack(data), q = plain.query(110, 14, -16);
  data.courts = [{ id: 'upper', from: q.seg, to: q.seg + 2, holes: [{ id: 'window', node: q.seg, ds: Math.max(0, q.s - plain.nodeS[q.seg] - 1), x: q.lateral, w: 3, len: 3 }] }];
  const t = loadTrack(data), hole = t.courts[0].holes[0], p = point(t, hole.s + 1.5, hole.x, 1), under = t.query.below(...p);
  assert.ok(under && under.p[1] < 1, 'lower road remains available through the aperture');
});
