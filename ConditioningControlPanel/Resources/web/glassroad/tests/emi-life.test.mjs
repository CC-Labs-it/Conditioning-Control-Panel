import test from 'node:test';
import assert from 'node:assert/strict';
import { createLifeState, stepLife, spring, gestureAt, LIFE_LIMITS } from '../render/emiLifeMath.js';
import fs from 'node:fs/promises';

test('spring integration agrees across frame rates for held targets', () => {
  const run = hz => { const s = { x: 0, v: 0 }; for (let i = 0; i < hz * 3; i++) spring(s, .19, 1 / hz); return s; };
  const a = run(30), b = run(144);
  assert.ok(Math.abs(a.x - b.x) < 1e-12); assert.ok(Math.abs(a.v - b.v) < 1e-12);
});
test('a greeting anticipates, holds deliberately, and recovers to rest', () => {
  assert.ok(gestureAt(.15).anticipate > .9);
  assert.equal(gestureAt(1).hold, 1); assert.equal(gestureAt(1.5).hold, 1);
  assert.equal(gestureAt(4).hold, 0); assert.equal(gestureAt(4).phase, 'rest');
});
test('motion off removes every offset immediately without a lingering spring', () => {
  const s = createLifeState(7);
  for (let i = 0; i < 70; i++) stepLife(s, 1 / 60, { menu: true, selected: true });
  assert.ok(Object.values(stepLife(s, 0, {})).some(x => x !== 0));
  const off = stepLife(s, 1 / 60, { motionOff: true });
  assert.ok(Object.values(off).every(x => x === 0));
  assert.ok(Object.values(s.channels).every(q => q.v === 0));
});
test('authoritative clips own the body and all contact limbs immediately', () => {
  const s = createLifeState();
  for (let i = 0; i < 80; i++) stepLife(s, 1 / 60, { menu: true, selected: true });
  const o = stepLife(s, 1 / 60, { clip: 'landing_heavy', boost: 1, hit: 1 });
  for (const k of ['bodyPitch', 'bodyYaw', 'bodyRoll', 'shoulder', 'foot']) assert.equal(o[k], 0);
});
test('all signals stay bounded over a long session and rest after inputs stop', () => {
  const s = createLifeState('repeatable'); let o;
  for (let i = 0; i < 18000; i++) {
    o = stepLife(s, 1 / 60, { speed: 1, steer: i % 2 ? -1 : 1, boost: 1, land: 1, hit: 1, air: 1 });
    assert.ok(Object.values(o).every(Number.isFinite));
    for (const [k, v] of Object.entries(o)) assert.ok(Math.abs(v) <= (k.startsWith('ant') || k.startsWith('tip') ? LIFE_LIMITS.antenna : k === 'foot' ? LIFE_LIMITS.foot : k === 'shoulder' ? LIFE_LIMITS.shoulder : LIFE_LIMITS.body));
  }
  for (let i = 0; i < 600; i++) o = stepLife(s, 1 / 60, {});
  assert.ok(Object.values(o).every(x => Math.abs(x) < 1e-8));
});
test('gentle reduces the same seeded performance and different seeds separate held attention', () => {
  const run = (seed, gentle) => { const s = createLifeState(seed); let o; for (let i = 0; i < 60; i++) o = stepLife(s, 1 / 60, { menu: true, selected: true, gentle }); return o; };
  const a = run(0, false), b = run(0, true), c = run(900, false);
  assert.ok(Math.abs(b.bodyPitch) < Math.abs(a.bodyPitch) * .5);
  assert.ok(a.bodyYaw * c.bodyYaw < 0);
});

test('the full menu performance agrees at 30 and 120 fps', () => {
  const run = hz => { const s = createLifeState(63); let o; for (let i = 0; i < hz * 2; i++) o = stepLife(s, 1 / hz, { menu: true, selected: true }); return o; };
  const a = run(30), b = run(120);
  for (const k in a) assert.ok(Math.abs(a[k] - b[k]) < .003, k);
});

test('actual R4 rest hierarchy retains floor heights and resets additive transforms without drift', async () => {
  const vendor = new URL('../vendor/three/build/three.webgpu.min.js', import.meta.url);
  const THREE = await import(vendor.href);
  let source = await fs.readFile(new URL('../render/emiLife.js', import.meta.url), 'utf8');
  source = source.replace("'three/webgpu'", JSON.stringify(vendor.href)).replace("'./emiLifeMath.js'", JSON.stringify(new URL('../render/emiLifeMath.js', import.meta.url).href));
  const { createEmiLife } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
  const bytes = await fs.readFile(new URL('../art/models/emi-teacup-kart.glb', import.meta.url));
  const doc = JSON.parse(bytes.subarray(20, 20 + bytes.readUInt32LE(12)).toString());
  const model = new THREE.Group();
  const nodes = doc.nodes.map(n => { const o = new THREE.Object3D(); o.name = n.name || ''; if (n.translation) o.position.fromArray(n.translation); if (n.rotation) o.quaternion.fromArray(n.rotation); if (n.scale) o.scale.fromArray(n.scale); return o; });
  doc.nodes.forEach((n, i) => { for (const c of n.children || []) nodes[i].add(nodes[c]); });
  for (const i of doc.scenes[doc.scene || 0].nodes) model.add(nodes[i]);
  model.updateMatrixWorld(true);
  const names = ['EMI_root', 'footL', 'footR', 'shoulderL', 'shoulderR', 'ant0', 'ant1', 'ant2', 'head_attach'];
  const before = names.map(name => { const b = model.getObjectByName(name); assert.ok(b, name); return { b, p: b.position.clone(), q: b.quaternion.clone() }; });
  const probes = [
    ['footL', [.12848665, .31999999, -.18965095]], ['footR', [-.0568836, .31999999, -.09388392]],
    ['shoulderL', [.36838141, .51002574, -.12452629]], ['shoulderR', [-.36643818, .50999796, -.12452624]],
  ].map(([name, point]) => { const b = model.getObjectByName(name), p = new THREE.Vector3(...point); return { b, y: p.y, local: b.worldToLocal(p) }; });
  const life = createEmiLife(model, { seed: 12 });
  for (let i = 0; i < 480; i++) {
    life.beforePose();
    life.update(1 / 60, { menu: true, selected: true, clip: 'idle_bob' });
    model.updateMatrixWorld(true);
    for (const p of probes) assert.ok(Math.abs(p.b.localToWorld(p.local.clone()).y - p.y) < 1e-7, p.b.name + ' contact height');
  }
  assert.ok(life.metrics.frames === 480);
  life.update(1 / 60, { motionOff: true }); model.updateMatrixWorld(true);
  for (const b of before) {
    assert.ok(b.b.position.distanceTo(b.p) < 1e-12, b.b.name + ' position drift');
    // Float32 source quaternions are not perfectly unit length; compare restored components directly.
    for (const key of ['x', 'y', 'z', 'w']) assert.equal(b.b.quaternion[key], b.q[key], b.b.name + ' rotation drift');
  }
  life.dispose(); life.dispose();
});
