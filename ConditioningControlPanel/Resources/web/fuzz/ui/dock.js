/* Objects live directly on the page. The bottom strip owns their visibility and order. */
export const DOCK_MIN_W = 1100;
export const dockIsRail = w => w >= DOCK_MIN_W;
export const railWidth = w => dockIsRail(w) ? Math.round(Math.max(200, Math.min(300, (w - 776) / 2))) : 0;
export const emptierEdge = (left, right) => left <= right ? 'left' : 'right';
export const slotOrder = (boughtAt, nth = 0) => (boughtAt < 0 ? 100000 : boughtAt * 8) + Math.min(7, nth);
export const WINDOW_MIN = Object.freeze({ w: 96, h: 44 });
export const EDGE_BLEED = 32;
export const HOLD_MS = 320;
export const HOLD_SLOP = 8;
/* A resting fingertip drifts more than a mouse: the contact patch flattens and rolls in the first 100-200 ms of a press.
 * Android's own touch slop is 8 dp, iOS lets a long press drift 10 pt, Chromium's tap slop is 15 dip. 8 px cancelled
 * a press-and-settle (owner 2026-11-09: "feels about 1 s", the hold kept restarting), so touch gets 14 px. */
export const TOUCH_HOLD_SLOP = 14;
export const holdSlop = touch => touch ? TOUCH_HOLD_SLOP : HOLD_SLOP;
/** The layout lock and the extent outline are owner 2026-11-09 asks: a lock beside the speaker, a dashed outline of the
 *  whole object (toys that hang below their box included) while the resize corner is hovered. */
export const LOCK_COPY = Object.freeze({ lock: 'Lock layout', unlock: 'Unlock layout',
  lockTitle: 'Lock the layout. Nothing moves or resizes.', unlockTitle: 'Layout locked. Unlock it to move things again.' });
const LOCK_SVG = on => `<svg viewBox="0 0 20 20" width="18" height="18" aria-hidden="true"><g fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="4" y="9" width="12" height="8.5" rx="1.5" fill="${on ? 'currentColor' : 'none'}"/><path d="${on ? 'M6.5 9V6.5a3.5 3.5 0 0 1 7 0V9' : 'M6.5 9V6.5a3.5 3.5 0 0 1 6.8-1.2'}"/></g></svg>`;

/** The whole drawn extent of an object: the union of every descendant's box, each cut to any scrolling or clipping box
 *  around it inside the object (what is clipped is not on screen). Null when nothing has a size. */
export function extentOf(root, style = el => el.ownerDocument?.defaultView?.getComputedStyle?.(el), cap = 2000) {
  let out = null, seen = 0;
  const cut = (r, c) => c ? { left: Math.max(r.left, c.left), top: Math.max(r.top, c.top), right: Math.min(r.right, c.right), bottom: Math.min(r.bottom, c.bottom) } : r;
  const walk = (el, clip) => {
    if (!el || seen++ > cap) return;
    const raw = el.getBoundingClientRect?.();
    const s = el === root ? null : style(el);
    if (s?.display === 'none') return;
    if (raw && raw.width > 0 && raw.height > 0 && s?.visibility !== 'hidden') {
      const r = cut(raw, clip);
      if (r.right > r.left && r.bottom > r.top) out = out
        ? { left: Math.min(out.left, r.left), top: Math.min(out.top, r.top), right: Math.max(out.right, r.right), bottom: Math.max(out.bottom, r.bottom) }
        : { left: r.left, top: r.top, right: r.right, bottom: r.bottom };
    }
    const clips = s && /hidden|auto|scroll|clip/.test(`${s.overflowX || s.overflow || ''} ${s.overflowY || s.overflow || ''}`);
    const next = clips && raw ? cut(raw, clip) : clip;
    for (const child of el.children || []) walk(child, next);
  };
  walk(root, null);
  return out && { ...out, width: out.right - out.left, height: out.bottom - out.top };
}
export const LAYOUT_VERSION = 2;
/* A tab a purchase just added stays NEW (seen false, saved) until the player hovers it a moment, touches or opens it.
 * Saves from before this have no seen key and never nudge. */
export const NEW_HOVER_MS = 350;
export const tabIsNew = saved => saved?.seen === false;
/* The bottom strip never scrolls (owner 2026-11-07): full tabs, then compact tabs, then icon pills that wrap to
 * STRIP_MAX_ROWS rows and squeeze below PILL_W; past PILL_MIN a further row opens rather than a scroll. */
export const PILL_W = 30;
export const PILL_GAP = 3;
export const PILL_ROW_H = 34;
export const STRIP_MAX_ROWS = 3;
export const PILL_MIN = 18;
/* Pills grow to fill the strip: up to PILL_MAX on a narrow strip, PILL_WIDE_MAX on a desktop one, where a pill this wide
 * (PILL_LABEL_FROM) also shows its name, cut short. Owner 2026-11-08: 19+ tabs left a third of the bar empty. */
export const PILL_MAX = 44;
export const PILL_WIDE_MAX = 112;
export const PILL_WIDE_FROM = 760;
export const PILL_LABEL_FROM = 72;
/** Rows and pill basis for n icon pills in a strip of the given width. */
export function pillPlan(n, avail, { pill = PILL_W, gap = PILL_GAP, maxRows = STRIP_MAX_ROWS, max = PILL_MAX } = {}) {
  if (!(n > 0) || !(avail > 0)) return { rows: 1, basis: pill };
  const perRow = Math.max(1, Math.floor((avail + gap) / (pill + gap)));
  let rows = Math.max(1, Math.min(maxRows, Math.ceil(n / perRow)));
  const basisFor = r => { const across = Math.ceil(n / r); return Math.floor((avail - gap * (across - 1)) / across - .5); };
  while (basisFor(rows) < PILL_MIN && rows < n && rows < 8) rows++;
  return { rows, basis: Math.max(8, Math.min(max, basisFor(rows))) };
}
export const stripHeight = rows => rows > 1 ? PILL_GAP + rows * PILL_ROW_H + (rows - 1) * PILL_GAP : null;
const STRIP_CSS = `
#dock-strip { overflow: hidden; scrollbar-width: none; touch-action: none; overscroll-behavior: none; }
#dock-strip::-webkit-scrollbar { display: none; }
#dock-strip .dk-chip { max-width: 150px; min-width: 0; gap: 4px; padding: 0 10px 0 6px; font-size: 11.5px; touch-action: none; transition: max-width .14s ease-out, padding .14s ease-out; }
#dock-strip .dk-chip .dk-i { width: 20px; height: 20px; }
#dock-strip[data-fit=compact] .dk-chip { max-width: 104px; gap: 3px; padding: 0 8px 0 4px; font-size: 11px; }
#dock-strip[data-fit=compact] .dk-chip .dk-i { width: 18px; height: 18px; }
#dock-strip[data-fit=icon] { flex-wrap: wrap; row-gap: 3px; align-content: flex-end; }
#dock-strip[data-fit=icon] .dk-chip { flex: 1 1 var(--dk-basis, 30px); max-width: var(--dk-max, 44px); min-width: 0; height: calc((100% - (var(--dk-rows, 1) - 1) * 3px) / var(--dk-rows, 1)); padding: 0; justify-content: center; border-bottom: 1px solid color-mix(in srgb,var(--line) 65%,transparent); border-radius: 999px; }
#dock-strip[data-fit=icon] .dk-chip.on { border-color: var(--niche); }
#dock-strip[data-fit=icon] .dk-label { display: none; }
#dock-strip[data-fit=icon][data-roomy] .dk-chip { gap: 4px; padding: 0 8px 0 6px; }
#dock-strip[data-fit=icon][data-roomy] .dk-label { display: block; font-size: 11px; }
#dock-strip[data-fit=icon] .dk-ready { top: 1px; right: 3px; }
@media (prefers-reduced-motion: reduce) { #dock-strip .dk-chip { transition: none; } }
#dock-strip[data-measuring] .dk-chip { transition: none !important; }
`;
const finite = (v, fallback) => Number.isFinite(v) ? v : fallback;
const desired = v => Number.isFinite(v) && v > 0 ? Math.min(4096, Math.max(WINDOW_MIN.w, v)) : null;
export const defaultWindowWidth = w => Math.min(220, w - 16);

/** Legacy window dimensions included title bars and blank padding. Keep placement, reset those dimensions once. */
export function migrateLayout(saved = {}) {
  return { ...saved, w: saved.version === LAYOUT_VERSION ? desired(saved.w) : null, h: null, version: LAYOUT_VERSION };
}
export function windowSize({ w }, { W, defaultW }) {
  return { w: Math.round(Math.max(1, Math.min(desired(w) ?? defaultW, W - 8))), h: null };
}
/** A little overhang lets players tuck objects against the viewport edges. */
export function clampWindow({ x = 8, y = 48 }, { w, h = 44, W, H, bottom = 46 }) {
  const edge = EDGE_BLEED;
  return { x: Math.round(Math.max(-edge, Math.min(finite(x, 8), Math.max(-edge, W - w + edge)))),
    y: Math.round(Math.max(-edge, Math.min(finite(y, 48), Math.max(-edge, H - bottom - Math.min(h, H - bottom + edge * 2) + edge)))) };
}

/** Find actual free room before opening a newly bought object. Existing objects never move to make room. */
export function freeObjectSpot({ W, H, w, h, top = 48, bottom = 46, phone = false, preferred = 'left', centerTop = null }, occupied = []) {
  const gap = 12, left = 8, right = Math.max(left, W - w - 8);
  const xs = phone ? [...new Set([(W - w) / 2, left, right])] : preferred === 'right' ? [right, left] : [left, right];
  const ys = [...new Set([top, ...occupied.map(p => p.y + p.h + gap)])].filter(y => y >= top && y + h <= H - bottom).sort((a, b) => a - b);
  const overlaps = (x, y, p) => x < p.x + p.w + gap && x + w + gap > p.x && y < p.y + p.h + gap && y + h + gap > p.y;
  const choices = [];
  for (const [preference, x] of xs.entries()) for (const y of ys) {
    if (occupied.every(p => !overlaps(x, y, p))) choices.push({ x, y, score: y + preference * 20 });
  }
  choices.sort((a, b) => a.score - b.score);
  if (choices.length) return { x: choices[0].x, y: choices[0].y };
  // Full side columns do not mean a full page. The space beneath the actual shop
  // content is available, while the counter, button, banners and tiles remain clear.
  if (!phone && Number.isFinite(centerTop)) {
    const middleXs = [...new Set([(W - w) / 2, ...occupied.flatMap(p => [p.x + p.w + gap, p.x - w - gap]), left, right])]
      .filter(x => x >= left && x <= right);
    const middleYs = [...new Set([centerTop, ...occupied.map(p => p.y + p.h + gap)])]
      .filter(y => y >= centerTop && y + h <= H - bottom).sort((a, b) => a - b);
    for (const y of middleYs) for (const x of middleXs) {
      if (occupied.every(p => !overlaps(x, y, p))) return { x, y };
    }
  }
  return null;
}

/** Scrolling ancestors need native short swipes even when a label is also a hold-to-move surface. */
export function scrollContainer(target, root) {
  for (let el = target; el && el !== root; el = el.parentElement) {
    if (el.hasAttribute?.('data-dock-scroll')) return el;
    if ((el.scrollHeight > el.clientHeight + 2 || el.scrollWidth > el.clientWidth + 2) && el.clientHeight > 0) {
      const overflow = root.ownerDocument?.defaultView?.getComputedStyle?.(el);
      if (overflow && /auto|scroll/.test(`${overflow.overflowX} ${overflow.overflowY}`)) return el;
    }
  }
  return null;
}

/** Semantic controls and custom game gestures always own their pointer. */
export function canMoveFrom(target, root) {
  if (!target || target.closest?.('button,input,select,textarea,a,[role="button"],[contenteditable="true"],[data-dock-interactive]')) return false;
  const canvas = target.closest?.('canvas');
  if (canvas && !canvas.hasAttribute?.('data-dock-surface')) return false;
  for (let el = target; el && el !== root; el = el.parentElement) {
    if (el.hasAttribute?.('data-dock-surface')) return true;
    if (el.hasAttribute?.('data-dock-scroll')) return false;
    if ((el.scrollHeight > el.clientHeight + 2 || el.scrollWidth > el.clientWidth + 2) && el.clientHeight > 0) {
      const overflow = root.ownerDocument?.defaultView?.getComputedStyle?.(el);
      if (overflow && /auto|scroll/.test(`${overflow.overflowX} ${overflow.overflowY}`)) return false;
    }
  }
  return true;
}

/** Two boxes share some area. */
const overlapsRect = (a, b) => !!a && !!b && a.width > 0 && a.height > 0 && b.width > 0 && b.height > 0
  && a.left < b.right && a.right > b.left && a.top < b.bottom && a.bottom > b.top;

/* lock: { host, read(), write(on) } puts the layout lock in the HUD row (host, before #audio-controls) and keeps its
 * state with the dock layout preferences. Without it the dock simply never locks. */
export function createDock({ doc, rails, strip, onStrip, lock: lockPrefs = null }) {
  const win = doc.defaultView;
  const slots = new Set();
  let topZ = 0, queued = false, active = null, serial = 0;
  let locked = false;
  try { locked = !!lockPrefs?.read?.(); } catch { locked = false; }
  const isRail = () => dockIsRail(win.innerWidth);
  const paused = () => doc.getElementById('pause')?.hidden === false;
  const timer = (fn, ms) => win.setTimeout(fn, ms);
  const untimer = id => { if (id != null) win.clearTimeout(id); };
  if (doc.head && !doc.getElementById?.('dk-strip-css')) {
    const st = doc.createElement('style'); st.id = 'dk-strip-css'; st.textContent = STRIP_CSS; doc.head.appendChild(st);
  }
  strip.setAttribute('role', 'toolbar');
  strip.setAttribute('aria-label', 'Features. Click to show or hide. Drag to reorder.');

  const ordered = () => [...slots].filter(s => s.chip).sort((a, b) => a.tabOrder - b.tabOrder || a.serial - b.serial);
  const bottomSpace = () => {
    const r = strip.getBoundingClientRect?.();
    const occupied = r?.height ? Math.min(win.innerHeight - 44, win.innerHeight - r.top) : (isRail() ? 38 : 46);
    return Math.max(40, occupied) + 8;
  };
  const width = s => s.defaultW ?? defaultWindowWidth(win.innerWidth);
  const layout = s => ({ version: LAYOUT_VERSION, edge: s.side, x: s.x, y: s.y, w: s.w, h: null, min: s.min, z: s.z, tabOrder: s.tabOrder, ...(s.anchor ? { attached:true } : {}), ...(typeof s.seen === 'boolean' ? { seen: s.seen } : {}) });
  const save = s => s.onLayout?.(layout(s));
  const moved = s => {
    if (win.CustomEvent) doc.dispatchEvent(new win.CustomEvent('fz-dock-move', { detail: { root: s.root, x: s.x, y: s.y } }));
  };
  function paint(s, notify = false) {
    const size = windowSize(s, { W: win.innerWidth, defaultW: width(s) });
    s.root.style.width = `${size.w}px`;
    // Scale the complete feature, including fixed-size art and its hit targets.
    s.scale = size.w / width(s);
    s.body.style.width = `${width(s)}px`;
    s.body.style.zoom = String(s.scale);
    // A previous max-height must not hide the new artwork when a saved toy grows.
    const h = Math.max(s.root.getBoundingClientRect().height, (s.body.scrollHeight || 0) * s.scale) || s.lastHeight || 80;
    if (!s.min) s.lastHeight = h;
    const bottom = s.allowUnderStrip ? 0 : bottomSpace();
    const point = clampWindow(s, { w: size.w, h: s.lastHeight || h, W: win.innerWidth, H: win.innerHeight, bottom });
    s.x = point.x; s.y = point.y;
    Object.assign(s.root.style, { left: `${s.x}px`, top: `${s.y}px`, height: '', maxHeight: `${Math.max(44, win.innerHeight - bottom - s.y + EDGE_BLEED)}px`, zIndex: String(s.z) });
    s.root.classList.toggle('min', s.min);
    s.root.classList.toggle('open', !s.min);
    s.root.classList.toggle('locked', locked);
    s.root.setAttribute('aria-hidden', String(s.min || s.door));
    s.chip?.classList.toggle('on', !s.min && !s.door);
    s.chip?.setAttribute('aria-expanded', String(!s.min && !s.door));
    if (notify) moved(s);
  }
  function raise(s) {
    if (s.z === topZ) return;
    s.z = ++topZ; s.root.style.zIndex = String(s.z); save(s);
  }
  /** Another open object above this one overlaps it, so a tab press should bring it up rather than fold it. */
  function covered(s) {
    if (s.min || s.door) return false;
    const mine = s.root.getBoundingClientRect?.();
    for (const o of slots) {
      if (o === s || o.min || o.door || !(o.z > s.z)) continue;
      if (overlapsRect(mine, o.root.getBoundingClientRect?.())) return true;
    }
    return false;
  }
  /* ---- the dashed full-extent outline while the resize corner is hovered (mouse and pen only) ---- */
  let extentBox = null, extentOwner = null;
  function showExtent(s) {
    const r = extentOf(s.root);
    if (!r || !doc.body) { hideExtent(); return; }
    if (!extentBox) {
      extentBox = doc.createElement('div'); extentBox.className = 'dk-extent'; extentBox.setAttribute('aria-hidden', 'true');
    }
    if (extentBox.parentElement !== doc.body) doc.body.appendChild(extentBox);
    extentOwner = s;
    Object.assign(extentBox.style, { left: `${Math.round(r.left) - 3}px`, top: `${Math.round(r.top) - 3}px`,
      width: `${Math.round(r.width) + 6}px`, height: `${Math.round(r.height) + 6}px` });
  }
  function hideExtent(s = null) {
    if (s && extentOwner !== s) return;
    extentOwner = null; extentBox?.remove();
  }
  /* ---- the layout lock ---- */
  let lockButton = null;
  function paintLock() {
    if (doc.documentElement?.dataset) {
      if (locked) doc.documentElement.dataset.dockLocked = ''; else delete doc.documentElement.dataset.dockLocked;
    }
    if (!lockButton) return;
    lockButton.setAttribute('aria-pressed', String(locked));
    lockButton.setAttribute('aria-label', locked ? LOCK_COPY.unlock : LOCK_COPY.lock);
    lockButton.title = locked ? LOCK_COPY.unlockTitle : LOCK_COPY.lockTitle;
    lockButton.innerHTML = LOCK_SVG(locked);
  }
  function setLocked(value) {
    const next = !!value;
    if (next === locked) return;
    locked = next;
    if (locked && active && active.gesture?.kind !== 'tab') cancel(active);
    if (locked) hideExtent();
    for (const s of slots) { s.root.classList.toggle('locked', locked); if (locked) s.root.classList.remove('resize-hint'); }
    paintLock();
    try { lockPrefs?.write?.(locked); } catch { /* the save can wait */ }
  }
  if (lockPrefs?.host) {
    const host = lockPrefs.host;
    lockButton = doc.createElement('button'); lockButton.type = 'button'; lockButton.id = 'dock-lock';
    const speaker = [...(host.children || [])].find(c => c.id === 'audio-controls') || null;
    host.insertBefore(lockButton, speaker);
    lockButton.addEventListener('click', () => setLocked(!locked));
  }
  paintLock();
  function minimize(s, value) {
    if (s.min === value) return;
    cancel(s);
    s.min = value; s.fresh = false;
    if (!value) raise(s);
    paint(s, true); save(s); s.onMin?.(value);
  }
  function drawStrip() {
    const any = strip.childElementCount > 0;
    if (strip.hidden === any) { strip.hidden = !any; onStrip?.(); }
    for (const [i, s] of ordered().entries()) s.chip.style.order = String(i);
    syncTabDom();
    fitStrip();
  }
  /** Full tabs, compact tabs, then icon pills on up to three rows: the strip never scrolls. */
  function fitStrip() {
    const root = doc.documentElement, st = strip.style;
    const before = `${strip.dataset.fit || 'full'}/${rows}`;
    // A chip's max-width transition would let over() read the old widths and pick a mode that overflows once they grow.
    if (strip.dataset) strip.dataset.measuring = '';
    root?.style?.removeProperty?.('--dock-h');
    st.removeProperty?.('--dk-rows'); st.removeProperty?.('--dk-basis'); st.removeProperty?.('--dk-max');
    if (strip.dataset) delete strip.dataset.roomy;
    strip.dataset.fit = 'full'; rows = 1;
    const list = ordered();
    const over = () => strip.scrollWidth > strip.clientWidth + 1 || strip.scrollHeight > strip.clientHeight + 1;
    if (list.length && !strip.hidden && strip.clientWidth > 0 && over()) {
      strip.dataset.fit = 'compact';
      if (over()) {
        strip.dataset.fit = 'icon';
        const max = strip.clientWidth >= PILL_WIDE_FROM ? PILL_WIDE_MAX : PILL_MAX;
        const plan = pillPlan(list.length, strip.clientWidth, { max });
        st.setProperty?.('--dk-max', `${max}px`);
        if (plan.basis >= PILL_LABEL_FROM) strip.dataset.roomy = '';
        rows = plan.rows;
        const h = stripHeight(rows);
        if (h) root?.style?.setProperty?.('--dock-h', `${h}px`);
        st.setProperty?.('--dk-rows', String(rows));
        // Rounding can still push one pill to a row that is not there: shave the basis until it fits.
        for (let basis = plan.basis, i = 0; i < 12; i++, basis -= 2) {
          st.setProperty?.('--dk-basis', `${Math.max(12, basis)}px`);
          if (!over() || basis <= 12) break;
        }
      }
    }
    if (strip.dataset) delete strip.dataset.measuring;
    if (`${strip.dataset.fit}/${rows}` !== before) { onStrip?.(); queueFit(); }
  }
  let rows = 1, fitFrame = false;
  const queueStrip = () => {
    if (fitFrame) return;
    fitFrame = true;
    const run = () => { fitFrame = false; fitStrip(); };
    win.requestAnimationFrame ? win.requestAnimationFrame(run) : run();
  };
  win.addEventListener?.('resize', queueStrip);
  doc.fonts?.ready?.then?.(queueStrip);
  function syncTabDom() {
    // Moving a captured node can drop its pointer capture. Commit DOM order after the drag ends.
    if (active?.gesture?.kind === 'tab') return;
    const focus = doc.activeElement;
    for (const [i, s] of ordered().entries()) {
      if (strip.children[i] !== s.chip) strip.insertBefore(s.chip, strip.children[i] || null);
    }
    if (focus && doc.activeElement !== focus && ordered().some(s => s.chip === focus)) focus.focus?.({ preventScroll: true });
  }
  function firstSpot(s, placed, settled = false) {
    const w = windowSize(s, { W: win.innerWidth, defaultW: width(s) }).w;
    const h = Math.min(Math.max(s.root.getBoundingClientRect().height || 100, s.reserveH), win.innerHeight - bottomSpace() - 48);
    const occupied = placed.filter(p => !p.min && !p.door && !p.anchor).map(p => {
      const r = p.root.getBoundingClientRect(); return { x: p.x, y: p.y, w: r.width, h: Math.max(r.height, p.reserveH) };
    });
    // #shop is an empty flex column down to the page foot. Only its visible children occupy room.
    const shop = doc.getElementById('shop');
    const children = [...(shop?.children || [])].map(el => el.getBoundingClientRect?.()).filter(r => r?.width && r?.height);
    const button = doc.getElementById('btn')?.getBoundingClientRect?.();
    const underShop = Math.max(48, children.length ? Math.max(...children.map(r => r.bottom)) + 12 : (button?.bottom || 140) + 12);
    const top = isRail() ? 48 : underShop;
    const found = freeObjectSpot({ W: win.innerWidth, H: win.innerHeight, w, h, top, bottom: bottomSpace(), phone: !isRail(), preferred: s.side, centerTop: underShop }, occupied);
    if (found || settled) return found;
    return { x: isRail() ? (s.side === 'left' ? 8 : win.innerWidth - w - 8) : (win.innerWidth - w) / 2, y: top };
  }
  function fit() {
    queued = false;
    if (paused()) return;
    const placed = [...slots].filter(s => !s.fresh && !s.min && !s.door);
    for (const s of slots) {
      if (s.fresh) {
        if (!s.min && !s.door) {
          const spot = firstSpot(s, placed, true);
          if (spot) { Object.assign(s, spot); s.side = spot.x + width(s) / 2 > win.innerWidth / 2 ? 'right' : 'left'; }
          else {
            minimize(s, true);
            // An object that asked to open but found no room waits as a tab, and that tab says NEW.
            if (s.wantOpen && s.seen !== true) { s.seen = false; paintNew(s); }
          }
        }
        s.fresh = false;
      }
      paint(s, true); save(s); if (!placed.includes(s) && !s.min && !s.door) placed.push(s);
    }
  }
  function queueFit() {
    if (queued) return;
    queued = true;
    win.requestAnimationFrame ? win.requestAnimationFrame(fit) : fit();
  }
  win.addEventListener?.('resize', queueFit);

  function finish(s, event = {}, cancelled = false) {
    const d = s.gesture;
    if (!d || (event.pointerId != null && event.pointerId !== d.id)) return;
    s.gesture = null;
    untimer(d.timer);
    if (active === s) active = null;
    s.root.classList.remove('dragging');
    s.root.classList.remove('resizing');
    s.chip?.classList.remove('dragging');
    try { if (d.target.hasPointerCapture?.(d.id)) d.target.releasePointerCapture(d.id); } catch { /* detached capture */ }
    if (d.held || d.moved) {
      s.suppressClick = true;
      untimer(s.suppressTimer);
      s.suppressTimer = timer(() => { s.suppressClick = false; }, 500);
    }
    if (d.kind === 'tab' && d.moved) {
      for (const item of ordered()) save(item);
      syncTabDom();
    } else if ((d.kind === 'object' || d.kind === 'resize') && d.moved) { save(s); moved(s); }
    if (d.kind === 'resize' && !s.gripHover) hideExtent(s);
    if (cancelled) s.root.classList.remove('holding');
  }
  function cancel(s) { finish(s, {}, true); }
  function begin(s, ev, kind) {
    if (paused() || s.gesture || ev.button > 0 || ev.isPrimary === false) return;
    // A new physical press may hit a button just after a drag suppressed its own release click.
    s.suppressClick = false;
    if (kind === 'object' && (locked || s.anchor || s.min || !canMoveFrom(ev.target, s.root))) return;
    if (active) cancel(active);
    active = s;
    const d = { kind, id: ev.pointerId, target: kind === 'object' ? s.root : s.chip, x: ev.clientX, y: ev.clientY,
      left: s.x, top: s.y, held: false, moved: false, touch: ev.pointerType === 'touch', timer: null, from: ev.target, at: win.performance?.now?.() ?? Date.now() };
    s.gesture = d;
    const hold = () => {
      if (s.gesture !== d || paused()) { cancel(s); return; }
      d.held = true; s.fresh = false;
      if (kind === 'object') { raise(s); s.root.classList.add('dragging'); }
      else s.chip.classList.add('dragging');
      try { d.target.setPointerCapture?.(d.id); } catch { /* unsupported pointer */ }
    };
    d.activate = hold;
    // Capture a mouse press immediately so a quick drag cannot leave a small casing.
    if (kind === 'object' && !d.touch) {
      try { d.target.setPointerCapture?.(d.id); } catch { /* unsupported pointer */ }
    }
    if (kind === 'object' || d.touch) d.timer = timer(hold, HOLD_MS);
  }
  function reorder(s, index) {
    const list = ordered();
    const old = list.indexOf(s);
    if (old < 0) return;
    list.splice(old, 1);
    list.splice(Math.max(0, Math.min(index, list.length)), 0, s);
    list.forEach((item, i) => { item.tabOrder = i; item.chip.style.order = String(i); });
  }
  function tabMove(s, ev) {
    const d = s.gesture;
    if (!d || d.kind !== 'tab' || d.id !== ev.pointerId) return;
    if (paused()) { cancel(s); return; }
    const distance = Math.hypot(ev.clientX - d.x, ev.clientY - d.y);
    if (!d.held) {
      if (d.touch) { if (distance > holdSlop(true)) cancel(s); return; }
      if (distance < 6) return;
      d.held = true; s.chip.classList.add('dragging');
      try { s.chip.setPointerCapture?.(d.id); } catch { /* unsupported pointer */ }
    }
    ev.preventDefault();
    if (distance < 6) return;
    d.moved = true;
    const rest = ordered().filter(item => item !== s);
    const before = rows > 1
      ? r => ev.clientY < r.top || (ev.clientY < r.bottom && ev.clientX < r.left + r.width / 2)
      : r => ev.clientX < r.left + r.width / 2;
    let index = rest.findIndex(item => before(item.chip.getBoundingClientRect()));
    if (index < 0) index = rest.length;
    reorder(s, index);
  }
  function objectMove(s, ev) {
    const d = s.gesture;
    if (!d || d.kind !== 'object' || d.id !== ev.pointerId) return;
    if (paused()) { cancel(s); return; }
    const dx = ev.clientX - d.x, dy = ev.clientY - d.y;
    if (!d.held) {
      if (Math.hypot(dx, dy) <= holdSlop(d.touch)) return;
      // Touch needs the hold to distinguish moving an object from scrolling.
      // A mouse drag already expresses that intent and must not be cancelled.
      if (d.touch) {
        // A swipe that starts in a scrolling list is a scroll. Anywhere else on the casing it was a try at moving it.
        const missed = !scrollContainer(d.from, s.root);
        const after = Math.round((win.performance?.now?.() ?? Date.now()) - d.at);
        cancel(s);
        if (missed && win.CustomEvent) doc.dispatchEvent(new win.CustomEvent('fz-dock-hold-miss', { detail: { root: s.root, after } }));
        return;
      }
      untimer(d.timer); d.activate();
      if (!d.held) return;
    }
    ev.preventDefault(); ev.stopPropagation?.();
    d.moved ||= Math.hypot(dx, dy) > 1;
    s.x = d.left + dx; s.y = d.top + dy; paint(s, true);
  }
  function resizeTo(s, d, factor) {
    const min = Math.max(WINDOW_MIN.w / width(s), .5);
    const max = Math.max(min, Math.min(2.5, (d.right + EDGE_BLEED) / width(s), (d.bottom + EDGE_BLEED) / d.baseH));
    const scale = Math.max(min, Math.min(max, factor));
    s.w = width(s) * scale;
    s.x = d.right - s.w; s.y = d.bottom - d.baseH * scale;
    s.fresh = false;
    paint(s, true);
  }
  function resizeStart(s, ev) {
    if (paused() || locked || s.min || ev.button > 0 || ev.isPrimary === false) return;
    ev.preventDefault(); ev.stopPropagation();
    if (active) cancel(active);
    raise(s); active = s;
    const rect = s.root.getBoundingClientRect(), scale = s.scale || 1;
    s.gesture = { kind: 'resize', id: ev.pointerId, target: s.handle, held: true, moved: false,
      x: ev.clientX, y: ev.clientY, right: rect.right, bottom: rect.bottom,
      baseH: rect.height / scale, startScale: scale };
    s.root.classList.add('resizing');
    s.handle.setPointerCapture?.(ev.pointerId);
  }
  function resizeMove(s, ev) {
    const d = s.gesture;
    if (!d || d.kind !== 'resize' || d.id !== ev.pointerId) return;
    if (paused()) { cancel(s); return; }
    ev.preventDefault(); ev.stopPropagation();
    const dx = d.x - ev.clientX, dy = d.y - ev.clientY, w = width(s);
    d.moved ||= Math.hypot(dx, dy) > 1;
    resizeTo(s, d, d.startScale + (dx * w + dy * d.baseH) / (w*w + d.baseH*d.baseH));
    if (extentOwner === s) showExtent(s);
  }
  function paintNew(s) {
    const on = tabIsNew(s);
    s.chip?.classList.toggle('dk-new', on);
    if (s.tag) s.tag.hidden = !on;
    s.chip?.setAttribute('data-new', String(on));
  }
  /** The first hover, touch, key or click on a NEW tab clears it for good. */
  function seeTab(s) {
    untimer(s.seenTimer); s.seenTimer = null;
    if (s.seen !== false) return;
    s.seen = true; paintNew(s); save(s);
  }
  /** A purchase added this tab. Tabs the player has met before (seen true) stay quiet. */
  function markNew(chip) {
    const s = [...slots].find(item => item.chip && item.chip === chip);
    if (!s || s.seen === true) return false;
    if (s.seen !== false) { s.seen = false; paintNew(s); save(s); }
    return true;
  }
  function add({ edge, anchor = null, title = '', icon = '', order = 100000, chip = true, onChip = null, door = null,
    min, onMin = null, width: defaultWidth = null, reserveHeight = 0, allowUnderStrip = false, layout: original = {}, onLayout = null }) {
    const saved = migrateLayout(original);
    const counts = [...slots].reduce((n, s) => { if (!s.min && !s.door) n[s.side]++; return n; }, { left: 0, right: 0 });
    const side = edge === 'left' || edge === 'right' ? edge : emptierEdge(counts.left, counts.right);
    const root = doc.createElement('section'); root.className = `dk${door ? ' door' : ''}`;
    root.setAttribute('aria-label', title);
    const body = doc.createElement('div'); body.className = 'dk-b';
    root.appendChild(body); (anchor?.el || rails[side]).appendChild(root);
    const restored = Number.isFinite(saved.x) && Number.isFinite(saved.y);
    const z = Number.isFinite(saved.z) && saved.z > 0 ? Math.min(saved.z, 1000000) : topZ + 1;
    topZ = Math.max(topZ, z);
    const maxOrder = ordered().reduce((n, s) => Math.max(n, s.tabOrder), -1);
    const s = { root, body, title, side, anchor, allowUnderStrip, chip: null, door: !!door, onMin, onLayout, z, defaultW: desired(defaultWidth),
      x: saved.x ?? 8, y: saved.y ?? 48, w: saved.w, h: null, reserveH: Math.max(0, Math.min(1000, finite(reserveHeight, 0))), tabOrder: Number.isFinite(saved.tabOrder) ? saved.tabOrder : maxOrder + 1,
      // wantOpen: the caller asked for it open (theme screens on a phone). With no free room it still waits as a NEW tab.
      serial: serial++, min: typeof min === 'boolean' ? min : typeof saved.min === 'boolean' ? saved.min : !isRail(), fresh: !restored && !anchor, wantOpen: min === false,
      ready: false, readyLabel: 'Ready', gesture: null, suppressClick: false, seen: typeof saved.seen === 'boolean' ? saved.seen : null };
    if (!door) {
      const handle = doc.createElement('button'); handle.type = 'button'; handle.className = 'dk-resize';
      handle.setAttribute('aria-label', `Resize ${title}`);
      handle.title = 'Drag to resize. Arrow keys adjust size; Home resets.';
      s.handle = handle; root.appendChild(handle);
      handle.addEventListener('pointerdown', ev => resizeStart(s, ev));
      handle.addEventListener('pointermove', ev => resizeMove(s, ev));
      for (const type of ['pointerup', 'pointercancel', 'lostpointercapture']) handle.addEventListener(type, ev => finish(s, ev, type !== 'pointerup'));
      // Mouse or pen over the corner: a dashed outline shows everything the object takes up, toys hanging below included.
      handle.addEventListener('pointerenter', ev => {
        if (ev.pointerType === 'touch' || locked || s.min) return;
        s.gripHover = true; showExtent(s);
      });
      handle.addEventListener('pointerleave', () => { s.gripHover = false; if (s.gesture?.kind !== 'resize') hideExtent(s); });
      handle.addEventListener('keydown', ev => {
        if (paused() || locked) return;
        const delta = ['ArrowLeft','ArrowUp'].includes(ev.key) ? .05 : ['ArrowRight','ArrowDown'].includes(ev.key) ? -.05 : 0;
        if (!delta && ev.key !== 'Home') return;
        ev.preventDefault(); ev.stopPropagation();
        const r = root.getBoundingClientRect(), scale = s.scale || 1;
        resizeTo(s, {right:r.right,bottom:r.bottom,baseH:r.height/scale}, ev.key === 'Home' ? 1 : scale + delta);
        save(s);
      });
      root.addEventListener('pointerdown', ev => {
        if (ev.pointerType !== 'touch' || locked) return;
        root.classList.add('resize-hint'); untimer(s.hintTimer);
        s.hintTimer = timer(() => root.classList.remove('resize-hint'), 1800);
      });
    }
    slots.add(s);
    if (!restored && !anchor) Object.assign(s, firstSpot(s, [...slots].filter(item => item !== s)));
    const suppress = ev => {
      if (!s.suppressClick || ev.detail === 0) return;
      ev.preventDefault(); ev.stopImmediatePropagation?.(); s.suppressClick = false;
    };
    root.addEventListener('click', suppress, true);
    root.addEventListener('pointerdown', ev => { if (!paused()) raise(s); begin(s, ev, 'object'); }, true);
    root.addEventListener('pointermove', ev => objectMove(s, ev));
    root.addEventListener('touchstart', ev => {
      if (!paused() && !locked && canMoveFrom(ev.target, root) && !scrollContainer(ev.target, root) && ev.touches?.length === 1) ev.preventDefault();
    }, { passive: false });
    root.addEventListener('touchmove', ev => {
      if (s.gesture?.kind === 'object' && s.gesture.held) ev.preventDefault();
    }, { passive: false });
    root.addEventListener('contextmenu', ev => { if (s.gesture || canMoveFrom(ev.target, root)) ev.preventDefault(); });
    root.addEventListener('dragstart', ev => { if (canMoveFrom(ev.target, root)) ev.preventDefault(); });
    for (const event of ['pointerup', 'pointercancel', 'lostpointercapture']) root.addEventListener(event, ev => finish(s, ev, event !== 'pointerup'));
    if (chip) {
      const c = doc.createElement('button'); c.type = 'button'; c.className = 'dk-chip';
      const i = doc.createElement('span'); i.className = 'dk-i'; i.textContent = icon;
      const n = doc.createElement('span'); n.className = 'dk-label'; n.textContent = title;
      const dot = doc.createElement('span'); dot.className = 'dk-ready'; dot.hidden = true; dot.setAttribute('aria-hidden', 'true');
      const tag = doc.createElement('span'); tag.className = 'dk-new-tag'; tag.textContent = 'NEW'; tag.hidden = true; tag.setAttribute('aria-hidden', 'true');
      c.append(i, n, dot, tag);
      c.title = `${title}. Click to show or hide. Drag the object body to move; on touch, hold first. Drag this tab to reorder. Keyboard: Alt+Left/Right reorders; Ctrl+arrows moves the object.`;
      c.setAttribute('aria-label', title);
      c.addEventListener('click', ev => {
        if (s.suppressClick && ev.detail !== 0) { suppress(ev); return; }
        if (paused()) return;
        if (door || onChip) { (door || onChip)(); return; }
        // Open but under another object: the first press brings it up, the next one folds it.
        if (covered(s)) { raise(s); paint(s); return; }
        minimize(s, !s.min);
      });
      c.addEventListener('pointerdown', ev => { seeTab(s); begin(s, ev, 'tab'); });
      c.addEventListener('keydown', () => seeTab(s));
      c.addEventListener('pointerenter', ev => { if (ev.pointerType === 'mouse' && s.seen === false) { untimer(s.seenTimer); s.seenTimer = timer(() => seeTab(s), NEW_HOVER_MS); } });
      c.addEventListener('pointerleave', () => { untimer(s.seenTimer); s.seenTimer = null; });
      c.addEventListener('pointermove', ev => tabMove(s, ev));
      c.addEventListener('contextmenu', ev => ev.preventDefault());
      // A held tab owns touch movement. The strip itself never scrolls.
      c.addEventListener('touchmove', ev => { if (s.gesture?.held) ev.preventDefault(); }, { passive: false });
      for (const event of ['pointerup', 'pointercancel', 'lostpointercapture']) c.addEventListener(event, ev => finish(s, ev, event !== 'pointerup'));
      c.addEventListener('keydown', ev => {
        if (paused()) return;
        const dx = ev.key === 'ArrowRight' ? 1 : ev.key === 'ArrowLeft' ? -1 : 0;
        const dy = ev.key === 'ArrowDown' ? 1 : ev.key === 'ArrowUp' ? -1 : 0;
        if (ev.key === 'Escape') { cancel(s); return; }
        if (!dx && !dy) return;
        const list = ordered(), index = list.indexOf(s);
        if (ev.altKey && dx) {
          ev.preventDefault(); reorder(s, index + dx); for (const item of ordered()) save(item); syncTabDom();
          c.focus?.();
        } else if (ev.ctrlKey && !s.door && !locked) {
          ev.preventDefault(); if (s.min) minimize(s, false);
          const step = ev.shiftKey ? 32 : 8;
          if (s.anchor) s.anchor.shift(dx * step, dy * step);
          else { s.x += dx * step; s.y += dy * step; }
          s.fresh = false; raise(s); paint(s, true); save(s);
        } else if (dx) { ev.preventDefault(); list[(index + dx + list.length) % list.length].chip.focus?.(); }
      });
      strip.appendChild(c); s.chip = c; s.dot = dot; s.tag = tag; s.iconEl = i; s.labelEl = n; drawStrip(); paintNew(s);
    }
    s.layout = () => layout(s);
    s.cancelMove = () => cancel(s);
    body.dock = {
      get root() { return root; }, get chip() { return s.chip; },
      get visible() { return !s.min && !s.door; }, get moving() { return s.gesture?.kind === 'object' && s.gesture.held; },
      setVisible(value) { minimize(s, !value); }, cancelMove: s.cancelMove,
      bringToFront() { raise(s); },
      setReady(value, label = 'Ready') {
        const next = !!value;
        if (next === s.ready && label === s.readyLabel) return;
        s.ready = next; s.readyLabel = String(label || 'Ready');
        if (s.dot) s.dot.hidden = !next;
        s.chip?.setAttribute('data-ready', String(next));
        const shown = s.mask?.title ?? title;
        s.chip?.setAttribute('aria-label', next ? `${shown}: ${s.readyLabel}` : shown);
      },
      /** The tab reads as something else (another title and icon: a Node or text) until undisguise(); its own label
       *  and icon stay in the tab, only hidden, so they come back exactly. Answers the tab, or null without one. */
      disguise({ title: t = '', icon: ic = '' } = {}) {
        const c = s.chip;
        if (!c || !s.iconEl) return null;
        if (!s.mask) {
          const i = doc.createElement('span'); i.className = 'dk-i';
          const n = doc.createElement('span'); n.className = 'dk-label';
          c.insertBefore(i, s.iconEl); c.insertBefore(n, s.iconEl);
          s.mask = { i, n, tip: c.title, title: '' };
          s.iconEl.style.display = 'none'; s.labelEl.style.display = 'none';
        }
        s.mask.title = String(t);
        s.mask.n.textContent = s.mask.title;
        if (ic && typeof ic === 'object') { s.mask.i.textContent = ''; s.mask.i.append(ic); } else s.mask.i.textContent = String(ic || '');
        c.setAttribute('aria-label', s.ready ? `${s.mask.title}: ${s.readyLabel}` : s.mask.title);
        c.title = s.mask.title;
        c.dataset.disguised = '1';
        queueFit();
        return c;
      },
      undisguise() {
        const m = s.mask, c = s.chip;
        if (!m || !c) return;
        s.mask = null;
        m.i.remove(); m.n.remove();
        s.iconEl.style.display = ''; s.labelEl.style.display = '';
        c.setAttribute('aria-label', s.ready ? `${title}: ${s.readyLabel}` : title);
        c.title = m.tip;
        delete c.dataset.disguised;
        queueFit();
      },
      get disguised() { return !!s.mask; },
    };
    s.remove = () => {
      cancel(s); hideExtent(s); untimer(s.suppressTimer); untimer(s.hintTimer); untimer(s.seenTimer); s.observer?.disconnect(); slots.delete(s);
      root.remove(); s.chip?.remove(); drawStrip();
    };
    // New content may be taller than the idle toy. Clamp it without changing its chosen width.
    if (win.ResizeObserver) { s.observer = new win.ResizeObserver(() => { if (!s.min && !s.gesture) paint(s); }); s.observer.observe(body); }
    paint(s); queueFit();
    return s;
  }
  win.addEventListener?.('blur', () => { if (active) cancel(active); });
  win.addEventListener?.('pointerup', ev => { if (active) finish(active, ev); });
  win.addEventListener?.('pointercancel', ev => { if (active) finish(active, ev, true); });
  return { add, isRail, fitStrip, markNew, setLocked, get locked() { return locked; }, get lockButton() { return lockButton; },
    cancelMove() { if (active) cancel(active); }, close() { for (const s of slots) minimize(s, true); } };
}
