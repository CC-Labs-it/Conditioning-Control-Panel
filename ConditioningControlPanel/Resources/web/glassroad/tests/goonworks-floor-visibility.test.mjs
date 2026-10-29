import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { evaluateColour } from './floor-colour-eval.mjs';

const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { three: 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { Color, Vector2 } = await import('three/webgpu');
const { uniform, float, vec3 } = await import('three/tsl');
const { createAudioReactiveUniforms } = await import('../render/audioReactive.js');
const { GOONWORKS_PALETTE } = await import('../render/goonworksMaterials.js');
const { goonworksMusicFloor } = await import('../render/materials/goonworksMusicFloor.js');
hooks.deregister();

// Sample two visible road lengths, including the driving area beside the word lane.
// These checks measure changed area, not a few fortunate pixels on narrow shoulder lines.
const samples = Array.from({ length: 22 }, (_, i) => -15.75 + i * 1.5)
  .flatMap(x => Array.from({ length: 22 }, (_, i) => [x, .75 + i * 3]));
const drivingArea = samples.map(([x]) => Math.abs(x) >= 2 && Math.abs(x) <= 7);
const footprints = [.01, .2, 1, 2];
const notes = [.15, .32, .61, .24, .48, .65, .19, .43];
const delta = (a, b) => Math.max(...a.map((value, i) => Math.abs(value - b[i])));
const fraction = values => values.filter(Boolean).length / values.length;

function setup(finish) {
  const p = uniform(new Vector2()), reactive = createAudioReactiveUniforms();
  const base = new Color(GOONWORKS_PALETTE[finish]).toArray();
  const music = { tileBeat: uniform(25.44525), tileBar: uniform(6.3613125), tileOn: uniform(1), tileGentle: uniform(0) };
  for (const [key, value] of Object.entries({ energy: .28, bass: .44, mid: .36, treble: .22 })) reactive[key].value = value;
  notes.forEach((value, i) => { reactive['note' + i].value = value; });
  const pink = finish === 'ice' ? 'magenta' : 'fuchsia', accent = finish === 'ice' ? 'teal' : 'gold';
  const root = goonworksMusicFloor({ p, base: vec3(...base), top: float(1), edge: float(18).sub(p.x.abs()), half: float(18),
    pink: vec3(...new Color(GOONWORKS_PALETTE[pink]).toArray()),
    accent: vec3(...new Color(GOONWORKS_PALETTE[accent]).toArray()), reactive, music });
  const capture = footprint => samples.map(([x, y]) => { p.value.set(x, y); return evaluateColour(root, footprint); });
  return { base, reactive, music, capture };
}

for (const finish of ['white', 'ice']) {
  for (const footprint of footprints) test('GOONWORKS ' + finish + ' floor reads across the driving surface at 16 seconds, footprint ' + footprint, () => {
    const { base, capture } = setup(finish), colours = capture(footprint);
    assert.ok(colours.flat().every(Number.isFinite), 'Visible floor colours remain finite');
    const changed = colours.map(colour => delta(colour, base) >= .035);
    const coverage = fraction(changed), drivingCoverage = fraction(changed.filter((_, i) => drivingArea[i]));
    assert.ok(coverage >= .25, 'At least a quarter of the sampled floor visibly responds; measured ' + (coverage * 100).toFixed(1) + '%');
    assert.ok(drivingCoverage >= .20, 'Paint reaches the driving area beside the words; measured ' + (drivingCoverage * 100).toFixed(1) + '%');
  });

  test('GOONWORKS ' + finish + ' floor visibly distinguishes measured band distributions at equal loudness', () => {
    const { reactive, capture } = setup(finish), before = capture(.2);
    // Same measured loudness and same set of note levels, heard in different bands.
    notes.forEach((_, i) => { reactive['note' + i].value = notes[(i + 3) % notes.length]; });
    reactive.bass.value = .22; reactive.mid.value = .44; reactive.treble.value = .36;
    const changed = capture(.2).map((colour, i) => delta(colour, before[i]) >= .018);
    const coverage = fraction(changed);
    assert.ok(coverage >= .03, 'Changing real bands lights sparse stepped tiles at equal energy; measured ' + (coverage * 100).toFixed(1) + '%');
  });

  test('GOONWORKS ' + finish + ' floor preserves its exact pale finish without audible music', () => {
    const { base, reactive, music, capture } = setup(finish);
    reactive.energy.value = 0;
    assert.ok(capture(.2).every(colour => colour.every((value, i) => value === base[i])), 'Real silence restores the authored finish exactly');
    reactive.energy.value = .28; music.tileOn.value = 0;
    assert.ok(capture(.2).every(colour => colour.every((value, i) => value === base[i])), 'A stopped song restores the authored finish exactly');
  });
}
