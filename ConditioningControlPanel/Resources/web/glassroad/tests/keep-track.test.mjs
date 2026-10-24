import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { buildKeep, formatKeep } from '../dev/gen-keep.mjs';
import { KEEP_PIECES, getKeepLayout, walkKeep } from '../track/keep-layout.js';
import { loadTrack } from '../track/load.js';
import { validate } from '../track/schema.js';
import { makeRace } from './sim-racers-harness.mjs';
import { createItems } from '../sim/items.js';
import { createKeepGates } from '../sim/keepGates.js';
const json = JSON.parse(readFileSync(new URL('../tracks/keep.json', import.meta.url), 'utf8'));
const keep = () => loadTrack(json);
test('Keep regenerates exactly and is a closed, three-lap course with a level grid', () => {
  assert.deepEqual(json, buildKeep());
  assert.deepEqual(validate(json), { ok: true, errors: [], warnings: [] });
  assert.ok(formatKeep(json).split('\n').length < 200);
  const t = keep(), end = walkKeep(KEEP_PIECES);
  assert.equal(t.id, 'keep'); assert.equal(t.laps, 3);
  assert.ok(t.lengthM >= 1000 && t.lengthM <= 1500);
  assert.ok(Math.hypot(end.x, end.z) < 1e-7);
  assert.ok(json.nodes.every((n) => n.w >= 22 && n.w <= 26 && n.flags.includes('rail')));
  assert.ok(Math.max(...json.nodes.map((n) => n.p[1])) >= 28);
  for (let i = 0; i < 8; i++) assert.ok(Math.abs(t.spawnPose(i).p[1]) < .001);
});
test('Keep gate halls have straight flat approaches, two 6m bypasses and no checkpoint on a gate', () => {
  const t = keep(), layout = getKeepLayout(t);
  assert.equal(layout.halls.length, 2); assert.equal(layout.bypassWidthM, 6);
  for (const hall of layout.halls) {
    const base = t.frameAt(hall.gates[0]);
    for (let s = hall.gates[0] - 45; s <= hall.gates.at(-1) + 15; s += 2) {
      const f = t.frameAt(s);
      assert.ok(Math.abs(f.p[1] - base.p[1]) < .01, 'flat hall ' + hall.id);
      assert.ok(f.t.reduce((sum, v, i) => sum + v * base.t[i], 0) > .9999, 'straight hall ' + hall.id);
      assert.ok(f.w >= 25.99, '26m gate road');
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
      if (ds < 65 || Math.abs(a.p[1] - b.p[1]) > 10) continue;
      const room = Math.hypot(a.p[0] - b.p[0], a.p[2] - b.p[2]) - (a.w + b.w) / 2;
      minSeparation = Math.min(minSeparation, room);
      assert.ok(room > 4, 'ribbon clearance at ' + a.s + '/' + b.s);
    }
  }
  assert.ok(minRadius >= 40, 'minimum sampled bend radius ' + minRadius);
  assert.ok(minSeparation > 4);
});
for (const seed of [1, 241023, 20261024]) test('Keep: all eight racers finish three laps without falls, seed ' + seed, () => {
  const r = makeRace({ track: keep(), seed, items: createItems });
  const hazards = createKeepGates(r.track, { gates: getKeepLayout(r.track).gates });
  const drive = r.ai.drive.bind(r.ai), step = r.racers.step.bind(r.racers);
  r.ai.drive = (kart, ctx, dt, out) => { drive(kart, ctx, dt, out); if (ctx.race.phase === 'racing') hazards.steer(kart, out); };
  r.racers.step = (dt, actions, race, ctx) => step(dt, actions, race, { ...ctx, hazards });
  hazards.reset(); assert.equal(hazards.rows.length, 6);
  r.run(240, () => r.racers.list.every((x) => x.finished));
  assert.ok(r.racers.list.every((x) => x.finished), 'finish times ' + r.racers.list.map((x) => x.finishS));
  assert.deepEqual(r.racers.list.map((x) => x.falls), Array(8).fill(0));
  assert.ok(r.racers.list.every((x) => x.lapTimes.length === 3));
});


