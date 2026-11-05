/* Boudoir jump ramps (handoff-1030/GLASSROAD-RAMPS.md, lane R5): every authored ramp is flown as a ballistic
 * arc against the real loaded road (runtime shaping on) at 40, 50, 60 and 66.7 m/s, from the ramp's
 * centre and both edges, launched from the road and from the wedge top. The arc must stay over open road
 * clear of the edges, pass under no other deck, keep out of every hazard, jump, boost carry, fork mouth,
 * route mouth and checkpoint, and land on supported road that is not a sharp turn. The checker is local
 * to this file on purpose: the sim side of ramps is built by another lane. */
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { getBoudoirLayout } from '../track/boudoir-layout.js';
import { getBoudoirSetpieces } from '../track/boudoir-setpieces.js';
import { BOOST_TURN } from '../track/boostSafety.js';

const json = JSON.parse(readFileSync(new URL('../tracks/boudoir.json', import.meta.url), 'utf8'));
const feel = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
const track = loadTrack(json);
const L = track.lengthM, GRAV = feel.hop.gravityMps2, DT = 1 / 240, SPEEDS = [40, 50, 60, 66.7];
const BOX_R = feel.items.boxRadiusM, PICK_H = feel.items.pickHeightM;
const ramps = json.ramps || [];

// Keep-out stretches in runtime arc length, each [from, to, why].
const layout = getBoudoirLayout(track), setpieces = getBoudoirSetpieces(track, layout), keep = [];
const add = (a, b, why) => keep.push([a, b, why]);
add(L - 60, L + 1, 'start grid'); add(-1, 150, 'start grid');
json.checkpoints.forEach((n, i) => add(track.nodeS[n], track.nodeS[n] + 25, 'checkpoint ' + i));
for (const j of track.jumps) add(j.s - 60, j.landS + 40, 'jump');
for (const a of track.aerials) add(a.rampS0, a.landingS1, 'aerial ' + a.id);
for (const f of track.fields) add(f.fromS, f.toS, 'field');
for (const w of track.waterSpills) add(w.fromS - 10, w.toS + 10, 'water spill');
for (const p of track.forkPortals) add(p.s0 - 10, p.s1 + 10, 'fork mouth ' + p.id);
for (const r of track.routes) add(r.openS0 - 10, r.openS1 + 10, 'route mouth ' + r.id);
for (const b of track.boosts) add(b.s - 4, b.s + b.len + BOOST_TURN.forcedM, 'boost carry');
for (const p of setpieces.presses) add(p.s - 10, p.s + 10, p.id);
for (const p of setpieces.sweepers) add(p.s - 12, p.s + 12, p.id);
for (const p of setpieces.mirrorGates) add(p.s - 6, p.s + 6, p.id);
for (const c of setpieces.conveyors) add(c.s0, c.s1, c.id);
for (const h of layout.halls) add(h.s0, h.s1, 'hall ' + h.id);
for (const z of layout.perfume) add(z.s0, z.s1, 'perfume ' + z.id);
json.items.forEach((it, i) => { if (!it.liftM) { const s = track.nodeS[it.node] + (it.ds || 0); add(s - 4, s + 4, 'item ' + i); } });

// Every other deck (main lap and fork roads), sampled every 3 m, for the overhead / crossing check.
const decks = [];
for (let s = 0; s < L; s += 3) { const f = track.frameAt(s); decks.push({ s, x: f.p[0], y: f.p[1], z: f.p[2], half: f.w / 2, fork: null }); }
for (const fork of track.forks) for (const f of fork.samples(3)) decks.push({ s: fork.fromS, x: f.p[0], y: f.p[1], z: f.p[2], half: f.w / 2, fork: fork.id });
const arcDist = (a, b) => { const d = ((a - b) % L + L) % L; return Math.min(d, L - d); };
function deckNear(P, roadS) {
  for (const d of decks) {
    const dy = d.y - P[1];
    if (dy < (d.fork ? .5 : -1) || dy > 8) continue;           // apex + 2 m is under 8 m for every ramp here
    if (!d.fork && arcDist(d.s, roadS) < 40) continue;          // the kart's own road
    if (Math.hypot(d.x - P[0], d.z - P[2]) < d.half + 3) return d;
  }
  return null;
}

/** Fly one launch: lip of ramp r at lateral lat, h0 metres above the road, horizontal speed v. */
function fly(r, v, lat, h0) {
  const s0 = track.nodeS[r.node], f = track.frameAt(s0), surf = track.surfaceAt(s0, lat);
  const P = [0, 1, 2].map(k => surf.p[k] + f.n[k] * h0), V = [0, 1, 2].map(k => f.t[k] * v);
  V[1] = Math.max(V[1], r.upMps);
  const q = track.query(P[0], P[1], P[2], f.seg), path = [];
  let seg = q.seg, t = 0, apex = 0, step = 0;
  for (; t < 4; t += DT, step++) {
    V[1] -= GRAV * DT; for (let k = 0; k < 3; k++) P[k] += V[k] * DT;
    track.query(P[0], P[1], P[2], seg, q); seg = q.seg;
    path.push([P[0], P[1], P[2]]); apex = Math.max(apex, q.height);
    const where = ' at t ' + t.toFixed(2) + ' s ' + q.s.toFixed(0) + ' lat ' + q.lateral.toFixed(1);
    if (!q.onRoad || q.hole) return { fail: 'leaves the road' + where };
    if (q.flags?.includes('open') || q.flags?.includes('jump')) return { fail: 'flies over a gap' + where };
    if (q.edgeDist < 1) return { fail: 'within 1 m of the edge' + where };
    if (step % 12 === 0) { const d = deckNear(P, q.s); if (d) return { fail: 'another deck (' + (d.fork || 'main ' + d.s.toFixed(0)) + ') within the arc' + where }; }
    if (t > .05 && q.height <= 0) break;
  }
  if (q.height > 0) return { fail: 'never lands' };
  const sT = q.s, a = track.frameAt(sT), b = track.frameAt(sT + 20);
  const bend = Math.acos(Math.min(1, a.t[0] * b.t[0] + a.t[1] * b.t[1] + a.t[2] * b.t[2])) / 20, radius = bend > 1e-6 ? 1 / bend : Infinity;
  if (radius < 80) return { fail: 'lands in a ' + radius.toFixed(0) + ' m turn' };
  if (q.edgeDist < 2) return { fail: 'lands ' + q.edgeDist.toFixed(1) + ' m from the edge' };
  for (let d = 0; d <= 10; d += 2) { const fr = track.frameAt(sT + d); if (fr.flags.includes('open') || fr.flags.includes('jump')) return { fail: 'gap within 10 m of touchdown' }; }
  const lo = s0 - r.lenM - 5, hi = sT + 10, hit = keep.find(([x0, x1]) => hi >= x0 && lo <= x1);
  if (hit) return { fail: 'corridor ' + lo.toFixed(0) + '-' + hi.toFixed(0) + ' meets ' + hit[2] + ' ' + hit[0].toFixed(0) + '-' + hit[1].toFixed(0) };
  return { t, sT, dist: sT - s0, landLat: q.lateral, apex, path, radius };
}

test('Boudoir carries 3-4 ramps on the contract, with an item ramp, a cut and an air ramp', () => {
  assert.ok(ramps.length >= 3 && ramps.length <= 4, 'ramp count ' + ramps.length);
  assert.equal(new Set(ramps.map(r => r.id)).size, ramps.length);
  for (const r of ramps) {
    assert.ok(Number.isInteger(r.node) && r.node > 0 && r.node < json.nodes.length, r.id + ' node');
    assert.ok(r.widthM >= 4 && r.widthM <= 8 && r.lenM >= 5 && r.lenM <= 9 && r.heightM >= .8 && r.heightM <= 1.6, r.id + ' wedge size');
    assert.ok(r.upMps >= 11 && r.upMps <= 16, r.id + ' upMps');
    assert.ok(['shortcut', 'item', 'cut', 'air'].includes(r.purpose) && typeof r.note === 'string' && r.note.length > 10, r.id + ' purpose/note');
    assert.ok(Math.abs(r.x) + r.widthM / 2 <= json.nodes[r.node].w / 2 - 2, r.id + ' wedge on the road');
  }
  for (const purpose of ['item', 'cut', 'air']) assert.ok(ramps.some(r => r.purpose === purpose), 'needs a ' + purpose + ' ramp');
  // Ramps are spread over the lap, never stacked into one stretch.
  const lips = ramps.map(r => track.nodeS[r.node]).sort((a, b) => a - b);
  for (let i = 1; i < lips.length; i++) assert.ok(lips[i] - lips[i - 1] > 400, 'ramps too close: ' + lips.join(', '));
});

for (const r of ramps) test('Ramp ' + r.id + ' (' + r.purpose + ') lands clean at 40-66.7 m/s from every lane', () => {
  const rows = [];
  for (const v of SPEEDS) for (const lat of [r.x - r.widthM / 2 + .3, r.x, r.x + r.widthM / 2 - .3]) for (const h0 of [0, r.heightM]) {
    const res = fly(r, v, lat, h0);
    assert.ok(!res.fail, r.id + ' at ' + v + ' m/s, lane ' + lat.toFixed(1) + ', h0 ' + h0 + ': ' + res.fail);
    assert.ok(res.t > .75 && res.t < 1.3, r.id + ' air time ' + res.t.toFixed(2) + ' s at ' + v + ' m/s');
    rows.push({ v, lat, h0, ...res });
  }
  // The show: about a second of air at normal racing speed from the wedge top.
  const normal = rows.find(o => o.v === 50 && o.lat === r.x && o.h0 === r.heightM);
  assert.ok(normal.t >= .85 && normal.t <= 1.2, r.id + ' air at 50 m/s ' + normal.t.toFixed(2) + ' s');
  const span = k => Math.min(...rows.map(o => o[k])).toFixed(k === 't' ? 2 : 1) + '..' + Math.max(...rows.map(o => o[k])).toFixed(k === 't' ? 2 : 1);
  console.log(r.id, r.purpose, 'node', r.node, 'air', span('t'), 's, reach', span('dist'), 'm, landing lat', span('landLat'), 'm, apex', span('apex'), 'm');
});

test('The item ramp arc collects at least two floating boxes at every speed; driving under them misses', () => {
  const ramp = ramps.find(r => r.purpose === 'item'), lifted = json.items.filter(it => it.liftM);
  assert.ok(ramp && lifted.length >= 2 && lifted.length <= 3, 'item ramp with 2-3 lifted boxes');
  const boxes = lifted.map(it => {
    const s = track.nodeS[it.node] + (it.ds || 0), f = track.frameAt(s), p = track.surfaceAt(s, it.x).p;
    return [0, 1, 2].map(k => p[k] + f.n[k] * it.liftM);
  });
  const near = (P, B) => Math.hypot(P[0] - B[0], P[2] - B[2]) <= BOX_R && Math.abs(P[1] - B[1]) <= PICK_H;
  for (const v of SPEEDS) for (const h0 of [0, ramp.heightM]) {
    const res = fly(ramp, v, ramp.x, h0);
    assert.ok(!res.fail, res.fail);
    const got = boxes.filter(B => res.path.some(P => near(P, B))).length;
    assert.ok(got >= 2, 'arc at ' + v + ' m/s (h0 ' + h0 + ') collects ' + got + ' boxes');
  }
  // A kart that skips the ramp drives the same line on the road: every box is out of reach above it.
  for (const it of lifted) {
    const s = track.nodeS[it.node] + (it.ds || 0), f = track.frameAt(s);
    assert.ok(it.liftM - PICK_H > .5, 'grounded pass at ' + s.toFixed(0) + ' would pick the box');
    assert.ok(f.w / 2 - Math.abs(it.x) > 5, 'lifted box over the road');
  }
});
