import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { getBoudoirSetpieces, powderPressPhase } from '../track/boudoir-setpieces.js';
import { trapArrangement } from '../track/trapArrangement.js';
import { createBoudoirHazards } from '../sim/boudoirHazards.js';
import { fakeTrack, place, makeFeel, recorder } from './sim-fake.mjs';
import { onlineCourseHash, courseHash } from '../net/courseHash.js';
import { AERIAL_REVISION } from '../track/aerial.js';
import { BOOST_SAFETY_REVISION } from '../track/boostSafety.js';
const json = JSON.parse(readFileSync(new URL('../tracks/boudoir.json', import.meta.url)));
const course = loadTrack(json, { moving: true }), stations = getBoudoirSetpieces(course).presses;

test('three chess lanes remain passable through every musical stroke, tempo, seek and fallback', () => {
  for (const bpm of [40, 96.75, 128, 240]) for (const arranged of [false, true]) {
    const arrangement = arranged ? trapArrangement({ bpm, durationS: 90, sections: [{ startS: 0, mood: 'drop', energy: .9 }] }, 'boudoir') : null;
    for (const station of stations) {
      const hits = new Set();
      assert.equal(station.pressHeads.length, 3);
      for (let beat = -4; beat < 120; beat += .08) {
        const frame = { beat, songBeat: beat, beatS: 60 / bpm, safe: beat < 0, epochBeat: 0, arrangement };
        const poses = station.pressHeads.map(row => powderPressPhase(row, 999, frame));
        assert.ok(poses.filter(p => p.liftM >= 8.39).length >= 2, 'two fully lifted lanes throughout the sequence');
        poses.forEach((p, lane) => { if (p.blocking) hits.add(lane); });
        for (const row of station.pressHeads) {
          assert.equal(powderPressPhase(row, 999, { ...frame, safe: true, rearming: true }).blocking, false);
          assert.ok(row.w > 1.4 + 2, 'lane is wider than a kart plus steering margin');
        }
      }
      assert.equal(hits.size, 3, 'every piece takes a turn');
    }
  }
  for (let t = 0; t < 60; t += .04)
    assert.ok(stations[0].pressHeads.filter(row => powderPressPhase(row, t).liftM > 8).length >= 2);
});

test('swept kart contact follows the lowered chess lane and passes raised neighbours', () => {
  const track = fakeTrack({ w: 36 });
  const station = { ...stations[0], s: 110, pressHeads: stations[0].pressHeads.map(h => ({ ...h, s: 110 })) };
  for (const lane of [0, 1, 2]) {
    const t = (3.5 + lane * 6) * .625;
    for (const head of station.pressHeads) {
      const h = createBoudoirHazards(track, { presses: [station], sweepers: [], conveyors: [], mirrorGates: [] });
      const r = { i: 0, st: place(track, { s: 119, lateral: head.x, speed: 40 }), events: recorder(), feel: makeFeel() };
      const prev = place(track, { s: 101, lateral: head.x, speed: 40 });
      assert.equal(!!h.apply(r, prev, t), head.pressLane === lane);
    }
  }
});

test('Boudoir admission rejects old powder-press physics', async () => {
  const old = { aerialRevision: AERIAL_REVISION, aerials: course.aerials, moving: course.moving,
    boostSafetyRevision: BOOST_SAFETY_REVISION, boosts: course.boosts };
  assert.notEqual(await onlineCourseHash(json, {}, course), await courseHash(json, {}, old));
});

test('real approved chess art fits trap envelopes, retains paint, pauses and releases only owned resources', async () => {
  const hooks = registerHooks({ resolve(id, ctx, next) {
    const path = id === 'three' ? 'build/three.webgpu.min.js' : id === 'three/webgpu' ? 'build/three.webgpu.min.js'
      : id === 'three/tsl' ? 'build/three.tsl.min.js' : id.startsWith('three/addons/') ? id.slice(6) : null;
    return path ? { url: new URL('../vendor/three/' + path, import.meta.url).href, shortCircuit: true } : next(id, ctx);
  } });
  try {
    const { Group, Box3, Vector3 } = await import('three/webgpu');
    const { GLTFLoader } = await import('three/addons/loaders/GLTFLoader.js');
    const { MeshoptDecoder } = await import('three/addons/libs/meshopt_decoder.module.js');
    const { CHESS_TRAP_IDS } = await import('../render/chessTrapIds.js');
    const { createBoudoirHazardView } = await import('../render/boudoirHazards.js');
    const { createMovingHazardView } = await import('../render/movingHazards.js');
    const { chessTrap, chessWobble } = await import('../render/chessTrap.js');
    const loader = new GLTFLoader().setMeshoptDecoder(MeshoptDecoder), models = new Map();
    for (const id of CHESS_TRAP_IDS) {
      const bytes = readFileSync(new URL('../art/models/' + id + '.glb', import.meta.url));
      models.set(id, await loader.parseAsync(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength), ''));
    }
    const art = { model: id => models.get(id) }, sourceMaterials = new Set(); let disposed = 0;
    for (const model of models.values()) model.scene.traverse(o => { if (o.isMesh) {
      sourceMaterials.add(o.material); o.material.addEventListener('dispose', () => disposed++);
      o.geometry.addEventListener('dispose', () => disposed++);
    } });
    for (const side of [-1, 0, 1]) for (const i of [0, 1, 2]) {
      const size = side ? [8, 3.2, 5.6] : [4, 5.5, 4], sculpture = chessTrap(art, i, size, side);
      for (let age = 0; age < .65; age += .03) {
        sculpture.rotation.x = chessWobble(age);
        const bounds = new Box3().setFromObject(sculpture);
        assert.ok(bounds.min.x >= -size[0] / 2 && bounds.max.x <= size[0] / 2);
        assert.ok(bounds.min.y >= 0 && bounds.max.y <= size[1]);
        assert.ok(bounds.min.z >= -size[2] / 2 && bounds.max.z <= size[2] / 2);
      }
      sculpture.traverse(o => { if (o.isMesh) assert.ok(sourceMaterials.has(o.material)); });
    }
    const scene = new Group(), press = createBoudoirHazardView(scene, course, art);
    const side = createMovingHazardView(scene, course, {}, art);
    assert.equal(press.entries.flatMap(e => e.pressHeads || []).filter(h => h.sculpture).length, stations.length * 3);
    assert.equal(side.group.getObjectsByProperty('name', 'chess-trap-sculpture').length, 6);
    const head = press.entries[0].pressHeads[0];
    for (const entry of press.entries.filter(e => e.pressHeads)) for (const item of entry.pressHeads) {
      const body = item.sculpture.children[0];
      assert.ok(Math.cos(body.rotation.z) < -.99, 'Original base attaches above the downward-facing crown');
      const original = chessTrap(art, item.row.pressLane, [item.row.w,item.row.heightM,item.row.depthM]);
      const legacy = new Box3().setFromObject(original).getSize(new Vector3());
      const bounds = new Box3().setFromObject(item.sculpture), size = bounds.getSize(new Vector3());
      assert.ok(size.y > legacy.y * 1.34 && size.x > legacy.x * 1.14);
      assert.ok(Math.abs(bounds.min.y) < 1e-6 && Math.abs(bounds.max.y - item.visualHeight) < 1e-6,
        'Tip touches the contact plane and shaft begins at the actual inverted base');
      assert.ok(size.x < 5, 'Adjacent lanes retain a visible gap');
    }
    press.update(3 * .625 + .05);
    assert.notEqual(head.sculpture.rotation.z, 0);
    const before = head.moving.position.y, wobble = head.sculpture.rotation.z;
    press.update(6, { paused: true }); assert.equal(head.moving.position.y, before); assert.equal(head.sculpture.rotation.z, wobble);
    press.update(3 * .625 + .05, { gentle: true }); assert.equal(head.sculpture.rotation.z, 0);
    press.dispose(); side.dispose(); assert.equal(disposed, 0); assert.equal(scene.children.length, 0);
  } finally { hooks.deregister(); }
});
