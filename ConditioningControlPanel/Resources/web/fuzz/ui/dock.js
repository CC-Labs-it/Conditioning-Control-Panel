/* Movable widget windows. Rails supply their first positions; the player owns the layout after that. */
export const DOCK_MIN_W = 1100;
export const dockIsRail = (w) => w >= DOCK_MIN_W;
export function railWidth(w) {
  return dockIsRail(w) ? Math.round(Math.max(200, Math.min(300, (w - 776) / 2))) : 0;
}
export const emptierEdge = (left, right) => left <= right ? 'left' : 'right';
export const slotOrder = (boughtAt, nth = 0) => (boughtAt < 0 ? 100000 : boughtAt * 8) + Math.min(7, nth);
export const WINDOW_MIN = Object.freeze({ w: 180, h: 100 });
const desired = (v, floor) => Number.isFinite(v) && v > 0 ? Math.min(4096, Math.max(floor, v)) : null;

/** A small viewport clips the presentation, never the player's chosen size. Null height means content-sized. */
export function windowSize({ w, h }, { W, H, y, defaultW }) {
  return { w: Math.round(Math.max(1, Math.min(desired(w, WINDOW_MIN.w) ?? defaultW, W - 8))),
    h: desired(h, WINDOW_MIN.h) == null ? null : Math.round(Math.max(44, Math.min(desired(h, WINDOW_MIN.h), H - y - 8))) };
}

/** Keep the whole title bar reachable, including its minimize button. Bodies may scroll. */
export function clampWindow({ x = 8, y = 48 }, { w, W, H }) {
  return {
    x: Math.round(Math.max(0, Math.min(Number.isFinite(x) ? x : 8, Math.max(0, W - w)))),
    y: Math.round(Math.max(44, Math.min(Number.isFinite(y) ? y : 48, Math.max(44, H - 48)))),
  };
}

export function createDock({ doc, rails, strip, onStrip }) {
  const win = doc.defaultView;
  const slots = new Set(), count = { left: 0, right: 0 };
  let topZ = 0, queued = false, active = null;
  const isRail = () => dockIsRail(win.innerWidth);
  const width = (s) => s?.defaultW ?? (isRail() ? railWidth(win.innerWidth) - 12 : Math.min(360, win.innerWidth - 16));
  const paused = () => doc.getElementById('pause')?.hidden === false;
  function layout(s) { return { edge: s.side, x: s.x, y: s.y, w: s.w, h: s.h, min: s.min, z: s.z }; }
  function save(s) { s.onLayout?.(layout(s)); }
  function moved(s) {
    if (win.CustomEvent) doc.dispatchEvent(new win.CustomEvent('fz-dock-move', { detail: { root: s.root, x: s.x, y: s.y } }));
  }
  function paint(s, notify = false) {
    const view = { W: win.innerWidth, H: win.innerHeight, y: s.y, defaultW: width(s) };
    const size = windowSize(s, view);
    const p = clampWindow(s, { w: size.w, W: view.W, H: view.H });
    s.x = p.x; s.y = p.y;
    const h = windowSize(s, { ...view, y: s.y }).h;
    Object.assign(s.root.style, { left: `${s.x}px`, top: `${s.y}px`, width: `${size.w}px`, height: s.min || h == null ? '' : `${h}px`, maxHeight: `${Math.max(44, win.innerHeight - s.y - 8)}px`, zIndex: String(s.z) });
    s.root.classList.toggle('sized', h != null && !s.min);
    s.root.classList.toggle('min', s.min);
    s.root.classList.toggle('open', !s.min);
    s.chip?.classList.toggle('on', !s.min);
    s.fold.textContent = s.min ? '+' : '^';
    s.fold.setAttribute('aria-label', `${s.min ? 'Restore' : 'Minimize'} ${s.title}`);
    s.fold.setAttribute('aria-expanded', String(!s.min));
    s.chip?.setAttribute('aria-expanded', String(!s.min));
    if (notify) moved(s);
  }
  function raise(s) {
    if (s.z === topZ) return;
    s.z = ++topZ; s.root.style.zIndex = String(s.z); save(s);
  }
  function minimize(s, value) {
    s.min = value; s.fresh = false;
    if (!value) raise(s);
    paint(s, true); save(s); s.onMin?.(value);
  }
  function drawStrip() {
    const any = strip.childElementCount > 0;
    if (strip.hidden === any) { strip.hidden = !any; onStrip?.(); }
  }
  function firstSpot(s, placed) {
    const same = placed.filter((p) => p.side === s.side);
    const floor = same.reduce((y, p) => Math.max(y, p.y + (p.root.getBoundingClientRect().height || 100) + 8), 48);
    const h = s.root.getBoundingClientRect().height || 140;
    const cascade = floor + h > win.innerHeight - 8;
    const offset = cascade ? (same.length % 7) * 26 : 0;
    const w = windowSize(s, { W: win.innerWidth, H: win.innerHeight, y: 48, defaultW: width(s) }).w;
    return { x: isRail() ? (s.side === 'left' ? 6 + offset : win.innerWidth - w - 6 - offset) : (win.innerWidth - w) / 2,
      y: isRail() ? (cascade ? 48 + offset : floor) : Math.min(180 + (same.length % 4) * 28, win.innerHeight * .35) };
  }
  function fit() {
    queued = false;
    if (paused()) return;
    const placed = [];
    for (const s of slots) {
      if (s.fresh) { Object.assign(s, firstSpot(s, placed)); s.fresh = false; }
      paint(s, true); save(s); placed.push(s);
    }
  }
  function queueFit() {
    if (queued) return;
    queued = true;
    win.requestAnimationFrame ? win.requestAnimationFrame(fit) : fit();
  }
  win.addEventListener?.('resize', queueFit);

  function add({ edge, title = '', icon = '', order = 100000, chip = true, onChip = null, door = null,
    min, onMin = null, pin = false, keep = false, width: defaultWidth = null, layout: saved = {}, onLayout = null }) {
    const side = edge === 'left' || edge === 'right' ? edge : emptierEdge(count.left, count.right);
    count[side]++;
    const root = doc.createElement('section'); root.className = `dk${pin ? ' pin' : ''}${door ? ' door' : ''}`;
    root.setAttribute('aria-label', title);
    root.style.order = String(order);
    const head = doc.createElement('div'); head.className = 'dk-t'; head.title = `Move ${title}`;
    const ico = doc.createElement('span'); ico.className = 'dk-i'; ico.textContent = icon;
    const name = doc.createElement(door ? 'button' : 'span'); name.className = 'dk-n'; name.textContent = title;
    if (door) { name.type = 'button'; name.addEventListener('click', door); }
    const fold = doc.createElement('button'); fold.type = 'button'; fold.className = 'dk-f';
    head.append(ico, name);
    const body = doc.createElement('div'); body.className = 'dk-b';
    root.append(head, fold, body); rails[side].appendChild(root);
    const restored = Number.isFinite(saved.x) && Number.isFinite(saved.y);
    const z = Number.isFinite(saved.z) && saved.z > 0 ? Math.min(saved.z, 1000000) : topZ + 1;
    topZ = Math.max(topZ, z);
    const slot = { root, body, head, fold, title, side, chip: null, pin, keep, onMin, onLayout, z, defaultW: desired(defaultWidth, WINDOW_MIN.w),
      x: saved.x ?? 8, y: saved.y ?? 48, w: desired(saved.w, WINDOW_MIN.w), h: desired(saved.h, WINDOW_MIN.h),
      min: typeof min === 'boolean' ? min : !isRail(), fresh: !restored };
    slots.add(slot);
    if (!restored) Object.assign(slot, firstSpot(slot, [...slots].filter((s) => s !== slot)));
    root.addEventListener('pointerdown', () => { if (!paused()) raise(slot); }, true);
    fold.addEventListener('click', () => { if (!paused()) minimize(slot, !slot.min); });
    head.addEventListener('pointerdown', (ev) => {
      if (paused() || (ev.button != null && ev.button !== 0) || ev.target.closest?.('button,input,select,textarea,a,[role="button"]')) return;
      active = slot; slot.fresh = false;
      slot.drag = { kind: 'move', target: head, id: ev.pointerId, x: ev.clientX, y: ev.clientY, left: slot.x, top: slot.y };
      root.classList.add('dragging'); head.setPointerCapture?.(ev.pointerId); ev.preventDefault();
    });
    head.addEventListener('pointermove', (ev) => {
      const d = slot.drag;
      if (!d || d.kind !== 'move' || d.id !== ev.pointerId || paused()) return;
      slot.x = d.left + ev.clientX - d.x; slot.y = d.top + ev.clientY - d.y; paint(slot, true);
    });
    const finish = (ev) => {
      if (!slot.drag || (ev.pointerId != null && ev.pointerId !== slot.drag.id)) return;
      const { id, target } = slot.drag; slot.drag = null; active = null; root.classList.remove('dragging'); root.classList.remove('resizing');
      if (target.hasPointerCapture?.(id)) target.releasePointerCapture(id);
      save(slot); moved(slot);
    };
    for (const event of ['pointerup', 'pointercancel', 'lostpointercapture']) head.addEventListener(event, finish);
    slot.finish = finish;
    slot.handles = [];
    if (!door) for (const edge of ['corner', 'right', 'bottom']) {
      const handle = doc.createElement('button'); handle.type = 'button'; handle.className = `dk-resize dk-resize-${edge}`;
      handle.setAttribute('aria-label', `Resize ${title}${edge === 'right' ? ' width' : edge === 'bottom' ? ' height' : ''}. Arrow keys resize, Shift moves faster, Home resets size.`);
      handle.title = 'Resize. Arrow keys adjust; Home resets size.';
      const resize = (w, h, axes) => {
        if (edge !== 'bottom' && axes.x) slot.w = Math.max(WINDOW_MIN.w, Math.min(w, win.innerWidth - slot.x - 8));
        if (edge !== 'right' && axes.y) slot.h = Math.max(WINDOW_MIN.h, Math.min(h, win.innerHeight - slot.y - 8));
        slot.fresh = false; paint(slot, true);
      };
      const sizeNow = () => { const r = root.getBoundingClientRect(); return { w: r.width || parseFloat(root.style.width), h: r.height || 150 }; };
      handle.addEventListener('pointerdown', (ev) => {
        if (paused() || slot.min || (ev.button != null && ev.button !== 0)) return;
        raise(slot); active = slot; slot.fresh = false;
        slot.drag = { ...sizeNow(), kind: 'resize', target: handle, id: ev.pointerId, x: ev.clientX, y: ev.clientY };
        root.classList.add('resizing'); handle.setPointerCapture?.(ev.pointerId); ev.preventDefault();
      });
      handle.addEventListener('pointermove', (ev) => {
        const d = slot.drag;
        if (!d || d.target !== handle || d.id !== ev.pointerId || paused()) return;
        const dx = ev.clientX - d.x, dy = ev.clientY - d.y;
        resize(d.w + dx, d.h + dy, { x: dx !== 0, y: dy !== 0 });
      });
      for (const event of ['pointerup', 'pointercancel', 'lostpointercapture']) handle.addEventListener(event, finish);
      handle.addEventListener('keydown', (ev) => {
        if (paused() || slot.min) return;
        const dx = ev.key === 'ArrowRight' ? 1 : ev.key === 'ArrowLeft' ? -1 : 0;
        const dy = ev.key === 'ArrowDown' ? 1 : ev.key === 'ArrowUp' ? -1 : 0;
        if (ev.key !== 'Home' && !(dx && edge !== 'bottom') && !(dy && edge !== 'right')) return;
        ev.preventDefault(); ev.stopPropagation?.(); raise(slot);
        if (ev.key === 'Home') { slot.w = null; slot.h = null; paint(slot, true); }
        else { const size = sizeNow(), step = ev.shiftKey ? 32 : 8; resize(size.w + dx * step, size.h + dy * step, { x: dx !== 0, y: dy !== 0 }); }
        save(slot);
      });
      root.appendChild(handle); slot.handles.push(handle);
    }
    if (chip) {
      const c = doc.createElement('button'); c.type = 'button'; c.className = 'dk-chip'; c.style.order = String(order);
      const i = doc.createElement('span'); i.className = 'dk-i'; i.textContent = icon;
      const n = doc.createElement('span'); n.textContent = title; c.append(i, n);
      c.addEventListener('click', () => {
        if (paused()) return;
        if (door || onChip) { (door || onChip)(); return; }
        if (slot.min) minimize(slot, false);
        else if (slot.z !== topZ) raise(slot);
        else minimize(slot, true);
      });
      strip.appendChild(c); slot.chip = c; drawStrip();
    } else root.classList.add('nochip');
    paint(slot); queueFit();
    slot.layout = () => layout(slot);
    slot.remove = () => {
      slot.finish({}); count[side] = Math.max(0, count[side] - 1); slots.delete(slot);
      root.remove(); slot.chip?.remove(); drawStrip();
    };
    return slot;
  }
  win.addEventListener?.('blur', () => active?.finish({}));
  return { add, isRail, close() { if (!isRail()) for (const s of slots) minimize(s, true); } };
}
