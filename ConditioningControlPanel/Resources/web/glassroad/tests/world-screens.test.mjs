import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { roadSamples, rectPoints, tubeClash, haloSlots, V3, mediaLook } from '../render/mediaMath.js';
import { selectScrolllerSlots } from '../render/mediaSelection.js';
import { keepMediaLayout } from '../render/keepMedia.js';
import { boudoirMediaLayout, boudoirBoxesIntersect } from '../render/boudoirMedia.js';
import { worldScreenLayout, worldScreenExistingSlots, worldScreenBlockers, worldScreenSolids, worldScreenSightlines, WORLD_SCREEN_TIERS } from '../render/worldScreens.js';

const read = id => JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url), 'utf8'));
const tracks = Object.fromEntries(['threshold', 'mainframe', 'keep', 'boudoir'].map(id => [id, loadTrack(read(id))]));
const authored = track => track.id === 'keep' ? keepMediaLayout(track) : track.id === 'boudoir' ? boudoirMediaLayout(track) : read(track.id + '.media');
const faceBox = slot => ({ p: slot.C, size: [slot.w, slot.h, .08], axes: [slot.X, slot.Y, slot.Z] });
const plans = new Map();
function plan(id, tier = 'high') {
  const key = id + tier;
  if (!plans.has(key)) {
    const track = tracks[id], existingSlots = worldScreenExistingSlots(track, authored(track), { tier });
    plans.set(key, { ...worldScreenLayout(track, { tier, existingSlots }), existingSlots });
  }
  return plans.get(key);
}

for (const id of Object.keys(tracks)) test(id + ': pictures cover the lap at road height with real-media/Loom balance and tier caps', () => {
  const track = tracks[id];
  for (const tier of ['high', 'mid', 'phone']) {
    const p = plan(id, tier), slots = p.slots, budget = WORLD_SCREEN_TIERS[tier];
    assert.ok(slots.length >= (tier === 'high' ? id === 'threshold' ? 35 : 90 : tier === 'mid' ? 28 : 20), id + ' ' + tier + ' added coverage');
    assert.ok(slots.length <= budget.max);
    assert.ok(slots.length + p.existingSlots.length <= Math.max(budget.totalMax, p.existingSlots.length));
    assert.equal(new Set(slots.map(s => s.id)).size, slots.length);
    assert.equal(selectScrolllerSlots(slots).length, slots.length / 2);
    assert.ok(slots.every(s => s.phone && [0, 3].includes(s.program)), 'every unassigned face is animated Loom');
    const sectors = new Set(slots.map(s => Math.floor(s.s / track.lengthM * 8)));
    assert.equal(sectors.size, 8, 'media remains distributed through the full lap');
    assert.ok(slots.some(s => s.cluster === 'crown'));
    assert.ok(slots.some(s => s.cluster === 'portrait'));
    assert.ok(slots.filter(s => s.cluster !== 'crown').every(s => {
      const frame = track.frameAt(s.s), bottom = V3.dot(V3.sub(s.C, frame.p), frame.n) - s.h / 2;
      return bottom >= 2 && bottom < 5;
    }), 'low pictures read beside the driver instead of floating in the sky');
    assert.equal(p.stats.videoOwners, 0); assert.equal(p.stats.extraUploads, 0);
  }
});

test('world faces clear every stacked road, authored solid, other picture and open shortcut mouth', () => {
  for (const [id, track] of Object.entries(tracks)) {
    const p = plan(id), solids = worldScreenBlockers(track), samples = roadSamples(track, 2);
    const clearance = { ...mediaLook(), sideM: 2, clearM: 10, underM: 5 };
    for (const slot of p.slots) {
      assert.equal(tubeClash(rectPoints(slot, 4), samples, clearance), null, slot.id + ' stacked-road clearance');
      assert.equal(worldScreenSightlines(track, slot, solids) >= 2, true, slot.id + ' visible on approach');
      const body = faceBox(slot);
      assert.ok(!solids.some(row => boudoirBoxesIntersect(body, row)), slot.id + ' solid intersection');
      assert.ok(!p.existingSlots.some(other => boudoirBoxesIntersect(body, faceBox(other), .3)), slot.id + ' duplicates authored face');
      for (const route of track.routes || []) assert.ok(slot.s < route.openS0 - 15 || slot.s > route.openS1 + 15, 'drop shortcut stays readable');
    }
  }
});

const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three': 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { Group, InstancedMesh, BoxGeometry, MeshBasicNodeMaterial, Matrix4, PerspectiveCamera, Frustum } = await import('three/webgpu');
const { buildMainframe } = await import('../render/mainframe.js');
const { createMediaSlots } = await import('../render/media.js');
hooks.deregister();

test('actual transformed opaque instances become correctly positioned visibility blockers', () => {
  const group = new Group(); group.position.set(10, 20, 30); group.rotation.y = Math.PI / 2;
  const geometry = new BoxGeometry(2, 4, 6), material = new MeshBasicNodeMaterial();
  const mesh = new InstancedMesh(geometry, material, 2); group.add(mesh);
  mesh.setMatrixAt(0, new Matrix4().makeTranslation(5, 0, 0));
  mesh.setMatrixAt(1, new Matrix4().makeScale(2, 3, 4).setPosition(-5, 0, 0));
  const solids = worldScreenSolids(group);
  assert.equal(solids.length, 2);
  assert.ok(Math.hypot(...V3.sub(solids[0].p, [10, 20, 25])) < 1e-5);
  assert.ok(Math.hypot(...V3.sub(solids[1].p, [10, 20, 35])) < 1e-5);
  assert.deepEqual(solids[1].size.map(v => Math.round(v)), [4, 12, 24]);
  mesh.dispose(); geometry.dispose(); material.dispose();
});

test('Mainframe supplements clear actual machine instances and retain every district', t => {
  const track = tracks.mainframe, layout = authored(track), machine = buildMainframe(track, null, { mediaLayout: layout });
  t.after(() => machine.dispose());
  const solids = worldScreenSolids(machine.group), existingSlots = worldScreenExistingSlots(track, layout);
  assert.ok(solids.length > 1000, 'test uses real authored machine geometry');
  const p = worldScreenLayout(track, { solids, existingSlots });
  assert.ok(p.slots.length >= 90);
  assert.ok(p.slots.length + existingSlots.length <= WORLD_SCREEN_TIERS.high.totalMax);
  const districts = new Set(p.slots.map(s => track.sections[track.sectionAt(s.s)].id));
  for (const id of ['boot-hall', 'core-approach', 'spiral-core', 'upper-bus', 'lower-bus', 'submerged', 'emerge']) assert.ok(districts.has(id), id);
  for (const slot of p.slots) {
    assert.ok(worldScreenSightlines(track, slot, solids) >= 2, slot.id + ' actual visible image');
    assert.ok(!solids.some(row => boudoirBoxesIntersect(faceBox(slot), row)), slot.id + ' actual solid clearance');
  }
});

test('hundreds of world faces retain the same placeholder draws and prewarmed video-owner budgets', t => {
  const gradient = { addColorStop() {} }, context = new Proxy({}, { get: (o, k) => k in o ? o[k] : /Gradient/.test(k) ? () => gradient : () => {} });
  const make = (width, height) => ({ width, height, getContext: () => context });
  const track = tracks.keep, layout = authored(track);
  const plain = createMediaSlots(null, track, { layout, make });
  const rich = createMediaSlots(null, track, { layout, make, extraSlots: plan('keep').slots, extraSlotsDynamic: false });
  t.after(() => { plain.dispose(); rich.dispose(); });
  assert.ok(rich.slots.length > plain.slots.length + 90);
  assert.equal(rich.object.children.filter(o => o.isInstancedMesh).length, plain.object.children.filter(o => o.isInstancedMesh).length,
    'procedural world faces use existing instanced draws');
  assert.equal(rich.videoCapacity, plain.videoCapacity);
  assert.equal(rich.stats().prewarmedVideoTextures, plain.stats().prewarmedVideoTextures);
  const planes = rich.object.children.filter(o => o.isInstancedMesh), versions = planes.map(o => o.instanceMatrix.version);
  rich.update(1 / 60);
  assert.deepEqual(planes.map(o => o.instanceMatrix.version), versions, 'static supplements upload no new instance matrices every frame');
});

test('future course ids inherit bounded distributed screens without requiring a bespoke media file', () => {
  const track = Object.create(tracks.threshold); track.id = 'goonworks';
  const p = worldScreenLayout(track, { tier: 'phone' });
  assert.ok(p.slots.length >= 20); assert.ok(p.slots.length <= WORLD_SCREEN_TIERS.phone.max);
  assert.ok(p.slots.every(s => s.id.startsWith('world-goonworks-')));
});

test('supplemental pictures leave the full animated halo orbit clear', () => {
  for (const id of ['threshold', 'mainframe']) {
    const track = tracks[id], layout = authored(track), supplemental = plan(id).slots;
    for (let phase = 0; phase < 12; phase++) {
      const orbit = haloSlots(layout.halo, track.frameAt((layout.halo.f || 0) * track.lengthM), layout.halo.count, phase * Math.PI / 6);
      for (const halo of orbit) for (const slot of supplemental)
        assert.ok(!boudoirBoxesIntersect(faceBox(halo), faceBox(slot), .3), id + ' halo orbit ' + phase);
    }
  }
});

test('real source and credit frustum bounds follow moving picture matrices and source-off', t => {
  const gradient = { addColorStop() {} }, context = new Proxy({}, { get: (o, k) => k in o ? o[k] : /Gradient/.test(k) ? () => gradient : () => {} });
  const make = (width, height) => ({ width, height, getContext: () => context });
  const slot = { id: 'moving-test', frame: 'billboard', w: 8, h: 4, C: [0, 3, 0], X: [1, 0, 0], Y: [0, 1, 0], Z: [0, 0, 1], phone: true };
  const media = createMediaSlots(null, tracks.threshold, { layout: { slots: [] }, extraSlots: [slot], make });
  t.after(() => media.dispose());
  assert.ok(media.setSource(slot.id, { kind: 'canvas', el: make(640, 360), borrowed: true, credit: 'scrolller' }));
  const source = media.object.getObjectByName('media-world-source-' + slot.id), credit = media.object.getObjectByName('media-world-credit-' + slot.id);
  assert.equal(source.frustumCulled, true); assert.equal(credit.frustumCulled, true);
  const camera = new PerspectiveCamera(65, 1.6, .1, 100); camera.position.set(0, 3, 20); camera.lookAt(0, 3, 0); camera.updateMatrixWorld();
  const frustum = new Frustum().setFromProjectionMatrix(new Matrix4().multiplyMatrices(camera.projectionMatrix, camera.matrixWorldInverse));
  media.object.updateMatrixWorld(true);
  assert.ok(frustum.intersectsObject(source)); assert.ok(frustum.intersectsObject(credit));
  slot.C[0] = 1000; media.update(1 / 60); media.object.updateMatrixWorld(true);
  assert.equal(frustum.intersectsObject(source), false); assert.equal(frustum.intersectsObject(credit), false);
  assert.ok(media.setSource(slot.id, null)); assert.equal(source.visible, false); assert.equal(credit.visible, false);
});
