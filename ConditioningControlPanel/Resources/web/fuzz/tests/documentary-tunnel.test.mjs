import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRide, validateManifest, STORY_ART_SLOTS, isDocumentaryArtSlot } from '../ui/documentary/model.js';
import { createStageClock } from '../ui/documentary/stage.js';
import { needsDomMedia } from '../ui/unlocks/arcade/slot-reel-media.js';
import { trackPoint, trackPosition, trackYaw, trackPitch, trackMotion, trackStyle } from '../ui/documentary/track.js';

const manifest = JSON.parse(await readFile(new URL('../assets/documentary/exhibits.json', import.meta.url), 'utf8'));
test('manifest covers the complete ride and validates art paths', () => {
  assert.equal(validateManifest(manifest), manifest);
  for (const slot of STORY_ART_SLOTS) {
    const valid = structuredClone(manifest);
    valid.exhibits[0].slot = slot;
    assert.equal(validateManifest(valid), valid);
  }
  for (const slot of ['../private', '../intro-subject', 'intro-subject.webp', 'reflex-cheetah/../../private',
    'subject-spreadsheet?private', 'https://example.com/image', 'unlisted-slide']) {
    const bad = structuredClone(manifest);
    bad.exhibits[0].slot = slot;
    assert.equal(isDocumentaryArtSlot(slot), false);
    assert.throws(() => validateManifest(bad));
  }
});
test('booth stays locked, pumping gains speed, and coasting stops', () => {
  const ride = createRide(manifest);
  ride.pump(); ride.step(10);
  assert.equal(ride.state.z, 0);
  ride.unlock(); ride.pump(); ride.step(0.5);
  assert.ok(ride.state.z > 0.5);
  assert.ok(ride.state.v < 1.4);
  ride.step(30);
  assert.equal(ride.state.v, 0);
});
test('fast pumping respects reduced motion and zone caps', () => {
  const reduced = createRide(manifest, { reduced: true, at: 'lever' });
  for (let i = 0; i < 30; i++) reduced.pump();
  assert.equal(reduced.state.v, 6);
  const capped = createRide(manifest, { at: 'floorboard' });
  for (let i = 0; i < 30; i++) capped.pump();
  capped.step(1 / 60);
  assert.ok(capped.state.v <= 4.5);
});
test('crossed cues fire once and a synchronous hold prevents passing the stop', () => {
  const heard = [];
  let release;
  const ride = createRide(manifest, { at: 287 }, (event, data) => {
    if (event === 'cue') {
      heard.push(data.id);
      if (data.id === 'floor.snap') release = ride.hold('floor', 305);
    }
  });
  for (let i = 0; i < 180; i++) { ride.pump(); ride.step(0.2); }
  assert.equal(ride.state.z, 305);
  assert.equal(ride.state.v, 0);
  assert.deepEqual(heard, ['floor.creak', 'floor.snap', 'floor.park']);
  release(); ride.pump(); ride.step(1);
  assert.ok(ride.state.z > 305);
  assert.equal(heard.length, 3);
});
test('zone events and docking fire once with a finite coast into the stage', () => {
  let arrivals = 0;
  const ride = createRide(manifest, { at: 'approach' }, event => { if (event === 'arrive') arrivals++; });
  for (let i = 0; i < 400; i++) { ride.pump(); ride.step(0.2); }
  assert.equal(ride.state.z, 580);
  assert.equal(ride.state.zone, 'stage');
  assert.equal(arrivals, 1);
  ride.pump(); ride.step(10);
  assert.equal(ride.state.z, 580);
});
test('cart state is read only and cue registration is safe to repeat', () => {
  const ride = createRide(manifest);
  assert.throws(() => { ride.state.z = 500; });
  ride.addCues([{ id: 'audio.hello', at: 20 }]);
  ride.addCues([{ id: 'audio.hello', at: 20 }]);
  assert.throws(() => ride.addCues([{ id: 'audio.hello', at: 21 }]));
});
test('physics agrees across 30 and 60 fps', () => {
  const run = fps => {
    const ride = createRide(manifest, { at: 'lever' });
    for (let i = 0; i < fps * 10; i++) { if (i % fps === 0) ride.pump(); ride.step(1 / fps); }
    return ride.state.z;
  };
  assert.ok(Math.abs(run(30) - run(60)) < 0.02);
});
test('stage clock starts at pitch end, catches up across three hidden hours and freezes during pause', () => {
  let now = 10000;
  const clock = createStageClock(() => now);
  assert.equal(clock.elapsed(), 0);
  clock.start(); now += 300000;
  assert.equal(clock.elapsed(), 300000);
  now += 3 * 3600000;
  assert.equal(clock.elapsed(), 11100000);
  clock.pause(true); now += 60000;
  assert.equal(clock.elapsed(), 11100000);
  clock.pause(false); now += 1000;
  assert.equal(clock.elapsed(), 11101000);
});
test('Scrolller slots stay on the DOM media path', async () => {
  assert.ok(needsDomMedia('https://images.scrolller.com/fixture.jpg', 'https://tc.cclabs.app'));
  assert.ok(needsDomMedia('https://yocto.scrolller.com/fixture.mp4', 'https://tc.cclabs.app'));
  for (const file of ['media.js', 'scene.js', 'stage.js']) {
    const source = await readFile(new URL(`../ui/documentary/${file}`, import.meta.url), 'utf8');
    assert.doesNotMatch(source, /getContext\(|drawImage\(|texImage2D\(|createImageBitmap\(/);
  }
});

test('winding track measures real distance, joins slopes smoothly and grounds exhibits', () => {
  let length = 0, minHeight = Infinity, maxHeight = -Infinity, minYaw = Infinity, maxYaw = -Infinity;
  for (let s = 0; s < 600; s += .25) {
    const a = trackPoint(s), b = trackPoint(s + .25);
    length += Math.hypot(b.x - a.x, b.y - a.y, b.z - a.z);
    minHeight = Math.min(minHeight, a.y); maxHeight = Math.max(maxHeight, a.y);
    minYaw = Math.min(minYaw, trackYaw(s)); maxYaw = Math.max(maxYaw, trackYaw(s));
    assert.ok(Number.isFinite(trackPitch(s)));
    const placed = trackPosition(s, 3, 1.5);
    assert.ok(Math.abs(placed.y - a.y - 1.5) < 1e-9);
    assert.ok(Math.abs(Math.hypot(placed.x - a.x, placed.z - a.z) - 3) < 1e-9);
  }
  assert.ok(Math.abs(length - 600) < .03);
  assert.ok(maxHeight - minHeight >= 8);
  assert.ok(maxYaw - minYaw > 1.5);
  assert.deepEqual([0, 150, 200, 375, 450, 580].map(s => trackStyle(s).id),
    ['intake', 'connector', 'reflex', 'descent', 'wakeup', 'theatre']);
});

test('lift and descent move without pumping, then brake before the scripted exhibit', () => {
  const ride = createRide(manifest, { at: 118 });
  let fastest = 0, descending = false;
  for (let i = 0; i < 45 * 60 && ride.state.z < 190; i++) {
    ride.step(1 / 60);
    fastest = Math.max(fastest, ride.state.v);
    descending ||= ride.state.motion === 'descent' && ride.state.pitch < -.1;
  }
  assert.ok(ride.state.z >= 190);
  assert.ok(fastest > 10);
  assert.ok(descending);
  assert.ok(ride.state.v <= 4.5);
  assert.equal(trackMotion(250).mode, 'manual');
});

test('second descent respects reduced motion and holds override its powered movement', () => {
  for (const reduced of [false, true]) {
    const ride = createRide(manifest, { at: 340, reduced });
    const release = ride.hold('inspect', 370);
    let fastest = 0;
    for (let i = 0; i < 45 * 60; i++) { ride.step(1 / 60); fastest = Math.max(fastest, ride.state.v); }
    assert.equal(ride.state.z, 370);
    assert.equal(ride.state.v, 0);
    if (reduced) assert.ok(fastest <= 6);
    release(); ride.step(1);
    assert.ok(ride.state.z > 370);
  }
});
