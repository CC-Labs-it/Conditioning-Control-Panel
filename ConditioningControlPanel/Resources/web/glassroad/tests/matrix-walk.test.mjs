import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';

const hooks = registerHooks({ resolve(s, c, next) {
  const files = { 'three/tsl': 'three.tsl.min.js', 'three/webgpu': 'three.webgpu.min.js' };
  return files[s] ? { url: new URL('../vendor/three/build/' + files[s], import.meta.url).href, shortCircuit: true } : next(s, c);
} });
const { installMatrixWalk, refreshMatrices, matrixWalkStats } = await import('../render/matrixWalk.js');
const { Scene, Group, Mesh, LOD, Bone, Matrix4, Vector3, Quaternion, Euler, PerspectiveCamera } = await import('three/webgpu');
hooks.deregister();

function rng(seed) { let s = seed >>> 0; return () => ((s = (s * 1664525 + 1013904223) >>> 0) / 4294967296); }

/** What stock three would hold: the composed chain of every ancestor (non-auto nodes keep their own .matrix). */
function expected(o) {
  const chain = [];
  for (let x = o; x; x = x.parent) chain.unshift(x);
  const m = new Matrix4(), local = new Matrix4();
  for (const x of chain) {
    if (x.matrixAutoUpdate) local.compose(x.position, x.quaternion, x.scale); else local.copy(x.matrix);
    m.multiply(local);
  }
  return m;
}
function shown(o) { for (let x = o; x; x = x.parent) if (!x.visible) return false; return true; }
function near(a, b) { for (let i = 0; i < 16; i++) if (Math.abs(a.elements[i] - b.elements[i]) > 1e-9) return false; return true; }
function checkShown(scene, label) {
  scene.traverse((o) => {
    if (o === scene || !shown(o)) return;
    assert.ok(near(o.matrixWorld, expected(o)), label + ': ' + (o.name || o.type) + ' world matrix');
  });
}

function buildTree(rand, n = 160) {
  const scene = new Scene(), nodes = [scene];
  for (let i = 0; i < n; i++) {
    const parent = nodes[Math.floor(rand() * nodes.length)];
    const o = rand() < .5 ? new Group() : new Mesh();
    o.name = 'n' + i;
    o.position.set(rand() * 10 - 5, rand() * 10 - 5, rand() * 10 - 5);
    o.rotation.set(rand() * 3, rand() * 3, rand() * 3);
    o.scale.setScalar(.5 + rand());
    parent.add(o); nodes.push(o);
  }
  return { scene, nodes: nodes.slice(1) };
}

test('matrix walk keeps every shown world matrix equal to stock three through moves, hides, reparents and outside writes', () => {
  const rand = rng(7);
  const { scene, nodes } = buildTree(rand);
  installMatrixWalk(scene);
  const frozen = nodes.filter((_, i) => i % 9 === 0);
  for (const o of frozen) { o.matrixAutoUpdate = false; o.updateMatrix(); }
  scene.updateMatrixWorld();
  checkShown(scene, 'first walk');
  for (let frame = 0; frame < 120; frame++) {
    for (let k = 0; k < 6; k++) {
      const o = nodes[Math.floor(rand() * nodes.length)], r = rand();
      if (r < .3) o.position.x += rand() - .5;
      else if (r < .45) o.rotation.y += rand();
      else if (r < .55) o.scale.y = .5 + rand();
      else if (r < .7) o.visible = !o.visible;
      else if (r < .78) {
        // reparent without touching the local transform (add, not attach)
        const p = nodes[Math.floor(rand() * nodes.length)];
        let loop = false; for (let x = p; x; x = x.parent) if (x === o) loop = true;
        if (!loop) p.add(o);
      } else if (r < .86 && !o.matrixAutoUpdate) {
        o.position.z += 1; o.updateMatrix();                    // a frozen node moved the documented way
      } else if (r < .93) {
        // an outside compose with other values, then the values put back without a compose
        const was = o.position.x; o.position.x += 3; o.updateMatrixWorld(true); o.position.x = was;
      } else if (!o.matrixAutoUpdate) {
        o.matrix.elements[12] += .25;                             // a direct write on a frozen node's matrix
      }
    }
    scene.updateMatrixWorld();
    checkShown(scene, 'frame ' + frame);
  }
});

test('hidden subtrees are skipped, then recomputed whole when shown again', () => {
  const scene = new Scene(), pool = new Group(), a = new Group(), b = new Mesh();
  scene.add(pool); pool.add(a); a.add(b);
  installMatrixWalk(scene);
  pool.visible = false;
  scene.updateMatrixWorld();
  const before = matrixWalkStats();
  scene.updateMatrixWorld();
  assert.equal(matrixWalkStats().visits - before.visits, 1, 'only the scene root is visited');
  a.position.set(4, 5, 6); b.position.set(1, 0, 0);
  pool.visible = true;
  scene.updateMatrixWorld();
  checkShown(scene, 'shown again');
  assert.deepEqual(new Vector3().setFromMatrixPosition(b.matrixWorld).toArray(), [5, 5, 6]);
});

test('LOD levels, bones and lights are walked even while hidden (LOD.update shows a level after the walk)', () => {
  const scene = new Scene(), lod = new LOD(), close = new Mesh(), far = new Mesh(), bone = new Bone();
  lod.addLevel(close, 0); lod.addLevel(far, 50); scene.add(lod); scene.add(bone);
  installMatrixWalk(scene);
  far.visible = false; bone.visible = false;
  far.position.set(0, 2, 0); bone.position.set(3, 0, 0); lod.position.set(1, 0, 0);
  scene.updateMatrixWorld();
  assert.ok(near(far.matrixWorld, expected(far)));
  assert.ok(near(bone.matrixWorld, expected(bone)));
});

test('a render nested inside the same scene\'s render skips its walk; a throw never leaves the walk off', async () => {
  const scene = new Scene(), o = new Group(); scene.add(o);
  const three = { _callDepth: -1 };
  installMatrixWalk(scene, three);
  const render = (inner) => {
    three._callDepth++;
    scene.updateMatrixWorld();
    scene.onBeforeRender(three, scene, null, null);
    if (inner) inner();
    three._callDepth--;
    scene.onAfterRender(three, scene, null, null);
  };
  const s0 = matrixWalkStats();
  render(() => render());                                     // the shadow pass inside the scene pass
  const s1 = matrixWalkStats();
  assert.equal(s1.walks - s0.walks, 1);
  assert.equal(s1.nestedSkips - s0.nestedSkips, 1);
  render();                                                   // a later pass in the same task walks
  assert.equal(matrixWalkStats().walks - s1.walks, 1);
  // a pass that throws after onBeforeRender: r186 never lowers its depth again
  three._callDepth++; scene.updateMatrixWorld(); scene.onBeforeRender(three, scene, null, null);
  await Promise.resolve();
  const s2 = matrixWalkStats();
  o.position.set(9, 0, 0);
  render();
  assert.equal(matrixWalkStats().walks - s2.walks, 1, 'next task walks again');
  assert.equal(o.matrixWorld.elements[12], 9);
});

test('forced walks stay stock (warm paths) and refreshMatrices covers hidden instancing sources', () => {
  const scene = new Scene(), group = new Group(), head = new Group(), source = new Mesh();
  scene.add(group); group.add(head); head.add(source); source.visible = false;
  installMatrixWalk(scene);
  group.visible = false;
  head.position.set(2, 0, 0);
  scene.updateMatrixWorld(true);
  assert.ok(near(source.matrixWorld, expected(source)), 'forced walk reaches hidden nodes');
  group.visible = true; head.position.set(0, 7, 0);
  refreshMatrices(group, { hidden: true });
  assert.ok(near(source.matrixWorld, expected(source)), 'subtree refresh reaches the hidden source');
  // the subtree root follows its parent even when the parent moved outside any walk
  scene.position.set(0, 0, 3); scene.updateMatrix(); scene.matrixWorld.copy(scene.matrix);
  refreshMatrices(group, { hidden: true });
  assert.ok(near(source.matrixWorld, expected(source)), 'root follows a moved parent');
});

test('cameras and other nodes with their own updateMatrixWorld keep it', () => {
  const scene = new Scene(), rig = new Group(), cam = new PerspectiveCamera();
  scene.add(rig); rig.add(cam);
  installMatrixWalk(scene);
  rig.position.set(0, 4, 0); cam.position.set(0, 0, 10);
  scene.updateMatrixWorld();
  assert.ok(near(cam.matrixWorld, expected(cam)));
  assert.ok(near(cam.matrixWorldInverse, cam.matrixWorld.clone().invert()), 'camera inverse kept current');
  void Quaternion; void Euler;
});

test('the walk keeps one record per node: hidden, re-parented and shown again all still land', () => {
  const scene = new Scene(), a = new Group(), b = new Group(), leaf = new Mesh();
  scene.add(a, b); a.add(leaf);
  a.position.set(1, 2, 3); b.position.set(-5, 0, 0); leaf.position.set(0, 1, 0);
  installMatrixWalk(scene);
  scene.updateMatrixWorld();
  const own = (o) => Object.keys(o).filter((k) => k.startsWith('_glassroadMw'));
  assert.deepEqual(own(leaf), ['_glassroadMw'], 'one walk property per node');
  a.visible = false; a.position.x = 10; scene.updateMatrixWorld();
  assert.deepEqual(own(a), ['_glassroadMw'], 'a hidden skip marks the same record');
  a.visible = true; scene.updateMatrixWorld();
  assert.ok(near(leaf.matrixWorld, expected(leaf)), 'shown again: stale subtree recomputed');
  b.add(leaf); scene.updateMatrixWorld();
  assert.ok(near(leaf.matrixWorld, expected(leaf)), 're-parented: recomputed under the new parent');
});
