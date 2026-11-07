/* ============================================================================
 * skins/wear.js - who wears what on this board, and the player's own pick.
 *
 * The player's look is theirs to pick from what they own; the board decides
 * which side wears it:
 *   menu, solo        the player's side wears it, the computer's side is bare
 *   two here          both sides (one person, one look)
 *   online            the player's seat at once, then the match state's
 *                     `looks: { white, black }` (contract 4) for both seats
 *   watching          bare until the watch head brings `looks`
 *
 * Ownership comes from the host (`{ type: 'owned', grants }`, the same frame
 * Glassroad gets, re-sent when the Prize Counter closes). The host also owns
 * the synced pick: it sends `{ type: 'pbp:look', look }` at boot and after an
 * adopted server copy, and the page sends the same frame back on a pick
 * (contract 3, the host writes locally first and PUTs /v2/equip). Unhosted, the
 * pick lives in localStorage and nothing is owned, so nothing is worn.
 *
 *   createWear({ bus, pieces, post, onHost, storage, hosted }) ->
 *     { mine(), owned(), choose(slot, id), openCounter(), onChange(fn), sides(), debug }
 * ==========================================================================*/

import { SLOTS, CATALOG, EMPTY, normalLook, clampLook, ownedIds, sameLook, counterFrame } from './look.js';

const KEY = 'pbp.look.v1';

function readSaved(storage) {
  try { const raw = storage && storage.getItem(KEY); return raw ? normalLook(JSON.parse(raw)) : { ...EMPTY }; }
  catch { return { ...EMPTY }; }
}
function writeSaved(storage, look) {
  try { if (storage) storage.setItem(KEY, JSON.stringify(look)); } catch { /* private window */ }
}

export function createWear({ bus = null, pieces = null, post = () => {}, onHost = null, storage = null, hosted = false } = {}) {
  let picked = readSaved(storage);          // what the player chose (may name an item they no longer hold)
  let grants = null;                        // null until the host says; unhosted stays null
  let mode = 'menu';
  let seat = 'w';
  let server = null;                        // { w, b } from the match state or watch head
  const listeners = new Set();
  const off = [];

  /** The player's look as it is WORN: only what the grants own. */
  const mine = () => clampLook(picked, grants || []);

  function sides() {
    const me = mine();
    if (server) return { w: normalLook(server.w), b: normalLook(server.b) };
    if (mode === 'hotseat') return { w: me, b: me };
    if (mode === 'watch') return { w: { ...EMPTY }, b: { ...EMPTY } };
    const other = seat === 'w' ? 'b' : 'w';
    return { [seat]: me, [other]: { ...EMPTY } };
  }

  let last = null;
  function apply() {
    const s = sides();
    if (last && sameLook(last.w, s.w) && sameLook(last.b, s.b)) return;
    last = s;
    try { if (pieces && pieces.setLooks) pieces.setLooks(s); } catch { /* the board keeps its men */ }
  }

  function changed() {
    apply();
    for (const fn of listeners) { try { fn(); } catch { /* one bad listener */ } }
  }

  /** A pick. Refused for an item the player does not own; null takes the slot off. */
  function choose(slot, id) {
    if (!SLOTS.includes(slot)) return false;
    if (id !== null && !(CATALOG[slot].includes(id) && ownedIds(grants || []).has(id))) return false;
    if (picked[slot] === id) return true;
    picked = { ...picked, [slot]: id };
    writeSaved(storage, picked);
    if (hosted) post({ type: 'pbp:look', look: normalLook(picked) });
    changed();
    return true;
  }

  function onFrame(m) {
    if (!m || typeof m.type !== 'string') return;
    if (m.type === 'owned' && Array.isArray(m.grants)) { grants = m.grants.filter((g) => typeof g === 'string'); changed(); }
    else if (m.type === 'pbp:look') { picked = normalLook(m.look); writeSaved(storage, picked); changed(); }
  }

  if (typeof onHost === 'function') { const u = onHost(onFrame); if (typeof u === 'function') off.push(u); }

  if (bus && typeof bus.on === 'function') {
    const on = (name, fn) => { const u = bus.on(name, fn); if (typeof u === 'function') off.push(u); };
    on('local', (p) => {
      mode = (p && p.mode) || 'hotseat';
      server = null;
      const m = p && p.match;
      seat = (p && (p.me === 'w' || p.me === 'b')) ? p.me
        : (m && (m.side === 'w' || m.side === 'b')) ? m.side
          : (m && (m.color === 'w' || m.color === 'b')) ? m.color : 'w';
      apply();
    });
    on('seat', (p) => { if (p && (p.color === 'w' || p.color === 'b')) { seat = p.color; apply(); } });
    on('looks', (p) => {
      if (!p || typeof p !== 'object') return;
      server = { w: normalLook(p.white), b: normalLook(p.black) };
      apply();
    });
  }

  return {
    mine,
    picked: () => normalLook(picked),
    owned: () => ownedIds(grants || []),
    known: () => grants !== null,
    choose,
    sides,
    apply,
    /** The menu's board: the player's side wears the pick (boot calls this when the door shows the menu). */
    toMenu() { mode = 'menu'; seat = 'w'; server = null; apply(); },
    openCounter() { post(counterFrame()); },
    onChange(fn) { listeners.add(fn); return () => listeners.delete(fn); },
    debug: () => ({ picked: normalLook(picked), grants, mode, seat, server, worn: sides() }),
    dispose() { for (const u of off.splice(0)) { try { u(); } catch { /* fine */ } } listeners.clear(); },
  };
}
