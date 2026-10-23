import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { createFeel } from '../core/feel.js';
import { createBus } from '../core/events.js';
import { createState } from '../sim/state.js';
import { createFx } from '../fx/reactions.js';
import { FX_CUES, STONE_CUES } from '../fx/cues.js';
import { createPhotosafe } from '../fx/photosafe.js';
import { createGentle } from '../fx/gentle.js';

// Use the shipped renderer classes, without a DOM, adapter or GPU context (the render-court pattern).
const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true }
    : next(specifier, context);
} });
const { stoneLayout, buildStones, STONE_KIT } = await import('../render/stones.js');
const { Group, Mesh, BoxGeometry, MeshStandardMaterial } = await import('three/webgpu');
hooks.deregister();

const json = JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8'));
const WANT = { lanes: [-7, 0, 7], stoneM: 5, gapM: 4, staggerM: 3, fromU: 0.40, toU: 0.71 };

/**
 * SHIM (RENDER lane, BRIEF-STONES): until TRACK's generator writes `fields` into tracks/threshold.json, build the
 * bridge field here from the loaded track: the bridge's old open-rail fall region (nodes flagged `open` after the
 * bridge pad) starts at u 0.40 of the 162 m bridge, so the field runs from that node over about 50 m (u 0.71):
 * three lanes at -7 / 0 / +7, 5 m stones, 4 m gaps, 3 m stagger, the last stone 2 m before the deck resumes.
 * A loaded track that already carries fields is used as is.
 */
function withFields(track) {
  if (track.fields && track.fields.length) return track;
  const open = json.nodes.findIndex((n, i) => i > 30 && (n.flags || []).includes('open'));
  assert.ok(open > 0, 'the bridge fall region marks the field start');
  const from = open, fromS = track.nodeS[from];
  const lengthM = 162 * (WANT.toU - WANT.fromU);
  let to = from; while (track.nodeS[to] < fromS + lengthM) to++;
  const toS = track.nodeS[to], stones = [];
  WANT.lanes.forEach((x, li) => {
    for (let s = fromS + li * WANT.staggerM, k = 0; s + WANT.stoneM <= toS - 2; s += WANT.stoneM + WANT.gapM, k++) {
      let node = from; while (node + 1 < to && track.nodeS[node + 1] <= s) node++;
      stones.push({ id: 'bridge-stones:' + li + ':' + k, node, ds: s - track.nodeS[node], x, w: WANT.stoneM, len: WANT.stoneM, s });
    }
  });
  // the generator eases the road to 21 m over the field (BRIEF-STONES); the shim widens the frame there
  const frameAt = (s, out) => { const f = track.frameAt(s, out); if (s >= fromS && s <= toS) f.w = Math.max(f.w, 21); return f; };
  return { ...track, frameAt, fields: [{ id: 'bridge-stones', from, to, fromS, toS, lanes: WANT.lanes.slice(), stones }] };
}

const track = withFields(loadTrack(json));
const field = track.fields[0], rows = stoneLayout(track);

test('stone layout: one row per 5 m tile of every stone, the root 0.55 under the deck along the normal, inside the road', () => {
  // a lead or closing stone longer than the model is covered by evenly spaced tiles squeezed along the road
  const tiles = field.stones.reduce((n, st) => n + Math.max(1, Math.round(st.len / STONE_KIT.slab[2])), 0);
  assert.equal(rows.length, tiles);
  assert.equal(new Set(rows.map((r) => r.stone)).size, field.stones.length, 'every stone has a tile');
  for (const r of rows) assert.ok(r.scaleT > 0.7 && r.scaleT < 1.2, r.id + ' tile scale ' + r.scaleT);
  assert.ok(rows.length >= 13, 'tiles ' + rows.length);
  for (const r of rows) {
    const f = track.frameAt(r.s);
    const d = r.p.map((v, i) => v - f.p[i]);
    const along = d[0] * f.n[0] + d[1] * f.n[1] + d[2] * f.n[2];
    const across = d[0] * f.b[0] + d[1] * f.b[1] + d[2] * f.b[2];
    assert.ok(Math.abs(along + STONE_KIT.deckM) < 1e-9, r.id + ' root ' + along);
    assert.ok(Math.abs(across - r.x) < 1e-9, r.id + ' lateral');
    assert.ok(Math.abs(r.x) + r.w / 2 <= f.w / 2 + 1e-9, r.id + ' outside the road');
    assert.ok(r.s - r.len / 2 >= field.fromS - 1e-9 && r.s + r.len / 2 <= field.toS + 1e-9, r.id + ' outside the field');
  }
  // every s of the field up to the last stone has a stone in at least one lane (the stagger: never a wall; the
  // generator's own check covers the hand-over back to the deck)
  const lastEnd = Math.max(...rows.map((r) => r.s + r.len / 2));
  for (let s = field.fromS + 0.5; s < lastEnd - 0.5; s += 0.5) {
    assert.ok(rows.some((r) => Math.abs(s - r.s) <= r.len / 2), 'no stone at s ' + s.toFixed(1));
  }
  assert.deepEqual(stoneLayout({ fields: [] }), []);
  assert.deepEqual(stoneLayout({}), []);
});

test('stone layout follows a moved frame (no world-up assumptions)', () => {
  const turn = ([x, y, z]) => [z, x, y], offset = [20, -14, 80];
  const moved = { ...track, frameAt(s) {
    const f = track.frameAt(s);
    return { ...f, p: turn(f.p).map((v, i) => v + offset[i]), b: turn(f.b), n: turn(f.n), t: turn(f.t) };
  } };
  const t = stoneLayout(moved);
  rows.forEach((r, i) => assert.ok(Math.hypot(...turn(r.p).map((v, j) => v + offset[j] - t[i].p[j])) < 1e-9));
});

test('slab fallback: one instanced draw with one slab per stone, released once', () => {
  const built = buildStones(track, null, null);
  assert.equal(built.mode, 'slab');
  assert.equal(built.count, rows.length);
  assert.equal(built.group.children.length, 1);
  const mesh = built.group.children[0];
  assert.equal(mesh.count, rows.length);
  assert.ok(mesh.boundingSphere.radius > 0);
  let geometryDisposed = 0, materialsDisposed = 0;
  mesh.geometry.addEventListener('dispose', () => geometryDisposed++);
  mesh.material.addEventListener('dispose', () => materialsDisposed++);
  built.dispose(); built.dispose();
  assert.equal(geometryDisposed, 1); assert.equal(materialsDisposed, 1);
  const absent = buildStones({ fields: [] }, null, null);
  assert.equal(absent.mode, 'none'); assert.equal(absent.group.children.length, 0); absent.dispose();
});

test('model path: Astra platform cloned per stone with shared geometry and one brass lip material', () => {
  const scene = new Group(); scene.name = 'platform_root';
  const geo = new BoxGeometry(5, 0.8, 5), mats = {};
  for (const slot of ['surface', 'lip', 'underside', 'detail']) {
    const m = new MeshStandardMaterial(); m.name = slot; mats[slot] = m;
    scene.add(new Mesh(geo, m));
  }
  const art = { model: (id) => (id === STONE_KIT.modelId ? { scene } : null) };
  const built = buildStones(track, art, null);
  assert.equal(built.mode, 'model');
  assert.equal(built.group.children.length, rows.length);
  const lips = new Set(), geos = new Set();
  built.group.traverse((o) => { if (o.isMesh) { geos.add(o.geometry); if (o.material.name === 'stone-lip') lips.add(o.material); } });
  assert.equal(geos.size, 1, 'geometry shared');
  assert.equal(lips.size, 1, 'one lip material');
  const lip = [...lips][0];
  assert.notEqual(lip, mats.lip, 'the art set keeps its own lip');
  assert.equal('#' + lip.color.getHexString(), STONE_KIT.brass);
  built.group.children.forEach((holder, i) => {
    assert.ok(Math.hypot(holder.position.x - rows[i].p[0], holder.position.y - rows[i].p[1], holder.position.z - rows[i].p[2]) < 1e-9);
  });
  let lipDisposed = 0, artDisposed = 0;
  lip.addEventListener('dispose', () => lipDisposed++);
  mats.lip.addEventListener('dispose', () => artDisposed++);
  built.dispose(); built.dispose();
  assert.equal(lipDisposed, 1); assert.equal(artDisposed, 0);
});

function rig({ gentle = false } = {}) {
  const feelJson = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
  const feel = createFeel(feelJson), bus = createBus(), g = createGentle(feel, { search: '' });
  if (gentle) g.set({ on: true });
  const fx = createFx(bus, feel, { gentle: g, photosafe: createPhotosafe(feel) }), kart = createState();
  return { bus, fx, run: (s) => { for (let t = 0; t < s - 1e-9; t += 1 / 60) fx.update(1 / 60, kart); } };
}

test('cues: a stone is a tick and a 2 cm bob, Gentle keeps only the tick; a void dips and darkens, shorter than a drop', () => {
  assert.equal(typeof FX_CUES.stone, 'function'); assert.equal(typeof FX_CUES.void, 'function');
  const a = rig();
  a.bus.emit('stone', { id: 'x', s: 1, x: 0 }); a.run(0.05);
  assert.ok(a.fx.state.rumble > 0, 'tick');
  assert.ok(a.fx.state.camPush > 0 && a.fx.state.camPush <= STONE_CUES.bobM + 1e-9, 'bob ' + a.fx.state.camPush);
  a.run(0.6);
  assert.equal(a.fx.state.camPush, 0); assert.equal(a.fx.state.rumble, 0);
  const g = rig({ gentle: true });
  g.bus.emit('stone', { id: 'x', s: 1, x: 0 }); g.run(0.05);
  assert.equal(g.fx.state.camPush, 0, 'no bob under Gentle');
  const v = rig();
  v.bus.emit('void', { s: 1, x: 0 }); v.run(0.08);
  assert.ok(v.fx.state.fovKick < 0, 'dip'); assert.ok(v.fx.state.duck > 0, 'darkening');
  v.run(0.6);
  assert.equal(v.fx.state.fovKick, 0); assert.equal(v.fx.state.duck, 0, 'a void lets go on its own, a drop holds');
  const gv = rig({ gentle: true });
  gv.bus.emit('void', { s: 1, x: 0 }); gv.run(0.08);
  assert.equal(gv.fx.state.fovKick, 0, 'no dip under Gentle');
});
