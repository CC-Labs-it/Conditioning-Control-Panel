import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { goonworksTubeShell } from '../render/goonworksGeometry.js';
import { goonworksWorldPlan, goonworksBoxesIntersect } from '../render/goonworksWorld.js';
import { goonworksSetpieces } from '../track/goonworks-setpieces.js';
import { goonworksOverlap } from '../sim/goonworksHazards.js';
import { movingOverlap } from '../sim/movingHazards.js';

const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/goonworks.json', import.meta.url), 'utf8')), { moving: true });

test('both tunnel skins clear the entire driving lane, shoulders and chase camera', () => {
  for (const outer of [false, true]) {
    const tube = goonworksTubeShell(track, outer);
    for (let ring = 0; ring <= tube.rings; ring++) {
      const f = track.frameAt(tube.range.s0 + (tube.range.s1 - tube.range.s0) * ring / tube.rings);
      for (let sector = 0; sector <= tube.sectors; sector++) {
        const i = (ring * (tube.sectors + 1) + sector) * 3;
        const d = f.p.map((v, k) => tube.positions[i + k] - v);
        const x = d.reduce((n, v, k) => n + v * f.b[k], 0);
        const h = d.reduce((n, v, k) => n + v * f.n[k], 0);
        if (Math.abs(x) <= f.w / 2 + 3) assert.ok(h < -2 || h > 14, `wall crosses road at ring ${ring}, x=${x}, h=${h}`);
      }
    }
  }
  assert.ok(!goonworksWorldPlan(track).rows.some(row => row.id === 'rabbit-ceiling-conduit'));
});

test('shutters and blocks forgive grazing edges while centre hits remain solid', () => {
  for (const row of goonworksSetpieces(track).filter(r => ['shutter', 'block'].includes(r.kind))) {
    const pose = { x: row.x, height: row.h || row.height, active: true };
    assert.equal(goonworksOverlap(row, pose, { x: row.x, along: 0, h: 0 }), true);
    assert.equal(goonworksOverlap(row, pose, { x: row.x + row.w / 2, along: 0, h: 0 }), false);
    assert.equal(goonworksOverlap(row, pose, { x: row.x, along: row.depth / 2 + .5, h: 0 }), false);
  }
});

test('side piston contact is inset on both axes without changing other course defaults', () => {
  for (const row of track.moving.traps) {
    const pose = { x: row.x, active: true };
    assert.equal(movingOverlap(row, pose, { x: row.x, z: 0, h: 0 }), true);
    assert.equal(movingOverlap(row, pose, { x: row.x + row.w / 2, z: 0, h: 0 }), false);
    assert.equal(movingOverlap(row, pose, { x: row.x, z: row.len / 2 + .5, h: 0 }), false);
    assert.equal(movingOverlap({ ...row, hitboxScale: undefined }, pose, { x: row.x + row.w / 2, z: 0, h: 0 }), true);
  }
});

test('the final shortcut is clear of complete scenery bodies and their ink', () => {
  const plan = goonworksWorldPlan(track);
  for (const fork of track.forks) for (let s = 0; s < fork.lengthM; s += 3.5) {
    const f = fork.frameAt(s), corridor = {
      p: f.p.map((v, k) => v + f.n[k] * 6), size: [f.w + 6, 16, 4],
      axes: [f.b, f.n, f.t.map(v => -v)],
    };
    for (const row of plan.rows) {
      if (row.id.startsWith('horizon-') || row.id === 'white-cartoon-cloud') continue;
      const size = row.size.map(v => v + (row.outline ? .45 : 0));
      assert.equal(goonworksBoxesIntersect({ ...row, size }, corridor), false, `${row.id} blocks ${fork.id} at ${s}`);
    }
  }
});
