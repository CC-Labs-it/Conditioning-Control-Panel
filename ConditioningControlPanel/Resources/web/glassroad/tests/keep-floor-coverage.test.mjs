import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { evaluateColour } from './floor-colour-eval.mjs';

const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { three: 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { Color, Vector2 } = await import('three/webgpu');
const { uniform, vec3 } = await import('three/tsl');
const { createAudioReactiveUniforms } = await import('../render/audioReactive.js');
const { keepMusicFloor } = await import('../render/materials/keepMusicFloor.js');
hooks.deregister();

// Interior samples from the authored staggered slate slabs, including both sides of the road.
// The seam texture repeats every 12 x 16 m with cross-joints 8 m apart, offset 4 m on its right half.
const tiles = [];
for (const repeat of [-2, -1, 0, 1]) for (const x of [1, 4, 8, 11]) {
  const worldX = repeat * 12 + x;
  if (Math.abs(worldX) > 18) continue;
  for (let row = 0; row < 8; row++) tiles.push([worldX, row * 8 + (x < 6 ? 4 : 0)]);
}
const notes = [.15, .32, .61, .24, .48, .65, .19, .43];
const delta = (a, b) => Math.max(...a.map((value, i) => Math.abs(value - b[i])));
function setup() {
  const p = uniform(new Vector2()), reactive = createAudioReactiveUniforms(), seam = uniform(0);
  const base = new Color('#664b69').toArray();
  const music = { tileBeat: uniform(26.4), tileBar: uniform(6.6), tileOn: uniform(1) };
  for (const [key, value] of Object.entries({ energy: .28, bass: .44, mid: .36, treble: .22 })) reactive[key].value = value;
  notes.forEach((value, i) => { reactive['note' + i].value = value; });
  for(let i=0;i<64;i++)reactive.vizTexture.image.data[i*4+1]=Math.round(notes[Math.floor(i/8)]*255);
  const root = keepMusicFloor({ p, base: vec3(...base), seam, reactive, music });
  const colour = (x, y, footprint = .2) => { p.value.set(x, y); return evaluateColour(root, footprint); };
  const capture = footprint => tiles.map(([x, y]) => colour(x, y, footprint));
  return { base, reactive, music, seam, colour, capture };
}

for (const bar of [2, 10, 18]) test('Keep phrase at bar ' + bar + ' lights complete slabs throughout the floor', () => {
  const { base, music, colour, capture } = setup(); music.tileBar.value = bar;
  for (const footprint of [.01, .2, 1, 2]) {
    const colours = capture(footprint);
    assert.ok(colours.flat().every(v => Number.isFinite(v) && v >= 0 && v <= 1));
    const covered = colours.filter(value => delta(value, base) > .035).length / tiles.length;
    assert.ok(covered > .9, 'The full tile field participates at ordinary music energy; coverage ' + covered);
  }
  for (const [x, y] of tiles) {
    const middle = colour(x, y);
    for (const [dx, dy] of [[-.3, -2], [.3, -2], [-.3, 2], [.3, 2]])
      assert.ok(delta(middle, colour(x + dx, y + dy)) < 1e-10,
        'One slab receives one fill rather than a line or figure painted through it');
  }
});

test('Keep tile choreography changes across the whole road with heard time and measured bands', () => {
  const { reactive, music, capture } = setup(), before = capture(.2);
  music.tileBar.value += .8; music.tileBeat.value += 3.2;
  reactive.kick.value = reactive.snare.value = reactive.hitStrength.value = .9; reactive.hitAge.value = .08;
  const moved = capture(.2).filter((value, i) => delta(value, before[i]) > .018).length / tiles.length;
  assert.ok(moved > .5, 'Music motion changes most slabs rather than a few animated marks');
  reactive.kick.value = reactive.snare.value = reactive.hitStrength.value = 0; reactive.hitAge.value = 9;
  const first = capture(.2);
  notes.forEach((_, i) => { reactive['note' + i].value = notes[(i + 3) % 8]; });
  for(let i=0;i<64;i++)reactive.vizTexture.image.data[i*4+1]=Math.round(notes[(Math.floor(i/8)+3)%8]*255);
  const redistributed = capture(.2).filter((value, i) => delta(value, first[i]) > .01).length / tiles.length;
  assert.ok(redistributed > .5, 'Equal loudness with different frequency content gives a different tile field');
});

test('Keep tile fill preserves its mortar, exact silence and stopped-song finish', () => {
  const { base, reactive, music, seam, capture } = setup(), lit = capture(.2);
  seam.value = 1;
  for (const [i, value] of capture(.2).entries())
    assert.ok(delta(value, base) <= delta(lit[i], base) * .041, 'Existing dark mortar remains legible');
  seam.value = 0; reactive.energy.value = 0;
  assert.ok(capture(.2).every(value => delta(value, base) === 0));
  reactive.energy.value = .28; music.tileOn.value = 0;
  assert.ok(capture(.2).every(value => delta(value, base) === 0));
});
