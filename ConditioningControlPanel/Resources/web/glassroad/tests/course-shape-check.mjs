import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { getKeepLayout } from '../track/keep-layout.js';
import { getBoudoirLayout } from '../track/boudoir-layout.js';

const read = id => JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url), 'utf8'));
const delta = (a, b) => Math.atan2(Math.sin(b - a), Math.cos(b - a));
const heading = (t, s) => { const f = t.frameAt(s); return Math.atan2(f.t[0], f.t[2]); };
export function checkCourseShape(id, shapeNodes, maximumLength) {
    const source = read(id), json = structuredClone(source), shape = shapeNodes(json.nodes, source);
    if (shape.forks) json.forks = shape.forks;
    const track = loadTrack(json, { moving: true, courseShape: false });
    track.courseShape = shape;
    assert.equal(json.nodes.length, source.nodes.length);
    json.nodes.forEach((n, i) => {
      assert.equal(n.p[1], source.nodes[i].p[1], 'preserve height at node ' + i);
      assert.deepEqual(n.flags, source.nodes[i].flags);
      assert.equal(n.w, source.nodes[i].w);
    });
    assert.deepEqual(source, read(id), 'authoring input is not modified');
    assert.ok(track.lengthM < maximumLength, 'avoid padding the course with redundant full turns');
    let minRadius = Infinity, minStation = 0, maxTurn = 0;
    for (let s = 0; s < track.lengthM; s += 2) {
      const frames = [-2, 0, 2, 60].map(d => track.frameAt(s + d));
      if (frames.some(f => f.flags.includes('jump') || Math.abs(f.t[1]) > .6)) continue;
      const radius = 4 / Math.max(1e-9, Math.abs(delta(heading(track, s - 2), heading(track, s + 2))));
      if (radius < minRadius) { minRadius = radius; minStation = s; }
      maxTurn = Math.max(maxTurn, Math.abs(delta(heading(track, s), heading(track, s + 60))) * 180 / Math.PI);
    }
    assert.ok(minRadius > 110, 'real loaded spline radius ' + minRadius + ' at ' + minStation + ' ' + track.sections[track.sectionAt(minStation)].id);
    assert.ok(maxTurn < 35, 'turn within 60m ' + maxTurn);
    const samples = [];
    for (let s = 0; s < track.lengthM; s += 8) samples.push({ s, f: track.frameAt(s) });
    const overlaps = (a, b) => Math.hypot(a.p[0] - b.p[0], a.p[2] - b.p[2]) < (a.w + b.w) / 2 + 2;
    const gap = (a, b) => Math.abs(a.p[1] - b.p[1]) - Math.abs(a.b[1]) * a.w / 2 - Math.abs(b.b[1]) * b.w / 2;
    for (let i = 0; i < samples.length; i++) for (let j = i + 16; j < samples.length; j++) {
      const a = samples[i], b = samples[j], ds = Math.min(b.s - a.s, track.lengthM - b.s + a.s);
      if (ds < Math.max(100, (a.f.w + b.f.w) / 2 + 10) || !overlaps(a.f, b.f)) continue;
      assert.ok(gap(a.f, b.f) >= 9, 'nonlocal road clearance at ' + a.s + '/' + b.s);
    }
    for (const fork of track.forks || []) for (const f of fork.samples(4)) for (const row of samples) {
      const junction = f.localS < 100 && Math.abs(row.s - fork.fromS) < 100
        || f.localS > fork.lengthM - 100 && Math.abs(row.s - fork.toS) < 100;
      if (junction || !overlaps(f, row.f)) continue;
      assert.ok(gap(f, row.f) >= 9, fork.id + ' clears unrelated main road at ' + row.s);
    }
    for (const route of track.routes) assert.ok(route.land.toS > route.land.fromS && route.land.fromS > route.fromS);
    if (id === 'keep') {
      const layout = getKeepLayout(track), ascent = layout.landmarks.find(p => p.id === 'tower-spiral');
      assert.ok(layout.gates.filter(g => g.id.startsWith('ascent-')).every(g => g.s > ascent.s0 && g.s < ascent.s1));
    }
    if (id === 'boudoir') {
      const layout = getBoudoirLayout(track);
      assert.ok(layout.furniture.every(f => Number.isFinite(f.x + f.z) && f.radiusM > 100));
      for (const f of json.forks) {
        assert.deepEqual(f.points[0].p, json.nodes[f.from].p);
        assert.deepEqual(f.points.at(-1).p, json.nodes[f.to].p);
      }
    }
    console.log(id, JSON.stringify({ lengthM: track.lengthM, minRadiusM: minRadius, maximum60mTurnDeg: maxTurn, pads: track.boosts.length }));
}
