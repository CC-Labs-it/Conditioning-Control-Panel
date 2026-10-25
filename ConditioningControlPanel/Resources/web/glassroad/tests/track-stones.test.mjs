/* Stepping-stone fields (stones wave, BRIEF-STONES.md): schema, courts.js helpers, query, load, mesh, generator. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { validate } from '../track/schema.js';
import { loadTrack } from '../track/load.js';
import { fieldAt, stoneAt, fieldSupport, buildFields, holeDistance } from '../track/courts.js';
import { build, checks, FIELDS } from '../dev/gen-threshold.mjs';

const json = JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8'));
const tr = loadTrack(json);
const place = (s, lat, h) => {
  const f = tr.frameAt(s);
  return [0, 1, 2].map((k) => f.p[k] + f.b[k] * lat + f.n[k] * h);
};
const field = tr.fields[0];
const laneStones = (x) => field.stones.filter((h) => h.x === x).sort((a, b) => a.s - b.s);

// a fake field in a flat chart: fromS 100, toS 140, lanes -7 / 0 / 7, 5 m stones with 4 m gaps staggered by 3 m
const fakeFields = () => {
  const nodeS = [0, 100, 140, 200], stones = [];
  [-7, 0, 7].forEach((x, li) => { for (let s = 100 + li * 3; s + 5 <= 138; s += 9) stones.push({ id: 'f-' + li + '-' + stones.length, node: 1, ds: s - 100, x, w: 5, len: 5 }); });
  return buildFields([{ id: 'fake', from: 1, to: 2, lanes: [-7, 0, 7], stones }], nodeS, () => ({ w: 21, flags: [] }));
};

test('stones schema: Threshold carries a field; a bad field is refused', () => {
  assert.equal(validate(json).ok, true);
  assert.ok(Array.isArray(json.fields) && json.fields.length === 1);
  const bad = (mut) => { const t = structuredClone(json); mut(t); return validate(t); };
  assert.equal(bad((t) => { t.fields = {}; }).ok, false, 'not an array');
  assert.equal(bad((t) => { t.fields.push({ ...t.fields[0] }); }).ok, false, 'duplicate id');
  assert.equal(bad((t) => { t.fields[0].lanes = [7, 0, -7]; }).ok, false, 'lanes unsorted');
  assert.equal(bad((t) => { t.fields[0].stones[0].w = 2; }).ok, false, 'w under 3');
  assert.equal(bad((t) => { t.fields[0].stones[0].len = 81; }).ok, false, 'challenge len over 80');
  assert.equal(bad((t) => { t.fields[0].stones[0].x = 40; }).ok, false, 'stone off the road');
  assert.equal(bad((t) => { t.fields[0].stones[0].node = t.fields[0].to; }).ok, false, 'stone outside its field');
  assert.equal(bad((t) => { t.fields[0].stones[1].id = t.fields[0].stones[0].id; }).ok, false, 'duplicate stone id');
  assert.equal(bad((t) => { delete t.fields; }).ok, true, 'fields optional');
});

test('stones courts: fieldAt, stoneAt margins, fieldSupport outside null and inside sorted', () => {
  const fs = fakeFields(), f = fs[0];
  assert.equal(f.fromS, 100); assert.equal(f.toS, 140);
  assert.equal(fieldAt(fs, 99.9), null); assert.equal(fieldAt(fs, 100), f); assert.equal(fieldAt(fs, 139.9), f); assert.equal(fieldAt(fs, 140), null);
  assert.equal(f.void.id, 'fake:void'); assert.equal(f.void.lip, true); assert.equal(f.void.field, f);
  assert.ok(!Object.keys(f).includes('void'), 'the void stays out of the enumerable keys');
  // lane -7's first stone spans s 100..105, x -9.5..-4.5
  const st = stoneAt(fs, 102, -7);
  assert.ok(st && st.x === -7 && st.s === 100);
  assert.equal(stoneAt(fs, 102, -4.4), null, 'beside the stone');
  assert.equal(stoneAt(fs, 102, -4.6).x, -7, 'on its edge');
  assert.equal(stoneAt(fs, 102, -4.6, 0.5), null, 'a margin shrinks the stone');
  assert.equal(stoneAt(fs, 100.2, -7, 0.5), null, 'shrunk along s too');
  assert.equal(stoneAt(fs, 106, -7), null, 'the gap'); assert.equal(stoneAt(fs, 50, 0), null, 'outside every field');
  assert.equal(fieldSupport(fs, 50, 10.5), null, 'outside = the road as usual');
  // s 102: lane -7 (100..105) only; s 104: lanes -7 and 0 (103..108)
  assert.deepEqual(fieldSupport(fs, 102, 10.5), [[-9.5, -4.5]]);
  assert.deepEqual(fieldSupport(fs, 104, 10.5), [[-9.5, -4.5], [-2.5, 2.5]]);
  assert.deepEqual(fieldSupport(fs, 104, 10.5, 0.5), [[-9, -5], [-2, 2]]);
  assert.deepEqual(fieldSupport(fs, 104, 8), [[-8, -4.5], [-2.5, 2.5]], 'clipped to the road');
  // the void of one lane at a staggered s still has support in another lane
  for (let s = 100; s < 136; s += 0.5) assert.ok(fieldSupport(fs, s, 10.5).length >= 1, 'support at s ' + s);
  assert.deepEqual(fieldSupport(fs, 139, 10.5), [], 'the end gap before the deck');
  assert.throws(() => buildFields([{ id: 'x', from: 1, to: 2, lanes: [0], stones: [{ id: 'a', node: 1, ds: 38, x: 0, w: 5, len: 5 }] }], [0, 100, 140, 200], () => ({ w: 21, flags: [] })), /outside its field/);
  assert.throws(() => buildFields([{ id: 'x', from: 1, to: 2, lanes: [9], stones: [{ id: 'a', node: 1, ds: 0, x: 9, w: 5, len: 5 }] }], [0, 100, 140, 200], () => ({ w: 21, flags: [] })), /footprint/);
});

test('stones load: Threshold field with fromS/toS, staggered lanes, stones inside, doubled islands keep real gaps within a 50 m gallery', () => {
  assert.equal(tr.fields.length, 1);
  assert.equal(field.id, 'bridge-stones');
  assert.equal(field.fromS, tr.nodeS[field.from]); assert.equal(field.toS, tr.nodeS[field.to]);
  assert.deepEqual(field.lanes, [-18, 0, 18]);
  assert.ok(field.toS - field.fromS > 80 && field.toS - field.fromS < 100, 'about 90 m');
  for (let i = field.from; i < field.to; i++) assert.ok(tr.nodeFlags(i).includes('open'), 'node ' + i + ' open');
  assert.ok(!tr.nodeFlags(field.from - 1).includes('open') && !tr.nodeFlags(field.to).includes('open'), 'the deck before and after keeps its rails');
  const lanes = field.lanes.map(laneStones);
  for (const l of lanes) assert.ok(l.length >= 3, 'at least three distinct islands a lane');
  assert.ok(Math.abs(lanes[0][0].s - field.fromS) < 1e-6, 'lane -7 starts at the field start');
  // every lane starts at the deck's edge with a lead stone; the regular stones behind it are staggered by 3 m
  for (const l of lanes) assert.ok(Math.abs(l[0].s - field.fromS) < 0.05, 'a lead stone at the field start');
  assert.ok(Math.abs(lanes[1][1].s - lanes[0][1].s - 8) < 0.05 && Math.abs(lanes[2][1].s - lanes[0][1].s - 16) < 0.05, 'staggered by 8 m');
  for (const l of lanes) assert.ok(Math.abs(l[l.length - 1].s + l[l.length - 1].len - (field.toS - 2)) < 0.1, 'every lane ends 2 m before the deck');
  for (const l of lanes) {
    for (let k = 1; k < l.length; k++) { const gap = l[k].s - l[k - 1].s - l[k - 1].len; assert.ok(gap >= 14.99 && gap <= 15.01, 'gap ' + gap); }
    assert.ok(l[l.length - 1].s + l[l.length - 1].len <= field.toS - 2 + 1e-6, 'the last stone ends 2 m before the deck');
  }
  for (const h of field.stones) {
    assert.equal(h.w, 10); assert.ok(h.len >= 7 && h.len <= 32, 'len ' + h.len);   // lead and closing stones run longer than the 5 m model
    for (const s of [h.s, h.s + h.len / 2, h.s + h.len]) assert.ok(tr.frameAt(s).w / 2 >= Math.abs(h.x) + h.w / 2 + 0.5, 'spare room at s ' + s);
  }
  assert.ok(Math.abs(tr.frameAt(field.fromS).w - 50) < 0.05 && Math.abs(tr.frameAt(field.toS - 1).w - 50) < 0.1, 'the field is 50 m wide');
  assert.ok(Math.abs(tr.frameAt(field.fromS - 60).w - 32) < 0.05 && Math.abs(tr.frameAt(field.toS + 40).w - 32) < 0.05, 'the ordinary road remains 32 m well before and after');
  for (let s = field.fromS; s < field.toS - 4.5; s += 0.5) assert.ok(fieldSupport(tr.fields, s, 25).length >= 1, 'never a wall at s ' + s);
  for (const it of tr.items) assert.ok(it.s < field.fromS || it.s >= field.toS, 'no item inside');
  for (const b of tr.boosts) assert.ok(b.s + b.len <= field.fromS || b.s >= field.toS, 'no pad inside');
  const pad = tr.boosts.filter((b) => b.s < field.fromS).sort((a, b) => b.s - a.s)[0];
  assert.ok(pad && field.fromS - pad.s >= 6, 'the pad before the field');
  assert.doesNotThrow(() => JSON.stringify(tr.fields), 'fields serialise');
});

test('stones query: on a stone = on road with stone; in a gap = the shared void with a negative holeDist', () => {
  const first = laneStones(0)[0];
  let q = tr.query(...place(first.s + 2.5, 0, 0.2), field.from);
  assert.equal(q.onRoad, true); assert.equal(q.hole, null); assert.equal(q.holeDist, Infinity);
  assert.equal(q.stone, first); assert.ok(q.edgeDist > 10, 'edgeDist still means the road edge');
  // the gap after it, lane 0
  const gapS = first.s + first.len + 2;
  q = tr.query(...place(gapS, 0, 0.2), field.from);
  assert.equal(q.onRoad, false); assert.equal(q.stone, null);
  assert.equal(q.hole, field.void); assert.equal(q.hole.id, 'bridge-stones:void'); assert.equal(q.hole.lip, true);
  assert.ok(q.holeDist < 0 && q.holeDist > -2.5, 'holeDist ' + q.holeDist);
  assert.ok(Math.abs(-q.holeDist - Math.min(holeDistance(first, q.s, q.lateral), holeDistance(laneStones(0)[1], q.s, q.lateral))) < 1e-6, 'minus the distance to the nearest stone edge');
  assert.ok(q.edgeDist > 10, 'the road edge is still there');
  const again = tr.query(...place(gapS + 0.5, 1, 0.2), field.from);
  assert.equal(again.hole, q.hole, 'one void object per field');
  // the slit between lanes
  q = tr.query(...place(first.s + 2.5, 7, 0.2), field.from);
  assert.equal(q.onRoad, false); assert.equal(q.hole, field.void);
  // the outer margin is void too
  q = tr.query(...place(first.s + 2.5, 24, 0.2), field.from);
  assert.equal(q.onRoad, false); assert.equal(q.hole, field.void);
  // the deck before and after the field is plain road with no stone
  q = tr.query(...place(field.fromS - 1, 0, 0.2), field.from - 1);
  assert.equal(q.onRoad, true); assert.equal(q.stone, null); assert.equal(q.hole, null);
  q = tr.query(...place(field.toS + 1, 0, 0.2), field.to);
  assert.equal(q.onRoad, true); assert.equal(q.stone, null); assert.equal(q.hole, null);
  // the court is untouched
  const h = tr.courts[0].holes[0];
  q = tr.query(...place(h.s + h.len / 2, h.x, 0.2), tr.courts[0].from);
  assert.equal(q.hole, h); assert.equal(q.stone, null); assert.ok(q.holeDist < 0);
  // the road beneath the field's first gap is the start straight, found by below() (the void is not a road)
  const under = tr.query.below(...place(gapS, 0, -0.3));
  assert.equal(under, null, 'the enlarged field has real empty space below its gap');
});

test('stones mesh: no road rows inside the field, no hazard strip there, byte-identical without fields', () => {
  const M = tr.meshes;
  const inField = (m) => {
    let hits = 0;
    for (let v = 0; v < m.positions.length / 3; v++) {
      const q = tr.query(m.positions[v * 3], m.positions[v * 3 + 1], m.positions[v * 3 + 2]);
      if (q.s > field.fromS + 0.5 && q.s < field.toS - 0.5 && Math.abs(q.height) < 1) hits++;
    }
    return hits;
  };
  assert.equal(inField(M.road), 0, 'road vertices inside the field');
  assert.equal(inField(M.edgeStrip), 0, 'hazard strip inside the field');
  assert.equal(inField(M.rails), 0, 'rails inside the field');
  const bare = structuredClone(json);
  delete bare.fields;
  const t2 = loadTrack(bare), M2 = t2.meshes;
  assert.deepEqual(t2.fields, []);
  assert.ok(M2.road.positions.length > M.road.positions.length, 'the field removes road rows');
  const t3 = loadTrack(bare);
  for (const k of Object.keys(M2)) {
    assert.equal(t3.meshes[k].positions.length, M2[k].positions.length, k);
    assert.deepEqual(t3.meshes[k].indices, M2[k].indices, k);
  }
});

test('stones gen: the FIELDS config builds the row, its checks pass, the info line names what lies beneath', () => {
  assert.equal(FIELDS.length, 1);
  assert.deepEqual(FIELDS[0].lanes, [-18, 0, 18]);
  assert.equal(FIELDS[0].piece, 'bridge');
  const built = build(), { errs, info } = checks(built);
  assert.deepEqual(errs, []);
  assert.equal(built.track.fields.length, 1);
  assert.ok(info.fields.length === 2 && /stones \d+\/\d+\/\d+/.test(info.fields[0]), info.fields[0]);
  assert.ok(/beneath: nothing/.test(info.fields[1]), info.fields[1]);
  assert.deepEqual(built.track.fields[0].stones.map((h) => h.id).length, new Set(built.track.fields[0].stones.map((h) => h.id)).size, 'unique ids');
  const fd = built.track.fields[0];
  for (let i = fd.from; i < fd.to; i++) assert.deepEqual(built.track.nodes[i].flags, ['open']);
  assert.ok(!built.track.nodes[fd.from - 1].flags.includes('open'), 'the old fall region is gone from FALLS');
  // the checks catch a wall and a stone past the end gap
  const wall = build();
  wall.track.fields[0].stones = wall.track.fields[0].stones.filter((h) => h.ds > 10 || h.node !== fd.from);
  assert.ok(checks(wall).errs.some((e) => /wall|first stone/.test(e)), 'a wall is refused');
  const late = build();
  late.track.fields[0].stones[0].ds = 200;
  assert.ok(checks(late).errs.some((e) => /outside the field/.test(e)), 'a stone past the end is refused');
});
