import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { buildBoudoir } from '../dev/gen-boudoir.mjs';
import { BOUDOIR_PIECES, getBoudoirLayout, walkBoudoir } from '../track/boudoir-layout.js';
import { loadTrack } from '../track/load.js';
import { validate } from '../track/schema.js';
import { makeRace } from './sim-racers-harness.mjs';
import { createItems } from '../sim/items.js';
import { S } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { updateRespawn } from '../sim/respawn.js';
import { makeFeel, place, recorder, acts, DT } from './sim-fake.mjs';
const json = JSON.parse(readFileSync(new URL('../tracks/boudoir.json', import.meta.url), 'utf8'));
const course = () => loadTrack(json);
test('Boudoir regenerates a closed substantial two-lap course with a level eight-racer grid', () => {
  assert.deepEqual(buildBoudoir(), json); assert.deepEqual(validate(json), { ok: true, errors: [], warnings: [] });
  const track = course(), end = walkBoudoir(BOUDOIR_PIECES);
  const source = loadTrack(json, { courseShape: false });
  assert.equal(track.laps, 2); assert.ok(source.lengthM > 6000 && source.lengthM < 8000);
  assert.ok(track.lengthM > 10000 && track.lengthM < 11000, 'wide curves, long bath boost runout and a single shelf circuit');
  assert.ok(Math.hypot(end.x, end.z) < 1e-7);
  assert.ok(json.nodes.every(n => n.w >= 28));
  for (let i = 0; i < 8; i++) assert.ok(Math.abs(track.spawnPose(i).p[1]) < .001);
});
test('Vanity and galleries vary the physical drive with smooth crests, a broad S-bend and tapered decks', () => {
  const track = course(), layout = getBoudoirLayout(track);
  for (const id of ['vanity-grid', 'shelf-gallery', 'upper-gallery']) {
    const span = layout.landmarks.find(p => p.id === id), frames = [];
    for (let s = span.s0; s < span.s1; s += 2) frames.push(track.frameAt(s));
    const start = frames[0], height = Math.max(...frames.map(f => f.p[1])) - start.p[1];
    assert.ok(height > (id === 'vanity-grid' ? 1.8 : 3.7), id + ' crest');
    assert.ok(Math.min(...frames.map(f => f.w)) <= start.w - 3.5, id + ' tapered width');
    assert.ok(frames.every(f => Math.abs(f.t[1]) < .16), id + ' gentle slope');
    if (id === 'shelf-gallery') {
      const end = track.frameAt(span.s1), dx = end.p[0] - start.p[0], dz = end.p[2] - start.p[2];
      const offset = frames.map(f => ((f.p[0] - start.p[0]) * dz - (f.p[2] - start.p[2]) * dx) / Math.hypot(dx, dz));
      assert.ok(Math.max(...offset) > 1.5 && Math.min(...offset) < -1.5, 'two gentler S-bend turns remain');
    }
  }
  for (let s = 0; s < 60; s += 3) assert.ok(Math.abs(track.frameAt(s).p[1]) < .001, 'grid remains flat');
});
test('Perfume courts have long visible flat approaches and an always-open eight-metre bypass', () => {
  const track = course(), layout = getBoudoirLayout(track);
  assert.equal(layout.perfume.length, 3);
  for (const z of layout.perfume) {
    const first = track.frameAt(z.s0 - 35);
    for (let s = z.s0 - 35; s < z.s1 + 20; s += 4) {
      const f = track.frameAt(s), left = z.x - z.w / 2 + f.w / 2, right = f.w / 2 - z.x - z.w / 2;
      assert.ok(Math.max(left, right) >= 8);
      assert.ok(Math.abs(f.p[1] - first.p[1]) < .001);
      const heading = f.t.reduce((sum, v, i) => sum + v * first.t[i], 0);
      assert.ok(heading > .9999, z.id + ' approach heading at ' + s + ': ' + heading);
    }
  }
});
test('Every overlapping Boudoir road layer clears both full-width banks by nine metres', () => {
  const track = course(), frames = [];
  for (let s = 0; s < track.lengthM; s += 8) frames.push({ s, f: track.frameAt(s) });
  const clashes = []; let crossings = 0;
  for (let i = 0; i < frames.length; i++) for (let j = i + 16; j < frames.length; j++) {
    const a = frames[i], b = frames[j]; if (track.lengthM - b.s + a.s < 128) continue;
    if (Math.hypot(a.f.p[0] - b.f.p[0], a.f.p[2] - b.f.p[2]) > (a.f.w + b.f.w) / 2 + 2) continue;
    const gap = Math.abs(a.f.p[1] - b.f.p[1]) - Math.abs(a.f.b[1]) * a.f.w / 2 - Math.abs(b.f.b[1]) * b.f.w / 2;
    if (gap < 9) clashes.push([a.s, b.s, +gap.toFixed(2)]); crossings++;
  }
  assert.deepEqual(clashes.slice(0, 20), []); assert.ok(crossings > 100);
});
test('Bath channel has real curved support, flowing water, a descent and deep spill curtains', () => {
  const track = course(), layout = getBoudoirLayout(track), bath = layout.landmarks.find(p => p.id === 'bath-stream');
  const middle = (bath.s0 + bath.s1) / 2;
  assert.equal(track.nodeSurface(bath.node), 'stream');
  assert.ok(track.surfaceAt(middle, 15).p[1] - track.surfaceAt(middle, 0).p[1] > 6);
  assert.ok(track.surfaceAt(middle, 15).n[1] < .8);
  assert.equal(track.waterSpills.length, 3); assert.ok(track.waterSpills.every(s => s.dropM >= 34 && s.toS - s.fromS >= 30));
  const chute = layout.landmarks.find(p => p.id === 'bath-chute');
  assert.ok(track.frameAt(chute.s0 + 2).p[1] - track.frameAt(chute.s1 - 2).p[1] > 30);
});
test('Seven compact islands have broad coherent landing surfaces and close cleanly onto the deck', () => {
  const track = course(), field = track.fields[0]; assert.equal(field.stones.length, 7);
  assert.ok(field.stones.every(s => s.w === 32 && s.len === 26));
  assert.ok(Math.abs(field.stones[0].s - field.fromS) < .001);
  assert.ok(Math.abs(field.stones.at(-1).s + 26 - field.toS) < .001);
  for (let i = 1; i < field.stones.length; i++) assert.ok(Math.abs(field.stones[i].s - field.stones[i - 1].s - 29) < .001);
});
for (const seed of [1, 241023, 20261024]) test('Boudoir eight-racer two-lap race completes without falls, seed ' + seed, () => {
  const r = makeRace({ track: course(), seed, items: createItems });
  r.run(60 + r.track.lengthM * r.track.laps / (r.feel.get('speed.topMps') * .6), () => r.racers.list.every(k => k.finished));
  assert.ok(r.racers.list.every(k => k.finished), JSON.stringify(r.racers.list.map(k => ({ s: k.st[S.s], laps: k.lapTimes.length, finish: k.finishS }))));
  assert.deepEqual(r.racers.list.map(k => k.falls), Array(8).fill(0));
  assert.ok(r.racers.list.every(k => k.lapTimes.length === 2));
});
for (const speed of [0, 10, 24]) for (const lateral of [-12, 0, 12]) test('Compact field supports novice hops and throttle, speed ' + speed + ', lane ' + lateral, () => {
  const track = course(), field = track.fields[0], st = place(track, { s: field.fromS, speed, lateral }), ev = recorder(), feel = makeFeel(), a = acts({ throttle: 1 });
  const hopped = new Set();
  for (let i = 0; i < 60 * 40 && st[S.s] < field.toS + 5; i++) {
    a.hopPressed = false;
    for (const stone of field.stones.slice(0, -1)) if (stone.s + stone.len - st[S.s] > 0 && stone.s + stone.len - st[S.s] < 7 && !hopped.has(stone.id)) {
      a.hopPressed = true; hopped.add(stone.id); break;
    }
    ev.t += DT; step(st, a, track, feel, DT, ev); updateRespawn(st, track, feel, DT, ev);
  }
  assert.ok(st[S.s] > field.toS + 5); assert.equal(ev.all('drop').length, 0); assert.equal(ev.all('void').length, 0);
});
