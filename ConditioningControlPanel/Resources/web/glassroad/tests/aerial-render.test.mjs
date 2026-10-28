import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';

const hooks = registerHooks({ resolve(s, c, next) {
  const files = { 'three/tsl': 'three.tsl.min.js', 'three/webgpu': 'three.webgpu.min.js' };
  return files[s] ? { url: new URL('../vendor/three/build/' + files[s], import.meta.url).href, shortCircuit: true } : next(s, c);
} });
const { createAirWords, AIR_WORD_CAPACITY, airWordBit, airWordPose, airWordSpark, AIR_WORD_MOTES, AIR_WORD_SPARKS,
  AIR_WORD_TRAIL } = await import('../render/airWords.js');
const { letterContours, extrudeLetters, letterCatchPoints } = await import('../render/wordSolid.js');
const { Scene, Box3, Mesh, MeshBasicMaterial, Raycaster, Vector3, BackSide } = await import('three/webgpu');
hooks.deregister();

function ringPixels(w, h) {
  const data = new Uint8ClampedArray(w * h * 4);
  for (let y = 20; y < h - 20; y++) for (let x = 20; x < w - 20; x++) {
    if (x >= 32 && x < w - 32 && y >= 32 && y < h - 32) continue;
    data[(y * w + x) * 4 + 3] = 255;
  }
  return data;
}
function documentFixture() {
  const canvases = [];
  return { canvases, createElement(tag) {
    assert.equal(tag, 'canvas');
    const calls = [], ctx = { measureText: t => ({ width: t.length * 60 }),
      getImageData: (x, y, w, h) => ({ data: ringPixels(w, h) }) };
    for (const name of ['clearRect', 'save', 'restore', 'translate', 'scale', 'strokeText', 'fillText']) {
      ctx[name] = (...args) => calls.push([name, ...args]);
    }
    const canvas = { calls, context: ctx, getContext: () => ctx }; canvases.push(canvas); return canvas;
  } };
}
function platform(overrides = {}) {
  return { id: 'air-word-0', text: 'Breathe', s: 50, p: [4, 12, 30], t: [0, 0, 1], n: [0, 1, 0], b: [1, 0, 0],
    widthM: 12, lengthM: 18, alpha: 1, solid: true, ...overrides };
}
function fixture(track = 'threshold', options = {}) {
  const scene = new Scene(), doc = documentFixture();
  return { scene, doc, view: createAirWords(scene, { id: track }, { doc, ...options }) };
}

test('words are closed extruded letters with real holes and side walls, never a backing rectangle', () => {
  const contours = letterContours(ringPixels(96, 96), 96, 96);
  assert.equal(contours.length, 2, 'outer contour and the counter of O');
  const geo = extrudeLetters(contours);
  assert.ok(geo.boundingBox.min.y < -1);
  assert.ok(Math.abs(geo.boundingBox.max.y) < 1e-6);
  assert.ok(geo.groups.some(g => g.materialIndex === 1), 'sidewall material group exists');
  const material = new MeshBasicMaterial(), mesh = new Mesh(geo, material);
  mesh.updateMatrixWorld(true);
  const ray = new Raycaster(new Vector3(0, 5, 0), new Vector3(0, -1, 0));
  assert.equal(ray.intersectObject(mesh).length, 0, 'letter counter stays open all the way through');
  ray.ray.origin.x = .4;
  assert.ok(ray.intersectObject(mesh).length > 0, 'the glyph stroke is a solid surface');
  geo.dispose(); material.dispose();
});

test('catch anchors touch thick glyph strokes, including counters and separated letters', () => {
  const separated = new Uint8ClampedArray(96 * 96 * 4);
  for (let y = 20; y < 76; y++) for (let x = 12; x < 84; x++) {
    if (x < 28 || x >= 68) separated[(y * 96 + x) * 4 + 3] = 255;
  }
  for (const pixels of [ringPixels(96, 96), separated]) {
    const geometry = extrudeLetters(letterContours(pixels, 96, 96)), points = letterCatchPoints(pixels, 96, 96, geometry);
    assert.ok(points.length >= 2 && points.length <= 9);
    const material = new MeshBasicMaterial(), mesh = new Mesh(geometry, material); mesh.updateMatrixWorld(true);
    for (const p of points) {
      assert.ok(Math.abs(p.x) <= .5 && Math.abs(p.z) <= .5);
      const ray = new Raycaster(new Vector3(p.x, 5, -p.z), new Vector3(0, -1, 0));
      assert.ok(ray.intersectObject(mesh).length > 0, 'catch point lies on visible geometry');
    }
    assert.ok(points.every(p => Math.abs(p.x) > .2 || Math.abs(p.z) > .2), 'never target central empty space');
    geometry.dispose(); material.dispose();
  }
});

test('pooled solid words keep course typography and colours with their top at the support plane', () => {
  const colors = new Set();
  for (const course of ['threshold', 'mainframe', 'keep', 'boudoir', 'goonworks']) {
    const { scene, doc, view } = fixture(course), p = platform(), run = { timeS: 3, platforms: [p] };
    assert.equal(doc.canvases.length, 1, 'one scratch canvas, never one texture per platform');
    view.update(run, 1 / 60);
    const root = view.object.children[0], letters = root.children[0];
    assert.equal(root.visible, true); assert.deepEqual(root.position.toArray(), p.p);
    assert.equal(root.children.length, 4, 'letters, particles, an ink outline and a bright rim, never a backplate');
    assert.equal(root.children[3].name, 'solid-letter-rim');
    assert.equal(letters.name, 'solid-letters');
    root.updateMatrixWorld(true);
    const bounds = new Box3().setFromObject(letters);
    assert.ok(Math.abs(bounds.max.x - bounds.min.x - p.widthM) < .001);
    assert.ok(Math.abs(bounds.max.z - bounds.min.z - p.lengthM) < .001);
    assert.ok(Math.abs(bounds.max.y - p.p[1]) < 1e-6);
    assert.ok(bounds.min.y < p.p[1] - 1);
    assert.ok(doc.canvases[0].calls.some(c => c[0] === 'fillText' && c[1] === 'Breathe'));
    const family = { threshold: 'editorial', mainframe: 'mono', keep: 'silk', boudoir: 'silk', goonworks: 'rounded' }[course];
    assert.ok(doc.canvases[0].context.font.includes('gr-' + family));
    colors.add(letters.material[0].color.getHex());
    assert.equal(p.catchText, p.text); assert.ok(p.catchPoints.length > 0);
    assert.equal(view.stats.solid, 1); assert.equal(view.stats.particles, AIR_WORD_MOTES + AIR_WORD_SPARKS);
    view.dispose(); assert.equal(scene.children.length, 0);
  }
  assert.equal(colors.size, 5);
});

test('ink outlines share each solid glyph and follow its tilt, compression and fade', () => {
  const { view } = fixture(), p = platform({ catchS: 4, landedS: 4 });
  const root = view.object.children[0], letters = root.children[0], outline = root.children[2];
  const material = outline.material;
  assert.equal(outline.name, 'solid-letter-outline');
  assert.ok(material.color.getHex() !== root.children[3].material.color.getHex(), 'a dark ink line inside a bright rim');
  assert.equal(material.side, BackSide);
  assert.ok(material.positionNode, 'expanded glyph hull is compiled during covered warmup');
  for (const timeS of [3.6, 4, 4.18, 4.4]) {
    p.text = 'phrase ' + timeS; p.alpha = timeS > 4 ? .45 : 1;
    view.update({ timeS, platforms: [p] }, .1);
    assert.equal(outline.geometry, letters.geometry, 'outline preserves individual contours and counters');
    assert.deepEqual(outline.position.toArray(), letters.position.toArray());
    assert.deepEqual(outline.rotation.toArray(), letters.rotation.toArray());
    assert.deepEqual(outline.scale.toArray(), letters.scale.toArray());
    assert.equal(material.opacity, letters.material[0].opacity);
    assert.equal(outline.material, material, 'no new material at phrase contact');
  }
  let released = 0; material.addEventListener('dispose', () => released++);
  view.dispose(); view.dispose(); assert.equal(released, 1);
});

test('materialization and dissolve never grow or move the support silhouette', () => {
  const { view } = fixture(), p = platform(), root = view.object.children[0], letters = root.children[0];
  const run = { timeS: 1, platforms: [p] };
  view.update(run, .1); const scale = letters.scale.toArray(), solidOpacity = letters.material[0].opacity;
  p.alpha = .45; p.solid = false; view.update(run, .1);
  assert.equal(view.stats.solid, 0); assert.deepEqual(letters.scale.toArray(), scale);
  assert.ok(letters.material[0].opacity < solidOpacity);
  p.alpha = 0; view.update(run, .1); assert.equal(view.stats.visible, 0); assert.equal(root.visible, false);
  view.dispose();
});

test('song clock and every mote freeze on pause, while restart and missing runs clear immediately', () => {
  const { view } = fixture(), run = { timeS: 3, platforms: [platform()] };
  view.update(run, .1); const root = view.object.children[0], pool = root.children[1];
  const matrices = pool.instanceMatrix.array.slice(), before = { ...view.stats };
  run.timeS = 10; run.platforms[0].alpha = .2;
  view.update(run, 7, { paused: true });
  assert.deepEqual(pool.instanceMatrix.array, matrices); assert.deepEqual(view.stats, before);
  view.clear(); assert.equal(view.stats.visible, 0); assert.equal(pool.count, 0);
  view.update(run, .1); view.update(null, .1, { paused: true }); assert.equal(root.visible, false);
  view.update(run, .1); view.update(run, .1, { mode: 'menu' }); assert.equal(root.visible, false);
  view.dispose();
});

test('reused slots keep meshes and materials fixed and bound their shared geometry cache', () => {
  const { view, doc } = fixture(), children = view.object.children.slice(), run = { timeS: 0, platforms: [] };
  const resources = children.flatMap(root => [root.children[0], ...root.children[0].material, root.children[1].geometry]);
  const discarded = children[0].children[0].geometry; let discardedCount = 0;
  discarded.addEventListener('dispose', () => discardedCount++);
  for (let i = 0; i < 80; i++) {
    run.timeS = i / 10; run.platforms = [platform({ text: 'word ' + i, id: 'air-word-' + i % AIR_WORD_CAPACITY })];
    view.update(run, .1); assert.ok(view.stats.cachedWords <= 24);
  }
  assert.deepEqual(view.object.children, children); assert.equal(doc.canvases.length, 1);
  assert.deepEqual(children.flatMap(root => [root.children[0], ...root.children[0].material, root.children[1].geometry]), resources);
  assert.equal(discardedCount, 1, 'unreferenced old word released when cache fills');
  const current = children[0].children[0].geometry; let released = 0;
  current.addEventListener('dispose', () => released++);
  view.dispose(); view.dispose(); assert.equal(released, 1); assert.equal(discardedCount, 1);
});

test('Gentle and refused brightness budgets retain readable words with calmer particles', () => {
  for (const options of [{ feel: { get: () => true } }, { photosafe: { request: () => false } }]) {
    const { view } = fixture('mainframe', options);
    view.update({ timeS: 3, platforms: [platform()] }, .1);
    assert.equal(view.stats.solid, 1); assert.equal(view.stats.particles, 20);
    view.dispose();
  }
  for (let i = 0; i < 40; i++) for (const alpha of [0, .3, 1]) {
    const v = airWordBit(i, 15, 12, 18, alpha, false);
    assert.ok(Object.values(v).every(Number.isFinite));
    assert.ok(Math.abs(v.x) <= 6 && Math.abs(v.z) <= 9 && v.size >= 0 && v.size <= .11);
    if (!alpha) assert.equal(v.size, 0);
  }
});

test('HD letters float and tilt into view, settle for the catch and compress on contact', () => {
  const { view, doc } = fixture();
  assert.equal(doc.canvases[0].width, 1024);
  assert.equal(doc.canvases[0].height, 256);
  const p = platform({ catchS: 4, landedS: 4, index: 1 });
  const before = airWordPose(p, 3.6), contact = airWordPose(p, 4), bounced = airWordPose(p, 4.18);
  assert.ok(Math.abs(before.y) > .001 && Math.abs(before.tilt) > .001);
  assert.equal(contact.y, 0); assert.equal(contact.tilt, 0); assert.equal(contact.depth, 1);
  assert.ok(bounced.depth < .8 && bounced.y === 0, 'compression leaves the support top planted');
  assert.ok(airWordPose(p, 4.18, true).depth > bounced.depth);
  const run = { timeS: 3.6, platforms: [p, platform({ id: 'air-word-1' }), platform({ id: 'air-word-2' })] };
  view.update(run, .1); assert.equal(view.stats.visible, 2);
  view.dispose();
});

test('sparks burst when a word appears and on contact, and the flight trail is allocated up front and freezes on pause', () => {
  const p = platform({ bornS: 2, landedS: 4, index: 0 });
  assert.equal(airWordSpark(3, 1.9, p, false).size, 0, 'nothing before the word appears');
  const appear = airWordSpark(3, 2.3, p, false), contact = airWordSpark(3, 4.3, p, false), calm = airWordSpark(3, 4.3, p, true);
  assert.ok(appear.size > .1 && contact.size > .1, 'a burst on appearance and on each landing');
  assert.ok(calm.size < contact.size && Math.hypot(calm.x, calm.z) < Math.hypot(contact.x, contact.z), 'Gentle calms the burst');
  assert.equal(airWordSpark(3, 5, p, false).size, 0, 'the burst is gone within a beat');
  const { scene, view } = fixture(), trail = view.trail, matrices = trail.instanceMatrix.array;
  assert.equal(trail.instanceMatrix.count, AIR_WORD_TRAIL); assert.ok(scene.children.includes(trail));
  const kart = { x: 0, y: 10, z: 0 }, run = { timeS: 0, flying: true, platforms: [] };
  for (let i = 0; i < 20; i++) { run.timeS = i / 60; kart.z = i * .4; view.update(run, 1 / 60, { kart }); }
  assert.ok(trail.count > 8, 'a sparkle trail follows the airborne kart');
  const frozen = trail.instanceMatrix.array.slice(), count = trail.count;
  run.timeS = 3; view.update(run, 1 / 60, { kart, paused: true });
  assert.equal(trail.count, count); assert.deepEqual(trail.instanceMatrix.array, frozen);
  view.update(run, 1 / 60, { kart, motion: 'off' }); run.timeS = 3.1; view.update({ ...run, flying: false }, 1 / 60, { kart });
  assert.equal(trail.count, 0, 'old motes fade out and motion off emits none');
  assert.equal(trail.instanceMatrix.array, matrices, 'no reallocation');
  view.dispose(); assert.equal(scene.children.length, 0);
});
