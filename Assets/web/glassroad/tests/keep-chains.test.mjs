import { test } from 'node:test';
import assert from 'node:assert/strict';
import { keepChainPhase, createKeepChains, KEEP_CHAIN } from '../sim/keepChains.js';
import { S, RESPAWN } from '../sim/state.js';
import { fakeTrack, makeFeel, place, recorder } from './sim-fake.mjs';
import { registerHooks } from 'node:module';
const track = fakeTrack({ w: 34 }), row = { id: 'swing', s: 110, offsetS: 0 };
const racer = (s, x = 0, height = 0) => ({ i: 0, st: place(track, { s, lateral: x, height, speed: 40 }), events: recorder(), feel: makeFeel() });
test('chain position repeats on one race clock with a real low sweep and high escape', () => {
  const low = keepChainPhase(row, 0), high = keepChainPhase(row, KEEP_CHAIN.cycleS / 4);
  assert.equal(low.x, 0); assert.equal(low.y, KEEP_CHAIN.pivotM - KEEP_CHAIN.lengthM);
  assert.ok(KEEP_CHAIN.radiusM >= 3.25, 'large swinging weight');
  assert.ok(Math.abs(low.y - KEEP_CHAIN.radiusM - .5) < 1e-9, 'weight clears the road');
  assert.ok(high.x > 7 && high.y > 5);
  for (const t of [0, 1, 3, 4.8]) {
    const a = keepChainPhase(row, t), b = keepChainPhase(row, t + KEEP_CHAIN.cycleS);
    assert.ok(Math.abs(a.x - b.x) < 1e-12); assert.ok(Math.abs(a.y - b.y) < 1e-12);
    assert.deepEqual(keepChainPhase(row, t), a);
  }
});
test('a fast crossing hits the actual hanging weight and keeps bounded cooldown', () => {
  const chains = createKeepChains(track, { chains: [row] }), r = racer(115);
  assert.equal(chains.apply(r, place(track, { s: 105, speed: 40 }), .01), 'swing');
  assert.ok(r.st[S.stalled] <= .451); assert.equal(chains.inspect().hits, 1);
  r.st.set(place(track, { s: 115, speed: 40 }));
  assert.equal(chains.apply(r, place(track, { s: 105, speed: 40 }), .1), null);
  chains.reset(); assert.equal(chains.inspect().hits, 0);
});
test('both bypasses, raised weights and remote/rescued karts stay free', () => {
  for (const [x, time, height] of [[-13, .01, 0], [13, .01, 0], [0, 1.6, 0], [0, .01, 12]]) {
    const chains = createKeepChains(track, { chains: [row] }), r = racer(115, x, height);
    assert.equal(chains.apply(r, place(track, { s: 105, lateral: x, height, speed: 40 }), time), null);
  }
  for (const mutate of [r => r.kind = 'remote', r => r.finished = true, r => r.st[S.respawnPhase] = RESPAWN.PULL]) {
    const chains = createKeepChains(track, { chains: [row] }), r = racer(115); mutate(r);
    assert.equal(chains.apply(r, place(track, { s: 105, speed: 40 }), .01), null);
  }
  assert.throws(() => createKeepChains(fakeTrack({ w: 24 }), { chains: [row] }), /30 m/);
});

test('the enlarged visible weight reaches its new contact lane and stays inside its shared sphere', async () => {
  const r = racer(115, 3.6), chains = createKeepChains(track, { chains: [row] });
  assert.equal(chains.apply(r, place(track, { s:105, lateral:3.6, speed:40 }), .01), row.id,
    'the enlarged outer body has real contact beyond the old 3.15 m kart clearance');
  const hooks = registerHooks({ resolve(specifier, context, next) {
    const file = { 'three/webgpu':'three.webgpu.min.js', 'three/tsl':'three.tsl.min.js' }[specifier];
    return file ? {url:new URL('../vendor/three/build/'+file,import.meta.url).href,shortCircuit:true} : next(specifier,context);
  }});
  try {
    const { Scene, Matrix4, Vector3 } = await import('three/webgpu');
    const { createKeepChainView } = await import('../render/keepChains.js');
    const view = createKeepChainView(new Scene(), track, { chains:[row] });
    const body = ['forged-pendulum-core','pendulum-armour-panels','pendulum-rivets','pendulum-equator-band'];
    const center = new Vector3(0,-KEEP_CHAIN.lengthM,0), point = new Vector3(), matrix = new Matrix4();
    let extent = 0;
    for(const name of body) {
      const mesh = view.group.getObjectByName(name), vertices = mesh.geometry.getAttribute('position');
      mesh.updateMatrix();
      for(let i=0;i<(mesh.isInstancedMesh?mesh.count:1);i++) {
        if(mesh.isInstancedMesh)mesh.getMatrixAt(i,matrix);else matrix.copy(mesh.matrix);
        for(let v=0;v<vertices.count;v++) {
          point.fromBufferAttribute(vertices,v).applyMatrix4(matrix);
          const distance=point.distanceTo(center);extent=Math.max(extent,distance);
          assert.ok(distance<=KEEP_CHAIN.radiusM+1e-6,name+' fits shared collision radius');
        }
      }
    }
    assert.ok(extent>KEEP_CHAIN.radiusM-.02,'visible body reaches the enlarged radius');
    const ball=view.group.getObjectByName('forged-pendulum-core');
    for(const t of [0,.4,1.1,2.4]) {
      view.update(t);const pose=keepChainPhase(row,t);
      assert.equal(ball.parent.rotation.z,pose.angle);
      assert.equal(ball.parent.position.y,KEEP_CHAIN.pivotM);
    }
    view.dispose();
  } finally { hooks.deregister(); }
});
