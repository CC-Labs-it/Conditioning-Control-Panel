import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { getKeepLayout } from '../track/keep-layout.js';
import { keepDetails } from '../render/keepDetails.js';

test('Keep tablet piers leave every driving lane clear through widening approaches', () => {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/keep.json', import.meta.url), 'utf8')));
  const piers = keepDetails(track, getKeepLayout(track)).boxes.filter(b => b.name === 'crossing-tablet-pier');
  assert.equal(piers.length, 16);
  for (let s = 0; s < track.lengthM; s++) {
    const f = track.frameAt(s);
    for (const lateral of [0, -f.w / 2 + .7, f.w / 2 - .7]) for (const height of [.03, 1.4, 6.5]) {
      const p = f.p.map((v, i) => v + f.b[i] * lateral + f.n[i] * height);
      for (const pier of piers) {
        const dx = p[0] - pier.p[0], dy = p[1] - pier.p[1], dz = p[2] - pier.p[2];
        const c = Math.cos(pier.yaw), n = Math.sin(pier.yaw);
        const x = dx * c - dz * n, z = dx * n + dz * c;
        assert.ok(!(Math.abs(x) < pier.size[0] * .499 && Math.abs(dy) < pier.size[1] * .499 &&
          Math.abs(z) < pier.size[2] * .499), 'pier obstructs the road at ' + s);
      }
    }
  }
});
