/* input/padmap.js - gamepad layouts, pure (no DOM, no three; tested in tests/hud-pad.test.mjs).
 *
 * A pad with mapping 'standard' reads the W3C standard layout. Anything else gets a best-effort
 * fallback: 'xinput' is the common generic layout of an Xbox-style pad without the standard
 * mapping (triggers on axes 2 and 5, d-pad on axes 6 and 7), 'dinput' the common generic HID
 * layout (face buttons west, south, east, north as 0..3, triggers as buttons 6 and 7, d-pad on a
 * hat axis 9 or buttons 12..15). The player can pick another layout on the Controls card; that
 * choice is kept per gamepad id in localStorage (every access in try/catch). Axes are never
 * inverted or swapped: left stays left.
 */

export const PROFILES = Object.freeze({
  standard: { label: 'Standard', south: 0, east: 1, west: 2, north: 3, lb: 4, rb: 5, lt: { b: 6 }, rt: { b: 7 }, select: 8, start: 9, dpad: { b: [12, 13, 14, 15] } },
  xinput: { label: 'Generic A', south: 0, east: 1, west: 2, north: 3, lb: 4, rb: 5, lt: { axis: 2 }, rt: { axis: 5 }, select: 6, start: 7, dpad: { axes: [6, 7], b: [12, 13, 14, 15] } },
  dinput: { label: 'Generic B', south: 1, east: 2, west: 0, north: 3, lb: 4, rb: 5, lt: { b: 6 }, rt: { b: 7 }, select: 8, start: 9, dpad: { hat: 9, b: [12, 13, 14, 15] } },
});
export const PROFILE_ORDER = ['standard', 'xinput', 'dinput'];
export const PADMAP_KEY = 'glassroad.padmap.v1';

const lower = (g) => String((g && g.id) || '').toLowerCase();
const has = (s, words) => words.some((w) => s.includes(w));
const PS_WORDS = ['054c', 'dualsense', 'dualshock', 'wireless controller'];
const XBOX_WORDS = ['xbox', 'xinput', 'x-box', '045e'];

/** 'ps' | 'xbox' | 'generic': which button names to show for this pad. */
export function padFamily(g) {
  const id = lower(g);
  if (has(id, PS_WORDS)) return 'ps';
  if (has(id, XBOX_WORDS) || (g && g.mapping === 'standard')) return 'xbox';
  return 'generic';
}

/** The fallback layout for a pad by its mapping, id and shape. */
export function guessProfile(g) {
  if (!g || g.mapping === 'standard') return 'standard';
  const id = lower(g), nb = (g.buttons && g.buttons.length) || 0, na = (g.axes && g.axes.length) || 0;
  if (has(id, XBOX_WORDS) || (na === 8 && nb === 11)) return 'xinput';
  if (nb >= 17 && na <= 4) return 'standard';    // a standard-shaped pad the browser did not tag
  return 'dinput';
}

const store = () => { try { return globalThis.localStorage || null; } catch (e) { return null; } };

export function loadChoices(storage = store()) {
  try {
    const raw = JSON.parse((storage && storage.getItem(PADMAP_KEY)) || 'null');
    if (!raw || typeof raw !== 'object') return {};
    const out = {};
    for (const k of Object.keys(raw)) if (PROFILES[raw[k]]) out[k] = raw[k];
    return out;
  } catch (e) { return {}; }
}

/** Keep (or with a bad name, forget) the layout for one pad id. Returns false when storage refuses. */
export function saveChoice(id, profile, storage = store()) {
  try {
    if (!storage || !id) return false;
    const all = loadChoices(storage);
    if (PROFILES[profile]) all[id] = profile; else delete all[id];
    storage.setItem(PADMAP_KEY, JSON.stringify(all));
    return true;
  } catch (e) { return false; }
}

/** The kept choice for this pad id, else the guess. Standard pads always read standard. */
export function profileFor(g, choices = null) {
  if (!g || g.mapping === 'standard') return 'standard';
  const c = choices || loadChoices();
  const id = (g && g.id) || '';
  return PROFILES[c[id]] ? c[id] : guessProfile(g);
}

/** The next layout in the cycle (Controls card button). */
export function nextProfile(name) {
  const i = PROFILE_ORDER.indexOf(name);
  return PROFILE_ORDER[(i + 1) % PROFILE_ORDER.length];
}

const clamp01 = (v) => (v > 1 ? 1 : v > 0 ? v : 0);

/**
 * Chrome's POV hat axis: up = -1, then clockwise in steps of 2/7; anything off a step (rest is
 * about 1.29, and an idle axis may read 0) is no direction.
 * @returns {number} 0 up, 1 up right, 2 right ... 7 up left, or -1
 */
export function hatDir(v) {
  if (!(v >= -1.05 && v <= 1.05)) return -1;
  const k = (v + 1) * 3.5, n = Math.round(k);
  return Math.abs(k - n) < 0.2 && n >= 0 && n <= 7 ? n : -1;
}

/**
 * Read one pad through a layout into plain values (out is reused). `seen` keeps, per axis, whether a
 * trigger axis has moved yet: an axis trigger rests at -1 but some browsers report 0 until it is
 * first touched, which would read as half throttle.
 */
export function readPad(g, profile, seen = {}, out = {}) {
  const p = typeof profile === 'string' ? PROFILES[profile] || PROFILES.standard : profile || PROFILES.standard;
  const B = (g && g.buttons) || [], A = (g && g.axes) || [];
  const bv = (i) => {
    const b = i >= 0 ? B[i] : null;
    if (!b) return 0;
    const v = typeof b.value === 'number' ? b.value : 0;
    return Math.max(v, b.pressed ? 1 : 0);
  };
  const ax = (i) => (i >= 0 && i < A.length ? A[i] || 0 : 0);
  const trig = (t) => {
    if (!t) return 0;
    if (t.axis == null) return clamp01(bv(t.b));
    const v = ax(t.axis);
    if (Math.abs(v) > 0.5) seen[t.axis] = true;
    return seen[t.axis] ? clamp01((v + 1) / 2) : 0;
  };
  out.x = ax(0); out.y = ax(1);
  out.south = bv(p.south) > 0.5; out.east = bv(p.east) > 0.5; out.west = bv(p.west) > 0.5; out.north = bv(p.north) > 0.5;
  out.lb = bv(p.lb) > 0.5; out.rb = bv(p.rb) > 0.5; out.select = bv(p.select) > 0.5; out.start = bv(p.start) > 0.5;
  out.lt = trig(p.lt); out.rt = trig(p.rt);
  let up = false, down = false, left = false, right = false;
  const d = p.dpad || {};
  if (d.b) { up = bv(d.b[0]) > 0.5; down = bv(d.b[1]) > 0.5; left = bv(d.b[2]) > 0.5; right = bv(d.b[3]) > 0.5; }
  if (d.axes && A.length > Math.max(d.axes[0], d.axes[1])) {
    const x = ax(d.axes[0]), y = ax(d.axes[1]);
    left = left || x < -0.5; right = right || x > 0.5; up = up || y < -0.5; down = down || y > 0.5;
  }
  if (d.hat != null && A.length > d.hat) {
    const h = hatDir(ax(d.hat));
    if (h >= 0) { up = up || h === 7 || h <= 1; right = right || (h >= 1 && h <= 3); down = down || (h >= 3 && h <= 5); left = left || h >= 5; }
  }
  out.up = up; out.down = down; out.left = left; out.right = right;
  return out;
}
