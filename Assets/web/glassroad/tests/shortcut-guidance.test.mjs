import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { placeKart } from '../dev/hook.js';
import { S, createState } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { registeredSkip, guideLanding } from '../sim/routes.js';
import { createActions } from '../core/actions.js';
import { qrotate } from '../core/math.js';
import { makeFeel, DT } from './sim-fake.mjs';

for (const id of ['threshold', 'keep', 'mainframe', 'boudoir', 'goonworks']) {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url), 'utf8')));
  test(id + ': forgiving entries physically land and finish pointing along the exit', t => {
    let passed = 0, total = 0;
    for (const r of track.routes) {
      assert.equal(r.openS0, r.fromS);
      assert.ok(r.openS1<=r.toS && r.openS1-r.openS0>=14,'physical opening fits its authored anchor range');
      if (id==='threshold') assert.equal(r.openS1-r.openS0,24,'the broadened coil retains its deliberate 24m mouth');
      for (const ds of [2, 6, 10, 14]) for (const v of [20, 30, 40]) for (const yaw of [25, 40, 55]) {
        total++;
        const st = createState(), feel = makeFeel(), side = r.side === 'L' ? -1 : 1, start = r.openS0 + ds;
        const frame = track.frameAt(start);
        placeKart(st, track, feel, start, side * (frame.w / 2 - 3), v, yaw * side);
        const a = createActions(); a.throttle = 1;
        let land = null, landedAt = -1, drop = false;
        const ev = { emit(n, p) { if (n === 'route-land') land = p; if (n === 'drop') drop = true; } };
        for (let i = 0; i < 5 / DT; i++) {
          step(st, a, track, feel, DT, ev);
          if (land && landedAt < 0) landedAt = i;
          if (st[S.respawnPhase] || (land && i > landedAt + 45)) break;
        }
        const label = `${r.id} offset ${ds}, speed ${v}, yaw ${yaw}`;
        if (v <= 30 || ds >= 10 && yaw === 25) {
          assert.ok(land && !drop, label + ': ordinary entries and fast runouts land');
        }
        if (!land) continue;
        passed++;
        assert.equal(registeredSkip(track, start, land.s)?.id, r.id, label + ': registered physical landing');
        const forward = qrotate([], Array.from(st.slice(S.quatX, S.quatW + 1)), [0, 0, -1]);
        const tangent = track.frameAt(st[S.s]).t;
        assert.ok(forward.reduce((sum, x, k) => sum + x * tangent[k], 0) > Math.cos(25 * Math.PI / 180), label + ': clear forward exit');
      }
    }
    t.diagnostic(`${passed}/${total} unsteered entry variations landed`);
    assert.ok(passed >= total * 0.85);
  });
  test(id + ': large landing arrows follow the actual forward road tangent', () => {
    const m = track.meshes.routeLand, positions = [];
    for (let i = 0; i < m.uvs.length / 2; i++) {
      if (Math.abs(m.uvs[i * 2 + 1] - 1.3) < 1e-5) positions.push(Array.from(m.positions.slice(i * 3, i * 3 + 3)));
    }
    assert.ok(positions.length >= track.routes.length * 24);
    for (let i = 0; i < positions.length; i += 12) {
      const tip = positions[i], tail = positions[i + 8];
      const q = track.query(...tip), tangent = track.frameAt(q.s).t;
      assert.ok(tip.reduce((sum, x, k) => sum + (x - tail[k]) * tangent[k], 0) > 5);
    }
  });
  test(id + ': counter-steering through a marked entrance never hits an invisible rail', () => {
    for (const r of track.routes) {
      const feel = makeFeel(), st = createState(), side = r.side === 'L' ? -1 : 1, s = (r.openS0 + r.openS1) / 2;
      placeKart(st, track, feel, s, side * (track.frameAt(s).w / 2 - 0.2), 20, side * 45);
      st[S.steerSlew] = -side;
      const a = createActions(); a.throttle = 1; a.steer = -side;
      const names = [];
      step(st, a, track, feel, DT, { emit: n => names.push(n) });
      assert.ok(!names.includes('wall'), r.id);
    }
  });
}

test('ordinary hops and driving never acquire a landing turn guide', () => {
  const st = createState(), F = [1, 0, 0];
  guideLanding({}, st, {}, F, makeFeel(), DT);
  assert.deepEqual(F, [1, 0, 0]);
});
