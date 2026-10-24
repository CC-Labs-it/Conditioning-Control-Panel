import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { boudoirWorldPlan, boudoirMediaLayout, boudoirBoxesIntersect, boudoirSegmentBlocked } from '../render/boudoirMedia.js';
import { boudoirSpiralLayout } from '../render/boudoirSpirals.js';
import { selectScrolllerSlots } from '../render/mediaSelection.js';
import { roadSamples, rectPoints, tierSlots, mediaLook } from '../render/mediaMath.js';
import { S } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { updateRespawn } from '../sim/respawn.js';
import { place, makeFeel, recorder, acts, DT } from './sim-fake.mjs';
const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/boudoir.json', import.meta.url), 'utf8')));
const plan = boudoirWorldPlan(track), media = boudoirMediaLayout(track);

test('Boudoir is a complete dressing room with clear furniture, drawer interiors and recognizable cosmetics', () => {
  assert.equal(boudoirWorldPlan({ id: 'threshold' }), null);
  assert.ok(plan.rows.length > 600);
  assert.equal(plan.interiors.length, 2);
  for (const name of ['dressing-room-parquet', 'dressing-room-ceiling', 'wardrobe-descent-crown', 'shelf-climb-crown',
    'drawer-lacquer-wall', 'drawer-ceiling', 'vanity-tabletop', 'lipstick-colour', 'perfume-flacon', 'vanity-mirror-frame'])
    assert.ok(plan.rows.some(r => r.name === name), name);
  const hits = [];
  for (const row of plan.rows) {
    assert.ok(row.p.every(Number.isFinite) && row.size.every(v => v > 0 && Number.isFinite(v)));
    if (plan.corridors.some(c => boudoirBoxesIntersect(row, c))) hits.push(row.name);
  }
  assert.deepEqual(hits, [], 'all solid bounds clear every road level, including beneath crossings');
  assert.ok(plan.groundY < -60);
});

test('Boudoir mounted faces remain unobstructed and share moving media and Loom equally', () => {
  assert.equal(boudoirMediaLayout({ id: 'threshold' }), null);
  assert.ok(media.slots.length >= 48, 'moving displays cover the long route');
  assert.equal(new Set(media.slots.map(s => s.id)).size, media.slots.length);
  for (const tier of ['high', 'mid', 'phone']) {
    const visible = tierSlots(media.slots, tier, mediaLook()), selected = selectScrolllerSlots(visible);
    for (const frame of ['billboard', 'tunnel']) {
      const all = visible.filter(s => s.frame === frame), videos = selected.filter(s => s.frame === frame);
      assert.ok(all.length >= 2, frame + ' has world displays'); assert.equal(videos.length, all.length / 2);
    }
  }
  const blocked = [];
  for (const slot of media.slots) {
    const face = { p: slot.C, size: [slot.w, slot.h, .1], axes: [slot.X, slot.Y, slot.Z] };
    if (plan.rows.some(r => boudoirBoxesIntersect(face, r))) blocked.push(slot.id);
    assert.ok(rectPoints(slot).every(p => p.every(Number.isFinite)));
    assert.ok(plan.rows.some(r => r.name === 'mounted-mirror-backing' &&
      Math.hypot(...r.p.map((v, i) => v - slot.C[i])) < 1.1), 'real furniture mount for ' + slot.id);
  }
  assert.deepEqual(blocked, [], 'no wall, trim, shelf or crossing blocks the displayed pixels');
});

test('Every mounted picture has a clear driver sightline before reaching it, including both enclosed halls', () => {
  const blocked = [];
  for (const slot of media.slots) for (const lead of [30, 45, 60]) {
    const f = track.frameAt(slot.s - lead), eye = f.p.map((v, i) => v + f.n[i] * 3);
    const points = [slot.C, ...[-1, 1].flatMap(x => [-1, 1].map(y => slot.C.map((v, i) => v + slot.X[i] * x * slot.w * .4 + slot.Y[i] * y * slot.h * .4)))];
    if (points.some(p => plan.rows.some(row => boudoirSegmentBlocked(eye, p, row)))) blocked.push(slot.id + '/' + lead);
  }
  assert.deepEqual(blocked, [], 'clear face pixels alone do not prove the driver can see them');
});

test('Boudoir portals cover both spirals without obscuring the compact field or flight landing', () => {
  assert.equal(boudoirSpiralLayout({ id: 'threshold' }), null);
  const gates = boudoirSpiralLayout(track).gates;
  assert.equal(gates.length, 7);
  assert.ok(new Set(gates.map(g => g.loom.layer.arms)).size >= 4);
  for (const gate of gates) {
    assert.equal(gate.loom.pulse.amp, 0); assert.ok(gate.radiusM >= 22);
    const s = gate.f * track.lengthM;
    assert.ok(!(track.fields || []).some(f => s >= f.fromS && s <= f.toS));
  }
});

test('Real shortcut flights clear the dressing-room solids at slow, normal and fast entry speeds', () => {
  const hits = [];
  for (const route of track.routes) for (const speed of [20, 30, 40]) {
    const s = route.openS0 + 3, state = place(track, { s, lateral: track.frameAt(s).w / 2 - 1, speed, headingDeg: 15 });
    const events = recorder(), feel = makeFeel(), action = acts({ throttle: 1 });
    for (let frame = 0; frame < 420 && !events.all('route-land').length; frame++) {
      step(state, action, track, feel, DT, events); updateRespawn(state, track, feel, DT, events);
      const kart = { p: [state[S.posX], state[S.posY] + 1, state[S.posZ]], size: [3, 3, 3] };
      for (const row of plan.rows) if (boudoirBoxesIntersect(kart, row)) hits.push(route.id + '/' + speed + ':' + row.name);
    }
  }
  assert.deepEqual([...new Set(hits)], []);
});

const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js', three: 'three.webgpu.min.js' };
  if (files[specifier]) return { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true };
  if (specifier.startsWith('three/addons/')) return { url: new URL('../vendor/three/addons/' + specifier.slice(13), import.meta.url).href, shortCircuit: true };
  return next(specifier, context);
} });
const { buildBoudoirDressing } = await import('../render/boudoirDressing.js');
const { createPalette } = await import('../render/materials/palette.js');
hooks.deregister();

test('Boudoir world batches its solids and sparkles, and pause freezes decorative motion', () => {
  const dressing = buildBoudoirDressing(track, null, createPalette('boudoir'), roadSamples(track));
  assert.ok(dressing.stats.draws <= 32, JSON.stringify(dressing.stats));
  assert.ok(dressing.stats.sparkles >= 240);
  dressing.group.traverse(o => { if (o.isMesh) {
    o.geometry.computeBoundingSphere(); assert.ok(Number.isFinite(o.geometry.boundingSphere.radius));
  } });
  const sparkles = dressing.group.getObjectByName('boudoir-pearl-sparkles');
  const initial = [...sparkles.instanceMatrix.array]; dressing.update({ dt: .5 });
  assert.notDeepEqual([...sparkles.instanceMatrix.array], initial);
  const moved = [...sparkles.instanceMatrix.array]; dressing.update({ dt: .5, paused: true });
  assert.deepEqual([...sparkles.instanceMatrix.array], moved);
  const released = new Map();
  dressing.group.traverse(o => { if (o.isInstancedMesh) {
    released.set(o.name, 0); o.addEventListener('dispose', () => released.set(o.name, released.get(o.name) + 1));
  } });
  dressing.dispose(); dressing.dispose();
  assert.ok([...released.values()].every(n => n === 1), 'GPU instance buffers released exactly once');
});
