import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createCamRig } from '../render/cameraMath.js';
import { loadTrack } from '../track/load.js';
import { createState, S, setPos, setVel, setQuat } from '../sim/state.js';
import { qfromBasis } from '../core/math.js';
import { makeFeel, DT } from './sim-fake.mjs';
import { RACER_FRAMING } from '../render/framing.js';

const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const dot = (a, b) => a.reduce((sum, x, i) => sum + x * b[i], 0);
const norm = (v) => { const d = Math.hypot(...v); return v.map(x => x / d); };
function pose(k, f, speed = 30) {
  setPos(k, f.p); setVel(k, f.t.map(v => v * speed));
  setQuat(k, qfromBasis([0, 0, 0, 1], cross(f.t, f.n), f.n, f.t.map(v => -v)));
  k[S.seg] = f.seg || 0; k[S.grounded] = 1; k[S.airborne] = 0; k[S.speedPct] = speed / 30;
}
function framing(rig, k, f) {
  const forward = norm(rig.look.map((v, i) => v - rig.eye[i])), right = norm(cross(forward, rig.up)), up = cross(right, forward);
  const p = [k[S.posX], k[S.posY], k[S.posZ]].map((v, i) => v + f.n[i] * RACER_FRAMING.centerUpM * RACER_FRAMING.scale - rig.eye[i]);
  const screenY = 0.5 - dot(p, up) / dot(p, forward) / Math.tan(rig.vFov * Math.PI / 360) / 2;
  assert.ok(Math.abs(screenY - RACER_FRAMING.screenY) < 1e-6, 'accepted vertical framing');
  assert.ok(Math.abs(dot(p, right)) < 1e-5, 'racer stays horizontally centred');
}

test('grounded 30m/s ramp chase stays above the road while retaining the level horizon and framing', () => {
  for (const gentle of [false, true]) for (const slope of [-0.3, 0.3]) {
    const feel = makeFeel({ 'gentle.on': gentle }), rig = createCamRig(feel), k = createState(); rig.useVel = false;
    const t = norm([0, slope, -1]), n = norm([0, 1, slope]);
    let minClearance = Infinity;
    for (let tick = 0; tick <= 240; tick++) {
      const f = { p: t.map(v => v * tick * DT * 30), t, n }; pose(k, f);
      if (!tick) rig.snap(k, 16 / 9); else rig.update(k, null, DT, 16 / 9);
      minClearance = Math.min(minClearance, dot(rig.eye.map((v, i) => v - f.p[i]), n));
      framing(rig, k, f); assert.deepEqual(rig.up, [0, 1, 0]);
    }
    assert.ok(minClearance > 1.5, `clearance=${minClearance}, slope=${slope}, gentle=${gentle}`);
  }
});
