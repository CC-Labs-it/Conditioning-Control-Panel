// Prize Counter kart looks (7.2 lane B3-G1): outfits, paints, the lace umbrella and the podium trail are worn only
// when owned, ride the race look next to the vehicle, and never touch the online course hash.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { DEFAULT, OUTFITS, PAINTS, ownedLooks, wearable, cleanGarage, raceLook, shopLockLine, COUNTER_LINE } from '../hud/garageState.js';
import { cleanGrants } from '../meta/owned.js';
import { normLook, roomLook, botLook, TYPES, C2R, LOOK_OUTFITS, SHOP_LOOK_OUTFITS, LOOK_PAINTS, LOOK_UMBRELLAS, LOOK_TRAILS } from '../net/protocol.js';
import { catalogFrom, lookForSeat, lookForTier, SHOP_PIECES, shopExtras } from '../render/wardrobeMath.js';
import { hostCounter, counterMessage } from '../host/prizeCounter.js';
import { onlineCourseHash } from '../net/courseHash.js';
import { loadTrack } from '../track/load.js';

const read = (p) => JSON.parse(readFileSync(new URL(p, import.meta.url), 'utf8'));
const ALL = ['cos.kart.outfit.pit-crew', 'cos.kart.outfit.ringmaster', 'cos.kart.outfit.ballerina', 'cos.kart.paint.pattern-bundle',
  'cos.kart.umbrella.lace', 'cos.prime.paint.host-gold', 'cos.prime.trail.podium'];
const custom = { ...DEFAULT, original: false, skin: 'Custom' };

test('grants open the shop looks; retired v1 paints keep what their owners bought', () => {
  const none = ownedLooks([]);
  for (const k of ['outfits', 'paints', 'umbrellas', 'trails']) assert.equal(none[k].size, 0, k);
  const all = ownedLooks(ALL);
  assert.deepEqual([...all.outfits].sort(), ['ballerina', 'pit-crew', 'ringmaster']);
  assert.deepEqual([...all.paints].sort(), ['hearts', 'host-gold', 'spiral']);
  assert.deepEqual([...all.umbrellas], ['lace']);
  assert.deepEqual([...all.trails], ['podium']);
  assert.deepEqual([...ownedLooks(['cos.kart.paint.hearts']).paints], ['hearts'], 'a v1 hearts owner keeps hearts');
  assert.deepEqual([...ownedLooks(['cos.kart.paint.spiral']).paints], ['spiral']);
  assert.equal(ownedLooks(['cos.kart.paint.pink-checker']).paints.size, 0, 'checks is a free native pattern already');
  // the owned snapshot keeps the two kart Prime families and nothing else outside cos.kart.*
  assert.deepEqual(cleanGrants([...ALL, 'cos.prime.frame.room-host', 'cos.prime.banner.arena', 'cos.rain.dress.x']), [...ALL].sort());
});

test('free outfits and native patterns stay free; shop ones are dark with the counter line until owned', () => {
  for (const [id] of OUTFITS) assert.equal(wearable('outfit', id), !['pit-crew', 'ringmaster', 'ballerina'].includes(id), id);
  for (const [id] of PAINTS) assert.equal(wearable('paint', id), false, id);
  assert.equal(wearable('umbrella', 'lace'), false);
  assert.equal(wearable('trail', 'podium'), false);
  assert.equal(shopLockLine('ballerina'), COUNTER_LINE);
  assert.equal(shopLockLine('lace'), 'In the Prize Counter');
  assert.equal(shopLockLine('host-gold'), 'Prime, in the Prize Counter');
  assert.equal(shopLockLine('podium'), 'Prime, in the Prize Counter');
});

test('a saved shop look is kept only while owned', () => {
  const saved = { ...custom, outfit: 'ballerina', paint: 'hearts', umbrella: 'lace', trail: 'podium' };
  const bare = cleanGarage(saved);
  assert.equal(bare.outfit, DEFAULT.outfit);
  assert.equal(bare.paint, null); assert.equal(bare.umbrella, null); assert.equal(bare.trail, null);
  const owned = cleanGarage(saved, undefined, ownedLooks(ALL));
  assert.equal(owned.outfit, 'ballerina'); assert.equal(owned.paint, 'hearts');
  assert.equal(owned.umbrella, 'lace'); assert.equal(owned.trail, 'podium');
  assert.equal(cleanGarage({ ...saved, paint: 'toString', umbrella: 'nope' }, undefined, ownedLooks(ALL)).paint, null);
});

test('the race look carries outfit, paint, umbrella and trail next to the vehicle, only when worn and owned', () => {
  const owns = ownedLooks(ALL), s = { ...custom, outfit: 'ringmaster', paint: 'host-gold', umbrella: 'lace', trail: 'podium' };
  const look = raceLook(s, owns);
  assert.equal(look.vehicle, 'emi-vehicle-lovebomb');
  assert.equal(look.outfit, 'emi-outfit-ringmaster');
  assert.equal(look.paint, 'host-gold'); assert.equal(look.umbrella, 'lace'); assert.equal(look.trail, 'podium');
  assert.match(look.skin, /^[0-9a-f]{36}[0-3]$/, 'the skin keeps its old wire shape');
  assert.deepEqual(normLook(look), look, 'every id is on the wire list');
  assert.ok(TYPES.look(look));
  const lost = raceLook(s, ownedLooks([]));
  assert.equal(lost.outfit, null); assert.equal(lost.paint, undefined); assert.equal(lost.umbrella, undefined); assert.equal(lost.trail, undefined);
  assert.equal(raceLook({ ...s, original: true }, owns).paint, undefined, 'a paint needs the custom skin it tints');
  assert.deepEqual(Object.keys(raceLook(DEFAULT)).sort(), ['outfit', 'vehicle'], 'a plain look is unchanged on the wire');
});

test('wire: extras are known lists, the room look message carries them, bots never wear shop pieces', () => {
  assert.deepEqual([...LOOK_PAINTS], SHOP_PIECES.paints);
  assert.deepEqual([...LOOK_UMBRELLAS], Object.keys(SHOP_PIECES.umbrellas));
  assert.deepEqual([...LOOK_TRAILS], Object.keys(SHOP_PIECES.trails));
  assert.deepEqual([...SHOP_LOOK_OUTFITS], SHOP_PIECES.outfits);
  const full = { outfit: 'emi-outfit-ballerina', vehicle: 'emi-vehicle-petal', paint: 'spiral', umbrella: 'lace', trail: 'podium' };
  assert.deepEqual(normLook({ ...full, paint: 'gold-leaf', trail: 'x' }), { outfit: 'emi-outfit-ballerina', vehicle: 'emi-vehicle-petal', umbrella: 'lace' });
  assert.ok(TYPES.look(full), 'a seat record with extras decodes');
  assert.ok(TYPES.seatList([{ seat: 0, kind: 'human', name: 'a', look: { ...full, extra: 1 } }]) === false, 'unknown keys still refuse');
  assert.deepEqual(roomLook(full), full, 'a lobby look change keeps the extras the ticket carried (room boundLook)');
  assert.deepEqual(roomLook({ outfit: null, vehicle: 'emi-vehicle-petal' }), { outfit: null, vehicle: 'emi-vehicle-petal' }, 'nothing worn, no extra keys');
  assert.deepEqual(Object.keys(C2R.look).sort(), ['outfit', 'paint', 'skin', 'trail', 'umbrella', 'vehicle'], 'the room look message matches CCP-Server #371');
  for (let seat = 0; seat < 8; seat++) for (const seed of [0, 7, 99, 12345]) {
    const b = botLook(seat, seed);
    assert.ok(LOOK_OUTFITS.includes(b.outfit) && !SHOP_LOOK_OUTFITS.includes(b.outfit));
    assert.deepEqual(Object.keys(b).sort(), ['outfit', 'vehicle']);
  }
});

test('render: a rival wears the shop pieces it sent; bots and unknown ids get none', () => {
  const C = catalogFrom(read('../art/models/emi-bot-wardrobe.json'), read('../art/models/emi-vehicle-variants.json'));
  const want = { outfit: 'emi-outfit-pit-crew', vehicle: 'emi-vehicle-petal', paint: 'hearts', umbrella: 'lace', trail: 'podium' };
  assert.deepEqual(lookForSeat(2, 5, C, want), want);
  assert.deepEqual(lookForTier(lookForSeat(2, 5, C, want), 'phone'), { ...want, vehicle: null }, 'phones keep the light pieces');
  for (let seat = 1; seat < 8; seat++) {
    const bot = lookForSeat(seat, 3, C);
    assert.ok(!SHOP_PIECES.outfits.includes(bot.outfit));
    assert.equal(bot.paint ?? bot.umbrella ?? bot.trail, undefined);
  }
  assert.deepEqual(shopExtras({ paint: 'x', umbrella: 'constructor', trail: '__proto__' }), {});
});

test('art: the shop pieces ship in the manifest with their file hash and the accepted kart base', async () => {
  const { createHash } = await import('node:crypto');
  const man = read('../art/manifest.json'), ward = read('../art/models/emi-bot-wardrobe.json');
  const artSrc = readFileSync(new URL('../render/art.js', import.meta.url), 'utf8');
  for (const id of [...SHOP_PIECES.outfits, 'umbrella-lace', 'podium-trail']) {
    const row = man.find((r) => r.id === id);
    assert.ok(row, id);
    assert.equal(row.baseSha256, ward.baseSha256, id);
    assert.equal(createHash('sha256').update(readFileSync(new URL('../art/' + row.file, import.meta.url))).digest('hex'), row.sha256, id);
    assert.ok(artSrc.includes("'" + id + "'"), id + ' wanted by render/art.js');
  }
  for (const p of ['hearts', 'spiral', 'host-gold']) assert.ok(readFileSync(new URL('../art/paints/' + p + '-mask.png', import.meta.url)).length > 1000, p);
});

test('the Prize Counter row: only a host with caps.prizeCounter, contract message shape', () => {
  const hosted = { hosted: true }, init = (prizeCounter) => ({ caps: { prizeCounter } });
  assert.equal(hostCounter(hosted, init(true)), true);
  assert.equal(hostCounter(hosted, init(undefined)), false);
  assert.equal(hostCounter({ hosted: false }, init(true)), false);
  assert.equal(hostCounter(null, init(true)), false);
  assert.deepEqual(counterMessage(), { type: 'open-prize-counter', tab: 'kart', from: 'glassroad' });
});

test('looks never reach the online course hash: all five courses still match the room cards', async () => {
  // CCP-Server rooms/src/tracks.js courseHash after the ramp cards (CCP-Server #376); a change here means a room regenerate + redeploy
  const ROOM = {
    threshold: 'f624dda631750196a5d4d235ff8929751dc2fafe76074f7d217d3bbfb820380d',
    mainframe: '7098b15a184c9433af7efb0675558d1cb280387a23825f8dd9737741f3e56259',
    keep: 'c963da264582e3f62e0f94f3d73df66ff7a8df025d2aef882ecf3b5be9499137',
    boudoir: '08aa68c811983e5f8d9f251de4ed65d415a95fbb61304375492e6e7fcdfcd6d0',
    goonworks: '3dba4d72c747c4116e593929725278b3a7d46f84b29546c2e7e4013e9d11a5f1',
  };
  const feel = read('../feel.json');
  for (const [id, hash] of Object.entries(ROOM)) {
    const json = read('../tracks/' + id + '.json');
    assert.equal(await onlineCourseHash(json, feel, loadTrack(json, { moving: true })), hash, id);
  }
  const src = readFileSync(new URL('../net/courseHash.js', import.meta.url), 'utf8');
  assert.ok(!/look|outfit|paint|umbrella|trail/i.test(src), 'the hash never reads a look');
});
