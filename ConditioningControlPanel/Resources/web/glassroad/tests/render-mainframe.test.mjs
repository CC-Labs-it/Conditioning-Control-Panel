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
const { buildMainframe } = await import('../render/mainframe.js');
const { createPalette } = await import('../render/materials/palette.js');
const { Raycaster, Vector3 } = await import('three/webgpu');
hooks.deregister();
const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/mainframe.json', import.meta.url), 'utf8')));
const mediaLayout = JSON.parse(readFileSync(new URL('../tracks/mainframe.media.json', import.meta.url), 'utf8'));

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
      if (u === 0 && v === 0) assert.equal(hits.length, 0, `${face.id}: centre visible from ${lead} m`);
      // A real scan gantry may briefly cover a narrow edge; walls, racks and cables may not.
      const hit = hits.find(h => !h.object.parent.name.startsWith('scan:'));
      if (hit) blocked.push(`${face.id}, ${lead} m, ${u}/${v}: ${hit.object.name || hit.object.parent.name}`);
    }
    assert.ok(visible >= 6, `${face.id}: at least two thirds of sampled face visible from ${lead} m`);
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
  world.update(1, {}, true); assert.equal(disc.rotation.z, 0.045);
  world.update(1, {}, false); assert.equal(disc.rotation.z, 0.135);
  assert.equal(draws, initialDraws, 'rotation does not redraw or upload another canvas frame');
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
  assert.equal(standard.theme, '');
  assert.ok(themed.rampRoad(themed.hue));
});
