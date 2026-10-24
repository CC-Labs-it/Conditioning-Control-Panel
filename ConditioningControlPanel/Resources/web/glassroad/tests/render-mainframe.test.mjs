import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { scanPose } from '../sim/mainframeScan.js';
import { bodyClear, sampleTrack, WORLD_DEFAULTS } from '../render/colossiMath.js';
import { mediaLook, placeBillboard } from '../render/mediaMath.js';

const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true }
    : next(specifier, context);
} });
const { buildMainframe, reactorRoom } = await import('../render/mainframe.js');
const { createPalette } = await import('../render/materials/palette.js');
const { Raycaster, Vector3 } = await import('three/webgpu');
hooks.deregister();
const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/mainframe.json', import.meta.url), 'utf8')));
const mediaLayout = JSON.parse(readFileSync(new URL('../tracks/mainframe.media.json', import.meta.url), 'utf8'));

test('floating processor motion stays inside clear bounds and freezes on pause', () => {
  const samples = sampleTrack(track, 4), world = buildMainframe(track, null, { samples, mediaLayout });
  assert.ok(world.floating.sites.length >= 10);
  for (const site of world.floating.sites) assert.ok(bodyClear(site.capsule, samples).ok);
  const body = world.floating.group.getObjectByName('floating-processor-body');
  const original = [...body.instanceMatrix.array]; world.update(1, {});
  assert.notDeepEqual([...body.instanceMatrix.array], original);
  const moved = [...body.instanceMatrix.array]; world.update(2, { paused: true });
  assert.deepEqual([...body.instanceMatrix.array], moved);
  world.dispose();
});

test('reactor placement follows a translated course and preserves the inner racing clearance', () => {
  const json = JSON.parse(readFileSync(new URL('../tracks/mainframe.json', import.meta.url), 'utf8'));
  const delta = [800, 37, -250]; json.nodes.forEach(n => { n.p = n.p.map((v, k) => v + delta[k]); });
  const room = reactorRoom(track), shifted = reactorRoom(loadTrack(json));
  room.center.forEach((v, k) => assert.ok(Math.abs(shifted.center[k] - v - delta[k]) < 0.01));
  assert.ok(Math.abs(room.radius - shifted.radius) < 0.01);
  const spiral = track.sections.find(s => s.id === 'spiral-core');
  for (let s = spiral.fromS; s < spiral.toS; s += 5) {
    const f = track.frameAt(s);
    assert.ok(Math.hypot(f.p[0] - room.center[0], f.p[2] - room.center[2]) - f.w / 2 - room.radius >= 10.5);
  }
});

test('reservoir follows the wet route at its shared waterline and bounds all bubbles below it', () => {
  const world = buildMainframe(track, null), surface = world.group.getObjectByName('mainframe-reservoir-surface');
  const p = surface.geometry.attributes.position, wet = world.reservoir;
  assert.ok(p.count > 40, 'a sampled route strip replaces the old broad AABB plane');
  for (let i = 0; i < p.count; i++) assert.equal(p.getY(i), wet.waterY);
  const end = track.frameAt(wet.emerge?.toS ?? wet.submerged.toS);
  assert.ok(Math.hypot((p.getX(p.count - 2) + p.getX(p.count - 1)) / 2 - end.p[0],
    (p.getZ(p.count - 2) + p.getZ(p.count - 1)) / 2 - end.p[2]) < 0.01);
  world.update(7, {});
  const bubbles = world.group.getObjectByName('mainframe-coolant-bubbles');
  for (let i = 0; i < bubbles.count; i++) assert.ok(bubbles.instanceMatrix.array[i * 16 + 13] <= wet.waterY - 0.19);
  const samples = sampleTrack(track, 4);
  for (const landmark of world.landmarks) assert.ok(bodyClear(landmark.capsule, samples).ok);
  world.dispose();
});

test('decorative floor slabs stay below the driving surface through bank transitions', () => {
  const world = buildMainframe(track, null), floor = world.group.getObjectByName('mainframe-floor');
  world.group.updateMatrixWorld(true);
  const ray = new Raycaster(), blocked = [];
  for (let s = 0; s < track.lengthM; s += 8) for (const fraction of [-0.9, 0, 0.9]) {
    const f = track.surfaceAt(s, track.frameAt(s).w / 2 * fraction);
    const up = new Vector3(...f.n), start = new Vector3(...f.p).addScaledVector(up, 1.2);
    ray.set(start, up.negate()); ray.far = 1.19;
    if (ray.intersectObject(floor, false).length) blocked.push({ s, fraction });
  }
  assert.deepEqual(blocked, [], 'no decorative slab can cut through the road skin');
  world.dispose();
});

test('spill edges and the hex gallery void stay open below the driving surface', () => {
  const world = buildMainframe(track, null), opaque = [], ray = new Raycaster();
  world.group.updateMatrixWorld(true);
  world.group.traverse(o => { if (o.isMesh && !o.material.transparent) opaque.push(o); });
  const check = (s, x, depth) => {
    const f = track.frameAt(s), start = new Vector3(...f.p.map((v, k) => v + f.b[k] * x - f.n[k] * 0.2));
    ray.set(start, new Vector3(...f.n).negate()); ray.far = depth;
    assert.equal(ray.intersectObjects(opaque, false).length, 0, `open below s=${s.toFixed(1)}, x=${x}`);
  };
  for (const section of track.sections.filter(s => /spillway|coolant-chute/.test(s.id))) {
    for (let s = section.fromS + 12; s < section.toS - 12; s += 24) for (const side of [-1, 1]) check(s, side * (track.frameAt(s).w / 2 + 0.4), 5);
  }
  for (const field of track.fields) for (let s = field.fromS + 12; s < field.toS - 12; s += 24)
    for (const x of [-40, 0, 40]) check(s, x, 160);
  world.dispose();
});

test('framed display bays keep billboard faces visible from the approaching road', () => {
  const samples = sampleTrack(track, 4), world = buildMainframe(track, null, { samples, mediaLayout });
  const faces = mediaLayout.billboards.map(b => placeBillboard(b, track, samples, mediaLook())).filter(Boolean);
  assert.equal(faces.length, mediaLayout.billboards.length);
  const opaque = []; world.group.updateMatrixWorld(true);
  world.group.traverse(o => { if (o.isMesh && !o.material.transparent) opaque.push(o); });
  const ray = new Raycaster(), start = new Vector3(), target = new Vector3(), direction = new Vector3(), blocked = [];
  for (const face of faces) for (const lead of [30, 45, 60]) {
    let visible = 0;
    for (const u of [-0.3, 0, 0.3]) for (const v of [-0.25, 0, 0.25]) {
      const f = track.frameAt(face.s - lead);
      start.fromArray(f.p.map((x, i) => x + f.n[i] * 3));
      target.fromArray(face.C.map((x, i) => x + face.X[i] * face.w * u + face.Y[i] * face.h * v));
      direction.subVectors(target, start); ray.set(start, direction.clone().normalize()); ray.far = direction.length() - 0.1;
      const hits = ray.intersectObjects(opaque, false);
      if (!hits.length) visible++;
      if (u === 0 && v === 0 && hits.length) blocked.push(`${face.id}: centre from ${lead} m, ${hits[0].object.name || hits[0].object.parent.name}`);
      // A real scan gantry may briefly cover a narrow edge; walls, racks and cables may not.
      const hit = hits.find(h => !h.object.parent.name.startsWith('scan:'));
      if (hit) blocked.push(`${face.id}, ${lead} m, ${u}/${v}: ${hit.object.name || hit.object.parent.name}`);
    }
    if (visible < 6) blocked.push(`${face.id}: only ${visible}/9 visible from ${lead} m`);
  }
  assert.deepEqual(blocked, []);
  assert.ok(world.counts.wall > 250, 'the display bays retain substantial enclosed architecture');
  world.dispose();
});

test('Mainframe constructs server batches and scan volumes before the first update', () => {
  const world = buildMainframe(track, null);
  assert.ok(world.counts.shell > 10);
  assert.ok(world.counts.green > world.counts.shell);
  assert.ok(world.scans.length >= 2);
  assert.ok(world.counts.wall > 50, 'the road runs through enclosed machine aisles');
  assert.ok(world.counts.floor > 50, 'structural decks anchor the road');
  assert.equal(world.core.name, 'mainframe-reactor');
  for (const scan of world.scans) {
    assert.equal(scan.panel.scale.x, scan.zone.width);
    assert.equal(scan.panel.scale.y, scan.zone.height, 'visible height matches banked-road collision clearance');
    assert.equal(scan.panel.scale.z, scan.zone.len, 'visible depth covers the entire collision region');
    assert.equal(scan.floor.scale.z, scan.zone.len);
  }
  world.dispose();
});

test('coolant arches never include the lower hoop across the racing line', () => {
  const world = buildMainframe(track, null), arches = [];
  world.group.traverse(o => { if (o.name === 'mainframe-tube-arch') arches.push(o); });
  assert.ok(arches.length > 10);
  for (const arch of arches) {
    const p = arch.geometry.attributes.position;
    for (let i = 0; i < p.count; i++) assert.ok(p.getY(i) >= -0.81, 'only the upper semicircle may be present');
  }
  assert.ok(world.group.getObjectByName('mainframe-reservoir-surface'));
  world.dispose();
});

test('three real Loom displays are built once, clear every road and respect pause and Gentle', () => {
  let draws = 0;
  const gradient = { addColorStop() {} };
  const context = new Proxy({}, {
    get: (o, key) => key in o ? o[key] : /Gradient/.test(key) ? () => gradient : () => { draws++; },
    set: (o, key, value) => { o[key] = value; return true; },
  });
  const makeCanvas = (width, height) => ({ width, height, getContext: () => context });
  const samples = sampleTrack(track, 4), world = buildMainframe(track, null, { makeCanvas, samples });
  assert.equal(world.displays.length, 3);
  assert.ok(draws > 0);
  for (const d of world.displays) assert.ok(bodyClear({ B: d.center, D: d.frame.n, len: 0, r: d.radius + 1.5 }, samples,
    { ...WORLD_DEFAULTS, sideM: 1, underM: 1 }).ok);
  const disc = world.displays[0].disc, initialDraws = draws;
  world.update(1, { paused: true }); assert.equal(disc.rotation.z, 0);
  assert.equal(draws, initialDraws, 'pause freezes canvas evolution as well as mesh rotation');
  world.update(1, {}, true); assert.equal(disc.rotation.z, 0.18);
  world.update(1, {}, false); assert.equal(disc.rotation.z, 0.63);
  assert.ok(draws > initialDraws, 'Loom arms evolve as well as turning');
  assert.equal(new Set(world.displays.map(d => d.tex)).size, 3);
  world.dispose();
});

test('Mainframe scans follow the shared song clock, freeze on pause and warn after a seek', () => {
  const world = buildMainframe(track, null), scan = world.scans[0];
  world.update(0.1, { scanTimeS: 4.75, scanBpm: 120 }, false);
  assert.equal(scan.panel.position.x, scanPose(scan.zone, 4.75, 120).x);
  const before = scan.panel.position.x;
  world.update(0.1, { scanTimeS: 5.5, scanBpm: 120, paused: true });
  assert.equal(scan.panel.position.x, before);
  world.update(0.1, { scanTimeS: 5.5, scanBpm: 120, scanReady: false }, true);
  assert.equal(scan.panelMat.color.getHexString(), 'ffc65c');
  assert.ok(scan.panelMat.opacity < 0.52);
  world.dispose();
});

test('Mainframe palette is opt-in and accepts all three existing section ids', () => {
  const themed = createPalette('mainframe'), standard = createPalette();
  for (const key of ['dawn', 'deep', 'afterglow']) themed.set(key, key, 1);
  assert.equal(themed.theme, 'mainframe');
  assert.equal(standard.theme, 'threshold');
  assert.ok(themed.rampRoad(themed.hue));
});
