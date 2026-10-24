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
  for (const mesh of water.object.children) for (const attribute of Object.values(mesh.geometry.attributes)) assert.ok(attribute.array.every(Number.isFinite));
});

test('water volume has shallow raised surface, visible depth and curtains falling in world gravity', t => {
  const tr = track(); tr.waterSpills = [{ fromS: 10, toS: 50, side: -1, dropM: 14 }];
  const water = buildWaterVolume(tr, pal); t.after(() => water.dispose());
  assert.deepEqual(water.runs, [{ fromS: 0, toS: 40 }]);
  assert.deepEqual(water.spills, [{ fromS: 10, toS: 40, side: -1, dropM: 14 }]);
  const positions = name => water.object.getObjectByName(name).geometry.attributes.position;
  const top = positions('water-surface-raised'), side = positions('water-visible-depth'), fall = positions('water-spills-into-void');
  for (let i = 0; i < top.count; i++) assert.ok(Math.abs(top.getY(i) - 5.1) < 0.00001);
  assert.ok(WATER_VOLUME.liftM + WATER_VOLUME.waveM <= 0.15);
  assert.ok(Math.abs(Math.min(...Array.from({ length: side.count }, (_, i) => side.getY(i))) - 2.7) < 0.00001);
  assert.ok(Math.min(...Array.from({ length: fall.count }, (_, i) => fall.getY(i))) < -8.8);
  water.update(1); assert.equal(water.uniforms.t.value, 1);
  water.update(1, { paused: true }); assert.equal(water.uniforms.t.value, 1);
  water.update(1, { gentle: true }); assert.equal(water.uniforms.t.value, 1.5);
  assert.equal(water.object.children.length, 3, 'all material paths exist before warm-up');
  for (const mesh of water.object.children) {
    assert.ok(mesh.renderOrder > 0 && mesh.renderOrder < 1, 'water composites after road but before wake rings and spray');
    assert.equal(mesh.material.depthWrite, false, 'transparent water cannot occlude pooled spray');
  }
});

test('water respects canal bank height, dry stretches and separately authored reservoir waterlines', () => {
  const tr = track(); tr.canalProfile = (s, x) => ({ height: x * x * 0.1 });
  assert.ok(waterHalfWidth(tr, 10) > 0.99 && waterHalfWidth(tr, 10) < 1.01);
  assert.equal(waterSpills(tr).length, 0, 'contained water cannot spill through a high canal bank');
  tr.sections = [{ id: 'submerged', fromS: 0, toS: 20 }, { id: 'emerge', fromS: 20, toS: 40 }];
  assert.deepEqual(waterRuns(tr), []);
});

test('pooled water spray follows curved contacts and is silent in the air, during respawn and pause', t => {
  const tr = track(), bursts = [], rings = [];
  tr.surfaceAt = (s, x) => ({ ...tr.frameAt(s), p: [x, 5 + x * x * 0.1, -s], n: [-0.2 * x, 1, 0] });
  const water = createWaterfall(tr, pal, { particles: { burst: (...p) => bursts.push(p) }, wakes: { ring: (...p) => rings.push(p) } });
  t.after(() => water.dispose());
  const st = createState(); st[S.s] = 10; st[S.lateral] = 3; st[S.velZ] = -12; st[S.grounded] = 1; st[S.driftDir] = 1;
  water.kart(0, st, 0.25); assert.equal(bursts.length, 2); assert.equal(rings.length, 1);
  assert.ok(bursts.every(b => b[0][1] > 5.2), 'spray is on the bank, not the road centre plane');
  const drift = bursts[0]; st[S.driftDir] = 0; water.kart(0, st, 0.1);
  const straight = bursts.at(-1);
  assert.ok(straight[1] >= 3 && straight[2].sizeM >= 0.18 && straight[2].up >= 1.3, 'straight-running spray remains visible');
  assert.ok(drift[1] > straight[1] && drift[2].sizeM > straight[2].sizeM, 'drift spray retains its stronger hierarchy');
  const before = bursts.length;
  water.kart(0, st, 0.25, { paused: true });
  st[S.airborne] = 1; water.kart(0, st, 0.25); st[S.airborne] = 0;
  st[S.respawnPhase] = 1; water.kart(0, st, 0.25);
  assert.equal(bursts.length, before);
  water.splash({ s: 10, x: 3, p: [100, 30, 20], normal: [0, 1, 0], down: 7 });
  assert.deepEqual(bursts.at(-1)[0], [100, 30.22, 20], 'real simulation contact wins over reconstructed frame');
});
