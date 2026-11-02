import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { validate } from '../track/schema.js';
import { boostExclusions } from '../track/boostPlan.js';

const previousLengths = { threshold: 89.392, keep: 580, boudoir: 300, mainframe: 720, goonworks: 300 };
function contains(triangle, x, s) {
  const [a, b, c] = triangle;
  const cross = (u, v) => (u[0] - x) * (v[1] - s) - (u[1] - s) * (v[0] - x);
  const signs = [cross(a, b), cross(b, c), cross(c, a)];
  return signs.every(v => v >= -1e-5) || signs.every(v => v <= 1e-5);
}

test('authored landing runouts retain their boost exclusion and reject invalid end anchors', () => {
  const json = JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8'));
  const track = loadTrack(json), field = track.fields[0];
  assert.ok(field.boostClearTo > field.to);
  assert.ok(boostExclusions(track).some(([from, to]) => from === field.fromS - 65 && to === track.nodeS[field.boostClearTo] + 25));
  for (const end of [field.to - 1, json.nodes.length + 1, 1.5]) {
    const bad = structuredClone(json); bad.fields[0].boostClearTo = end;
    assert.ok(validate(bad).errors.some(e => e.includes('boostClearTo')));
  }
});

for (const [id, previousLength] of Object.entries(previousLengths)) {
  test(id + ': shorter island section ends on rendered, railed, physically solid road', () => {
    const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url), 'utf8')));
    const field = track.fields[0], length = field.toS - field.fromS;
    assert.ok(length / previousLength >= .62 && length / previousLength <= .68, 'about one third less platforming');
    for (const x of field.lanes) {
      const end = Math.max(...field.stones.filter(h => Math.abs(h.x - x) <= h.w / 2).map(h => h.s + h.len));
      assert.ok(field.toS - end <= 2.02, 'last island reaches the new landing deck');
    }
    const road = track.meshes.road, triangles = [];
    for (let i = 0; i < road.indices.length; i += 3) {
      const indices = Array.from(road.indices.slice(i, i + 3));
      if (!indices.every(v => road.normals[v * 3 + 1] > .9)) continue;
      const uv = indices.map(v => [road.uvs[v * 2], road.uvs[v * 2 + 1]]);
      if (Math.max(...uv.map(v => v[1])) < field.toS || Math.min(...uv.map(v => v[1])) > field.fromS + previousLength) continue;
      triangles.push(uv);
    }
    for (let s = field.toS + .25; s < field.fromS + previousLength - .1; s += 5) {
      const frame = track.frameAt(s);
      assert.ok(frame.flags.includes('rail'), 'new runout restores side rails');
      for (const lane of field.lanes) {
        const x = Math.max(-frame.w / 2 + 3, Math.min(frame.w / 2 - 3, lane));
        const p = frame.p.map((v, i) => v + frame.b[i] * x + frame.n[i] * .1);
        const q = track.query(...p, frame.seg);
        assert.ok(q.onRoad && !q.stone && !q.hole && Math.abs(q.s - s) < .01, 'former void now supports the kart');
        assert.ok(triangles.some(t => contains(t, x, s)), 'visible road top covers the former void');
      }
    }
  });
}
