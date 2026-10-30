// Mainframe ramps (handoff-1030/GLASSROAD-RAMPS.md): every ramp lands clean at 40/50/60/66.7 m/s on the real
// runtime-shaped road. The ballistic checker lives here on purpose (the sim launch is built in another lane).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { buildMainframe } from '../dev/gen-mainframe.mjs';
import { loadTrack } from '../track/load.js';
import { industryZones } from '../sim/mainframeIndustry.js';
import { scanZones } from '../sim/mainframeScan.js';
import { phraseGateLayout } from '../track/phraseGates.js';
import { movingPlan } from '../track/movingHazards.js';
import { CANAL } from '../track/canal.js';

const read = p => JSON.parse(readFileSync(new URL(p, import.meta.url), 'utf8'));
const json = read('../tracks/mainframe.json'), feel = read('../feel.json');
// validate:false keeps the geometry proof independent of the schema lane (R0 adds `ramps` / `liftM`).
const track = loadTrack(json, { validate: false });
const G = feel.hop.gravityMps2, SPEEDS = [40, 50, 60, 66.7], BOX_R = feel.items?.boxRadiusM ?? 1.8;
const sdist = (a, b) => { const d = Math.abs(a - b) % track.lengthM; return Math.min(d, track.lengthM - d); };

function hazardSpans() {
  const spans = [];
  for (const z of industryZones(track)) spans.push(['industry-' + z.kind, z.s - z.len / 2 - 3, z.s + z.len / 2 + 3]);
  for (const z of scanZones(track)) spans.push(['scan', z.s - 30, z.s + 20]);
  for (const g of phraseGateLayout(track)) spans.push(['phrase ' + g.id, g.s - 15, g.s + 15]);
  for (const r of movingPlan(track).traps) spans.push(['moving ' + r.id, r.s - 25, r.s + 25]);
  for (const a of track.aerials) spans.push(['aerial ' + a.id, a.rampS0, a.landingS1]);
  for (const j of track.jumps) spans.push(['jump', j.s - 20, j.landS + 20]);
  for (const f of track.fields) spans.push(['field', f.fromS, f.toS]);
  for (const c of track.checklines) spans.push(['checkpoint', c.s - 5, c.s + 30]);
  for (const b of track.boosts) spans.push(['boost carry', b.s - 5, b.s + b.len + 40]);
  for (const w of track.waterSpills || []) spans.push(['water spill', w.fromS, w.toS]);
  for (const r of track.routes) spans.push(['route open ' + r.id, r.openS0 - 10, r.openS1 + 10], ['route land ' + r.id, r.land.fromS, r.land.toS]);
  spans.push(['start grid', track.lengthM - 60, track.lengthM + 1], ['start grid', -1, 220]);
  return spans;
}
const spans = hazardSpans();
const decks = [];
for (let s = 0; s < track.lengthM; s += 3) { const f = track.frameAt(s); decks.push({ s, p: f.p.slice(), t: f.t.slice(), b: f.b.slice(), w: f.w }); }

/** One ballistic flight from the lip: kart contact point, v along the road tangent, velY = max(velY, upMps). */
function fly(ramp, v) {
  const lipS = track.nodeS[ramp.node], f = track.surfaceAt(lipS, ramp.x);
  const p = [0, 1, 2].map(k => f.p[k] + f.n[k] * ramp.heightM), lipY = p[1];
  const vel = f.t.map(c => c * v); vel[1] = Math.max(vel[1], ramp.upMps);
  const dt = 1 / 240, fail = [], path = [];
  let t = 0, apexY = p[1], hint = f.seg, landed = null, minCrown = Infinity;
  while (t < 4 && !landed) {
    vel[1] -= G * dt; for (let k = 0; k < 3; k++) p[k] += vel[k] * dt; t += dt; apexY = Math.max(apexY, p[1]);
    path.push(p.slice());
    const q = track.query(p[0], p[1], p[2], hint);
    if (q.edgeDist >= 0) hint = q.seg;
    if (q.edgeDist < 0) { fail.push(`leaves the road edge at s ${q.s.toFixed(0)}`); break; }
    // overhead: every other deck over the arc sits at least 4.5 m above the kart's contact point
    for (const d of decks) {
      if (sdist(d.s, q.s) < 40) continue;
      const dx = p[0] - d.p[0], dz = p[2] - d.p[2], lat = dx * d.b[0] + dz * d.b[2], along = dx * d.t[0] + dz * d.t[2];
      if (Math.abs(lat) > d.w / 2 + 2 || Math.abs(along) > 3) continue;
      const above = d.p[1] + d.b[1] * lat - p[1];
      if (above > -0.5 && above < 4.5) fail.push(`deck at s ${d.s.toFixed(0)} only ${above.toFixed(1)} m overhead`);
    }
    if (track.sections[track.sectionAt(q.s)].id === 'coolant-canal') minCrown = Math.min(minCrown, 2 * CANAL.radiusM - q.height);
    if (q.height <= 0) {
      if (!q.onRoad) { fail.push(`comes down in a gap or hole at s ${q.s.toFixed(0)}`); break; }
      landed = { s: q.s, lat: q.lateral, edge: q.edgeDist, seg: q.seg };
    }
  }
  if (!landed) { if (!fail.length) fail.push('never lands'); return { fail, t }; }
  const lf = track.frameAt(landed.s), hv = Math.hypot(vel[0], vel[2]);
  const yaw = Math.acos(Math.min(1, (vel[0] * lf.t[0] + vel[2] * lf.t[2]) / hv / Math.hypot(lf.t[0], lf.t[2]))) * 180 / Math.PI;
  if (landed.edge < 2.5) fail.push(`touchdown ${landed.edge.toFixed(1)} m from the edge`);
  if (yaw > 10) fail.push(`touchdown ${yaw.toFixed(1)} deg off the road heading`);
  for (let d = 2; d <= 10; d += 2) {
    const q = track.query(p[0] + vel[0] / hv * d, p[1] + 1, p[2] + vel[2] / hv * d, landed.seg);
    if (!q.onRoad || q.edgeDist < 2) { fail.push(`roll-out leaves open road ${d} m after touchdown`); break; }
  }
  for (const [id, a, b] of spans) if (b >= lipS - ramp.lenM && a <= landed.s + 10) fail.push(`${id} between the wedge and touchdown + 10 m`);
  return { fail, t, apexM: apexY - lipY, land: landed, yaw, path, minCrown };
}

test('Mainframe carries 3-4 ramps on the shared contract, generator-backed', () => {
  assert.deepEqual(json.ramps, buildMainframe().track.ramps, 'tracks/mainframe.json matches dev/gen-mainframe.mjs');
  assert.ok(json.ramps.length >= 3 && json.ramps.length <= 4);
  assert.equal(new Set(json.ramps.map(r => r.id)).size, json.ramps.length);
  for (const r of json.ramps) {
    assert.ok(Number.isInteger(r.node) && r.node >= 0 && r.node < json.nodes.length, r.id);
    assert.ok(r.widthM >= 4 && r.widthM <= 8 && r.lenM >= 5 && r.lenM <= 9 && r.heightM >= 0.8 && r.heightM <= 1.6, r.id);
    assert.ok(r.upMps >= 11 && r.upMps <= 16, r.id);
    assert.ok(['shortcut', 'item', 'cut', 'air'].includes(r.purpose) && r.note, r.id);
    assert.ok(Math.abs(r.x) + r.widthM / 2 <= track.frameAt(track.nodeS[r.node]).w / 2 - 2, `${r.id} wedge sits on the road`);
  }
  assert.ok(json.ramps.some(r => r.purpose === 'item'));
  assert.ok(json.ramps.some(r => r.purpose === 'air' || r.purpose === 'cut'));
  const lips = json.ramps.map(r => track.nodeS[r.node]).sort((a, b) => a - b);
  for (let i = 1; i < lips.length; i++) assert.ok(lips[i] - lips[i - 1] > 300, 'ramps are spread over the lap');
});

for (const ramp of json.ramps) test(`Mainframe ${ramp.id} lands clean at 40/50/60/66.7 m/s`, () => {
  const rows = SPEEDS.map(v => ({ v, ...fly(ramp, v) }));
  for (const r of rows) {
    assert.deepEqual(r.fail, [], `${ramp.id} at ${r.v} m/s`);
    assert.ok(r.t >= 0.8 && r.t <= 1.25, `${ramp.id} at ${r.v} m/s: ${r.t.toFixed(2)} s of air`);
    if (Number.isFinite(r.minCrown)) assert.ok(r.minCrown - r.apexM > 2, 'the coolant tube crown clears the apex');
  }
  // the spread: slow and fast karts both land inside the same clean stretch
  assert.ok(rows.at(-1).land.s - rows[0].land.s < 40, `${ramp.id} landing spread`);
});

test('Mainframe item ramp arc passes its floating boxes', () => {
  const ramp = json.ramps.find(r => r.purpose === 'item'), lifted = track.items.filter((_, i) => json.items[i].liftM > 0);
  assert.equal(lifted.length, 3);
  for (const [i, box] of lifted.entries()) {
    const row = json.items[track.items.indexOf(box)];
    assert.equal(row.node, ramp.node, 'lifted boxes hang off the item ramp lip');
    assert.ok(row.liftM >= 3 && row.liftM <= 5);
    lifted[i].p = (f => [0, 1, 2].map(k => f.p[k] + f.n[k] * row.liftM))(track.surfaceAt(box.s, box.x));
  }
  for (const v of SPEEDS) {
    const { path } = fly(ramp, v);
    // the kart body centre rides ~0.5 m over its contact point
    const hits = lifted.filter(b => path.some(p => Math.hypot(p[0] - b.p[0], p[1] + 0.5 - b.p[1], p[2] - b.p[2]) <= BOX_R)).length;
    assert.ok(hits >= (v === 50 || v === 60 ? 2 : 1), `${v} m/s reaches ${hits} lifted boxes`);
  }
  // a kart that skips the ramp drives under every lifted box
  for (const b of lifted) assert.ok(b.p[1] - track.surfaceAt(b.s, b.x).p[1] - 0.5 > BOX_R, 'ground karts pass beneath');
});
