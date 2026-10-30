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
