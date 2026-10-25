import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { screenBodySize, screenBodyBox } from '../render/screenBodyMath.js';
import { axesFrom } from '../render/mediaMath.js';

const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { Mesh, Matrix4, Vector3, Raycaster, Scene } = await import('three/webgpu');
const { screenBodyGeometry, screenBodyMaterial } = await import('../render/screenBody.js');
const { createMediaSlots } = await import('../render/media.js');
const { createMarquees } = await import('../render/marquee.js');
const { createCueWindows } = await import('../render/cueWindows.js');
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
