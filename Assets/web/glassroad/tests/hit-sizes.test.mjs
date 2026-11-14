// Owner 2026-11-01 (ccp-bugs #1538): a hazard hits with what it draws. Each contact shape is measured against the art here.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { MeshoptDecoder } from '../vendor/three/addons/libs/meshopt_decoder.module.js';
import { CHESS_TRAP_IDS } from '../render/chessTrapIds.js';
import { CHESS_UNIT_M, THUMPER_CONTACT, sideHeadContact } from '../track/chessContact.js';
import { HAZARD_KART, roundReach, ellipseSweep, firstContact } from '../sim/hitShapes.js';
import { KEEP_CHAIN, createKeepChains } from '../sim/keepChains.js';
import { createBoudoirHazards } from '../sim/boudoirHazards.js';
import { getBoudoirSetpieces, powderPressHeads, mascaraPhase } from '../track/boudoir-setpieces.js';
import { loadTrack } from '../track/load.js';
import { fakeTrack, place, makeFeel, recorder } from './sim-fake.mjs';

const read = (path) => JSON.parse(readFileSync(new URL(path, import.meta.url)));
const COURSES = ['threshold', 'mainframe', 'keep', 'boudoir', 'goonworks'];
const racer = (st) => ({ i: 0, st, events: recorder(), feel: makeFeel() });

await MeshoptDecoder.ready;
/** Every vertex of a GLB in model space (node translation + scale, the only transforms these models carry). */
function glbPoints(id) {
  const b = readFileSync(new URL('../art/models/' + id + '.glb', import.meta.url));
  const jsonLen = b.readUInt32LE(12), j = JSON.parse(b.subarray(20, 20 + jsonLen).toString()), bin = b.subarray(20 + jsonLen + 8), pts = [];
  for (const node of j.nodes) {
    if (node.mesh === undefined) continue;
    const acc = j.accessors[j.meshes[node.mesh].primitives[0].attributes.POSITION], ext = j.bufferViews[acc.bufferView].extensions.EXT_meshopt_compression;
    const out = new Uint8Array(ext.count * ext.byteStride), view = new DataView(out.buffer), off = acc.byteOffset || 0;
    MeshoptDecoder.decodeGltfBuffer(out, ext.count, ext.byteStride, bin.subarray(ext.byteOffset, ext.byteOffset + ext.byteLength), 'ATTRIBUTES', 'NONE');
    const t = node.translation || [0, 0, 0], k = node.scale || [1, 1, 1];
    for (let i = 0; i < acc.count; i++) pts.push([0, 1, 2].map(a => view.getInt16(off + i * ext.byteStride + a * 2, true) / 32767 * k[a] + t[a]));
  }
  return pts;
}
const span = (pts, a) => { let lo = Infinity, hi = -Infinity; for (const p of pts) { lo = Math.min(lo, p[a]); hi = Math.max(hi, p[a]); } return { lo, hi, size: hi - lo, mid: (lo + hi) / 2 }; };

test('the hazard kart is the live kart', () => {
  const feel = read('../feel.json');
  assert.equal(HAZARD_KART.radiusM, feel.contact.radiusM);
  assert.equal(HAZARD_KART.heightM, feel.contact.maxHeightM);
});

test('the chess numbers match the GLBs as render/chessTrap.js and render/boudoirHazards.js draw them', () => {
  CHESS_TRAP_IDS.forEach((id, lane) => {
    const pts = glbPoints(id), X = span(pts, 0), Y = span(pts, 1), Z = span(pts, 2);
    [X, Y, Z].forEach((axis, a) => assert.ok(Math.abs(axis.size - CHESS_UNIT_M[lane][a]) < .01, id + ' unit size'));
    // Thumper: fit to 4 x 5.5 x 4 at .92, stretched 1.15 / 1.35 / 1.15, flipped crown down. Measure the lowest kart height.
    const k = Math.min(4 / X.size, 5.5 / Y.size, 4 / Z.size) * .92;
    let hx = 0, hz = 0;
    for (const p of pts) {
      if ((Y.hi - p[1]) * k * 1.35 >= HAZARD_KART.heightM) continue;
      hx = Math.max(hx, Math.abs(p[0] - X.mid) * k * 1.15); hz = Math.max(hz, Math.abs(p[2] - Z.mid) * k * 1.15);
    }
    assert.ok(THUMPER_CONTACT[lane].x <= hx && hx - THUMPER_CONTACT[lane].x < .02, id + ' thumper x ' + hx);
    assert.ok(THUMPER_CONTACT[lane].z <= hz && hz - THUMPER_CONTACT[lane].z < .02, id + ' thumper z ' + hz);
  });
});

test('no moving head on any course hits wider than the piece it draws', () => {
  for (const id of COURSES) {
    const track = loadTrack(read('../tracks/' + id + '.json'), { moving: true });
    track.moving.traps.forEach((row, index) => {
      const art = sideHeadContact(index, row.w, row.len), scale = row.hitboxScale ?? 1;
      assert.ok(row.contactW <= art.x * 2 + 1e-6 && row.contactW <= row.w * scale + 1e-6, row.id + ' width');
      assert.ok(row.contactLen <= art.z * 2 + 1e-6 && row.contactLen <= row.len * scale + 1e-6, row.id + ' length');
    });
  }
});

test('shape helpers: a round body thins with height, an ellipse is entered once, a kart already inside can leave', () => {
  assert.equal(roundReach(2, 5, 0, 1), -1, 'clear above');
  assert.equal(roundReach(2, .5, 0, 1), 2, 'the widest part is level with the kart');
  assert.ok(Math.abs(roundReach(2, 2, 0, 1) - Math.sqrt(3)) < 1e-9);
  assert.ok(Math.abs(ellipseSweep([-5, 0], [5, 0], 2, 1) - .3) < 1e-9);
  assert.equal(ellipseSweep([-5, 3], [5, 3], 2, 1), null, 'misses');
  assert.equal(ellipseSweep([0, 0], [5, 0], 2, 1), null, 'already inside');
  assert.ok(Math.abs(firstContact(f => f > .5, 4) - .5) < .02);
  assert.equal(firstContact(() => true, 4), null);
});

test('a thumper hits under its drawn tip and misses inside the old 4 m box', () => {
  const track = fakeTrack({ w: 36 }), source = getBoudoirSetpieces(loadTrack(read('../tracks/boudoir.json'), { moving: true })).presses[0];
  const station = { ...source, s: 110, pressHeads: powderPressHeads(source).map(h => ({ ...h, s: 110 })) };
  powderPressHeads(station).forEach((head, lane) => {
    const down = (3.5 + lane * 6) * .625, run = lateral => {
      const h = createBoudoirHazards(track, { presses: [station], sweepers: [], conveyors: [], mirrorGates: [] });
      return h.apply(racer(place(track, { s: 119, lateral, speed: 40 })), place(track, { s: 101, lateral, speed: 40 }), down);
    };
    assert.ok(run(head.x), head.id + ' under the tip');
    assert.equal(run(head.x + THUMPER_CONTACT[lane].x + HAZARD_KART.radiusM + .05), null, head.id + ' just beside it');
    assert.equal(run(head.x + 2.65), null, head.id + ' was a hit in the old box');
  });
});

test('the Keep ball hits by its slice at kart height, not by a square around it', () => {
  const track = fakeTrack({ w: 36 }), row = { id: 'swing', s: 110, x: 0 };
  const run = (lateral, height = 0) => createKeepChains(track, { chains: [row] })
    .apply(racer(place(track, { s: 115, lateral, height, speed: 40 })), place(track, { s: 105, lateral, height, speed: 40 }), .01);
  const low = KEEP_CHAIN.pivotM - KEEP_CHAIN.lengthM, reach = roundReach(KEEP_CHAIN.radiusM, low, 0);
  assert.ok(run(0), 'centre');
  assert.ok(run(reach + HAZARD_KART.radiusM - .15), 'the edge of the slice');
  assert.equal(run(reach + HAZARD_KART.radiusM + .15), null, 'just beside the slice');
  assert.equal(run(3.9), null, 'inside the old 4.05 m square');
});

test('a mascara brush hits by the bar it draws and clears a kart past its reach', () => {
  const track = fakeTrack({ w: 36 });
  const head = { id: 'h', stationId: 'st', kind: 'sweeper', s: 110, x: 0, longitudinalM: 0, radiusM: 1.12, depthM: 2.5, armM: 8.6, pivotY: 10.25, amplitudeM: 2.9, cycleS: 6, offsetS: 0 };
  const station = { id: 'st', kind: 'sweeper', s: 110, x: 0, w: 17.2, depthM: 8.5, heads: [head] };
  const p = mascaraPhase(head, .01), reach = roundReach(1.12, p.y, 0);
  const run = lateral => createBoudoirHazards(track, { presses: [], sweepers: [station], conveyors: [], mirrorGates: [] })
    .apply(racer(place(track, { s: 115, lateral, speed: 40 })), place(track, { s: 105, lateral, speed: 40 }), .01);
  assert.ok(run(p.x), 'under the bar');
  assert.equal(run(p.x + reach + HAZARD_KART.radiusM + .15), null, 'past it');
});
