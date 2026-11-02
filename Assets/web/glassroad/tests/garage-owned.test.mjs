import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createOwned, cleanGrants, OWNED_KEY } from '../meta/owned.js';
import { createProfileSync } from '../meta/profileSync.js';
import { KARTS, LOCKED, FREE_KART, DEFAULT, kartGrant, ownedKarts, lockedKarts, cleanGarage, raceLook } from '../hud/garageState.js';
import { LOOK_VEHICLES } from '../net/protocol.js';

const memory = () => { const m = new Map(); return { m, getItem: (k) => (m.has(k) ? m.get(k) : null), setItem: (k, v) => m.set(k, String(v)) }; };
const read = (rel) => readFileSync(new URL(rel, import.meta.url), 'utf8');

test('owned bodies: the ten Prize Counter bodies are all in the garage, the free kart is a new player\'s pick', () => {
  const sold = ['petal', 'trinket', 'pendulum', 'checkmate', 'lovebomb', 'undertow', 'feedback', 'velvet-lock', 'reverie', 'house-edge'];
  assert.deepEqual(KARTS.map(([id]) => id).sort(), [...sold].sort());
  for (const [id] of KARTS) assert.ok(LOOK_VEHICLES.includes(raceLook({ ...DEFAULT, kart: id }).vehicle), id + ' is a room vehicle id');
  assert.equal(DEFAULT.kart, FREE_KART);
  assert.equal(kartGrant('house-edge'), 'cos.kart.body.house-edge');
});

test('owned bodies: grants open only their own body, junk opens nothing', () => {
  assert.deepEqual([...ownedKarts(['cos.kart.body.petal', 'cos.kart.body.glass-slipper', 'cos.kart.outfit.pit-crew', 'kart.body.reverie', 7])], ['petal']);
  assert.equal(ownedKarts(null).size, 0);
  const dark = lockedKarts(new Set(), ownedKarts(['cos.kart.body.petal']));
  assert.equal(dark.size, LOCKED.size - 1);
  assert.equal(cleanGarage({ kart: 'petal' }, dark).kart, 'petal');
  assert.equal(cleanGarage({ kart: 'reverie' }, dark).kart, FREE_KART, 'a pick that is locked now falls back to the free kart');
});

test('owned store: keeps cos.kart ids only, remembers per account, a new snapshot replaces the old', () => {
  assert.deepEqual(cleanGrants(['cos.kart.body.petal', 'cos.kart.body.petal', 'cos.rain.hair.ink-sleek', 'cos.kart.<x>', 'cos.kart.body.' + 'a'.repeat(60)]), ['cos.kart.body.petal']);
  assert.equal(cleanGrants('cos.kart.body.petal'), null);
  const storage = memory();
  const a = createOwned({ storage, uid: 'u_ann' });
  assert.deepEqual(a.grants, []);
  assert.equal(a.adopt(['cos.kart.body.trinket', 'cos.kart.body.petal']), true);
  assert.deepEqual(a.grants, ['cos.kart.body.petal', 'cos.kart.body.trinket']);
  assert.equal(a.adopt({ grants: [] }), false, 'junk leaves the snapshot alone');
  assert.deepEqual(createOwned({ storage, uid: 'u_ann' }).grants, ['cos.kart.body.petal', 'cos.kart.body.trinket'], 'remembered for the next page');
  assert.deepEqual(createOwned({ storage, uid: 'u_bea' }).grants, [], 'another account owns nothing of it');
  assert.deepEqual(createOwned({ storage }).grants, [], 'signed out owns nothing');
  a.adopt(['cos.kart.body.petal']);
  assert.deepEqual(JSON.parse(storage.m.get(OWNED_KEY)), { uid: 'u_ann', grants: ['cos.kart.body.petal'] });
  a.setAccount('u_bea');
  assert.deepEqual(a.grants, []);
  const broken = createOwned({ storage: { getItem() { throw Error('denied'); }, setItem() { throw Error('denied'); } }, uid: 'u_x' });
  assert.equal(broken.adopt(['cos.kart.body.petal']), true);
  assert.deepEqual(broken.grants, ['cos.kart.body.petal'], 'blocked storage still holds the snapshot for the page');
});

test('owned store: the profile pull hands its grants to the garage, a push never does', async () => {
  const seen = [];
  const request = async (method) => ({ status: 200, body: { ok: true, record: { v: 1 }, grants: method === 'GET' ? ['cos.kart.body.petal'] : ['cos.kart.body.reverie'] } });
  const storage = memory();
  const sync = createProfileSync({ request, storage, onGrants: (g) => seen.push(g), timers: { setTimeout: (fn) => { fn(); return 1; }, clearTimeout() {} } });
  await sync.pull();
  assert.deepEqual(seen, [['cos.kart.body.petal']]);
  const quiet = createProfileSync({ request: async () => ({ status: 200, body: { ok: true, record: { v: 1 } } }), storage, onGrants: (g) => seen.push(g) });
  await quiet.pull();
  assert.equal(seen.length, 1, 'an older server without grants changes nothing');
  const throwing = createProfileSync({ request, storage, onGrants: () => { throw Error('boom'); } });
  assert.ok(await throwing.pull(), 'a broken garage never breaks the logbook');
});

test('owned bodies: boot wires every source into the garage and the host push', () => {
  const boot = read('../boot.js');
  assert.match(boot, /shared\.bridge\.on\('owned'/);
  assert.match(boot, /init\.identity\.grants/);
  assert.match(boot, /owned: \(\) => shared\.owned\.grants/);
  assert.match(boot, /onGrants: adoptOwned/);
  const garage = read('../render/garage.js');
  assert.match(garage, /ownedKarts\(ownedGrants\(\)\)/);
  assert.ok(!/Math\.PI\/4/.test(garage), 'the ring spacing follows the kart count');
});
