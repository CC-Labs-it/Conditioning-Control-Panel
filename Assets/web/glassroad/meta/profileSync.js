/* meta/profileSync.js - the account record (CONTRACT-LOGBOOK.md section 8).
 *
 * createProfileSync({ request, storage }) where `request` is the lobby transport (net/lobbyApi.js
 * fetchTransport / hostTransport: (method, path, body) -> { status, body }).
 *   pull()        GET /v2/race/profile, merge it into the local record (mergeRecords), save, return the merge;
 *                 when the local side holds anything the account lacks, push the merge back up.
 *   push(record)  POST /v2/race/profile { record }, debounced, one call in flight, a later push folds into the
 *                 waiting one. POST (the server takes PUT too) because the desktop host relay forwards GET and
 *                 POST only.
 * onGrants(list) gets the `grants` array a GET answers (the Prize Counter's `cos.kart.*`, meta/owned.js).
 * The record's `garage` block rides both ways: every merge here reads the local record at answer time, so a look
 * stamped while a call was in the air stays on top; applying a pulled block is the garage's job (render/garage.js).
 * Never throws, never blocks a race. Offline (status 0) drops the call; signed out or no access (401, 403,
 * 404) switches the sync off for the page's life, so the logbook stays local only.
 */
import { cleanRecord, loadRecord, mergeRecords, saveRecord } from './record.js';

export const PROFILE_PATH = '/v2/race/profile';
export const PUSH_DEBOUNCE_MS = 2500;
/** Under the desktop relay's 16 KB body cap with room for the unified_id it adds. */
export const MAX_PUSH_BYTES = 15 * 1024;

const OFF_STATUS = new Set([401, 403, 404]);
const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);

export function createProfileSync({ request, storage, timers = globalThis, debounceMs = PUSH_DEBOUNCE_MS, onGrants = null } = {}) {
  let off = typeof request !== 'function';
  let pending = null, timer = null, flight = null;
  const stats = { pulls: 0, pushes: 0, failures: 0 };

  async function call(method, body) {
    try {
      const r = await request(method, PROFILE_PATH, body);
      const status = (r && r.status) | 0;
      if (status >= 200 && status < 300 && r.body && r.body.record) {
        if (method === 'GET' && Array.isArray(r.body.grants) && typeof onGrants === 'function') try { onGrants(r.body.grants); } catch (e) { /* the garage is optional */ }
        return r.body.record;
      }
      if (OFF_STATUS.has(status)) off = true;
    } catch (e) { /* offline or a broken bridge: silent */ }
    stats.failures++;
    return null;
  }

  /** Fold an account record into the local one and save it; returns the merge. */
  function adopt(remote) {
    const merged = mergeRecords(loadRecord(storage), remote);
    saveRecord(merged, storage);
    return merged;
  }

  async function pull() {
    try {
      if (off) return loadRecord(storage);
      stats.pulls++;
      const remote = await call('GET');
      if (!remote) return loadRecord(storage);
      const merged = adopt(remote);
      if (!same(merged, cleanRecord(remote))) push(merged);
      return merged;
    } catch (e) { return loadRecord(storage); }
  }

  function push(record) {
    try {
      if (off || !record || typeof record !== 'object') return;
      pending = pending ? mergeRecords(pending, record) : cleanRecord(record);
      if (timer) timers.clearTimeout(timer);
      timer = timers.setTimeout(() => { timer = null; void send(); }, debounceMs);
    } catch (e) { /* never throws */ }
  }

  async function send() {
    if (flight) { await flight.catch(() => {}); if (timer || !pending) return; }
    if (off || !pending) { pending = null; return; }
    const body = pending;
    pending = null;
    try { if (JSON.stringify(body).length > MAX_PUSH_BYTES) { stats.failures++; return; } } catch (e) { return; }
    flight = (async () => {
      stats.pushes++;
      const remote = await call('POST', { record: body });
      if (remote) adopt(remote);
    })();
    try { await flight; } finally { flight = null; }
  }

  /** Send the waiting push now (tests and page unload); resolves when it lands or fails. */
  async function flush() {
    try {
      if (timer) { timers.clearTimeout(timer); timer = null; }
      if (flight) await flight.catch(() => {});
      await send();
    } catch (e) { /* never throws */ }
  }

  return { pull, push, flush, get off() { return off; }, stats };
}
