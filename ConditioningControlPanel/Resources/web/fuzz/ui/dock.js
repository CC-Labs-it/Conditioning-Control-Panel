/* Movable widget windows. Rails supply their first positions; the player owns the layout after that. */
export const DOCK_MIN_W = 1100;
export const dockIsRail = (w) => w >= DOCK_MIN_W;
export function railWidth(w) {
  return dockIsRail(w) ? Math.round(Math.max(200, Math.min(300, (w - 776) / 2))) : 0;
}
export const emptierEdge = (left, right) => left <= right ? 'left' : 'right';
export const slotOrder = (boughtAt, nth = 0) => (boughtAt < 0 ? 100000 : boughtAt * 8) + Math.min(7, nth);

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
  const width = () => isRail() ? railWidth(win.innerWidth) - 12 : Math.min(360, win.innerWidth - 16);
  const paused = () => doc.getElementById('pause')?.hidden === false;
  function layout(s) { return { edge: s.side, x: s.x, y: s.y, min: s.min, z: s.z }; }
  function save(s) { s.onLayout?.(layout(s)); }
  function moved(s) {
    if (win.CustomEvent) doc.dispatchEvent(new win.CustomEvent('fz-dock-move', { detail: { root: s.root, x: s.x, y: s.y } }));
  }
  function paint(s, notify = false) {
    const p = clampWindow(s, { w: width(), W: win.innerWidth, H: win.innerHeight });
    s.x = p.x; s.y = p.y;
    Object.assign(s.root.style, { left: `${s.x}px`, top: `${s.y}px`, width: `${width()}px`, maxHeight: `${Math.max(44, win.innerHeight - s.y - 8)}px`, zIndex: String(s.z) });
    s.root.classList.toggle('min', s.min);
    s.root.classList.toggle('open', !s.min);
    s.chip?.classList.toggle('on', !s.min);
    s.fold.textContent = s.min ? '+' : '−';
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
    return { x: isRail() ? (s.side === 'left' ? 6 + offset : win.innerWidth - width() - 6 - offset) : (win.innerWidth - width()) / 2,
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
    min, onMin = null, pin = false, keep = false, layout: saved = {}, onLayout = null }) {
    const side = edge === 'left' || edge === 'right' ? edge : emptierEdge(count.left, count.right);
    count[side]++;
    const root = doc.createElement('section'); root.className = `dk${pin ? ' pin' : ''}${door ? ' door' : ''}`;
    root.style.order = String(order);
    const head = doc.createElement('div'); head.className = 'dk-t';
    const ico = doc.createElement('span'); ico.className = 'dk-i'; ico.textContent = icon;
    const name = doc.createElement(door ? 'button' : 'span'); name.className = 'dk-n'; name.textContent = title;
    if (door) { name.type = 'button'; name.addEventListener('click', door); }
    const fold = doc.createElement('button'); fold.type = 'button'; fold.className = 'dk-f';
    head.append(ico, name, fold);
    const body = doc.createElement('div'); body.className = 'dk-b';
    root.append(head, body); rails[side].appendChild(root);
    const restored = Number.isFinite(saved.x) && Number.isFinite(saved.y);
    const z = Number.isFinite(saved.z) && saved.z > 0 ? Math.min(saved.z, 1000000) : topZ + 1;
    topZ = Math.max(topZ, z);
    const slot = { root, body, head, fold, title, side, chip: null, pin, keep, onMin, onLayout, z,
      x: saved.x ?? 8, y: saved.y ?? 48, min: typeof min === 'boolean' ? min : !isRail(), fresh: !restored };
    slots.add(slot);
    if (!restored) Object.assign(slot, firstSpot(slot, [...slots].filter((s) => s !== slot)));
    root.addEventListener('pointerdown', () => { if (!paused()) raise(slot); }, true);
    fold.addEventListener('click', () => { if (!paused()) minimize(slot, !slot.min); });
    head.addEventListener('pointerdown', (ev) => {
      if (paused() || (ev.button != null && ev.button !== 0) || ev.target.closest?.('button,input,select,textarea,a,[role="button"]')) return;
      active = slot; slot.fresh = false;
      slot.drag = { id: ev.pointerId, x: ev.clientX, y: ev.clientY, left: slot.x, top: slot.y };
      root.classList.add('dragging'); head.setPointerCapture?.(ev.pointerId); ev.preventDefault();
    });
    head.addEventListener('pointermove', (ev) => {
      const d = slot.drag;
      if (!d || d.id !== ev.pointerId || paused()) return;
      slot.x = d.left + ev.clientX - d.x; slot.y = d.top + ev.clientY - d.y; paint(slot, true);
    });
    const finish = (ev) => {
      if (!slot.drag || (ev.pointerId != null && ev.pointerId !== slot.drag.id)) return;
      const id = slot.drag.id; slot.drag = null; active = null; root.classList.remove('dragging');
      if (head.hasPointerCapture?.(id)) head.releasePointerCapture(id);
      save(slot); moved(slot);
    };
    for (const event of ['pointerup', 'pointercancel', 'lostpointercapture']) head.addEventListener(event, finish);
    slot.finish = finish;
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
