import test from 'node:test';
import assert from 'node:assert/strict';
import { createCameraViews, mirrorOpening } from '../render/cameraViews.js';
import { createCamRig } from '../render/cameraMath.js';
import { createState, S, RESPAWN } from '../sim/state.js';
import { makeFeel, fakeTrack, place } from './sim-fake.mjs';
import { createSongCueRun } from '../sim/songCues.js';

const DT = 1 / 60;
const track = { lengthM: 1000, forkAtSeg: () => null };
function setup() {
  const kart = createState(); kart[S.grounded] = 1;
  const rig = createCamRig(makeFeel()); rig.snap(kart);
  return { kart, rig, views: createCameraViews() };
}
const clone = x => JSON.parse(JSON.stringify(x));
const mirror = (s = 18) => ({ id: 'test', kind: 'PROP', startS: 10, endS: 16, s, locked: true,
  frame: { p: [0, 0, -s], n: [0, 1, 0] }, params: { variant: 'mirror', cameraCloseUp: true } });
const opts = cue => ({ track, songS: 10, revision: 1, cues: [cue] });
const forward = p => p.look.map((v, i) => v - p.eye[i]);

test('default view preserves every chase coordinate and FOV exactly', () => {
  const { kart, rig, views } = setup();
  const p = views.update(rig, kart, DT);
  for (const k of ['eye', 'look', 'up']) assert.deepEqual(p[k], rig[k]);
  assert.equal(p.vFov, rig.vFov);
});
test('first person is mounted ahead of the driver and looks forward; rear release restores it', () => {
  const { kart, rig, views } = setup();
  views.toggle();
  const first = clone(views.update(rig, kart, DT));
  assert.ok(first.eye[2] < kart[S.posZ]);
  assert.ok(first.eye[1] > .7 && first.eye[1] < 1.5);
  assert.ok(forward(first)[2] < -10);
  const rear = clone(views.update(rig, kart, DT, { rearView: true }));
  assert.ok(forward(rear)[2] > 0);
  assert.deepEqual(views.update(rig, kart, DT), first);
  assert.equal(views.selected, 'first');
  assert.equal(views.toggle(), 'chase');
});
test('manual glances work in first person and no shared racer object is hidden', () => {
  const { kart, rig, views } = setup(); views.toggle();
  rig.glance.update({ x: 1, y: 0 }, .1);
  const pose = views.update(rig, kart, DT);
  assert.ok(forward(pose)[0] > 0);
  assert.equal(kart[S.grounded], 1);
});
test('first person and rear view follow the road up through an inverted loop', () => {
  const { kart, rig, views } = setup();
  rig.rf = [0, 0, 1]; rig.ru = [0, -1, 0]; views.toggle();
  for (const rearView of [false, true]) {
    const pose = views.update(rig, kart, DT, { rearView });
    assert.deepEqual(pose.up, [0, -1, 0]);
    assert.ok(forward(pose)[2] * (rearView ? -1 : 1) > 0);
    assert.ok([...pose.eye, ...pose.look, ...pose.up, pose.vFov].every(Number.isFinite));
  }
});
test('mirror opening uses the same fixed approach distances across the lap seam', () => {
  assert.equal(mirrorOpening({ s: 10 }, 986, 1000), 0);
  assert.equal(mirrorOpening({ s: 10 }, 992, 1000), .5);
  assert.equal(mirrorOpening({ s: 10 }, 998, 1000), 1);
});
test('a slow approach waits for the heard line before the special mirror opens', () => {
  const cue = mirror(6);
  assert.equal(mirrorOpening(cue, 0, 1000, 9), 0);
  assert.ok(mirrorOpening(cue, 0, 1000, 10) > .9);
});
test('authored mirror eases in, holds briefly, returns exactly and cannot retrigger', () => {
  const { kart, rig, views } = setup(), o = opts(mirror());
  const baseline = clone(views.update(rig, kart, DT));
  let minDistance = Infinity, firstDistance;
  for (let i = 0; i < 150; i++) {
    const pose = views.update(rig, kart, DT, { ...o, songS: 10 + i * DT });
    const distance = Math.hypot(...pose.eye.map((v, j) => v - rig.anchor[j]));
    if (i === 0) firstDistance = distance;
    minDistance = Math.min(minDistance, distance);
  }
  const baselineDistance = Math.hypot(...baseline.eye.map((v, i) => v - rig.anchor[i]));
  assert.ok(firstDistance > baselineDistance * .99, 'continuous entry');
  assert.ok(minDistance < baselineDistance * .72, 'visible close-up');
  assert.deepEqual(views.update(rig, kart, DT, { ...o, songS: 13 }), baseline);
  assert.equal(views.mirrorActive, false);
});
test('mirror returns to first person and preserves its selection', () => {
  const { kart, rig, views } = setup(); views.toggle();
  const base = clone(views.update(rig, kart, DT));
  const o = opts(mirror());
  for (let i = 0; i < 120; i++) views.update(rig, kart, DT, { ...o, songS: 10 + i * DT });
  assert.deepEqual(views.update(rig, kart, DT), base);
  assert.equal(views.selected, 'first');
});
test('pause freezes every camera coordinate and mirror progress', () => {
  const { kart, rig, views } = setup(), o = opts(mirror());
  for (let i = 0; i < 20; i++) views.update(rig, kart, DT, o);
  const held = clone(views.update(rig, kart, 0, o));
  for (let i = 0; i < 120; i++) assert.deepEqual(views.update(rig, kart, 0, o), held);
  assert.equal(views.mirrorActive, true);
});
test('manual controls, Gentle, airborne and rescue suppress and consume this opening', () => {
  for (const suppression of ['rearView', 'manual', 'gentle', 'air', 'rescue']) {
    const { kart, rig, views } = setup(), o = opts(mirror());
    if (suppression === 'air') kart[S.airborne] = 1;
    if (suppression === 'rescue') kart[S.respawnPhase] = RESPAWN.PULL;
    views.update(rig, kart, DT, { ...o, [suppression]: true });
    kart[S.airborne] = 0; kart[S.respawnPhase] = RESPAWN.NONE;
    views.update(rig, kart, DT, o);
    assert.equal(views.mirrorActive, false, suppression);
  }
});
test('ordinary mirrors, missed pieces, another deck and fork roads never trigger the shot', () => {
  for (const kind of ['ordinary', 'passed', 'far', 'deck', 'fork', 'noSong']) {
    const { kart, rig, views } = setup(), cue = mirror(), o = opts(cue);
    if (kind === 'ordinary') cue.params.cameraCloseUp = false;
    if (kind === 'passed') cue.s = 998;
    if (kind === 'far') cue.frame.p[0] = 100;
    if (kind === 'deck') cue.frame.p[1] = 10;
    if (kind === 'fork') o.track = { ...track, forkAtSeg: () => ({}) };
    if (kind === 'noSong') o.songS = NaN;
    views.update(rig, kart, DT, o);
    assert.equal(views.mirrorActive, false, kind);
  }
});
test('song seek, cue revision and restart rearm the authored shot', () => {
  for (const restart of ['seek', 'revision', 'reset']) {
    const { kart, rig, views } = setup(), o = opts(mirror());
    for (let i = 0; i < 120; i++) views.update(rig, kart, DT, { ...o, songS: 10 + i * DT });
    assert.equal(views.mirrorActive, false);
    if (restart === 'reset') views.reset();
    if (restart === 'revision') o.revision = 2;
    views.update(rig, kart, DT, { ...o, songS: restart === 'seek' ? 10 : 12 });
    assert.equal(views.mirrorActive, true, restart);
  }
});
test('fixed WATCH placement opens and starts the close-up around its heard anchor', () => {
  const track = fakeTrack(), feel = makeFeel(), rig = createCamRig(feel), views = createCameraViews();
  const run = createSongCueRun({ durationS: 30, words: [{ w: 'test', startS: 10 }], cues: [{
    id: 'mirror', kind: 'PROP', mode: 'WATCH', anchor: { word: 'test', n: 1 }, durationS: 6, leadS: 3,
    params: { variant: 'mirror', standoffS: .6, cameraCloseUp: true },
  }] }, track);
  let began = null, ended = null;
  for (let i = 0; i < 480; i++) {
    const songS = 6 + i * DT, kart = place(track, { s: 100 + i * DT * 30, speed: 30 });
    if (!i) rig.snap(kart); else rig.update(kart, null, DT, 16 / 9, track);
    const cue = run.update(songS, kart);
    views.update(rig, kart, DT, { track, songS, revision: cue.revision, cues: cue.live });
    if (began === null && views.mirrorActive) began = songS;
    if (began !== null && ended === null && !views.mirrorActive) ended = songS;
  }
  assert.ok(began >= 9.75 && began <= 10.05, String(began));
  assert.ok(ended >= 11.15 && ended <= 11.55, String(ended));
});
