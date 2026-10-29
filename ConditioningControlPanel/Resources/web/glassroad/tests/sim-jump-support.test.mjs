import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { buildMainframe } from '../dev/gen-mainframe.mjs';
import { loadTrack } from '../track/load.js';
import { S, createState, setPos, setQuat, setVel } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { crossedSupportedRoad } from '../sim/support.js';
import { qlook } from '../core/math.js';
import { makeFeel, acts, recorder } from './sim-fake.mjs';

const track = loadTrack(buildMainframe().track);
const keep = loadTrack(JSON.parse(readFileSync(new URL('../tracks/keep.json', import.meta.url), 'utf8')));
const cases = [
  [track, [[15, 0, 35], [21, 0, -35], [75, .8, 0], [153, -.8, 0], [165, -.8, 0], [219, .8, 0], [357, .8, 0]]],
  [keep, [[30, 0, 35], [30, .8, 35]]],
];
for (const [track, jumps] of cases) for (const [node, lane, heading] of jumps) {
  test(`${track.id} hop lands on twisted road at node ${node}, lane ${lane}`, () => {
    const s = track.nodeS[node] + 1, f = track.surfaceAt(s, lane * track.frameAt(s).w / 2);
    const st = createState(), feel = makeFeel(), ev = recorder();
    const angle = heading * Math.PI / 180, dir = f.t.map((v, k) => v * Math.cos(angle) + f.b[k] * Math.sin(angle));
    setPos(st, f.p); setQuat(st, qlook([0, 0, 0, 1], dir, f.n)); setVel(st, dir.map(v => v * 45));
    st[S.seg] = st[S.latchSeg] = f.seg; st[S.s] = s; st[S.lateral] = lane * f.w / 2;
    st[S.grounded] = 1; st[S.hopT] = 1000; st[S.engineV] = 45;
    const input = acts({ throttle: 1, hopPressed: true });
    for (let tick = 0; tick < 180 && !ev.first('land'); tick++) {
      step(st, input, track, feel, 1 / 60, ev); input.hopPressed = false;
      const q = track.query(st[S.posX], st[S.posY], st[S.posZ], st[S.seg]);
      assert.ok(!q.onRoad || q.height >= -.03, `fell inside the road at ${q.s}: ${q.height}`);
      assert.equal(st[S.respawnPhase], 0, 'ordinary hop must not require rescue');
    }
    assert.ok(ev.first('land'), 'jump reaches the supported road');
  });
}

test('curved contact fallback rejects undersides, unrelated decks, holes and other forks', () => {
  const before = { onRoad: true, seg: 20, height: .04 }, after = { onRoad: true, seg: 21, height: -.15 };
  assert.equal(crossedSupportedRoad(track, before, after), true);
  assert.equal(crossedSupportedRoad(track, { ...before, height: -.2 }, after), false);
  assert.equal(crossedSupportedRoad(track, before, { ...after, seg: 45 }), false);
  assert.equal(crossedSupportedRoad(track, { ...before, onRoad: false }, after), false);
  assert.equal(crossedSupportedRoad(track, before, { ...after, onRoad: false }), false);
  assert.equal(crossedSupportedRoad(track, before, { ...after, forkId: 'lower-road' }), false);
  assert.equal(crossedSupportedRoad(track, before, { ...after, height: .1 }), false);
  assert.equal(crossedSupportedRoad(track, { ...before, seg: track.nodeCount - 1 }, { ...after, seg: 0 }), true);
});
