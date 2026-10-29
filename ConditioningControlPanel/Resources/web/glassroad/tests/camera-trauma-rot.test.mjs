// Feel wave item 12 (lane E): rotational trauma on a COPY of the composed pose. The drawn three.js orientation
// takes noise * traumaOut * camera.traumaRot.*; the rig (the follow state) keeps the clean pose, the eye never
// moves, and the defaults (all 0) leave today's pose byte for byte.
import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { traumaRot, offsetQuat } from '../render/cameraMath.js';
import { fakeTrack, makeFeel, place } from './sim-fake.mjs';
import { S } from '../sim/state.js';

const hooks = registerHooks({ resolve(name, context, next) {
  return name === 'three/webgpu' ? { url: new URL('../vendor/three/build/three.webgpu.min.js', import.meta.url).href, shortCircuit: true } : next(name, context);
} });
const { createChaseCamera } = await import('../render/camera.js');
const { Quaternion, Euler } = await import('three/webgpu');
hooks.deregister();

const DEG = Math.PI / 180;
const noise = (axis, t) => Math.sin(t * 23 + axis * 1.7);
const trial = { 'camera.traumaRot.rollDeg': 1.5, 'camera.traumaRot.yawDeg': 0.8, 'camera.traumaRot.pitchDeg': 0.6 };

test('feel defaults are all 0', () => {
  const f = makeFeel();
  assert.deepEqual(['rollDeg', 'yawDeg', 'pitchDeg'].map((k) => f.get('camera.traumaRot.' + k)), [0, 0, 0]);
  const out = traumaRot({}, f, { traumaOut: 1 }, noise, 0.3);
  assert.deepEqual(out, { pitch: 0, yaw: 0, roll: 0 });
});

test('noise * traumaOut * deg per axis; nothing without noise, traumaOut or under Gentle', () => {
  const f = makeFeel(trial), t = 0.37;
  const out = traumaRot({}, f, { traumaOut: 0.5 }, noise, t);
  assert.ok(Math.abs(out.pitch - (noise(0, t) * 0.5 * 0.6 * DEG)) < 1e-15);
  assert.ok(Math.abs(out.yaw - (noise(1, t) * 0.5 * 0.8 * DEG)) < 1e-15);
  assert.ok(Math.abs(out.roll - (noise(2, t) * 0.5 * 1.5 * DEG)) < 1e-15);
  for (const [fx, n, g] of [[{ traumaOut: 1 }, undefined, false], [{}, noise, false], [null, noise, false], [{ traumaOut: 1 }, noise, true]])
    assert.deepEqual(traumaRot({}, f, fx, n, t, g), { pitch: 0, yaw: 0, roll: 0 });
});

test('offsetQuat is three\'s YXZ Euler', () => {
  for (const [p, y, r] of [[0.01, -0.02, 0.03], [-0.2, 0.5, -0.1]]) {
    const q = new Quaternion().setFromEuler(new Euler(p, y, r, 'YXZ')).toArray();
    offsetQuat([0, 0, 0, 0], p, y, r).forEach((v, i) => assert.ok(Math.abs(v - q[i]) < 1e-12));
  }
});

function run(feelOver, fx, opts = {}) {
  const track = fakeTrack({ nodes: 40, spacing: 10 }), feel = makeFeel(feelOver);
  const quiet = createChaseCamera(feel), shaken = createChaseCamera(feel), rows = [];
  const dt = 1 / 60;
  for (let n = 0; n < 150; n++) {
    const st = place(track, { s: 60 + n * dt * 20, speed: 20 }); st[S.speedPct] = 0.5;
    if (!n) { quiet.snap(st, track, 16 / 9); shaken.snap(st, track, 16 / 9); continue; }
    quiet.update(st, null, track, dt, 16 / 9, 'race', null, { viewDt: dt, ...opts });
    shaken.update(st, fx, track, dt, 16 / 9, 'race', null, { viewDt: dt, noise, ...opts });
    rows.push([quiet, shaken].map((c) => ({ pos: c.camera.position.toArray(), q: c.camera.quaternion.toArray(), fov: c.camera.fov,
      rig: JSON.stringify([c.rig.eye, c.rig.look, c.rig.up, c.rig.vFov, c.rig.focus, c.rig.yaw, c.rig.pitch]) })));
  }
  quiet.dispose(); shaken.dispose();
  return rows;
}

test('defaults: full trauma leaves the drawn pose byte-identical', () => {
  for (const [q, s] of run({}, { traumaOut: 1 })) {
    assert.deepEqual(s.q, q.q); assert.deepEqual(s.pos, q.pos); assert.equal(s.rig, q.rig);
  }
});

test('trial values rotate only the drawn copy: eye, fov and the follow state stay clean', () => {
  let moved = 0, maxDeg = 0;
  for (const [q, s] of run(trial, { traumaOut: 1 })) {
    assert.deepEqual(s.pos, q.pos, 'the eye never moves');
    assert.equal(s.fov, q.fov, 'no zoom');
    assert.equal(s.rig, q.rig, 'the follow keeps the clean pose');
    const a = new Quaternion().fromArray(q.q).angleTo(new Quaternion().fromArray(s.q));
    if (a > 1e-6) moved++;
    maxDeg = Math.max(maxDeg, a / DEG);
  }
  assert.ok(moved > 100, 'trauma was drawn: ' + moved);
  assert.ok(maxDeg <= Math.hypot(1.5, 0.8, 0.6) + 1e-6, 'bounded by the feel degrees: ' + maxDeg);
});

test('Gentle and a quiet fx draw no rotation even at trial values', () => {
  for (const [fx, opts] of [[{ traumaOut: 1 }, { gentle: true }], [{ traumaOut: 0 }, {}], [{}, {}]])
    for (const [q, s] of run(trial, fx, opts)) assert.deepEqual(s.q, q.q);
});
