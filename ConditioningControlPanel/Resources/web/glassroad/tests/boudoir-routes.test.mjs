import test from 'node:test';
import assert from 'node:assert/strict';
import { buildBoudoir } from '../dev/gen-boudoir.mjs';
import { getBoudoirLayout } from '../track/boudoir-layout.js';
import { loadTrack } from '../track/load.js';
import { S } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { updateRespawn } from '../sim/respawn.js';
import { createProgress } from '../sim/progress.js';
import { registeredSkip } from '../sim/routes.js';
import { place, makeFeel, recorder, acts, DT } from './sim-fake.mjs';
const track = loadTrack(buildBoudoir());
for (const route of track.routes) for (const speed of [20, 30, 40]) test(route.id + ' physically lands before crediting its shortcut at ' + speed, () => {
  const s = route.openS0 + 3, st = place(track, { s, lateral: track.frameAt(s).w / 2 - 1, speed, headingDeg: 15 });
  const events = recorder(), feel = makeFeel(), action = acts({ throttle: 1 }), progress = createProgress(track, events);
  const core = getBoudoirLayout(track).furniture.find(f => f.id === 'wardrobe-descent');
  const race = { phase: 'racing', timeS: 0, lapStartS: 0, finish() {} };
  progress.reset(st); st[S.checkpoint] = track.checklines.filter(c => c.s <= s).length - 1;
  for (let i = 0; i < 420 && !events.all('route-skip').length; i++) {
    step(st, action, track, feel, DT, events); updateRespawn(st, track, feel, DT, events); race.timeS += DT; progress.update(st, race);
    assert.ok(Math.hypot(st[S.posX] - core.x, st[S.posZ] - core.z) > core.radiusM + 2, 'shortcut clears wardrobe core');
  }
  assert.equal(events.all('drop').length, 0);
  assert.ok(events.all('route-land').length, 'physical landing ' + JSON.stringify(events.list));
  assert.ok(events.all('route-skip').length, 'honest progress');
  assert.ok(st[S.s] >= route.land.fromS && st[S.s] <= route.land.toS, 'skip ends on the physical widened landing stretch');
  assert.equal(registeredSkip(track, route.land.fromS, route.fromS), null);
});
test('Shelf ascent and wardrobe descent keep all lanes attached to their correct vertical layer', () => {
  const layout = getBoudoirLayout(track);
  for (const id of ['shelf-climb', 'wardrobe-descent']) {
    const span = layout.landmarks.find(p => p.id === id);
    for (let s = span.s0 + 5; s < span.s1 - 5; s += 5) for (const x of [-.45, 0, .45]) {
      const f = track.frameAt(s), p = f.p.map((v, i) => v + f.b[i] * f.w * x), q = track.query(...p, f.seg);
      const fork = track.forks?.find(path => path.id === q.forkId);
      const mouth = fork && (q.forkS < fork.mouthM || q.forkS > fork.lengthM - fork.mouthM);
      assert.ok(q.onRoad && (Math.abs(q.s - s) < .1 || (mouth && Math.abs(q.height) < .65)), id + ' layer at ' + s);
    }
  }
});
