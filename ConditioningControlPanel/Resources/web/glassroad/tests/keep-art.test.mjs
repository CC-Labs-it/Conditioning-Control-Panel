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
  assert.ok(fortress.plan.buildings.length >= 16, 'prison wings and halls');
  assert.ok(fortress.plan.drums.length >= 9, 'jail tower and separate guard turrets');
  assert.ok(fortress.plan.boxes.some((p) => p.name === 'cell-bars'));
  assert.ok(fortress.plan.boxes.some((p) => p.name === 'collapsed-wall'));
  assert.ok(fortress.group.getObjectByName('keep-tower-volume'));
  assert.equal(fortress.plan.mounts.length, 54);
  const solids = [], matrix = new Matrix4();
  fortress.group.updateMatrixWorld(true);
  fortress.group.traverse((o) => {
    if (!o.isMesh) return;
    assert.equal(o.material.transparent, false);
    for (let i = 0; i < (o.isInstancedMesh ? o.count : 1); i++) {
      if (o.isInstancedMesh) o.getMatrixAt(i, matrix); else matrix.identity();
      const world = matrix.clone().premultiply(o.matrixWorld), center = new Vector3().setFromMatrixPosition(world), scale = new Vector3().setFromMatrixScale(world);
      solids.push({ name: o.name, cylinder: o.geometry.type === 'CylinderGeometry', inverse: world.invert(), center, radius: scale.length() });
    }
  });
  const point = new Vector3();
  for (let s = 0; s < track.lengthM; s += 3) {
    const f = track.frameAt(s);
    for (const x of [0, -f.w / 2 + 0.7, f.w / 2 - 0.7]) for (const height of [0.03, 1.4]) for (const solid of solids) {
      point.fromArray(f.p).addScaledVector(new Vector3(...f.b), x).addScaledVector(new Vector3(...f.n), height);
      if (point.distanceTo(solid.center) > solid.radius) continue;
      point.applyMatrix4(solid.inverse);
      const inside = Math.abs(point.y) < 0.499 && (solid.cylinder ? Math.hypot(point.x, point.z) < 0.999 : Math.abs(point.x) < 0.499 && Math.abs(point.z) < 0.499);
      assert.ok(!inside, `${solid.name} blocks s=${s}, x=${x.toFixed(2)}`);
    }
  }
  // A failed crossing must still visibly fall into the moat, never onto hidden architectural decking.
  for (const [a, b] of [...track.jumps.map((j) => [j.s, j.landS]), ...track.fields.map((f) => [f.fromS, f.toS])]) {
    for (let s = a + 1; s < b - 1; s += 3) {
      const f = track.frameAt(s);
      for (const x of [-f.w / 2, 0, f.w / 2]) for (const depth of [1, 3, 8, 16]) for (const solid of solids) {
        point.fromArray(f.p).addScaledVector(new Vector3(...f.b), x).addScaledVector(new Vector3(...f.n), -depth);
        if (point.distanceTo(solid.center) > solid.radius) continue;
        point.applyMatrix4(solid.inverse);
        const inside = Math.abs(point.y) < .499 && (solid.cylinder ? Math.hypot(point.x, point.z) < .999 : Math.abs(point.x) < .499 && Math.abs(point.z) < .499);
        assert.ok(!inside, `${solid.name} fills crossing at ${s}`);
      }
    }
  }
  for (const edge of layout.brokenEdges) for (let s = edge.s0 + 6; s < edge.s1 - 6; s += 3) {
    const f = track.frameAt(s), side = edge.side === 'L' ? -1 : 1;
    if (f.flags.includes('rail') || f.flags.includes('rail' + edge.side)) continue;
    for (const solid of solids) {
      point.fromArray(f.p).addScaledVector(new Vector3(...f.b), side * (f.w / 2 + 1.3)).addScaledVector(new Vector3(...f.n), .7);
      if (point.distanceTo(solid.center) > solid.radius) continue;
      point.applyMatrix4(solid.inverse);
      assert.ok(!(Math.abs(point.y) < .499 && (solid.cylinder ? Math.hypot(point.x, point.z) < .999 : Math.abs(point.x) < .499 && Math.abs(point.z) < .499)), `${solid.name} seals broken edge at ${s}`);
    }
  }
  fortress.dispose();
});
