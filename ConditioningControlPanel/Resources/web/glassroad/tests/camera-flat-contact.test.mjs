import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { step } from '../sim/kart.js';
import { S } from '../sim/state.js';
import { createCamRig, createKick } from '../render/cameraMath.js';
import { makeFeel, place, acts, recorder, DT } from './sim-fake.mjs';

test('Mainframe flat opening keeps camera height through repeated glancing contacts', () => {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/mainframe.json', import.meta.url), 'utf8')));
  const feel = makeFeel(), st = place(track, { s: 12, speed: 28 }), rig = createCamRig(feel), rec = recorder();
  rig.useVel = false; rig.snap(st, 16 / 9, null, track);
  let maxCameraStep = 0, maxSide = 0, lastY = rig.eye[1], contacts = 0;
  for (let tick = 0; st[S.s] < 150; tick++) {
    step(st, acts({ throttle: 1 }), track, feel, DT, rec);
    if (tick % 18 === 0) { rig.hit('bump', [2.2, 1.6, 1.51][contacts++ % 3], 1); }
    rig.update(st, null, DT, 16 / 9, track);
    const support = track.query(st[S.posX], st[S.posY], st[S.posZ], st[S.seg]);
    assert.ok(support.onRoad && Math.abs(support.height) < 0.001);
    assert.equal(st[S.airborne], 0);
    maxCameraStep = Math.max(maxCameraStep, Math.abs(rig.eye[1] - lastY)); lastY = rig.eye[1];
    maxSide = Math.max(maxSide, Math.abs(rig.kick.out.x));
  }
  assert.ok(contacts > 10 && maxSide > 0.05, 'real contact response remains visible');
  assert.ok(maxCameraStep < 0.001, 'no false floor-step camera lift: ' + maxCameraStep);
});

test('vertical collision lift rises continuously for hard hits while landing dips remain', () => {
  const peaks = [];
  for (const speed of [5.9, 6, 6.1, 7.5, 9]) {
    const kick = createKick(); kick.hit('wall', speed, 1);
    let peak = 0;
    for (let t = 0; t < 1; t += DT) peak = Math.max(peak, kick.update(DT).y);
    peaks.push(peak);
  }
  assert.equal(peaks[0], 0); assert.equal(peaks[1], 0);
  assert.ok(peaks[2] > 0 && peaks[2] < 0.01);
  assert.ok(peaks[3] > peaks[2] && peaks[4] > 0.12);
  const landing = createKick(); landing.hit('land', 2);
  assert.ok(landing.update(DT).y < -0.1, 'real landing still dips');
});
