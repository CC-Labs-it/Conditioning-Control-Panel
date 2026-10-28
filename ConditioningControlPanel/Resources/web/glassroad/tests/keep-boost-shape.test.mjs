import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { getKeepLayout } from '../track/keep-layout.js';
import { BOOST_TURN, safeBoostTurn } from '../track/boostSafety.js';
import { safeBoostCorridor } from '../track/boostCorridor.js';
import { S } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { updateRespawn } from '../sim/respawn.js';
import { calmMul } from '../sim/steer.js';
import { steerTo } from '../dev/hook.js';
import { makeFeel, place, acts, recorder, DT } from './sim-fake.mjs';

const json = JSON.parse(readFileSync(new URL('../tracks/keep.json', import.meta.url), 'utf8'));
const track = loadTrack(json, { moving: true }), layout = getKeepLayout(track);

test('Keep pays at least six real pads with full supported towing-speed exits on a bounded course', () => {
  assert.ok(track.lengthM < 10000);
  assert.ok(track.boosts.length >= 6, 'added real straight road retains fun boost choices');
  const home = layout.landmarks.find(row => row.id === 'home');
  console.log('Keep boost shape', JSON.stringify({ lengthM: track.lengthM, pads: track.boosts.length, homeM: home.s1 - home.s0 }));
  const original = loadTrack(json, { courseShape: false, boostSafety: false });
  const oldHome = getKeepLayout(original).landmarks.find(row => row.id === 'home');
  assert.ok(home.s1 - home.s0 > oldHome.s1 - oldHome.s0 + 50, 'home gains real supported road after rounded joins');
  for (const pad of track.boosts) {
    assert.ok(safeBoostTurn(track, pad));
    assert.ok(safeBoostCorridor(track, pad));
  }
});

test('all Keep boost trains and complete runouts drive at maximum towing speed and Surge entry', () => {
  const runs = [];
  for (const [index, pad] of track.boosts.entries()) {
    const previous = runs.at(-1)?.at(-1);
    if (previous && pad.s - previous.s < 90 && pad.x === previous.x) runs.at(-1).push({ ...pad, index });
    else runs.push([{ ...pad, index }]);
  }
  assert.equal(runs.reduce((sum, run) => sum + run.length, 0), track.boosts.length);
  for (const run of runs) for (const speed of [64.584, 66.7368]) {
    const feel = makeFeel(), events = recorder();
    feel.set('speed.topMps', 53.82);
    const start = (run[0].s - 12 + track.lengthM) % track.lengthM;
    const st = place(track, { s: start, lateral: run[0].x, speed });
    const target = run.at(-1).s - run[0].s + 12 + run.at(-1).len + BOOST_TURN.rolloutM;
    let travelled = 0, previous = st[S.s];
    for (let i = 0; i < 15 / DT && travelled < target; i++) {
      const steer = steerTo(st, track, st[S.s] + 12, run[0].x, 3) / Math.max(.2, calmMul(st[S.engineV], feel));
      step(st, acts({ throttle: 1, steer }), track, feel, DT, events);
      updateRespawn(st, track, feel, DT, events);
      let distance = st[S.s] - previous;
      if (distance < -track.lengthM / 2) distance += track.lengthM;
      travelled += distance; previous = st[S.s];
    }
    assert.ok(travelled >= target, 'complete train, boost tail, reaction and braking room at ' + speed);
    for (const pad of run) assert.ok(events.all('pad').some(event => event.payload.index === pad.index), 'pad ' + pad.index + ' pays');
    assert.equal(events.all('wall').length, 0);
    assert.equal(events.all('drop').length, 0);
  }
});
