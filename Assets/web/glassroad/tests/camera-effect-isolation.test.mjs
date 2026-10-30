import test from 'node:test';
import assert from 'node:assert/strict';
import { createCamRig } from '../render/cameraMath.js';
import { createBus } from '../core/events.js';
import { createFx } from '../fx/reactions.js';
import { createGentle } from '../fx/gentle.js';
import { createPhotosafe } from '../fx/photosafe.js';
import { fakeTrack, makeFeel, place } from './sim-fake.mjs';
import { S, BOOST } from '../sim/state.js';

for (const dt of [1 / 60, 1 / 20]) for (const aspect of [16 / 9, 390 / 844]) {
  test(`camera isolates repeated real boost/hit envelopes at ${1 / dt} fps, aspect ${aspect}`, () => {
    const feel = makeFeel(), track = fakeTrack(), bus = createBus();
    const quiet = createCamRig(feel), active = createCamRig(feel);
    quiet.useVel = active.useVel = false;
    const fx = createFx(bus, feel, { gentle: createGentle(feel, { search: '' }), photosafe: createPhotosafe(feel) });
    let effectFrames = 0;
    try {
      for (let n = 0; n < 8 / dt; n++) {
        const state = place(track, { s: 50 + n * dt * 30, speed: 30 });
        state[S.speedPct] = 1;
        if (n % Math.round(1 / dt) === 0) {
          bus.emit('boost-start', { kind: n % 2 ? 'pad' : 'launch', tier: 3 });
          bus.emit('item-hit', { victim: 0, verdict: 'hit' });
          state[S.boostKind] = BOOST.LAUNCH;
        }
        fx.update(dt, state);
        if ((fx.state.traumaOut || 0) > .001) effectFrames++;
        if (!n) { quiet.snap(state, aspect, null, track); active.snap(state, aspect, fx.state, track); }
        else { quiet.update(state, null, dt, aspect, track); active.update(state, fx.state, dt, aspect, track); }
        assert.deepEqual(active.eye, quiet.eye, 'effect cannot change eye');
        assert.deepEqual(active.look, quiet.look, 'effect cannot change aim');
        assert.equal(active.vFov, quiet.vFov, 'effect cannot change zoom');
        assert.equal(active.eye[1], feel.get('camera.eyeUpM'), 'flat road remains flat');
      }
      assert.ok(effectFrames > 30, 'real envelopes were active');
    } finally { fx.dispose(); }
  });
}

test('camera boundary rejects arbitrary shake, tilted push and FOV even on snap', () => {
  const feel = makeFeel(), track = fakeTrack(), state = place(track, { s: 50, speed: 0 });
  const quiet = createCamRig(feel), active = createCamRig(feel);
  quiet.snap(state, 16 / 9, null, track);
  active.snap(state, 16 / 9, { shake: [5, 5, 5], camPush: 9, fovKick: 30 }, track);
  assert.deepEqual(active.eye, quiet.eye); assert.deepEqual(active.look, quiet.look);
  assert.equal(active.vFov, quiet.vFov);
});
