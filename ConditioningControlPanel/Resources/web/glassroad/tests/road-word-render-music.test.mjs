import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { normalizeChart } from '../audio/chart.js';
import { fakeTrack } from './sim-fake.mjs';
import { S, createState } from '../sim/state.js';
import { createMusicFrame } from '../render/musicRoadMath.js';
import { evaluateColour } from './floor-colour-eval.mjs';
const hooks = registerHooks({ resolve(s, c, next) {
  const files = { three: 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[s] ? { url: new URL('../vendor/three/build/' + files[s], import.meta.url).href, shortCircuit: true } : next(s, c);
} });
const { Scene, Vector2 } = await import('three');
const { uniform } = await import('three/tsl');
const { createRoadWords, roadWordMusicUv, ROAD_WORD_ACCENTS, ROAD_PAINT } = await import('../render/roadWords.js');
hooks.deregister();

test('the actual whole-glyph UV graph is identity at rest and bounded inside its atlas padding', () => {
  const loc = uniform(new Vector2()), geo = uniform(new Vector2(11, 11.4));
  const u = { musicKick: uniform(0), musicSnare: uniform(0), vibration: uniform(0) };
  const root = roadWordMusicUv(loc, geo, u), sample = (x, y) => { loc.value.set(x, y); return evaluateColour(root); };
  for (const [x, y] of [[.2, .3], [.5, .5], [.8, .9]]) assert.deepEqual(sample(x, y), [x, y]);
  u.musicKick.value = u.musicSnare.value = 1;
  const width = sample(.75, .5)[0] - sample(.25, .5)[0];
  assert.ok(Math.abs(.5 / width - 1.04) < 1e-12, 'the complete face and outline pulse by at most four percent');
  for (const widthM of [2, 6, 11]) {
    geo.value.x = widthM;
    const padX = Math.min(.2, ROAD_PAINT.padPx * geo.value.y / widthM / ROAD_PAINT.stretch);
    for (const vibration of [-.08, .08, -1, 1]) {
      u.vibration.value = vibration;
      const centre = sample(.5, .5);
      assert.ok(Math.abs((centre[0] - .5) * widthM) <= .080000001);
      for (const [x, y] of [[padX, .12], [1 - padX, .88]])
        assert.ok(sample(x, y).every(v => v > 0 && v < 1), 'every outlined stroke retains atlas margin');
    }
  }
});

test('each course has a distinct accent ink while ordinary markings keep their themed style', () => {
  assert.equal(new Set(Object.values(ROAD_WORD_ACCENTS).map(c => c.ink.join(','))).size, 5);
  assert.ok(ROAD_WORD_ACCENTS.mainframe.ink[0] > ROAD_WORD_ACCENTS.mainframe.ink[1]);
  assert.ok(ROAD_WORD_ACCENTS.boudoir.ink[1] > ROAD_WORD_ACCENTS.boudoir.ink[0]);
  assert.ok(ROAD_WORD_ACCENTS.goonworks.ink[2] > ROAD_WORD_ACCENTS.goonworks.ink[0]);
});

test('renderer stores sparse word accents once and a pause freezes all paint attributes', t => {
  const before = globalThis.document;
  globalThis.document = { createElement() {
    const context = new Proxy({ measureText(text) { return { width: text.length * 48, actualBoundingBoxAscent: 70, actualBoundingBoxDescent: 12 }; } },
      { get(target, key) { return key in target ? target[key] : () => {}; } });
    return { width: 1, height: 1, getContext: () => context };
  } };
  t.after(() => { if (before === undefined) delete globalThis.document; else globalThis.document = before; });
  const track = fakeTrack({ w: 18 }); track.id = 'threshold';
  const scene = new Scene(), paint = createRoadWords(scene, { track }); t.after(() => paint.dispose());
  const song = normalizeChart({ bpm: 120, durationS: 20,
    sections: [{ id: 'groove', startS: 0, endS: 20, mood: 'groove' }],
    words: [{ w: 'amber', startS: 3, endS: 3.4, line: 0 }, { w: 'ordinary', startS: 7, endS: 7.4, line: 1 }],
    triggers: [{ kind: 'drop', startS: 3 }] });
  const kart = createState(); kart[S.velX] = 30;
  const mf = Object.assign(createMusicFrame(), { on: true, kick: 1, snare: .5 });
  for (let i = 0; i < 10 && !paint.stats().ready; i++) paint.update(song, 1.2, kart, mf);
  assert.ok(paint.stats().ready && paint.stats().live > 0);
  const a = paint.object.geometry.attributes;
  const strengths = () => {
    const vertices = a.position.count / ROAD_PAINT.slots, out = [];
    for (let slot = 0; slot < ROAD_PAINT.slots; slot++) {
      const at = slot * vertices * 4;
      if (a.aState.array[at] > 0) out.push(a.aLoc.array[at + 3]);
    }
    return out;
  };
  assert.ok(strengths().includes(1), 'the drop-selected marking owns a static accent');
  kart[S.s] = 125; paint.update(song, 5, kart, mf);
  assert.ok(strengths().includes(0), 'ordinary markings retain zero accent strength');
  const snapshot = Object.fromEntries(Object.entries(a).map(([k, v]) => [k, { values: Array.from(v.array), version: v.version }]));
  paint.update(song, 12, kart, Object.assign({}, mf, { kick: 0 }), { paused: true });
  for (const [key, value] of Object.entries(a)) {
    assert.equal(value.version, snapshot[key].version, key + ' is not uploaded during pause');
    assert.deepEqual(Array.from(value.array), snapshot[key].values, key + ' holds exactly');
  }
});
