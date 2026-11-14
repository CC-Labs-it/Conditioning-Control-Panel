// GOONWORKS jump ramps (handoff-1030/GLASSROAD-RAMPS.md): every lip is flown as a ballistic arc at 40, 50,
// 60 and 66.7 m/s against the real loaded course (runtime shaping on) and must come down on open road.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { getGoonworksLayout } from '../track/goonworks-layout.js';
import { goonworksSetpieces } from '../track/goonworks-setpieces.js';
import { phraseGateLayout } from '../track/phraseGates.js';
import { goonworksWorldPlan, goonworksBoxesIntersect } from '../render/goonworksWorld.js';

const json = JSON.parse(readFileSync(new URL('../tracks/goonworks.json', import.meta.url), 'utf8'));
const feel = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
const track = loadTrack(json, { moving: true });
const layout = getGoonworksLayout(track);
const GRAVITY = feel.hop.gravityMps2, DT = 1 / 240, SPEEDS = [40, 50, 60, 66.7];
const BOX_R = feel.items.boxRadiusM, PICK_H = feel.items.pickHeightM;
const L = track.lengthM;
const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const railOn = (flags, side) => flags.includes('rail') || flags.includes(side > 0 ? 'railR' : 'railL');

// Keep-out spans along the main road: [s0, s1, why]. A ramp's wedge, arc and first 10 m after touchdown avoid them.
const keep = [];
const add = (a, b, why) => keep.push([a, b, why]);
add(L - 60, L, 'start grid'); add(0, 120, 'start grid');
add(layout.tube.s0 - 40, layout.tube.s1 + 40, 'rabbit tube');
for (const id of ['grotto-gallery', 'mill-hall', 'quiz-ballroom', 'pinball-ramp', 'pinball-gap', 'pinball-landing', 'finish-underpass'])
  add(layout.byId[id].s0 - 20, layout.byId[id].s1 + 20, id);
for (const row of goonworksSetpieces(track, layout)) add(row.s - 25, row.s + 25, row.id);
for (const row of track.moving.traps) add(row.s - row.len / 2 - 15, row.s + row.len / 2 + 15, row.id);
for (const j of track.jumps) add(j.s - 40, j.landS + 20, 'jump');
for (const a of track.aerials) add(a.rampS0 - 20, a.landingS1 + 15, a.id);
for (const f of track.fields) add(f.fromS - 20, f.toS + 20, f.id);
for (const r of track.routes) { add(r.land.fromS - 10, r.land.toS + 10, r.id + ' land'); add(r.openS0 - 10, r.openS1 + 10, r.id + ' open edge'); }
for (const fk of track.forks || []) add(fk.fromS - 20, fk.toS + 20, fk.id);
for (const b of track.boosts) add(b.s - 10, b.s + b.len + 60, 'boost carry');
for (const c of track.checklines) add(c.s - 5, c.s + 30, 'checkpoint');
for (const g of phraseGateLayout(track)) add(g.s - 25, g.s + 100, g.id);
const keepAt = (a, b, skip) => keep.filter(([k0, k1, why]) => b >= k0 && a <= k1 && !skip.some(x => why.startsWith(x))).map(k => k[2]);

// Every road deck (main and forks) every 2 m, hashed in plan, for ceilings over the arc.
const CELL = 16, decks = new Map();
const deckRows = [];
for (let s = 0; s < L; s += 2) deckRows.push({ f: track.frameAt(s), s, src: 'main' });
for (const fk of track.forks || []) for (let s = 0; s < fk.lengthM; s += 2) deckRows.push({ f: fk.frameAt(s), s: fk.fromS, src: fk.id });
for (const d of deckRows) {
  const r = d.f.w / 2 + 3;
  for (let x = Math.floor((d.f.p[0] - r) / CELL); x <= Math.floor((d.f.p[0] + r) / CELL); x++)
    for (let z = Math.floor((d.f.p[2] - r) / CELL); z <= Math.floor((d.f.p[2] + r) / CELL); z++) {
      const k = x + ':' + z; if (!decks.has(k)) decks.set(k, []); decks.get(k).push(d);
    }
}
const scenery = goonworksWorldPlan(track).rows.filter(r => !r.id.startsWith('horizon-') && r.id !== 'white-cartoon-cloud');

/** Flies one lip crossing: kart centre at ramp.x + dLat, speed v along the lip tangent, velY = max(v * t.y, upMps). */
function fly(ramp, v, dLat = 0) {
  const sLip = track.nodeS[ramp.node], f = track.frameAt(sLip);
  const P = [0, 1, 2].map(k => f.p[k] + f.b[k] * (ramp.x + dLat) + f.n[k] * ramp.heightM);
  const hl = Math.hypot(f.t[0], f.t[2]), dir = [f.t[0] / hl, 0, f.t[2] / hl], side = [dir[2], 0, -dir[0]];
  const vh = v * hl, ceiling = f.p[1] + ramp.heightM + ramp.upMps ** 2 / (2 * GRAVITY) + 1.6 + 2;
  const route = ramp.purpose === 'shortcut' ? track.routes.find(r => r.node === undefined && r.from === ramp.node) : null;
  let vy = Math.max(v * f.t[1], ramp.upMps), t = 0, hint = f.seg, apex = 0, touch = null, step = 0;
  const issues = new Set(), path = [];
  while (t < 4 && !touch) {
    const prev = [...P];
    P[0] += dir[0] * vh * DT; P[1] += vy * DT; P[2] += dir[2] * vh * DT; vy -= GRAVITY * DT; t += DT; step++;
    apex = Math.max(apex, P[1] - f.p[1]);
    const q = track.query(P[0], P[1], P[2], hint); hint = q.seg;
    const below = track.query.below(P[0], P[1], P[2]);
    path.push({ t, p: [...P], h: below ? below.height : null });
    // leaving the deck sideways at rail height is a wall hit (an open drop edge has no rail on that side)
    if (q.edgeDist < 0.8 && q.height > -1 && q.height < 2.5 && railOn(track.nodeFlags(q.seg), Math.sign(q.lateral))) issues.add('rail near s=' + Math.round(q.s));
    if (below && below.height <= 0 && vy < 0) { touch = { t, q: below, p: [...P] }; break; }
    // ceilings: any other deck over the kart below apex + kart + 2 m (the deck being dropped from is the drop itself)
    for (const d of decks.get(Math.floor(P[0] / CELL) + ':' + Math.floor(P[2] / CELL)) || []) {
      if (d.src === 'main' && (Math.abs(d.s - q.s) < 40 || (route && Math.abs(d.s - sLip) < 150))) continue;
      const rel = [P[0] - d.f.p[0], P[1] - d.f.p[1], P[2] - d.f.p[2]];
      if (d.f.p[1] - P[1] < 0.5 || d.f.p[1] > ceiling) continue;
      if (Math.abs(dot(rel, d.f.t)) <= 1.2 && Math.abs(dot(rel, d.f.b)) <= d.f.w / 2 + 3) issues.add('deck overhead ' + d.src + ' s=' + Math.round(d.s));
    }
    // scenery bodies against a kart box with 2 m head clearance
    if (step % 6 === 0) {
      const box = { p: [(P[0] + prev[0]) / 2, (P[1] + prev[1]) / 2 + 1.8, (P[2] + prev[2]) / 2], size: [2.4, 5.6, vh * DT * 6 + 2.4], axes: [side, [0, 1, 0], dir] };
      for (const row of scenery) if (goonworksBoxesIntersect(row, box)) issues.add('scenery ' + row.id);
    }
  }
  if (!touch) { issues.add('no touchdown'); return { issues: [...issues], sLip, apex, path }; }
  const q = touch.q, tf = track.frameAt(q.s);
  if (q.hole || !q.onRoad) issues.add('touchdown off road');
  if (track.nodeFlags(q.seg).includes('jump')) issues.add('touchdown on a gap');
  if (Math.abs(q.lateral) > tf.w / 2 - 3) issues.add('touchdown within 3 m of the edge');
  const heading = Math.acos(Math.min(1, (tf.t[0] * dir[0] + tf.t[2] * dir[2]) / Math.hypot(tf.t[0], tf.t[2]))) * 180 / Math.PI;
  // a drop shortcut lands on the wide lower turn at an angle by design; every other ramp lands nearly straight
  if (heading > (route ? 35 : 20)) issues.add('landing heading off by ' + heading.toFixed(1));
  const after = [touch.p[0] + dir[0] * 10, touch.p[1] + 2, touch.p[2] + dir[2] * 10];
  const qa = track.query(after[0], after[1], after[2], q.seg);
  if (Math.abs(qa.lateral) > track.frameAt(qa.s).w / 2 - 1.5) issues.add('10 m roll-out leaves the road');
  const a = track.frameAt(q.s - 15).t, b = track.frameAt(q.s + 15).t;
  const turn = Math.acos(Math.min(1, (a[0] * b[0] + a[2] * b[2]) / Math.hypot(a[0], a[2]) / Math.hypot(b[0], b[2])));
  const radius = turn > 1e-6 ? 30 / turn : Infinity;
  if (radius < 70) issues.add('touchdown turn radius ' + radius.toFixed(0));
  if (route && !(q.s >= route.land.fromS && q.s <= route.land.toS)) issues.add('missed the ' + route.id + ' land deck');
  const s1 = route ? route.openS1 : q.s + 10;
  for (const why of keepAt(sLip - ramp.lenM - 5, s1, route ? [route.id] : [])) issues.add('keep-out ' + why);
  return { issues: [...issues], sLip, apex, path, airS: touch.t, landS: q.s, landLat: q.lateral, heading, radius };
}

test('GOONWORKS carries 3-4 ramps on the shared contract, one per purpose it needs', () => {
  const ramps = json.ramps;
  assert.ok(ramps.length >= 3 && ramps.length <= 4);
  assert.equal(new Set(ramps.map(r => r.id)).size, ramps.length);
  for (const r of ramps) {
    assert.ok(Number.isInteger(r.node) && r.node >= 0 && r.node < json.nodes.length, r.id);
    assert.ok(r.widthM >= 4 && r.widthM <= 8 && r.lenM >= 5 && r.lenM <= 9 && r.heightM >= 0.8 && r.heightM <= 1.6, r.id);
    assert.ok(r.upMps >= 11 && r.upMps <= 16, r.id);
    assert.ok(['shortcut', 'item', 'cut', 'air'].includes(r.purpose) && r.note.length > 10, r.id);
    assert.ok(Math.abs(r.x) + r.widthM / 2 <= track.frameAt(track.nodeS[r.node]).w / 2, r.id + ' wedge fits the road');
    assert.ok(!json.nodes[r.node].flags.includes('jump'), r.id);
  }
  for (const p of ['shortcut', 'item']) assert.ok(ramps.some(r => r.purpose === p), p);
  assert.ok(ramps.some(r => r.purpose === 'cut' || r.purpose === 'air'));
  assert.equal(json.items.filter(i => i.liftM !== undefined).length, 3);
});

test('every ramp lands on open road at 40, 50, 60 and 66.7 m/s across its whole lip', () => {
  for (const ramp of json.ramps) for (const v of SPEEDS) for (const dLat of [-(ramp.widthM / 2 - 0.3), 0, ramp.widthM / 2 - 0.3]) {
    const r = fly(ramp, v, dLat);
    assert.deepEqual(r.issues, [], `${ramp.id} at ${v} m/s, lip offset ${dLat}`);
  }
});

test('ramp air time sits in the EMI trick window at racing speed', () => {
  for (const ramp of json.ramps) for (const v of [50, 60]) {
    const r = fly(ramp, v);
    if (ramp.purpose === 'shortcut') assert.ok(r.airS > 1.2 && r.airS < 2.6, ramp.id + ' drop ' + r.airS);
    else assert.ok(r.airS >= 0.85 && r.airS <= 1.2, `${ramp.id} at ${v}: ${r.airS.toFixed(2)} s`);
  }
});

test('the drop shortcut ramp lands every speed on grand-fold-cut-1 lower turn', () => {
  const ramp = json.ramps.find(r => r.purpose === 'shortcut'), route = track.routes.find(r => r.from === ramp.node);
  assert.equal(route.id, 'grand-fold-cut-1');
  assert.ok(track.nodeS[ramp.node] - ramp.lenM >= route.openS0 - 10 && track.nodeS[ramp.node] < route.openS1);
  assert.equal(route.side, 'L'); assert.ok(ramp.x < 0, 'the lip leans out over the open left edge');
  for (const v of SPEEDS) {
    const r = fly(ramp, v);
    assert.ok(r.landS >= route.land.fromS && r.landS <= route.land.toS, v + ' m/s lands at ' + r.landS);
  }
});

test('the floating boxes sit on the pinball ramp arc and out of reach from the road', () => {
  const ramp = json.ramps.find(r => r.purpose === 'item');
  const lifted = json.items.filter(i => i.liftM !== undefined);
  for (const box of lifted) {
    assert.ok(box.liftM > PICK_H + 0.5, 'a grounded kart cannot take a lifted box');
    assert.equal(box.node, ramp.node); assert.equal(box.x, ramp.x);
  }
  for (const v of SPEEDS) {
    const r = fly(ramp, v);
    for (const box of lifted) {
      const f = track.frameAt(track.nodeS[box.node] + box.ds);
      const at = [0, 1, 2].map(k => f.p[k] + f.b[k] * box.x);
      const hit = r.path.some(p => Math.hypot(p.p[0] - at[0], p.p[2] - at[2]) <= BOX_R && p.h !== null && Math.abs(p.h - box.liftM) <= PICK_H - 0.5);
      assert.ok(hit, `${v} m/s misses the box at ds ${box.ds}`);
    }
  }
});

if (process.env.RAMPS_REPORT) test('report', () => {
  for (const ramp of json.ramps) console.log(ramp.id, SPEEDS.map(v => { const r = fly(ramp, v); return v + ': ' + r.airS.toFixed(2) + ' s, lands ' + (r.landS - r.sLip).toFixed(0) + ' m on lat ' + r.landLat.toFixed(1) + ', apex ' + r.apex.toFixed(1); }).join(' | '));
});
