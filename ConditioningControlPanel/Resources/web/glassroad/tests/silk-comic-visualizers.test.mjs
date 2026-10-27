import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { evaluateColour } from './floor-colour-eval.mjs';

const hooks = registerHooks({ resolve(s, c, next) {
  const names = { three: 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return names[s] ? { url: new URL('../vendor/three/build/' + names[s], import.meta.url).href, shortCircuit: true } : next(s, c);
} });
const { Vector2 } = await import('three');
const { uniform, vec3, float } = await import('three/tsl');
const { createAudioReactiveUniforms, disposeAudioReactiveUniforms } = await import('../render/audioReactive.js');
const { boudoirMusicFloor } = await import('../render/materials/boudoirMusicFloor.js');
const { goonworksMusicFloor } = await import('../render/materials/goonworksMusicFloor.js');
hooks.deregister();

const positions = Array.from({ length: 14 }, (_, x) => x + .8)
  .flatMap(x => Array.from({ length: 18 }, (_, s) => [x, s * 2.3 + .7]));
const changedArea = (a, b) => a.filter((c, i) => c.some((v, k) => Math.abs(v - b[i][k]) > .012)).length / a.length;

function setup(kind, t) {
  const p = uniform(new Vector2()), u = createAudioReactiveUniforms();
  t.after(() => disposeAudioReactiveUniforms(u));
  for (const key of ['bass', 'mid', 'treble', 'energy', 'drive']) u[key].value = .32;
  for (let i = 0; i < 8; i++) u['note' + i].value = .18 + i * .04;
  const music = { tileBeat: uniform(0), tileBar: uniform(0), tileOn: uniform(1), tileGentle: uniform(0), big: uniform(.6), calm: uniform(0) };
  const base = vec3(.14, .12, .16);
  const root = kind === 'boudoir'
    ? boudoirMusicFloor({ p, base, trueEdge: float(8), distance: float(24), wet: float(0), reactive: u, music })
    : goonworksMusicFloor({ p, base, top: float(1), edge: float(18).sub(p.x.abs()), half: float(18),
      pink: vec3(.8, .04, .3), accent: vec3(.08, .6, .5), reactive: u, music });
  const data = u.vizTexture.image.data;
  for (let i = 0; i < 64; i++) { data[i * 4] = 128; data[i * 4 + 1] = 70; }
  const capture = () => positions.map(([x, s]) => { p.value.set(x, s); return evaluateColour(root); });
  return { u, music, data, root, capture };
}

for (const kind of ['boudoir', 'goonworks']) {
  test(kind + ' changes its floor silhouette with actual PCM at identical energy and beat', t => {
    const { music, data, capture } = setup(kind, t);
    if (kind === 'goonworks') music.tileBar.value = 16;
    const writeWave = turns => { for (let i = 0; i < 64; i++) data[i * 4] = Math.round(128 + Math.sin(i / 63 * Math.PI * 2 * turns) * 95); };
    writeWave(1); const before = capture(); writeWave(3);
    const area = changedArea(before, capture());
    assert.ok(area > .12, 'different recorded waves must reshape a broad floor area, measured ' + area);
  });

  test(kind + ' reacts to hits and real song sections with the transport held fixed', t => {
    const { u, capture } = setup(kind, t), before = capture();
    u.kick.value = .95; u.snare.value = .8; u.hats.value = .7; u.hitAge.value = .25; u.hitStrength.value = .9;
    assert.ok(changedArea(before, capture()) > .2, 'real percussive events must produce an unmistakable response');
    const hit = capture(); u.section.value = 1;
    assert.ok(changedArea(hit, capture()) > .12, 'real sections choose a visibly different composition');
  });

  test(kind + ' reads independent FFT and spectral history at fixed loudness', t => {
    const { music, data, capture } = setup(kind, t);
    const before = capture();
    for (let i = 0; i < 64; i++) data[i * 4 + 1] = i < 32 ? 210 : 20;
    assert.ok(changedArea(before, capture()) > .12, 'live frequency content reshapes the floor independently of overall energy');
    music.tileBar.value = 16;
    const noHistory = capture();
    for (let row = 1; row < 18; row++) for (let i = 0; i < 64; i++)
      data[(row * 64 + i) * 4] = (i + row * 3) % 16 < 8 ? 240 : 15;
    assert.ok(changedArea(noHistory, capture()) > .12, 'recorded spectral trails form visible silk/comic detail');
  });

  test(kind + ' keeps fast PCM and event patterns still in Gentle and returns exactly to silence', t => {
    const { u, music, data, capture } = setup(kind, t);
    u.gentle.value = music.tileGentle.value = 1;
    const before = capture();
    for (let i = 0; i < data.length; i++) data[i] = 220;
    u.kick.value = u.snare.value = u.hats.value = u.hitStrength.value = 1; u.hitAge.value = .1;
    assert.deepEqual(capture(), before, 'fast wave, spectrum, history and event changes are suppressed');
    u.energy.value = u.drive.value = 0;
    for (const colour of capture()) assert.deepEqual(colour, [.14, .12, .16]);
  });
}
