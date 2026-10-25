import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { readFileSync } from 'node:fs';
import { screenBodySize, screenBodyBox } from '../render/screenBodyMath.js';
import { axesFrom } from '../render/mediaMath.js';
import { loadTrack } from '../track/load.js';
import { goonworksMediaLayout } from '../render/goonworksMedia.js';
import { largeMediaLayout } from '../render/worldMediaScale.js';
import { worldScreenSolids, worldScreenLayout, worldScreenExistingSlots } from '../render/worldScreens.js';
import { goonworksBoxesIntersect } from '../render/goonworksWorld.js';
import { goonworksScreenVisibility } from '../render/goonworksVisibility.js';

const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { Mesh, Matrix4, Vector3, Raycaster, Scene } = await import('three/webgpu');
const { screenBodyGeometry, screenBodyMaterial } = await import('../render/screenBody.js');
const { createMediaSlots } = await import('../render/media.js');
const { createMarquees } = await import('../render/marquee.js');
const { createCueWindows } = await import('../render/cueWindows.js');
const { buildGoonworks } = await import('../render/goonworks.js');
hooks.deregister();

test('cabinet is closed from front, sides and rear while the inset bezel exposes the full image', t => {
  const shell = new Mesh(screenBodyGeometry(), screenBodyMaterial());
  const rim = new Mesh(screenBodyGeometry(true), screenBodyMaterial(true));
  t.after(() => { for (const mesh of [shell, rim]) { mesh.geometry.dispose(); mesh.material.dispose(); } });
  const hit = (mesh, origin, direction) => new Raycaster(new Vector3(...origin), new Vector3(...direction)).intersectObject(mesh);
  assert.ok(hit(shell, [0, 0, 2], [0, 0, -1]).length, 'front closes behind the image');
  assert.ok(hit(shell, [0, 0, -3], [0, 0, 1]).length, 'back is opaque');
  for (const [origin, direction] of [[[2, 0, -.6], [-1, 0, 0]], [[-2, 0, -.6], [1, 0, 0]],
    [[0, 2, -.6], [0, -1, 0]], [[0, -2, -.6], [0, 1, 0]]]) assert.ok(hit(shell, origin, direction).length, 'sidewall faces out');
  for (const x of [-.5, 0, .5]) for (const y of [-.5, 0, .5])
    assert.equal(hit(rim, [x, y, 2], [0, 0, -1]).length, 0, 'bezel leaves every picture corner readable');
  assert.ok(hit(rim, [.525, 0, 2], [0, 0, -1]).length, 'real raised bevel catches the light');
  for (const mesh of [shell, rim]) {
    assert.ok(mesh.geometry.getAttribute('position').count < 160);
    assert.ok([...mesh.geometry.getAttribute('normal').array].every(Number.isFinite));
  }
});

test('fading marquee and window cabinets cannot punch opaque holes through the race during their exit', t => {
  const context = new Proxy({}, { get: (o, k) => k in o ? o[k] : () => {} });
  const make = () => ({ getContext: () => context }), scene = new Scene(), track = { id: 'threshold' };
  const acquireAtlas = () => ({ texture: null, dispose() {} });
  const marquees = createMarquees(scene, track, { make, acquireAtlas, visibility: null });
  const windows = createCueWindows(scene, track, { make, acquireAtlas });
  t.after(() => { marquees.dispose(); windows.dispose(); });
  let cabinets = 0;
  scene.traverse(object => {
    const material = object.material;
    if (!material?.isMeshStandardNodeMaterial) return;
    cabinets++;
    assert.equal(material.transparent, true);
    assert.equal(material.depthWrite, false, 'transient housing fades without writing invisible depth');
  });
  assert.ok(cabinets >= 80, 'every marquee and pooled window has a fading cabinet and bezel');
});

test('every media category gets a depth-scaled enclosure in constant shared batches, including moving cards', t => {
  const gradient = { addColorStop() {} }, context = new Proxy({}, { get: (o, k) => k in o ? o[k] : /Gradient/.test(k) ? () => gradient : () => {} });
  const make = (width, height) => ({ width, height, getContext: () => context });
  const slots = ['billboard', 'halo', 'tunnel', 'card'].map((frame, i) => ({ id: 'test-' + frame, frame, phone: true,
    w: 8 + i * 3, h: 4 + i, C: [i * 20, 10, 3], ...axesFrom([.2, -.1, 1], [0, 1, 0]) }));
  const media = createMediaSlots(null, { id: 'test' }, { layout: { slots: [] }, extraSlots: slots, samples: [], make });
  t.after(() => media.dispose());
  const back = media.object.getObjectByName('media-back'), rim = media.object.getObjectByName('media-bezel');
  assert.ok(back.isInstancedMesh && rim.isInstancedMesh);
  assert.equal(back.count, slots.length); assert.equal(rim.count, slots.length);
  assert.equal(back.material.transparent, false); assert.equal(back.material.depthWrite, true);
  const matrix = new Matrix4(), vertex = new Vector3();
  for (const [i, slot] of slots.entries()) {
    const bounds = screenBodyBox(slot), dimensions = screenBodySize(slot);
    assert.ok(dimensions.depth >= .28 && dimensions.depth <= (slot.frame === 'card' ? .65 : 1.65));
    for (const mesh of [back, rim]) {
      mesh.getMatrixAt(i, matrix); assert.ok(matrix.determinant() > 0, 'no reflected road basis');
      const p = mesh.geometry.getAttribute('position');
      for (let v = 0; v < p.count; v++) {
        vertex.fromBufferAttribute(p, v).applyMatrix4(matrix).sub(new Vector3(...bounds.p));
        for (let a = 0; a < 3; a++) assert.ok(Math.abs(vertex.dot(new Vector3(...bounds.axes[a]))) <= bounds.size[a] / 2 + 1e-5, 'placement envelope contains physical casing');
      }
    }
  }
  slots[3].C = [130, 21, -7]; media.update(.016);
  back.getMatrixAt(3, matrix);
  assert.deepEqual([matrix.elements[12], matrix.elements[13], matrix.elements[14]], slots[3].C, 'moving card carries its entire body');
  const held = [...matrix.elements]; media.update(1, { paused: true }); back.getMatrixAt(3, matrix);
  assert.deepEqual([...matrix.elements], held);
});

test('fitted GOONWORKS screens carry one white and wine enclosure without leaving empty scenery cabinets', t => {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/goonworks.json', import.meta.url), 'utf8')));
  const world = buildGoonworks(track);
  t.after(() => world.dispose());
  const authored = goonworksMediaLayout(track), solids = worldScreenSolids([world.group]);
  assert.ok(!solids.some(row => ['goonworks-thick-ribbon', 'goonworks-blush-atmosphere', 'goonworks-rabbit-inner-shell', 'goonworks-rabbit-outer-shell'].includes(row.name)), 'Whole-course and hollow bounds must not fill usable screen space');
  assert.ok(solids.length > world.plan.rows.length, 'Actual architecture and its outline hulls still block placement');
  const layout = largeMediaLayout(track, authored, { solids });
  assert.ok(layout.slots.length >= authored.slots.length * .8, 'Real scene bounds retain the authored screen coverage');
  const walls = solids.filter(row => row.id?.startsWith('rabbit-wall-'));
  assert.ok(walls.length > 100 && walls.length < 5000, 'Tube uses bounded wall patches rather than a filled volume');
  const extra = worldScreenLayout(track, { solids, existingSlots: worldScreenExistingSlots(track, layout) });
  assert.ok(extra.slots.length > 100, 'Tube clearance retains lap coverage');
  for (const slot of [...layout.slots, ...extra.slots])
    assert.ok(!walls.some(wall => goonworksBoxesIntersect(screenBodyBox(slot), wall)), slot.id + ' enclosure clears the opaque tube skin');
  const report = goonworksScreenVisibility(track, { ...world.plan, mounts: extra.slots });
  assert.ok(report.every(row => !row.blockers.some(([id]) => id === 'rabbit-shell')), 'Exact rendered tube triangles never hide supplemental faces');
  for (const id of ['world-goonworks-3-left', 'world-goonworks-3-right']) {
    const row = report.find(row => row.id === id);
    assert.ok(!row || row.visible >= .5, 'Previously 0% and 5% visible faces must move into view or be omitted');
  }
  const original = new Map(authored.slots.map(slot => [slot.id, slot]));
  assert.ok(layout.slots.some(slot => Math.hypot(...slot.C.map((v, i) => v - original.get(slot.id).C[i])) > 1), 'Exercises relocated pictures');
  assert.ok(!world.plan.rows.some(row => /-(cabinet|white-bezel)$/.test(row.id)), 'Authored mounts never leave independent housings or clearance blockers');
  assert.equal(world.stats.parts, world.plan.rows.length, 'Clearance plan contains the rendered scenery');

  const gradient = { addColorStop() {} }, context = new Proxy({}, { get: (o, k) => k in o ? o[k] : /Gradient/.test(k) ? () => gradient : () => {} });
  const make = (width, height) => ({ width, height, getContext: () => context });
  const media = createMediaSlots(null, track, { layout, samples: [], make });
  t.after(() => media.dispose());
  const back = media.object.getObjectByName('media-back'), rim = media.object.getObjectByName('media-bezel');
  assert.equal(back.count, layout.slots.length); assert.equal(rim.count, layout.slots.length);
  assert.equal(back.material.color.getHexString(), '661448');
  assert.equal(rim.material.color.getHexString(), 'ede7e6');
  assert.equal(rim.instanceColor, null, 'Porcelain rims retain the course colour instead of a second palette tint');
  const matrix = new Matrix4(), centers = [];
  for (const mesh of media.object.children) {
    if (!mesh.isInstancedMesh || mesh.geometry?.type !== 'PlaneGeometry' || ['media-border', 'media-strip'].includes(mesh.name)) continue;
    for (let i = 0; i < mesh.count; i++) { mesh.getMatrixAt(i, matrix); centers.push(new Vector3().setFromMatrixPosition(matrix)); }
  }
  assert.equal(centers.length, layout.slots.length, 'Every physical housing has exactly one picture');
  for (let i = 0; i < layout.slots.length; i++) {
    const target = new Vector3(...layout.slots[i].C);
    for (const mesh of [back, rim]) {
      mesh.getMatrixAt(i, matrix);
      assert.ok(new Vector3().setFromMatrixPosition(matrix).distanceTo(target) < .001, 'Complete casing follows the fitted picture');
    }
    assert.equal(centers.filter(center => center.distanceTo(target) < .02).length, 1, 'One visible image occupies each enclosure');
  }
});
