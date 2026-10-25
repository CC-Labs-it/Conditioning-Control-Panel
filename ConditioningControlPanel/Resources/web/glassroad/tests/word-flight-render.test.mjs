import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { createAirWordRun, bindAirWords } from '../sim/airWords.js';
import { step } from '../sim/kart.js';
import { S } from '../sim/state.js';
import { DT, place, makeFeel, recorder, acts } from './sim-fake.mjs';

const hooks = registerHooks({ resolve(s, context, next) {
  const files = { 'three/tsl': 'three.tsl.min.js', 'three/webgpu': 'three.webgpu.min.js' };
  return files[s] ? { url: new URL('../vendor/three/build/' + files[s], import.meta.url).href, shortCircuit: true } : next(s, context);
} });
const { createAirWords } = await import('../render/airWords.js');
const { Scene, Raycaster, Vector3 } = await import('three/webgpu');
hooks.deregister();

// A wide open O makes a center-of-box catch fail. The production contour/extrusion pipeline is used.
const doc = { createElement() {
  const context = { measureText: () => ({ width: 150 }), getImageData(x, y, w, h) {
    const data = new Uint8ClampedArray(w * h * 4);
    for (let yy = 20; yy < h - 20; yy++) for (let xx = 20; xx < w - 20; xx++) {
      if (xx >= 36 && xx < w - 36 && yy >= 36 && yy < h - 36) continue;
      data[(yy * w + xx) * 4 + 3] = 255;
    }
    return { data };
  } };
  for (const key of ['clearRect', 'save', 'restore', 'translate', 'scale', 'strokeText', 'fillText']) context[key] = () => {};
  return { getContext: () => context };
} };

for (const id of ['threshold', 'keep', 'boudoir', 'mainframe', 'goonworks']) test(id + ': scripted catches touch rendered letter strokes at 30 fps', () => {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url), 'utf8')));
  const scene = new Scene(), view = createAirWords(scene, track, { doc });
  try {
    for (const zone of track.aerials) {
      const st = place(track, { s: zone.rampS0 + .5, speed: 26 }), ev = recorder(), feel = makeFeel();
      const run = createAirWordRun({ words: [{ w: 'O', startS: 0 }] }, track); bindAirWords(st, run);
      let contacts = 0;
      for (let i = 0; i < 1000 && st[S.s] < zone.gapS1 + 12; i++) {
        run.update(i * DT, st); step(st, acts({ steer: i % 120 < 60 ? -1 : 1 }), track, feel, DT, ev);
        if (i % 2 === 0) view.update(run, DT * 2);
        if (ev.all('air-word-land').length <= contacts) continue;
        const contact = ev.all('air-word-land').at(-1).payload;
        const slot = Number(contact.id.split('-').at(-1)), letters = view.object.children[slot].children[0];
        scene.updateMatrixWorld(true);
        const ray = new Raycaster(new Vector3(st[S.posX], st[S.posY] + .15, st[S.posZ]), new Vector3(0, -1, 0), 0, .3);
        assert.ok(ray.intersectObject(letters).length > 0, zone.id + ' caught the actual O stroke');
        contacts++;
      }
      assert.ok(contacts >= 3); assert.equal(st[S.respawnPhase], 0);
      run.reset(); view.clear();
    }
  } finally { view.dispose(); }
});
