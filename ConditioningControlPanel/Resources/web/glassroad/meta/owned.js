/* meta/owned.js - the Prize Counter grants the garage reads (owner 2026-10-29: kart bodies are sold, the garage
 * offers only owned ones; CONTRACT.md section 7 "Owned kart bodies").
 *
 * createOwned({ storage, uid }) -> { grants, setAccount(uid), adopt(list) }. Sources, newest wins, each a FULL
 * snapshot (never merged): the host init's `identity.grants`, a host `owned { grants }` push (desktop, after a buy)
 * and the `grants` of GET /v2/race/profile (meta/profileSync.js, every platform). Only `cos.kart.*` ids are kept.
 * The last snapshot is remembered per account under `glassroad.owned.v1`, so the garage opens with it before the
 * profile answers; another account, or none, owns nothing. A client lock only: rooms take any listed vehicle id
 * (NET.md) and a look never reaches the sim or the course hash. Storage access always in try/catch.
 */
export const OWNED_KEY = 'glassroad.owned.v1';
export const MAX_GRANTS = 64;
const GRANT_RE = /^cos\.kart\.[a-z0-9-]+\.[a-z0-9-]+$/;

/** A grant list cleaned to known-shape `cos.kart.*` ids, sorted and capped; null when `list` is not an array. */
export function cleanGrants(list) {
  if (!Array.isArray(list)) return null;
  return [...new Set(list.filter(g => typeof g === 'string' && g.length <= 64 && GRANT_RE.test(g)))].sort().slice(0, MAX_GRANTS);
}

function defaultStorage() { try { return globalThis.localStorage || null; } catch (e) { return null; } }

export function createOwned({ storage = defaultStorage(), uid = null } = {}) {
  const idOf = v => (typeof v === 'string' && v ? v : null);
  let account = idOf(uid);
  const read = () => {
    if (!account) return [];
    try { const o = JSON.parse(storage?.getItem(OWNED_KEY) || 'null'); return o && o.uid === account ? cleanGrants(o.grants) || [] : []; } catch (e) { return []; }
  };
  let grants = read();
  return {
    get grants() { return grants; },
    /** The signed-in account (init identity); a different one swaps to its own remembered snapshot. */
    setAccount(id) { const next = idOf(id); if (next !== account) { account = next; grants = read(); } },
    /** Adopt a full snapshot; junk is ignored. @returns {boolean} true when adopted */
    adopt(list) {
      const next = cleanGrants(list);
      if (!next) return false;
      grants = next;
      if (account) try { storage?.setItem(OWNED_KEY, JSON.stringify({ uid: account, grants })); } catch (e) { /* private window */ }
      return true;
    },
  };
}
