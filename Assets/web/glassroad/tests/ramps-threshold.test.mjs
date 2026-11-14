// tests/ramps-threshold.test.mjs - lane R1 (handoff-1030/GLASSROAD-RAMPS.md): Threshold's jump ramps keep the
// landing rule. Every ramp is flown ballistically from its lip (road tangent * speed, vertical speed
// max(tangent, upMps), gravity hop.gravityMps2, no steering) against the real loaded course (runtime shaping
// and moving traps on) at 40, 50, 60 and 66.7 m/s, and must come down on supported open road of its own
// stretch: inside the edges with room, never over a jump gap, a hole or a stone field, with no other deck in
// the arc, ten metres of straight runout on road and no turn sharper than the course's own sweeps. The
// corridor from the foot of the wedge to the furthest touchdown stays clear of jumps, aerial gaps, fields,
// courts, moving traps and the lens bridge, phrase gates (and the 100 m after them), runtime pads, route
// openings, checkpoints and the start. The floating boxes ride the item ramp's arc. The checker lives here
// on purpose: the ramp sim (lane R0) is built in parallel and nothing it adds is imported.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { movingPlan } from '../track/movingHazards.js';
import { phraseGateLayout } from '../track/phraseGates.js';

const json = JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8'));
const feel = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
// Schema support for `ramps` / `liftM` is lane R0's; the geometry below does not depend on it.
const track = loadTrack(json, { validate: false, moving: true });
const L = track.lengthM;
const GRAVITY = feel.hop.gravityMps2, DT = 1 / 240, SPEEDS = [40, 50, 60, 66.7];
const BOX_R = feel.items.boxRadiusM, PICK_H = feel.items.pickHeightM;
const wrapS = (s) => ((s % L) + L) % L;
const ahead = (from, s) => wrapS(s - from);
const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const heading = (t) => Math.atan2(t[0], t[2]);
const turnDeg = (s, len) => {
  let d = (heading(track.frameAt(s + len).t) - heading(track.frameAt(s).t)) * 180 / Math.PI;
  while (d > 180) d -= 360;
  while (d < -180) d += 360;
  return d;
};

// every 2 m of road, for the "no other deck inside the arc" check
const samples = [];
for (let s = 0; s < L; s += 2) { const f = track.frameAt(s); samples.push({ s, p: f.p.slice(), t: f.t.slice(), b: f.b.slice(), n: f.n.slice(), w: f.w }); }

/** Fly a ramp at one speed. Returns { airS, landS, landLat, landEdge, path, fails }. */
function fly(ramp, speed) {
  const lipS = track.nodeS[ramp.node], f = track.surfaceAt(lipS, ramp.x);
  const P = [0, 1, 2].map((k) => f.p[k] + f.n[k] * ramp.heightM), V = f.t.map((v) => v * speed);
  V[1] = Math.max(V[1], ramp.upMps);
  const fails = [], path = [];
  let hint = f.seg, t = 0, land = null;
  for (let i = 0; i < 5 / DT && !land; i++) {
    V[1] -= GRAVITY * DT;
    for (let k = 0; k < 3; k++) P[k] += V[k] * DT;
    t += DT;
    const q = track.query(P[0], P[1], P[2], hint);
    hint = q.seg;
    path.push({ t, p: P.slice(), s: q.s, lat: q.lateral, h: q.height });
    if (q.flags?.includes('jump')) { fails.push('over a jump gap at s ' + q.s.toFixed(1)); break; }
    if (q.edgeDist < 0) { fails.push('leaves its road at s ' + q.s.toFixed(1) + (q.railed ? ' into the rail' : '')); break; }
    if (q.height <= 0) {
      if (!q.onRoad) { fails.push('comes down in a hole or the stones at s ' + q.s.toFixed(1)); break; }
      land = { t, P: P.slice(), V: V.slice(), q: { ...q } };
    }
  }
  if (!land) { if (!fails.length) fails.push('never lands'); return { fails, path }; }
  const { q } = land;
  if (q.edgeDist < 3) fails.push('touchdown ' + q.edgeDist.toFixed(1) + ' m from the edge');
  if (q.stone || q.hole) fails.push('touchdown on a field');
  const turn = turnDeg(q.s, 30);
  if (Math.abs(turn) > 15) fails.push('touchdown in a ' + turn.toFixed(1) + ' deg / 30 m turn');
  // ten metres held straight along the landing heading: still on open road, no rail, no gap
  const hv = Math.hypot(land.V[0], land.V[2]);
  let h2 = q.seg;
  for (let d = 1; d <= 10; d++) {
    const r = track.query(land.P[0] + land.V[0] / hv * d, land.P[1] + 0.5, land.P[2] + land.V[2] / hv * d, h2);
    h2 = r.seg;
    if (!r.onRoad || r.flags?.includes('jump')) { fails.push('off supported road ' + d + ' m after touchdown'); break; }
  }
  // no other deck (overhead roof, a crossing road) within the kart's height plus clearance of the arc
  outer: for (const pt of path) for (const sm of samples) {
    const ds = Math.abs(wrapS(sm.s - pt.s));
    if (Math.min(ds, L - ds) < 25) continue;
    const rel = [pt.p[0] - sm.p[0], pt.p[1] - sm.p[1], pt.p[2] - sm.p[2]];
    if (Math.abs(dot(rel, sm.t)) > 1.2 || Math.abs(dot(rel, sm.b)) > sm.w / 2 + 1) continue;
    const h = dot(rel, sm.n);
    if (h > -4.5 && h < 0.5) { fails.push('another deck ' + (-h).toFixed(1) + ' m from the arc at s ' + pt.s.toFixed(1)); break outer; }
  }
  return { airS: land.t, landS: q.s, landLat: q.lateral, landEdge: q.edgeDist, turn, path, fails };
}

const ramps = json.ramps || [];
const flights = new Map(ramps.map((r) => [r.id, SPEEDS.map((v) => fly(r, v))]));

test('threshold ramps: three to four ramps on the contract, one item ramp, one cut or air', () => {
  assert.ok(ramps.length >= 3 && ramps.length <= 4, 'ramp count ' + ramps.length);
  assert.equal(new Set(ramps.map((r) => r.id)).size, ramps.length);
  for (const r of ramps) {
    assert.ok(Number.isInteger(r.node) && r.node >= 0 && r.node < json.nodes.length, r.id + ' node');
    assert.ok(r.widthM >= 4 && r.widthM <= 8 && r.lenM >= 5 && r.lenM <= 9, r.id + ' size');
    assert.ok(r.heightM >= 0.8 && r.heightM <= 1.6 && r.upMps >= 11 && r.upMps <= 16, r.id + ' lift');
    assert.ok(['shortcut', 'item', 'cut', 'air'].includes(r.purpose), r.id + ' purpose');
    assert.ok(typeof r.note === 'string' && r.note.length > 0, r.id + ' note');
    // the whole wedge sits on the road with a metre spare, from its foot to its lip
    for (const s of [track.nodeS[r.node] - r.lenM, track.nodeS[r.node]]) {
      const w = track.frameAt(s).w;
      assert.ok(Math.abs(r.x) + r.widthM / 2 <= w / 2 - 1, r.id + ' wedge off the road (w ' + w + ')');
    }
  }
  assert.ok(ramps.some((r) => r.purpose === 'item'));
  assert.ok(ramps.some((r) => r.purpose === 'cut' || r.purpose === 'air'));
});

test('threshold ramps: every ramp lands on open road of its own stretch at 40, 50, 60 and 66.7 m/s', () => {
  for (const r of ramps) flights.get(r.id).forEach((fl, i) => {
    assert.deepEqual(fl.fails, [], r.id + ' at ' + SPEEDS[i] + ' m/s');
    const carry = ahead(track.nodeS[r.node], fl.landS);
    assert.ok(carry > 20 && carry < 100, r.id + ' carry ' + carry.toFixed(1));
  });
});

test('threshold ramps: racing speeds get about a second of air (EMI\'s trick window)', () => {
  for (const r of ramps) flights.get(r.id).forEach((fl, i) => {
    if (SPEEDS[i] < 50) return;
    assert.ok(fl.airS >= 0.85 && fl.airS <= 1.25, r.id + ' air ' + fl.airS.toFixed(2) + ' s at ' + SPEEDS[i]);
  });
});

test('threshold ramps: wedge-to-landing corridors keep clear of every other course feature', () => {
  const spans = [];
  for (const j of track.jumps) spans.push([j.s - 30, j.landS + 30, 'jump ' + j.node]);
  for (const a of track.aerials) spans.push([a.rampS0 - 20, a.landingS1 + 20, a.id]);
  for (const f of track.fields) spans.push([f.fromS - 20, f.toS + 20, 'field ' + f.id]);
  for (const c of track.courts) spans.push([(c.s0 ?? c.fromS) - 20, (c.s1 ?? c.toS) + 20, 'court ' + c.id]);
  const moving = movingPlan(track);
  assert.ok(moving.traps.length > 0, 'the moving plan is live');
  for (const m of moving.traps) spans.push([m.s - 25, m.s + 25, 'trap ' + m.id]);
  for (const f of track.forks || []) spans.push([f.fromS - 20, f.toS + 20, 'fork ' + f.id]);
  for (const p of phraseGateLayout(track)) spans.push([p.s - 25, p.s + 100, 'phrase ' + p.text]);
  for (const b of track.boosts) spans.push([b.s - b.len / 2 - 10, b.s + b.len / 2 + 10, 'pad at node ' + b.node]);
  for (const rt of track.routes) spans.push([rt.openS0 - 10, rt.openS1 + 10, 'route ' + rt.id]);
  for (const c of json.checkpoints) spans.push([track.nodeS[c] - 10, track.nodeS[c] + 30, 'checkpoint ' + c]);
  spans.push([L - 60, L + 40, 'start grid']);
  for (const r of ramps) {
    const s0 = track.nodeS[r.node] - r.lenM - 5;
    const len = Math.max(...flights.get(r.id).map((fl) => ahead(s0, fl.landS))) + 10;
    for (const [a, b, what] of spans) {
      const hit = [-L, 0, L].some((o) => s0 < b + o && s0 + len > a + o);
      assert.ok(!hit, r.id + ' corridor s ' + s0.toFixed(0) + '+' + len.toFixed(0) + ' meets ' + what);
    }
    // no ground item under the wedge's lane
    for (const it of track.items) {
      if (json.items.find((e) => e.node === it.node && e.x === it.x && (e.ds || 0) === it.ds)?.liftM) continue;
      const d = ahead(s0, it.s);
      assert.ok(!(d < r.lenM + 10 && Math.abs(it.x - r.x) < r.widthM / 2 + 2), r.id + ' wedge over a ' + it.kind + ' at node ' + it.node);
    }
  }
  // ramps keep their distance from each other
  const lips = ramps.map((r) => track.nodeS[r.node]).sort((a, b) => a - b);
  lips.forEach((s, i) => assert.ok(ahead(s, lips[(i + 1) % lips.length]) > 300 || lips.length === 1, 'ramps closer than 300 m'));
});

test('threshold ramps: the floating boxes ride the item ramp arc and stay out of reach from the road', () => {
  const lifted = json.items.filter((e) => e.liftM);
  assert.ok(lifted.length >= 2 && lifted.length <= 3, 'two or three floating boxes');
  const itemRamp = ramps.find((r) => r.purpose === 'item');
  const fl = SPEEDS.map((v) => fly(itemRamp, v)).concat([fly(itemRamp, 55)]);
  for (const e of lifted) {
    assert.equal(e.kind, 'box');
    // out of a grounded kart's 3D pickup radius and its old flat pick height, with a metre to spare
    assert.ok(e.liftM >= Math.max(BOX_R, PICK_H) + 1, 'a grounded kart cannot reach a box at ' + e.liftM + ' m');
    const s = wrapS(track.nodeS[e.node] + (e.ds || 0)), f = track.surfaceAt(s, e.x);
    const c = [0, 1, 2].map((k) => f.p[k] + f.n[k] * e.liftM);
    const miss = (flight) => Math.min(...flight.path.map((pt) => Math.hypot(pt.p[0] - c[0], pt.p[1] - c[1], pt.p[2] - c[2])));
    // 50, 60, 66.7 and 55 m/s collect every box; 40 m/s may fall short of the last one
    for (const k of [1, 2, 3, 4]) assert.ok(miss(fl[k]) <= BOX_R, 'box at node ' + e.node + ' missed by ' + miss(fl[k]).toFixed(2) + ' m at speed index ' + k);
    // it hangs over the wedge's lane, between the lip and the touchdown
    const lip = track.nodeS[itemRamp.node];
    assert.ok(Math.abs(e.x - itemRamp.x) <= itemRamp.widthM / 2, 'box lane');
    assert.ok(ahead(lip, s) > 5 && ahead(lip, s) < ahead(lip, fl[0].landS), 'box between lip and landing');
  }
});

test('threshold ramps: floating boxes never move a runtime pad', () => {
  const ground = { ...json, items: json.items.filter((e) => !e.liftM) };
  const a = loadTrack(ground, { validate: false, moving: true }).boosts.map((b) => [b.node, b.x, b.s]);
  assert.deepEqual(track.boosts.map((b) => [b.node, b.x, b.s]), a);
});
