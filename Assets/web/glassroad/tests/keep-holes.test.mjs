import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { getKeepLayout } from '../track/keep-layout.js';
import { supportIntervals } from '../track/courts.js';
import { createCourtPaths } from '../sim/court-paths.js';
import { S } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { place, makeFeel, recorder, acts, DT } from './sim-fake.mjs';

const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/keep.json', import.meta.url), 'utf8')));
const holes = track.courts.flatMap(c => c.holes), layout = getKeepLayout(track);
const point = (s, x, h = 0) => {
  const f = track.frameAt(s);
  return f.p.map((v, i) => v + f.b[i] * x + f.n[i] * h);
};

test('Keep missing slabs are scattered, steerable and clear of traps and flight approaches', () => {
  assert.equal(holes.length, 9);
  assert.equal(new Set(holes.map(h => track.sectionAt(h.s))).size, 4);
  const paths = createCourtPaths(track);
  for (const court of track.courts) {
    const plan = paths.at(court.fromS), h = court.holes[0];
    assert.ok(plan.bands.some(b => b.hi - b.lo >= 3), 'A continuous kart-wide path survives each opening');
    for (const station of [...layout.gates, ...layout.chains])
      assert.ok(h.s + h.len < station.s - 45 || h.s > station.s + 45, 'Never combine a hole with a trap escape lane');
    for (const flight of track.aerials)
      assert.ok(h.s + h.len < flight.rampS0 - 20 || h.s > flight.landingS1 + 20);
    for (const route of track.routes)
      assert.ok(h.s + h.len < route.land.fromS - 20 || h.s > route.land.toS + 20);
    const s = h.s + h.len / 2;
    assert.equal(track.query(...point(s, h.x), track.frameAt(s).seg).onRoad, false);
    assert.ok(supportIntervals(track.courts, s, track.frameAt(s).w / 2, 1.25).some(([lo, hi]) => hi - lo >= 3));
    assert.ok(track.query(...point(s, h.x + h.w / 2 + 1), track.frameAt(s).seg).onRoad);
  }
});

test('Each Keep opening drops an unsupported kart into the ordinary rescue flow', () => {
  for (const hole of holes) {
    const state = place(track, { s: hole.s + hole.len / 2, lateral: hole.x, speed: 0 });
    const events = recorder(), feel = makeFeel();
    for (let i = 0; i < 180 && !events.all('drop').length; i++) step(state, acts(), track, feel, DT, events);
    assert.ok(events.all('drop').some(e => e.payload.cause === 'hole'), hole.id + ' enters the ordinary fall and rescue flow');
    assert.equal(state[S.grounded], 0);
  }
});

const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { buildKeepFortress } = await import('../render/keepFortress.js');
const { buildCourt } = await import('../render/court.js');
const { Matrix4, Vector3, BufferGeometry, BufferAttribute, Mesh, MeshBasicMaterial, DoubleSide, Raycaster } = await import('three/webgpu');
hooks.deregister();

test('Keep apertures stay visibly open through structural decking, foundations and chipped rims', t => {
  const fortress = buildKeepFortress(track, layout, null), court = buildCourt(track, null);
  t.after(() => { fortress.dispose(); court.dispose(); });
  const solids = [], matrix = new Matrix4(), position = new Vector3();
  for (const root of [fortress.group, court.group]) {
    root.updateMatrixWorld(true);
    root.traverse(mesh => {
      if (!mesh.isMesh) return;
      for (let i = 0; i < (mesh.isInstancedMesh ? mesh.count : 1); i++) {
        if (mesh.isInstancedMesh) mesh.getMatrixAt(i, matrix); else matrix.identity();
        const world = matrix.clone().premultiply(mesh.matrixWorld);
        solids.push({ name: mesh.name, centre: new Vector3().setFromMatrixPosition(world),
          radius: new Vector3().setFromMatrixScale(world).length(), inverse: world.invert(), cylinder: mesh.geometry.type === 'CylinderGeometry' });
      }
    });
  }
  const p = new Vector3();
  for (const h of holes) for (const ds of [1, h.len / 2, h.len - 1]) for (const dx of [-h.w / 2 + .7, 0, h.w / 2 - .7])
    for (const depth of [.1, 1, 2.8, 4.5]) {
      position.fromArray(point(h.s + ds, h.x + dx, -depth));
      for (const solid of solids) {
        if (position.distanceTo(solid.centre) > solid.radius) continue;
        p.copy(position).applyMatrix4(solid.inverse);
        const inside = Math.abs(p.y) < .499 && (solid.cylinder ? Math.hypot(p.x, p.z) < .999 : Math.abs(p.x) < .499 && Math.abs(p.z) < .499);
        assert.ok(!inside, solid.name + ' fills ' + h.id + ' at depth ' + depth);
      }
    }
});

test('Keep rendered road top and underside leave the same nine openings as collision', t => {
  const data = track.meshes.road, geometry = new BufferGeometry();
  geometry.setAttribute('position', new BufferAttribute(data.positions, 3));
  geometry.setIndex(new BufferAttribute(data.indices, 1));
  const mesh = new Mesh(geometry, new MeshBasicMaterial({ side: DoubleSide }));
  t.after(() => { geometry.dispose(); mesh.material.dispose(); });
  for (const h of holes) for (const ds of [1, h.len / 2, h.len - 1]) for (const dx of [-h.w / 2 + .6, 0, h.w / 2 - .6]) {
    const s = h.s + ds, frame = track.frameAt(s), origin = new Vector3(...point(s, h.x + dx, 2));
    const ray = new Raycaster(origin, new Vector3(...frame.n).negate(), 0, 8);
    assert.equal(ray.intersectObject(mesh).length, 0, h.id + ' has no rendered cap across the missing slab');
  }
});
