import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { createRide } from './model.js';
import { createNarrationPacer, pacingTimeline, samplePacing } from './narration-pacing.js';
import { NARRATION_CLIPS, RIDE_RECORDINGS } from './narration-data.js';
import { NARRATION_SUBTITLES } from './subtitles.js';
import { projectionTimeline } from './narration-slides.js';

const manifest = JSON.parse(fs.readFileSync(new URL('../../assets/documentary/exhibits.json', import.meta.url)));

test('phrase-anchored movement is continuous and monotone at every recorded visual cue', () => {
  for (const id of ['r1', 'r2', 'r3', 'r4']) {
    const duration = NARRATION_CLIPS[id].duration, points = pacingTimeline(id, duration);
    let previous = -Infinity;
    for (let time = 0; time <= duration; time += .01) {
      const sample = samplePacing(points, time);
      assert.ok(sample.target >= previous - 1e-6, `${id} never reverses`);
      assert.ok(Number.isFinite(sample.speed) && sample.speed >= 0);
      previous = sample.target;
    }
    for (const anchor of points.slice(1, -1)) {
      const left = samplePacing(points, anchor.at - .00001), right = samplePacing(points, anchor.at + .00001);
      assert.ok(Math.abs(left.speed - right.speed) < .01, `${id} velocity is continuous`);
      assert.ok(Math.abs(samplePacing(points, anchor.at).target - anchor.z) < .00001);
    }
  }
});

test('word timing adjustments move the corresponding cart approach anchor', () => {
  const data = { ...NARRATION_SUBTITLES, r3: NARRATION_SUBTITLES.r3.map(cue => [...cue]) };
  data.r3.find(cue => cue[2].includes('The mantis shrimp strikes'))[0] = .3;
  assert.equal(pacingTimeline('r3', 50, data).find(point => point.z === 301.11).at, 15);
});

test('the complete narrated route moves without a parked listening section or extra physics loop', () => {
  const queued = [];
  const ride = createRide(manifest, { cues: RIDE_RECORDINGS.filter(row => row.at).map(row => ({ id: row.id, at: row.at })) }, (event, cue) => {
    if (event === 'cue' && /^r[234]$/.test(cue.id)) queued.push(cue.id);
  });
  let clip = { id: 'r1', ride: true, time: 0, duration: NARRATION_CLIPS.r1.duration, playing: true };
  const pacer = createNarrationPacer(ride, () => clip);
  let time = 0, lastEnd = -Infinity, stationary = 0, maximumStationary = 0, minReflexSpeed = Infinity;
  const completed = [], anchors = [];
  while (!ride.state.docked && time < 300) {
    if (clip) {
      clip.time += 1 / 60;
      if (clip.id === 'r1' && clip.time >= 1.4) ride.unlock();
      if (clip.time >= clip.duration) { anchors.push([clip.id, ride.state.z]); completed.push(clip.id); clip = null; lastEnd = time; }
    } else if (queued.length && time - lastEnd >= 1.5) {
      const id = queued.shift(); clip = { id, ride: true, time: 0, duration: NARRATION_CLIPS[id].duration, playing: true };
    }
    const before = ride.state.z;
    ride.step(1 / 60); time += 1 / 60;
    assert.ok(ride.state.z >= before);
    if (time > 2 && ride.state.z - before < .00001) stationary += 1 / 60; else stationary = 0;
    maximumStationary = Math.max(maximumStationary, stationary);
    if (clip?.id === 'r3' && clip.time > 8 && clip.time < 36) minReflexSpeed = Math.min(minReflexSpeed, ride.state.v);
  }
  assert.deepEqual(completed, ['r1', 'r2', 'r3', 'r4']);
  assert.ok(ride.state.docked && time < 240, `route completes naturally in ${time.toFixed(1)}s`);
  assert.ok(maximumStationary < .5, `longest stationary listening period is ${maximumStationary.toFixed(2)}s`);
  assert.ok(minReflexSpeed > .1, `comparison keeps moving at ${minReflexSpeed.toFixed(2)}m/s or more`);
  for (const [id, z] of anchors) {
    const end = pacingTimeline(id, NARRATION_CLIPS[id].duration).at(-1).z;
    assert.ok(Math.abs(z - end) < 4, `${id} ends near its route anchor: ${z.toFixed(1)}m vs ${end}m`);
  }
  pacer.dispose(); assert.equal(ride.state.pacing, false);
});

test('rapid pumping stays inside the narration corridor and cannot skip the displayed sequence', () => {
  const ride = createRide(manifest, { at: 291 });
  const clip = { id: 'r3', ride: true, time: 0, duration: NARRATION_CLIPS.r3.duration, playing: true };
  const pacer = createNarrationPacer(ride, () => clip);
  for (let i = 0; i < 2200; i++) {
    clip.time = i / 60;
    ride.pump(); ride.step(1 / 60);
    assert.ok(ride.state.z <= pacer.provide(ride.state).ceiling + 1e-5);
    assert.ok(ride.state.v <= 4.5);
  }
  assert.ok(ride.state.z < 305, 'the same reflex screen stays at least five metres ahead through slow motion');
  const progress = clip.time; clip.playing = false;
  for (let i = 0; i < 120; i++) ride.step(1 / 60);
  assert.equal(clip.time, progress, 'the controller never writes to the media clock');
  pacer.dispose();
});

test('every narrated image begins within eleven metres on both the complete route and chapter entries', () => {
  const screens = { booth: 8, lever: 112, reflex: 310, wakeup: 418, conversation: 504 };
  for (const initial of ['r1', 'r2', 'r3', 'r4']) for (const reduced of [false, true]) {
    const first = RIDE_RECORDINGS.find(row => row.id === initial), queue = [], observed = new Set();
    const ride = createRide(manifest, { at: first.at, reduced,
      cues: RIDE_RECORDINGS.filter(row => row.at > first.at).map(row => ({ id: row.id, at: row.at })),
    }, (event, cue) => { if (event === 'cue' && /^r[234]$/.test(cue.id)) queue.push(cue.id); });
    let clip = { id: initial, ride: true, time: 0, duration: NARRATION_CLIPS[initial].duration, playing: true };
    const pacer = createNarrationPacer(ride, () => clip);
    let clock = 0, lastEnd = -Infinity;
    while (!ride.state.docked && clock < 260) {
      if (clip) {
        clip.time += 1 / 60;
        if (clip.id === 'r1' && clip.time >= 1.4) ride.unlock();
        for (const cue of projectionTimeline(clip.id).filter(row => row.slot)) {
          if (observed.has(cue.key) || clip.time < cue.at * clip.duration) continue;
          observed.add(cue.key);
          const distance = screens[cue.projector] - ride.state.z;
          assert.ok(distance >= 5 && distance <= 11, `${initial} reduced=${reduced} ${cue.slot} is ${distance.toFixed(2)}m away`);
        }
        if (clip.time >= clip.duration) { clip = null; lastEnd = clock; }
      } else if (queue.length && clock - lastEnd >= 1.5) {
        const id = queue.shift(); clip = { id, ride: true, time: 0, duration: NARRATION_CLIPS[id].duration, playing: true };
      }
      ride.step(1 / 60); clock += 1 / 60;
    }
    assert.ok(ride.state.docked);
    assert.equal(observed.size, { r1: 10, r2: 9, r3: 8, r4: 2 }[initial]);
    pacer.dispose();
  }
});

test('faster Wake Up transfer is scoped to narration and reduced motion keeps its six metre cap', () => {
  for (const reduced of [false, true]) {
    const ride = createRide(manifest, { at: 420, reduced });
    const clip = { id: 'r4', ride: true, time: 21, duration: NARRATION_CLIPS.r4.duration, playing: true };
    const pacer = createNarrationPacer(ride, () => clip);
    let fastest = 0;
    for (let i = 0; i < 720; i++) { clip.time = 21 + i / 60; ride.step(1 / 60); fastest = Math.max(fastest, ride.state.v); }
    assert.ok(fastest > 4.5 && fastest <= (reduced ? 6 : 6.5));
    assert.ok(pacer.provide({ z: 492, docked: false }).zoneCap <= 4.5);
    pacer.dispose();
    const manual = createRide(manifest, { at: 420, reduced });
    for (let i = 0; i < 300; i++) { manual.pump(); manual.step(1 / 60); assert.ok(manual.state.v <= 4.5); }
  }
});

test('a pace lease cannot clear a replacement and the ordinary manual cart remains available', () => {
  const ride = createRide(manifest, { at: 20 });
  const first = ride.setPace(() => ({ speed: 2 }));
  const second = ride.setPace(() => ({ speed: 3 }));
  first(); assert.equal(ride.state.pacing, true);
  second(); assert.equal(ride.state.pacing, false);
  ride.pump(); ride.step(.5); assert.ok(ride.state.z > 20);
  assert.throws(() => ride.setPace({ speed: 1 }), /Invalid ride pacing/);
});

test('an arbitrary diagnostic position still moves forward instead of compressing a chapter into a long crawl', () => {
  const ride = createRide(manifest, { at: 303 });
  const clip = { id: 'r3', ride: true, time: 0, duration: NARRATION_CLIPS.r3.duration, playing: true };
  const pacer = createNarrationPacer(ride, () => clip);
  for (let i = 0; i < 1800; i++) { clip.time = i / 60; ride.step(1 / 60); }
  assert.ok(ride.state.z > 318, 'mid-chapter debug starts retain visible forward travel');
  assert.ok(ride.state.v > .4);
  pacer.dispose();
});
