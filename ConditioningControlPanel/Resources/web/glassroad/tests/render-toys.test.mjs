import { test } from 'node:test';
import assert from 'node:assert/strict';
import { toyPose, toyEnvelope, snapRing, guardLook, toyLod, toyScale, toyTrailPoint, toyLaunchPose, TOY_TRAIL } from '../render/items/toyMath.js';

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
    assert.ok(toyScale(id) >= 2.4 && toyScale(id) <= 3);
    assert.ok(toyScale(id, true) >= 1.7 && toyScale(id, true) <= 2.1, 'held props retain their readable socket size');
    assert.ok(toyScale(id) / toyScale(id, true) >= 1.35, 'released silhouette visibly grows');
  }
  assert.ok(TOY_TRAIL.points * TOY_TRAIL.stepM >= 9 && TOY_TRAIL.points * TOY_TRAIL.stepM <= 12);
  let previous = 0;
  for (let i = 0; i < TOY_TRAIL.points; i++) {
    const full = toyTrailPoint(i, TOY_TRAIL.points), faded = toyTrailPoint(i, TOY_TRAIL.points, .25);
    assert.ok(full.size >= .24 && full.size <= 1.1);
    assert.ok(full.alpha >= previous && full.alpha <= .78); previous = full.alpha;
    assert.equal(faded.alpha, full.alpha * .25);
    assert.equal(toyTrailPoint(i, TOY_TRAIL.points, 1, true).alpha, 0);
  }
});

test('release pose stretches along travel and settles continuously, while quiet mode stays rigid', () => {
  const out = {}, before = toyLaunchPose(0), kick = toyLaunchPose(.12), end = toyLaunchPose(.4);
  assert.equal(before.alpha, 0); assert.equal(before.cross, 1); assert.equal(before.along, 1);
  assert.ok(kick.cross < .85 && kick.along > 1.45);
  assert.deepEqual(end, { cross: 1, along: 1, alpha: 1 });
  assert.deepEqual(toyLaunchPose(2), end);
  assert.equal(toyLaunchPose(.2, false, out), out, 'pooled output');
  for (let age = 0; age <= .4; age += .01) {
    const quiet = toyLaunchPose(age, true);
    assert.equal(quiet.cross, 1); assert.equal(quiet.along, 1);
    if (age < .3) assert.ok(quiet.alpha < 1);
    const a = toyLaunchPose(age), b = toyLaunchPose(age + .001);
    assert.ok(Math.abs(a.along - b.along) < .01);
  }
});
