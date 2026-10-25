import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { AERIAL_REVISION, aerialAt, aerialGapAt } from '../track/aerial.js';
import { getKeepLayout } from '../track/keep-layout.js';
import { getBoudoirLayout } from '../track/boudoir-layout.js';
import { getBoudoirSetpieces } from '../track/boudoir-setpieces.js';
import { goonworksSetpieces } from '../track/goonworks-setpieces.js';
import { industryZones } from '../sim/mainframeIndustry.js';
import { scanZones } from '../sim/mainframeScan.js';

const ids = ['threshold', 'keep', 'boudoir', 'mainframe', 'goonworks'];
const dot = (a, b) => a.reduce((v, n, i) => v + n * b[i], 0);
for (const id of ids) {
  const json = JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url), 'utf8'));
  const track = loadTrack(json);
  test(id + ': two long dry aerial corridors have wide supported approaches and exits', () => {
    assert.equal(AERIAL_REVISION, 1);
    assert.equal(track.aerials.length, 2);
    for (const a of track.aerials) {
      assert.ok(a.gapS1 - a.gapS0 >= 89.9 && a.gapS1 - a.gapS0 <= 130);
      assert.equal(a.rampS1, a.gapS0); assert.equal(a.landingS0, a.gapS1);
      assert.ok(a.gapS0 - a.rampS0 >= 20 && a.landingS1 - a.gapS1 >= 35);
      assert.ok(a.widthM >= 28 - 1e-7);
      const launch = track.frameAt(a.gapS0), land = track.frameAt(a.gapS1);
      assert.ok(dot(launch.t, land.t) > .995, 'launch and exit align within six degrees');
      for (const s of [a.rampS0 + 1, a.gapS0 - 1, a.gapS1 + 1, a.landingS1 - 1]) {
        const f = track.frameAt(s), q = track.query(...f.p, f.seg);
        assert.ok(q.onRoad, a.id + ' supported approach/runout ' + s);
        assert.ok(['glass', 'grip', 'haze'].includes(q.surface));
        assert.equal(aerialAt(track, s), a);
      }
      for (const fd of track.fields) assert.ok(a.landingS1 <= fd.fromS || a.rampS0 >= fd.toS);
      for (const r of track.routes) {
        assert.ok(a.landingS1 <= r.fromS || a.rampS0 >= r.toS);
        assert.ok(a.landingS1 <= r.land.fromS || a.rampS0 >= r.land.toS);
      }
      const hazards = id === 'keep' ? [...getKeepLayout(track).gates, ...getKeepLayout(track).chains]
        : id === 'boudoir' ? [...Object.values(getBoudoirSetpieces(track)).flat(), ...getBoudoirLayout(track).perfume]
        : id === 'mainframe' ? [...industryZones(track), ...scanZones(track)]
        : id === 'goonworks' ? goonworksSetpieces(track) : [];
      for (const h of hazards) {
        const lo = h.s0 ?? h.s - (h.len || h.depthM || h.depth || 8) / 2;
        const hi = h.s1 ?? h.s + (h.len || h.depthM || h.depth || 8) / 2;
        assert.ok(a.landingS1 <= lo || a.rampS0 >= hi, a.id + ' overlaps ' + h.id);
      }
      assert.equal(aerialGapAt(track, a.gapS0), a);
      assert.equal(aerialGapAt(track, a.gapS1), null);
      assert.equal(aerialGapAt(track, a.gapS0 + track.lengthM), a);
    }
  });
  test(id + ': every aerial gap removes full-width support and rails, including below probes', () => {
    for (const a of track.aerials) for (let s = a.gapS0 + 2; s < a.gapS1 - 2; s += 4) {
      const f = track.frameAt(s);
      for (const x of [-f.w * .4, 0, f.w * .4]) {
        const p = f.p.map((v, k) => v + f.b[k] * x + f.n[k] * .1);
        const q = track.query(...p, f.seg);
        assert.equal(q.onRoad, false, a.id + ' s ' + s + ' x ' + x);
        assert.equal(q.railed, false);
        assert.ok(q.flags.includes('jump'));
        const below = track.query.below(...p);
        // A genuine different road below remains legal support on a stacked course.
        assert.ok(!below || !aerialGapAt(track, below.s), a.id + ' ghost support');
      }
    }
  });
  test(id + ': mesh and driveline cuts exactly match support with no floating boosts', () => {
    const d = track.driveline, m = track.meshes;
    for (const a of track.aerials) {
      assert.ok(Array.from(d.s).some(s => Math.abs(s - a.gapS0) < 1e-8));
      assert.ok(Array.from(d.s).some(s => Math.abs(s - a.gapS1) < 1e-8));
      for (let i = 0; i < d.quads; i++) if (d.s[i] >= a.gapS0 && d.s[i + 1] <= a.gapS1)
        assert.equal(d.gap[i], 1);
      for (const name of ['road', 'rails', 'edgeStrip', 'edgeHaze']) {
        const data = m[name];
        for (let i = 0; i < data.uvs.length; i += 2) {
          const s = ((data.uvs[i + 1] % track.lengthM) + track.lengthM) % track.lengthM;
          assert.ok(!(s > a.gapS0 + .002 && s < a.gapS1 - .002), name + ' face inside ' + a.id);
        }
      }
      for (const p of track.boosts) assert.ok(p.s + p.len <= a.gapS0 - 85 || p.s >= a.landingS1);
    }
    assert.ok(m.routeEdge.indices.length && m.routeLand.indices.length);
    const arrows = [];
    for (let i = 0; i < m.routeLand.uvs.length / 2; i++) if (Math.abs(m.routeLand.uvs[i * 2 + 1] - 1.25) < 1e-5)
      arrows.push(Array.from(m.routeLand.positions.slice(i * 3, i * 3 + 3)));
    assert.ok(arrows.length > 0, 'aerial landings have visible forward arrows');
    for (let i = 0; i < arrows.length; i += 4) {
      const tip = arrows[i], tail = arrows[i + 2], q = track.query(...tip), tangent = track.frameAt(q.s).t;
      assert.ok(tip.reduce((sum, x, k) => sum + (x - tail[k]) * tangent[k], 0) > 2, 'aerial arrow points forward');
    }
  });
}

test('aerial helpers ignore unsupported courses and non-finite distance', () => {
  const t = { lengthM: 100, aerials: [] };
  assert.equal(aerialAt(t, 30), null); assert.equal(aerialGapAt(t, NaN), null);
});
