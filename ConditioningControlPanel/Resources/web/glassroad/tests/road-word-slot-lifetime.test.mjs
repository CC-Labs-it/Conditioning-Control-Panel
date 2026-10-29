import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { normalizeChart } from '../audio/chart.js';
import { fakeTrack } from './sim-fake.mjs';
import { S, createState } from '../sim/state.js';
import { createMusicFrame } from '../render/musicRoadMath.js';

const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { three: 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { Scene } = await import('three');
const { createRoadWords, ROAD_PAINT } = await import('../render/roadWords.js');
hooks.deregister();

function fixture(t) {
  const before = globalThis.document;
  globalThis.document = { createElement() {
    const context = new Proxy({ measureText: text => ({ width: text.length * 48, actualBoundingBoxAscent: 70, actualBoundingBoxDescent: 12 }) },
      { get: (target, key) => key in target ? target[key] : () => {} });
    return { width: 1, height: 1, getContext: () => context };
  } };
  t.after(() => { if (before === undefined) delete globalThis.document; else globalThis.document = before; });
  const track = fakeTrack({ w: 18 }); track.id = 'threshold';
  const paint = createRoadWords(new Scene(), { track }); t.after(() => paint.dispose());
  const song = normalizeChart({ bpm: 120, durationS: 25,
    words: [{ w: 'amber', startS: 3, endS: 3.4, line: 0 }, { w: 'ordinary', startS: 12, endS: 12.4, line: 1 }] });
  const kart = createState(); kart[S.velX] = 30;
  const mf = createMusicFrame();
  for (let i = 0; i < 10 && !paint.stats().ready; i++) paint.update(song, 1.2, kart, mf);
  paint.update(song, 1.5, kart, mf);
  const a = paint.object.geometry.attributes, vertices = a.position.count / ROAD_PAINT.slots;
  let at = -1;
  for (let slot = 0; slot < ROAD_PAINT.slots; slot++) if (a.aState.array[slot * vertices * 4] > 0) { at = slot * vertices * 4; break; }
  assert.ok(at >= 0, 'a real painted marking occupies the pool');
  // Reproduce the GPU state of an interrupted pop. Source alpha and pickup
  // decoration are independent, so retiring only source alpha is insufficient.
  for (let vertex = 0; vertex < vertices; vertex++) a.aIdentity.array[at + vertex * 4 + 2] = .4;
  return { paint, song, kart, mf, a, at, vertices };
}

test('clear retires every vertex of an interrupted pickup and uploads the retirement', t => {
  const { paint, a, at, vertices } = fixture(t), before = a.aIdentity.version;
  paint.clear();
  assert.equal(paint.object.visible, false);
  assert.ok(a.aIdentity.version > before, 'GPU receives the stopped pickup clock');
  for (let vertex = 0; vertex < vertices; vertex++) {
    assert.equal(a.aState.array[at + vertex * 4], 0);
    assert.equal(a.aIdentity.array[at + vertex * 4 + 2], -1);
  }
});

test('ordinary slot expiry removes pickup decoration while another marking remains visible', t => {
  const { paint, song, kart, mf, a, at } = fixture(t);
  paint.update(song, 9, kart, mf);
  a.aIdentity.array[at + 2] = .4;
  paint.update(song, 11, kart, mf);
  assert.equal(paint.object.visible, true, 'another marking still uses the pooled draw');
  assert.equal(a.aState.array[at], 0, 'the original slot retired');
  assert.equal(a.aIdentity.array[at + 2], -1, 'its old halo cannot remain in the shared draw');
});
