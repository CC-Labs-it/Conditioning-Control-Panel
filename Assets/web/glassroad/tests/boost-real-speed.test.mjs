import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { BOOST_TURN, boostTurnRadius } from '../track/boostSafety.js';
import { step } from '../sim/kart.js';
import { S } from '../sim/state.js';
import { calmMul } from '../sim/steer.js';
import { steerTo } from '../dev/hook.js';
import { makeFeel, place, acts, recorder, DT } from './sim-fake.mjs';

for (const id of ['threshold', 'keep', 'mainframe', 'boudoir', 'goonworks']) test(id + ': worst boost exits work at maximum solo clean-music speed', () => {
  const json = JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url), 'utf8'));
  const track = loadTrack(json, { moving: true });
  assert.ok(track.boosts.length >= 6, 'preserve useful boosts in every course');
  const worst = track.boosts.map(pad => ({ pad, clearance: boostTurnRadius(track, pad, true) }))
    .sort((a, b) => a.clearance - b.clearance).slice(0, 3);
  for (const { pad } of worst) for (const speed of [36, BOOST_TURN.padSpeedMps, BOOST_TURN.padSpeedMps / 1.2 * 1.24]) {
    const top = speed > BOOST_TURN.padSpeedMps ? BOOST_TURN.padSpeedMps / 1.2 : speed / 1.2;
    const feel = makeFeel({ 'speed.topMps': top }), events = recorder();
    const st = place(track, { s: pad.s - 12, lateral: pad.x, speed });
    let distance = 0, previous = st[S.s];
    for (let i = 0; i < 18 / DT && distance < 12 + pad.len + BOOST_TURN.rolloutM; i++) {
      const steer = steerTo(st, track, st[S.s] + 14, pad.x, 3) / Math.max(.2, calmMul(st[S.engineV], feel));
      const travelled = Math.max(0, distance - 12 - pad.len);
      const slow = travelled > BOOST_TURN.forcedM;
      const braking = slow && st[S.engineV] > 30.5;
      const throttle = slow ? (st[S.engineV] < 29.5 ? .2 : 0) : 1;
      step(st, acts({ throttle, brake: braking ? 1 : 0, steer }), track, feel, DT, events);
      let ds = st[S.s] - previous;
      if (ds < -track.lengthM / 2) ds += track.lengthM;
      distance += ds; previous = st[S.s];
    }
    assert.ok(events.all('pad').length >= 1, 'real pad pays at ' + pad.s);
    assert.equal(events.all('wall').length, 0, 'wall-free boosted runout at ' + pad.s + ' speed ' + speed);
    assert.equal(events.all('drop').length, 0, 'supported boosted runout at ' + pad.s);
    assert.ok(distance >= 12 + pad.len + BOOST_TURN.rolloutM, 'complete rollout at ' + pad.s);
  }
});
