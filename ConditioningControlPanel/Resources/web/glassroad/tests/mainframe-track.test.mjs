import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { buildMainframe, PIECES } from '../dev/gen-mainframe.mjs';
import { validate } from '../track/schema.js';
import { loadTrack } from '../track/load.js';
import { stoneIn } from '../track/courts.js';
import { makeRace } from './sim-racers-harness.mjs';
import { S } from '../sim/state.js';
import { scanZones } from '../sim/mainframeScan.js';

const built = buildMainframe(), json = JSON.parse(readFileSync(new URL('../tracks/mainframe.json', import.meta.url), 'utf8'));
const track = loadTrack(json);
test('Mainframe is reproducible, closed, spacious and has three distinct acts', () => {
  assert.deepEqual(json, built.track);
  assert.deepEqual(validate(json), { ok: true, errors: [], warnings: [] });
  assert.ok(track.lengthM >= 4800 && track.lengthM <= 4900);
  assert.equal(new Set(track.sections.map(section => section.act)).size, 3);
  assert.ok(json.nodes.every(n => n.w >= 24));
  assert.ok(Math.max(...json.nodes.map(n => n.p[1])) >= 86);
  assert.ok(json.nodes.some(n => n.roll >= 18));
  assert.ok(json.nodes.some(n => n.surface === 'stream') && json.nodes.some(n => n.surface === 'pool'));
  assert.ok(track.sections.some(s => s.id === 'coolant-canal') && track.sections.some(s => s.id === 'submerged'));
  assert.ok(PIECES.filter(p => p.turn < 0).length >= 4, 'chicanes reverse direction');
  assert.equal(PIECES.filter(p => Math.abs(p.turn) === 180).length, 2, 'two elevated switchbacks');
  const emerge = track.sections.find(s => s.id === 'emerge');
  assert.ok(emerge.toS - emerge.fromS >= 120);
  assert.ok(track.frameAt(emerge.fromS).p[1] < track.reservoir.waterY);
  assert.ok(track.frameAt(emerge.toS).p[1] > track.reservoir.waterY);
});
test('Mainframe six scan approaches stay wide and clear of stepping gaps', () => {
  assert.equal(built.scanHalls.length, 6);
  assert.deepEqual(scanZones(track).map(zone => zone.s), built.scanHalls.map(hall => hall.s));
  for (const hall of built.scanHalls) {
    for (let s = hall.s - 35; s <= hall.s + 25; s += 5) {
      const frame = track.frameAt(s);
      assert.ok(frame.w >= 24);
      assert.ok(['glass', 'grip'].includes(track.nodeSurface(frame.seg)));
      assert.ok(!track.fields.some(f => s >= f.fromS && s <= f.toS));
    }
  }
});
test('Mainframe reactor is a full physical helix with a clear raised crossing', () => {
  assert.equal(PIECES.find(p => p.id === 'core').turn, 360);
  const { from, to } = built.spans.core;
  const centre = [json.nodes[from].p[0] - 85, json.nodes[from].p[2]];
  for (let i = from; i < to; i++) assert.ok(Math.abs(Math.hypot(json.nodes[i].p[0] - centre[0], json.nodes[i].p[2] - centre[1]) - 85) < 0.003);
  assert.equal(json.nodes[from].p[1], 24); assert.equal(json.nodes[to].p[1], 76);
  assert.ok(json.nodes.slice(from, to).every((n, i, a) => !i || n.p[1] > a[i - 1].p[1]));
  assert.ok(json.nodes[built.spans.memory.from].p[1] - json.nodes[from + 12].p[1] > 35);
});
test('Mainframe memory tiles start supported in every lane and meet the exit deck', () => {
  const field = track.fields[0];
  assert.ok(field.toS - field.fromS >= 350);
  assert.equal(field.lanes.length, 5);
  assert.ok(field.stones.every(stone => stone.shape === 'hex' && stone.w >= 20));
  assert.ok(track.frameAt(field.fromS).w >= 100);
  for (const x of field.lanes) {
    assert.ok(stoneIn(field, field.fromS + 0.1, x));
    const lane = field.stones.filter(stone => stone.x === x).sort((a, b) => a.s - b.s);
    assert.ok(Math.abs(field.toS - (lane.at(-1).s + lane.at(-1).len)) <= 0.01);
    for (let i = 1; i < lane.length; i++) assert.ok(lane[i].s - lane[i - 1].s - lane[i - 1].len <= 2.01);
  }
});
for (const seed of [1, 241023, 20261024]) test('Mainframe full starting grid finishes all three laps, seed ' + seed, () => {
  const run = makeRace({ track, seed });
  run.run(720, () => run.racers.list.every(racer => racer.finished));
  assert.ok(run.racers.list.every(racer => racer.finished && racer.st[S.lap] === 3), JSON.stringify(run.racers.list.map(r => ({ i: r.i, s: r.st[S.s], lap: r.st[S.lap], falls: r.falls }))));
  assert.ok(run.racers.list.every(r => r.falls === 0), 'no rescues needed by the starting grid');
});
