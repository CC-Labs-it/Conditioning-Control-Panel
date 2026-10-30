/* input/device.js - which device the player used last, and the pad notes, shared by the inputs and
 * the HUD (no DOM, no three). The inputs call use(); the gamepad calls connected() and lost(); the
 * HUD listens to show the right button names and the "Controller connected" note.
 * One shared instance (`devices`); createDevices() makes a fresh one for tests.
 */
import { saveChoice } from './padmap.js';

/**
 * @typedef {{ id: string, family: 'xbox'|'ps'|'generic', profile: string, standard: boolean }} PadInfo
 * Events to on(fn): fn(type, info) with type 'use' | 'connect' | 'lost' | 'layout'.
 */
export function createDevices({ last: first = 'keyboard', save = saveChoice } = {}) {
  let last = first, pad = null;
  const fns = new Set();
  const emit = (type, info) => { for (const fn of [...fns]) { try { fn(type, info); } catch (e) { /* a listener never breaks input */ } } };
  return {
    /** 'keyboard' | 'pad' | 'touch' */
    get last() { return last; },
    /** @returns {PadInfo|null} the pad seen last (kept after it leaves, for the labels) */
    get pad() { return pad; },
    use(kind, info = null) {
      const changed = kind !== last || (info && (!pad || info.id !== pad.id || info.profile !== pad.profile));
      last = kind;
      if (info) pad = info;
      if (changed) emit('use', { kind });
    },
    connected(info) { pad = info; emit('connect', info); },
    /** info.active: the pad was being played with when it left. */
    lost(info) { if (last === 'pad' && info && info.active) last = 'keyboard'; emit('lost', info); },
    setLayout(id, profile) {
      save(id, profile);
      if (pad && pad.id === id) pad = { ...pad, profile };
      emit('layout', { id, profile });
    },
    on(fn) { fns.add(fn); return () => fns.delete(fn); },
  };
}

export const devices = createDevices();
