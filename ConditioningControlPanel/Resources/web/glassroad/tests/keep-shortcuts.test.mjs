import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { getKeepLayout } from '../track/keep-layout.js';
import { S } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { updateRespawn } from '../sim/respawn.js';
import { createProgress } from '../sim/progress.js';
import { registeredSkip } from '../sim/routes.js';
import { place, makeFeel, recorder, acts, DT } from './sim-fake.mjs';
const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/keep.json', import.meta.url))));
for (const route of track.routes) for (const speed of [20, 30, 40]) test(route.id + ' lands and credits a deliberate inside cut at ' + speed, () => {
  const s = route.openS0 + 3, state = place(track, { s, lateral: track.frameAt(s).w / 2 - 1, speed, headingDeg: 15 });
  const events = recorder(), feel = makeFeel(), action = acts({ throttle: 1 }), progress = createProgress(track, events);
  const race = { phase: 'racing', timeS: 0, lapStartS: 0, finish() {} };
  progress.reset(state); state[S.checkpoint] = track.checklines.filter(c => c.s <= s).length - 1;
  for (let i = 0; i < 420 && !events.all('route-skip').length; i++) {
    step(state, action, track, feel, DT, events); updateRespawn(state, track, feel, DT, events);
    race.timeS += DT; progress.update(state, race);
  }
  assert.equal(events.all('drop').length, 0);
  assert.ok(events.all('route-land').length, 'physical landing');
  assert.ok(events.all('route-skip').length, 'honest race progress');
  assert.ok(state[S.s] - s > 500 && state[S.s] - s < 800);
  assert.equal(registeredSkip(track, route.land.fromS, route.fromS), null, 'backwards traversal is not a skip');
});
test('Keep descending stack queries its own road at every lane', () => {
  const turn = getKeepLayout(track).landmarks.find(p => p.id === 'north-tower');
  for (let s = turn.s0 + 5; s < turn.s1 - 5; s += 4) for (const x of [-.45, 0, .45]) {
    const f = track.frameAt(s), p = f.p.map((v, i) => v + f.b[i] * f.w * x);
    const q = track.query(...p, f.seg);
    assert.ok(q.onRoad && Math.abs(q.s - s) < .1, 'stays on descending level at ' + s);
  }
});
