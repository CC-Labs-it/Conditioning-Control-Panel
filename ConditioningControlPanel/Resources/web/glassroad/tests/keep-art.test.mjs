import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { keepArtPlan, KEEP_REPLACES } from '../render/keepTheme.js';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { getKeepLayout } from '../track/keep-layout.js';

const manifest = JSON.parse(await readFile(new URL('../art/manifest.json', import.meta.url)));
const rows = new Map(manifest.map((r) => [r.id, r]));
const slots = [...Object.keys(KEEP_REPLACES), 'sky-dawn-galaxy', 'sky-afterglow-galaxy',
  'sky-dawn-stars', 'sky-afterglow-stars', 'chess-king-lod0', 'slot-cabinet-lod0', 'threshold-slot-row-lod0', 'emi-teacup-kart'];

test('Keep selects only phone replacements, aliases all sky states, and leaves Threshold props unloaded', () => {
  const plan = keepArtPlan(slots, 'phone');
  for (const id of plan.ids) assert.ok(rows.has(id), id);
  for (const [slot, replacement] of Object.entries(KEEP_REPLACES)) {
    assert.equal(plan.aliases[slot], replacement + '-phone');
    assert.ok(!plan.ids.includes(slot));
    assert.ok(!plan.ids.includes(replacement));
  }
  assert.equal(plan.aliases['sky-dawn-galaxy'], 'keep-sky-galaxy-phone');
  assert.equal(plan.aliases['sky-afterglow-stars'], 'sky-deep-stars');
  assert.ok(!plan.ids.some((id) => /chess|slot-cabinet|threshold-slot/.test(id)));
  assert.ok(plan.ids.includes('emi-teacup-kart'));
  assert.ok(plan.ids.includes('keep-colossus-chainpile-lod1'));
});

test('Keep replacement residency stays within the accepted phone budget and uses R masks', () => {
  const plan = keepArtPlan(slots, 'phone');
  const ids = Object.values(KEEP_REPLACES).map((id) => id + '-phone');
  assert.equal(ids.reduce((sum, id) => sum + rows.get(id).texBytes, 0), 3276800);
  for (const id of ids) {
    assert.equal(rows.get(id).colorSpace, 'linear');
    assert.ok(rows.get(id).mipLevels > 1);
    assert.ok(plan.ids.includes(id));
  }
});

test('Keep high and mid share desktop assets without loading a second texture tier', () => {
  for (const tier of ['high', 'mid']) {
    const plan = keepArtPlan(slots, tier);
    assert.ok(!plan.ids.some((id) => id.endsWith('-phone')));
    assert.ok(plan.ids.includes('keep-colossus-padlock-lod0'));
    assert.ok(plan.ids.includes('hazard-keep-footprint'));
  }
});

const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { buildKeepFortress } = await import('../render/keepFortress.js');
const { Matrix4, Vector3 } = await import('three/webgpu');
hooks.deregister();

test('Keep is solid architecture with instanced decks, walls, halls and a tower, clear of the racing surface', async () => {
  const track = loadTrack(JSON.parse(await readFile(new URL('../tracks/keep.json', import.meta.url))));
  const layout = getKeepLayout(track), fortress = buildKeepFortress(track, layout, null);
  assert.ok(fortress.counts.deck > 450);
  assert.ok(fortress.counts.wall > 900);
  assert.ok(fortress.group.getObjectByName('keep-tower-volume'));
  assert.ok(fortress.group.getObjectByName('keep-black-moat'));
  const solids = [], matrix = new Matrix4();
  fortress.group.updateMatrixWorld(true);
  fortress.group.traverse((o) => {
    if (!o.isMesh) return;
    assert.equal(o.material.transparent, false);
    for (let i = 0; i < (o.isInstancedMesh ? o.count : 1); i++) {
      if (o.isInstancedMesh) o.getMatrixAt(i, matrix); else matrix.identity();
      solids.push({ name: o.name, cylinder: o.geometry.type === 'CylinderGeometry', inverse: matrix.clone().premultiply(o.matrixWorld).invert() });
    }
  });
  const point = new Vector3();
  for (let s = 0; s < track.lengthM; s += 12) {
    const f = track.frameAt(s);
    for (const x of [0, -f.w / 2 + 0.7, f.w / 2 - 0.7]) for (const solid of solids) {
      point.fromArray(f.p).addScaledVector(new Vector3(...f.b), x).addScaledVector(new Vector3(...f.n), 1.4).applyMatrix4(solid.inverse);
      const inside = Math.abs(point.y) < 0.499 && (solid.cylinder ? Math.hypot(point.x, point.z) < 0.999 : Math.abs(point.x) < 0.499 && Math.abs(point.z) < 0.499);
      assert.ok(!inside, `${solid.name} blocks s=${s}, x=${x.toFixed(2)}`);
    }
  }
  fortress.dispose();
});
