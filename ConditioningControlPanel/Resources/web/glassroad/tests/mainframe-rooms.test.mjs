import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { sampleTrack, bodyClear } from '../render/colossiMath.js';
import { mediaLook } from '../render/mediaMath.js';
import { fitMainframeBillboard, motionEnvelope } from '../render/mainframeMediaMotion.js';
const hook = registerHooks({ resolve(s, c, next) {
  const files = { three: 'build/three.webgpu.min.js', 'three/webgpu': 'build/three.webgpu.min.js', 'three/tsl': 'build/three.tsl.min.js' };
  const path = files[s] || (s.startsWith('three/addons/') ? s.slice(6) : null);
  return path ? { url: new URL('../vendor/three/' + path, import.meta.url).href, shortCircuit: true } : next(s, c);
} });
const { buildMainframeRooms, mainframeRoomSites, mainframeRoomWindows, ROOM_CLEARANCE, roomScreenClear, buildVatOccupant } = await import('../render/mainframeRooms.js');
const { Group, Bone, Skeleton, SkinnedMesh, BufferGeometry, Float32BufferAttribute, Uint16BufferAttribute, MeshBasicNodeMaterial, Matrix4 } = await import('three/webgpu');
const { GLTFLoader } = await import('three/addons/loaders/GLTFLoader.js');
const { MeshoptDecoder } = await import('three/addons/libs/meshopt_decoder.module.js');
hook.deregister();
const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/mainframe.json', import.meta.url))));
const mediaLayout = JSON.parse(readFileSync(new URL('../tracks/mainframe.media.json', import.meta.url)));
const samples = sampleTrack(track, 2), M = mediaLook();
const faces = mediaLayout.billboards.map(b => fitMainframeBillboard(b, track, samples, M)).filter(Boolean).map(f => motionEnvelope(f, M));

test('five departments fit whole room bounds away from every road level and moving screen approach', () => {
  const sites = mainframeRoomSites(track, samples, faces, 'high');
  assert.equal(mainframeRoomWindows(track).length, 5);
  for (const kind of ['control', 'engine', 'containment', 'latex', 'cables']) assert.ok(sites.filter(s => s.kind === kind).length >= 4, kind);
  for (const site of sites) {
    assert.ok(bodyClear(site.capsule, samples, ROOM_CLEARANCE).ok, `${site.kind} route clearance`);
    assert.ok(roomScreenClear(site.capsule, faces, track), `${site.kind} screen clearance`);
  }
  assert.ok(mainframeRoomSites(track, samples, faces, 'phone').length < sites.length);
});

test('department machines are batched upfront, black tar has volume, pause holds all motion', () => {
  const rooms = buildMainframeRooms(track, null, { samples, mediaLayout });
  assert.ok(rooms.stats.parts > 1800);
  assert.ok(rooms.stats.batches < 35);
  const tar = rooms.group.getObjectByName('department-tube:latex');
  assert.ok(tar.count >= 4); assert.equal(tar.material.transparent, false);
  assert.ok(rooms.group.getObjectByName('department-ring:latex'));
  const rotor = rooms.rotors[0], before = new Matrix4(), after = new Matrix4();
  rotor.mesh.getMatrixAt(rotor.index, before); rooms.update(0.1); rotor.mesh.getMatrixAt(rotor.index, after);
  assert.notDeepEqual(after.elements, before.elements);
  const clock = rooms.clock.value; before.copy(after); rooms.update(0.1, { paused: true }); rotor.mesh.getMatrixAt(rotor.index, after);
  assert.equal(rooms.clock.value, clock); assert.deepEqual(after.elements, before.elements);
  rooms.update(0.1, { gentle: true }); assert.ok(Math.abs(rooms.clock.value - clock - 0.05) < 1e-8);
  rooms.dispose();
});

test('vat occupants retain source rider geometry and material map without keeping vehicle triangles', () => {
  const root = new Group(), kart = new Bone(), rider = new Bone(), head = new Bone(), cup = new Bone();
  kart.name = 'kart_root'; rider.name = 'rider_mount'; head.name = 'EMI_root'; cup.name = 'cup_body';
  root.add(kart); kart.add(rider, cup); rider.add(head);
  const geometry = new BufferGeometry();
  geometry.setAttribute('position', new Float32BufferAttribute([0, 1, 0, 1, 1, 0, 0, 2, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1], 3));
  geometry.setAttribute('skinIndex', new Uint16BufferAttribute([2,0,0,0, 2,0,0,0, 2,0,0,0, 3,0,0,0, 3,0,0,0, 3,0,0,0], 4));
  geometry.setAttribute('skinWeight', new Float32BufferAttribute(Array.from({length:6}, () => [1,0,0,0]).flat(), 4));
  geometry.setIndex([0,1,2,3,4,5]); geometry.computeVertexNormals();
  const material = new MeshBasicNodeMaterial(); material.name = 'emi_screen';
  const mesh = new SkinnedMesh(geometry, material); root.add(mesh); root.updateMatrixWorld(true);
  mesh.bind(new Skeleton([kart, rider, head, cup]));
  const before = [...geometry.index.array], occupant = buildVatOccupant({ model: () => ({ scene: root }) });
  assert.ok(occupant); assert.equal(occupant.group.children.length, 1);
  assert.deepEqual([...occupant.group.children[0].geometry.index.array], [0,1,2]);
  assert.deepEqual([...geometry.index.array], before, 'source asset is not edited');
  assert.equal(occupant.group.children[0].material.map, material.map);
  occupant.dispose(); geometry.dispose(); material.dispose();
});

test('the shipped EMI rig yields a complete body and original UV face in every containment vat', async () => {
  const source = readFileSync(new URL('../art/models/emi-teacup-kart.glb', import.meta.url));
  const oldLength = source.readUInt32LE(12), json = JSON.parse(source.subarray(20, 20 + oldLength));
  json.materials = json.materials.map(m => ({ name: m.name }));
  const bytes = Buffer.from(JSON.stringify(json)), padded = (bytes.length + 3) & ~3;
  const binary = source.subarray(20 + oldLength), glb = Buffer.alloc(20 + padded + binary.length, 0x20);
  source.copy(glb, 0, 0, 20); glb.writeUInt32LE(glb.length, 8); glb.writeUInt32LE(padded, 12);
  bytes.copy(glb, 20); binary.copy(glb, 20 + padded);
  const model = await new GLTFLoader().setMeshoptDecoder(MeshoptDecoder).parseAsync(glb.buffer.slice(glb.byteOffset, glb.byteOffset + glb.byteLength), '');
  const art = { model: () => model }, occupant = buildVatOccupant(art);
  assert.equal(occupant.group.children.length, 2, 'body and face survive extraction');
  const body = occupant.group.children.find(m => m.material.name === 'kart_surface');
  assert.ok(body.geometry.index.count > 4000, 'accepted head, limbs, shoes and antenna remain');
  const face = occupant.group.children.find(m => m.material.name === 'emi_screen');
  assert.ok(face.geometry.getAttribute('uv'));
  for (const mesh of occupant.group.children) {
    const pos = mesh.geometry.getAttribute('position'), ix = mesh.geometry.index;
    for (let j = 0; j < ix.count; j++) {
      const y = pos.getY(ix.getX(j)); assert.ok(y >= 0.31 && y < 1.2, 'rider keeps exact accepted scale and no saucer');
    }
  }
  const rooms = buildMainframeRooms(track, art, { samples, mediaLayout });
  assert.equal(rooms.occupants.length, rooms.stats.containment);
  rooms.dispose(); occupant.dispose();
});
