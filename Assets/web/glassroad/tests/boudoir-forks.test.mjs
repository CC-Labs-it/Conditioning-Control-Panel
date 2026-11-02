import test from 'node:test';
import assert from 'node:assert/strict';
import { buildBoudoir } from '../dev/gen-boudoir.mjs';
import { loadTrack } from '../track/load.js';

const json = buildBoudoir(), track = loadTrack(json);
const main = [];
for (let s = 0; s < track.lengthM; s += 4) main.push({ s, f: track.frameAt(s) });

test('Boudoir has three physically separate, named road choices with distinct driving profiles', () => {
  assert.equal(track.forks.length, 3);
  assert.deepEqual(new Set(track.forks.map(f => f.kind)), new Set(['narrow', 'dry', 'slalom']));
  for (const fork of track.forks) {
    const frames = fork.samples(3);
    assert.ok(fork.toS - fork.fromS > fork.lengthM, fork.id + ' is a real distance saving');
    assert.ok(frames.every(f => f.w >= 10 && f.w <= 22 && f.surface === 'grip'));
    assert.ok(frames.every(f => f.n[1] > .88), fork.id + ' remains an ordinary drivable deck');
    assert.ok(frames.some(f => {
      const q = main.reduce((best, row) => Math.min(best, Math.hypot(f.p[0] - row.f.p[0], f.p[2] - row.f.p[2])), Infinity);
      return q > 40;
    }), fork.id + ' visibly departs the main road');
  }
  const bath = track.forks.find(f => f.kind === 'dry'), wardrobe = track.forks.find(f => f.kind === 'slalom');
  assert.ok(Math.max(...bath.samples().map(f => f.p[1])) > 40, 'dry bridge rises above the bath');
  assert.ok(wardrobe.frameAt(0).p[1] - wardrobe.frameAt(wardrobe.lengthM).p[1] > 85, 'alternate wardrobe drive is a long physical descent');
  assert.equal(track.routes.length, 2, 'both proven aerial drops remain available');
});

test('Each fork joins its main deck with a usable same-height rail opening at both ends', () => {
  for (const fork of track.forks) for (const [local, s] of [[0, fork.fromS], [fork.lengthM, fork.toS]]) {
    const f = fork.frameAt(local), m = track.frameAt(s);
    assert.ok(Math.hypot(...f.p.map((v, i) => v - m.p[i])) < .002, fork.id + ' exact endpoint');
    assert.ok(f.t.reduce((n, v, i) => n + v * m.t[i], 0) > .98, fork.id + ' smooth endpoint heading');
    const portals = track.forkPortals.filter(p => p.id === fork.id && Math.abs((p.s0 + p.s1) / 2 - s) < 80);
    assert.ok(portals.some(p => p.s1 - p.s0 >= 18), fork.id + ' full-width opening');
    for (const p of portals) for (let ps = p.s0 + 2; ps < p.s1 - 2; ps += 3) {
      const edge = track.frameAt(ps), point = edge.p.map((v, i) => v + edge.b[i] * p.side * edge.w / 2), q = fork.query(...point);
      assert.ok(q.onRoad && Math.abs(q.height) < .7, fork.id + ' no raised lip at the crossing');
    }
  }
});

test('Fork decks clear unrelated main road layers and each other across their full width', () => {
  const hits = [], branches = track.forks.map(fork => ({ fork, frames: fork.samples(4) }));
  const gap = (a, b) => Math.abs(a.p[1] - b.p[1]) - Math.abs(a.b[1]) * a.w / 2 - Math.abs(b.b[1]) * b.w / 2;
  const overlaps = (a, b) => Math.hypot(a.p[0] - b.p[0], a.p[2] - b.p[2]) < (a.w + b.w) / 2 + 2;
  for (const { fork, frames } of branches) for (const f of frames) for (const { s, f: m } of main) {
    const junction = (f.localS < 100 && Math.abs(s - fork.fromS) < 100)
      || (f.localS > fork.lengthM - 100 && Math.abs(s - fork.toS) < 100);
    if (!junction && overlaps(f, m) && gap(f, m) < 9) hits.push([fork.id, +f.localS.toFixed(1), +s.toFixed(1), +gap(f, m).toFixed(2)]);
  }
  for (let a = 0; a < branches.length; a++) for (let b = a + 1; b < branches.length; b++)
    for (const f of branches[a].frames) for (const g of branches[b].frames)
      if (overlaps(f, g) && gap(f, g) < 9) hits.push([branches[a].fork.id, branches[b].fork.id]);
  assert.deepEqual(hits.slice(0, 20), [], 'nine metres of full-width clearance outside the six intended junctions');
});
