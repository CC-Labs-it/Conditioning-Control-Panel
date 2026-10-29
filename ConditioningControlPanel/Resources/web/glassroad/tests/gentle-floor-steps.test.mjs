import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { createMusicVisualData, stepMusicVisualData, GENTLE_SECTION_S, GENTLE_RISE_S } from '../render/musicVisualData.js';
import { normalizeChart } from '../audio/chart.js';
import { evaluateColour } from './floor-colour-eval.mjs';

// Gentle visuals: a chart-section change or a bar boundary must never swap the floor in one frame.
const song = normalizeChart({ bpm: 120, sections: [{ startS: 0, endS: 4 }, { startS: 4, endS: 30 }] });
const signal = songS => ({ active: true, songS, rawEnergy: .4, rawBass: .2, rawMid: .2, rawTreble: .2,
  waveform: new Float32Array(64), spectrum: new Float32Array(64) });
function run(v, from, to, opts) { for (let t = from; t <= to + 1e-9; t += 1 / 60) stepMusicVisualData(v, signal(t), 1 / 60, { song, ...opts }); }

test('Full snaps to a new section; Gentle walks there slowly', () => {
  const full = createMusicVisualData(); run(full, 3.9, 4.05, {});
  assert.equal(full.section, 1, 'Full keeps its punchy section change');
  const soft = createMusicVisualData(); run(soft, 3.9, 3.99, { gentle: true });
  assert.equal(soft.section, 0);
  stepMusicVisualData(soft, signal(4.01), 1 / 60, { song, gentle: true });
  assert.ok(soft.section > 0 && soft.section <= 1 / 60 / GENTLE_SECTION_S + 1e-9, 'one frame moves the section by a sliver only');
  let last = soft.section;
  for (let t = 4.02; t < 4 + GENTLE_SECTION_S + 1; t += 1 / 60) {
    stepMusicVisualData(soft, signal(t), 1 / 60, { song, gentle: true });
    assert.ok(soft.section - last <= 1 / 60 / GENTLE_SECTION_S + 1e-9); last = soft.section;
  }
  assert.equal(soft.section, 1, 'the walk lands on the new section');
});

test('toggling Gentle mid-walk never jumps the section', () => {
  const v = createMusicVisualData(); run(v, 3.9, 6, { gentle: true });
  const mid = v.section; assert.ok(mid > 0 && mid < 1);
  stepMusicVisualData(v, signal(6.02), 1 / 60, { song });
  assert.ok(Math.abs(v.section - mid) <= 1 / 60 / GENTLE_SECTION_S + 1e-9, 'Full keeps walking until it catches up');
  run(v, 6.04, 6.04 + GENTLE_SECTION_S, {});
  assert.equal(v.section, 1);
  const on = v.section; stepMusicVisualData(v, signal(14.5), 1 / 60, { song, gentle: true });
  assert.equal(v.section, on, 'switching Gentle on holds the current section');
});

test('the Gentle bass layer fades in over the rise floor', () => {
  const v = createMusicVisualData(); run(v, 1, 1.1, {});
  assert.equal(v.gentleMix, 0);
  stepMusicVisualData(v, signal(1.12), 1 / 60, { song, gentle: true });
  assert.ok(v.gentleMix > 0 && v.gentleMix < .1);
  assert.ok(GENTLE_RISE_S >= .3);
  run(v, 1.13, 1.13 + GENTLE_RISE_S, { gentle: true });
  assert.equal(v.gentleMix, 1);
});

test('Gentle bass bank never steps on a bare floor(tileBar) while lit', () => {
  const src = readFileSync(new URL('../render/materials/tileMusicFloor.js', import.meta.url), 'utf8');
  assert.ok(/bassMask\.mul\(u\.bass\)\.mul\(gentleBass\)/.test(src), 'the Gentle bass layer goes through the bar gate');
  assert.ok(/barGate = smoothstep\(0, \.25, barAt\)\.mul\(one\(smoothstep\(\.75, 1, barAt\)\)\)/.test(src), 'a quarter bar fade on each side of the step');
});

const hooks = registerHooks({ resolve(s, c, next) {
  const names = { three: 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return names[s] ? { url: new URL('../vendor/three/build/' + names[s], import.meta.url).href, shortCircuit: true } : next(s, c);
} });
const { Vector2 } = await import('three');
const { uniform, float } = await import('three/tsl');
const { createAudioReactiveUniforms, disposeAudioReactiveUniforms } = await import('../render/audioReactive.js');
const { tileInstrument } = await import('../render/materials/tileMusicFloor.js');
hooks.deregister();

for (let kind = 0; kind < 4; kind++) test('Gentle tile floor ' + kind + ' changes smoothly across bar and section steps', t => {
  const p = uniform(new Vector2()), column = uniform(0), row = uniform(0), u = createAudioReactiveUniforms();
  t.after(() => disposeAudioReactiveUniforms(u));
  for (const k of ['drop', 'lead', ...Array.from({ length: 4 }, (_, i) => 'pluckAge' + i), ...Array.from({ length: 4 }, (_, i) => 'pluckStrength' + i)]) u[k] ||= uniform(0);
  const music = { tileBar: uniform(0), tileBeat: uniform(0), tileOn: uniform(1) };
  const layers = tileInstrument({ p, column, row, halfSpan: float(8), kind, reactive: u, music });
  const scan = layer => Array.from({ length: 16 }, (_, y) => Array.from({ length: 12 }, (_, x) => {
    column.value = x; row.value = y; return evaluateColour(layers[layer])[0];
  })).flat();
  const worst = (a, b) => Math.max(...a.map((x, i) => Math.abs(x - b[i])));
  u.gentle.value = 1; u.gentleMix.value = 1; u.bass.value = .8; u.mid.value = .6;
  for (const bar of [16.999, 8.999, 20.999]) {
    music.tileBar.value = bar; const before = scan('bass');
    music.tileBar.value = bar + .002; assert.ok(worst(before, scan('bass')) < .02, 'bar ' + bar + ' crossfades, no tile snaps');
  }
  music.tileBar.value = 13.3; const step = 1 / 60 / GENTLE_SECTION_S;
  for (const s of [0, .9, 1.87]) {
    u.section.value = s; const bass = scan('bass'), mids = scan('mids');
    u.section.value = s + step; assert.ok(worst(bass, scan('bass')) < .03 && worst(mids, scan('mids')) < .03, 'one frame of section walk is a sliver');
  }
});
