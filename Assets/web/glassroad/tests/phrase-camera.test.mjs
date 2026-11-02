import test from 'node:test';
import assert from 'node:assert/strict';
import { createCamRig } from '../render/cameraMath.js';
import { phraseCameraGuide, phraseFlightPoints } from '../core/phraseFlight.js';
import { S } from '../sim/state.js';
import { fakeTrack, place, makeFeel, DT } from './sim-fake.mjs';

test('phrase camera follows the route grade while the kart arcs visibly above it', () => {
  const track = fakeTrack(), zone = { gapS0: 100, gapS1: 240, landingS1: 270 };
  track.aerials = [zone];
  const feel = makeFeel(), rig = createCamRig(feel), baseline = createCamRig(feel);
  rig.useVel = baseline.useVel = false;
  let maxEyeBob = 0, maxAimBob = 0, maxKartArc = 0, previousY = 0, previousBaseY = 0;
  const points = phraseFlightPoints(track, zone);
  for (let tick = 0; tick <= 420; tick++) {
    const s = 90 + tick * DT * 24, guide = phraseCameraGuide(track, s), baseY = guide?.y || 0;
    const leg = s < points[1].s ? points.slice(0, 2) : points.slice(1);
    const u = Math.max(0, Math.min(1, (s - leg[0].s) / (leg[1].s - leg[0].s)));
    const arc = s >= zone.gapS0 && s <= zone.gapS1 ? 1.8 * Math.sin(Math.PI * u) ** 2 : 0;
    const k = place(track, { s, speed: 24, height: baseY + arc, airborne: arc > 0 });
    const flat = place(track, { s, speed: 24, height: baseY, airborne: arc > 0 });
    k[S.velY] = tick ? (baseY + arc - previousY) / DT : 0;
    flat[S.velY] = tick ? (baseY - previousBaseY) / DT : 0;
    if (!tick) { rig.snap(k, 16 / 9, null, track); baseline.snap(flat, 16 / 9, null, track); }
    else { rig.update(k, null, DT, 16 / 9, track); baseline.update(flat, null, DT, 16 / 9, track); }
    if (guide?.weight > .99) {
      maxEyeBob = Math.max(maxEyeBob, Math.abs(rig.eye[1] - baseline.eye[1]));
      const pitch = r => Math.atan2(r.look[1] - r.eye[1], Math.hypot(r.look[0] - r.eye[0], r.look[2] - r.eye[2]));
      maxAimBob = Math.max(maxAimBob, Math.abs(pitch(rig) - pitch(baseline)));
      maxKartArc = Math.max(maxKartArc, arc);
      assert.deepEqual(rig.up, [0, 1, 0], 'horizon stays level through both legs');
    }
    previousY = baseY + arc; previousBaseY = baseY;
  }
  assert.ok(maxKartArc > 1.7, 'the kart still has a real jump');
  assert.ok(maxEyeBob < .3, `camera ignores most of the arc: ${maxEyeBob}`);
  assert.ok(maxAimBob < .05, `aim does not chase the kart vertically: ${maxAimBob}`);
});
