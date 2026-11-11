/* ui/unlocks/wave2/meta.js - The Guide and the Control Panel. Each has a door beside the audio controls and a sheet: outside
 * the world, so a tilted or warped page never tilts them, and under Wake Up and the pause card. Neither stops the game. */

import { BY_ID } from '../../../data/items.js';
import { iconFor } from '../../icons.js';
import { docOf, fresh, squish } from '../basics/fx.js';

/* The Control Panel is a compact sheet that hangs under the HUD, left of the speaker (owner 2026-11-07). Its door is an icon
 * beside the volume button, never a dock tab. Tabs sort the switches into Effects, Looks, Sound and Toys. A pin keeps it on
 * screen; unpinned, it shuts on any press outside it. It never covers Wake Up: it starts below the HUD row. */
const PANEL_CSS = `
#ctl-panel-btn { display: grid; place-items: center; width: 38px; height: 36px; padding: 0; color: #171717; background: #d4d0c8; border: 2px outset #fff; touch-action: manipulation; cursor: pointer; }
#ctl-panel-btn:active, #ctl-panel-btn[aria-expanded=true] { border-style: inset; background: #c1bdb6; }
#ctl-panel-btn svg { width: 26px; height: 26px; pointer-events: none; }
#ctl-panel-btn:focus-visible { outline: 2px solid #000; outline-offset: -4px; }
@media (pointer: coarse) { #ctl-panel-btn { min-width: 44px; min-height: 44px; } }
body[data-tc-ending] #ctl-panel-btn, body[data-tc-ending] .fz-panel { display: none; }
.fz-panel { position: fixed; z-index: 1; pointer-events: auto; top: var(--game-controls-bottom, calc(max(8px, env(safe-area-inset-top)) + 44px)); right: max(8px, env(safe-area-inset-right)); width: min(340px, calc(100vw - 16px)); max-height: min(560px, calc(100vh - var(--game-controls-bottom, 52px) - 16px - var(--dock-h, 0px))); display: flex; flex-direction: column; min-height: 0; background: var(--panel); color: var(--ink); border: 1px solid var(--line); border-top: 4px solid var(--niche); box-shadow: 2px 3px 0 rgba(0, 0, 0, .3); font-family: var(--font); transform-origin: calc(100% - 60px) 0; animation: fz-cp-in .16s ease-out; }
.fz-panel[hidden] { display: none; }
.fz-panel.out { animation: fz-cp-out .11s ease-in forwards; }
@keyframes fz-cp-in { from { opacity: 0; transform: translateY(-6px) scale(.96); } }
@keyframes fz-cp-out { to { opacity: 0; transform: translateY(-4px) scale(.97); } }
body.gentle .fz-panel, body.gentle .fz-panel.out { animation-duration: .3s; animation-timing-function: ease; }
@media (prefers-reduced-motion: reduce) { .fz-panel, .fz-panel.out { animation: none; } }
.fz-panel button { color: inherit; font: inherit; touch-action: manipulation; }
.fz-panel .cp-top { flex: none; display: flex; align-items: center; gap: 4px; padding: 4px 4px 0 8px; }
.fz-panel h2 { flex: 1 1 auto; min-width: 0; margin: 0; font-size: 15px; line-height: 1.1; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.fz-panel .cp-ic { width: 32px; height: 32px; display: grid; place-items: center; padding: 0; background: none; border: 1px solid transparent; border-radius: 6px; cursor: pointer; }
.fz-panel .cp-ic:hover { border-color: var(--line); }
.fz-panel .cp-ic svg { width: 18px; height: 18px; pointer-events: none; }
.fz-panel .cp-pin[aria-pressed=true] { background: var(--niche-soft); color: var(--niche-ink); border-color: var(--niche); }
.fz-panel .cp-pin[aria-pressed=true] svg { transform: rotate(-35deg); }
.fz-panel .cp-pin svg { transition: transform .14s ease-out; }
.fz-panel .cp-tabs { flex: none; display: flex; flex-wrap: wrap; gap: 3px; padding: 4px 6px 0; border-bottom: 1px solid var(--line); }
.fz-panel .cp-tab { flex: 1 1 0; min-width: 36px; display: flex; align-items: center; justify-content: center; gap: 4px; height: 34px; padding: 0 6px; background: color-mix(in srgb, var(--panel) 80%, var(--line)); border: 1px solid var(--line); border-bottom: 0; border-radius: 8px 8px 0 0; font-size: 13px; cursor: pointer; white-space: nowrap; }
.fz-panel .cp-tab img { width: 22px; height: 22px; flex: none; pointer-events: none; }
.fz-panel .cp-tab b { font-weight: 400; overflow: hidden; text-overflow: ellipsis; pointer-events: none; }
.fz-panel .cp-tab small { font-size: 11px; color: var(--ink-soft); pointer-events: none; }
.fz-panel .cp-tab[aria-selected=true] { flex-grow: 1.6; background: var(--panel); border-color: var(--niche); border-top-width: 2px; color: var(--ink); font-weight: 700; }
.fz-panel .cp-tab[aria-selected=true] img { transform: scale(1.12); }
@media (max-width: 420px) { .fz-panel .cp-tab:not([aria-selected=true]) b { display: none; } }
.fz-panel .list { flex: 1 1 auto; min-height: 0; overflow-y: auto; overscroll-behavior: contain; padding: 0 8px 4px; }
.fz-panel .grp { display: flex; align-items: center; justify-content: space-between; gap: 8px; margin-top: 6px; padding-bottom: 2px; border-bottom: 1px solid var(--line); font-size: 12px; font-weight: 700; color: var(--ink-soft); }
.fz-panel .grp button { min-height: 26px; padding: 0 8px; background: none; border: 1px solid var(--line); font-size: 11px; font-weight: 400; }
.fz-panel .sw { display: grid; grid-template-columns: 24px minmax(0, 1fr) 38px; gap: 8px; align-items: center; width: 100%; min-height: 38px; padding: 1px 0; background: none; border: 0; border-bottom: 1px solid color-mix(in srgb, var(--line) 35%, transparent); text-align: left; font-size: 14px; line-height: 1.1; cursor: pointer; }
.fz-panel .sw .ico { width: 24px; height: 24px; }
.fz-panel .sw[aria-checked=false] .nm, .fz-panel .sw[aria-checked=false] .ico, .fz-panel .sw.wait .ico { opacity: .5; }
.fz-panel .sw small { display: block; font-size: 11px; color: var(--ink-soft); }
.fz-panel .sw small:empty { display: none; }
.fz-panel .sw i { position: relative; display: block; width: 38px; height: 20px; background: var(--panel-short); border: 1px solid var(--line); border-radius: 10px; transition: background .12s ease-out; }
.fz-panel .sw i::after { content: ''; position: absolute; left: 2px; top: 2px; width: 14px; height: 14px; background: var(--ink); border-radius: 50%; transition: transform .12s ease-out; }
.fz-panel .sw[aria-checked=true] i { background: linear-gradient(90deg, var(--niche), var(--niche-2)); }
.fz-panel .sw[aria-checked=true] i::after { transform: translateX(18px); background: var(--niche-ink); box-shadow: 0 0 0 1px var(--panel); }
.fz-panel .cp-foot { flex: none; margin: 0; padding: 4px 8px 6px; font-size: 11px; line-height: 1.2; color: var(--ink-soft); border-top: 1px solid color-mix(in srgb, var(--line) 35%, transparent); }
@media (max-width: 759px) { .fz-panel { top: var(--game-controls-bottom, calc(max(8px, env(safe-area-inset-top)) + 52px)); max-height: calc(100vh - var(--game-controls-bottom, 60px) - 16px - var(--dock-h, 0px)); } .fz-panel .sw { min-height: 42px; } }
`;

const COPY = {
  panel: 'Control Panel',
  sub: 'Off takes it off the screen and out of the speakers. What it pays keeps coming.',
  none: 'Nothing here yet.',
  allOff: 'All off',
  allOn: 'All on',
  close: 'Close',
  pin: 'Keep on screen',
  unpin: 'Let it close',
  stage: 'On stage',
  stageAll: 'Everything at once',
  stageAllOn: 'Every look at full strength',
  stageAllOff: 'Three at full strength, the rest take turns',
  stagePinned: 'Pinned at full strength',
  stageOn: 'On stage now',
  stageRest: 'Resting, takes its turn',
  stageFull: 'At full strength',
};

/** The stage rows' small line (ui/stage.js): what the effect is doing now. */
export function stageNote(row, all) {
  if (all) return COPY.stageFull;
  if (row.pinned) return COPY.stagePinned;
  return row.cast ? COPY.stageOn : COPY.stageRest;
}
/** The tab the stage group lives on: Looks when there is one, else the first tab. */
export const stageTabOf = (tabs) => (tabs.some((t) => t.id === 'looks') ? 'looks' : tabs[0]?.id || 'looks');

/** Tabs by lane. Everything not listed is an effect. */
export const PANEL_TABS = Object.freeze([
  { id: 'effects', name: 'Effects', art: 'spiral' },
  { id: 'looks', name: 'Looks', art: 'palette' },
  { id: 'sound', name: 'Sound', art: 'speaker' },
  { id: 'toys', name: 'Toys', art: 'box' },
]);
const TAB_OF_LANE = { look: 'looks', ears: 'sound', side: 'toys', emi: 'toys', trouble: 'toys' };
export const panelTabOf = (lane) => TAB_OF_LANE[lane] || 'effects';
/** The lane groups sorted into tabs, empty tabs left out. */
export function panelTabs(groups) {
  return PANEL_TABS.map((t) => ({ ...t, groups: groups.filter((g) => panelTabOf(g.lane) === t.id) }))
    .filter((t) => t.groups.length)
    .map((t) => ({ ...t, count: t.groups.reduce((n, g) => n + g.rows.length, 0) }));
}

const GUIDE_CSS = `
#guide-btn { display: grid; place-items: center; flex: none; width: 38px; height: 36px; padding: 0; color: #171717; background: #d4d0c8; border: 2px outset #fff; touch-action: manipulation; cursor: pointer; }
#guide-btn:active { border-style: inset; background: #c1bdb6; }
#guide-btn svg { width: 28px; height: 28px; pointer-events: none; }
#guide-btn:focus-visible { outline: 2px solid #000; outline-offset: -4px; }
#guide-btn:disabled { opacity: .55; cursor: default; }
body[data-tc-ending] #guide-btn { display: none; }
@media (pointer: coarse) { #guide-btn { min-width: 44px; min-height: 44px; } }
@media (max-width: 759px) {
  #game-controls { display: grid; grid-template-columns: 44px 44px auto auto; gap: 4px; justify-items: end; }
  #game-controls > #ctl-panel-btn { grid-area: 1 / 1; }
  #guide-btn { grid-area: 1 / 2; }
  #audio-controls { grid-area: 1 / 3; }
  #wake { grid-area: 1 / 4; }
  #dock-lock { grid-area: 2 / 2; }
  #usage-ring { grid-area: 2 / 3; }
  #account-chip { grid-area: 2 / 4; max-width: 44px; padding: 4px 9px; }
  #account-chip span { display: none; }
}
`;

/** A persistent map door; its one arrival runs on the game clock with the rest of the unlock. */
export function guideButton(ctx, onPress) {
  const doc = docOf(ctx), controls = doc.getElementById('game-controls');
  if (!controls) return null;
  ctx.css(GUIDE_CSS);
  doc.getElementById('guide-btn')?.remove();
  const b = doc.createElement('button');
  b.type = 'button'; b.id = 'guide-btn'; b.title = 'Guide';
  b.setAttribute('aria-label', 'Guide'); b.setAttribute('aria-haspopup', 'dialog');
  b.appendChild(iconFor(doc, BY_ID.guide));
  controls.insertBefore(b, doc.getElementById('audio-controls'));
  b.addEventListener('click', () => { if (!ctx.paused && !ctx.state.ended) onPress(); });
  const sync = () => { b.disabled = ctx.paused || !!ctx.state.ended; };
  for (const event of ['pause', 'resume', 'suspend', 'ending']) ctx.on(event, sync);
  sync();
  const inset = () => doc.documentElement.style.setProperty('--game-controls-bottom', `${Math.ceil(controls.getBoundingClientRect().bottom + 8)}px`);
  const Observer = doc.defaultView.ResizeObserver;
  const observer = Observer ? new Observer(inset) : null;
  observer?.observe(controls);
  doc.defaultView.addEventListener('resize', inset);
  inset();
  ctx.cleanup(() => {
    observer?.disconnect(); doc.defaultView.removeEventListener('resize', inset);
    doc.documentElement.style.removeProperty('--game-controls-bottom'); b.remove();
  });
  if (fresh(ctx)) ctx.anim(b, ctx.gentle || ctx.reduced
    ? [{ opacity: 0 }, { opacity: 1 }]
    : [{ opacity: 0, transform: 'translateY(-8px) scale(.65)' }, { opacity: 1, transform: 'translateY(3px) scale(1.12)', offset: .55 }, { opacity: 1, transform: 'none' }],
  { duration: ctx.gentle || ctx.reduced ? 300 : 620, easing: 'ease-out' });
  return b;
}

const SVG = 'http://www.w3.org/2000/svg';
const glyph = (doc, d) => {
  const s = doc.createElementNS(SVG, 'svg');
  s.setAttribute('viewBox', '0 0 24 24');
  s.setAttribute('aria-hidden', 'true');
  s.innerHTML = d;
  return s;
};
const PIN = '<path d="M9 3h6l-1 6 3 3H7l3-3Z M12 12v9" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round" stroke-linecap="round"/>';
const CROSS = '<path d="m6 6 12 12M18 6 6 18" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"/>';
const PREF_KEY = 'control-panel';

function createPanel(ctx) {
  const doc = docOf(ctx);
  const cards = doc.getElementById('cards');
  const box = doc.createElement('section');
  box.className = 'fz-panel';
  box.id = 'ctl-panel';
  box.setAttribute('role', 'dialog');
  box.setAttribute('aria-label', COPY.panel);
  box.hidden = true;
  cards.prepend(box);   // under any card that comes up, which stays the host's last child
  const top = doc.createElement('div');
  top.className = 'cp-top';
  const title = doc.createElement('h2');
  title.textContent = COPY.panel;
  const pinBtn = doc.createElement('button');
  pinBtn.type = 'button';
  pinBtn.className = 'cp-ic cp-pin';
  pinBtn.appendChild(glyph(doc, PIN));
  const shut = doc.createElement('button');
  shut.type = 'button';
  shut.className = 'cp-ic cp-shut';
  shut.setAttribute('aria-label', COPY.close);
  shut.title = COPY.close;
  shut.appendChild(glyph(doc, CROSS));
  top.append(title, pinBtn, shut);
  const tabBar = doc.createElement('div');
  tabBar.className = 'cp-tabs';
  tabBar.setAttribute('role', 'tablist');
  const list = doc.createElement('div');
  list.className = 'list';
  list.setAttribute('role', 'tabpanel');
  const foot = doc.createElement('p');
  foot.className = 'cp-foot';
  foot.textContent = COPY.sub;
  box.append(top, tabBar, list, foot);

  const saved = ctx.store.getLayout(PREF_KEY) || {};
  let pinned = saved.pinned === true, tab = typeof saved.tab === 'string' ? saved.tab : 'effects', shown = false;
  const persist = () => ctx.store.setLayout(PREF_KEY, { pinned, tab, open: shown });
  function paintPin() {
    pinBtn.setAttribute('aria-pressed', String(pinned));
    const label = pinned ? COPY.unpin : COPY.pin;
    pinBtn.setAttribute('aria-label', label);
    pinBtn.title = label;
  }
  paintPin();

  const rows = new Map();
  function paint(id) {
    const r = rows.get(id);
    if (!r) return;
    r.setAttribute('aria-checked', String(ctx.switches.isOn(id)));
    const parent = ctx.switches.waitingOn(id);
    r.classList.toggle('wait', !!parent);
    r.querySelector('small').textContent = parent ? `Waiting for ${ctx.brand.text(BY_ID[parent].name)}` : '';
  }
  const groupBtn = new Map();
  function paintGroup(g) {
    const anyOn = g.rows.some((it) => ctx.switches.isOn(it.id));
    const b = groupBtn.get(g.lane);
    if (b) b.textContent = anyOn ? COPY.allOff : COPY.allOn;
  }
  let groups = [], tabs = [];
  function build() {
    const scroll = list.scrollTop;
    list.replaceChildren();
    tabBar.replaceChildren();
    rows.clear();
    groupBtn.clear();
    groups = ctx.switches.groups();
    tabs = panelTabs(groups);
    if (!tabs.some((t) => t.id === tab)) tab = tabs[0]?.id || 'effects';
    for (const t of tabs) {
      const b = doc.createElement('button');
      b.type = 'button';
      b.className = 'cp-tab';
      b.dataset.tab = t.id;
      b.setAttribute('role', 'tab');
      b.setAttribute('aria-selected', String(t.id === tab));
      b.setAttribute('aria-label', `${t.name}, ${t.count}`);
      b.title = t.name;
      const img = doc.createElement('img');
      img.src = `assets/clipart/${t.art}.webp`;
      img.alt = '';
      img.width = 22;
      img.height = 22;
      const nm = doc.createElement('b');
      nm.textContent = t.name;
      const n = doc.createElement('small');
      n.textContent = String(t.count);
      b.append(img, nm, n);
      b.addEventListener('click', () => {
        if (tab === t.id) return;
        tab = t.id;
        persist();
        list.scrollTop = 0;
        build();
        if (!ctx.gentle && !ctx.reduced) squish(ctx, b, .6);
      });
      tabBar.appendChild(b);
    }
    tabBar.hidden = tabs.length < 2;
    const shownGroups = tabs.find((t) => t.id === tab)?.groups || [];
    const staged = ctx.stage.list();
    if (staged.length && tab === stageTabOf(tabs)) stageGroup(staged);
    else if (!shownGroups.length) { const p = doc.createElement('p'); p.textContent = COPY.none; list.appendChild(p); }
    for (const g of shownGroups) {
      const h = doc.createElement('div');
      h.className = 'grp';
      const name = doc.createElement('span');
      name.textContent = ctx.brand.text(g.name);
      const all = doc.createElement('button');
      all.type = 'button';
      all.addEventListener('click', () => {
        const to = !g.rows.some((it) => ctx.switches.isOn(it.id));
        for (const it of g.rows) ctx.switches.set(it.id, to);
      });
      groupBtn.set(g.lane, all);
      h.append(name, all);
      list.appendChild(h);
      for (const it of g.rows) {
        const b = doc.createElement('button');
        b.type = 'button';
        b.className = 'sw';
        b.dataset.id = it.id;
        b.setAttribute('role', 'switch');
        const nm = doc.createElement('span');
        nm.className = 'nm';
        nm.textContent = ctx.brand.text(it.name);
        nm.appendChild(doc.createElement('small'));
        b.append(iconFor(doc, it), nm, doc.createElement('i'));
        b.addEventListener('click', () => ctx.switches.set(it.id, !ctx.switches.isOn(it.id)));
        rows.set(it.id, b);
        list.appendChild(b);
        paint(it.id);
      }
      paintGroup(g);
    }
    list.scrollTop = scroll;
  }
  /* The stage group: one "everything at once" switch, then a pin per ambient effect. A pinned effect always plays at full
   * strength; the switch turns the cast of three off. */
  function stageRow(label, note, on, onPress, icon) {
    const b = doc.createElement('button');
    b.type = 'button';
    b.className = 'sw';
    b.setAttribute('role', 'switch');
    b.setAttribute('aria-checked', String(on));
    const nm = doc.createElement('span');
    nm.className = 'nm';
    nm.textContent = label;
    const sm = doc.createElement('small');
    sm.textContent = note;
    nm.appendChild(sm);
    b.append(icon, nm, doc.createElement('i'));
    b.addEventListener('click', onPress);
    list.appendChild(b);
    return b;
  }
  function stageGroup(staged) {
    const h = doc.createElement('div');
    h.className = 'grp';
    h.dataset.stage = '';
    const name = doc.createElement('span');
    name.textContent = COPY.stage;
    h.appendChild(name);
    list.appendChild(h);
    const all = ctx.stage.all;
    const star = doc.createElement('span');
    star.className = 'ico';
    try { star.appendChild(iconFor(doc, BY_ID.starburst)); } catch {}
    stageRow(COPY.stageAll, all ? COPY.stageAllOn : COPY.stageAllOff, all, () => ctx.stage.setAll(!ctx.stage.all), star).dataset.stageAll = '';
    for (const r of staged) {
      let icon;
      try { icon = iconFor(doc, r.item); } catch { icon = doc.createElement('span'); icon.className = 'ico'; }
      const b = stageRow(r.name, stageNote(r, all), r.pinned, () => ctx.stage.pin(r.id, !r.pinned), icon);
      b.dataset.stagePin = r.id;
      b.setAttribute('aria-label', `${r.name}: keep at full strength`);
    }
  }
  ctx.on('stage', () => { if (shown) build(); });

  ctx.on('switch', ({ id }) => {
    if (!shown) return;
    if (!rows.has(id) && !groups.some((g) => g.rows.some((it) => it.id === id))) { build(); return; }   // a row bought while it was up
    for (const r of rows.keys()) paint(r);
    for (const g of groups) paintGroup(g);
  });

  let btn = null, outTimer = null;
  const paintBtn = () => btn?.setAttribute('aria-expanded', String(shown));
  function hide() {
    if (!shown) return;
    shown = false;
    persist();
    paintBtn();
    clearTimeout(outTimer);
    if (ctx.reduced) { box.hidden = true; return; }
    box.classList.add('out');
    outTimer = setTimeout(() => { box.classList.remove('out'); if (!shown) box.hidden = true; }, ctx.gentle ? 300 : 110);
  }
  function show() {
    if (shown) return;
    shown = true;
    clearTimeout(outTimer);
    box.classList.remove('out');
    box.hidden = false;
    build();
    persist();
    paintBtn();
  }
  const toggle = () => (shown ? hide() : show());
  shut.addEventListener('click', hide);
  pinBtn.addEventListener('click', () => {
    pinned = !pinned;
    paintPin();
    persist();
    if (!ctx.gentle && !ctx.reduced) squish(ctx, pinBtn, .8);
  });
  const outside = (e) => {
    if (!shown || pinned) return;
    const t = e.target;
    if (box.contains(t) || btn?.contains(t)) return;
    if (t?.closest?.('#wake,#pause,.card')) return;   // Wake Up and cards keep their own press; the sheet waits
    hide();
  };
  doc.addEventListener('pointerdown', outside, true);
  ctx.cleanup(() => { doc.removeEventListener('pointerdown', outside, true); clearTimeout(outTimer); box.remove(); });
  return {
    show, hide, toggle,
    attach(b) { btn = b; paintBtn(); },
    get shown() { return shown; }, get pinned() { return pinned; }, get root() { return box; },
    reopen() { if (saved.pinned && saved.open) show(); },
  };
}

/** The panel's door on the HUD: left of the speaker, so Wake Up stays the outer edge. */
function hudButton(ctx, item, onPress) {
  const doc = docOf(ctx);
  const controls = doc.getElementById('game-controls');
  const audio = doc.getElementById('audio-controls');
  if (!controls) return null;
  doc.getElementById('ctl-panel-btn')?.remove();
  const b = doc.createElement('button');
  b.type = 'button';
  b.id = 'ctl-panel-btn';
  b.setAttribute('aria-label', COPY.panel);
  b.setAttribute('aria-controls', 'ctl-panel');
  b.setAttribute('aria-expanded', 'false');
  b.title = COPY.panel;
  try { b.appendChild(iconFor(doc, item)); } catch { b.textContent = '🎛️'; }
  controls.insertBefore(b, doc.getElementById('guide-btn') || (audio && audio.parentElement === controls ? audio : controls.firstChild));
  b.addEventListener('click', onPress);
  ctx.cleanup(() => b.remove());
  if (fresh(ctx)) squish(ctx, b, 1.3);
  return b;
}

export default {
  /* Guide: a door to the whole tree. The map and its layout load the first time it opens. */
  guide: {
    mount(ctx) {
      let sheet = null, opening = false, gone = false;
      const open = async () => {
        if (opening || gone || ctx.paused || ctx.state.ended) return;
        opening = true;
        try {
          const { createGuide } = await import('../../guide-map.js');
          if (!gone && !ctx.paused && !ctx.state.ended) { sheet ||= createGuide(ctx); sheet.show(); }
        } catch (e) { console.error('guide failed', e); }
        opening = false;
      };
      guideButton(ctx, open);
      return { open, get sheet() { return sheet; }, off() { gone = true; } };
    },
  },

  /* Control Panel: a switch for every unlock owned so far, behind an icon beside the speaker. See sim/switches.js for what off means. */
  panel: {
    mount(ctx) {
      ctx.css(PANEL_CSS);
      const sheet = createPanel(ctx);
      sheet.attach(hudButton(ctx, ctx.item, () => sheet.toggle()));
      sheet.reopen();
      return { open: () => sheet.show(), sheet };
    },
  },
};
