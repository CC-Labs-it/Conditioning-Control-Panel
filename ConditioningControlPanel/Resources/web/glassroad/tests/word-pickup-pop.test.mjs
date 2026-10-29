import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { evaluateColour } from './floor-colour-eval.mjs';
import { B1_CUES } from '../audio/cues-b1.js';
const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { three: 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { Vector2 } = await import('three');
const { uniform } = await import('three/tsl');
const { wordPickupPop } = await import('../render/wordPickupPop.js');
hooks.deregister();
const value = node => evaluateColour(node)[0];
function fixture() {
  const loc = uniform(new Vector2(.35, .6)), progress = uniform(-1), motion = uniform(1);
  return { loc, progress, motion, pop: wordPickupPop(loc, progress, motion) };
}

test('pickup stays absent before contact and vanishes completely after its lifetime', () => {
  const { progress, pop } = fixture();
  for (const p of [-1, 0, 1, 2]) {
    progress.value = p;
    for (const node of [pop.glow, pop.shine, pop.lift]) assert.equal(value(node), 0);
  }
});

test('contact compresses lettering then releases it above the road with a bounded lift', () => {
  const { progress, pop } = fixture();
  progress.value = .12;
  assert.ok(evaluateColour(pop.uv)[1] > .6, 'inverse compressed height');
  progress.value = .38;
  assert.ok(evaluateColour(pop.uv)[1] < .6, 'inverse stretched height');
  assert.ok(value(pop.lift) > .4, 'lift visibly separates the ink from the road');
  for (let p = 0; p <= 1; p += .025) {
    progress.value = p;
    assert.ok(value(pop.lift) <= .55 && value(pop.lift) >= 0, 'keep the long strip beneath the chase eye');
  }
});

test('Gentle and Motion Off suppress the entire spatial pop and decorative burst', () => {
  const { loc, progress, motion, pop } = fixture();
  motion.value = 0;
  for (const p of [.08, .3, .5, .8]) {
    progress.value = p;
    assert.deepEqual(evaluateColour(pop.uv).slice(0, 2), [loc.value.x, loc.value.y]);
    for (const node of [pop.glow, pop.shine, pop.lift]) assert.equal(value(node), 0);
  }
});

test('the expanding halo stays local and its twelve sparkles use a finite deterministic envelope', () => {
  const { loc, progress, pop } = fixture();
  progress.value = .3;
  loc.value.set(.5 + .07 + .3 * .31, .5);
  assert.ok(value(pop.glow) > .5);
  for (const p of [.1, .3, .55, .9]) for (const x of [0, .2, .5, .8, 1]) for (const y of [0, .2, .5, .8, 1]) {
    progress.value = p; loc.value.set(x, y);
    assert.ok(Number.isFinite(value(pop.glow)) && value(pop.glow) <= 1.55);
    if (x === 0 || x === 1 || y === 0 || y === 1) assert.equal(value(pop.glow), 0);
  }
});

test('floor pickups sound at the cap, with bounded close-contact polyphony', () => {
  const tones = [], noises = [], syn = { ctx: { currentTime: 10 }, semis: n => 110 * 2 ** (n / 12),
    tone: (f, options) => tones.push({ f, ...options }), noise: options => noises.push(options) };
  B1_CUES['word-pickup'](syn, { count: 10 });
  assert.equal(tones.length, 2); assert.equal(noises.length, 1);
  assert.ok(tones[1].f > tones[0].f);
  B1_CUES['word-pickup'](syn, { count: 10 });
  assert.equal(tones.length, 2, 'same-frame dense words do not stack audio');
  syn.ctx.currentTime += .1;
  B1_CUES['word-pickup'](syn, { count: 10 });
  assert.equal(tones.length, 4, 'later contacts still pop while capped');
});
