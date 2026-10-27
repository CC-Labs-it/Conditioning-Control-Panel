// Uniform groups (Firefox per-draw cost, 2026-10-27): the learned update writes exactly what three's stock update writes.
import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
registerHooks({ resolve(s, c, next) {
  const files = { 'three': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js', 'three/webgpu': 'three.webgpu.min.js' };
  if (s.startsWith('three/addons/')) return { url: new URL('../vendor/' + s, import.meta.url).href, shortCircuit: true };
  return files[s] ? { url: new URL('../vendor/three/build/' + files[s], import.meta.url).href, shortCircuit: true } : next(s, c);
} });
const T = await import('three/webgpu');
const { fastGroupUpdate, fastUniformGroupUpdates, restoreUniformGroupUpdates } = await import('../render/uniformGroups.js');

const SHAPES = {
  float: ['isNumberUniform', 4, 1, () => Math.random() < .5 ? 1 : Math.random()],
  int: ['isNumberUniform', 4, 1, () => Math.floor(Math.random() * 3) - 1],
  uint: ['isNumberUniform', 4, 1, () => Math.floor(Math.random() * 3)],
  vec2: ['isVector2Uniform', 8, 2, () => new T.Vector2(r(), r())],
  ivec3: ['isVector3Uniform', 16, 3, () => new T.Vector3(Math.floor(r() * 4), 1, 2)],
  vec3: ['isVector3Uniform', 16, 3, () => new T.Vector3(r(), r(), r())],
  vec4: ['isVector4Uniform', 16, 4, () => new T.Vector4(r(), r(), r(), r())],
  color: ['isColorUniform', 16, 3, () => new T.Color(r(), r(), r())],
  mat3: ['isMatrix3Uniform', 48, 12, () => new T.Matrix3().set(r(), r(), 0, 1, r(), 0, 0, 0, r())],
  mat4: ['isMatrix4Uniform', 64, 16, () => new T.Matrix4().makeRotationY(r()).setPosition(r(), 0, r())],
};
function r() { return Math.random() < .6 ? 0.5 : Math.round(Math.random() * 8) / 4; }

/** A real r186 NodeUniformsGroup: a GLSL node builder lays the given uniform nodes out (no renderer needed). */
function makeGroup(types, nodes) {
  const builder = new T.GLSLNodeBuilder(new T.Mesh(), { nodes: { library: { fromMaterial() { return null; } } }, contextNode: null,
    getMRT() { return null; }, backend: {}, library: {}, debug: { diagnostics: {} } });
  builder.shaderStage = 'fragment';
  types.forEach((type, i) => builder.getUniformFromNode(nodes[i], type, 'fragment', 'u' + i));
  builder.getUniforms('fragment');
  const seen = new Set();
  let group = null;
  const walk = (x, d) => {
    if (group || !x || typeof x !== 'object' || d > 5 || seen.has(x)) return;
    seen.add(x);
    if (x.isNodeUniformsGroup) { group = x; return; }
    for (const k of Object.keys(x)) walk(x[k], d + 1);
  };
  walk(builder, 0);
  assert.ok(group && group.uniforms.length === types.length, 'the builder made one group');
  return group;
}

test('the learned uniform update matches stock three: buffer bytes, shadow values, ranges, answer', () => {
  const types = Object.keys(SHAPES).concat(['mat4', 'float', 'vec3', 'int']);
  const nodes = types.map((t) => new T.UniformNode(SHAPES[t][3]()));
  const stock = makeGroup(types, nodes), fast = makeGroup(types, nodes);
  for (let step = 0; step < 400; step++) {
    for (let i = 0; i < nodes.length; i++) if (Math.random() < .3) nodes[i].value = SHAPES[types[i]][3]();
    const a = stock.update(), b = fastGroupUpdate.call(fast);
    assert.equal(b, a, 'same answer at step ' + step);
    assert.deepEqual(new Uint32Array(fast.buffer.buffer), new Uint32Array(stock.buffer.buffer), 'same bytes');
    assert.deepEqual(fast.values, stock.values);
    assert.deepEqual(fast.updateRanges.map((x) => [x.start, x.count]), stock.updateRanges.map((x) => [x.start, x.count]));
    stock.clearUpdateRanges(); fast.clearUpdateRanges();
  }
});

test('a group whose uniform list changes is learned again', () => {
  const nodes = [new T.UniformNode(1), new T.UniformNode(new T.Vector3(1, 2, 3))];
  const g = makeGroup(['float', 'vec3'], nodes);
  fastGroupUpdate.call(g); g.clearUpdateRanges();
  const extra = makeGroup(['float', 'float'], [new T.UniformNode(7), new T.UniformNode(9)]).uniforms;
  g.uniforms[0] = extra[0];                                            // same length, another uniform in slot 0 (offset 0)
  assert.equal(fastGroupUpdate.call(g), true);
  assert.equal(g.buffer[0], 7);
  const plain = { isNumberUniform: true, boundary: 4, itemSize: 1, offset: 1, value: 5, getValue() { return this.value; }, getType() { return 'float'; } };
  g.uniforms[0] = plain;                                               // not a node uniform: three's own updateByType
  assert.equal(fastGroupUpdate.call(g), true);
  assert.equal(g.values[1], 5);
});

test('installs on the first bind group that holds a uniform group, then steps aside', () => {
  const calls = [];
  const bindings = { _update(group) { calls.push(group); } };
  assert.equal(fastUniformGroupUpdates({ _bindings: bindings }), true);
  assert.equal(fastUniformGroupUpdates({ _bindings: bindings }), false, 'once');
  const g = makeGroup(['float'], [new T.UniformNode(1)]);
  const proto = Object.getPrototypeOf(Object.getPrototypeOf(g)), stockUpdate = proto.update;
  bindings._update({ bindings: [{ isSampledTexture: true }, g] });
  assert.equal(calls.length, 1);
  assert.equal(proto.update, fastGroupUpdate, 'UniformsGroup.update swapped');
  restoreUniformGroupUpdates();
  assert.equal(proto.update, stockUpdate, 'restored');
});
