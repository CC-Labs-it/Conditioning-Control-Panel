import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';

for (const id of ['threshold','keep','mainframe','boudoir','goonworks']) test(id + ': broad main lanes outside shared branch aprons keep their physical ground', () => {
  const json = JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url), 'utf8'));
  const t = loadTrack(json, { moving: true });
  let samples = 0;
  for (let s = 1; s < t.lengthM; s += 17) {
    const f = t.frameAt(s);
    if (f.flags.includes('jump') || f.flags.includes('magnet') || f.n[1] < .6
      || t.fields.some(c => s >= c.fromS - 5 && s <= c.toS + 5)
      || t.courts.some(c => s >= c.fromS - 5 && s <= c.toS + 5)
      || t.forks.some(fork => s >= fork.fromS - 20 && s <= fork.toS + 20)) continue;
    for (const x of [-f.w * .25, 0, f.w * .25]) {
      const surface = t.surfaceAt(s, x), p = surface.p.map((v,k) => v + surface.n[k] * .1);
      for (const hint of [f.seg, -1]) {
        const q = t.query(...p, hint), ds = Math.abs(q.s - s);
        assert.ok(q.onRoad, 'supported lane at ' + s + '/' + x + ' hint ' + hint);
        assert.equal(q.forkId, null, 'an unrelated branch cannot own this main-road floor');
        assert.ok(Math.min(ds, t.lengthM - ds) < .2, 'same physical deck at ' + s);
        assert.ok(Math.abs(q.height - .1) < .02, 'actual curved surface height at ' + s);
      }
      samples++;
    }
  }
  assert.ok(samples > 800, 'whole-course physical sampling');
});
