// CCP host bridge for the fitting room (WPF RainWardrobeHostService; the 7.2 Avalonia head on
// feat/rain-avatar speaks the same frames).
// In the app (?host=ccp) the host owns the look: it hands the saved look in as ?look=<json>
// ({rev:1, items:{slot:id, accessories:[ids]}, colors:{channel:'#rrggbb'}}, empty = Rain's
// authored default) and the page posts {type:'rain-look', look} back, debounced, on every change.
// Prize Counter: ?locked=slot:id,slot:id lists the items the account does not own; they show
// ?lockNote= (the host's localised "In the Prize Counter") and cannot be worn. The host posts
// {type:'rain-locked', locked:[...]} when ownership changes while the window is up. Single items
// only: the bundled "Looks" tab is hidden in the app.
// Rain V2 (wardrobe-shop.js): a host that posts init {applyMode:true} gets draft then rain-apply in
// place of the rain-look autosave; ?locked= stays the fallback until that init arrives.
// Outside the app nothing here is active and the page keeps its localStorage save.
// The review studio (?review=1) and QA panel (?qa=1) never open inside the app.

const params = new URLSearchParams(location.search);

export const hosted = params.get('host') === 'ccp';

/** Where avatar.glb and pelvis-data.js live: never in the repo. In the app the host maps
 *  https://ccp.rain-assets/ to a local folder (CCP_RAIN_ASSETS_DIR or <user data>/rain-assets). */
export const BIG_ASSETS = hosted ? 'https://ccp.rain-assets/' : '/rain-assets/';

export const reviewAllowed = !hosted && params.get('review') === '1';
export const qaAllowed = !hosted && params.get('qa') === '1';

const HEX = /^#[0-9a-f]{6}$/i;
const LOCK = /^[a-z]+:[a-z0-9-]+$/;

function parseLocks(list) {
  const out = new Set();
  for (const raw of list || []) {
    const key = String(raw || '').trim().toLowerCase();
    if (LOCK.test(key)) out.add(key);
  }
  return out;
}

let locks = hosted ? parseLocks((params.get('locked') || '').split(',')) : new Set();

/** The host's line for a locked item (falls back to English outside a host). */
export const lockNote = (hosted && params.get('lockNote')) || 'In the Prize Counter';

/** True when the account does not own this item (only ever inside the app). */
export function isLocked(slot, id) { return locks.has(`${slot}:${id}`); }

function readHostLook() {
  if (!hosted) return null;
  try {
    const value = JSON.parse(params.get('look') || 'null');
    return value && typeof value === 'object' && !Array.isArray(value) ? value : null;
  } catch { return null; }
}

const hostLook = readHostLook();

/** The host's saved items as the viewer's look shape, or null for Rain's authored default.
 *  A locked item never comes through (the host clamps too); clamp=false trusts the host's init look,
 *  since ?locked= goes stale once the shop layer tracks grants. */
export function hostItems(source = hostLook, clamp = true) {
  const items = source?.items;
  if (!items || typeof items !== 'object' || Array.isArray(items)) return null;
  const look = {};
  for (const [slot, id] of Object.entries(items)) {
    if (slot === 'accessories') { if (Array.isArray(id)) look.accessories = id.filter(x => typeof x === 'string' && !(clamp && isLocked(slot, x))); }
    else if (typeof id === 'string' && id && !(clamp && isLocked(slot, id))) look[slot] = id;
  }
  return Object.keys(look).length ? look : null;
}

/** The host's saved colour overrides (channel -> #rrggbb), lower-cased; {} = the original palette. */
export function hostColors(channels, look = hostLook) {
  const out = {};
  const colors = look?.colors;
  if (!colors || typeof colors !== 'object' || Array.isArray(colors)) return out;
  for (const id of channels) if (typeof colors[id] === 'string' && HEX.test(colors[id])) out[id] = colors[id].toLowerCase();
  return out;
}

/** The look as the host stores it (brief contract 4). */
export function toHostLook(look, colors) {
  const items = {};
  for (const [slot, id] of Object.entries(look || {})) {
    if (slot === 'accessories') items.accessories = Array.isArray(id) ? [...id] : [];
    else if (typeof id === 'string') items[slot] = id;
  }
  return { rev: 1, items, colors: { ...(colors || {}) } };
}

function send(message) {
  const webview = window.chrome && window.chrome.webview;
  // WPF reads WebMessageAsJson: post the object itself, never a JSON string.
  if (webview && typeof webview.postMessage === 'function') { webview.postMessage(message); return true; }
  if (typeof window.invokeCSharpAction === 'function') { window.invokeCSharpAction(JSON.stringify(message)); return true; }
  return false;
}

let pending = null, timer = 0, retries = 0;

function flush() {
  clearTimeout(timer); timer = 0;
  if (!pending) return;
  // The Linux carrier lands after load: hold the frame a little while rather than drop it.
  if (send(pending)) { pending = null; retries = 0; return; }
  if (++retries <= 20) timer = setTimeout(flush, 500);
}

/** Queue the current look for the host (debounced; the last call wins). False outside the app. */
export function postLook(look, colors) {
  if (!hosted) return false;
  pending = { type: 'rain-look', look: toHostLook(look, colors) };
  retries = 0;
  clearTimeout(timer); timer = setTimeout(flush, 400);
  return true;
}

/** A one-off frame to the host (boot error, ready). No-op outside the app. The WPF window
 *  holds its posts until the shared 'ready' frame, so rain-ready also says ready. */
export function postHost(message) {
  if (!hosted) return;
  send(message);
  if (message && message.type === 'rain-ready') send({ type: 'ready' });
}

if (hosted) {
  window.addEventListener('pagehide', flush);
  const status = document.getElementById('saveStatus');
  if (status) status.textContent = 'Saved';
  // Single items only in the app: the bundled looks tab goes.
  document.querySelector('[data-slot="looks"]')?.remove();
  // The brand link reloads the page without the host's query: inside the app it goes nowhere.
  document.addEventListener('click', event => {
    const link = event.target instanceof Element ? event.target.closest('a.brand') : null;
    if (link) event.preventDefault();
  });
  const webview = window.chrome && window.chrome.webview;
  webview?.addEventListener?.('message', event => {
    const data = event.data;
    if (data && data.type === 'rain-locked' && Array.isArray(data.locked)) {
      locks = parseLocks(data.locked);
      window.dispatchEvent(new CustomEvent('rain-locks'));
    }
  });
}
