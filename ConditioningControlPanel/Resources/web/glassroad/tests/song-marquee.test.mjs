import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
const hooks = registerHooks({ resolve(s, c, next) {
  const files = { 'three': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js', 'three/webgpu': 'three.webgpu.min.js' };
  if (s.startsWith('three/addons/')) return { url: new URL('../vendor/' + s, import.meta.url).href, shortCircuit: true };
  return files[s] ? { url: new URL('../vendor/three/build/' + files[s], import.meta.url).href, shortCircuit: true } : next(s, c);
} });
const { createMarquees, createMarqueeSurface, marqueePose } = await import('../render/marquee.js');
const { createScreenFx } = await import('../render/screenfx.js');
const { Scene, Vector3 } = await import('three/webgpu');
hooks.deregister();
test('the complete screen-effects module loads so world cues retain blackout and fog', () => {
  assert.equal(typeof createScreenFx, 'function');
});
test('hidden tabs purge copied marquee pixels even when animation is suspended', () => {
  const visibility = new EventTarget(); visibility.hidden = false;
  const { effect } = setup({ visibility, source: () => ({ width: 640, height: 360 }) });
  effect.update([row()], .1, { songS: 4 }); assert.equal(effect.stats.privateSurfaces, 1);
  visibility.hidden = true; visibility.dispatchEvent(new Event('visibilitychange'));
  assert.equal(effect.stats.privateSurfaces, 0); assert.equal(effect.stats.active, 0);
  effect.update([row()], 0, { paused: true }); assert.equal(effect.stats.privateSurfaces, 0); effect.dispose();
});
const row = (extra = {}) => ({ id: 'screen', kind: 'MARQUEE', startS: 3, endS: 8, bornS: 1, alpha: 1,
  frame: { p: [0, 0, 0], b: [1, 0, 0], n: [0, 1, 0], t: [0, 0, -1], w: 32 },
  params: { variant: 'chains', exit: 'snap' }, joltTimes: [4, 5, 6], ...extra });
function canvas() {
  const calls = [], ctx = { fillRect() { calls.push('clear'); }, drawImage(image) { calls.push(image); }, fillText(text) { calls.push(text); } };
  return { width: 0, height: 0, calls, getContext: () => ctx };
}
function setup(extra = {}) {
  const scene = new Scene(), canvases = [], atlasImage = { width: 768, height: 512 }, released = [];
  const effect = createMarquees(scene, { id: 'goonworks' }, { make: () => { const c = canvas(); canvases.push(c); return c; },
    acquireAtlas: () => ({ texture: { image: atlasImage }, dispose() { released.push('atlas'); } }), ...extra });
  return { scene, effect, canvases, atlasImage, released };
}

test('drop lands on the cue, bounces twice, holds and exits with seven metres of corner clearance', () => {
  const r = row(), settled = marqueePose(r, 3);
  assert.ok(marqueePose(r, 2).y > settled.y + 25);
  assert.ok(marqueePose(r, 3.18).y > settled.y);
  assert.ok(marqueePose(r, 3.54).y > marqueePose(r, 3.36).y);
  assert.ok(marqueePose(r, 10).y > marqueePose(r, 8).y + 30);
  for (const gentle of [false, true]) for (const side of [-1, 0, 1]) for (const exit of ['snap', 'winch']) {
    for (let t = 0; t < 12; t += .017) {
      const p = marqueePose(row({ params: { exit } }), t, gentle, side);
      assert.ok(p.bottom >= 7.2 - 1e-9);
      assert.ok(Object.values(p).every(Number.isFinite));
      if (gentle) assert.equal(p.whip, 0);
    }
  }
});

test('Gentle removes both bounce and swing while keeping a smooth drop and winch', () => {
  const r = row();
  for (const t of [3, 3.18, 3.36, 3.54, 4.1]) {
    const p = marqueePose(r, t, true);
    assert.equal(p.y, marqueePose(r, 3, true).y); assert.equal(p.roll, 0); assert.equal(p.yaw, 0);
  }
});

test('decoder copies carry credit, pause freezes uploads, and revocation clears before the next frame budget', () => {
  const c = canvas(), image = { width: 640, height: 360 }, fallback = { width: 768, height: 512 };
  const surface = createMarqueeSurface(c, { image: fallback }, 4);
  assert.ok(c.calls.includes(fallback));
  assert.equal(surface.paint(image, 1), true); assert.equal(surface.privatePixels, true);
  assert.ok(c.calls.includes('via Scrolller'));
  const before = c.calls.length;
  surface.paint(image, 3, { paused: true }); assert.equal(c.calls.length, before);
  surface.paint(null, 3, { paused: true, allowed: false }); assert.equal(surface.privatePixels, false);
  assert.equal(c.calls.filter(x => x === image).length, 1); assert.ok(c.calls.slice(before).includes(fallback));
  surface.paint(image, 4); surface.paint({ width: 120, height: 100 }, 5, { allowed: false });
  assert.equal(surface.privatePixels, false, 'a replaced decoder cannot leave revoked pixels');
  surface.paint(image, 8); assert.equal(surface.paint(image, 1), true, 'a song loop rearms the copy clock');
  surface.dispose(); assert.equal(c.width, 1); assert.equal(surface.paint(image, 8), false);
});

test('triptych reuses prebuilt meshes, anchors stay outside road, pose freezes and all resources retire', () => {
  let image = { width: 640, height: 360 };
  const { effect, scene, canvases, released } = setup({ source: () => image, photosafe: { request: () => true } });
  const meshes = []; effect.object.traverse(o => { if (o.isMesh) meshes.push(o); });
  const cues = [row(), row({ id: 'sides', params: { count: 2, variant: 'ribbons', exit: 'winch' } })];
  effect.update(cues, .1, { songS: 3.1 }); assert.equal(effect.stats.active, 3);
  assert.ok(effect.stats.privateSurfaces <= 2, 'at most two decoder copies per update');
  for (const root of effect.object.children.filter(v => v.visible)) {
    const [hanger, screen] = root.children;
    for (const anchor of hanger.children.slice(2)) assert.ok(Math.abs(anchor.position.x) - .6 > 16 + 2);
    screen.updateMatrix();
    for (const mesh of screen.children) {
      mesh.geometry.computeBoundingBox();
      const box = mesh.geometry.boundingBox;
      for (const x of [box.min.x, box.max.x]) for (const y of [box.min.y, box.max.y]) for (const z of [box.min.z, box.max.z]) {
        mesh.updateMatrix(); const v = new Vector3(x, y, z).applyMatrix4(mesh.matrix).applyMatrix4(screen.matrix);
        assert.ok(v.y >= 7, `screen corner below clearance: ${v.y}`);
      }
    }
  }
  const matrices = effect.object.children.map(v => v.children[1].matrix.toArray());
  effect.update(cues, 10, { paused: true, songS: 9 });
  assert.equal(effect.stats.clock, 3.1);
  assert.deepEqual(effect.object.children.map(v => v.children[1].matrix.toArray()), matrices);
  image = null; effect.update(cues, 0, { paused: true }); assert.equal(effect.stats.privateSurfaces, 0);
  const after = []; effect.object.traverse(o => { if (o.isMesh) after.push(o); }); assert.deepEqual(after, meshes);
  effect.clear(); assert.equal(effect.stats.active, 0);
  effect.dispose(); effect.dispose(); assert.equal(scene.children.length, 0); assert.deepEqual(released, ['atlas']);
  assert.ok(canvases.every(c => c.width === 1));
});

test('safety refusal softens the complete screen and its rise stays at least 300 milliseconds', () => {
  const { effect } = setup({ photosafe: { request: () => false } });
  const r = row({ bornS: 2 });
  effect.update([r], .1, { songS: 2.15 });
  const surface = effect.object.children[0].children[1].children[2].material;
  assert.ok(surface.opacity > 0 && surface.opacity < .55);
  effect.update([r], .15, { songS: 2.3 }); assert.ok(Math.abs(surface.opacity - .55) < 1e-9);
  effect.dispose();
});

test('real course frames keep a proper rotation and show the picture toward the approaching player', () => {
  for (const id of ['threshold', 'mainframe', 'boudoir', 'goonworks']) {
    const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url))));
    const { effect } = setup();
    for (const ratio of [0, .2, .4, .7, .9]) {
      const f = track.frameAt(track.lengthM * ratio), r = row({ frame: f });
      effect.update([r], .1, { songS: 4, gentle: true }); effect.object.updateMatrixWorld(true);
      const root = effect.object.children[0], screen = root.children[1], face = screen.children[2];
      assert.ok(Math.abs(root.quaternion.length() - 1) < 1e-9);
      assert.ok(Math.abs(root.matrixWorld.determinant() - 1) < 1e-9);
      const backward = new Vector3(...f.t).negate(), normal = new Vector3(0, 0, 1).transformDirection(face.matrixWorld);
      assert.ok(normal.dot(backward) > .9999, id + ': face looks toward incoming kart');
      const front = new Vector3().setFromMatrixPosition(face.matrixWorld), back = new Vector3().setFromMatrixPosition(screen.matrixWorld);
      assert.ok(front.sub(back).dot(backward) > .5, id + ': picture sits in front of the bezel');
    }
    effect.dispose();
  }
});

test('unavailable Loom still builds warmed screens with a procedural fallback', () => {
  const { effect, canvases } = setup({ acquireAtlas() { throw new Error('unavailable'); } });
  assert.equal(effect.stats.capacity, 6);
  assert.ok(canvases.every(c => c.calls.filter(v => v === 'clear').length > 8));
  effect.update([row()], .1, { songS: 4 }); assert.equal(effect.stats.active, 1);
  assert.equal(effect.stats.privateSurfaces, 0); effect.dispose();
});

test('the transport retains authority over a longer Gentle exit envelope', () => {
  const { effect } = setup();
  effect.update([row({ alpha: .75 })], .1, { songS: 11, gentle: true });
  const face = effect.object.children[0].children[1].children[2];
  assert.equal(face.material.opacity, .75, 'rendering must not shorten the transport fade');
  effect.dispose();
});
