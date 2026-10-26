import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createProfileSync, PROFILE_PATH, MAX_PUSH_BYTES } from '../meta/profileSync.js';
import { emptyRecord, loadRecord, saveRecord, mergeRecords, RECORD_KEY } from '../meta/record.js';

const memory = () => { const m = new Map(); return { getItem: (k) => (m.has(k) ? m.get(k) : null), setItem: (k, v) => m.set(k, String(v)), m }; };

/** A fake clock: setTimeout queues, run() fires everything due. */
function fakeTimers() {
  let now = 0, next = 1;
  const q = new Map();
  return {
    setTimeout(fn, ms) { const id = next++; q.set(id, { fn, at: now + ms }); return id; },
    clearTimeout(id) { q.delete(id); },
    advance(ms) { now += ms; for (const [id, t] of [...q]) if (t.at <= now) { q.delete(id); t.fn(); } },
    get pending() { return q.size; },
  };
}
const tick = () => new Promise((r) => setImmediate(r));

/** A fake account server that keeps one record and answers like CCP-Server /v2/race/profile. */
function fakeServer({ status = 200, stored = emptyRecord() } = {}) {
  const calls = [];
  let inFlight = 0, maxInFlight = 0;
  const request = async (method, path, body) => {
    calls.push({ method, path, body });
    inFlight++; maxInFlight = Math.max(maxInFlight, inFlight);
    await tick();
    inFlight--;
    if (typeof status === 'function') { const s = status(); if (s !== 200) return { status: s, body: { error: 'x' } }; }
    else if (status !== 200) return { status, body: status ? { error: 'x' } : null };
    if (method !== 'GET') stored = mergeRecords(stored, body.record);
    return { status: 200, body: { ok: true, record: stored } };
  };
  return { request, calls, get stored() { return stored; }, get maxInFlight() { return maxInFlight; } };
}

test('logbook sync: pull merges the account record into the local one and saves it', async () => {
  const storage = memory(), timers = fakeTimers();
  const local = emptyRecord();
  local.courses.keep.races = 3; local.courses.keep.bestRaceS = 200; local.unlocks = ['kart:undertow'];
  saveRecord(local, storage);
  const remote = emptyRecord();
  remote.courses.keep.races = 9; remote.courses.keep.bestRaceS = 210; remote.achievements.licensed = '2026-10-20T00:00:00Z';
  const srv = fakeServer({ stored: remote });
  const sync = createProfileSync({ request: srv.request, storage, timers });
  const merged = await sync.pull();
  assert.equal(srv.calls[0].method, 'GET'); assert.equal(srv.calls[0].path, PROFILE_PATH);
  assert.equal(merged.courses.keep.races, 9); assert.equal(merged.courses.keep.bestRaceS, 200);
  assert.deepEqual(merged.unlocks, ['kart:undertow']); assert.ok(merged.achievements.licensed);
  assert.deepEqual(loadRecord(storage), merged);
  // the local side held more than the account: the merge goes back up after the debounce
  timers.advance(5000); await tick(); await tick(); await tick();
  assert.equal(srv.calls.length, 2); assert.equal(srv.calls[1].method, 'POST');
  assert.equal(srv.stored.courses.keep.bestRaceS, 200);
});

test('logbook sync: a pull that adds nothing new does not push', async () => {
  const storage = memory(), timers = fakeTimers();
  const remote = emptyRecord(); remote.totals.races = 4;
  const srv = fakeServer({ stored: remote });
  const sync = createProfileSync({ request: srv.request, storage, timers });
  await sync.pull();
  timers.advance(5000); await tick();
  assert.equal(srv.calls.length, 1);
  assert.equal(loadRecord(storage).totals.races, 4);
});

test('logbook sync: push debounces, folds bursts into one POST and keeps one call in flight', async () => {
  const storage = memory(), timers = fakeTimers(), srv = fakeServer();
  const sync = createProfileSync({ request: srv.request, storage, timers, debounceMs: 1000 });
  const a = emptyRecord(); a.totals.races = 1;
  const b = emptyRecord(); b.totals.races = 2; b.unlocks = ['hat:umbrella'];
  sync.push(a); timers.advance(500); sync.push(b);
  timers.advance(999); await tick();
  assert.equal(srv.calls.length, 0);
  timers.advance(1); await tick();
  assert.equal(srv.calls.length, 1);
  assert.equal(srv.calls[0].body.record.totals.races, 2); assert.deepEqual(srv.calls[0].body.record.unlocks, ['hat:umbrella']);
  // a push while that call is still out waits for it
  const c = emptyRecord(); c.totals.races = 3;
  sync.push(c); timers.advance(1000);
  await tick(); await tick(); await tick();
  assert.equal(srv.calls.length, 2); assert.equal(srv.maxInFlight, 1);
  assert.equal(srv.stored.totals.races, 3);
  assert.equal(loadRecord(storage).totals.races, 3);   // the answer is folded into the local record
});

test('logbook sync: flush sends now', async () => {
  const srv = fakeServer(), timers = fakeTimers();
  const sync = createProfileSync({ request: srv.request, storage: memory(), timers });
  const r = emptyRecord(); r.totals.wins = 1;
  sync.push(r); await sync.flush();
  assert.equal(srv.calls.length, 1); assert.equal(timers.pending, 0);
});

test('logbook sync: signed out switches it off quietly, offline drops and retries later', async () => {
  const storage = memory(), timers = fakeTimers();
  const local = emptyRecord(); local.totals.races = 2; saveRecord(local, storage);
  const out = fakeServer({ status: 401 });
  const sync = createProfileSync({ request: out.request, storage, timers });
  assert.equal((await sync.pull()).totals.races, 2);
  assert.equal(sync.off, true);
  sync.push(local); timers.advance(10000); await tick();
  assert.equal(out.calls.length, 1);
  assert.equal(loadRecord(storage).totals.races, 2);

  let s = 0;
  const flaky = fakeServer({ status: () => s });
  const sync2 = createProfileSync({ request: flaky.request, storage, timers });
  sync2.push(local); await sync2.flush();
  assert.equal(sync2.off, false);
  s = 200; sync2.push(local); await sync2.flush();
  assert.equal(flaky.calls.length, 2); assert.equal(flaky.stored.totals.races, 2);
});

test('logbook sync: a throwing transport, bad input or no transport never throws', async () => {
  const storage = memory(), timers = fakeTimers();
  const boom = async () => { throw new Error('bridge gone'); };
  const sync = createProfileSync({ request: boom, storage, timers });
  assert.deepEqual(await sync.pull(), emptyRecord());
  sync.push(null); sync.push('x'); sync.push(emptyRecord());
  await sync.flush();
  assert.equal(sync.stats.failures, 2);
  const none = createProfileSync({ storage, timers });
  assert.equal(none.off, true);
  assert.deepEqual(await none.pull(), emptyRecord());
  none.push(emptyRecord()); assert.equal(timers.pending, 0);
  // a blocked storage still answers a record
  const blocked = { getItem() { throw Error('denied'); }, setItem() { throw Error('denied'); } };
  const srv = fakeServer();
  assert.deepEqual(await createProfileSync({ request: srv.request, storage: blocked, timers }).pull(), emptyRecord());
  assert.ok(!storage.m.has('nothing') && storage.m.has(RECORD_KEY) === false);
});

test('logbook sync: a record past the relay cap stays local', async () => {
  const srv = fakeServer(), timers = fakeTimers();
  const sync = createProfileSync({ request: srv.request, storage: memory(), timers });
  const r = emptyRecord();
  for (let i = 0; i < 400; i++) r.achievements['a' + i + '-' + 'x'.repeat(40)] = '2026-10-26T10:00:00.000Z';
  assert.ok(JSON.stringify(mergeRecords(r, null)).length > MAX_PUSH_BYTES);
  sync.push(r); await sync.flush();
  assert.equal(srv.calls.length, 0);
});

test('record: an empty updatedAt never blanks the other side, merge stays commutative', () => {
  const a = emptyRecord(), b = emptyRecord();
  b.updatedAt = '2026-10-26T10:00:00Z';
  assert.equal(mergeRecords(a, b).updatedAt, b.updatedAt);
  assert.equal(mergeRecords(b, a).updatedAt, b.updatedAt);
  a.updatedAt = '2026-10-27T10:00:00Z';
  assert.equal(mergeRecords(a, b).updatedAt, a.updatedAt);
  assert.deepEqual(mergeRecords(a, b), mergeRecords(b, a));
});
