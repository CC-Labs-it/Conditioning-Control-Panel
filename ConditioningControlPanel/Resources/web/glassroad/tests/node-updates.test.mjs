// Node updates (Firefox per-draw cost, 2026-10-27): the remembered record calls node.update exactly when stock three does.
import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
registerHooks({ resolve(s, c, next) {
  const files = { 'three': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js', 'three/webgpu': 'three.webgpu.min.js' };
  if (s.startsWith('three/addons/')) return { url: new URL('../vendor/' + s, import.meta.url).href, shortCircuit: true };
  return files[s] ? { url: new URL('../vendor/three/build/' + files[s], import.meta.url).href, shortCircuit: true } : next(s, c);
} });
const T = await import('three/webgpu');
const { fastUpdateNode, installNodeUpdates, restoreNodeUpdates } = await import('../render/nodeUpdates.js');
const stockUpdateNode = T.NodeFrame.prototype.updateNode;

function scenario(updateNode, seed) {
  let s = seed;
  const rnd = () => (s = (s * 1103515245 + 12345) % 2147483648) / 2147483648;
  const frames = [new T.NodeFrame(), new T.NodeFrame()];
  const shared = {}, frozen = Object.freeze({}), userData = { fx: 1 };
  const refs = [shared, frozen, userData, () => 0, null];
  const log = [];
  const nodes = [];
  for (let i = 0; i < 24; i++) {
    const type = ['frame', 'render', 'object', 'none'][i % 4], ref = i % 5 === 0 ? null : refs[i % refs.length];
    const node = { id: i, getUpdateType() { return type; }, updateReference() { return ref === null ? this : ref; },
      update(f) { log.push(i + '@' + f.frameId + '/' + f.renderId); return i % 7 === 0 && rnd() < .3 ? false : undefined; } };
    nodes.push(node);
  }
  for (let step = 0; step < 300; step++) {
    const f = frames[rnd() < .8 ? 0 : 1];
    if (rnd() < .2) f.frameId++;
    if (rnd() < .4) f.renderId++;
    if (step === 150) { const copy = { ...userData }; nodes[2].updateReference = () => copy; }   // a spread copy is its own reference
    updateNode.call(f, nodes[Math.floor(rnd() * nodes.length)]);
  }
  return log;
}

test('the remembered update record matches stock three call for call', () => {
  for (const seed of [1, 7, 42, 2027]) assert.deepEqual(scenario(fastUpdateNode, seed), scenario(stockUpdateNode, seed));
});

test('installs once and restores', () => {
  assert.equal(installNodeUpdates({ _nodes: { nodeFrame: new T.NodeFrame() } }), true);
  assert.equal(installNodeUpdates({ _nodes: { nodeFrame: new T.NodeFrame() } }), false);
  assert.equal(T.NodeFrame.prototype.updateNode, fastUpdateNode);
  restoreNodeUpdates();
  assert.equal(T.NodeFrame.prototype.updateNode, stockUpdateNode);
});
