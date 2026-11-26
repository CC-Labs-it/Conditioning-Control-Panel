/* ui/tips.js - the first-run tips on the page: a small paper note with a crayon arrow at the real target
 * (owner 2026-11-08; the ghost hand that acted out the gesture was removed the same day as uncanny: arrow only, never
 * re-add a hand). The schedule is pure (ui/tips-schedule.js); this file reads the page into
 * facts every quarter second and draws what the schedule answers. Nothing here takes a click: the whole layer is
 * pointer-events none, it never pauses the game, and it fades on its own. One lamp top-left: the note's shadow sits
 * down-right and nothing rotates. Gentle and reduced motion: no bob, a still note that fades. "Seen" is a
 * per-device preference (its own key), so a New run never teaches again what this device has already seen. */

import { brandText } from '../data/brand.js';
import { setRich } from './rich-text.js';
import { TIP_COPY } from '../data/content/tips.js';
import { ALL_TIP_IDS, createTipSchedule } from './tips-schedule.js';
import { canMoveFrom } from './dock.js';

export const TIPS_KEY = 'fuzz.tips.v1';
export const PREVIEW_TIPS_KEY = 'fuzz.preview.tips.v1';
const POLL_MS = 250;
const SVG = 'http://www.w3.org/2000/svg';
const INK = '#2b2230';

export function readTipsSeen(storage, key = TIPS_KEY) {
  try {
    const v = JSON.parse(storage?.getItem(key) || '{}');
    const seen = v && typeof v === 'object' && v.seen && typeof v.seen === 'object' ? v.seen : {};
    return Object.fromEntries(ALL_TIP_IDS.filter((id) => seen[id]).map((id) => [id, 1]));
  } catch { return {}; }
}
export function writeTipsSeen(storage, seen, key = TIPS_KEY) {
  try { storage?.setItem(key, JSON.stringify({ seen })); } catch { /* storage may be unavailable */ }
}

const CSS = `
#tc-tips { position: fixed; inset: 0; z-index: 92; pointer-events: none; overflow: hidden; }
#tc-tips * { pointer-events: none; }
.tip-group { position: absolute; inset: 0; opacity: 0; }
.tip-note { position: absolute; max-width: 220px; padding: 9px 12px 9px; background: #fffbe9; color: ${INK};
  font: 600 15px/1.25 "Segoe Print", "Comic Sans MS", "Chalkboard SE", cursive; border: 2px solid ${INK};
  border-radius: 3px 6px 4px 7px; box-shadow: 3px 4px 0 rgba(43, 34, 48, .28); }
.tip-note::before { content: ''; position: absolute; left: 14px; right: 14px; top: -6px; height: 9px; border: 1.5px solid ${INK};
  border-radius: 2px; background: color-mix(in srgb, var(--accent, #ff6fb5) 70%, #fff); opacity: .95; }
.tip-note b { font-weight: 700; }
.tip-arrow { position: absolute; inset: 0; width: 100%; height: 100%; overflow: visible; }
.dk.tip-grip > .dk-resize { opacity: 1; pointer-events: auto; transform: none; }
`;

const rectOf = (el) => {
  const r = el?.getBoundingClientRect?.();
  return r && r.width > 0 && r.height > 0 ? r : null;
};
const hits = (a, b, pad = 0) => a.left < b.right + pad && a.right > b.left - pad && a.top < b.bottom + pad && a.bottom > b.top - pad;

export function createTips({ doc = document, engine, ui, audio, storage, key = TIPS_KEY, reset = false }) {
  const win = doc.defaultView;
  if (reset) { try { storage?.removeItem?.(key); } catch { /* fine */ } }
  const schedule = createTipSchedule({ seen: readTipsSeen(storage, key) });
  const still = () => !!(ui?.gentle?.() || ui?.reduced?.() || doc.body.classList.contains('gentle'));
  const touch = () => !!win.matchMedia?.('(pointer: coarse)').matches;

  if (!doc.getElementById('tc-tips-css')) {
    const st = doc.createElement('style');
    st.id = 'tc-tips-css';
    st.textContent = CSS;
    doc.head.appendChild(st);
  }
  const host = doc.createElement('div');
  host.id = 'tc-tips';
  host.setAttribute('aria-live', 'polite');
  doc.body.appendChild(host);

  /* ---- the facts ---- */
  let fpsvAt = engine.run.owned?.fpsv ? -Infinity : null;
  const done = new Set();
  const offs = [
    engine.subscribe('buy', (e) => { if (e?.item?.id === 'fpsv' && fpsvAt == null) fpsvAt = engine.run.clock; }),
    engine.subscribe('fork', () => done.add('choices')),
    engine.subscribe('pause', () => done.add('wake')),
    engine.subscribe('tips-replay', () => { const a = schedule.replay(win.performance.now()); if (a?.hide) drop(); }),
  ];
  const onDown = (e) => {
    const t = e.target;
    if (!t?.closest) return;
    if (t.closest('.dk-resize')) { done.add('resize'); return; }
    const obj = t.closest('.dk:not(.door)');
    if (obj && canMoveFrom(t, obj)) done.add('move');
    if (t.closest('#audio-controls')) done.add('sound');
    const chip = t.closest('.dk-chip');
    if (chip && chip === guideChip()) done.add('map');
  };
  doc.addEventListener('pointerdown', onDown, true);
  /* A touch that dragged a casing without holding first (ui/dock.js fz-dock-hold-miss): the 'keep' tip points at it. */
  let missed = null;
  const onMiss = (e) => { if (e?.detail?.root) missed = e.detail.root; };
  doc.addEventListener('fz-dock-hold-miss', onMiss);

  const objects = () => [...doc.querySelectorAll('#l-side .dk:not(.door):not(.min)')].filter((el) => {
    const r = rectOf(el);
    return r && r.right > 0 && r.bottom > 0 && r.left < win.innerWidth && r.top < win.innerHeight;
  });
  function guideChip() {
    for (const c of doc.querySelectorAll('#dock-strip .dk-chip')) {
      if (c.querySelector('.dk-label')?.textContent?.trim() === 'Guide') return c;
    }
    return null;
  }
  const forkCard = () => doc.querySelector('#cards .backdrop .card.fork');
  function blockers() {
    const veil = !!doc.querySelector('body > .tc');
    const pause = doc.getElementById('pause');
    const guideOpen = !!doc.querySelector('#cards .gr-back:not([hidden])');
    const game = engine.paused || engine.suspended || engine.stopped || !!engine.run.ended || doc.hidden;
    const backs = [...doc.querySelectorAll('#cards .backdrop')];
    const otherCard = backs.some((b) => !b.querySelector('.card.fork'));
    const hard = veil || (pause && !pause.hidden) || guideOpen || game || otherCard;
    return { hard, any: hard || backs.length > 0 };
  }
  const overlapping = (list) => {
    const rs = list.map(rectOf).filter(Boolean);
    for (let i = 0; i < rs.length; i++) for (let j = i + 1; j < rs.length; j++) if (hits(rs[i], rs[j], -6)) return true;
    return false;
  };

  /* ---- targets: what each tip points at, or null ---- */
  function target(id) {
    const objs = objects();
    switch (id) {
      case 'move': {
        const el = objs[objs.length - 1], r = rectOf(el);
        return r ? { el, rect: r, at: { x: r.left + r.width / 2, y: r.top + Math.min(10, r.height / 3) }, gesture: 'drag', dx: 56, dy: 26 } : null;
      }
      case 'resize': {
        const el = objs[objs.length - 1];
        if (!el?.querySelector(':scope > .dk-resize')) return null;
        el.classList.add('tip-grip');
        const g = rectOf(el.querySelector(':scope > .dk-resize')), r = rectOf(el);
        return g && r ? { el, rect: r, at: { x: g.left + g.width / 2, y: g.top + g.height / 2 }, gesture: 'drag', dx: -34, dy: -30, grip: el } : null;
      }
      case 'sound': {
        const m = rectOf(doc.getElementById('audio-mute')), a = rectOf(doc.getElementById('audio-toggle'));
        if (!m) return null;
        const rect = rectOf(doc.getElementById('audio-controls')) || m;
        return { rect, at: { x: m.left + m.width / 2, y: m.bottom - 6 }, extra: a ? { x: a.left + a.width / 2, y: a.bottom - 4 } : null, gesture: 'tap' };
      }
      case 'choices': {
        const card = rectOf(forkCard());
        return card ? { rect: card, at: null, avoid: card, gesture: 'none' } : null;
      }
      case 'active': {
        const r = rectOf(doc.getElementById('btn'));
        return r ? { rect: r, at: { x: r.left + r.width / 2, y: r.top + r.height / 2 }, gesture: 'tap' } : null;
      }
      case 'map': {
        const r = rectOf(guideChip());
        return r ? { rect: r, at: { x: r.left + r.width / 2, y: r.top + 6 }, gesture: 'tap' } : null;
      }
      case 'keep': {
        const el = missed?.isConnected && !missed.classList?.contains('min') ? missed : null, r = rectOf(el);
        return r ? { el, rect: r, at: { x: r.left + r.width / 2, y: r.top + Math.min(10, r.height / 3) }, gesture: 'hold' } : null;
      }
      case 'wake': {
        const r = rectOf(doc.getElementById('wake'));
        return r ? { rect: r, at: { x: r.left + r.width / 2, y: r.bottom - 6 }, gesture: 'tap' } : null;
      }
      default: return null;
    }
  }

  /* ---- drawing ---- */
  let shown = null;   // { id, group, note, arrow, anim, t }
  function place(note, t) {
    const W = win.innerWidth, H = win.innerHeight, M = 12, GAP = 34;
    const w = note.offsetWidth, h = note.offsetHeight;
    if (!t) return { x: (W - w) / 2, y: Math.max(64, H * 0.22) };
    const r = t.rect;
    const avoid = [r, rectOf(doc.getElementById('game-controls'))].filter(Boolean);
    const cx = (r.left + r.right) / 2 - w / 2, cy = (r.top + r.bottom) / 2 - h / 2;
    const tries = [
      { x: cx, y: r.bottom + GAP }, { x: r.left - GAP - w, y: cy }, { x: r.right + GAP, y: cy }, { x: cx, y: r.top - GAP - h },
      { x: r.right - w, y: r.bottom + GAP }, { x: r.left, y: r.bottom + GAP }, { x: r.left - GAP - w, y: r.bottom + GAP * 0.6 },
    ];
    const clampX = (x) => Math.min(Math.max(M, x), W - w - M), clampY = (y) => Math.min(Math.max(M, y), H - h - M);
    for (const p of tries) {
      const box = { left: p.x, top: p.y, right: p.x + w, bottom: p.y + h };
      if (box.left < M || box.top < M || box.right > W - M || box.bottom > H - M) continue;
      if (avoid.some((a) => hits(box, a, 6))) continue;
      return p;
    }
    /* No clean spot (a phone with a full-width card): the roomiest side of the target, kept on screen. */
    const room = [['below', H - r.bottom], ['above', r.top], ['left', r.left], ['right', W - r.right]].sort((a, b) => b[1] - a[1])[0][0];
    if (room === 'below') return { x: clampX(cx), y: clampY(r.bottom + 10) };
    if (room === 'above') return { x: clampX(cx), y: clampY(r.top - 10 - h) };
    if (room === 'left') return { x: clampX(r.left - 10 - w), y: clampY(cy) };
    return { x: clampX(r.right + 10), y: clampY(cy) };
  }
  function arrowPath(from, to, bend) {
    const dx = to.x - from.x, dy = to.y - from.y, len = Math.hypot(dx, dy) || 1;
    const ux = dx / len, uy = dy / len, end = { x: to.x - ux * 8, y: to.y - uy * 8 };
    const mx = (from.x + end.x) / 2 - uy * len * bend, my = (from.y + end.y) / 2 + ux * len * bend;
    const ax = end.x - mx, ay = end.y - my, al = Math.hypot(ax, ay) || 1, hx = ax / al, hy = ay / al, s = 11;
    const head = `M${end.x - hx * s - hy * s * 0.6} ${end.y - hy * s + hx * s * 0.6}L${end.x} ${end.y}L${end.x - hx * s + hy * s * 0.6} ${end.y - hy * s - hx * s * 0.6}`;
    return `M${from.x} ${from.y}Q${mx} ${my} ${end.x} ${end.y}${head}`;
  }
  function edgePoint(box, to) {
    const cx = (box.left + box.right) / 2, cy = (box.top + box.bottom) / 2;
    const dx = to.x - cx, dy = to.y - cy;
    const sx = dx ? (box.right - box.left) / 2 / Math.abs(dx) : Infinity, sy = dy ? (box.bottom - box.top) / 2 / Math.abs(dy) : Infinity;
    const k = Math.min(sx, sy);
    return { x: cx + dx * k, y: cy + dy * k };
  }
  function choicePoint(note, card) {
    const n = rectOf(note);
    if (!n) return { x: card.left, y: card.top + 30 };
    const nx = (n.left + n.right) / 2, ny = (n.top + n.bottom) / 2;
    return { x: Math.min(Math.max(nx, card.left + 20), card.right - 20), y: Math.min(Math.max(ny, card.top + 20), card.bottom - 20) };
  }
  function draw(id, t) {
    const s = shown;
    const p = place(s.note, t);
    s.note.style.left = `${Math.round(p.x)}px`;
    s.note.style.top = `${Math.round(p.y)}px`;
    s.arrow.replaceChildren();
    if (!t) return;
    const box = { left: p.x, top: p.y, right: p.x + s.note.offsetWidth, bottom: p.y + s.note.offsetHeight };
    const points = [t.at || choicePoint(s.note, t.rect), ...(t.extra ? [t.extra] : [])];
    points.forEach((to, i) => {
      const d = arrowPath(edgePoint(box, to), to, i ? -0.14 : 0.14);
      for (const [stroke, width] of [[INK, 6], [`color-mix(in srgb, var(--accent, #ff6fb5) 80%, ${INK})`, 3]]) {
        const path = doc.createElementNS(SVG, 'path');
        path.setAttribute('d', d);
        path.setAttribute('fill', 'none');
        path.style.stroke = stroke;   // a style, not the attribute: presentation attributes refuse var() and color-mix()
        path.setAttribute('stroke-width', String(width));
        path.setAttribute('stroke-linecap', 'round');
        path.setAttribute('stroke-linejoin', 'round');
        if (width === 3) path.setAttribute('stroke-dasharray', '10 2 14 1.5');   // crayon skips
        s.arrow.appendChild(path);
      }
    });
  }
  function show(id, hasTarget) {
    drop(true);
    const t = hasTarget ? target(id) : null;
    const group = doc.createElement('div');
    group.className = 'tip-group';
    group.dataset.tip = id;
    const arrow = doc.createElementNS(SVG, 'svg');
    arrow.setAttribute('class', 'tip-arrow');
    arrow.setAttribute('aria-hidden', 'true');
    const note = doc.createElement('div');
    note.className = 'tip-note';
    note.setAttribute('role', 'note');
    setRich(note, id === 'move' && touch() ? TIP_COPY.moveTouch : TIP_COPY[id]);
    group.append(arrow, note);
    host.appendChild(group);
    shown = { id, group, note, arrow, grip: t?.grip || null, hasTarget };
    draw(id, t);
    group.animate?.([{ opacity: 0 }, { opacity: 1 }], { duration: still() ? 320 : 260, easing: 'ease-out', fill: 'forwards' });
    if (!group.animate) group.style.opacity = '1';
    if (!still()) note.animate?.([{ transform: 'translateY(6px)' }, { transform: 'none' }], { duration: 300, easing: 'cubic-bezier(.3,1.5,.5,1)' });
  }
  function drop(now = false) {
    const s = shown;
    if (!s) return;
    shown = null;
    s.grip?.classList.remove('tip-grip');
    for (const el of doc.querySelectorAll('.dk.tip-grip')) el.classList.remove('tip-grip');
    if (now || !s.group.animate) { s.group.remove(); return; }
    const out = s.group.animate([{ opacity: getComputedStyle(s.group).opacity }, { opacity: 0 }], { duration: still() ? 400 : 420, easing: 'ease-in', fill: 'forwards' });
    out.finished.then(() => s.group.remove(), () => s.group.remove());
  }

  /* ---- the loop ---- */
  function facts() {
    const b = blockers(), objs = objects();
    const has = {};
    for (const id of ALL_TIP_IDS) {
      if (id === 'resize') {   // read without touching the grip class
        const el = objs[objs.length - 1];
        has.resize = !!el?.querySelector(':scope > .dk-resize');
      } else has[id] = !!target(id);
    }
    const out = {
      blocked: b.any, hardBlocked: b.hard, fork: !!forkCard(), objects: objs.length, overlap: overlapping(objs),
      sounds: audio?.mixer?.sources?.().length || 0, clock: engine.run.clock || 0,
      fpsvAt: fpsvAt === -Infinity ? -1e12 : fpsvAt, guide: !!engine.run.owned?.guide, has, done: [...done], holdMiss: !!has.keep,
    };
    done.clear();
    return out;
  }
  const save = () => writeTipsSeen(storage, schedule.seen, key);
  function poll() {
    let a;
    try { a = schedule.tick(win.performance.now(), facts()); } catch (e) { console.error('tips', e); return; }
    if (a?.show) show(a.show, a.target);
    else if (a?.hide) { drop(); save(); }
    else if (shown) {
      const t = shown.hasTarget ? target(shown.id) : null;
      if (!shown.hasTarget || t) draw(shown.id, t);
    }
  }
  const timer = win.setInterval(poll, POLL_MS);
  const onResize = () => { if (shown) draw(shown.id, shown.hasTarget ? target(shown.id) : null); };
  win.addEventListener('resize', onResize);

  return {
    schedule,
    replay() { const a = schedule.replay(win.performance.now()); if (a?.hide) drop(); },
    get shown() { return shown?.id || null; },
    off() {
      win.clearInterval(timer);
      win.removeEventListener('resize', onResize);
      doc.removeEventListener('pointerdown', onDown, true);
      doc.removeEventListener('fz-dock-hold-miss', onMiss);
      for (const o of offs) o?.();
      drop(true);
      host.remove();
    },
  };
}
