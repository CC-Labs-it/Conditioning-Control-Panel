import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { PerspectiveCamera } from '../vendor/three/build/three.webgpu.min.js';
import { loadTrack } from '../track/load.js';
import { createCourseIntro } from '../render/courseIntro.js';
import { COURSE_INTRO_SECONDS, sampleCourseIntro } from '../render/courseIntroMath.js';

for (const id of ['threshold', 'mainframe', 'keep', 'boudoir', 'goonworks']) {
  const track = loadTrack(JSON.parse(readFileSync(new URL(`../tracks/${id}.json`, import.meta.url))));
  const intro = createCourseIntro(track);
  test(`${id}: intro reveals three distinct districts and ends on the grid at 6.6 seconds`, () => {
    assert.equal(intro.duration, 6.6);
    assert.equal(new Set(intro.shots.map(s => s.section)).size, 3);
    assert.deepEqual([0, 1.5, 3, 4.5, 6.6].map(intro.shotAt), [0, 1, 2, 3, 3]);
    for (let frame = 0; frame <= 396; frame++) {
      const pose = sampleCourseIntro(track, intro.shots, frame / 60);
      assert.ok([...pose.eye, ...pose.look, ...pose.up, pose.fov].every(Number.isFinite));
      assert.ok(Math.hypot(...pose.eye.map((v, i) => v - pose.look[i])) > 1);
    }
    assert.equal(sampleCourseIntro(track, intro.shots, 4.5).blend, 0);
    assert.equal(sampleCourseIntro(track, intro.shots, 6.6).blend, 1);
  });
  test(`${id}: Gentle keeps each take coherent with substantially less pan`, () => {
    const distance = (a, b) => Math.hypot(...a.map((v, i) => v - b[i]));
    for (let shot = 0; shot < 3; shot++) {
      const first = shot * 1.5, last = first + 1.49999;
      const eye = (time, gentle) => sampleCourseIntro(track, intro.shots, time, gentle).eye;
      assert.ok(distance(eye(first, true), eye(last, true)) < distance(eye(first, false), eye(last, false)) * .3);
      const saved = eye(first, true);
      eye(last, true);
      assert.deepEqual(eye(first, true), saved, 'sampling is independent of prior frames');
    }
  });
  test(`${id}: final take settles into the exact chase position, orientation, horizon and lens`, () => {
    const target = new PerspectiveCamera(62, 16 / 9);
    target.position.set(...track.spawnPose(0).p).addScalar(4);
    target.lookAt(...track.frameAt(18).p);
    const camera = target.clone();
    intro.apply(camera, COURSE_INTRO_SECONDS - .001, { target });
    assert.ok(camera.position.distanceTo(target.position) < .000001);
    assert.ok(camera.quaternion.angleTo(target.quaternion) < .000001);
    assert.equal(intro.apply(camera, COURSE_INTRO_SECONDS, { target }), true);
    assert.deepEqual(camera.position.toArray(), target.position.toArray());
    assert.deepEqual(camera.quaternion.toArray(), target.quaternion.toArray());
    assert.deepEqual(camera.up.toArray(), target.up.toArray());
    assert.equal(camera.fov, target.fov);
    intro.apply(camera, 1, { target });
    const held = camera.clone();
    intro.apply(camera, 1, { target });
    assert.deepEqual(camera.position.toArray(), held.position.toArray(), 'paused elapsed holds camera');
  });
}
