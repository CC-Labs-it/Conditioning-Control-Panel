import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { S } from '../sim/state.js';
import { makeFeel, place, run, acts, recorder } from './sim-fake.mjs';

const json = JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8'));
const track = loadTrack(json);

test('width: expanded surface and rails agree with the ground probe, including joins', () => {
  const rails = track.meshes.rails;
  let widened = 0, tapered = 0;
  for (let i = 0; i < rails.positions.length / 3; i++) {
    const f = track.frameAt(rails.uvs[i * 2 + 1]);
    const lat = [0, 1, 2].reduce((sum, k) => sum + (rails.positions[i * 3 + k] - f.p[k]) * f.b[k], 0);
    assert.ok(Math.abs(Math.abs(lat) - f.w / 2) < 0.081, 'rail follows the true road edge');
    if (f.w >= 17.5) widened++;
    if (f.w > 14 && f.w < 16) tapered++;
  }
  assert.ok(widened > 500 && tapered > 10);
  // stones wave: 400 sat inside the bridge's stone field (no deck there), the probe moved to the railed deck after it
  for (const s of [30, 120, 260, 450, track.jumps[0].landS + 3]) {
    const f = track.frameAt(s);
    for (const side of [-1, 1]) {
      for (const inset of [0.25, -0.25]) {
        const p = f.p.map((v, k) => v + f.b[k] * side * (f.w / 2 - inset));
        const q = track.query(...p, f.seg);
        assert.equal(q.onRoad, inset > 0, 'road contact ends at the drawn edge');
        assert.ok(Math.abs(q.edgeDist - inset) < 0.001);
      }
    }
  }
});

test('width: every change eases monotonically between node widths without a sudden edge step', () => {
  let changes = 0;
  for (let i = 0; i < json.nodes.length; i++) {
    const a = json.nodes[i].w, b = json.nodes[(i + 1) % json.nodes.length].w;
    if (a === b) continue;
    changes++;
    const s0 = track.nodeS[i], s1 = track.nodeS[i + 1];
    let last = a;
    for (let j = 1; j <= 100; j++) {
      const w = track.frameAt(s0 + (s1 - s0) * j / 100).w;
      assert.ok(w >= Math.min(a, b) - 1e-7 && w <= Math.max(a, b) + 1e-7);
      assert.ok((w - last) * (b - a) >= -1e-7);
      // Widening the ordinary road changes metres per metre where it meets unchanged technical
      // widths. Preserve the shape limit: a monotone cubic's slope cannot exceed 3x its mean.
      assert.ok(Math.abs(w - last) <= Math.abs(b - a) * 3 / 100 + 1e-7, 'no 1% station step exceeds 3% of the complete width transition');
      last = w;
    }
  }
  assert.ok(changes >= 6);
});

test('width: steering out then counter-steering uses the extra room instead of hitting the old rail', () => {
  const narrow = structuredClone(json);
  // The home straight grew from 20m to 28m. Rebuild its actual former boundary for comparison.
  assert.equal(track.frameAt(15).w, 28);
  for (const node of narrow.nodes) if (node.w === 28) node.w = 20;
  const oldTrack = loadTrack(narrow), feel = makeFeel();
  const formerEdge = oldTrack.frameAt(15).w / 2, newEdge = track.frameAt(15).w / 2;
  for (const side of [-1, 1]) {
    for (const t of [track, oldTrack]) {
      const st = place(t, { s: 15, lateral: (formerEdge - .75) * side, speed: 24 }), rec = recorder();
      run(st, t, feel, acts({ throttle: 1, steer: 0.5 * side }), 0.4, rec);
      run(st, t, feel, acts({ throttle: 1, steer: -0.5 * side }), 0.4, rec);
      run(st, t, feel, acts({ throttle: 1 }), 0.4, rec);
      if (t === track) {
        assert.ok(Math.abs(st[S.lateral]) > formerEdge + .1, 'travels beyond the former road edge');
        assert.ok(Math.abs(st[S.lateral]) < newEdge - 2, 'keeps at least 2m of room inside the new rail');
        assert.ok(st[S.grounded] && st[S.engineV] > 29);
        assert.ok(!rec.names().some((n) => ['wall', 'drop', 'edge-enter'].includes(n)), rec.names().join(','));
      } else assert.ok(rec.first('wall'), 'the same maneuver hits the old boundary');
    }
  }
});

test('width: both added shoulders clear the jump and land from cruise through boost speed', () => {
  for (const speed of [18, 24, 30, 37.2]) for (const lateral of [-7.5, 7.5]) {
    const st = place(track, { s: track.jumps[0].s - 5, lateral, speed }), rec = recorder();
    const feel = makeFeel(), action = acts({ throttle: 1 });
    if (speed > 30) st[S.boostT] = 2;
    run(st, track, feel, action, 2, rec, () => !!rec.first('land'));
    assert.ok(rec.first('airborne') && rec.first('land'), speed + '/' + lateral + ': ' + rec.names());
    // A medium landing immediately starts its authored bounce, so check touchdown and surface contact.
    const q = track.query(st[S.posX], st[S.posY], st[S.posZ], st[S.seg]);
    assert.ok(q.onRoad && st[S.s] > track.jumps[0].landS);
    assert.ok(!rec.names().some((n) => ['wall', 'drop', 'edge-enter'].includes(n)), rec.names().join(','));
  }
});
