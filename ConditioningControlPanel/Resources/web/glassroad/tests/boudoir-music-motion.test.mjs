import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { perfumePhase, createBoudoirPerfume } from '../sim/boudoirPerfume.js';
import { ribbonPhase, powderPressPhase, mascaraPhase } from '../track/boudoir-setpieces.js';
import { setMachineryBeat, machineryBeatAt } from '../track/machineryBeat.js';
import { fakeTrack, place, recorder } from './sim-fake.mjs';
import { S } from '../sim/state.js';
import { loadTrack } from '../track/load.js';
const beatS = 60 / 99.02, offsetS = .309;
const zone = { id: 'song-mist', s0: 100, s1: 165, x: -5, w: 18, offsetS: 0 };
const frame = beat => ({ beat, beatS, safe: beat < 0, rearming: false });
const anchor = (track, songS = offsetS, raceS = 0, extra = {}) => setMachineryBeat(track,
  { enabled: true, songS, raceS, bpm: 99.02, offsetS, ...extra });

test('perfume warning, spray, contact and fade land on the recording beat grid', () => {
  for (const [beat, phase] of [[0, 'clear'], [5.3, 'warn'], [9.3, 'spray'], [10, 'active'], [15.5, 'fade'], [17, 'clear']]) {
    const a = perfumePhase(zone, 87, {}, frame(beat)), b = perfumePhase(zone, 0, {}, frame(beat + 20));
    assert.equal(a.phase, phase); assert.equal(a.phase, b.phase); assert.ok(Math.abs(a.mist - b.mist) < 1e-10);
  }
  assert.equal(perfumePhase(zone, 0, {}, frame(9.99)).active, false);
  assert.equal(perfumePhase(zone, 0, {}, frame(10)).active, true);
  assert.equal(perfumePhase(zone, 0, {}, frame(15)).active, false);
  for (const offset of [0, 1.3, 4.7, -2]) {
    const row = { ...zone, offsetS: offset }, start = ((-Math.round(offset / beatS) % 20) + 20) % 20;
    for (let beat = -.5; beat < start + 9; beat += .1)
      assert.equal(perfumePhase(row, 6, {}, frame(beat)).active, false, 'full warning for offset ' + offset);
  }
});

test('ribbon arrows travel continuously and in the collision direction through beat reversals', () => {
  const row = { offsetS: 0 }, pose = beat => ribbonPhase(row, 123, frame(beat));
  for (let beat = .001; beat < 60; beat += .025) {
    const a = pose(beat), b = pose(beat + .001), travel = b.travelM - a.travelM;
    if (a.phase === b.phase) assert.ok(a.direction === 0 ? Math.abs(travel) < 1e-8 : travel * a.direction >= -1e-8);
  }
  for (const beat of [0, 10, 12, 18, 20, 30, 32, 38, 40])
    assert.ok(Math.abs(pose(beat - 1e-7).travelM - pose(beat + 1e-7).travelM) < 1e-5, 'continuous at ' + beat);
  assert.equal(ribbonPhase(row, 3).travelM, undefined, 'online/no-song fallback stays unchanged');
});

const hooks = registerHooks({ resolve(specifier, context, next) {
  const names = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js', three: 'three.webgpu.min.js' };
  if (names[specifier]) return { url: new URL('../vendor/three/build/' + names[specifier], import.meta.url).href, shortCircuit: true };
  if (specifier.startsWith('three/addons/')) return { url: new URL('../vendor/three/addons/' + specifier.slice(13), import.meta.url).href, shortCircuit: true };
  return next(specifier, context);
} });
const { Group } = await import('three/webgpu');
const { createBoudoirPerfumeView } = await import('../render/boudoirPerfume.js');
const { createBoudoirHazardView } = await import('../render/boudoirHazards.js');
const { buildBoudoirDressing } = await import('../render/boudoirDressing.js');
const { createPalette } = await import('../render/materials/palette.js');
hooks.deregister();

test('perfume drawing and swept contact share heard time, pause and seek rearming', () => {
  const track = fakeTrack({ w: 36 }), events = recorder(), sim = createBoudoirPerfume(track, events, { zones: [zone] });
  const view = createBoudoirPerfumeView(new Group(), track, { zones: [zone] }); anchor(track);
  const r = { i: 0, kind: 'local', st: place(track, { s: 120, lateral: -5, speed: 35 }), finished: false }, t = 10.5 * beatS;
  sim.update([r], t); assert.equal(events.all('perfume-contact').length, 1); assert.equal(sim.inspect().zones[0].phase, 'active');
  for (let n = 0; n < 30; n++) view.update(t, { gentle: true });
  assert.ok(view.inspect().cloudOpacity[0] > .1);
  const before = view.inspect(); view.update(t + 3, { paused: true }); assert.deepEqual(view.inspect(), before);
  const engine = r.st[S.engineV]; anchor(track, .5, t + .01); sim.update([r], t + .01);
  assert.equal(r.st[S.engineV], engine); assert.equal(sim.inspect().zones[0].phase, 'warn');
  for (let n = 0; n < 30; n++) view.update(t + .01);
  assert.equal(view.inspect().cloudOpacity[0], 0);
  setMachineryBeat(track, null); assert.equal(sim.inspect(6).zones[0].phase, 'active');
  view.dispose();
});

test('Boudoir presses, brushes and bar-driven scenery remain finite and freeze together', () => {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/boudoir.json', import.meta.url), 'utf8')));
  const view = createBoudoirHazardView(new Group(), track), dressing = buildBoudoirDressing(track, null, createPalette('boudoir'));
  anchor(track);
  for (const t of [3, 9 * beatS, 18 * beatS]) {
    view.update(t, { gentle: true });
    for (const entry of view.entries) {
      if (entry.row.kind === 'press') assert.equal(entry.moving.position.y, powderPressPhase(entry.row, t, machineryBeatAt(track, t)).liftM);
      for (const head of entry.heads || []) assert.equal(head.pivot.rotation.z, mascaraPhase(head.row, t, machineryBeatAt(track, t)).angle);
    }
    dressing.update({ dt: .1, songS: offsetS + t, beatSong: { bpm: 99.02, downbeatS: offsetS }, audioReactive: { active: true, bass: .8 } });
  }
  const snapshots = dressing.group.children.filter(o => o.instanceMatrix).map(o => [...o.instanceMatrix.array]);
  assert.ok(snapshots.flat().every(Number.isFinite));
  dressing.update({ dt: 1, paused: true, songS: 100, song: { bpm: 99.02 } });
  assert.deepEqual(dressing.group.children.filter(o => o.instanceMatrix).map(o => [...o.instanceMatrix.array]), snapshots);
  view.dispose(); dressing.dispose();
});
