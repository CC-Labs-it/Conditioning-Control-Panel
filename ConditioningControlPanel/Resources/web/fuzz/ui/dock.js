/* ============================================================================
 * ui/dock.js - the screen's edges (Stimulation Clicker's rule: passive things at the edges, active things in the middle).
 *
 * A wide page has two rails of slots, one down each side, in a stable order. The stage and the shop are padded clear of
 * them in styles.css, so a slot can never sit on the button or the shop. A rail that fills up folds its longest-untouched
 * slots down to their titles, so every slot stays in sight; pressing a title opens it again and folds the next one along.
 * A narrow page has no room for rails: each slot is a small chip in one strip under the shop, in the page flow
 * so the shop just gets a little shorter, and a tap opens that slot as a tray just above the strip. Same element either
 * way, so a widget keeps running and a resize only moves it.
 *
 * Widgets reach this through ctx.dock(), never directly.
 * ==========================================================================*/

/** Same number as the media queries in styles.css. */
export const DOCK_MIN_W = 1100;

export const dockIsRail = (w) => w >= DOCK_MIN_W;

/** How wide a rail is on a page this wide: same sum as --rail-w in styles.css. */
export function railWidth(w) {
  return dockIsRail(w) ? Math.round(Math.max(200, Math.min(300, (w - 776) / 2))) : 0;
}

/** The emptier rail, left on a tie. */
export function emptierEdge(left, right) {
  return left <= right ? 'left' : 'right';
}

/** A slot's place in its rail. Buy order, so a new one lands at the end and nothing already there moves; the same on a reload.
 *  A second slot from one unlock sits right after its first. */
export function slotOrder(boughtAt, nth = 0) {
  return (boughtAt < 0 ? 100000 : boughtAt * 8) + Math.min(7, nth);
}

export function createDock({ doc, rails, strip, onStrip }) {
  const win = doc.defaultView;
  const narrow = win.matchMedia?.(`(max-width:${DOCK_MIN_W - 0.02}px)`);
  const isRail = () => !narrow?.matches;
  const count = { left: 0, right: 0 };
  const slots = new Set();
  const focused = { left: null, right: null };
  let open = null;   // the slot showing as a tray
  let touch = 0, fitQueued = false;

  /** Where the screens standing at the bottom of this rail begin. A screen dropped up the rail does not count. */
  function screensFloor(side) {
    const rail = rails[side].getBoundingClientRect(), H = win.innerHeight;
    const low = [...doc.querySelectorAll('.scr')].filter((e) => e.style.display !== 'none').map((e) => e.getBoundingClientRect())
      .filter((r) => Math.min(r.right, rail.right) - Math.max(r.left, rail.left) > r.width / 2 && r.top + r.height / 2 > H * 0.45);
    return low.length ? Math.min(...low.map((r) => r.top)) : 0;
  }

  /** Fold just enough for this rail to fit, oldest touch first. What the player folded stays folded. It stops above any
   *  screens at its foot; if its titles alone need more than that, it takes what it needs and the screens stand behind. */
  function fitRail(side) {
    const rail = rails[side];
    const mine = [...slots].filter((s) => s.side === side && !s.keep && !s.pin && !s.root.classList.contains('door'));
    for (const s of mine) s.root.classList.remove('auto');
    rail.style.maxHeight = '';
    const floor = screensFloor(side);
    if (floor) rail.style.maxHeight = `${Math.round(floor)}px`;
    if (rail.scrollHeight <= rail.clientHeight) return;
    for (const s of mine.sort((a, b) => a.touch - b.touch)) {
      if (s.root.classList.contains('min') || focused[side] === s) continue;
      s.root.classList.add('auto');
      if (rail.scrollHeight <= rail.clientHeight) return;
    }
    /* A title press wins over fitting above a picture. Keep that toy open and let
     * the rail scroll if its headers and body need more than the viewport. */
    if (floor) rail.style.maxHeight = `${Math.min(win.innerHeight, rail.scrollHeight)}px`;
  }
  function fit() {
    fitQueued = false;
    if (!isRail()) return;
    /* Stopped means stopped: nothing on the page moves behind the pause card. It fits again after. */
    if (doc.getElementById('pause')?.hidden === false) { win.setTimeout?.(queueFit, 500); return; }
    fitRail('left');
    fitRail('right');
  }
  /** Soon, because the widget is still filling its slot. */
  function queueFit() {
    if (fitQueued) return;
    fitQueued = true;
    win.requestAnimationFrame?.(fit);
  }
  win.addEventListener?.('resize', queueFit);
  doc.addEventListener?.('fz-screens', queueFit);

  function setOpen(slot) {
    if (open === slot) return;
    if (open) { open.root.classList.remove('open'); open.chip?.classList.remove('on'); }
    open = slot;
    if (slot) {
      slot.root.classList.add('open');
      slot.chip?.classList.add('on');
      slot.chip?.scrollIntoView?.({ inline: 'nearest', block: 'nearest' });
    }
  }

  function drawStrip() {
    const any = strip.childElementCount > 0;
    if (strip.hidden === any) { strip.hidden = !any; onStrip?.(); }
  }

  /* A press anywhere else puts the tray away. It never eats the press: tapping the game goes on. */
  doc.addEventListener('pointerdown', (ev) => {
    if (!open) return;
    if (open.root.contains(ev.target) || open.chip?.contains(ev.target)) return;
    setOpen(null);
  }, true);

  narrow?.addEventListener?.('change', () => { if (isRail()) setOpen(null); });

  function add({ edge, title = '', icon = '', order = 100000, chip = true, onChip = null, door = null, min = false, onMin = null, pin = false, keep = false }) {
    const side = edge === 'left' || edge === 'right' ? edge : emptierEdge(count.left, count.right);
    count[side] += 1;
    const root = doc.createElement('section');
    root.className = `dk${pin ? ' pin' : ''}${door ? ' door' : min ? ' min' : ''}`;
    root.style.order = String(order);
    const head = doc.createElement('button');
    head.type = 'button';
    head.className = 'dk-t';
    head.setAttribute('aria-expanded', String(!min));
    const ico = doc.createElement('span');
    ico.className = 'dk-i';
    ico.textContent = icon;
    const name = doc.createElement('span');
    name.className = 'dk-n';
    name.textContent = title;
    const fold = doc.createElement('span');
    fold.className = 'dk-f';
    head.append(ico, name, fold);
    const body = doc.createElement('div');
    body.className = 'dk-b';
    root.append(head, body);
    rails[side].appendChild(root);

    const slot = { root, body, side, chip: null, pin, keep, touch: ++touch };
    slots.add(slot);
    head.addEventListener('click', () => {
      if (door) { door(); return; }
      if (!isRail()) { setOpen(null); return; }
      slot.touch = ++touch;
      if (root.classList.contains('auto')) root.classList.remove('min');
      else {
        const shut = root.classList.toggle('min');
        head.setAttribute('aria-expanded', String(!shut));
        onMin?.(shut);
      }
      focused[side] = root.classList.contains('min') ? null : slot;
      fit();
      head.setAttribute('aria-expanded', String(!root.matches('.min,.auto')));
      if (focused[side] === slot) root.scrollIntoView?.({ block: 'nearest', inline: 'nearest' });
    });
    queueFit();
    win.setTimeout?.(queueFit, 500);

    if (chip) {
      const c = doc.createElement('button');
      c.type = 'button';
      c.className = 'dk-chip';
      c.style.order = String(order);
      const i = doc.createElement('span');
      i.className = 'dk-i';
      i.textContent = icon;
      const n = doc.createElement('span');
      n.textContent = title;
      c.append(i, n);
      c.addEventListener('click', () => {
        const press = door || onChip;
        if (press) { setOpen(null); press(); return; }
        setOpen(open === slot ? null : slot);
      });
      strip.appendChild(c);
      slot.chip = c;
      drawStrip();
    } else {
      root.classList.add('nochip');
    }

    slot.remove = () => {
      if (open === slot) setOpen(null);
      if (focused[side] === slot) focused[side] = null;
      count[side] = Math.max(0, count[side] - 1);
      slots.delete(slot);
      root.remove();
      slot.chip?.remove();
      drawStrip();
      queueFit();
    };
    return slot;
  }

  return { add, isRail, close: () => setOpen(null) };
}
