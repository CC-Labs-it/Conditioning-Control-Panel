import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { readFileSync } from 'node:fs';
const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { Scene, PerspectiveCamera, Vector3, Matrix4, Vector4, NoBlending } = await import('three/webgpu');
const { panelTransform, createWebMediaScreens } = await import('../render/webMediaScreens.js');
const { programPalette } = await import('../render/media.js');
const slot = { C: [0, 0, -10], X: [1, 0, 0], Y: [0, 1, 0], Z: [0, 0, 1], w: 4, h: 2 };
const camera = new PerspectiveCamera(60, 2, .1, 100); camera.updateMatrixWorld();

test('display-only panel homography exactly matches all world corners under chase tilt', () => {
  const cam = camera.clone(); cam.position.set(2, 4, 7); cam.lookAt(0, 0, -10); cam.updateMatrixWorld();
  const matrix = panelTransform(slot, cam, 1600, 800);
  for (const [x, y] of [[0,0], [1,0], [0,1], [1,1], [.2,.7]]) {
    const actual = new Vector4(x,y,0,1).applyMatrix4(matrix);
    const world = new Vector3(slot.C[0] + (x - .5) * slot.w, (.5 - y) * slot.h, -10).project(cam);
    assert.ok(Math.abs(actual.x / actual.w - (world.x + 1) * 800) < 1e-9);
    assert.ok(Math.abs(actual.y / actual.w - (1 - world.y) * 400) < 1e-9);
  }
});

function fakeDocument() {
  const elements = [];
  return { elements, createElement(tag) {
    const el = { tag, style: {}, children: [], complete: tag === 'img', naturalWidth: tag === 'img' ? 640 : 0,
      append(...xs) { this.children.push(...xs); }, setAttribute() {}, removeAttribute(key) { delete this[key]; }, remove() { this.removed = true; } };
    elements.push(el); return el;
  } };
}

test('world windows have real depth, no GPU pictures, freeze on pause and close on source-off', () => {
  const scene = new Scene(), doc = fakeDocument(), canvas = { clientWidth: 1600, clientHeight: 800,
    style: {}, parentNode: { insertBefore() {} } };
  let on = true, visible = true;
  const source = { src: 'https://images.scrolller.com/a.jpg', complete: true, naturalWidth: 640 };
  const feed = { get ready() { return on ? 1 : 0; }, sourceFor: () => on ? source : null };
  const screens = createWebMediaScreens({ scene, slots: [slot], canvas, document: doc, isVisible: () => visible });
  const mesh = screens.object.children[0];
  assert.equal(mesh.material.depthTest, true); assert.equal(mesh.material.depthWrite, true);
  assert.equal(mesh.material.transparent, false); assert.equal(mesh.material.blending, NoBlending);
  assert.equal(mesh.material.map, null);
  screens.update(.1, { camera }); assert.equal(screens.stats().faces, 0);
  screens.setFeed(feed); screens.update(.1, { camera });
  assert.equal(screens.stats().faces, 1); assert.equal(screens.stats().turn, 0);
  screens.update(20, { camera, paused: true }); assert.equal(screens.stats().turn, 0);
  screens.update(7, { camera }); assert.equal(screens.stats().turn, 1);
  visible = false; screens.update(0, { camera }); assert.equal(screens.stats().faces, 0);
  visible = true; screens.update(0, { camera }); assert.equal(screens.stats().faces, 1);
  on = false; screens.update(0, { camera, paused: true });
  assert.equal(screens.stats().faces, 0);
  const m = new Matrix4(); mesh.getMatrixAt(0,m); assert.equal(m.elements[0], 0);
  assert.equal(doc.elements.filter(e => e.tag === 'img').every(e => !e.src), true);
  screens.dispose(); assert.equal(scene.children.length, 0); assert.equal(canvas.style.zIndex, undefined);
});

test('course palette preserves six visibly distinct Loom inks instead of one forced tint', () => {
  const palettes = Array.from({ length: 6 }, (_, i) => programPalette(['#bd006e','#fff9ff'], i));
  assert.equal(new Set(palettes.map(p => p[0])).size, 6);
  assert.equal(new Set(palettes.map(p => p[1])).size, 6);
  assert.equal(programPalette(null, 2), null);
});

test('post preserves premultiplied depth windows and suppresses them during full-screen effects', () => {
  const src = readFileSync(new URL('../render/post.js', import.meta.url), 'utf8');
  assert.match(src, /vec4\(out\.mul\(alpha\), alpha\)/);
  for (const effect of ['whiteFade', 'submerged', 'fog?.k', 'prevK']) assert.ok(src.includes(effect));
  const display = readFileSync(new URL('../render/webMediaScreens.js', import.meta.url), 'utf8');
  assert.doesNotMatch(display, /new (CanvasTexture|Texture)|drawImage|getImageData|toDataURL|\bfetch\(/);
});

test.after(() => hooks.deregister());
