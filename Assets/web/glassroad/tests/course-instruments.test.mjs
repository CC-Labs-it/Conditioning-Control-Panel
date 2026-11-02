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
const { mainframeTileLight } = await import('../render/materials/mainframeMusicFloor.js');
const { boudoirMusicFloor } = await import('../render/materials/boudoirMusicFloor.js');
const { goonworksMusicFloor } = await import('../render/materials/goonworksMusicFloor.js');
hooks.deregister();
const points = Array.from({ length: 24 }, (_, i) => .3 + i * .58)
  .flatMap(x => Array.from({ length: 58 }, (_, i) => [x, .27 + i * .7]));
const light = c => c[0] * .2126 + c[1] * .7152 + c[2] * .0722;
const change = (a, b) => a.map((c, i) => Math.max(...c.map((v, k) => Math.abs(v - b[i][k]))));
function setup(kind, t) {
  const u = createAudioReactiveUniforms(), p = uniform(new Vector2()), base = vec3(.16, .14, .18);
  t.after(() => disposeAudioReactiveUniforms(u));
  for (const name of ['kick', 'snare', 'hats']) {
    for (const [suffix, value] of [['Age', 9], ['Strength', 0], ['Count', 0]]) u[name + suffix] ||= uniform(value);
  }
  for (const key of ['energy', 'drive']) u[key].value = .4;
  const music = { tileBeat: uniform(0), tileBar: uniform(0), tileOn: uniform(1), tileGentle: uniform(0), big: uniform(.4), calm: uniform(0) };
  const root = kind === 'mainframe' ? mainframeTileLight(p, u, music, float(8), base)
    : kind === 'boudoir' ? boudoirMusicFloor({ p, base, trueEdge: float(8), distance: float(24), wet: float(0), reactive: u, music })
    : goonworksMusicFloor({ p, base, top: float(1), edge: float(18).sub(p.x.abs()), half: float(18),
      pink: vec3(.8, .04, .3), accent: vec3(.08, .6, .5), reactive: u, music });
  const capture = () => points.map(([x, s]) => { p.value.set(x, s); return evaluateColour(root); });
  const set = (instrument, value) => {
    u[instrument].value = value; u[instrument + 'Age'].value = value ? .25 : 9;
    u[instrument + 'Strength'].value = value;
  };
  return { u, music, root, capture, set, data: u.vizTexture.image.data };
}

for (const kind of ['mainframe', 'boudoir', 'goonworks']) {
  test(kind + ' gives bass, snare and highs independent masks and brightness', t => {
    const { u, capture, set } = setup(kind, t), rest = capture(), masks = [];
    u.hitAge.value = .1; u.hitStrength.value = 1; u.onset.value = 1;
    assert.deepEqual(capture(), rest, 'the shared all-instrument pulse cannot drive these layers');
    for (const instrument of ['kick', 'snare', 'hats']) {
      set(instrument, .95); const shown = capture(); set(instrument, 0);
      const delta = change(shown, rest), mask = delta.map(v => v > .012);
      assert.ok(mask.some(Boolean), instrument + ' must visibly light its own geometry');
      assert.ok(mask.filter(Boolean).length < points.length * .42, instrument + ' must leave resting material');
      assert.ok(shown.some((c, i) => light(c) > light(rest[i]) + .04), instrument + ' must change brightness, not hue alone');
      masks.push(mask);
    }
    for (let i = 0; i < 3; i++) for (let j = i + 1; j < 3; j++) {
      const intersection = masks[i].filter((v, k) => v && masks[j][k]).length;
      const union = masks[i].filter((v, k) => v || masks[j][k]).length;
      assert.ok(intersection / union < .35, 'instrument masks need substantially different spatial coverage');
    }
  });
  test(kind + ' has dark rest, luminous attacks, and three distinct melodic compositions', t => {
    const { u, music, capture, set, data } = setup(kind, t);
    u.mid.value = .6;
    for (let i = 0; i < 64; i++) { data[i * 4] = Math.round(128 + Math.sin(i / 63 * Math.PI * 6) * 100); data[i * 4 + 1] = 180; }
    set('kick', 1); set('snare', .8); set('hats', .8);
    const shown = capture(), levels = shown.map(light);
    assert.ok(Math.max(...levels) - Math.min(...levels) > .2, 'visible lit/rest luminance range');
    assert.ok(levels.filter(v => v < .12).length > levels.length * .25, 'a useful fraction remains dark');
    const arrangements = [0, 8, 16].map(bar => { music.tileBar.value = bar; return capture(); });
    for (let i = 1; i < 3; i++) assert.ok(change(arrangements[0], arrangements[i]).some(v => v > .025));
  });
  test(kind + ' freezes fast instrument optics under Gentle and restores exact silence', t => {
    const { u, music, capture, set } = setup(kind, t);
    u.gentle.value = music.tileGentle.value = 1;
    const before = capture(); for (const instrument of ['kick', 'snare', 'hats']) set(instrument, 1);
    assert.deepEqual(capture(), before);
    u.energy.value = u.drive.value = 0;
    for (const c of capture()) assert.deepEqual(c, [.16, .14, .18]);
  });
}
