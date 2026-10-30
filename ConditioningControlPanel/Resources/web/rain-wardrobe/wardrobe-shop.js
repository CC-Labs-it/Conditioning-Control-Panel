// The dressing room's shop layer (RAIN-V2-PLAN 2.4 / 2.5): badges, try-on, draft then Apply.
// Pure on purpose: no location, no document at import, so node tests load it as is. viewer.js wires it.
// Host -> page: init {look, grants[], level, achievements[], locale, motion, gentle, prices?, applyMode?},
//   rain-owned {grants}, rain-applied {lookHash}, rain-tryon {key} (init.tryOn too),
//   rain-baking {lookHash, done, total, shown, fraction} (the host's count of the bake; init.strings localises STR). Page -> host: rain-draft {look, unowned},
//   rain-apply {look}, open-counter {tab}. Without init the page reads today's ?locked= list instead
//   (fallback), and without applyMode:true the page keeps the old autosave (viewer.js decides).

/** Every visible string in one table: a later loc lane lifts it whole. English only for the preview. */
export const STR = {
  wearing: 'WEARING',
  owned: 'OWNED',
  lockedLevel: 'LOCKED Lv {n}',
  lockedAchievement: 'LOCKED: first achievement',
  atCounter: 'At the Prize Counter',
  price: '{n} Sparkles',
  ribbon: 'TRY-ON: {n} not yours yet',
  wearingNow: 'Wearing now:',
  buy: 'Buy {name} at the Prize Counter',
  reachLevel: 'Reach level {n} to wear {name}',
  earnAchievement: 'Earn a first achievement to wear {name}',
  discard: 'Discard',
  apply: 'Apply look',
  openCounter: 'Open counter',
  applying: 'Getting dressed',
  baking: 'Getting dressed: {n} of {total}',
  bakeHint: 'Keep this window open. Closing it stops the change.',
  applied: 'Applied',
  saved: 'Saved',
};

export const fill = (text, values) => text.replace(/\{(\w+)\}/g, (_, k) => String(values[k] ?? ''));

const SINGLE = ['dress', 'hair', 'skin', 'shoes', 'legwear', 'arms', 'ankles'];

/** items.json -> Map of "slot:pageId" -> row. Bad rows are skipped, never thrown on. */
export function indexItems(json) {
  const map = new Map();
  for (const row of (json && Array.isArray(json.items)) ? json.items : []) {
    if (!row || typeof row.slot !== 'string' || typeof row.pageId !== 'string') continue;
    map.set(`${row.slot}:${row.pageId}`, row);
  }
  return map;
}

/** The look's item keys in a fixed order (single slots, then accessories). Empty slots ('none') skip. */
export function lookKeys(look) {
  const items = (look && look.items && typeof look.items === 'object') ? look.items : {};
  const keys = [];
  const slots = [...SINGLE, ...Object.keys(items).filter(s => s !== 'accessories' && !SINGLE.includes(s))];
  for (const slot of slots) if (typeof items[slot] === 'string' && items[slot] && items[slot] !== 'none') keys.push(`${slot}:${items[slot]}`);
  for (const id of Array.isArray(items.accessories) ? items.accessories : []) if (typeof id === 'string' && id) keys.push(`accessories:${id}`);
  return keys;
}

/** Two looks are the same when their items (accessories in any order) and colours match. */
export function sameLook(a, b) {
  const norm = look => {
    const items = { ...((look && look.items) || {}) };
    items.accessories = [...(Array.isArray(items.accessories) ? items.accessories : [])].sort();
    const colors = (look && look.colors) || {};
    return JSON.stringify([Object.keys(items).sort().map(k => [k, items[k]]), Object.keys(colors).sort().map(k => [k, colors[k]])]);
  };
  return norm(a) === norm(b);
}

const prettify = key => { const id = String(key).split(':').pop().replace(/-/g, ' '); return id.charAt(0).toUpperCase() + id.slice(1); };

/**
 * The shop state for one window. `isLocked(slot, id)` is today's ?locked= reader (fallback until init).
 * `post(message)` reaches the host (host-bridge postHost); `nameOf(key)` is the page's display name.
 */
export function createShop({ items, isLocked = () => false, post = () => {}, nameOf = prettify } = {}) {
  const s = {
    items: items instanceof Map ? items : indexItems(items),
    source: 'fallback', grants: new Set(), level: 0, achievements: [], prices: {},
    applyMode: false, calm: false, applied: null, draft: null, pending: null, applying: false, lookHash: '', tryOn: null,
    // bake = {shown, total, fraction} while a bake reports; outcome = how the last Apply ended (the meter's OUT).
    bake: null, outcome: null, str: { ...STR },
  };

  function owned(key) {
    const row = s.items.get(key);
    const [slot, id] = key.split(':');
    if (row && row.access === 'free') return true;
    if (s.source === 'fallback') return !isLocked(slot, id);
    if (!row) return true; // not in the manifest: the host clamps, the page never guesses a lock
    if (row.access === 'shop') return s.grants.has(row.grant);
    if (row.access === 'unlock') {
      const u = row.unlock || {};
      if (Number.isFinite(u.level)) return s.level >= u.level;
      if (u.achievement) return s.achievements.length > 0;
    }
    return false;
  }

  function priceLabel(key) {
    const row = s.items.get(key);
    const p = s.prices[key] ?? (row && row.grant ? s.prices[row.grant] : undefined);
    if (Number.isFinite(p)) return fill(STR.price, { n: p });
    if (typeof p === 'string' && p.trim()) return p.trim();
    return STR.atCounter;
  }

  /** {kind, text} for a tile, or null (a free item nobody wears). */
  function badge(slot, id) {
    const key = `${slot}:${id}`;
    const row = s.items.get(key);
    const wearing = !!s.draft && lookKeys(s.draft).includes(key);
    if (owned(key)) {
      if (wearing) return { kind: 'wearing', text: STR.wearing, owned: true };
      return row && row.access !== 'free' ? { kind: 'owned', text: STR.owned, owned: true } : null;
    }
    const u = (row && row.unlock) || {};
    if (row && row.access === 'unlock' && Number.isFinite(u.level)) return { kind: 'locked', text: fill(STR.lockedLevel, { n: u.level }), owned: false };
    if (row && row.access === 'unlock' && u.achievement) return { kind: 'locked', text: STR.lockedAchievement, owned: false };
    return { kind: 'price', text: priceLabel(key), owned: false };
  }

  function blockerFor(key) {
    const row = s.items.get(key);
    const name = nameOf(key) || prettify(key);
    const u = (row && row.unlock) || {};
    if (row && row.access === 'unlock' && Number.isFinite(u.level)) return { key, text: fill(STR.reachLevel, { n: u.level, name }), counter: false, tab: null };
    if (row && row.access === 'unlock' && u.achievement) return { key, text: fill(STR.earnAchievement, { name }), counter: false, tab: null };
    return { key, text: fill(STR.buy, { name }), counter: true, tab: (row && row.tab) || null };
  }

  /** What the bar shows: the unowned pieces, the first blocker, and whether Apply / Discard are live. */
  function status() {
    const keys = lookKeys(s.draft);
    const unowned = keys.filter(k => !owned(k));
    const dirty = !!s.draft && !!s.applied && !sameLook(s.draft, s.applied);
    const wearable = unowned.length === 0;
    return {
      keys, unowned, count: unowned.length, wearable, dirty, applying: s.applying, bake: s.bake, outcome: s.outcome,
      blocker: unowned.length ? blockerFor(unowned[0]) : null,
      canApply: wearable && dirty && !s.applying,
      canDiscard: dirty && !s.applying,
      names: keys.map(k => nameOf(k) || prettify(k)),
    };
  }

  function setDraft(look) {
    const changed = !s.draft || !sameLook(s.draft, look);
    s.draft = look;
    if (!s.applied) s.applied = look;
    if (changed && s.applyMode) post({ type: 'rain-draft', look, unowned: status().unowned });
    return status();
  }

  /** Apply: applyMode posts rain-apply and waits for rain-applied; the old host takes it as saved now. */
  function apply() {
    const st = status();
    if (!st.canApply) return false;
    if (s.applyMode) { s.applying = true; s.pending = s.draft; s.bake = null; s.outcome = null; post({ type: 'rain-apply', look: s.draft }); }
    else s.applied = s.draft;
    return true;
  }

  /** Discard: the draft goes back to the applied look, which the caller paints. */
  function discard() { if (s.applying) return null; s.draft = s.applied; return s.applied; }

  function openCounter() {
    const b = status().blocker;
    const message = { type: 'open-counter', tab: (b && b.counter && b.tab) || null };
    post(message);
    return message;
  }

  /** One host frame. Returns what changed: 'init' | 'owned' | 'applied' | 'apply-failed' | null. */
  function handle(data) {
    if (!data || typeof data !== 'object') return null;
    if (data.type === 'init') {
      s.source = 'init';
      s.grants = new Set(Array.isArray(data.grants) ? data.grants.filter(g => typeof g === 'string') : []);
      s.level = Number.isFinite(data.level) ? data.level : 0;
      s.achievements = Array.isArray(data.achievements) ? data.achievements : [];
      s.prices = (data.prices && typeof data.prices === 'object' && !Array.isArray(data.prices)) ? data.prices : {};
      s.applyMode = data.applyMode === true;
      s.calm = data.gentle === true || data.motion === false || data.motion === 0 || data.motion === 'off' || data.motion === 'reduced';
      if (data.look && typeof data.look === 'object' && !Array.isArray(data.look)) { s.applied = data.look; s.draft = data.look; }
      if (typeof data.tryOn === 'string') s.tryOn = data.tryOn;
      // The host's localised lines: known keys only, plain strings only.
      if (data.strings && typeof data.strings === 'object') for (const [k, v] of Object.entries(data.strings)) if (k in STR && typeof v === 'string' && v.trim()) s.str[k] = v;
      return 'init';
    }
    // The Prize Counter's Wear door on a window that is already up.
    if (data.type === 'rain-tryon' && typeof data.key === 'string') { s.tryOn = data.key; return 'tryon'; }
    if (data.type === 'rain-owned' && Array.isArray(data.grants)) {
      if (s.source === 'init') s.grants = new Set(data.grants.filter(g => typeof g === 'string'));
      return 'owned';
    }
    // The host counts the bake's clips (a host-side re-bake on open counts too, with no Apply press).
    if (data.type === 'rain-baking') {
      const total = Number(data.total), shown = Number(data.shown), fraction = Number(data.fraction);
      if (!(total > 0) || !Number.isFinite(shown) || !Number.isFinite(fraction)) return null;
      s.bake = { shown: Math.max(0, Math.min(total, Math.round(shown))), total: Math.round(total), fraction: Math.max(0, Math.min(1, fraction)) };
      s.outcome = null;
      return 'baking';
    }
    if (data.type === 'rain-applied') {
      // failed:true = cancelled, bake error or a host clamp (data.look): the applied look stays.
      const failed = data.failed === true;
      if (s.pending && !failed) s.applied = s.pending;
      s.outcome = s.bake ? (failed ? 'failed' : 'applied') : null;
      s.pending = null; s.applying = false; s.bake = null;
      if (failed) return 'apply-failed';
      s.lookHash = typeof data.lookHash === 'string' ? data.lookHash : '';
      return 'applied';
    }
    return null;
  }

  return {
    get applyMode() { return s.applyMode; }, get calm() { return s.calm; }, get source() { return s.source; },
    get applied() { return s.applied; }, get lookHash() { return s.lookHash; }, get str() { return s.str; },
    owned, badge, status, setDraft, apply, discard, openCounter, handle,
    /** The piece the counter asked to try on, once: {slot, id} when items.json knows it (never a bundled
     *  look), else null. Kept until items.json has loaded, so an early init is not lost. */
    takeTryOn() {
      const key = s.tryOn;
      if (!key || !s.items.size) return null;
      s.tryOn = null;
      const row = s.items.get(key);
      return row && row.slot !== 'looks' ? { slot: row.slot, id: row.pageId } : null;
    },
    setItems(json) { s.items = json instanceof Map ? json : indexItems(json); },
    /** The old autosave reached the host: that look is the applied one now. */
    markApplied() { s.applied = s.draft; },
  };
}

/** Listen for host frames on the WPF webview; `onChange(kind, data)` after the shop took one. */
export function listenToHost(win, shop, onChange) {
  const webview = win && win.chrome && win.chrome.webview;
  if (!webview || typeof webview.addEventListener !== 'function') return false;
  webview.addEventListener('message', event => { const kind = shop.handle(event && event.data); if (kind) onChange(kind, event.data); });
  return true;
}

/** Paint the ribbon and the bottom bar (ids from index.html). `doc` is the page document. */
export function paintShop(doc, shop) {
  const $ = id => doc.getElementById(id);
  const st = shop.status();
  const T = shop.str || STR;
  doc.documentElement.toggleAttribute('data-calm', shop.calm);
  const ribbon = $('tryonRibbon');
  if (ribbon) { ribbon.textContent = st.count ? fill(STR.ribbon, { n: st.count }) : ''; ribbon.classList.toggle('on', st.count > 0); ribbon.setAttribute('aria-hidden', String(!st.count)); }
  if ($('lookNowLabel')) $('lookNowLabel').textContent = STR.wearingNow;
  if ($('lookNow')) $('lookNow').textContent = st.names.join(', ');
  const blocker = $('lookBlocker');
  if (blocker) {
    blocker.classList.toggle('on', !!st.blocker || !!st.bake);
    $('lookBlockerText').textContent = st.blocker ? st.blocker.text : (st.bake ? T.bakeHint : (st.applying ? T.applying : ''));
    $('openCounter').hidden = !(st.blocker && st.blocker.counter);
    $('openCounter').textContent = STR.openCounter;
  }
  if ($('discardLook')) { $('discardLook').textContent = STR.discard; $('discardLook').disabled = !st.canDiscard; }
  if ($('applyLook')) { $('applyLook').textContent = st.bake ? bakeLine(T, st.bake) : (st.applying ? T.applying : STR.apply); $('applyLook').disabled = !st.canApply; }
  paintMeter($('bakeMeter'), st, shop.calm);
  return st;
}

/** "Getting dressed: 3 of 17" from the (localised) template. */
export const bakeLine = (T, bake) => fill(T.baking || STR.baking, { n: bake.shown, total: bake.total });

/** The bake meter (a hairline over the bar). IN: a fresh bake starts empty with no backwards sweep
 *  and fades in; the fill follows the host's count. OUT: an applied bake fills to the end, then fades
 *  (style.css delays the fade); a failed or cancelled one just fades. Calm lengthens both fades. */
export function paintMeter(meter, st, calm = false) {
  if (!meter) return;
  const fill = meter.firstElementChild;
  const on = !!st.bake, was = meter.classList.contains('on');
  meter.classList.toggle('calm', !!calm);
  if (on && !was) {
    meter.classList.toggle('done', false);
    if (fill) { fill.style.transition = 'none'; fill.style.transform = 'scaleX(0)'; void meter.offsetWidth; fill.style.transition = ''; }
  }
  if (on && fill) fill.style.transform = `scaleX(${st.bake.fraction})`;
  if (!on && was) {
    const done = st.outcome === 'applied';
    meter.classList.toggle('done', done);
    if (fill && done) fill.style.transform = 'scaleX(1)';
  }
  meter.classList.toggle('on', on);
  meter.setAttribute?.('aria-hidden', String(!on));
}
