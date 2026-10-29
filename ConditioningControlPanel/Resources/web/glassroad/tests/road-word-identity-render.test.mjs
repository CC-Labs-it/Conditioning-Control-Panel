import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { evaluateColour } from './floor-colour-eval.mjs';

const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { three: 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier]
    ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true }
    : next(specifier, context);
} });
const { Vector2, Vector4 } = await import('three');
const { uniform } = await import('three/tsl');
const { roadWordIdentityUv, ROAD_WORD_IDENTITY, WORD_IDENTITY_COLLECT_SECONDS } = await import('../render/roadWordIdentity.js');
hooks.deregister();

function fixture() {
  const loc = uniform(new Vector2(.37, .62));
  const identity = uniform(new Vector4(0, 0, -1, 6));
  const u = { time: uniform(0), identityMotion: uniform(1) };
  return { loc, identity, u, graph: roadWordIdentityUv(loc, identity, u) };
}
const scalar = (node) => evaluateColour(node)[0];
const near = (actual, expected, message) => assert.ok(Math.abs(actual - expected) < 1e-8, `${message}: ${actual} != ${expected}`);

test('unassigned markings retain the exact original source, including after a pickup', () => {
  const { loc, identity, graph } = fixture();
  for (const age of [-2, -.2, 0, .4, 2]) for (const taken of [-1, 0, .5, 1]) {
    identity.value.set(0, age, taken, 6);
    const uv = evaluateColour(graph.uv);
    near(uv[0], loc.value.x, 'plain u'); near(uv[1], loc.value.y, 'plain v');
    assert.equal(scalar(graph.mask), 1);
    assert.equal(scalar(graph.coreMask), 1);
    assert.equal(scalar(graph.tintMix), 0);
    assert.equal(scalar(graph.lift), 0);
  }
});

test('all identities settle to a complete readable source before the collectible window', () => {
  const { loc, identity, graph } = fixture();
  for (let kind = 1; kind <= 10; kind++) for (const point of [[.18, .3], [.37, .62], [.76, .8]]) {
    loc.value.set(...point); identity.value.set(kind, -.65, -1, 6);
    const uv = evaluateColour(graph.uv);
    near(uv[0], point[0], `${kind} ready u`); near(uv[1], point[1], `${kind} ready v`);
    assert.equal(scalar(graph.mask), 1, `${kind} ready source`);
    assert.equal(scalar(graph.coreMask), 1, `${kind} ready fill`);
  }
});

test('Gentle freezes the original source pose but keeps the real collection lifetime', () => {
  const { loc, identity, u, graph } = fixture();
  u.identityMotion.value = 0;
  for (let kind = 1; kind <= 10; kind++) for (const age of [-.3, .45, 2]) {
    for (const taken of [-1, .2, .8, 1]) {
      identity.value.set(kind, age, taken, 6);
      const uv = evaluateColour(graph.uv);
      near(uv[0], loc.value.x, `${kind} gentle u`); near(uv[1], loc.value.y, `${kind} gentle v`);
      assert.equal(scalar(graph.coreMask), 1, `${kind} gentle fill`);
      assert.equal(scalar(graph.lift), 0, `${kind} gentle lift`);
      const coverage = scalar(graph.mask);
      if (taken < .66) assert.equal(coverage, 1, `${kind} gentle read`);
      if (taken === .8) assert.ok(coverage > 0 && coverage < 1, `${kind} gentle fade`);
      if (taken === 1) assert.equal(coverage, 0, `${kind} gentle gone`);
    }
  }
});

test('all collected source layers disappear, even for long words and reduced motion', () => {
  const { loc, identity, u, graph } = fixture();
  for (const motion of [0, .5, 1]) for (let kind = 1; kind <= 10; kind++) {
    u.identityMotion.value = motion;
    for (const point of [[.01, .01], [.25, .75], [.5, .5], [.99, .99]]) {
      loc.value.set(...point); identity.value.set(kind, 1, 1, 18);
      assert.equal(scalar(graph.mask), 0, `${kind} collected at ${point}`);
    }
  }
  assert.equal(WORD_IDENTITY_COLLECT_SECONDS, .6);
});

test('EMPTY drains filled interiors while retaining its intact hollow shell', () => {
  const { identity, graph } = fixture();
  identity.value.set(ROAD_WORD_IDENTITY.empty, 1, -1, 5);
  assert.equal(scalar(graph.coreMask), 0);
  assert.equal(scalar(graph.mask), 1);
  const still = evaluateColour(graph.uv);
  near(still[0], .37, 'hollow u'); near(still[1], .62, 'hollow v');
  identity.value.z = .6;
  assert.ok(Math.abs(evaluateColour(graph.uv)[0] - .37) > .05, 'pickup collapses the hollow shell');
});

test('GLASS and BURN remove actual source regions during collection', () => {
  const { loc, identity, graph } = fixture();
  for (const kind of [ROAD_WORD_IDENTITY.glass, ROAD_WORD_IDENTITY.burn]) {
    let missing = 0, retained = 0;
    identity.value.set(kind, 1, .55, 5);
    for (let x = 1; x < 9; x++) for (let y = 1; y < 9; y++) {
      loc.value.set(x / 10, y / 10);
      const alpha = scalar(graph.mask);
      if (alpha < .1) missing++;
      if (alpha > .9) retained++;
    }
    assert.ok(missing > 0, `${kind} leaves real holes in source coverage`);
    assert.ok(retained > 0, `${kind} retains source fragments before the final fade`);
  }
});

test('identity graphs remain finite and bounded throughout entry, hold and pickup', () => {
  const { loc, identity, graph } = fixture();
  for (let kind = 1; kind <= 10; kind++) {
    for (const [age, taken] of [[-2.4, -1], [-1.7, -1], [-.65, -1], [0, -1], [.5, -1], [2, .35], [3, .85], [4, 1]]) {
      loc.value.set(.13 + kind * .065, .24 + kind * .044);
      identity.value.set(kind, age, taken, kind % 3 === 0 ? 1 : 12);
      for (const node of [graph.uv, graph.mask, graph.coreMask, graph.tint, graph.tintMix, graph.lift]) {
        assert.ok(evaluateColour(node).every(Number.isFinite), `${kind} finite at ${age}/${taken}`);
      }
      for (const node of [graph.mask, graph.coreMask, graph.tintMix]) {
        const value = scalar(node); assert.ok(value >= 0 && value <= 1);
      }
      assert.ok(Math.abs(scalar(graph.lift)) <= .15, `${kind} road clearance`);
    }
  }
});
