import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { evaluateColour } from './floor-colour-eval.mjs';

const hooks = registerHooks({ resolve(s, c, n) {
  const files = { three: 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[s] ? { url: new URL('../vendor/three/build/' + files[s], import.meta.url).href, shortCircuit: true } : n(s, c);
} });
const { Vector2 } = await import('three');
const { uniform, vec3 } = await import('three/tsl');
const { createAudioReactiveUniforms, disposeAudioReactiveUniforms } = await import('../render/audioReactive.js');
const { keepMusicFloor } = await import('../render/materials/keepMusicFloor.js');
hooks.deregister();

const slabs = [];
for (let repeat = -2; repeat <= 1; repeat++) for (const x of [1, 4, 8, 11]) {
  if (Math.abs(repeat * 12 + x) > 18) continue;
  for (let row = 0; row < 12; row++) slabs.push([repeat * 12 + x, row * 8 + (x < 6 ? 4 : 0)]);
}
const luminance = c => c[0] * .2126 + c[1] * .7152 + c[2] * .0722;
const difference = (a, b) => Math.max(...a.map((v, i) => Math.abs(v - b[i])));

function fixture(t) {
  const p = uniform(new Vector2()), u = createAudioReactiveUniforms(), seam = uniform(0);
  t.after(() => disposeAudioReactiveUniforms(u));
  for (const event of ['kick', 'snare', 'hats']) for (const [suffix, value] of [['Age', 9], ['Strength', 0], ['Count', 0]])
    if (!u[event + suffix]) u[event + suffix] = uniform(value);
  u.energy.value = .35; u.drive.value = .5;
  u.bass.value = .4; u.mid.value = .4; u.treble.value = .3;
  const music = { tileOn: uniform(1), tileBar: uniform(0), tileBeat: uniform(0), waveAmp: uniform(0) };
  const base = [.13, .07, .14];
  const root = keepMusicFloor({ p, base: vec3(...base), seam, reactive: u, music });
  const colour = (x, y) => { p.value.set(x, y); return evaluateColour(root); };
  const capture = () => slabs.map(([x, y]) => colour(x, y));
  function strike(event, age = .1) {
    for (const kind of ['kick', 'snare', 'hats']) {
      u[kind].value = kind === event ? 1 : 0;
      u[kind + 'Strength'].value = kind === event ? 1 : 0;
      u[kind + 'Age'].value = kind === event ? age : 9;
      u[kind + 'Count'].value = 1;
    }
  }
  function tuneSpectrum() {
    for (let i = 0; i < 8; i++) u['note' + i].value = [.15, .8, .25, .6, .45, .9, .2, .55][i];
    const data = u.vizTexture.image.data;
    for (let row = 0; row < 18; row++) for (let i = 0; i < 64; i++)
      data[(row * 64 + i) * 4 + (row === 0 ? 1 : 0)] = Math.round((.1 + ((Math.floor(i / 8) + row) % 8) / 9) * 255);
  }
  return { u, music, seam, base, root, colour, capture, strike, tuneSpectrum };
}

test('Keep gives kick, snare and hats different masks at identical song time and loudness', t => {
  const { u, capture, strike, root } = fixture(t), rest = capture(), masks = [];
  for (const [event, age] of [['kick', .16], ['snare', .04], ['hats', .02]]) {
    strike(event, age); const lit = capture();
    const mask = lit.map((c, i) => luminance(c) - luminance(rest[i]) > .06);
    const coverage = mask.filter(Boolean).length / mask.length;
    assert.ok(coverage > .035 && coverage < .55, event + ' lights a bounded subset with dark rest space: ' + coverage);
    assert.ok(Math.max(...lit.map(luminance)) > Math.min(...lit.map(luminance)) * 4, event + ' has at least 4:1 dark/bright contrast');
    masks.push(mask);
  }
  for (let i = 0; i < masks.length; i++) for (let j = i + 1; j < masks.length; j++) {
    const changed = masks[i].filter((v, k) => v !== masks[j][k]).length / masks[i].length;
    assert.ok(changed > .1, 'different instruments do not reuse the same pulse mask');
  }
  const nodes = new Set(), todo = [root];
  while (todo.length) { const n = todo.pop(); if (nodes.has(n)) continue; nodes.add(n); for (const child of n.getChildren()) todo.push(child); }
  for (const key of ['kickAge', 'snareAge', 'hatsAge']) assert.ok(nodes.has(u[key]), key + ' has an independent visual clock');
  assert.ok(!nodes.has(u.hitAge), 'the instruments never collapse to the shared hit age');
  assert.ok(nodes.size < 1450, 'three tile layouts and four plucked strings retain a bounded graph: ' + nodes.size);
});

test('Keep uses three different spectral compositions with dark negative space', t => {
  const { capture, music, tuneSpectrum, strike } = fixture(t);
  tuneSpectrum(); strike(null);
  const fields = [0, 8, 16].map(bar => { music.tileBar.value = bar; return capture(); });
  for (const field of fields) {
    const light = field.map(luminance);
    assert.ok(Math.max(...light) - Math.min(...light) > .09, 'the composition has real brightness structure');
    assert.ok(Math.max(...light) > Math.min(...light) * 4, 'each composition has at least 4:1 bright versus quiet tiles');
  }
  for (let i = 0; i < fields.length; i++) for (let j = i + 1; j < fields.length; j++) {
    const changed = fields[i].filter((c, k) => difference(c, fields[j][k]) > .025).length / slabs.length;
    assert.ok(changed > .2, 'tile chords, zigzag relays and split beats have different spatial masks: ' + changed);
  }
});

test('Keep quiet recording levels still light sparse slabs in all three layouts', t => {
  const { u, music, capture, strike } = fixture(t);
  // Native Keep evidence: drive .22, energy .32, small waveform and quiet measured bands.
  // The atlas already stores compressed FFT/history values; do not substitute full-strength notes.
  u.drive.value = .22; u.energy.value = .32;
  u.bass.value = u.mid.value = .25; u.treble.value = .1;
  for (let i = 0; i < 8; i++) u['note' + i].value = .15;
  const data = u.vizTexture.image.data;
  for (let row = 0; row < 18; row++) for (let i = 0; i < 64; i++)
    data[(row * 64 + i) * 4 + (row === 0 ? 1 : 0)] = 57;
  strike(null);
  const masks = [];
  for (const bar of [0, 8, 16]) {
    music.tileBar.value = bar;
    const levels = capture().map(luminance), dark = Math.min(...levels), bright = Math.max(...levels);
    const mask = levels.map(v => v > dark * 2.2), coverage = mask.filter(Boolean).length / mask.length;
    assert.ok(bright > .14, 'selected slabs remain clearly luminous at actual low levels: ' + bright);
    assert.ok(bright > dark * 4, 'the quiet recording retains at least 4:1 lit/rest contrast');
    assert.ok(coverage > .1 && coverage < .55, 'a distinct sparse subset lights without a floor wash: ' + coverage);
    masks.push(mask);
  }
  for (let i = 0; i < 3; i++) for (let j = i + 1; j < 3; j++)
    assert.ok(masks[i].filter((v, k) => v !== masks[j][k]).length > slabs.length * .15,
      'quiet spectra still distinguish tile chords, zigzag relays and split beats: ' + i + '/' + j + ' changed ' + masks[i].filter((v, k) => v !== masks[j][k]).length + '/' + slabs.length);
});

test('Keep ages bass fronts independently and high glints expire promptly', t => {
  const { u, capture, strike } = fixture(t);
  strike('kick', .08); const first = capture(); u.kickAge.value = .28;
  assert.ok(capture().some((c, i) => difference(c, first[i]) > .08), 'the kick advances through tile rows');
  strike('hats', .02); const high = capture(); u.hats.value = 0; u.hatsAge.value = .7;
  assert.ok(capture().some((c, i) => difference(c, high[i]) > .1), 'a high hit is a brief glint rather than a sustained wash');
});

test('Keep preserves whole slabs, quiet Gentle, dark joints, silence and Motion Off', t => {
  const { u, music, seam, base, colour, capture, strike, tuneSpectrum } = fixture(t);
  tuneSpectrum(); strike('kick', .12);
  const centre = colour(4, 3);
  for (const [x, y] of [[3.2, 1], [4.8, 1], [3.2, 5], [4.8, 5]]) assert.deepEqual(colour(x, y), centre);
  u.gentle.value = 1; const calm = capture(); strike('snare', .02);
  assert.deepEqual(capture(), calm, 'Gentle ignores fast instrument attacks');
  u.gentle.value = 0; seam.value = 1;
  assert.ok(capture().every(c => difference(c, base) < .02), 'mortar retains the authored dark finish');
  seam.value = 0; u.energy.value = u.drive.value = 0;
  assert.ok(capture().every(c => difference(c, base) === 0));
  u.energy.value = u.drive.value = .5; u.motion.value = 0;
  assert.ok(capture().every(c => difference(c, base) === 0));
  u.motion.value = 1; music.tileOn.value = 0;
  assert.ok(capture().every(c => difference(c, base) === 0));
});
