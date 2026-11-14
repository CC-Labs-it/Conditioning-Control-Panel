// The Keep jump ramps: every lip lands on open, supported road at real racing speeds.
// The ballistic checker lives here on purpose (the ramp sim is built in a parallel lane).
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { getKeepLayout } from '../track/keep-layout.js';
import { phraseGateLayout } from '../track/phraseGates.js';
import { movingPlan } from '../track/movingHazards.js';
import { BOOST_TURN } from '../track/boostSafety.js';

const json = JSON.parse(readFileSync(new URL('../tracks/keep.json', import.meta.url), 'utf8'));
const feel = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
const track = loadTrack(json);
const layout = getKeepLayout(track), L = track.lengthM;
const GRAV = feel.hop.gravityMps2, DT = 1 / 120, SPEEDS = [40, 50, 60, 66.7];
const ahead = (from, s) => ((s - from) % L + L) % L;
const yaw = (t) => Math.atan2(t[2], t[0]);
const turn = (a, b) => { let d = b - a; while (d > Math.PI) d -= 2 * Math.PI; while (d < -Math.PI) d += 2 * Math.PI; return d; };

// Stretches the run-up, the carry and the first 10 m after touchdown must never touch.
const BLOCKERS = [];
const block = (id, a, b) => BLOCKERS.push({ id, a, b });
for (const h of layout.halls) block('hall ' + h.id, h.s0, h.s1);
for (const h of layout.architecture.halls) block('roofed ' + h.id, h.s0, h.s1);
for (const g of [...layout.gates, ...layout.chains]) block(g.id, g.s - 15, g.s + 15);
for (const row of movingPlan(track).traps) block(row.id, row.s - row.len / 2 - 10, row.s + row.len / 2 + 10);
for (const c of track.courts) for (const h of c.holes) block(h.id, h.s - 8, h.s + h.len + 8);
for (const j of track.jumps) block('jump', j.s - 40, j.landS + 20);
for (const a of track.aerials) block('aerial', a.rampS0 - 20, a.landingS1 + 20);
for (const f of track.fields) block('field ' + f.id, f.s0 ?? f.fromS, f.s1 ?? f.toS);
for (const b of track.boosts) block('boost carry', b.s - 4, b.s + b.len + BOOST_TURN.rolloutM);
for (const p of phraseGateLayout(track)) block(p.id, p.s - 25, p.s + 100);
for (const c of track.checklines) block('checkpoint', c.s - 5, c.s + 25);
block('grid', L - 45, L); block('grid', 0, 60);
const hits = (a, b) => BLOCKERS.filter(k => [-L, 0, L].some(sh => a <= k.b + sh && b >= k.a + sh)).map(k => k.id);

const DECK = [];
for (let s = 0; s < L; s += 2) { const f = track.frameAt(s); DECK.push({ s, p: f.p.slice(), t: f.t.slice(), b: f.b.slice(), w: f.w }); }
const SOLIDS = [...layout.castleBlocks, ...layout.architecture.courts, ...layout.architecture.ruins];

/** Integrate the launch from the lip (wedge top) until it meets the road below. */
function flight(ramp, speed, headingDeg = 0) {
  const sLip = track.nodeS[ramp.node], f = track.frameAt(sLip), issues = [];
  const P = [0, 1, 2].map(k => f.p[k] + f.b[k] * ramp.x + f.n[k] * ramp.heightM);
  const c = Math.cos(headingDeg * Math.PI / 180), sn = Math.sin(headingDeg * Math.PI / 180);
  const V = [0, 1, 2].map(k => (f.t[k] * c + f.b[k] * sn) * speed);
  V[1] = Math.max(V[1], ramp.upMps);
  const near = DECK.filter(d => Math.hypot(d.p[0] - f.p[0], d.p[2] - f.p[2]) < speed * 3.5 + 40);
  let seg = f.seg, apexY = P[1], exit = null, path = [];
  for (let t = DT; t < 6; t += DT) {
    V[1] -= GRAV * DT; for (let k = 0; k < 3; k++) P[k] += V[k] * DT; apexY = Math.max(apexY, P[1]);
    path.push(P.slice());
    const q = track.query(P[0], P[1], P[2], seg); seg = q.seg;
    if (!exit && ahead(sLip, q.s) < 200 && q.edgeDist < 1.2) exit = { s: q.s, railed: q.railed };
    // No other deck, roof or masonry over the kart up to apex + clearance.
    for (const d of near) {
      const dy = d.p[1] - P[1];
      if (dy <= 0 || dy > 6 || ahead(d.s, q.s) < 30 || ahead(q.s, d.s) < 30) continue;
      const w0 = P[0] - d.p[0], w2 = P[2] - d.p[2];
      if (Math.abs(w0 * d.t[0] + w2 * d.t[2]) <= 1.2 && Math.abs(w0 * d.b[0] + w2 * d.b[2]) <= d.w / 2 + 1) { issues.push('deck overhead at ' + d.s); break; }
    }
    for (const blk of SOLIDS) if (Math.abs(P[0] - blk.x) < blk.w / 2 + 2 && Math.abs(P[2] - blk.z) < blk.d / 2 + 2
      && P[1] < (blk.topY ?? blk.baseY + blk.wallM) + 2 && P[1] > blk.baseY - 2) issues.push('inside ' + blk.id);
    for (const tw of layout.towers) if (Math.hypot(P[0] - tw.x, P[2] - tw.z) < tw.radiusM + 2 && P[1] < tw.topY + 2) issues.push('tower');
    if (V[1] >= 0) continue;
    const b = track.query.below(P[0], P[1], P[2]);
    if (!b || b.height > 0) continue;
    const tf = track.frameAt(b.s), tf2 = track.frameAt(b.s + 50);
    const radius = 50 / Math.max(1e-6, Math.abs(turn(yaw(tf.t), yaw(tf2.t))));
    const misDeg = Math.abs(turn(yaw(V), yaw(tf.t))) * 180 / Math.PI;
    if (b.hole) issues.push('hole');
    if (b.edgeDist < 2) issues.push('lands ' + b.edgeDist.toFixed(2) + ' m from the edge');
    if (radius < 120) issues.push('touchdown bend radius ' + radius.toFixed(0));
    // The kart's own line keeps 1 m of road for the next 10 m (no rail, edge or hole).
    const flat = Math.hypot(V[0], V[2]);
    for (let d = 0; d <= 10; d++) {
      const q2 = track.query(P[0] + V[0] / flat * d, tf.p[1] + 1, P[2] + V[2] / flat * d, b.seg);
      if (q2.edgeDist < 1 || q2.hole) { issues.push('runout leaves the road ' + d + ' m after touchdown'); break; }
    }
    return { speed, airS: t, landS: b.s, dist: ahead(sLip, b.s), lat: b.lateral, edge: b.edgeDist, misDeg, radius,
      apexM: apexY - f.p[1], exit, path, issues };
  }
  return { speed, issues: ['no touchdown within 6 s'] };
}

const ramps = json.ramps;
test('Keep carries 3-4 contract ramps with an item and a cut/air ramp, none in a route mouth', () => {
  assert.ok(ramps.length >= 3 && ramps.length <= 4);
  for (const r of ramps) {
    assert.match(r.id, /^keep-ramp-/);
    assert.ok(Number.isInteger(r.node) && r.node > 0 && r.node < json.nodes.length);
    assert.ok(r.widthM >= 4 && r.widthM <= 8 && r.lenM >= 5 && r.lenM <= 9 && r.heightM >= .8 && r.heightM <= 1.6, r.id);
    assert.ok(r.upMps >= 11 && r.upMps <= 16, r.id);
    assert.ok(['shortcut', 'item', 'cut', 'air'].includes(r.purpose) && r.note.length > 10, r.id);
    const f = track.frameAt(track.nodeS[r.node]);
    assert.ok(Math.abs(r.x) + r.widthM / 2 <= f.w / 2 - 1, r.id + ' wedge fits the road');
  }
  assert.ok(ramps.some(r => r.purpose === 'item'), 'item');
  for (const r of ramps) for (const route of track.routes) {
    const s0 = track.nodeS[r.node];
    assert.ok(!(s0 >= route.openS0 - 10 && s0 - r.lenM <= route.openS1 + 10), r.id + ' clear of ' + route.id + ' mouth');
  }
  assert.ok(ramps.some(r => r.purpose === 'cut' || r.purpose === 'air'));
});

for (const ramp of ramps) for (const speed of SPEEDS) test(ramp.id + ' lands clean at ' + speed + ' m/s', (t) => {
  const r = flight(ramp, speed);
  if (r.airS) t.diagnostic('air ' + r.airS.toFixed(2) + ' s, ' + r.dist.toFixed(1) + ' m, lat ' + r.lat.toFixed(1) + ', apex +' + (ramp.upMps ** 2 / (2 * GRAV)).toFixed(1) + ' m, ' + r.misDeg.toFixed(0) + ' deg');
  assert.deepEqual(r.issues, []);
  assert.ok(r.dist > 15 && r.dist < 120, 'touchdown ahead on the same road, ' + r.dist.toFixed(1) + ' m');
  assert.ok(r.airS >= .7 && r.airS <= 1.25, 'air ' + r.airS.toFixed(2) + ' s');
  assert.ok(r.misDeg <= 25, 'heading ' + r.misDeg.toFixed(1) + ' deg off the road at touchdown');
  assert.ok(!r.exit, 'never crosses the road edge margin in the air');
  const s0 = track.nodeS[ramp.node];
  assert.deepEqual(hits(s0 - ramp.lenM - 5, s0 + r.dist + 10), [], 'run-up, carry and runout clear of hazards');
});

test('keep-ramp-rampart arc passes its floating boxes', () => {
  const ramp = ramps.find(r => r.purpose === 'item');
  const lifted = track.items.map((it, i) => ({ it, src: json.items[i] })).filter(v => v.src.liftM);
  assert.ok(lifted.length >= 2 && lifted.length <= 3);
  const boxes = lifted.map(({ it, src }) => {
    const f = track.frameAt(it.s);
    assert.equal(src.kind, 'box');
    assert.ok(src.liftM > feel.items.pickHeightM + .5, 'out of reach from the ground');
    return [0, 1, 2].map(k => f.p[k] + f.b[k] * it.x + f.n[k] * src.liftM);
  });
  for (const speed of [50, 60, 66.7]) {
    const { path } = flight(ramp, speed);
    const caught = boxes.filter(b => path.some(p => Math.hypot(p[0] - b[0], p[1] - b[1], p[2] - b[2]) <= feel.items.boxRadiusM)).length;
    assert.ok(caught >= 2, speed + ' m/s passes ' + caught + ' boxes');
  }
});
