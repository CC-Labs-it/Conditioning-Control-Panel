// The kart look on the logbook (7.2 shared shop, contract C1): the newer block wins whole, an older client's push never
// erases it, what is worn goes through the lock rules, nothing stamps on load, and a signed-out or Basic page stays local.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createProfileSync } from '../meta/profileSync.js';
import { GARAGE_MAX_BYTES, GARAGE_SEEN_KEY, RECORD_KEY, cleanGarageBlock, cleanRecord, dueGarageBlock, emptyRecord, loadRecord, mergeRecords, saveGarageSeen, saveRecord, stampGarageBlock } from '../meta/record.js';
import { DEFAULT, FREE_KART, LOCKED, STORAGE, cleanGarage, fitGarageBlock, garageTracker, lockedKarts, ownedKarts, ownedLooks } from '../hud/garageState.js';
import { LOOK_KEY, adoptBlockLook, fitLook, loadLook, lookChoices, stampLook } from '../hud/lobbyMath.js';

const memory = () => { const m = new Map(); return { getItem: (k) => (m.has(k) ? m.get(k) : null), setItem: (k, v) => m.set(k, String(v)), m }; };
const tick = () => new Promise((r) => setImmediate(r));
const T1 = '2026-10-31T10:00:00.000Z', T2 = '2026-10-31T11:00:00.000Z';
const look = (vehicle = 'emi-vehicle-petal') => ({ outfit: null, vehicle });
const block = (updatedAt, state = { kart: 'petal', outfit: 'varsity' }, l = look()) => ({ state, look: l, updatedAt });
const withGarage = (g) => ({ ...emptyRecord(), garage: g });
const owned = (grants) => ({ locked: lockedKarts(new Set(), ownedKarts(grants)), owns: ownedLooks(grants) });

function fakeServer(stored = emptyRecord(), { status = 200, strips = false } = {}) {
  const calls = [];
  const request = async (method, path, body) => {
    calls.push({ method, body });
    await tick();
    if (status !== 200) return { status, body: { error: 'x' } };
    if (method !== 'GET') stored = mergeRecords(stored, body.record);
    const out = JSON.parse(JSON.stringify(stored));
    if (strips) delete out.garage;
    return { status: 200, body: { ok: true, record: out } };
  };
  return { request, calls, get stored() { return stored; } };
}
const instant = { setTimeout: (fn) => { queueMicrotask(fn); return 1; }, clearTimeout() {} };

test('block: only a plain object under the cap with a real date survives, and only the block goes', () => {
  assert.deepEqual(cleanGarageBlock(block(T1)), block(T1));
  for (const bad of [null, 'x', [], { state: {}, look: {} }, { state: [], look: {}, updatedAt: T1 }, { state: {}, look: 7, updatedAt: T1 }, { state: {}, look: {}, updatedAt: 'soon' }])
    assert.equal(cleanGarageBlock(bad), null);
  const big = block(T1, { kart: 'petal', pad: 'x'.repeat(GARAGE_MAX_BYTES) });
  assert.equal(cleanGarageBlock(big), null);
  const rec = emptyRecord(); rec.totals.races = 4; rec.garage = big;
  const clean = cleanRecord(rec);
  assert.equal(clean.garage, undefined);
  assert.equal(clean.totals.races, 4, 'the record itself stays');
  assert.equal('garage' in emptyRecord(), false, 'a record starts with no block');
});

test('merge: the newer updatedAt wins the whole block, never field by field', () => {
  const older = withGarage(block(T1, { kart: 'petal', outfit: 'varsity', paint: 'hearts' }));
  const newer = withGarage(block(T2, { kart: 'reverie' }, look('emi-vehicle-reverie')));
  for (const m of [mergeRecords(older, newer), mergeRecords(newer, older)]) assert.deepEqual(m.garage, newer.garage);
});

test('merge: an older client push with no block never erases it, and no block stays no block', () => {
  const held = withGarage(block(T1));
  const old = emptyRecord(); old.totals.races = 9;
  for (const m of [mergeRecords(held, old), mergeRecords(old, held)]) {
    assert.deepEqual(m.garage, held.garage);
    assert.equal(m.totals.races, 9);
  }
  assert.equal('garage' in mergeRecords(emptyRecord(), old), false);
});

test('sync: a pull saves the account block, pushes a newer local one, and an old server reply never wipes it', async () => {
  const storage = memory();
  const srv = fakeServer(withGarage(block(T2)));
  saveRecord(withGarage(block(T1, { kart: 'reverie' })), storage);
  await createProfileSync({ request: srv.request, storage, timers: instant }).pull();
  assert.deepEqual(loadRecord(storage).garage, block(T2), 'the newer account block lands');

  const local = memory(), up = fakeServer(withGarage(block(T1)));
  saveRecord(withGarage(block(T2, { kart: 'reverie' })), local);
  const sync = createProfileSync({ request: up.request, storage: local, timers: instant });
  await sync.pull(); await tick(); await sync.flush();
  assert.equal(up.calls.at(-1).method, 'POST');
  assert.equal(up.stored.garage.state.kart, 'reverie', 'the newer local block went up');

  const dark = memory(), oldServer = fakeServer(emptyRecord(), { strips: true });
  saveRecord(withGarage(block(T1)), dark);
  const s2 = createProfileSync({ request: oldServer.request, storage: dark, timers: instant });
  await s2.pull(); await tick(); await s2.flush();
  assert.deepEqual(loadRecord(dark).garage, block(T1));
});

test('sync: a look stamped while the pull is in the air stays on top', async () => {
  const storage = memory();
  let release;
  const request = async () => { await new Promise((r) => { release = r; }); return { status: 200, body: { ok: true, record: withGarage(block(T2, { kart: 'reverie' })) } }; };
  const sync = createProfileSync({ request, storage, timers: { setTimeout() { return 1; }, clearTimeout() {} } });
  const pulled = sync.pull();
  await tick();
  stampGarageBlock({ state: { kart: 'petal' }, look: look() }, storage, Date.parse(T2) + 5000);
  release();
  await pulled;
  assert.equal(loadRecord(storage).garage.state.kart, 'petal');
  assert.equal(dueGarageBlock(storage), null, 'and the change is not applied back over itself');
});

test('stamp: later than anything seen, even when this device clock runs behind', () => {
  const storage = memory();
  saveRecord(withGarage(block(T2)), storage);
  const record = stampGarageBlock({ state: { kart: 'reverie' }, look: look() }, storage, Date.parse(T1));
  assert.equal(record.garage.updatedAt, new Date(Date.parse(T2) + 1).toISOString());
  assert.equal(loadRecord(storage).garage.state.kart, 'reverie');
  assert.equal(stampGarageBlock({ state: null, look: look() }, storage, Date.parse(T2) + 9), null, 'a bad block is not stamped');
  assert.equal(loadRecord(storage).garage.state.kart, 'reverie');
});

test('clamp: what is not owned falls back quietly and the block keeps the pick', () => {
  const picks = { kart: 'petal', outfit: 'ringmaster', paint: 'hearts', trail: 'podium', umbrella: 'lace', original: false, primary: '#112233' };
  const none = owned([]);
  const fit = fitGarageBlock(block(T1, picks), none.locked, none.owns);
  assert.equal(fit.clamped, true);
  assert.equal(fit.state.kart, FREE_KART);
  assert.equal(fit.state.outfit, DEFAULT.outfit);
  assert.equal(fit.state.paint, null);
  assert.equal(fit.state.trail, null);
  assert.equal(fit.state.umbrella, null);
  assert.equal(fit.state.primary, '#112233', 'free choices still come across');
  const all = owned(['cos.kart.body.petal', 'cos.kart.outfit.ringmaster', 'cos.kart.paint.hearts', 'cos.prime.trail.podium', 'cos.kart.umbrella.lace']);
  const worn = fitGarageBlock(block(T1, picks), all.locked, all.owns);
  assert.equal(worn.clamped, false);
  assert.equal(worn.state.kart, 'petal');
  assert.equal(worn.state.outfit, 'ringmaster');
  // junk reads as the defaults and is not a clamp
  const junk = fitGarageBlock(block(T1, { kart: 'nope', outfit: 'nope', primary: 'red', pattern: 99 }));
  assert.equal(junk.clamped, false);
  assert.deepEqual(junk.state, { ...cleanGarage(null), skin: 'Original' });
});

test('tracker: a load, a clamp or a repaint never stamps; a real change does, once', () => {
  const storage = memory(), pushed = [];
  storage.setItem(STORAGE, JSON.stringify({ kart: 'petal', outfit: 'ringmaster' }));
  const state = cleanGarage(JSON.parse(storage.getItem(STORAGE)), LOCKED, ownedLooks([]));
  assert.equal(state.kart, FREE_KART, 'the load clamped');
  const sync = { live: () => true, ready: () => true, push: (r) => pushed.push(r) };
  const tracker = garageTracker({ state, storage, sync });
  assert.equal(tracker.changed(), false, 'nothing moved: a repaint or a load');
  assert.equal(loadRecord(storage).garage, undefined);
  assert.equal(storage.getItem(GARAGE_SEEN_KEY), null);
  assert.equal(pushed.length, 0);

  state.outfit = 'varsity';
  assert.equal(tracker.changed(), true);
  assert.equal(tracker.changed(), false, 'the same state again is not another change');
  assert.equal(pushed.length, 1);
  const g = pushed[0].garage;
  assert.equal(g.state.outfit, 'varsity');
  assert.equal(g.look.vehicle, 'emi-vehicle-' + FREE_KART);
  assert.equal(g.look.outfit, 'emi-outfit-varsity');
  assert.equal(dueGarageBlock(storage), null, 'our own stamp is never owed back to us');
});

test('tracker: signed out or no access stays local only, with no block and no push', () => {
  for (const sync of [null, { live: () => false, ready: () => false, push() { throw new Error('no push'); } }]) {
    const storage = memory(), state = { ...DEFAULT };
    const tracker = garageTracker({ state, storage, sync });
    state.outfit = 'varsity';
    assert.equal(tracker.changed(), false);
    assert.equal(tracker.due(LOCKED, ownedLooks([])), null);
    assert.equal(storage.getItem(RECORD_KEY), null, 'the record is never written');
  }
});

test('tracker: a block waits for the first pull, applies once, and a clamped one comes back when a grant lands', () => {
  const storage = memory();
  let ready = false;
  const state = { ...DEFAULT }, looks = [];
  const tracker = garageTracker({ state, storage, sync: { live: () => true, ready: () => ready, push() {} }, adoptLook: (b) => looks.push(b.look) });
  saveRecord(withGarage(block(T1, { kart: 'petal', outfit: 'ringmaster' })), storage);
  const none = owned([]);
  assert.equal(tracker.due(none.locked, none.owns), null, 'not before the pull landed');
  ready = true;

  const first = tracker.due(none.locked, none.owns);
  assert.equal(first.clamped, true);
  assert.equal(first.state.kart, FREE_KART);
  Object.assign(state, first.state); tracker.applied(first);
  assert.equal(tracker.changed(), false, 'applying is not a player change');
  assert.equal(loadRecord(storage).garage.updatedAt, T1, 'and never restamps');
  assert.equal(looks.length, 1);

  const all = owned(['cos.kart.body.petal', 'cos.kart.outfit.ringmaster']);
  const again = tracker.due(all.locked, all.owns);
  assert.equal(again.clamped, false, 'same block, the grant has landed');
  assert.equal(again.state.kart, 'petal');
  assert.equal(again.state.outfit, 'ringmaster');
  Object.assign(state, again.state); tracker.applied(again);
  assert.equal(tracker.due(all.locked, all.owns), null, 'settled');

  saveRecord(withGarage(block(T2, { kart: 'reverie' })), storage);
  assert.equal(dueGarageBlock(storage).updatedAt, T2, 'a newer one is owed');
  saveGarageSeen(T2, false, storage);
  saveRecord(withGarage(block(T1)), storage);
  assert.equal(dueGarageBlock(storage), null, 'an older one is not');
});

test('lobby: a stepped look stamps with the garage as stored, and a pulled look lands fitted', () => {
  const storage = memory();
  storage.setItem(STORAGE, JSON.stringify({ kart: 'reverie', outfit: 'cheer' }));
  const record = stampLook(storage, { outfit: 'emi-outfit-varsity', vehicle: 'emi-vehicle-reverie', extra: 'x' });
  assert.equal(record.garage.state.kart, 'reverie');
  assert.deepEqual(record.garage.look, { outfit: 'emi-outfit-varsity', vehicle: 'emi-vehicle-reverie' });
  const empty = memory();
  assert.equal(stampLook(empty, look()).garage.state.kart, FREE_KART, 'no garage saved yet: the default');

  const device = memory();
  assert.equal(adoptBlockLook(device, block(T1, {}, { outfit: 'emi-outfit-ringmaster', vehicle: 'emi-vehicle-nope', paint: 'hearts' })), true);
  assert.deepEqual(JSON.parse(device.getItem(LOOK_KEY)), { outfit: 'emi-outfit-ringmaster', vehicle: 'emi-teacup-kart', paint: 'hearts' });
  const choices = lookChoices(null, null, { vehicles: new Set(), outfits: new Set(), paint: [], umbrella: [], trail: [] });
  const fit = fitLook(loadLook(device), choices);
  assert.equal(fit.outfit, null, 'the lobby takes an unowned outfit off');
  assert.equal(fit.paint, undefined);
});

test('wiring: only player actions stamp, and a newer block waits for an open garage at rest', () => {
  const read = (rel) => readFileSync(new URL(rel, import.meta.url), 'utf8').replace(/\r\n/g, '\n');
  const garage = read('../render/garage.js');
  assert.match(garage, /function save\(\)\{try\{storage\?\.setItem\(STORAGE,JSON\.stringify\(state\)\);\}catch\{\}\}/, 'a save never stamps');
  assert.doesNotMatch(garage.match(/function paint\(\)\{.*\}/)[0], /tracker/, 'nor a paint');
  assert.equal((garage.match(/tracker\.changed\(\)/g) || []).length, 3, 'a picker change, a finished turn, a Wear set without a turn');
  assert.match(garage, /function applySynced\(\)\{if\(!visible\|\|moving\|\|queued\|\|viewBusy\|\|custom\)return false;/);
  assert.match(garage, /paint\(\);applySynced\(\);\}/, 'each open and each new grant list looks again');
  const boot = read('../boot.js');
  assert.match(boot, /garageLink\.pulled = !profileSync\.off; garageLink\.closed = profileSync\.off;/, 'no access: no apply, no stamp, for the page');
  assert.match(boot, /live: \(\) => !!garageLink\.sync && !garageLink\.sync\.off && !garageLink\.closed/);
  assert.match(read('../hud/lobby.js'), /saveLook\(storage\(\), st\.look\);\n\s+deps\(\)\.lookChanged\?\.\(st\.look, storage\(\)\);/);
  const lobby = read('../hud/lobby.js');
  assert.equal((lobby.match(/lookChanged/g) || []).length, 1, 'a row step is the only lobby stamp');
});
