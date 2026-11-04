// ART5: the R4 rig director (render/kartPose.js), the wardrobe law (render/wardrobeMath.js) and the art contracts.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { createState, S, RESPAWN } from '../sim/state.js';
import {
  RIG_DEFAULTS as N, rigTable, landingStart, createDirector, directorEvent, directorStep, rigStateWant,
  createReact, reactTo, stepReact, REACT_DEFAULTS as R, reactionsFor,
} from '../render/kartPose.js';
import { catalogFrom, lookForSeat, seedHash, sameJoints, cutIndex, lookForTier, DEFAULT_LOOK } from '../render/wardrobeMath.js';

const read = (p) => JSON.parse(readFileSync(new URL(p, import.meta.url), 'utf8'));
const bytes = (p) => readFileSync(new URL(p, import.meta.url));
const sha = (p) => createHash('sha256').update(bytes(p)).digest('hex');
const BIND = read('../art/models/emi-kart-event-bindings.json');
const WARD = read('../art/models/emi-bot-wardrobe.json');
const VARS = read('../art/models/emi-vehicle-variants.json');
const MAN = read('../art/manifest.json');
const FEEL = read('../feel.json');
const T = rigTable(BIND), C = catalogFrom(WARD, VARS), DT = 1 / 60;
const moving = (sp = 0.6) => { const st = createState(); st[S.speedPct] = sp; st[S.grounded] = 1; return st; };
const step = (d, st, s, gentle = false) => { let last = null; for (let t = 0; t < s; t += DT) { const c = directorStep(d, st, DT, T, N, gentle); if (c) last = c; } return last; };

test('art5: the shipped files match the accepted hashes (R4 kart, classic faces); every outfit and vehicle names that base', () => {
  assert.equal(sha('../art/models/emi-teacup-kart.glb'), WARD.baseSha256);
  assert.equal(sha('../art/models/emi-teacup-kart.glb'), BIND.modelSha256);
  assert.equal(sha('../art/textures/emi-faces.ktx2'), WARD.approvedFaceSha256);
  assert.equal(VARS.baseSha256, WARD.baseSha256);
  for (const id of [...C.outfits, ...C.vehicles]) {
    const row = MAN.find((r) => r.id === id);
    assert.ok(row, id + ' in the manifest');
    assert.equal(row.baseSha256, WARD.baseSha256, id + ' base hash');
    assert.equal(sha('../art/' + row.file), row.sha256, id + ' file hash');
  }
  for (const v of VARS.vehicles) assert.equal(sha('../' + 'art/' + v.file), v.sha256);
});

test('art5: the classic face atlas: the 16 ids in render/faces.js order, no melty face anywhere', () => {
  const row = MAN.find((r) => r.id === 'emi-faces');
  const ids = BIND.faceAtlas.ids;
  assert.equal(ids.length, 16);
  ids.forEach((id, i) => { const c = row.cells[id] || row.cells[row.aliases[id]]; assert.deepEqual(c, [i % 4, (i / 4) | 0], id); });
  const src = readFileSync(new URL('../render/faces.js', import.meta.url), 'utf8') + JSON.stringify(row) + JSON.stringify(BIND.faceAtlas);
  assert.ok(!/melty/i.test(src));
  assert.equal(row.sourceExpressions.sad, 'idle0_0');
  assert.equal(row.sourceExpressions.sleepy, 'idle0_0');
});

test('art5: the rig table carries all 26 clips with the contract priorities, and heavy landings match feel.json', () => {
  assert.equal(Object.keys(T).length, 26);
  assert.equal(T.rescue_pull.pri, 100); assert.equal(T.idle_bob.pri, 0); assert.equal(T.spin_out.pri, 92);
  assert.ok(T.air_glide.loop && T.jump_takeoff.hold && !T.landing_heavy.loop && !T.landing_heavy.hold);
  assert.equal(N.heavyAirS, FEEL.air.mediumS);
  for (const c of N.gentleClips) assert.ok(T[c], c);
});

test('art5: a landing starts where the rider separation matches, so an early landing never reopens the full-air gap', () => {
  const curve = [[0, 0.42], [0.1, 0.4], [0.27, 0.27], [0.47, 0.03], [0.8, 0]];
  assert.equal(landingStart(0.5, curve), 0);
  assert.equal(landingStart(0.42, curve), 0);
  const t = landingStart(0.2, curve);
  assert.ok(t > 0.27 && t < 0.47);
  assert.equal(landingStart(0, curve), 0.8);
  // with a curve on the clip: a zero gap skips the clip, a partial one starts mid-clip
  const TT = { ...T, landing_squash: { ...T.landing_squash, sep: curve }, landing_heavy: { ...T.landing_heavy, sep: curve } };
  const d = createDirector();
  directorEvent(d, 'land', { airTime: 0.2 }, TT, N, 0);
  assert.equal(d.req, null, 'nothing to close: no landing clip');
  directorEvent(d, 'land', { airTime: 0.9 }, TT, N, 0.2);
  assert.equal(d.req.clip, 'landing_heavy');
  assert.ok(d.req.from > 0.27);
  const ch = directorStep(d, moving(), DT, TT, N);
  assert.equal(ch.clip, 'landing_heavy'); assert.ok(ch.land && ch.fade >= N.landFadeS);
});

test('art5: hop -> takeoff -> air_glide -> landing; drift leans; idle bobs; rescue owns everything', () => {
  const d = createDirector(), st = moving();
  directorEvent(d, 'hop', {}, T, N);
  st[S.airborne] = 1; st[S.grounded] = 0; st[S.airTime] = 0.05;
  assert.equal(directorStep(d, st, DT, T, N).clip, 'jump_takeoff');
  st[S.airTime] = 0.6;
  assert.equal(step(d, st, 0.4).clip, 'air_glide');
  st[S.airborne] = 0; st[S.grounded] = 1;
  directorEvent(d, 'land', { airTime: 0.7 }, T, N, 0.4);
  assert.equal(directorStep(d, st, DT, T, N).clip, 'landing_heavy');
  st[S.driftDir] = -1;
  assert.equal(step(d, st, 1).clip, 'lean_left');
  st[S.driftDir] = 0; st[S.speedPct] = 0;
  assert.equal(step(d, st, 0.3).clip, 'idle_bob');
  st[S.respawnPhase] = RESPAWN.PULL;
  assert.equal(step(d, st, 0.1).clip, 'rescue_pull');
  directorEvent(d, 'bump', { dv: 5, lx: -1 }, T, N);
  assert.equal(directorStep(d, st, DT, T, N), null, 'a bump never breaks a rescue');
});

test('art5: bumps pick a side, hard ones spin; an item hit recoils; cooldowns and priorities hold', () => {
  const d = createDirector(), st = moving();
  directorEvent(d, 'bump', { dv: 4, lx: -0.3 }, T, N);
  assert.equal(directorStep(d, st, DT, T, N).clip, 'bump_left');
  directorEvent(d, 'bump', { dv: 4, lx: 0.3 }, T, N);
  assert.equal(directorStep(d, st, DT, T, N), null, 'same priority inside the cooldown: no restart');
  step(d, st, 0.3);
  directorEvent(d, 'wall', { dv: 9, side: 1 }, T, N);
  assert.equal(directorStep(d, st, DT, T, N).clip, 'spin_out');
  directorEvent(d, 'overtake', {}, T, N);
  assert.equal(directorStep(d, st, DT, T, N), null, 'a wave never cuts a spin');
  const e = createDirector();
  directorEvent(e, 'itemHit', { kind: 'shatter' }, T, N);
  assert.equal(directorStep(e, st, DT, T, N).clip, 'hit_recoil');
  const ch = step(e, st, 1.2);
  assert.equal(ch.clip, null, 'a finished one-shot fades back to rest');
});

test('art5: held items, throws, countdown, finish and top speed come from events and state', () => {
  const d = createDirector(), st = moving(0.95);
  directorEvent(d, 'countdown', { n: 1 }, T, N);
  assert.equal(directorStep(d, st, DT, T, N).clip, 'countdown_ready');
  directorEvent(d, 'go', {}, T, N);
  assert.equal(directorStep(d, st, DT, T, N).clip, null);
  directorEvent(d, 'armed', { id: 'spiral' }, T, N);
  assert.equal(directorStep(d, st, DT, T, N).clip, 'item_ready');
  directorEvent(d, 'fire', { mode: 'lob' }, T, N);
  assert.equal(directorStep(d, st, DT, T, N).clip, 'item_lob');
  st[S.speedPct] = 0.99;
  assert.equal(step(d, st, 1).clip, 'max_speed');
  st[S.speedPct] = 0.93;
  assert.equal(rigStateWant(d, st, N), 'max_speed', 'hysteresis: still on above the exit');
  st[S.speedPct] = 0.85;
  assert.equal(rigStateWant(d, st, N), null);
  directorEvent(d, 'finish', { place: 8, count: 8 }, T, N);
  assert.equal(directorStep(d, st, DT, T, N).clip, 'sulk');
  directorEvent(d, 'countdown', { n: 3 }, T, N);
  assert.equal(directorStep(d, st, DT, T, N).clip, null, 'a new race clears the sulk');
  directorEvent(d, 'finish', { place: 2, count: 8 }, T, N);
  assert.equal(directorStep(d, st, DT, T, N).clip, 'cheer');
  assert.deepEqual(reactionsFor('item-roll', { racer: 3, id: 'echo' }), [[3, 'armed', { id: 'echo' }]]);
});

test('art5: Gentle keeps the assembled rest for separation, spin and knock clips and fades no faster than 0.3 s', () => {
  const d = createDirector(), st = moving();
  directorEvent(d, 'hop', {}, T, N);
  st[S.airborne] = 1; st[S.airTime] = 0.1;
  assert.equal(directorStep(d, st, DT, T, N, true), null);
  st[S.airborne] = 0; st[S.speedPct] = 0;
  const ch = step(d, st, 0.2, true);
  assert.equal(ch.clip, 'idle_bob'); assert.ok(ch.fade >= 0.3);
  directorEvent(d, 'wall', { dv: 9 }, T, N);
  assert.equal(directorStep(d, st, DT, T, N, true), null);
});

test('art5: under the rig the procedural squash, twist, knock and lifts give way; item hits still turn, flip and hop', () => {
  const r = Object.assign(createReact(0), { rig: true }), st = moving();
  reactTo(r, 'hop', {}, R); reactTo(r, 'land', { fall: 12 }, R); reactTo(r, 'bump', { dv: 9, lx: 1 }, R); reactTo(r, 'overtake', {}, R);
  let most = 0;
  for (let t = 0; t < 0.5; t += DT) { const o = stepReact(r, st, DT, R); most = Math.max(most, Math.abs(o.sq), Math.abs(o.yaw), Math.abs(o.roll), Math.abs(o.lift)); }
  assert.ok(most < 1e-6, 'nothing moved the body: ' + most);
  assert.equal(r.out.face !== undefined, true);
  reactTo(r, 'itemHit', { kind: 'shatter', durationS: 2 }, R);
  let roll = 0, lift = 0;
  for (let t = 0; t < 0.6; t += DT) { const o = stepReact(r, st, DT, R); roll = Math.max(roll, Math.abs(o.roll)); lift = Math.max(lift, o.lift); }
  assert.ok(roll > 1 && lift > 0.5, 'the polish 4 flip layers over the rig');
});

test('art5: a stable look per seat: distinct outfits for the seven bots, vehicles mixed, the player is EMI', () => {
  assert.equal(C.outfits.length, 8); assert.equal(C.vehicles.length, 10);
  assert.deepEqual(lookForSeat(0, 7, C), DEFAULT_LOOK);
  const looks = [1, 2, 3, 4, 5, 6, 7].map((s) => lookForSeat(s, 7, C));
  assert.equal(new Set(looks.map((l) => l.outfit)).size, 7);
  assert.deepEqual(looks, [1, 2, 3, 4, 5, 6, 7].map((s) => lookForSeat(s, 7, C)), 'stable');
  const vs = new Set(looks.map((l) => l.vehicle));
  assert.ok(vs.size >= 2, 'multiple stable kart variants');
  assert.notDeepEqual(looks, [1, 2, 3, 4, 5, 6, 7].map((s) => lookForSeat(s, 'other', C)));
  assert.equal(seedHash(7), seedHash('7'));
  // remote ids: kept when known, an unknown id falls back to the default look field by field
  assert.deepEqual(lookForSeat(3, 7, C, { outfit: 'emi-outfit-swim', vehicle: 'emi-vehicle-trinket' }), { outfit: 'emi-outfit-swim', vehicle: 'emi-vehicle-trinket' });
  assert.deepEqual(lookForSeat(3, 7, C, { outfit: 'nope', vehicle: 'emi-vehicle-petal' }), { outfit: null, vehicle: 'emi-vehicle-petal' });
  assert.deepEqual(lookForSeat(3, 7, C, {}), DEFAULT_LOOK);
  assert.deepEqual(lookForTier({ outfit: 'a', vehicle: 'b' }, 'phone'), { outfit: 'a', vehicle: null });
  // a wardrobe on another base hash offers nothing
  assert.equal(catalogFrom({ ...WARD, baseSha256: 'x' }, { ...VARS, baseSha256: 'x' }).outfits.length, 8, 'own base agrees');
  assert.equal(catalogFrom(WARD, { ...VARS, baseSha256: 'x' }).vehicles.length, 0, 'a vehicle on another base is rejected');
});

test('art5: the vehicle cut drops only triangles fully weighted to cup and saucer; bone sets compare exactly', () => {
  // vertices 0..2 on joint 1 (cup), 3..5 on joint 4 (saucer), 6..8 rider (joint 0), 9 half cup half rider
  const J = new Uint8Array(40), W = new Float32Array(40);
  const set = (v, j, w) => { J[v * 4] = j; W[v * 4] = w; };
  for (let v = 0; v < 3; v++) set(v, 1, 1);
  for (let v = 3; v < 6; v++) set(v, 4, 1);
  for (let v = 6; v < 9; v++) set(v, 0, 1);
  set(9, 1, 0.5); J[9 * 4 + 1] = 0; W[9 * 4 + 1] = 0.5;
  const idx = new Uint16Array([0, 1, 2, 3, 4, 5, 6, 7, 8, 0, 3, 1, 9, 6, 7]);
  const c = cutIndex(idx, J, W, [1, 4]);
  assert.equal(c.removed, 3); assert.equal(c.mixed, 0);
  assert.deepEqual([...c.index], [6, 7, 8, 9, 6, 7]);
  assert.ok(c.index instanceof Uint16Array);
  assert.equal(cutIndex(new Uint16Array([0, 1, 6]), J, W, [1, 4]).mixed, 1);
  assert.ok(sameJoints([...WARD.jointNames].reverse(), WARD.jointNames));
  assert.ok(!sameJoints(WARD.jointNames.slice(1), WARD.jointNames));
  assert.deepEqual(C.removeJoints, ['cup_body', 'saucer_body']);
  assert.equal(C.removedTriangles, 1612);
});
