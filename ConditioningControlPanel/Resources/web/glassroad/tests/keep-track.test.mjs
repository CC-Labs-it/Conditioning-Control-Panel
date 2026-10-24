import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { buildKeep, formatKeep } from '../dev/gen-keep.mjs';
import { KEEP_PIECES, getKeepLayout, walkKeep } from '../track/keep-layout.js';
import { loadTrack } from '../track/load.js';
import { validate } from '../track/schema.js';
import { makeRace } from './sim-racers-harness.mjs';
import { createItems } from '../sim/items.js';
import { createKeepHazards } from '../sim/keepHazards.js';
const json = JSON.parse(readFileSync(new URL('../tracks/keep.json', import.meta.url), 'utf8'));
const keep = () => loadTrack(json);
test('Keep regenerates exactly and is a closed, two-lap fortress course with a level grid', () => {
  assert.deepEqual(json, buildKeep());
  assert.deepEqual(validate(json), { ok: true, errors: [], warnings: [] });
  assert.ok(formatKeep(json).split('\n').length < 300);
  const t = keep(), end = walkKeep(KEEP_PIECES);
  assert.equal(t.id, 'keep'); assert.equal(t.laps, 2);
  assert.ok(t.lengthM >= 6500 && t.lengthM <= 7600);
  assert.ok(Math.hypot(end.x, end.z) < 1e-7);
  assert.ok(json.nodes.every((n) => n.w >= 26 && n.w <= 40));
  assert.ok(Math.max(...json.nodes.map((n) => n.p[1])) >= 58);
  for (let i = 0; i < 8; i++) assert.ok(Math.abs(t.spawnPose(i).p[1]) < .001);
});
test('Keep gate halls have straight flat approaches, two 8m bypasses and no checkpoint on a gate', () => {
  const t = keep(), layout = getKeepLayout(t);
  assert.equal(layout.halls.length, 2); assert.equal(layout.bypassWidthM, 8);
  for (const hall of layout.halls) {
    const base = t.frameAt(hall.gates[0]);
    for (let s = hall.gates[0] - 45; s <= hall.gates.at(-1) + 15; s += 2) {
      const f = t.frameAt(s);
      assert.ok(Math.abs(f.p[1] - base.p[1]) < .01, 'flat hall ' + hall.id);
      assert.ok(f.t.reduce((sum, v, i) => sum + v * base.t[i], 0) > .9999, 'straight hall ' + hall.id);
      assert.ok(f.w >= 29.99, '30m gate road');
    }
    for (const s of hall.gates) for (const n of json.checkpoints) assert.ok(Math.abs(s - t.nodeS[n]) > 5);
  }
});
test('Keep broad bends and non-adjacent ribbons never intersect at driving height', () => {
  const t = keep(), samples = [];
  assert.ok(KEEP_PIECES.filter((p) => p.turn).every((p) => p.r >= 40));
  for (let s = 0; s < t.lengthM; s += 5) samples.push({ s, ...t.frameAt(s) });
  let minRadius = Infinity, minSeparation = Infinity;
  for (let i = 0; i < samples.length; i++) {
    const a = samples[i], before = t.frameAt(a.s - 1), after = t.frameAt(a.s + 1);
    const da = Math.atan2(after.t[2], after.t[0]) - Math.atan2(before.t[2], before.t[0]);
    const angle = Math.atan2(Math.sin(da), Math.cos(da));
    if (Math.abs(angle) > .00001) minRadius = Math.min(minRadius, 2 / Math.abs(angle));
    for (let j = i + 1; j < samples.length; j++) {
      const b = samples[j], ds = Math.min(b.s - a.s, t.lengthM - (b.s - a.s));
      if (ds < 65 || Math.abs(a.p[1] - b.p[1]) >= 22) continue;
      const room = Math.hypot(a.p[0] - b.p[0], a.p[2] - b.p[2]) - (a.w + b.w) / 2;
      minSeparation = Math.min(minSeparation, room);
      assert.ok(room > 4, 'ribbon clearance at ' + a.s + '/' + b.s);
    }
  }
  assert.ok(minRadius >= 40, 'minimum sampled bend radius ' + minRadius);
  assert.ok(minSeparation > 4);
});
for (const seed of [1, 241023, 20261024]) test('Keep: all eight racers finish two laps without falls, seed ' + seed, () => {
  const r = makeRace({ track: keep(), seed, items: createItems });
  const hazards = createKeepHazards(r.track, getKeepLayout(r.track));
  const drive = r.ai.drive.bind(r.ai), step = r.racers.step.bind(r.racers);
  r.ai.drive = (kart, ctx, dt, out) => { drive(kart, ctx, dt, out); if (ctx.race.phase === 'racing') hazards.steer(kart, out); };
  r.racers.step = (dt, actions, race, ctx) => step(dt, actions, race, { ...ctx, hazards });
  hazards.reset();
  r.run(650, () => r.racers.list.every((x) => x.finished));
  assert.ok(r.racers.list.every((x) => x.finished), 'finish times ' + r.racers.list.map((x) => x.finishS));
  assert.deepEqual(r.racers.list.map((x) => x.falls), Array(8).fill(0));
  assert.ok(r.racers.list.every((x) => x.lapTimes.length === 2));
});




test('Keep tower wraps three and a half turns with 32m vertical separation and a clear solid core', () => {
  const t = keep(), layout = getKeepLayout(t), spiral = layout.landmarks.find((p) => p.id === 'tower-spiral');
  const tower = layout.towers[0], turnM = (spiral.s1 - spiral.s0) / 3.5;
  assert.equal(KEEP_PIECES.find((p) => p.id === 'tower-spiral').turn, -1260);
  for (let s = spiral.s0 + 12; s < spiral.s1 - turnM - 12; s += 10) {
    const a = t.frameAt(s).p, b = t.frameAt(s + turnM).p;
    assert.ok(Math.hypot(a[0] - b[0], a[2] - b[2]) < .2, 'stacked road station');
    assert.ok(b[1] - a[1] >= 31.9, '32m between revolutions');
  }
  for (let s = spiral.s0 + 2; s < spiral.s1 - 2; s += 5) {
    const f = t.frameAt(s), q = t.query(...f.p, f.seg);
    assert.ok(Math.hypot(f.p[0] - tower.x, f.p[2] - tower.z) - f.w / 2 > tower.radiusM + 1.5);
    assert.ok(q.onRoad && Math.abs(q.s - s) < .1, 'query stays on its actual spiral layer');
  }
});
test('Keep chains have a wide dedicated straight and the sluice is real flowing water', () => {
  const t = keep(), layout = getKeepLayout(t), hall = layout.landmarks.find((p) => p.id === 'chain-hall');
  assert.equal(layout.chains.length, 4);
  for (const [i, chain] of layout.chains.entries()) {
    assert.ok(chain.s > hall.s0 + 30 && chain.s < hall.s1 - 25);
    if (i) assert.ok(chain.s - layout.chains[i - 1].s >= 32);
    assert.ok(t.frameAt(chain.s).w >= 33.99);
  }
  const sluice = layout.landmarks.find((p) => p.id === 'sluice'), pool = layout.landmarks.find((p) => p.id === 'moat-pool');
  assert.equal(json.nodes[sluice.node].surface, 'stream');
  assert.deepEqual(json.nodes[sluice.node].flow, [3, 0]);
  assert.equal(json.nodes[pool.node].surface, 'pool');
  assert.equal(json.nodes[sluice.node].p[1], 2);
  assert.ok(pool.s1 - pool.s0 >= 50);
});


test('Keep prison blocks clear the road and exposed sections preserve the inner rail', () => {
  const t = keep(), layout = getKeepLayout(t);
  assert.equal(layout.castleBlocks.length, 4);
  for (const b of layout.castleBlocks) for (let s = 0; s < t.lengthM; s += 5) {
    const f = t.frameAt(s), dx = Math.max(0, Math.abs(f.p[0] - b.x) - b.w / 2), dz = Math.max(0, Math.abs(f.p[2] - b.z) - b.d / 2);
    assert.ok(Math.hypot(dx, dz) > f.w / 2 + 2, b.id + ' clear at ' + s);
  }
  assert.equal(layout.brokenEdges.length, 3);
  for (const edge of layout.brokenEdges) {
    const f = t.frameAt((edge.s0 + edge.s1) / 2);
    assert.ok(f.flags.includes('railR') && !f.flags.includes('railL') && !f.flags.includes('rail'));
    assert.ok(f.w >= 34);
  }
});
test('Keep has a real drawbridge and nine broad landing platforms with four-and-a-half metre gaps', () => {
  const t = keep(), layout = getKeepLayout(t), bridge = layout.drawbridge;
  assert.equal(t.jumps.length, 1);
  assert.ok(t.frameAt(bridge.gapS0 + 1).flags.includes('jump'));
  assert.ok(t.frameAt(bridge.gapS1 + 1).p[1] < t.frameAt(bridge.gapS0).p[1] - 15);
  assert.ok(t.frameAt(bridge.rampS0 + 25).t[1] > .25);
  const field = t.fields[0], stones = field.stones;
  assert.equal(field.id, 'keep-stones'); assert.equal(stones.length, 9);
  assert.ok(Math.abs(field.toS - field.fromS - 270) < .01);
  assert.ok(stones.every((h) => h.w === 28 && h.len === 26));
  assert.ok(Math.abs(stones[0].s - field.fromS) < .01);
  assert.ok(Math.abs(stones.at(-1).s + 26 - field.toS) < .01);
  for (let i = 1; i < stones.length; i++) assert.ok(Math.abs(stones[i].s - stones[i-1].s - 30.5) < .01);
});
