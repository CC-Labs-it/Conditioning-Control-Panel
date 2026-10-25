import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { waterRuns, waterSpills, waterHalfWidth, WATER_VOLUME } from '../render/waterMath.js';
import { createState, S } from '../sim/state.js';
const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three': 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { vec3 } = await import('three/tsl');
const { buildWaterVolume } = await import('../render/waterVolume.js');
const { createWaterfall } = await import('../render/waterfall.js');
hooks.deregister();
const pal = { col: () => vec3(0.2, 0.5, 0.7) };
const track = () => ({ lengthM: 60, nodeS: [0, 20, 40, 60], sections: [],
  frameAt(s) { return { p: [0, 5, -s], b: [1, 0, 0], n: [0, 1, 0], t: [0, 0, -1], w: 12, surface: s < 40 ? 'stream' : 'glass', flags: [] }; } });

test('Threshold inherits real water depth and spilling without new track metadata', t => {
  const tr = loadTrack(JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8')));
  const water = buildWaterVolume(tr, pal); t.after(() => water.dispose());
  assert.ok(water.stats.runs >= 2 && water.stats.spills >= 2);
  water.object.traverse(mesh => { if (mesh.geometry) for (const attribute of Object.values(mesh.geometry.attributes)) assert.ok(attribute.array.every(Number.isFinite)); });
});

test('water volume has shallow raised surface, visible depth and curtains falling in world gravity', t => {
  const tr = track(); tr.waterSpills = [{ fromS: 10, toS: 50, side: -1, dropM: 14 }];
  const water = buildWaterVolume(tr, pal); t.after(() => water.dispose());
  assert.deepEqual(water.runs, [{ fromS: 0, toS: 40 }]);
  assert.deepEqual(water.spills, [{ fromS: 10, toS: 40, side: -1, dropM: 14 }]);
  const positions = name => water.object.getObjectByName(name).geometry.attributes.position;
  const top = positions('water-surface-raised'), side = positions('water-visible-depth'), fall = positions('water-spills-into-void');
  for (let i = 0; i < top.count; i++) assert.ok(top.getY(i) >= 5.007 && top.getY(i) <= 5.101);
  assert.ok(Array.from({ length: top.count }, (_, i) => top.getY(i)).some(y => Math.abs(y - 5.1) < .00001), 'full liquid height stays in the body');
  assert.ok(WATER_VOLUME.liftM + WATER_VOLUME.waveM <= 0.15);
  assert.ok(Math.abs(Math.min(...Array.from({ length: side.count }, (_, i) => side.getY(i))) - (5.1 - WATER_VOLUME.depthM)) < 0.00001);
  for (let i = 0; i < side.count; i++) if (Math.abs(side.getZ(i)) < .001 || Math.abs(side.getZ(i) + 40) < .001)
    assert.ok(side.getY(i) > 5, 'wall base collapses into the shallow shoreline instead of leaving a deep end seam');
  assert.ok(Math.min(...Array.from({ length: fall.count }, (_, i) => fall.getY(i))) < -8.8);
  water.update(1); assert.equal(water.uniforms.t.value, 1);
  water.update(1, { paused: true }); assert.equal(water.uniforms.t.value, 1);
  water.update(1, { gentle: true }); assert.equal(water.uniforms.t.value, 1.5);
  assert.equal(water.object.children.length, 5, 'surface, depth, curtains, dimensional foam and drain dressing exist before warm-up');
  for (const mesh of water.object.children) {
    if (!mesh.isMesh) continue;
    assert.ok(mesh.renderOrder > 0 && mesh.renderOrder < 1, 'water composites after road but before wake rings and spray');
    assert.equal(mesh.material.depthWrite, mesh.name === 'water-surface-raised' || mesh.name === 'water-dimensional-foam',
      'reactive skin rejects later submerged shadow catchers; translucent walls and curtains do not write depth');
  }
});

test('water respects canal bank height, dry stretches and separately authored reservoir waterlines', () => {
  const tr = track(); tr.canalProfile = (s, x) => ({ height: x * x * 0.1 });
  assert.ok(waterHalfWidth(tr, 10) > 0.99 && waterHalfWidth(tr, 10) < 1.01);
  assert.equal(waterSpills(tr).length, 0, 'contained water cannot spill through a high canal bank');
  tr.sections = [{ id: 'submerged', fromS: 0, toS: 20 }, { id: 'emerge', fromS: 20, toS: 40 }];
  assert.deepEqual(waterRuns(tr), []);
});

test('pooled physical water spray follows curved contacts and is silent in the air, during respawn and pause', t => {
  const tr = track(), bursts = [];
  tr.surfaceAt = (s, x) => ({ ...tr.frameAt(s), p: [x, 5 + x * x * 0.1, -s], n: [-0.2 * x, 1, 0] });
  const water = createWaterfall(tr, pal, { particles: { burst: (...p) => bursts.push(p) } });
  t.after(() => water.dispose());
  const st = createState(); st[S.s] = 10; st[S.lateral] = 3; st[S.velZ] = -12; st[S.grounded] = 1; st[S.driftDir] = 1;
  water.kart(0, st, 0.25); assert.equal(bursts.length, 0, 'water never reuses the illustrated teardrop atlas');
  assert.equal(water.spray.stats().emitted,32);
  assert.ok(water.spray.data[1] > 5.2, 'spray is on the bank, not the road centre plane');
  const drift = water.spray.stats().emitted; st[S.driftDir] = 0; water.kart(0, st, 0.2);
  const straight = water.spray.stats().emitted-drift;
  assert.ok(straight>=6&&straight<drift, 'straight-running spray stays visible while drift sheds more fluid');
  const before = water.spray.stats().emitted;
  water.kart(0, st, 0.25, { paused: true });
  st[S.airborne] = 1; water.kart(0, st, 0.25); st[S.airborne] = 0;
  st[S.respawnPhase] = 1; water.kart(0, st, 0.25);
  assert.equal(water.spray.stats().emitted, before);
  water.splash({ s: 10, x: 3, p: [100, 30, 20], normal: [0, 1, 0], down: 7 });
  assert.ok(Math.abs(water.spray.data[before*12+1]-30.12)<.001, 'real simulation contact wins over reconstructed frame');
});

test('spill curtain is folded in depth with separated layers rather than one flat sheet', t => {
  const tr = track(); tr.waterSpills = [{ fromS: 10, toS: 30, side: -1, dropM: 14 }];
  const water = buildWaterVolume(tr, pal); t.after(() => water.dispose());
  const fall = water.object.getObjectByName('water-spills-into-void');
  const p = fall.geometry.attributes.position;
  const xs = Array.from({ length: p.count }, (_, i) => p.getX(i));
  const ys = new Set(Array.from({ length: p.count }, (_, i) => p.getY(i).toFixed(2)));
  assert.ok(Math.max(...xs) - Math.min(...xs) > 2, 'fall bows outward with real parallax');
  assert.ok(ys.size >= 12, 'subdivided fall has depth steps for a moving silhouette');
  assert.ok(fall.material.positionNode, 'cascade motion displaces geometry, not only its texture');
  assert.ok(p.count < 5000, 'a spill remains a small shared mesh');
});
