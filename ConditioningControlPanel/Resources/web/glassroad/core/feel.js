/* core/feel.js - the one place feel numbers are read from.
 *
 * Every tuning number lives in feel.json (see FEEL.md). Code reads it with
 * feel.get('group.key') each tick, so a live edit applies on the next tick.
 * Units are in the key name: ...Mps, ...Mps2, ...S, ...M, ...Deg, ...Degps.
 * Angles are degrees in the file; read them with feel.rad(path) to get radians.
 * The ?feel=1 sheet (hud lane) edits through set(); this module only stores.
 */

function walk(root, path) {
  const parts = path.split('.');
  let o = root;
  for (let i = 0; i < parts.length - 1; i++) {
    if (o == null || typeof o !== 'object') return [null, null];
    o = o[parts[i]];
  }
  return [o, parts[parts.length - 1]];
}

/**
 * @param {object} data parsed feel.json (deep copied)
 * @returns {Feel}
 * @typedef {{get(path: string): any, rad(path: string): number|number[], set(path: string, value: any): boolean,
 *   subscribe(fn: (path: string, value: any) => void): () => void, toJSON(): object, reset(): void}} Feel
 */
export function createFeel(data) {
  const base = JSON.parse(JSON.stringify(data || {}));
  let cur = JSON.parse(JSON.stringify(base));
  const subs = new Set();

  function get(path) {
    const [o, k] = walk(cur, path);
    const v = o == null ? undefined : o[k];
    if (v === undefined) throw new Error('[feel] missing key ' + path);
    return v;
  }
  function rad(path) {
    const v = get(path);
    return Array.isArray(v) ? v.map((d) => d * Math.PI / 180) : v * Math.PI / 180;
  }
  function set(path, value) {
    const [o, k] = walk(cur, path);
    if (o == null || !(k in o)) return false;
    o[k] = value;
    for (const fn of subs) { try { fn(path, value); } catch (e) { console.warn('[feel]', e); } }
    return true;
  }
  function subscribe(fn) { subs.add(fn); return () => subs.delete(fn); }
  function toJSON() { return JSON.parse(JSON.stringify(cur)); }
  function reset() {
    cur = JSON.parse(JSON.stringify(base));
    for (const fn of subs) { try { fn('*', null); } catch (e) { console.warn('[feel]', e); } }
  }
  return { get, rad, set, subscribe, toJSON, reset };
}

/** Browser: fetch feel.json next to the page. @returns {Promise<Feel>} */
export async function loadFeel(url = new URL('../feel.json', import.meta.url).href) {
  const res = await fetch(url, { cache: 'no-store' });
  if (!res.ok) throw new Error('[feel] ' + res.status + ' ' + url);
  return createFeel(await res.json());
}
