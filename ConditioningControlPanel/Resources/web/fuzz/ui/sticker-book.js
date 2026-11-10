/* ui/sticker-book.js - the Sticker Book (owner 2026-11-10, mockup artifact WL9qAeCn8oLHSPVDcKE1Sd).
 * An album: tabs down the page edge, paper cutout stickers in slots, a card for the one you tap. It reads the book
 * (life.stickers, sim/stickers.js) and this run's meter (the Achievements row's store); it never writes either.
 * Doors: the meter's little book (the event 'sticker-book') and a button on the pause card once the book has anything.
 * It lives in its own layer above the pause card, because the pause makes #cards inert. It closes on a suspend, and
 * on whichever of pause or resume ends the moment it was opened in, so it never sits over a game or a panic. */

import { STICKER_PAGES, STICKERS } from '../data/content/stickers.js';
import { brandText } from '../data/brand.js';

const CSS = `
#sticker-book { position: fixed; inset: 0; z-index: 96; display: grid; place-items: center; padding: 12px; padding-top: 52px; background: rgba(40, 35, 43, .42); pointer-events: auto; }
#sticker-book[hidden] { display: none; }
.sb { --paper: #fffdf7; --paper-2: #f5efe2; --ink: #28232b; --ink-soft: #6b6370; --pink: #f06ea9; --shade: rgba(40, 35, 43, .32);
  position: relative; width: min(1040px, 100%); height: min(760px, 100%); display: flex; flex-direction: column; min-height: 0; color: var(--ink);
  font: 600 15px/1.25 ShopRound, Fredoka, "Trebuchet MS", system-ui, sans-serif; }
.sb button { font: inherit; color: inherit; cursor: pointer; touch-action: manipulation; -webkit-tap-highlight-color: transparent; }
.sb button:focus-visible { outline: 3px solid var(--pink); outline-offset: 2px; }
.sb-head { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 12px; padding: 0 4px 10px; color: #fff; text-shadow: 0 2px 0 rgba(0,0,0,.35); }
.sb-head h2 { margin: 0; font-size: 26px; }
.sb-head .n { font-variant-numeric: tabular-nums; }
.sb-head .sp { flex: 1; }
.sb-seg { color: var(--ink); display: inline-flex; border: 2px solid var(--ink); border-radius: 999px; overflow: hidden; background: var(--paper); text-shadow: none; }
.sb-seg button { border: 0; background: none; padding: 5px 12px; font-size: 14px; }
.sb-seg button[aria-pressed="true"] { background: var(--ink); color: var(--paper); }
.sb-x { order: -1; flex: none; color: var(--ink); width: 40px; height: 40px; border: 2px solid var(--ink); border-radius: 50%; background: var(--paper); font-size: 22px; line-height: 1; text-shadow: none; }
.sb-body { display: grid; grid-template-columns: minmax(0, 1fr) auto 280px; gap: 0 14px; min-height: 0; flex: 1; }
.sb-page { position: relative; min-width: 0; min-height: 0; overflow-y: auto; background: var(--paper); border: 2px solid var(--ink); border-radius: 6px 0 0 6px; box-shadow: 5px 6px 0 var(--shade); padding: 18px 18px 22px 40px; }
.sb-page::before { content: ""; position: absolute; left: 11px; top: 16px; bottom: 16px; width: 12px; background: radial-gradient(circle at 6px 6px, #c9bfae 4px, var(--ink) 5px, transparent 6px) 0 0 / 12px 44px repeat-y; }
.sb-ph { display: flex; flex-wrap: wrap; align-items: baseline; gap: 4px 12px; margin-bottom: 12px; }
.sb-ph h3 { margin: 0; font-size: 24px; }
.sb-ph .n, .sb-ph p { color: var(--ink-soft); font-weight: 500; }
.sb-ph p { flex-basis: 100%; margin: 0; font-size: 14px; }
.sb-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(104px, 1fr)); gap: 14px 8px; }
.sb-grid.big { grid-template-columns: repeat(auto-fill, minmax(132px, 1fr)); }
.sb-tabs { display: flex; flex-direction: column; gap: 3px; padding: 14px 4px 4px 0; margin-left: -16px; min-height: 0; overflow-y: auto; scrollbar-width: none; }
.sb-tab { writing-mode: vertical-rl; transform: rotate(180deg); border: 2px solid var(--ink); border-radius: 8px 0 0 8px; padding: 8px 4px; background: var(--tab); font-size: 12.5px; position: relative; white-space: nowrap; flex: none; }
.sb-tab[aria-selected="true"] { background: var(--paper); padding-right: 9px; margin-left: -3px; }
.sb-tab.foil { background: linear-gradient(160deg, #fff6c9, #f5c64a 30%, #fffbe8 48%, #e7b33a 66%, #fff1b0) 0 0 / 100% 300%; }
.sb-tab.foil:not(.still) { animation: sb-foil 3.2s ease-in-out infinite alternate; }
@keyframes sb-foil { to { background-position: 0 100%; } }
.sb-slot { position: relative; display: grid; justify-items: center; gap: 3px; border: 0; background: none; padding: 4px 2px; text-align: center; }
.sb-hole { width: 84px; height: 84px; display: grid; place-items: center; }
.big .sb-hole { width: 108px; height: 108px; }
.sb-hold { width: 100%; height: 100%; display: grid; place-items: center; filter: drop-shadow(3px 4px 0 var(--shade)); }
.sb-stk { width: 100%; height: 100%; display: grid; place-items: center; transform: rotate(var(--tilt, 0deg)); transition: transform .16s; }
.sb-slot:hover .sb-stk { transform: scale(1.06); }
.sb-stk img { max-width: 86%; max-height: 86%; object-fit: contain; filter: drop-shadow(2.5px 0 0 #fff) drop-shadow(-2.5px 0 0 #fff) drop-shadow(0 2.5px 0 #fff) drop-shadow(0 -2.5px 0 #fff); }
.sb-stk.round img, .sb-stk.ticket img { width: 86%; height: 86%; max-width: none; max-height: none; object-fit: cover; border: 4px solid #fff; filter: none; background: #fff; }
.sb-stk.round img { border-radius: 50%; object-position: 30% 25%; }
.sb-stk.ticket img { border-radius: 8px; }
.sb-nm { font-size: 13px; line-height: 1.15; }
.sb-when { position: absolute; top: 0; right: 2px; font-size: 11px; font-weight: 500; color: var(--ink-soft); transform: rotate(6deg); }
.sb-slot.now .sb-when { color: var(--pink); }
.sb-slot.off .sb-hole { border: 2px dashed #c9bfae; border-radius: 14px; background: var(--paper-2); }
.sb-slot.off .sb-hold { filter: none; }
.sb-slot.off .sb-stk { transform: none; }
.sb-slot.off .sb-stk img { filter: brightness(0); opacity: .13; border-color: transparent; background: transparent; }
.sb-slot.off .sb-nm { color: var(--ink-soft); }
.sb-env { width: 72px; height: 52px; position: relative; background: #efe6d2; border: 2px solid var(--ink); border-radius: 4px; transform: rotate(var(--tilt, 0deg));
  background-image: linear-gradient(to bottom right, transparent 49%, var(--ink) 49.5% 50.5%, transparent 51%), linear-gradient(to bottom left, transparent 49%, var(--ink) 49.5% 50.5%, transparent 51%); background-size: 100% 70%; background-repeat: no-repeat; }
.sb-env img { position: absolute; width: 28px; left: 20px; top: 15px; filter: none; }
.sb.only-run .sb-slot.past { opacity: .3; }
.sb-slot.now .sb-stk::after { content: ""; position: absolute; inset: 8%; border-radius: 16px; pointer-events: none; background: linear-gradient(115deg, transparent 35%, rgba(255,255,255,.8) 48%, transparent 60%) -150% 0 / 250% 100% no-repeat; }
.sb-slot.now.shine .sb-stk::after { animation: sb-sheen 1.1s .2s ease-out 1; }
@keyframes sb-sheen { to { background-position: 150% 0; } }
.sb-card { align-self: start; background: var(--paper); border: 2px solid var(--ink); border-radius: 14px; box-shadow: 5px 6px 0 var(--shade); padding: 14px; display: grid; gap: 8px; max-height: 100%; overflow-y: auto; }
.sb-card .sb-hole { width: 140px; height: 140px; justify-self: center; }
.sb-card h4 { margin: 0; font-size: 22px; }
.sb-card .ln { margin: 0; font-weight: 500; font-size: 17px; }
.sb-card .how { margin: 0; color: var(--ink-soft); font-weight: 500; font-size: 14px; }
.sb-card .st { justify-self: start; padding: 2px 9px; border: 1.5px solid currentColor; border-radius: 999px; font-size: 12px; color: var(--ink-soft); }
.sb-card .st.now { color: var(--pink); }
.sb-card .close { display: none; }
@media (max-width: 760px), (max-height: 640px) {
  #sticker-book { padding: 8px; padding-top: 48px; align-items: stretch; }
  .sb-body { grid-template-columns: minmax(0, 1fr); grid-template-rows: auto minmax(0, 1fr); }
  .sb-tabs { order: -1; flex-direction: row; overflow-x: auto; padding: 0 0 0 4px; margin: 0; }
  .sb-tab { writing-mode: horizontal-tb; transform: none; border-radius: 8px 8px 0 0; border-bottom: 0; padding: 6px 10px; white-space: nowrap; margin: 0 0 -2px; }
  .sb-tab[aria-selected="true"] { margin: 0 0 -2px; padding: 8px 10px 6px; }
  .sb-page { border-radius: 0 6px 6px 6px; padding: 14px 10px 18px 30px; }
  .sb-page::before { left: 6px; }
  .sb-grid { grid-template-columns: repeat(auto-fill, minmax(88px, 1fr)); gap: 10px 4px; }
  .sb-hole { width: 72px; height: 72px; }
  .big .sb-hole { width: 92px; height: 92px; }
  .sb-card { position: absolute; left: 6px; right: 6px; bottom: 6px; z-index: 2; }
  .sb-card[hidden] { display: none; }
  .sb-card .close { display: block; justify-self: end; border: 2px solid var(--ink); border-radius: 10px; background: var(--paper-2); padding: 6px 14px; }
}`;

const tiltOf = (id) => { let h = 0; for (const c of id) h = (h * 31 + c.charCodeAt(0)) | 0; return ((h % 13) - 6) * 0.9; };

/** Mounts the doors once and builds the book the first time it opens. Returns {open(mode), close(), get shown}. */
export function mountStickerBook({ doc, engine, gentle = () => false }) {
  const pauseCard = doc.querySelector('#pause .card-buttons.small');
  let book = null;
  let door = null;
  if (pauseCard) {
    door = doc.createElement('button');
    door.type = 'button';
    door.id = 'sticker-door';
    door.textContent = 'Sticker Book';
    door.hidden = true;
    pauseCard.prepend(door);
    door.addEventListener('click', () => open('pause'));
  }
  const showDoor = () => {
    if (!door) return;
    door.hidden = !(engine.run.owned.depth || Object.keys(engine.life.stickers || {}).length);
  };
  engine.subscribe('pause', () => { showDoor(); if (book?.mode === 'play') book.hide(); });
  engine.subscribe('resume', () => { if (book?.mode === 'pause') book.hide(); });
  engine.subscribe('suspend', ({ on }) => { if (on) book?.hide(); });
  engine.subscribe('ending', () => book?.hide());
  engine.subscribe('sticker-book', () => { if (!engine.paused && !engine.suspended && !engine.run.ended) open('play'); });

  async function open(mode) {
    if (!book) {
      let stars = [];
      try {
        const g = await import('./unlocks/basics/gold-fx.js');
        stars = [g.crayonStarSvg(0), g.starVariant(g.LOOPY, 0), g.starVariant(g.RUSHED, 0)].map((s) => `data:image/svg+xml;charset=utf-8,${encodeURIComponent(s)}`);
      } catch { /* a missing star is an empty slot, never an error */ }
      book = buildBook(doc, engine, stars, gentle);
    }
    book.show(mode);
  }
  return { open, close: () => book?.hide(), get shown() { return !!book?.shown; } };
}

function buildBook(doc, engine, stars, gentle) {
  if (!doc.getElementById('sticker-book-css')) {
    const st = doc.createElement('style');
    st.id = 'sticker-book-css';
    st.textContent = CSS;
    doc.head.appendChild(st);
  }
  const mk = (tag, cls, parent, text) => {
    const e = doc.createElement(tag);
    if (cls) e.className = cls;
    if (text != null) e.textContent = text;
    if (parent) parent.appendChild(e);
    return e;
  };
  const back = mk('div', '', doc.body);
  back.id = 'sticker-book';
  back.hidden = true;
  const root = mk('section', 'sb', back);
  root.setAttribute('role', 'dialog');
  root.setAttribute('aria-modal', 'true');
  root.setAttribute('aria-label', 'Sticker Book');
  const head = mk('div', 'sb-head', root);
  mk('h2', '', head, 'Sticker Book');
  const count = mk('span', 'n', head);
  mk('span', 'sp', head);
  const seg = mk('div', 'sb-seg', head);
  const vAll = mk('button', '', seg, 'Every run'); vAll.type = 'button';
  const vRun = mk('button', '', seg, 'This run'); vRun.type = 'button';
  const x = mk('button', 'sb-x', head, '×'); x.type = 'button'; x.setAttribute('aria-label', 'Close the Sticker Book');
  const body = mk('div', 'sb-body', root);
  const page = mk('div', 'sb-page', body);
  const tabs = mk('div', 'sb-tabs', body);
  tabs.setAttribute('role', 'tablist');
  const card = mk('aside', 'sb-card', body);

  let pageId = STICKER_PAGES[0].id, sel = null, shown = false, mode = 'play', onlyRun = false, fresh = new Set();

  const runOf = (id) => engine.life.stickers?.[id] || 0;
  const thisRun = () => new Set(engine.run.store?.depth?.earned || []);
  const artOf = (s) => {
    const a = s.art;
    if (a.k === 'gold') return { src: stars[a.v] || '', cls: '' };
    if (a.k === 'portrait') return { src: `assets/adcard/${a.theme}.webp`, cls: 'round' };
    if (a.k === 'scene') return { src: a.src, cls: 'ticket' };
    if (a.k === 'card') return { src: 'assets/adcard/logo.webp', cls: '' };
    return { src: a.src, cls: '' };
  };
  const slot = (s, now, big = false) => {
    const got = runOf(s.id);
    const b = mk('button', `sb-slot ${got ? (now.has(s.id) ? 'now' : 'past') : 'off'}${fresh.has(s.id) ? ' shine' : ''}`);
    b.type = 'button';
    b.dataset.id = s.id;
    const sealed = !got && s.hidden;
    b.setAttribute('aria-label', sealed ? 'Secret sticker' : `${brandText(s.name)}${got ? '' : ', not yet'}`);
    const hole = mk('div', 'sb-hole', b);
    if (sealed) {
      const env = mk('div', 'sb-env', hole);
      env.style.setProperty('--tilt', `${tiltOf(s.id)}deg`);
      const seal = mk('img', '', env); seal.alt = ''; seal.src = stars[0] || '';
    } else {
      const hold = mk('div', 'sb-hold', hole);
      const art = artOf(s);
      const stk = mk('div', `sb-stk ${art.cls}`, hold);
      stk.style.setProperty('--tilt', `${tiltOf(s.id)}deg`);
      const im = mk('img', '', stk); im.alt = ''; im.loading = 'lazy'; im.decoding = 'async'; im.src = art.src;
    }
    if (got) mk('span', 'sb-when', b, now.has(s.id) ? 'this run' : `run ${got}`);
    if (!big) mk('div', 'sb-nm', b, sealed ? 'Secret' : brandText(s.name));
    return b;
  };

  function drawTabs(now) {
    tabs.replaceChildren();
    for (const p of STICKER_PAGES) {
      const list = STICKERS.filter((s) => s.page === p.id);
      const got = list.filter((s) => runOf(s.id)).length;
      const t = mk('button', `sb-tab${got === list.length ? ' foil' : ''}${gentle() ? ' still' : ''}`, tabs, `${p.name} ${got}/${list.length}`);
      t.type = 'button';
      t.setAttribute('role', 'tab');
      t.setAttribute('aria-selected', String(p.id === pageId));
      t.style.setProperty('--tab', p.tab);
      t.addEventListener('click', () => { pageId = p.id; sel = null; draw(); });
    }
    const all = STICKERS.filter((s) => runOf(s.id)).length;
    count.textContent = `${all} of ${STICKERS.length} · ${now.size} this run`;
  }
  function drawPage(now) {
    const p = STICKER_PAGES.find((q) => q.id === pageId);
    const list = STICKERS.filter((s) => s.page === p.id);
    page.replaceChildren();
    const ph = mk('div', 'sb-ph', page);
    mk('h3', '', ph, p.name);
    mk('span', 'n', ph, `${list.filter((s) => runOf(s.id)).length} of ${list.length}`);
    mk('p', '', ph, p.note);
    const grid = mk('div', `sb-grid${p.big ? ' big' : ''}`, page);
    for (const s of list) grid.appendChild(slot(s, now));
    if (!sel || !list.some((s) => s.id === sel)) sel = (list.find((s) => runOf(s.id)) || list[0]).id;
  }
  function drawCard(now) {
    const s = STICKERS.find((q) => q.id === sel);
    card.replaceChildren();
    if (!s) return;
    const got = runOf(s.id);
    const sealed = !got && s.hidden;
    const big = slot(s, now, true);
    big.classList.remove('shine');
    const hole = big.querySelector('.sb-hole');
    card.appendChild(hole);
    mk('h4', '', card, sealed ? 'Secret' : brandText(s.name));
    if (got) mk('p', 'ln', card, brandText(s.line));
    mk('p', 'how', card, sealed ? 'Keep going. Someone will notice.' : brandText(s.how));
    mk('span', `st${now.has(s.id) ? ' now' : ''}`, card, !got ? 'Not yet' : now.has(s.id) ? 'Earned this run' : `First earned in run ${got}`);
    const c = mk('button', 'close', card, 'Back to the page'); c.type = 'button';
    c.addEventListener('click', () => { card.hidden = true; });
  }
  function draw() {
    const now = thisRun();
    root.classList.toggle('only-run', onlyRun);
    vAll.setAttribute('aria-pressed', String(!onlyRun));
    vRun.setAttribute('aria-pressed', String(onlyRun));
    drawTabs(now);
    drawPage(now);
    drawCard(now);
  }

  page.addEventListener('click', (e) => {
    const b = e.target.closest('.sb-slot');
    if (!b) return;
    sel = b.dataset.id;
    card.hidden = false;
    drawCard(thisRun());
  });
  vAll.addEventListener('click', () => { onlyRun = false; draw(); });
  vRun.addEventListener('click', () => { onlyRun = true; draw(); });
  x.addEventListener('click', () => hide());
  back.addEventListener('click', (e) => { if (e.target === back) hide(); });

  /* Shine once on stickers that arrived since the book was last open. */
  let lastSeen = null;
  function show(m) {
    mode = m;
    const now = thisRun();
    const ids = Object.keys(engine.life.stickers || {});
    fresh = lastSeen ? new Set(ids.filter((id) => !lastSeen.has(id))) : new Set([...now]);
    lastSeen = new Set(ids);
    if (fresh.size) {
      const first = STICKERS.find((s) => fresh.has(s.id));
      if (first) { pageId = first.page; sel = first.id; }
    }
    shown = true;
    back.hidden = false;
    card.hidden = doc.defaultView?.innerWidth <= 760;
    draw();
    if (!gentle() && root.animate) root.animate([{ transform: 'translateY(24px) rotate(-1deg)', opacity: 0 }, { transform: 'none', opacity: 1 }], { duration: 220, easing: 'ease-out' });
    x.focus?.({ preventScroll: true });
  }
  function hide() {
    if (!shown) return;
    shown = false;
    back.hidden = true;
    fresh = new Set();
  }
  return { show, hide, get shown() { return shown; }, get mode() { return mode; } };
}
