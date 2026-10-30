// smoke/skins.test.mjs - the wardrobe, in node, no browser:  node --test smoke/skins.test.mjs
//
// What the men wear is the player's pick clamped to what the account owns; the board decides
// which side wears it; the seat and join bodies carry it; the match state and the watch head
// hand both seats' looks back (Prize Counter contracts 1, 2 and 4).

import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { SLOTS, CATALOG, normalLook, clampLook, ownedIds, wireLook, counterFrame, lookIds, grantOf } from '../skins/look.js';
import { createWear } from '../skins/wear.js';
import { readPick, wardrobeHtml, LOCKED_NOTE } from '../door/wardrobe.js';
import * as api from '../net/api.js';

const ALL = [...SLOTS.flatMap((s) => CATALOG[s])];
const grantsFor = (...ids) => ids.map(grantOf);

function fakeBus() {
  const subs = new Map();
  return {
    on(n, fn) { if (!subs.has(n)) subs.set(n, new Set()); subs.get(n).add(fn); return () => subs.get(n).delete(fn); },
    emit(n, p) { for (const fn of subs.get(n) || []) fn(p); },
  };
}
function fakeStorage() { const m = new Map(); return { getItem: (k) => (m.has(k) ? m.get(k) : null), setItem: (k, v) => m.set(k, String(v)) }; }
function hostPipe() {
  const fns = new Set();
  return { onHost: (fn) => { fns.add(fn); return () => fns.delete(fn); }, send: (m) => { for (const fn of fns) fn(m); } };
}

test('the catalog is every item Astra drew, one slot each', () => {
  const reg = JSON.parse(readFileSync(fileURLToPath(new URL('../assets/skins/registry.json', import.meta.url)), 'utf8'));
  const ids = [...new Set(reg.items.map((i) => i.id))].sort();
  assert.deepEqual(ids, [...ALL].sort());
  for (const id of ids) {
    const pieces = reg.items.filter((i) => i.id === id).map((i) => i.piece).sort();
    assert.deepEqual(pieces, ['bishop', 'king', 'knight', 'pawn', 'queen', 'rook'], id + ' has all six');
  }
});

test('normalLook keeps known ids in their own slot only', () => {
  assert.deepEqual(normalLook({ set: 'chess.set.royal-capes', banner: 'chess.set.velvet-court', insignia: 'nope' }),
    { set: 'chess.set.royal-capes', banner: null, insignia: null });
  assert.deepEqual(normalLook(null), { set: null, banner: null, insignia: null });
  assert.deepEqual(lookIds({ insignia: 'chess.insignia.star', set: 'chess.set.velvet-court' }), ['chess.set.velvet-court', 'chess.insignia.star']);
});

test('clampLook drops what the grants do not own; Prime items count when held', () => {
  const look = { set: 'chess.set.royal-capes', banner: 'prime.banner.arena', insignia: 'chess.insignia.heart' };
  assert.deepEqual(clampLook(look, grantsFor('prime.banner.arena')), { set: null, banner: 'prime.banner.arena', insignia: null });
  assert.deepEqual(clampLook(look, []), { set: null, banner: null, insignia: null });
  assert.equal(ownedIds(['cos.chess.set.royal-capes', 'cos.kart.body.x', 42]).size, 1);
});

test('wireLook sends nothing for a bare look', () => {
  assert.equal(wireLook({}), null);
  assert.deepEqual(wireLook({ banner: 'chess.banner.crest' }), { set: null, banner: 'chess.banner.crest', insignia: null });
});

test('the counter frame is contract 1', () => {
  assert.deepEqual(counterFrame(), { type: 'open-prize-counter', tab: 'chess', from: 'piecebypiece' });
});

test('wear: a pick needs a grant, is saved, and goes to the host', () => {
  const posted = [];
  const pipe = hostPipe();
  const looks = [];
  const pieces = { setLooks: (l) => looks.push(l) };
  const w = createWear({ bus: fakeBus(), pieces, post: (m) => posted.push(m), onHost: pipe.onHost, storage: fakeStorage(), hosted: true });
  assert.equal(w.choose('set', 'chess.set.royal-capes'), false, 'unowned is refused');
  pipe.send({ type: 'owned', grants: grantsFor('chess.set.royal-capes') });
  assert.equal(w.choose('set', 'chess.set.royal-capes'), true);
  assert.deepEqual(posted.at(-1), { type: 'pbp:look', look: { set: 'chess.set.royal-capes', banner: null, insignia: null } });
  assert.deepEqual(w.sides().w.set, 'chess.set.royal-capes', 'the menu dresses white');
  assert.equal(w.sides().b.set, null);
  w.openCounter();
  assert.deepEqual(posted.at(-1), counterFrame());
  assert.ok(looks.length >= 1);
});

test('wear: a grant that goes away takes the item off, and the host can hand a look in', () => {
  const pipe = hostPipe();
  const w = createWear({ onHost: pipe.onHost, storage: fakeStorage(), hosted: true });
  pipe.send({ type: 'owned', grants: grantsFor('chess.banner.crest') });
  pipe.send({ type: 'pbp:look', look: { banner: 'chess.banner.crest', set: 'chess.set.velvet-court' } });
  assert.deepEqual(w.mine(), { set: null, banner: 'chess.banner.crest', insignia: null });
  pipe.send({ type: 'owned', grants: [] });
  assert.deepEqual(w.mine(), { set: null, banner: null, insignia: null });
});

test('wear: who wears it - solo, two here, online seat, server looks, watching', () => {
  const bus = fakeBus();
  const pipe = hostPipe();
  const w = createWear({ bus, onHost: pipe.onHost, storage: fakeStorage(), hosted: true });
  pipe.send({ type: 'owned', grants: grantsFor('chess.insignia.star') });
  w.choose('insignia', 'chess.insignia.star');
  bus.emit('local', { mode: 'solo', me: 'b', sides: ['b'] });
  assert.equal(w.sides().b.insignia, 'chess.insignia.star');
  assert.equal(w.sides().w.insignia, null);
  bus.emit('local', { mode: 'hotseat', sides: ['w', 'b'] });
  assert.equal(w.sides().w.insignia, 'chess.insignia.star');
  assert.equal(w.sides().b.insignia, 'chess.insignia.star');
  bus.emit('local', { mode: 'online', sides: [], match: { id: 'm1', side: 'b' } });
  assert.equal(w.sides().b.insignia, 'chess.insignia.star');
  assert.equal(w.sides().w.insignia, null);
  bus.emit('looks', { white: { set: 'chess.set.velvet-court' }, black: { insignia: 'chess.insignia.star' } });
  assert.equal(w.sides().w.set, 'chess.set.velvet-court', 'the opponent wears what the server says');
  bus.emit('local', { mode: 'watch', sides: [] });
  assert.equal(w.sides().w.set, null, 'the stands start bare');
  bus.emit('looks', { white: { banner: 'chess.banner.house-pink' }, black: null });
  assert.equal(w.sides().w.banner, 'chess.banner.house-pink');
  w.toMenu();
  assert.equal(w.sides().w.insignia, 'chess.insignia.star');
});

test('wear: unhosted keeps the pick locally and owns nothing', () => {
  const storage = fakeStorage();
  storage.setItem('pbp.look.v1', JSON.stringify({ set: 'chess.set.royal-capes' }));
  const w = createWear({ storage });
  assert.deepEqual(w.picked().set, 'chess.set.royal-capes');
  assert.equal(w.mine().set, null);
});

test('wardrobe: owned pills pick, the rest stay visible and locked', () => {
  const pipe = hostPipe();
  const w = createWear({ onHost: pipe.onHost, storage: fakeStorage(), hosted: true });
  pipe.send({ type: 'owned', grants: grantsFor('chess.set.velvet-court') });
  const esc = (s) => String(s);
  const html = wardrobeHtml(w, { esc, bar: (t) => `<h1>${t}</h1>`, bigBtn: (a, i, t) => `<button data-act="${a}">${t}</button>`, hosted: true });
  assert.match(html, /data-id="set:chess\.set\.velvet-court"/);
  assert.doesNotMatch(html, /data-id="set:chess\.set\.royal-capes"/);
  assert.ok(html.includes('The Spoiled Royals') && html.includes(LOCKED_NOTE));
  assert.match(html, /data-act="counter"/);
  const bare = wardrobeHtml(w, { esc, bar: () => '', bigBtn: (a) => `<b data-act="${a}">`, hosted: false });
  assert.doesNotMatch(bare, /data-act="counter"/, 'no counter without a host');
  assert.deepEqual(readPick('banner:chess.banner.crest'), ['banner', 'chess.banner.crest']);
  assert.deepEqual(readPick('banner:'), ['banner', null]);
  assert.equal(readPick('banner:chess.set.royal-capes'), null);
  assert.equal(readPick('hat:x'), null);
});

test('api: every seat and join carries the look, a bare look carries nothing', async () => {
  const bodies = [];
  api.setTransport(async (method, path, body) => { bodies.push({ path, body }); return { status: 200, body: '{}' }; });
  api.setLookSource(() => ({ set: 'chess.set.royal-capes' }));
  await api.lobbyEnter(api.timeControl(300000, 0));
  await api.join('p_1');
  await api.quick(api.timeControl(300000, 0));
  await api.challenge('p_2');
  await api.acceptChallenge('c1');
  for (const b of bodies) assert.deepEqual(b.body.look, { set: 'chess.set.royal-capes', banner: null, insignia: null }, b.path);
  bodies.length = 0;
  api.setLookSource(() => ({}));
  await api.join('p_1');
  assert.equal('look' in bodies[0].body, false);
  api.setLookSource(null);
  api.setTransport(null);
});

// ---- the insignia faces its owner (desk run 2026-10-30, 09c-insignia-zoom) ------------------------------------------

import { backMountShift, unionBox, MOUNT_GAP } from '../skins/mount.js';

/** A position attribute from a plain [x,y,z,...] list (the shape three's BufferAttribute answers). */
const attr = (xyz) => ({ count: xyz.length / 3, getX: (i) => xyz[i * 3], getY: (i) => xyz[i * 3 + 1], getZ: (i) => xyz[i * 3 + 2] });

/** The body mesh (COLOR_0) of a sculpted piece glb, read straight from the file. */
function bodyOf(name) {
  const b = readFileSync(fileURLToPath(new URL('../assets/pieces/' + name + '.glb', import.meta.url)));
  const len = b.readUInt32LE(12);
  const j = JSON.parse(b.subarray(20, 20 + len).toString('utf8'));
  const bin = b.subarray(20 + len + 8);
  const xyz = [];
  for (const m of j.meshes) for (const p of m.primitives) {
    if (p.attributes.COLOR_0 === undefined) continue;
    const a = j.accessors[p.attributes.POSITION], v = j.bufferViews[a.bufferView];
    const stride = v.byteStride || 12, off = (v.byteOffset || 0) + (a.byteOffset || 0);
    for (let i = 0; i < a.count; i++) xyz.push(bin.readFloatLE(off + i * stride), bin.readFloatLE(off + i * stride + 4), bin.readFloatLE(off + i * stride + 8));
  }
  return attr(xyz);
}

/** The medal node of an insignia glb: where Astra hung it, and how big. */
function medalOf(file) {
  const b = readFileSync(fileURLToPath(new URL('../assets/skins/' + file, import.meta.url)));
  const len = b.readUInt32LE(12);
  const j = JSON.parse(b.subarray(20, 20 + len).toString('utf8'));
  return j.nodes.find((n) => /^medal_/.test(n.name || ''));
}

test('mount: a badge already resting off the back stays put, a floating or buried one is seated', () => {
  const body = attr([0, 0.5, 0.2, 0, 0.5, -0.2, 0.3, 0.5, 0.4]);           // the x 0.3 point is outside the footprint
  const box = (z0, z1) => ({ min: { x: -0.1, y: 0.4, z: z0 }, max: { x: 0.1, y: 0.6, z: z1 } });
  assert.equal(backMountShift(body, box(0.22, 0.25)), 0);
  const far = backMountShift(body, box(0.4, 0.43));
  assert.ok(Math.abs(0.4 + far - (0.2 + MOUNT_GAP.rest)) < 1e-9, 'pulled in to the rest gap');
  const sunk = backMountShift(body, box(0.1, 0.13));
  assert.ok(Math.abs(0.1 + sunk - (0.2 + MOUNT_GAP.rest)) < 1e-9, 'pushed out of the body');
  assert.equal(backMountShift(attr([]), box(0, 1)), 0, 'no body, no slide');
  assert.equal(backMountShift(attr([5, 5, 5]), box(0, 1)), 0, 'nothing in the footprint, no slide');
  assert.deepEqual(unionBox([box(0.1, 0.2), { min: { x: -0.3, y: 0, z: 0.05 }, max: { x: 0, y: 0.1, z: 0.15 } }]),
    { min: { x: -0.3, y: 0, z: 0.05 }, max: { x: 0.1, y: 0.6, z: 0.2 } });
});

test('mount: every insignia is authored on the front and lands on the back, clear of the real body', () => {
  for (const piece of ['pawn', 'rook', 'knight', 'bishop', 'queen', 'king']) {
    for (const kind of ['heart', 'star']) {
      const m = medalOf(`chess.insignia.${kind}-${piece}.glb`);
      assert.ok(m.translation[2] < 0, `${kind} ${piece}: authored on the front (-Z), away from its owner`);
      // the medal plate, turned half round about up: centre z -> -z, about 0.16 x 0.16 x 0.017 of its own scale
      const s = m.scale[0], c = { x: -m.translation[0], y: m.translation[1], z: -m.translation[2] };
      const turned = { min: { x: c.x - 0.16 * s, y: c.y - 0.16 * s, z: c.z - 0.0165 * s }, max: { x: c.x + 0.16 * s, y: c.y + 0.16 * s, z: c.z + 0.065 * s } };
      const body = bodyOf(piece);
      const dz = backMountShift(body, turned);
      let back = -Infinity;
      for (let i = 0; i < body.count; i++) {
        const x = body.getX(i), y = body.getY(i);
        if (x >= turned.min.x && x <= turned.max.x && y >= turned.min.y && y <= turned.max.y) back = Math.max(back, body.getZ(i));
      }
      const gap = turned.min.z + dz - back;
      assert.ok(turned.min.z + dz > 0, `${kind} ${piece}: on the back (+Z), toward its owner's camera`);
      assert.ok(gap >= MOUNT_GAP.min - 1e-9 && gap <= MOUNT_GAP.max + 1e-9, `${kind} ${piece}: rests ${gap.toFixed(3)} off the body`);
    }
  }
});

test('mount: attachments turn insignias only, and pieces seat them against the body', () => {
  const src = (p) => readFileSync(fileURLToPath(new URL(p, import.meta.url)), 'utf8');
  const att = src('../board/attachments.js');
  assert.match(att, /if \(slot !== 'insignia'\) continue;\s*p\.geometry\.rotateY\(Math\.PI\)/);
  const pcs = src('../board/pieces.js');
  assert.match(pcs, /seated\(art, attachments\.partsFor\(type, looks\[side\]\)\)/);
  assert.match(pcs, /backMountShift\(art\.geometry\.attributes\.position/);
});

// ---- the players' profile frames and avatars on the name tags (contract 4, fix wave 2 F4a) --------------------------

import { FRAMES, AVATARS, normCosmetics, seatCosmetics, artUrl, faceKey } from '../skins/profile.js';
import { existsSync } from 'node:fs';

test('profile: both seats read off the state, unknown ids and junk wear nothing', () => {
  assert.equal(seatCosmetics({}), null, 'an old server sends nothing');
  assert.equal(seatCosmetics({ cosmetics: [1] }), null);
  assert.deepEqual(seatCosmetics({ cosmetics: { white: { avatar_id: 'profile.avatar.emi-pixel', frame_id: 'profile.frame.lace' }, black: { frame_id: 'nope' } } }),
    { white: { avatar: 'profile.avatar.emi-pixel', frame: 'profile.frame.lace' }, black: null });
  assert.deepEqual(normCosmetics({ frame_id: 'prime.frame.room-host', avatar_id: 'avatar_bambi_1' }), { avatar: null, frame: 'prime.frame.room-host' });
  assert.equal(faceKey(null), '');
  assert.notEqual(faceKey({ avatar: null, frame: 'profile.frame.gilt' }), faceKey({ avatar: null, frame: 'profile.frame.lace' }));
});

test('profile: every frame and avatar has its picture beside the page, with the attribution', () => {
  for (const id of [...FRAMES, ...AVATARS]) {
    const file = fileURLToPath(new URL('../' + artUrl(id).replace(/^\.\//, ''), import.meta.url));
    assert.ok(existsSync(file), id);
  }
  assert.ok(existsSync(fileURLToPath(new URL('../assets/profile/ATTRIBUTION.txt', import.meta.url))));
});

test('profile: the match and the watch head hand the seats\' cosmetics to the name tags', () => {
  const src = (p) => readFileSync(fileURLToPath(new URL(p, import.meta.url)), 'utf8');
  assert.match(src('../net/match.js'), /const faces = seatCosmetics\(state\);\s*if \(faces\) bus\.emit\('cosmetics', faces\);/);
  assert.match(src('../net/watch.js'), /const faces = seatCosmetics\(data\);/);
  const tags = src('../board/name-tags.js');
  assert.match(tags, /on\('cosmetics', \(p\) => setFaces\(p\)\);/);
  assert.match(tags, /paintLabel\(t\.canvas\.getContext\('2d'\), side, label, faceArt\[side\]\)/);
});
