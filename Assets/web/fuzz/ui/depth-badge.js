/* ui/depth-badge.js - the Depth badge at the top right (brief handoff-1121/TC-ASCENSION-BRIEF.md, lane B).
 * A small round icon in #game-controls carrying the Depth number. Hover, focus or tap opens a speech bubble: Depth,
 * Stardust and a scrollable grid of bonus icons, each with its own tooltip. Every number and row comes from
 * sim/ascension.js ascensionView(life); nothing here re-derives a bonus. Hidden at Depth 0 with nothing owned or lit.
 * Escape closes the bubble and is never swallowed (the game's pause rule stands). Gentle / reduced motion: fades only. */

import { ascensionView } from '../sim/ascension.js';
import { PRESTIGE_CURRENCY } from '../data/prestige.js';
import { fmt } from '../sim/format.js';

/** Every word the badge shows, in one place (the owner may rename them). */
export const BADGE_COPY = Object.freeze({
  label: 'Depth {n}. Your Renewal bonuses.',
  header: 'Depth',
  dust: '{d} ' + PRESTIGE_CURRENCY,
  empty: 'Nothing lit yet. Buy stars in the Renewal Sky.',
  nextGroup: 'From next run',
  nextMark: 'next run',
  nextTip: 'From next run',
});

export const CLOSE_DELAY_MS = 320;
export const LONG_PRESS_MS = 420;
const CLIP = (name) => `assets/clipart/${name}.webp`;

/** What the badge shows for a life. Pure: the DOM half only paints this. */
export function depthBadgeState(life) {
  const v = ascensionView(life);
  const visible = v.depth > 0 || v.bonuses.length > 0 || v.next.length > 0;
  const n = String(v.depth);
  return {
    visible,
    depth: v.depth,
    numeral: n,
    label: BADGE_COPY.label.replace('{n}', n),
    dust: BADGE_COPY.dust.replace('{d}', fmt(v.dust)),
    bonuses: v.bonuses,
    next: v.next,
    empty: v.bonuses.length === 0 && v.next.length === 0,
  };
}

/** The tooltip lines for one bonus row (name, line, plain, and the next-run mark when it is one). Pure. */
export function tipLines(b) {
  const out = [{ k: 'name', t: b.name }];
  if (b.line) out.push({ k: 'line', t: b.line });
  if (b.plain && b.plain !== b.line) out.push({ k: 'plain', t: b.plain });
  if (b.next) out.push({ k: 'next', t: BADGE_COPY.nextTip });
  return out;
}

/** Mounts the badge before the audio controls in #game-controls. `life` is a getter (the save can be swapped).
 * Answers refresh(); it also refreshes on the document event `tc:prestige-changed` and on every open. */
export function mountDepthBadge({ doc = document, win = doc.defaultView, life = () => null } = {}) {
  const controls = doc.getElementById('game-controls');
  if (!controls || doc.getElementById('depth-badge')) return () => {};
  const wrap = doc.createElement('div');
  wrap.id = 'depth-badge';
  wrap.hidden = true;
  const badge = doc.createElement('button');
  badge.type = 'button';
  badge.className = 'db-icon';
  badge.setAttribute('aria-expanded', 'false');
  badge.setAttribute('aria-controls', 'depth-bubble');
  const num = doc.createElement('b'); num.setAttribute('aria-hidden', 'true');
  badge.append(num);

  const bubble = doc.createElement('section');
  bubble.id = 'depth-bubble';
  bubble.className = 'db-bubble';
  bubble.hidden = true;
  const head = doc.createElement('header'); head.className = 'db-head';
  const title = doc.createElement('p'); title.className = 'db-title';
  const tWord = doc.createElement('span'); tWord.textContent = BADGE_COPY.header;
  const tNum = doc.createElement('b');
  title.append(tWord, tNum);
  const dust = doc.createElement('p'); dust.className = 'db-dust';
  head.append(title, dust);
  const scroll = doc.createElement('div'); scroll.className = 'db-scroll';
  const tip = doc.createElement('div'); tip.className = 'db-tip'; tip.id = 'depth-tip'; tip.setAttribute('role', 'tooltip'); tip.hidden = true;
  bubble.append(head, scroll, tip);
  wrap.append(badge, bubble);
  const audio = doc.getElementById('audio-controls');
  if (audio && audio.parentNode === controls) controls.insertBefore(wrap, audio); else controls.prepend(wrap);

  let open = false, pinned = false, closeT = 0, fadeT = 0, pressT = 0, tipFor = null;
  const cancelClose = () => { win.clearTimeout(closeT); closeT = 0; };

  function place() {
    const r = badge.getBoundingClientRect();
    const vw = win.innerWidth || doc.documentElement.clientWidth || 360;
    const w = bubble.offsetWidth || 280;
    const cx = r.left + r.width / 2;
    const left = Math.max(8, Math.min(vw - w - 8, cx - w * 0.72));
    bubble.style.left = Math.round(left) + 'px';
    bubble.style.top = Math.round(r.bottom + 11) + 'px';
    bubble.style.setProperty('--db-tail', Math.round(Math.max(18, Math.min(w - 18, cx - left))) + 'px');
  }

  function hideTip() { tip.hidden = true; tipFor?.removeAttribute('aria-describedby'); tipFor = null; }
  function showTip(cell, b) {
    tip.replaceChildren(...tipLines(b).map(({ k, t }) => { const p = doc.createElement('p'); p.className = 'db-t-' + k; p.textContent = t; return p; }));
    tip.style.setProperty('--db-hue', b.hue || '#888');
    tip.hidden = false;
    tipFor?.removeAttribute('aria-describedby');
    tipFor = cell; cell.setAttribute('aria-describedby', 'depth-tip');
    const br = bubble.getBoundingClientRect(), cr = cell.getBoundingClientRect();
    const tw = tip.offsetWidth || 200, th = tip.offsetHeight || 60;
    let x = cr.left + cr.width / 2 - br.left - tw / 2;
    x = Math.max(6, Math.min(br.width - tw - 6, x));
    let y = cr.bottom - br.top + 6;
    if (y + th > br.height - 4 && cr.top - br.top - th - 6 > 0) y = cr.top - br.top - th - 6;
    tip.style.left = Math.round(x) + 'px';
    tip.style.top = Math.round(y) + 'px';
  }

  function cell(b) {
    const c = doc.createElement('button');
    c.type = 'button';
    c.className = 'db-cell' + (b.next ? ' db-next' : '');
    c.dataset.id = b.id;
    c.style.setProperty('--db-hue', b.hue || '#888');
    c.setAttribute('aria-label', tipLines(b).map((l) => l.t).join('. '));
    const img = doc.createElement('img'); img.src = CLIP(b.icon); img.alt = ''; img.width = 30; img.height = 30; img.draggable = false;
    c.append(img);
    if (b.next) { const m = doc.createElement('i'); m.textContent = BADGE_COPY.nextMark; m.setAttribute('aria-hidden', 'true'); c.append(m); }
    c.addEventListener('pointerenter', (ev) => { if (ev.pointerType === 'mouse') showTip(c, b); });
    c.addEventListener('pointerleave', (ev) => { if (ev.pointerType === 'mouse' && tipFor === c) hideTip(); });
    c.addEventListener('focus', () => showTip(c, b));
    c.addEventListener('blur', () => { if (tipFor === c) hideTip(); });
    /* A tap toggles the tip; the focus that comes with the same tap already showed it, so judge by what was up before. */
    let was = null;
    c.addEventListener('pointerdown', (ev) => {
      was = ev.pointerType === 'mouse' ? 'mouse' : (tipFor === c && !tip.hidden);
      if (ev.pointerType === 'mouse') return;
      win.clearTimeout(pressT);
      pressT = win.setTimeout(() => showTip(c, b), LONG_PRESS_MS);
    });
    for (const t of ['pointerup', 'pointercancel']) c.addEventListener(t, () => win.clearTimeout(pressT));
    c.addEventListener('contextmenu', (ev) => ev.preventDefault());
    c.addEventListener('click', () => {
      const up = was === null ? (tipFor === c && !tip.hidden) : was;
      was = null;
      if (up === true) hideTip(); else showTip(c, b);
    });
    return c;
  }

  function paint(s) {
    num.textContent = s.numeral;
    wrap.dataset.digits = String(s.numeral.length);
    badge.title = s.label;
    badge.setAttribute('aria-label', s.label);
    tNum.textContent = ' ' + s.numeral;
    dust.textContent = s.dust;
    hideTip();
    const parts = [];
    if (s.empty) { const p = doc.createElement('p'); p.className = 'db-empty'; p.textContent = BADGE_COPY.empty; parts.push(p); }
    if (s.bonuses.length) { const g = doc.createElement('div'); g.className = 'db-grid'; g.append(...s.bonuses.map(cell)); parts.push(g); }
    if (s.next.length) {
      const h = doc.createElement('p'); h.className = 'db-group'; h.textContent = BADGE_COPY.nextGroup;
      const g = doc.createElement('div'); g.className = 'db-grid'; g.append(...s.next.map(cell));
      parts.push(h, g);
    }
    scroll.replaceChildren(...parts);
  }

  function refresh() {
    let s;
    try { s = depthBadgeState(life()); } catch { s = depthBadgeState(null); }
    wrap.hidden = !s.visible;
    if (!s.visible) { setOpen(false, true); return; }
    paint(s);
    if (open) place();
  }

  function setOpen(want, now = false) {
    cancelClose();
    if (want === open) return;
    open = want;
    badge.setAttribute('aria-expanded', want ? 'true' : 'false');
    win.clearTimeout(fadeT);
    if (want) {
      refresh();
      if (wrap.hidden) { open = false; badge.setAttribute('aria-expanded', 'false'); return; }
      bubble.classList.remove('db-out');
      bubble.hidden = false;
      place();
      return;
    }
    pinned = false;
    hideTip();
    if (now || bubble.hidden) { bubble.hidden = true; bubble.classList.remove('db-out'); return; }
    bubble.classList.add('db-out');
    fadeT = win.setTimeout(() => { bubble.hidden = true; bubble.classList.remove('db-out'); }, 180);
  }
  const closeSoon = () => { if (pinned) return; cancelClose(); closeT = win.setTimeout(() => setOpen(false), CLOSE_DELAY_MS); };

  badge.addEventListener('pointerenter', (ev) => { if (ev.pointerType === 'mouse') setOpen(true); });
  wrap.addEventListener('pointerenter', (ev) => { if (ev.pointerType === 'mouse' && open) cancelClose(); });
  wrap.addEventListener('pointerleave', (ev) => { if (ev.pointerType === 'mouse') closeSoon(); });
  badge.addEventListener('click', () => {
    if (open && pinned) { setOpen(false); return; }
    setOpen(true); pinned = open;
  });
  wrap.addEventListener('focusin', () => { cancelClose(); setOpen(true); });
  wrap.addEventListener('focusout', (ev) => { if (!wrap.contains(ev.relatedTarget)) closeSoon(); });
  doc.addEventListener('pointerdown', (ev) => { if (open && !wrap.contains(ev.target)) setOpen(false); }, true);
  /* Escape closes the bubble and keeps going: no preventDefault, no stopPropagation, so the game still pauses. */
  doc.addEventListener('keydown', (ev) => { if (ev.key === 'Escape' && open) setOpen(false, true); }, true);
  doc.addEventListener('tc:prestige-changed', refresh);
  win.addEventListener('resize', () => { if (open) place(); });
  scroll.addEventListener('scroll', hideTip, { passive: true });

  refresh();
  return refresh;
}
