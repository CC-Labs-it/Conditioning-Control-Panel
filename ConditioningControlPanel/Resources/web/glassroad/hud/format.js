/* hud/format.js - pure helpers for the HUD and its cards (no DOM, no three).
 *
 * Time and place formatting, racer labels, item names and icons, the roll spin of the
 * item slot and the results rows. Everything here is tested in tests/hud-format.test.mjs.
 */

/** 75.256 -> '1:15.26'. Negative or bad input reads as 0. */
export function formatTime(s) {
  if (!(s >= 0)) s = 0;
  let m = Math.floor(s / 60), r = Math.round((s - m * 60) * 100) / 100;
  if (r >= 60) { m++; r -= 60; }
  return m + ':' + (r < 10 ? '0' : '') + r.toFixed(2);
}

/** 1 -> { n: '1', suf: 'st' }; 12 -> 'th'; 23 -> 'rd'. */
export function ordinal(place) {
  const n = Math.max(0, Math.round(place) || 0);
  const t = n % 100, o = n % 10;
  const suf = t >= 11 && t <= 13 ? 'th' : o === 1 ? 'st' : o === 2 ? 'nd' : o === 3 ? 'rd' : 'th';
  return { n: String(n), suf };
}

/** Short names of our own for bots that share the generic name. No real people. */
export const BOT_NAMES = ['Velvet', 'Hush', 'Lull', 'Murmur', 'Static', 'Glimmer', 'Sway'];

/**
 * Display labels for RacerMeta[] (index = racer i). The player reads 'You'. A bot keeps its own
 * name unless another racer has the same one; then it takes BOT_NAMES by its tint (1..7).
 * @param {{ i: number, name?: string, isBot?: boolean, tint?: number }[]} metas
 * @returns {string[]}
 */
export function racerLabels(metas = []) {
  const count = new Map();
  for (const m of metas) if (m && m.name) count.set(m.name, (count.get(m.name) || 0) + 1);
  const out = [];
  for (let k = 0; k < metas.length; k++) {
    const m = metas[k] || {};
    const i = m.i ?? k;
    if (!m.isBot && i === 0) { out[i] = 'You'; continue; }
    const dup = !m.name || count.get(m.name) > 1;
    const t = (m.tint > 0 ? m.tint : i) - 1;
    out[i] = dup ? BOT_NAMES[((t % BOT_NAMES.length) + BOT_NAMES.length) % BOT_NAMES.length] : m.name;
  }
  return out;
}

/** Item ids (sim/itemTable.js ITEM_IDS) to our names and icons (art/ART.md "fx"). */
export const ITEM_NAMES = {
  flash: 'Flash', spiral: 'Spiral', brainDrain: 'Brain Drain', freeze: 'Freeze', blackout: 'Blackout',
  pink: 'Fog', melt: 'Melt', shatter: 'Shatter', surge: 'Surge', glass: 'Bubble Shield', echo: 'Echo', golden: 'Golden',
  pendulum: 'Pendulum', disc: 'Disc', tether: 'Tether', snap: 'Snap', bigSpiral: 'Big Spiral',
};
const ICON_FILES = {
  flash: 'flash', spiral: 'spiral', brainDrain: 'braindrain', freeze: 'bambifreeze', blackout: 'blackout',
  pink: 'pinkfilter', melt: 'melt', shatter: 'prism', surge: 'gold_droplet', glass: 'bubble', echo: 'glitch', golden: 'golden',
  // Directional enamel frames match the world atlas and sim/itemTable.js.
  pendulum: 'pendulum', disc: 'disc', tether: 'tether', snap: 'snap', bigSpiral: 'bigspiral',
};
export const ICON_IDS = Object.keys(ICON_FILES);
/** 'spiral' -> 'art/fx/spiral.png' (relative to the page); unknown -> the empty bubble. */
export function iconOf(id) { return 'art/fx/' + (ICON_FILES[id] || 'bubble') + '.png'; }
export function itemName(id) { return ITEM_NAMES[id] || 'Item'; }

/**
 * Which icon the slot shows while rolling: fast at first, slowing to a stop on the drawn item.
 * Ticks follow t' = t * (2 - t) (ease out) over rollS, `turns` icon changes in all; the last
 * stretch (k >= settle) shows the drawn id. Gentle caps the change rate (no flicker).
 * @returns {{ id: string, tick: number }} tick changes exactly when the icon changes
 */
export function rollFrame(rollT, rollS, drawnId, { ids = ICON_IDS, turns = 14, settle = 0.85, gentle = false, gentleHz = 3 } = {}) {
  const k = rollS > 0 ? Math.min(1, Math.max(0, rollT / rollS)) : 1;
  if (k >= settle || !ids.length) return { id: drawnId, tick: -1 };
  const n = gentle ? Math.max(1, Math.floor(Math.max(0, rollT) * gentleHz)) : Math.floor(k * (2 - k) * turns);
  return { id: ids[n % ids.length], tick: n };
}

/**
 * Results rows for the card from a standings Result. Bot names go through racerLabels when metas
 * are given. Unfinished racers keep their projected time and read as estimated.
 * @returns {{ place: string, suf: string, name: string, time: string, est: boolean, me: boolean, best: string }[]}
 */
export function resultRows(result, metas = null) {
  const labels = metas ? racerLabels(metas) : null;
  return (result?.rows || []).map((r) => {
    const o = ordinal(r.place);
    return {
      place: o.n, suf: o.suf,
      name: r.me ? 'You' : (labels && labels[r.i]) || r.name || 'Bot',
      time: formatTime(r.timeS), est: r.finished === false, me: !!r.me,
      best: r.bestLapS > 0 ? formatTime(r.bestLapS) : '',
    };
  });
}

/** The pause card line: 'Threshold, lap 2 of 3, 3rd'. */
export function pauseLine({ track = '', lap = 0, laps = 0, place = 0, count = 0 } = {}) {
  const parts = [];
  if (track) parts.push(track);
  if (laps > 0) parts.push('lap ' + Math.min(Math.max(1, lap + 1), laps) + ' of ' + laps);
  if (place > 0) { const o = ordinal(place); parts.push(o.n + o.suf + (count > 0 ? ' of ' + count : '')); }
  return parts.join(', ');
}

/** Top speed the km/h readout shows (speed.shownTopMps), else the real top. The real top moved (33 m/s, F = 1.1) while the
 * numbers a player reads stay the ones of before; a feel never gains keys, so a miss is remembered per feel. */
const SHOWN_MISS = new WeakSet();
export function shownTopMps(feel) {
  if (!SHOWN_MISS.has(feel)) { try { return feel.get('speed.shownTopMps'); } catch { SHOWN_MISS.add(feel); } }
  return feel.get('speed.topMps');
}
/** The HUD km/h figure for a share of top speed. */
export const shownKmh = (speedPct, feel) => Math.round(Math.max(0, speedPct) * shownTopMps(feel) * 3.6);
