/* ui/unlocks/wave2/meta.js - The Guide and the Control Panel. Each is a door in the dock and a sheet in the card host: outside
 * the world, so a tilted or warped page never tilts them, and under Wake Up and the pause card. Neither stops the game. */

import { BY_ID } from '../../../data/items.js';
import { iconFor } from '../../icons.js';
import { docOf, fresh, squish } from '../basics/fx.js';

const PANEL_CSS = `
.fz-sheet-back { position: fixed; inset: 0; pointer-events: auto; background: rgba(0, 0, 0, .18); animation: fadein .15s ease-out; }
.fz-panel { position: absolute; right: 8px; top: 52px; bottom: 8px; width: min(380px, calc(100vw - 16px)); display: flex; flex-direction: column; min-height: 0; background: var(--panel); color: var(--ink); border: 1px solid var(--line); border-top: 4px solid var(--niche); box-shadow: 2px 2px 0 rgba(0, 0, 0, .35); font-family: var(--font); }
.fz-panel header { flex: none; display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 2px 8px; align-items: center; padding: 8px 10px; border-bottom: 1px solid var(--line); }
.fz-panel h2 { margin: 0; font-size: 20px; line-height: 1.1; }
.fz-panel header p { grid-column: 1 / -1; margin: 0; font-size: 13px; line-height: 1.2; color: var(--ink-soft); }
.fz-panel button { color: inherit; font: inherit; touch-action: manipulation; }
.fz-panel .shut { min-height: 40px; padding: 2px 12px; background: var(--btn); color: var(--btn-ink, inherit); border: 1px solid var(--line); }
.fz-panel .list { flex: 1 1 auto; min-height: 0; overflow-y: auto; overscroll-behavior: contain; padding: 0 10px 10px; }
.fz-panel .grp { display: flex; align-items: center; justify-content: space-between; gap: 8px; margin-top: 10px; padding-bottom: 2px; border-bottom: 1px solid var(--line); font-size: 13px; font-weight: 700; color: var(--ink-soft); }
.fz-panel .grp button { min-height: 32px; padding: 0 8px; background: none; border: 1px solid var(--line); font-size: 12px; font-weight: 400; }
.fz-panel .sw { display: grid; grid-template-columns: 26px minmax(0, 1fr) 42px; gap: 8px; align-items: center; width: 100%; min-height: 44px; padding: 2px 0; background: none; border: 0; border-bottom: 1px solid color-mix(in srgb, var(--line) 35%, transparent); text-align: left; font-size: 15px; line-height: 1.1; }
.fz-panel .sw .ico { font-size: 20px; width: 26px; height: 26px; line-height: 26px; text-align: center; }
.fz-panel .sw[aria-checked=false] .nm, .fz-panel .sw[aria-checked=false] .ico, .fz-panel .sw.wait .ico { opacity: .5; }
.fz-panel .sw small { display: block; font-size: 12px; color: var(--ink-soft); }
.fz-panel .sw i { position: relative; display: block; width: 42px; height: 22px; background: var(--panel-short); border: 1px solid var(--line); border-radius: 11px; transition: background .12s ease-out; }
.fz-panel .sw i::after { content: ''; position: absolute; left: 2px; top: 2px; width: 16px; height: 16px; background: var(--ink); border-radius: 50%; transition: transform .12s ease-out; }
.fz-panel .sw[aria-checked=true] i { background: linear-gradient(90deg, var(--niche), var(--niche-2)); }
.fz-panel .sw[aria-checked=true] i::after { transform: translateX(20px); background: var(--niche-ink); box-shadow: 0 0 0 1px var(--panel); }
@media (max-width: 759px) { .fz-panel { left: 8px; right: 8px; width: auto; top: 34vh; } }
`;

const COPY = {
  panel: 'Control Panel',
  sub: 'Off takes it off the screen and out of the speakers. What it pays by itself keeps coming.',
  none: 'Nothing here yet.',
  allOff: 'All off',
  allOn: 'All on',
  close: 'Close',
};

/** A door in the dock: an icon and a name, and the name goes when the edge is narrow. */
function door(ctx, label, emoji, onPress) {
  const host = ctx.dock({ edge: 'left', title: label, icon: emoji, door: onPress });
  const b = host.previousElementSibling;
  if (fresh(ctx)) squish(ctx, b, 1.2);
  return b;
}

function createPanel(ctx) {
  const doc = docOf(ctx);
  const back = ctx.el('div', '', null, doc.getElementById('cards'));
  back.className = 'fz-sheet-back';
  doc.getElementById('cards').prepend(back);   // under any card that comes up, which stays the host's last child
  const box = ctx.el('section', '', null, back);
  box.className = 'fz-panel';
  box.setAttribute('role', 'dialog');
  box.setAttribute('aria-label', COPY.panel);
  const head = ctx.el('header', '', null, box);
  ctx.el('h2', '', COPY.panel, head);
  const shut = ctx.el('button', '', COPY.close, head);
  shut.type = 'button';
  shut.className = 'shut';
  ctx.el('p', '', COPY.sub, head);
  const list = ctx.el('div', '', null, box);
  list.className = 'list';

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
  let groups = [];
  function build() {
    const top = list.scrollTop;
    list.replaceChildren();
    rows.clear();
    groupBtn.clear();
    groups = ctx.switches.groups();
    if (!groups.length) { const p = doc.createElement('p'); p.textContent = COPY.none; list.appendChild(p); }
    for (const g of groups) {
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
    list.scrollTop = top;
  }
  let shown = false;
  ctx.on('switch', ({ id }) => {
    if (!shown) return;
    if (!rows.has(id)) { build(); return; }   // a row bought while the sheet was up
    for (const r of rows.keys()) paint(r);
    for (const g of groups) paintGroup(g);
  });

  const hide = () => { shown = false; back.hidden = true; };
  const show = () => { if (shown) return; shown = true; back.hidden = false; build(); };
  shut.addEventListener('click', hide);
  back.addEventListener('click', (e) => { if (e.target === back) hide(); });
  back.hidden = true;
  return { show, hide, get shown() { return shown; }, get root() { return box; } };
}

export default {
  /* Guide: a door to the whole tree. The map and its layout load the first time it opens. */
  guide: {
    mount(ctx) {
      let sheet = null, opening = false, gone = false;
      const open = async () => {
        if (opening) return;
        opening = true;
        try {
          const { createGuide } = await import('../../guide-map.js');
          if (!gone && !ctx.state.ended) { sheet ||= createGuide(ctx); sheet.show(); }
        } catch (e) { console.error('guide failed', e); }
        opening = false;
      };
      door(ctx, 'Guide', '🗺️', open);
      return { open, get sheet() { return sheet; }, off() { gone = true; } };
    },
  },

  /* Control Panel: a switch for every unlock owned so far. See sim/switches.js for what off means. */
  panel: {
    mount(ctx) {
      ctx.css(PANEL_CSS);
      const sheet = createPanel(ctx);
      door(ctx, COPY.panel, '🎛️', () => sheet.show());
      return { open: () => sheet.show(), sheet };
    },
  },
};
