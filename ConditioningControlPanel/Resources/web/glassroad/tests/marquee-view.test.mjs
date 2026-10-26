import test from 'node:test';
import assert from 'node:assert/strict';
import { createCamRig, vFovForAspect } from '../render/cameraMath.js';
import { createSongCueRun } from '../sim/songCues.js';
import { marqueeFace, faceX, restY, CLEAR_M, MARQUEE_VIEW } from '../sim/marqueeView.js';
import { RACER_FRAMING } from '../render/framing.js';
import { S } from '../sim/state.js';
import { makeFeel, fakeTrack, place } from './sim-fake.mjs';

// Owner, 2026-10-26: the screens dropping on "keep watching" were slipped or not fully in view. These checks drive
// the real chase rig and ask the picture the player sees, at the impact, where every corner of the screen lands.
const DT = 1 / 60;
const chart = (count = 1, extra = {}) => ({ durationS: 60, words: [{ w: 'keep', startS: 10 }, { w: 'keep', startS: 11.3 }],
  cues: [{ id: 'screen', kind: 'MARQUEE', mode: 'WATCH', anchor: { word: 'keep', n: 1 }, durationS: 20, leadS: 3,
    params: { variant: 'chains', exit: 'snap', count, standoffS: 2, ...extra } }] });
const sub = (a, b) => a.map((v, i) => v - b[i]);
const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const norm = a => { const l = Math.hypot(...a); return a.map(v => v / l); };

/** Drive a kart from song 6 s to the impact at 10 s under the real chase rig; return the rig and the hanging row. */
function drive(track, { speed, lateral = 0, aspect = 16 / 9, count = 1, start = 300 }) {
  const feel = makeFeel(), rig = createCamRig(feel), run = createSongCueRun(chart(count), track);
  // The numbers boot hands the transport: the live aspect and its vertical field of view.
  const view = { aspect, vFovDeg: vFovForAspect(feel.get('fov.baseVDeg'), aspect) };
  let row = null, kart = null, waiting = null;
  for (let i = 0; i <= 4 * 60; i++) {
    const songS = 6 + i * DT;
    kart = place(track, { s: start + speed * i * DT, lateral, speed });
    if (!i) rig.snap(kart); else rig.update(kart, null, DT, aspect, track);
    row = run.update(songS, kart, { view }).live.find(r => r.kind === 'MARQUEE') || row;
    // The drop starts at the top edge of the impact view (render/marquee.js fades it in as it begins).
    if (row?.locked && songS >= row.startS - 1.05 && !waiting) waiting = corners(rig, row, aspect, row.fallM);
  }
  return { rig, row, kart, waiting };
}
/** Every face corner of the hanging rig (bounce-free rest pose) in the rig's own normalised view. */
function corners(rig, row, aspect, raise = 0) {
  const f = norm(sub(rig.look, rig.eye)), r = norm(cross(f, rig.up)), u = cross(r, f);
  const th = Math.tan(rig.vFov * Math.PI / 360), out = [];
  const sides = row.params.count === 3 ? [-1, 0, 1] : row.params.count === 2 ? [-1, 1] : [0];
  for (const side of sides) {
    const { w, h } = marqueeFace(row.frame.w, side, row.faceScale), x = faceX(row.frame.w, side, row.shiftM, row.faceScale);
    const y = (row.liftM || 0) + restY(h) + raise;
    for (const cx of [x - w / 2, x + w / 2]) for (const cy of [y - h / 2, y + h / 2]) {
      const P = [0, 1, 2].map(i => row.frame.p[i] + row.frame.b[i] * cx + row.frame.n[i] * cy), v = sub(P, rig.eye), depth = dot(v, f);
      out.push({ x: dot(v, r) / (depth * th * aspect), y: dot(v, u) / (depth * th), depth, cy });
    }
  }
  return out;
}

test('at the impact the whole screen sits inside the chase view, on the kart line, at slow and fast pace', () => {
  const track = fakeTrack({ w: 40 });
  for (const speed of [14, 22, 30, 45]) for (const lateral of [0, -15]) for (const count of [1, 3]) {
    const { rig, row, kart, waiting } = drive(track, { speed, lateral, count });
    assert.ok(waiting.some(c => c.y < 1.1) && waiting.every(c => c.y > .5), `${speed} ${lateral} ${count}: the drop starts at the top edge`);
    assert.ok(row?.locked && row.state === 'hit', `${speed} ${lateral}: the screen has landed`);
    // Owner: at least three seconds and 60 metres of approach after the drop.
    assert.ok(row.s - kart[S.s] >= Math.max(60, speed * 3) - 1, `${speed}: approach ${row.s - kart[S.s]}`);
    for (const c of corners(rig, row, 16 / 9)) {
      assert.ok(c.depth > 0 && c.y < 1 && c.y > -1 && Math.abs(c.x) < 1, `${speed} ${lateral} ${count}: corner ${c.x.toFixed(2)},${c.y.toFixed(2)}`);
      assert.ok(c.cy >= CLEAR_M - 1e-9, 'nothing enters the road clearance');
    }
    // The kart drives under it: its own line stays inside the centre face.
    const { w } = marqueeFace(row.frame.w, 0, row.faceScale);
    assert.ok(Math.abs(row.shiftM - lateral) <= w / 2 - 2.9, `${speed} ${lateral}: lane ${row.shiftM}`);
    // The drop starts just above the top edge, not tens of metres out of sight.
    assert.ok(row.fallM > 4 && row.fallM < 40, `fall ${row.fallM}`);
  }
});

test('on a curve the screen leans toward the view centre and stays readable, and wide or tall views keep it whole', () => {
  const curve = fakeTrack({ nodes: 120, spacing: 10, w: 36 });   // radius about 190 m
  for (const speed of [22, 34]) for (const lateral of [-10, 0, 10]) {
    const { rig, row } = drive(curve, { speed, lateral, start: 200 });
    const cs = corners(rig, row, 16 / 9), mid = (Math.min(...cs.map(c => c.x)) + Math.max(...cs.map(c => c.x))) / 2;
    assert.ok(cs.every(c => c.depth > 0 && Math.abs(c.x) < 1 && c.y < 1.02), `${speed} ${lateral}: inside`);
    assert.ok(Math.abs(mid) < .5, `${speed} ${lateral}: centred enough (${mid.toFixed(2)})`);
  }
  const straight = fakeTrack({ w: 40 });
  for (const aspect of [21 / 9, 4 / 3, 9 / 16]) {
    const { rig, row } = drive(straight, { speed: 30, aspect });
    assert.ok(corners(rig, row, aspect).every(c => c.y < 1 && Math.abs(c.x) < 1), `aspect ${aspect.toFixed(2)}`);
  }
});

test('a quick second drop retires the first one instead of landing hidden behind it', () => {
  const c = chart(1, { redrop: true }); c.cues[0].jolts = [{ word: 'keep', n: 2 }];
  const track = fakeTrack({ w: 40 }), run = createSongCueRun(c, track);
  const seen = new Map();
  for (let i = 0; i <= 8 * 60; i++) {
    const t = 6 + i * DT;
    for (const r of run.update(t, place(track, { s: 300 + 22 * i * DT, speed: 22 })).live) seen.set(r.id, { ...r });
  }
  const first = seen.get('screen'), second = seen.get('screen~1');
  assert.ok(first && second);
  assert.ok(Math.abs(first.replacedS - (11.3 - 1.05)) < 1e-9, 'the first leaves as the second starts to fall');
  assert.ok(second.s > first.s + 20, 'the second hangs clearly beyond the first');
  assert.equal(second.replacedS, null);
});

test('the transport predicts the same chase numbers the rig composes with', () => {
  const feel = makeFeel();
  assert.equal(MARQUEE_VIEW.screenY, RACER_FRAMING.screenY);
  assert.ok(Math.abs(MARQUEE_VIEW.centreUpM - RACER_FRAMING.centerUpM * RACER_FRAMING.scale) < .001);
  assert.equal(MARQUEE_VIEW.eyeUpM, feel.get('camera.eyeUpM')); assert.equal(MARQUEE_VIEW.eyeBackM, feel.get('camera.eyeBackM'));
  assert.equal(MARQUEE_VIEW.vFovDeg, feel.get('fov.baseVDeg'));
});
