import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { evaluateColour } from './floor-colour-eval.mjs';

const hooks = registerHooks({ resolve(s, c, n) {
  const files = { three: 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[s] ? { url: new URL('../vendor/three/build/' + files[s], import.meta.url).href, shortCircuit: true } : n(s, c);
} });
const { Vector2 } = await import('three');
const { uniform, vec3, float } = await import('three/tsl');
const { createAudioReactiveUniforms, disposeAudioReactiveUniforms } = await import('../render/audioReactive.js');
const { mainframeTileLight } = await import('../render/materials/mainframeMusicFloor.js');
const { keepMusicFloor } = await import('../render/materials/keepMusicFloor.js');
hooks.deregister();

function setup(kind, t) {
  const p = uniform(new Vector2()), u = createAudioReactiveUniforms();
  t.after(() => disposeAudioReactiveUniforms(u));
  for (const key of ['bass', 'mid', 'treble', 'energy', 'drive', ...Array.from({ length: 8 }, (_, i) => 'note' + i)]) u[key].value = .35;
  const music = { tileOn: uniform(1), tileBeat: uniform(2), tileBar: uniform(0), waveAmp: uniform(0) };
  const base = [.08, .1, .12], root = kind === 'mainframe'
    ? mainframeTileLight(p, u, music, float(9), vec3(...base))
    : keepMusicFloor({ p, base: vec3(...base), seam: float(0), reactive: u, music });
  const colour = (x, y) => { p.value.set(x, y); return evaluateColour(root); };
  const capture = () => [-13, -9, -4, 1, 4, 8, 11, 15].flatMap(x => [1, 5, 9, 13, 17, 23, 29, 35].flatMap(y => colour(x, y)));
  return { p, u, music, base, root, colour, capture };
}
const changed = (a, b) => a.some((v, i) => Math.abs(v - b[i]) > .006);
function audioRow(u, wave, spectrum) {
  const data = u.vizTexture.image.data;
  for (let i = 0; i < 64; i++) { data[i * 4] = wave(i); data[i * 4 + 1] = spectrum(i); }
}

test('Mainframe traces the actual waveform at fixed beat, level and section', t => {
  const { u, capture } = setup('mainframe', t);
  audioRow(u, () => 128, () => 70); const flat = capture();
  audioRow(u, i => Math.round(128 + Math.sin(i * Math.PI / 8) * 110), () => 70);
  assert.ok(changed(flat, capture()), 'PCM content itself bends the phosphor trace');
});

for (const kind of ['mainframe', 'keep']) test(kind + ' distinguishes frequency content, hits and song sections at equal loudness', t => {
  const { u, music, capture } = setup(kind, t);
  music.tileBar.value = 8;
  audioRow(u, () => 128, i => i < 32 ? 235 : 10); const low = capture();
  audioRow(u, () => 128, i => i < 32 ? 10 : 235);
  assert.ok(changed(low, capture()), 'equal energy with a different FFT spectrum changes the floor');
  const quiet = capture(); u.kick.value = .9; u.snare.value = .7; u.hitAge.value = .1; u.hitStrength.value = .9;
  assert.ok(changed(quiet, capture()), 'a real onset has a distinct visual consequence between grid beats');
  const first = capture(); u.section.value += 1;
  assert.ok(changed(first, capture()), 'the playing section changes the composition');
});

for (const kind of ['mainframe', 'keep']) test(kind + ' shows a bounded spectral trail without allocating another texture', t => {
  const { u, music, root, capture } = setup(kind, t);
  music.tileBar.value=16;audioRow(u,()=>128,()=>100);
  const before = capture(), data = u.vizTexture.image.data;
  for (let row = 1; row < 18; row++) for (let i = 0; i < 64; i++) data[(row * 64 + i) * 4] = (row + i) % 2 ? 235 : 50;
  assert.ok(changed(before, capture()), 'past measured frequencies visibly remain behind the current sound');
  const nodes = new Set(), pending = [root];
  while (pending.length) { const n = pending.pop(); if (nodes.has(n)) continue; nodes.add(n); for (const child of n.getChildren()) pending.push(child); }
  const textures = [...nodes].filter(n => n.isTextureNode);
  assert.ok(textures.length > 0 && textures.every(n => n.value === u.vizTexture), 'all samples share the world music atlas');
  assert.ok(nodes.size < 850, 'the classic visualization stays within the existing compact graph budget');
});

test('Keep responds with complete physical tile fills instead of lines through slabs', t => {
  const { colour, u } = setup('keep', t);
  audioRow(u, () => 128, i => i * 4);
  u.kick.value = .9; u.hitStrength.value = .8; u.hitAge.value = .2;
  const centre = colour(4, 3);
  for (const [x, y] of [[3.2, 1], [4.8, 1], [3.2, 5], [4.8, 5]]) assert.deepEqual(colour(x, y), centre);
});

for (const kind of ['mainframe', 'keep']) test(kind + ' suppresses hit motion in Gentle and restores exact silent finish', t => {
  const { u, music, base, capture } = setup(kind, t);
  u.gentle.value = 1; const calm = capture();
  u.kick.value = u.snare.value = u.hats.value = 1; u.hitAge.value = .1; u.hitStrength.value = 1;
  assert.deepEqual(capture(), calm, 'fast hit geometry and colour are removed in Gentle');
  u.energy.value = u.drive.value = 0;
  assert.deepEqual(capture(), Array.from({ length: 64 }, () => base).flat());
  u.energy.value = u.drive.value = .35; music.tileOn.value = 0;
  assert.deepEqual(capture(), Array.from({ length: 64 }, () => base).flat());
});
