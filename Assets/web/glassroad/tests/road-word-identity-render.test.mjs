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
const KINDS = Object.values(ROAD_WORD_IDENTITY).filter(kind => kind > 0);

test('every declared mechanism has one distinct stable id', () => {
  assert.equal(KINDS.length, 36);
  assert.equal(new Set(KINDS).size, KINDS.length);
  assert.deepEqual([...KINDS].sort((a, b) => a - b), Array.from({ length: 36 }, (_, i) => i + 1));
});

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
  for (const kind of KINDS) for (const point of [[.18, .3], [.37, .62], [.76, .8]]) {
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
  for (const kind of KINDS) for (const age of [-.3, .45, 2]) {
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
  for (const motion of [0, .5, 1]) for (const kind of KINDS) {
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
  for (const kind of KINDS) {
    for (const [age, taken] of [[-2.4, -1], [-1.7, -1], [-.65, -1], [0, -1], [.5, -1], [2, .35], [3, .85], [4, 1]]) {
      loc.value.set(.13 + (kind % 11) * .065, .24 + (kind % 11) * .044);
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

test('expanded destructive families remove source regions before their final fade', () => {
  const { loc, identity, graph } = fixture();
  for (const kind of [ROAD_WORD_IDENTITY.fragment, ROAD_WORD_IDENTITY.fade, ROAD_WORD_IDENTITY.code,
    ROAD_WORD_IDENTITY.fold, ROAD_WORD_IDENTITY.split, ROAD_WORD_IDENTITY.rain]) {
    identity.value.set(kind, 1, .58, 6);
    let cleared = 0;
    for (let x = 1; x <= 8; x++) for (let y = 1; y <= 8; y++) {
      loc.value.set(x / 10, y / 10);
      if (scalar(graph.mask) < .2) cleared++;
    }
    assert.ok(cleared > 0, `${kind} removes its source, not just an unrelated overlay`);
  }
});

test('each additional family has a distinct source performance', () => {
  const { loc, identity, graph } = fixture();
  const signatures = new Map();
  for (const kind of KINDS.filter(value => value >= 11)) {
    const values = [];
    for (const [x, y, age, taken] of [[.28, .42, .45, -1], [.69, .73, .7, -1], [.36, .58, 1, .48]]) {
      loc.value.set(x, y); identity.value.set(kind, age, taken, 6);
      values.push(...evaluateColour(graph.uv), scalar(graph.mask), scalar(graph.coreMask),
        ...evaluateColour(graph.tint), scalar(graph.tintMix));
    }
    const key = values.map(value => value.toFixed(5)).join(',');
    assert.equal(signatures.has(key), false, `${kind} duplicates ${signatures.get(key)}`);
    signatures.set(key, kind);
  }
});

test('literal green retains green pigment instead of a travelling spectrum', () => {
  const { identity, graph } = fixture();
  for (const age of [-.65, .1, 1, 4]) {
    identity.value.set(ROAD_WORD_IDENTITY.green, age, -1, 5);
    const color = evaluateColour(graph.tint);
    assert.ok(color[1] > color[0] && color[1] > color[2]);
  }
});

test('echo keeps one filled source and two hollow source impressions', () => {
  const { loc, identity, graph } = fixture();
  identity.value.set(ROAD_WORD_IDENTITY.echo, 1, -1, 5);
  for (const [y, filled] of [[.16, 0], [.5, 1], [.84, 0]]) {
    loc.value.set(.37, y);
    assert.equal(scalar(graph.coreMask), filled);
    assert.equal(scalar(graph.mask), 1);
  }
});

test('rain and slide source columns never borrow their neighbouring column on pickup', () => {
  const { loc, identity, graph } = fixture();
  for (const kind of [ROAD_WORD_IDENTITY.slide, ROAD_WORD_IDENTITY.rain]) {
    for (const taken of [.1, .4, .7]) for (const x of [.12, .39, .78]) {
      loc.value.set(x, .6); identity.value.set(kind, 1, taken, 6);
      near(evaluateColour(graph.uv)[0], x, `${kind} source column`);
    }
  }
});

test('vapour and digital erase never restore a source region after clearing it', () => {
  const { loc, identity, graph } = fixture();
  for (const kind of [ROAD_WORD_IDENTITY.fade, ROAD_WORD_IDENTITY.code]) {
    for (const [x, y] of [[.18, .4], [.46, .6], [.75, .3]]) {
      loc.value.set(x, y); let previous = 1;
      for (let i = 0; i <= 20; i++) {
        identity.value.set(kind, 1, i / 20, 6);
        const alpha = scalar(graph.mask);
        assert.ok(alpha <= previous + 1e-8, `${kind} reappeared at pickup ${i / 20}`);
        previous = alpha;
      }
    }
  }
});
