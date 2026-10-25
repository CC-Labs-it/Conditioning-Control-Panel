import { test } from 'node:test';
import assert from 'node:assert/strict';
import { toyPose, toyEnvelope, snapRing, guardLook, toyLod, toyScale, toyTrailPoint, TOY_TRAIL } from '../render/items/toyMath.js';

test('toys: the frozen clock reproduces every pose and reduced motion stills all spins', () => {
  for (const id of ['pendulum', 'disc', 'bigSpiral']) {
    assert.deepEqual(toyPose(id, 4.2), toyPose(id, 4.2));
    assert.deepEqual(toyPose(id, 99, true), toyPose(id, 0, true));
  }
  for (let t = 0; t < 20; t += .1) assert.ok(Math.abs(toyPose('pendulum', t).swing) <= .34);
  assert.ok(toyPose('disc', 2).bank >= .1);
});

test('toys: effects have a continuous IN and OUT, with a 300ms Gentle rise', () => {
  assert.equal(toyEnvelope(-1, 1, true), 0);
  assert.equal(toyEnvelope(0, 1, true), 0);
  assert.ok(toyEnvelope(.1, 1, true) < .3);
  assert.equal(toyEnvelope(.3, 1, true), 1);
  assert.equal(toyEnvelope(1, 1, true), 0);
  for (let t = 0; t <= 1; t += .01) {
    const a = toyEnvelope(t, 1, true); assert.ok(a >= 0 && a <= 1);
    assert.ok(Math.abs(a - toyEnvelope(t - .001, 1, true)) < .006);
  }
});

test('toys: Snap reaches the real six metre radius without a moving Gentle ring', () => {
  let previous = 0;
  for (let t = 0; t <= .6; t += .01) {
    const ring = snapRing(t); assert.ok(ring.radius >= previous && ring.radius <= 6); previous = ring.radius;
    assert.equal(snapRing(t, true).radius, 6);
  }
  assert.equal(snapRing(.6).radius, 6);
  assert.equal(snapRing(.6).alpha, 0);
});

test('toys: one second guard fades at both ends without expanding into a shell', () => {
  assert.equal(guardLook(1, true).alpha, 0);
  assert.equal(guardLook(.5, true).alpha, 1);
  assert.equal(guardLook(0, true).alpha, 0);
  for (const k of [-1, 0, .3, .8, 2, NaN]) assert.equal(guardLook(k).radius, 1.35);
});

test('toys: distant watches and phone quality select the lighter mesh', () => {
  assert.equal(toyLod(10), 0); assert.equal(toyLod(29), 1); assert.equal(toyLod(1, true), 1);
});

test('toys: enlarged silhouettes and distance-sampled wakes remain bounded at driver scale', () => {
  for (const id of ['pendulum', 'disc', 'tether', 'bigSpiral']) {
    assert.ok(toyScale(id) >= 1.6 && toyScale(id) <= 2);
    assert.ok(toyScale(id, true) >= toyScale(id) && toyScale(id, true) <= 2.1);
  }
  assert.ok(TOY_TRAIL.points * TOY_TRAIL.stepM >= 9 && TOY_TRAIL.points * TOY_TRAIL.stepM <= 12);
  let previous = 0;
  for (let i = 0; i < TOY_TRAIL.points; i++) {
    const full = toyTrailPoint(i, TOY_TRAIL.points), faded = toyTrailPoint(i, TOY_TRAIL.points, .25);
    assert.ok(full.size >= .18 && full.size <= .8);
    assert.ok(full.alpha >= previous && full.alpha <= .78); previous = full.alpha;
    assert.equal(faded.alpha, full.alpha * .25);
    assert.equal(toyTrailPoint(i, TOY_TRAIL.points, 1, true).alpha, 0);
  }
});
