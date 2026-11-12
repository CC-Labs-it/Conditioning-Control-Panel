/* ui/dock-volume.js - hovering a bottom tab shows that feature's own volume above it (owner 2026-11-08).
 * The slider is the same per-source level as the HUD mixer row (audio/mixer.js), so the two move together and the
 * mixer's save keeps it. Which sources a tab owns comes from what its unlock actually played through ctx
 * (ui/context.js soundOptions -> noteSound); a source the mixer has not heard yet shows no slider, like the HUD.
 * Shrunk icon pills also get the feature's icon and name, with or without sound. Mouse and pen only: a tap stays
 * the toggle and a long press stays the reorder drag. Gentle visuals and reduced motion: a plain fade. */

export const LINGER_MS = 1000;
export const OPEN_DELAY_MS = 120;

/** owner item id -> source ids it played, in first-played order. */
export const soundOwners = new Map();
export function noteSound(owner, source) {
  if (typeof owner !== 'string' || !owner || typeof source !== 'string' || !source || source.length > 128) return;
  let set = soundOwners.get(owner);
  if (!set) soundOwners.set(owner, (set = new Set()));
  set.add(source);
}

/** The live mixer rows a tab owns: noted sources first, then a source named after the owner itself. Max three. */
export function ownerSources(owner, live, owners = soundOwners) {
  if (!owner || !Array.isArray(live)) return [];
  const ids = [...(owners.get(owner) || []), owner];
  const byId = new Map(live.map(s => [s.id, s]));
  const out = [];
  for (const id of new Set(ids)) if (byId.has(id)) out.push(byId.get(id));
  return out.slice(0, 3);
}

/**
 * The open/close timing, pure. `show(id)` / `hide()` are effects; timers are injected.
 * Normal mode: leaving the tab or the card lingers LINGER_MS. Shrunk mode: also closes at once when the pointer
 * reaches anything else. Another tab always switches at once. Dragging the slider never closes.
 */
export function createHoverCard({ show, hide, setTimer = setTimeout, clearTimer = clearTimeout, linger = LINGER_MS, delay = OPEN_DELAY_MS }) {
  let open = null, hovered = null, onCard = false, dragging = false, pressed = false, timer = null;
  const stop = () => { if (timer != null) clearTimer(timer); timer = null; };
  const doShow = id => { stop(); open = id; show(id); };
  const doHide = () => { stop(); if (open == null) return; open = null; onCard = false; hide(); };
  const schedule = () => { stop(); if (open != null && !dragging && !onCard) timer = setTimer(() => { timer = null; if (!dragging && !onCard && hovered == null) doHide(); }, linger); };
  return {
    get open() { return open; },
    get dragging() { return dragging; },
    enterTab(id) {
      hovered = id;
      if (pressed) return;
      if (open != null) { if (open !== id) doShow(id); else stop(); return; }
      stop(); timer = setTimer(() => { timer = null; if (hovered === id && !pressed) doShow(id); }, delay);
    },
    leaveTab(id) {
      if (hovered === id) hovered = null;
      if (open == null) { stop(); return; }
      if (open === id) schedule();
    },
    enterCard() { onCard = true; stop(); },
    leaveCard() { onCard = false; schedule(); },
    /** The pointer reached something that is neither a tab nor the card. */
    elsewhere(shrunk) { if (open != null && hovered == null && !onCard && !dragging && shrunk) doHide(); },
    dragStart() { dragging = true; stop(); },
    dragEnd() { dragging = false; if (!onCard && hovered !== open) schedule(); },
    /** A press on a tab (toggle or reorder drag) hides the card until release. */
    press() { pressed = true; doHide(); },
    release() { if (!pressed) return; pressed = false; if (hovered != null) doShow(hovered); },
    close: doHide,
  };
}

/** Left/bottom for a card of size w x h above a tab rect, kept on screen and clear of the top-right controls. */
export function placeCard(tab, { w, h }, { W, H, avoid = null, margin = 8 }) {
  const left = Math.round(Math.max(margin, Math.min(tab.left + tab.width / 2 - w / 2, W - w - margin)));
  let bottom = Math.round(Math.max(0, H - tab.top));
  const top = H - bottom - h;
  if (avoid && left < avoid.right && left + w > avoid.left && top < avoid.bottom + 4) bottom = Math.max(0, H - (avoid.bottom + 4) - h);
  return { left, bottom };
}

const speaker = muted => `<svg viewBox="0 0 24 24" width="14" height="14" aria-hidden="true"><path d="M3 9h4l5-4v14l-5-4H3Z" fill="currentColor"/><path d="${muted ? 'm16 9 6 6m0-6-6 6' : 'M16 8q5 4 0 8M19 5q8 7 0 14'}" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>`;

const CSS = `
#dock-vol { position: fixed; z-index: 88; width: 172px; padding: 0 0 6px; pointer-events: auto; opacity: 0; transform: translateY(4px); transition: opacity .14s ease-out, transform .14s ease-out; }
#dock-vol[hidden] { display: none; }
#dock-vol.on { opacity: 1; transform: none; }
#dock-vol.fading { transition-duration: .22s; }
body.gentle #dock-vol { transform: none; }
@media (prefers-reduced-motion: reduce) { #dock-vol { transform: none; } }
#dock-vol .dv-card { padding: 6px 8px; background: var(--panel); color: var(--ink); border: 1px solid var(--line); box-shadow: 2px 3px 0 #0003; font: 13px/1.2 var(--font); }
#dock-vol .dv-head { display: flex; align-items: center; gap: 6px; margin: 0 0 2px; font-weight: 700; overflow: hidden; }
#dock-vol .dv-head[hidden] { display: none; }
#dock-vol .dv-head .dv-i { flex: none; display: grid; place-items: center; width: 24px; height: 24px; }
#dock-vol .dv-head .dv-i > * { max-width: 24px; max-height: 24px; }
#dock-vol .dv-head .dv-name { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
#dock-vol .dv-row { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; align-items: center; gap: 2px 6px; padding: 3px 0 0; }
#dock-vol .dv-src { grid-column: 1 / -1; font-size: 11px; color: var(--ink-soft); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
#dock-vol .dv-src[hidden] { display: none; }
#dock-vol .dv-mute { display: grid; place-items: center; width: 26px; height: 24px; padding: 0; background: var(--btn, #d4d0c8); color: var(--ink); border: 1px solid var(--line); cursor: pointer; }
#dock-vol .dv-mute[aria-pressed=true] { background: var(--fill, #c1bdb6); }
#dock-vol .dv-slider { width: 100%; min-width: 0; margin: 0; accent-color: var(--niche); touch-action: none; }
#dock-vol .dv-val { min-width: 3.2em; text-align: right; font-size: 11px; color: var(--ink-soft); }
`;

/** Wires the hover card onto the dock strip. `mixer` is audio.mixer; returns { close, destroy }. */
export function bindDockVolume({ doc, strip, mixer, avoid = () => null, owners = soundOwners }) {
  const win = doc.defaultView;
  if (!strip || !win) return { close() {}, destroy() {} };
  if (!doc.getElementById('dock-vol-css')) {
    const st = doc.createElement('style'); st.id = 'dock-vol-css'; st.textContent = CSS; doc.head.appendChild(st);
  }
  const host = doc.createElement('div'); host.id = 'dock-vol'; host.hidden = true; host.setAttribute('role', 'group');
  const card = doc.createElement('div'); card.className = 'dv-card';
  const head = doc.createElement('div'); head.className = 'dv-head';
  const icon = doc.createElement('span'); icon.className = 'dv-i';
  const name = doc.createElement('span'); name.className = 'dv-name';
  head.append(icon, name);
  const rowsEl = doc.createElement('div');
  card.append(head, rowsEl); host.append(card); doc.body.appendChild(host);

  const chips = () => [...strip.querySelectorAll('.dk-chip')];
  const chipOf = id => chips().find(c => c.dataset.fzOwner === id) || null;
  const shrunk = () => strip.dataset.fit === 'icon';
  const hoverable = ev => ev.pointerType === 'mouse' || ev.pointerType === 'pen';
  let current = null, fadeTimer = null, rows = [];

  function sourcesFor(id) { return mixer ? ownerSources(id, mixer.sources(), owners) : []; }
  function paintRows(list) {
    const ids = list.map(s => s.id).join('|');
    if (rows.map(r => r.id).join('|') !== ids) {
      rowsEl.replaceChildren(); rows = [];
      for (const s of list) {
        const row = doc.createElement('div'); row.className = 'dv-row';
        const src = doc.createElement('span'); src.className = 'dv-src';
        const mute = doc.createElement('button'); mute.type = 'button'; mute.className = 'dv-mute';
        const input = doc.createElement('input'); input.type = 'range'; input.min = '0'; input.max = '100'; input.step = '1'; input.className = 'dv-slider';
        const val = doc.createElement('span'); val.className = 'dv-val';
        row.append(src, mute, input, val); rowsEl.append(row);
        mute.addEventListener('click', () => { const r = mixer.sources().find(x => x.id === s.id); mixer.setMuted(s.id, !r?.muted); });
        input.addEventListener('input', () => {
          const v = input.valueAsNumber / 100;
          mixer.setVolume(s.id, v);
          if (v > 0 && mixer.sources().find(x => x.id === s.id)?.muted) mixer.setMuted(s.id, false);
        });
        input.addEventListener('pointerdown', ev => { ev.stopPropagation(); hover.dragStart(); });
        const up = () => { if (hover.dragging) hover.dragEnd(); };
        input.addEventListener('pointerup', up); input.addEventListener('pointercancel', up); input.addEventListener('lostpointercapture', up);
        input.addEventListener('change', up);
        rows.push({ id: s.id, src, mute, input, val });
      }
    }
    for (const [i, s] of list.entries()) {
      const r = rows[i], pct = Math.round(s.volume * 100), off = s.muted || pct === 0;
      r.src.textContent = s.label; r.src.hidden = list.length < 2;
      if (doc.activeElement !== r.input || r.input.valueAsNumber !== pct) r.input.value = String(pct);
      r.input.setAttribute('aria-label', s.label + ' volume');
      r.input.setAttribute('aria-valuetext', s.muted ? 'Muted' : pct + '%');
      r.val.textContent = s.muted ? 'Off' : pct + '%';
      r.mute.innerHTML = speaker(off);
      r.mute.setAttribute('aria-pressed', String(!!s.muted));
      r.mute.setAttribute('aria-label', (s.muted ? 'Unmute ' : 'Mute ') + s.label);
    }
  }
  function place() {
    const chip = current && chipOf(current);
    if (!chip) return;
    const r = chip.getBoundingClientRect(), box = host.getBoundingClientRect();
    const p = placeCard(r, { w: box.width || 172, h: box.height || 60 }, { W: win.innerWidth, H: win.innerHeight, avoid: avoid() });
    host.style.left = p.left + 'px'; host.style.bottom = p.bottom + 'px';
  }
  /** Fill the card for a tab. False when there is nothing to show (normal mode, no sound). */
  function fill(id) {
    const chip = chipOf(id);
    if (!chip) return false;
    const list = sourcesFor(id), small = shrunk();
    if (!list.length && !small) return false;
    head.hidden = !small;
    const label = chip.querySelector('.dk-label')?.textContent || chip.getAttribute('aria-label') || '';
    name.textContent = label;
    host.setAttribute('aria-label', label);
    const badge = chip.querySelector('.dk-i');
    icon.replaceChildren(...(badge ? [...badge.childNodes].map(n => n.cloneNode(true)) : []));
    for (const el of icon.querySelectorAll('[id]')) el.removeAttribute('id');
    paintRows(list);
    return true;
  }
  function showCard(id) {
    win.clearTimeout(fadeTimer);
    current = id;
    if (!fill(id)) { hideCard(true); return; }
    host.hidden = false; host.classList.remove('fading');
    place();
    win.requestAnimationFrame ? win.requestAnimationFrame(() => { if (current === id) host.classList.add('on'); }) : host.classList.add('on');
  }
  function hideCard(now = false) {
    current = null;
    host.classList.add('fading'); host.classList.remove('on');
    win.clearTimeout(fadeTimer);
    if (now) host.hidden = true; else fadeTimer = win.setTimeout(() => { if (!current) host.hidden = true; }, 240);
  }
  const hover = createHoverCard({ show: showCard, hide: () => hideCard(), setTimer: (fn, ms) => win.setTimeout(fn, ms), clearTimer: id => win.clearTimeout(id) });

  const chipFrom = t => t?.closest?.('.dk-chip');
  const onOver = ev => {
    if (!hoverable(ev)) return;
    const chip = chipFrom(ev.target);
    if (chip && strip.contains(chip)) { if (chip.dataset.fzOwner) hover.enterTab(chip.dataset.fzOwner); return; }
    if (host.contains(ev.target) || ev.target === strip) return;
    hover.elsewhere(shrunk());
  };
  const onOut = ev => {
    if (!hoverable(ev)) return;
    const chip = chipFrom(ev.target);
    if (!chip || !strip.contains(chip) || chip.contains(ev.relatedTarget)) return;
    hover.leaveTab(chip.dataset.fzOwner);
  };
  const onDown = ev => { const chip = chipFrom(ev.target); if (chip && strip.contains(chip)) hover.press(); };
  const onUp = () => hover.release();
  const onFocus = ev => { const chip = chipFrom(ev.target); if (chip && strip.contains(chip) && chip.matches(':focus-visible') && chip.dataset.fzOwner) { hover.enterTab(chip.dataset.fzOwner); } };
  const onBlur = ev => { const chip = chipFrom(ev.target); if (chip && strip.contains(chip) && !host.contains(ev.relatedTarget)) hover.leaveTab(chip.dataset.fzOwner); };
  doc.addEventListener('pointerover', onOver, true);
  strip.addEventListener('pointerout', onOut);
  strip.addEventListener('pointerdown', onDown, true);
  win.addEventListener('pointerup', onUp, true);
  strip.addEventListener('focusin', onFocus);
  strip.addEventListener('focusout', onBlur);
  host.addEventListener('pointerenter', ev => { if (hoverable(ev)) hover.enterCard(); });
  host.addEventListener('pointerleave', ev => { if (hoverable(ev)) hover.leaveCard(); });
  const offMixer = mixer?.subscribe?.(() => { if (current) { if (!fill(current)) hover.close(); else place(); } });
  const onResize = () => hover.close();
  win.addEventListener('resize', onResize);
  win.addEventListener('blur', onResize);
  return {
    close: () => hover.close(),
    destroy() {
      hover.close(); offMixer?.();
      doc.removeEventListener('pointerover', onOver, true); win.removeEventListener('pointerup', onUp, true);
      win.removeEventListener('resize', onResize); win.removeEventListener('blur', onResize); host.remove();
    },
  };
}
