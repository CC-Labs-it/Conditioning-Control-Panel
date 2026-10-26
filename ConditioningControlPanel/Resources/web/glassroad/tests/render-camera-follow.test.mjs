import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createFeel } from '../core/feel.js';
import { createState, S, BOOST, RESPAWN } from '../sim/state.js';
import { qfromBasis, qrotate } from '../core/math.js';
import { loadTrack } from '../track/load.js';
import { createCamRig, aimAtAnchor } from '../render/cameraMath.js';
import { RACER_FRAMING as F } from '../render/framing.js';

const freshFeel = () => createFeel(JSON.parse(readFileSync(new URL('../feel.json', import.meta.url))));
const ASPECTS = [16 / 9, 390 / 844, 844 / 390, 21 / 9], DT = 1 / 60;
const fx0 = () => ({ shake: [0, 0, 0], fovKick: 0, camPush: 0, orbit: 0 });
const dot = (a, b) => a.reduce((s, v, i) => s + v * b[i], 0);
const sub = (a, b) => a.map((v, i) => v - b[i]);
const norm = (a) => { const l = Math.hypot(...a); return a.map((v) => v / l); };
const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];

// Independent look-at projection of the actual state centre, without using rig.anchor or the aim helper.
function project(rig, kart, aspect) {
  const q = [kart[S.quatX], kart[S.quatY], kart[S.quatZ], kart[S.quatW]];
  const u = qrotate([0, 0, 0], q, [0, F.centerUpM * F.scale, 0]);
  const point = [kart[S.posX] + u[0], kart[S.posY] + u[1], kart[S.posZ] + u[2]];
  const forward = norm(sub(rig.look, rig.eye)), right = norm(cross(forward, rig.up)), up = cross(right, forward);
  const delta = sub(point, rig.eye), depth = dot(delta, forward), tan = Math.tan(rig.vFov * Math.PI / 360);
  return { x: 0.5 + dot(delta, right) / depth / tan / aspect / 2, y: 0.5 - dot(delta, up) / depth / tan / 2, depth };
}
function assertAnchor(rig, kart, aspect, targetY = F.screenY) {
  const p = project(rig, kart, aspect);
  assert.ok(p.depth > 0.5, 'racer in front of eye');
  assert.ok(Math.abs(p.x - 0.5) < 2e-6, 'horizontal centre ' + p.x);
  assert.ok(Math.abs(p.y - targetY) < 2e-6, 'vertical centre ' + p.y + ' vs ' + targetY);
}
function setFrame(kart, p, t, n, speed = 24) {
  const r = cross(t, n), q = qfromBasis([0, 0, 0, 1], r, n, t.map((v) => -v));
  [S.posX, S.posY, S.posZ].forEach((i, j) => { kart[i] = p[j]; });
  [S.quatX, S.quatY, S.quatZ, S.quatW].forEach((i, j) => { kart[i] = q[j]; });
  [S.velX, S.velY, S.velZ].forEach((i, j) => { kart[i] = t[j] * speed; });
  kart[S.speedPct] = speed / 30; kart[S.grounded] = 1;
}

test('follow: the raised chase reveals more floor lettering while keeping the racer close and centred', () => {
  for (const aspect of ASPECTS) {
    const beforeFeel = freshFeel(); beforeFeel.set('camera.eyeUpM', 1.95);
    const before = createCamRig(beforeFeel), now = createCamRig(freshFeel()), k = createState();
    setFrame(k, [0, 0, 0], [0, 0, -1], [0, 1, 0], 0);
    before.snap(k, aspect); now.snap(k, aspect);
    assertAnchor(now, k, aspect);
    const screenY = (rig, p) => {
      const forward = norm(sub(rig.look, rig.eye)), right = norm(cross(forward, rig.up));
      const up = cross(right, forward), delta = sub(p, rig.eye);
      return .5 - dot(delta, up) / dot(delta, forward) / Math.tan(rig.vFov * Math.PI / 360) / 2;
    };
    const pitch = rig => Math.atan2(rig.eye[1] - rig.look[1], rig.eye[2] - rig.look[2]);
    const tilt = (pitch(now) - pitch(before)) * 180 / Math.PI;
    assert.ok(tilt > 3 && tilt < 5, 'modest downward tilt: ' + tilt);
    for (const ahead of [8, 16, 28]) {
      // Project the ends of a 4 m stroke lying on the road, independent of the camera aim helper.
      const height = rig => screenY(rig, [0, .16, -ahead + 2]) - screenY(rig, [0, .16, -ahead - 2]);
      assert.ok(height(now) > height(before) * 1.1, 'less foreshortening at ' + ahead + ' m');
    }
    const size = rig => screenY(rig, [0, 0, 0]) - screenY(rig, [0, 1, 0]);
    assert.ok(size(now) / size(before) > .94, 'retain the accepted apparent racer height');
    assert.equal(now.vFov, before.vFov, 'no zoom used to recover composition');
  }
});

test('follow: grounded turns keep the actual racer centred while the eye yaw remains eased', () => {
  for (const aspect of ASPECTS) {
    const rig = createCamRig(freshFeel()), k = createState(); rig.useVel = false;
    setFrame(k, [18, 0, 0], [0, 0, -1], [0, 1, 0]); rig.snap(k, aspect);
    let maxYawLag = 0, maxAimStep = 0, previous = norm(sub(rig.look, rig.eye));
    for (let i = 1; i <= 420; i++) {
      const th = i * DT * 1.15;
      setFrame(k, [18 * Math.cos(th), 0, -18 * Math.sin(th)], [-Math.sin(th), 0, -Math.cos(th)], [0, 1, 0], 20.7);
      k[S.driftDir] = i > 90 && i < 270 ? 1 : 0; k[S.lateral] = -2;
      rig.update(k, fx0(), DT, aspect);
      assertAnchor(rig, k, aspect);
      maxYawLag = Math.max(maxYawLag, Math.abs(Math.atan2(Math.sin(th - rig.yaw), Math.cos(th - rig.yaw))));
      const forward = norm(sub(rig.look, rig.eye));
      maxAimStep = Math.max(maxAimStep, Math.acos(Math.max(-1, Math.min(1, dot(previous, forward)))));
      previous = forward;
    }
    assert.ok(maxYawLag > 0.1, 'the orbit has not snapped to kart heading');
    assert.ok(maxAimStep < 0.06, 'the corrected aim stays smooth: ' + maxAimStep);
  }
});

test('follow: the whole stacked-course frame path holds composition across slopes and both loop transitions', () => {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url))));
  for (const aspect of ASPECTS) {
    const rig = createCamRig(freshFeel()), k = createState(); rig.useVel = false;
    let loopFrames = 0;
    for (let s = 0; s < track.lengthM; s += 24 * DT) {
      const f = track.frameAt(s);
      setFrame(k, f.p, f.t, f.n); k[S.seg] = f.seg;
      if (s === 0) rig.snap(k, aspect, null, track); else rig.update(k, fx0(), DT, aspect, track);
      assertAnchor(rig, k, aspect);
      for (const v of [...rig.eye, ...rig.look, ...rig.up]) assert.ok(Number.isFinite(v));
      if (rig.loop > 0.9) loopFrames++;
    }
    assert.ok(loopFrames > 100, 'tested the interior of both loops');
    assert.equal(rig.loop, 0, 'returned to the level chase after the loops');
  }
});

test('follow: boost effects and a finish orbit do not displace the anchor or change FOV', () => {
  for (const aspect of ASPECTS) {
    const rig = createCamRig(freshFeel()), k = createState(); rig.useVel = false;
    k[S.grounded] = 1; rig.snap(k, aspect);
    const baseFov = rig.vFov;
    for (let i = 0; i < 150; i++) {
      k[S.boostKind] = i < 70 ? BOOST.PAD : BOOST.NONE;
      const wave = Math.sin(i / 150 * Math.PI);
      rig.update(k, { ...fx0(), fovKick: 9 * wave, camPush: 1.2 * wave, orbit: i / 150 }, DT, aspect);
      assertAnchor(rig, k, aspect); assert.equal(rig.vFov, baseFov);
    }
  }
});

test('follow: a hop has bounded vertical travel and eases back on landing', () => {
  for (const aspect of ASPECTS) {
    const rig = createCamRig(freshFeel()), k = createState(); rig.useVel = false;
    k[S.grounded] = 1; rig.snap(k, aspect);
    let excursion = 0, landingFirst = 0, landingLastAir = 0;
    for (let i = 0; i <= 120; i++) {
      const airborne = i < 48, t = Math.min(i / 48, 1);
      k[S.posY] = airborne ? 0.55 * Math.sin(t * Math.PI) : 0;
      k[S.velY] = airborne ? 0.55 * Math.PI / 0.8 * Math.cos(t * Math.PI) : 0;
      k[S.airborne] = airborne ? 1 : 0; k[S.grounded] = airborne ? 0 : 1;
      rig.update(k, fx0(), DT, aspect);
      const p = project(rig, k, aspect);
      assert.ok(Math.abs(p.x - 0.5) < 2e-6);
      assert.ok(Math.abs(p.y - F.screenY) <= F.airScreenMax + 2e-6, 'bounded air travel ' + p.y);
      excursion = Math.max(excursion, Math.abs(p.y - F.screenY));
      if (i === 47) landingLastAir = p.y - F.screenY;
      if (i === 48) landingFirst = p.y - F.screenY;
    }
    assert.ok(excursion > 0.008, 'hop remains visible rather than glued to the anchor');
    assert.ok(Math.abs(landingFirst - landingLastAir) < 0.005, 'landing eases instead of snapping the aim');
    assert.ok(Math.abs(project(rig, k, aspect).y - F.screenY) < 1e-5, 'settled after landing');
  }
});

test('follow: horizontal hit kicks remain visible while effect shake cannot bob the view', () => {
  for (const aspect of ASPECTS) {
    const rig = createCamRig(freshFeel()), k = createState(); rig.useVel = false;
    k[S.grounded] = 1; rig.snap(k, aspect); rig.hit('wall', 9, 1);
    let dx = 0, dy = 0;
    for (let i = 0; i < 150; i++) {
      const sh = i < 40 ? 0.065 * Math.sin(i * 0.4) : 0;
      rig.update(k, { ...fx0(), shake: [sh, sh, 0] }, DT, aspect);
      const p = project(rig, k, aspect);
      dx = Math.max(dx, Math.abs(p.x - 0.5)); dy = Math.max(dy, Math.abs(p.y - F.screenY));
    }
    assert.ok(dx > 0.01 && dx < 0.12, 'visible bounded horizontal kick ' + dx);
    assert.ok(dy < 0.002, 'no vertical effect shake ' + dy);
    assertAnchor(rig, k, aspect);
  }
});

test('follow: degenerate external anchor poses stay finite and do not move the eye or road-up', () => {
  const eye = [0, 1, 0], up = [0, 1, 0], look = [0, 0, -4];
  aimAtAnchor(look, eye, up, eye, 60);
  assert.deepEqual(look, [0, 0, -4]);
  aimAtAnchor(look, eye, up, [0, 2, 0], 60);
  assert.deepEqual(look, [0, 0, -4]);
  assert.deepEqual(eye, [0, 1, 0]); assert.deepEqual(up, [0, 1, 0]);
});

test('follow: a fast zero-velocity rescue cannot overtake the eye, and returns smoothly to the grounded chase', () => {
  for (const aspect of ASPECTS) {
    const rig = createCamRig(freshFeel()), k = createState(); rig.useVel = false;
    k[S.posY] = -10; k[S.posZ] = -30; k[S.respawnPhase] = RESPAWN.PULL;
    rig.snap(k, aspect);
    // A real ease error must shrink continuously, not be cleared by a rescue-specific snap.
    rig.focus[0] += 0.3;
    let previous = norm(sub(rig.look, rig.eye)), maxStep = 0;
    for (let i = 1; i <= 150; i++) {
      if (i <= 30) {
        k[S.posY] = -10 + 12 * i / 30; k[S.posZ] = -30 + i;
        k[S.respawnPhase] = RESPAWN.PULL;
      } else if (i <= 60) {
        k[S.respawnPhase] = RESPAWN.HOVER;
      } else if (i <= 90) {
        const t = (i - 60) / 30;
        k[S.posY] = 2 * (1 - t * t * (3 - 2 * t)); k[S.respawnPhase] = RESPAWN.RELEASE;
      } else {
        k[S.posY] = 0; k[S.posZ] = -(i - 90) * 0.3; k[S.velZ] = -18;
        k[S.respawnPhase] = RESPAWN.GRACE; k[S.grounded] = 1;
      }
      rig.update(k, fx0(), DT, aspect);
      assertAnchor(rig, k, aspect);
      const forward = norm(sub(rig.look, rig.eye));
      assert.ok(forward[2] < -0.8, 'never aims backward during the pull');
      assert.ok(rig.eye[2] - k[S.posZ] > 3, 'the kart cannot overtake the eye');
      maxStep = Math.max(maxStep, Math.acos(Math.max(-1, Math.min(1, dot(previous, forward)))));
      if (i === 1) assert.ok(rig.focus[0] > 0.2 && rig.focus[0] < 0.3, 'existing positional ease was preserved');
      previous = forward;
    }
    assert.ok(maxStep < 0.08, 'no rescue turn snap: ' + maxStep * 180 / Math.PI);
    assert.ok(Math.abs(rig.focus[2] - k[S.posZ]) < 1e-5, 'normal velocity prediction resumes after release');
  }
});
