/* sheet/view.js - draws the sheet with any document (the page's, or tests/fake-dom.mjs). It decides nothing: every
 * button carries `onpress(viaKey)` from sheet/main.js, which runs the state model. Paper objects on a night counter:
 * paper index tabs, items standing on shelf planks with hanging price tags, the fitting room on the right. */

import { TAB_ICONS, LOCK } from '../hud/icons.js';
import { tabKey } from '../state/strings.js';
import { sections, statusOf, statusText, artOf, locate, tabLabelKey, nameOf, FEATURED } from './model.js';

const TICK = '<svg viewBox="0 0 12 12" width="11" height="11" aria-hidden="true"><path d="M2 6.5l2.6 2.6L10 3.4" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/></svg>';

export function createView(doc, { t, sheet, base = './' }) {
  const el = (tag, cls, text) => { const n = doc.createElement(tag); if (cls) n.className = cls; if (text != null) n.textContent = text; return n; };
  const star = () => { const i = el('img', 'star'); i.src = `${base}star.png`; i.alt = ''; return i; };
  const button = (cls, text, key, onpress) => { const b = el('button', cls, text); b.type = 'button'; b.dataset.key = key; b.onpress = onpress; return b; };

  const root = el('section', 'sheet' + (sheet ? ' from-wallet' : ''));
  root.setAttribute('role', 'dialog'); root.setAttribute('aria-modal', 'true'); root.setAttribute('aria-labelledby', 's-title');
  const head = el('div', 's-head'), title = el('h1', 's-title', t('shop_title'));
  title.id = 's-title'; title.tabIndex = -1;
  const slip = el('span', 's-slip', t('shop_sheet_preview'));
  slip.hidden = true;
  const wallet = el('p', 's-wallet'), walletN = el('span', 'n');
  wallet.append(star(), walletN); wallet.setAttribute('aria-live', 'polite'); wallet.hidden = !!sheet;
  const search = el('input', 's-search');
  search.type = 'search'; search.placeholder = t('shop_sheet_search'); search.setAttribute('aria-label', t('shop_sheet_search')); search.autocomplete = 'off';
  const close = button('s-close', t('shop_back'), 'close');
  close.append(el('kbd', null, 'Esc'));
  const titleBox = el('div', 's-titlebox');
  titleBox.append(title, slip);
  head.append(titleBox, wallet, search, close);
  const tabs = el('div', 's-tabs');
  tabs.setAttribute('role', 'tablist'); tabs.setAttribute('aria-label', t('shop_tabs'));
  const list = el('div', 's-list'), room = el('aside', 's-detail'), main = el('div', 's-main');
  room.setAttribute('aria-live', 'polite');
  main.append(list, room);
  root.append(head, tabs, main);

  function artNode(art, big) {
    const box = el('span', 'art art-' + art.kind + (big ? ' big' : ''));
    if (art.kind === 'glyph') { box.innerHTML = TAB_ICONS[art.family] || TAB_ICONS.backroom; return box; }
    if (art.kind === 'desk') { const b = el('img', 'base'); b.src = art.base; b.alt = ''; box.append(b); }
    const i = el('img'); i.src = art.src; i.alt = ''; box.append(i);
    return box;
  }
  const ctx = (v) => ({ sp: v.sp, tier: v.tier, wearing: v.wearing, pv: v.pv });
  function statusNode(s, v, big) {
    if (s.kind === 'price') {
      const w = el('span', 'tg' + (s.short ? ' short' : '') + (big ? ' big' : '')), tag = el('span', 'tag ' + s.tier);
      tag.append(star(), el('span', null, String(s.price)));
      w.setAttribute('aria-label', t('shop_price', s.price));
      w.append(tag);
      return w;
    }
    const b = el('span', 'bd bd-' + s.kind);
    if (s.kind === 'wearing' || s.kind === 'on') b.innerHTML = TICK;
    if (s.kind === 'prime') b.innerHTML = LOCK;
    b.append(el('span', null, statusText(s, t, v.pv)));
    return b;
  }

  function paintTabs(v, on) {
    tabs.hidden = v.status !== 'ready' || !!v.query;
    tabs.replaceChildren(...v.tabs.map((id) => {
      const b = button('s-tab' + (id === 'prime' ? ' prime' : ''), null, 'tab:' + id, () => on.tab(id));
      b.setAttribute('role', 'tab'); b.setAttribute('aria-selected', String(id === v.tab));
      b.append(el('span', null, t(tabLabelKey(id))));
      if (id === FEATURED && v.badge) { const c = el('span', 'cnt', String(v.badge)); c.setAttribute('aria-label', t('shop_sheet_waiting', v.badge)); b.append(c); }
      return b;
    }));
  }

  /** One item standing on its plank. bare = picture only (the saving-for shelf). */
  function itemNode(e, v, on, bare = false) {
    const row = e.row, picked = v.pick === row.id;
    const b = button('it fam-' + (e.fx || e.tab), null, 'it:' + row.id, () => on.pick(row.id));
    b.setAttribute('aria-pressed', String(!!picked));
    b.dataset.v = String([...row.id].reduce((h, c) => (h * 31 + c.charCodeAt(0)) % 997, 7) % 4);   // a hand-placed tilt per item
    const stand = el('span', 'stand'), niche = el('span', 'niche');
    niche.append(view.thumb(e, v));
    if (e.x.isNew && !row.owned) niche.append(el('span', 'flag', t('shop_sheet_new')));
    stand.append(niche);
    b.append(stand);
    if (bare) { b.setAttribute('aria-label', nameOf(e, t)); return b; }
    const meta = el('span', 'meta');
    meta.append(statusNode(statusOf(e, ctx(v)), v), el('span', 'nm', nameOf(e, t)));
    if (e.x.how) meta.append(el('span', 'how', e.x.how));
    b.append(meta);
    return b;
  }
  function sectionHead(title, meta, fam) {
    const h = el('div', 'sh' + (fam ? ' fam-' + fam : ''));
    h.append(el('span', 'sq'), el('h2', null, title), el('span', 'rule'));
    if (meta) h.append(el('span', 'meta', meta));
    return h;
  }

  function paintList(v, on) {
    if (v.status !== 'ready') {
      const p = el('p', 'empty', t(v.status === 'signed_out' ? 'shop_signed_out' : v.status === 'offline' ? 'shop_offline' : 'shop_loading'));
      p.setAttribute('role', 'status');
      list.replaceChildren(p);
      if (v.status === 'signed_out') list.append(button('btn gold', t('shop_sign_in'), 'signin', on.signIn));
      return;
    }
    if (v.tab === FEATURED && !v.query && view.front) { list.replaceChildren(...view.front(v, on)); return; }
    list.replaceChildren(...sections(v.state, { tab: v.tab, query: v.query, t, pv: v.pv, platform: v.platform, hereOnly: v.hereOnly }).map((s) => {
      const sec = el('section', 'sec fam-' + s.key);
      sec.append(view.sectionHead(s, v));
      if (s.empty) sec.append(el('p', 'empty', s.empty));
      for (const plank of s.planks) { const g = el('div', 'grid'); g.append(...plank.map((e) => itemNode(e, v, on))); sec.append(g); }
      return sec;
    }));
  }

  function paintRoom(v, on) {
    const m = v.room;
    root.classList[m ? 'remove' : 'add']('nopick');
    if (!m) { room.replaceChildren(...view.idleRoom(v)); return; }
    const at = locate(v.state, m.itemId, v.pv), fam = at ? at.fx || at.tab : 'backroom';
    const mon = el('div', 'mon fam-' + fam), foot = el('div', 'mon-foot');
    const prev = view.monitor(at, v);
    if (v.sold === m.itemId) prev.append(el('span', 'sold', t('shop_owned')));
    foot.append(el('span', null, at && at.fx ? t('shop_sheet_your_screen') : t(tabKey(fam))));
    if (at && at.fx && !at.row.owned) foot.append(el('b', null, t('shop_sheet_trying')));
    mon.append(prev, foot);
    const name = el('h2', 'd-name', m.name);
    name.tabIndex = -1;
    room.replaceChildren(mon, name);
    if (m.desc) room.append(el('p', 'd-where', m.desc));
    room.append(...view.placeRow(at, v));
    if (at && !at.row.owned) { const pr = el('div', 'd-price'); pr.append(statusNode(statusOf(at, ctx(v)), v, true)); room.append(pr); }
    for (const l of m.lines) room.append(el('p', 'd-line d-' + l.cls, l.text));
    const acts = el('div', 'd-act');
    acts.append(...view.awayNote(at, v));
    if (m.ask) acts.append(el('p', 'd-ask', m.ask));
    for (const a of m.actions) {
      const b = button('btn' + (a.cls ? ' ' + a.cls : ''), a.label, 'act:' + a.id, (viaKey) => on.act(a.id, viaKey, a.main));
      b.disabled = !!a.disabled; b.dataset.main = a.main ? '1' : '';
      acts.append(b);
    }
    room.append(acts, ...view.roomExtra(at, v, on));
    if (m.note) { const n = el('p', 'd-note', m.note); n.setAttribute('role', 'status'); room.append(n); }
  }

  const view = {
    root, title, search, list, room, wallet, close, head, el, button, star, statusNode, itemNode, sectionHead: null, artNode,
    // Hooks the later parts fill (sheet/front.js, sheet/live.js, sheet/places.js); these defaults draw slice 1.
    front: null,
    thumb: (e, v) => artNode(artOf(e.id, v.index, e.fx || e.tab, base)),
    monitor(at, v) { const p = el('div', 'prev'); p.append(artNode(artOf(at ? at.id : '', v.index, at ? at.fx || at.tab : 'backroom', base), true)); return p; },
    idleRoom: () => [el('p', 'd-where', t('shop_sheet_pick'))],
    placeRow: () => [], awayNote: () => [], roomExtra: () => [],
    paint(v, on) {
      close.onpress = on.close;
      walletN.textContent = String(v.sp);
      wallet.setAttribute('aria-label', t('shop_wallet', v.sp));
      wallet.hidden = !!sheet || v.status === 'signed_out';
      search.hidden = v.status !== 'ready';
      slip.hidden = !(v.pv && v.pv.preview);
      paintTabs(v, on); paintList(v, on); paintRoom(v, on);
    },
    roomHeading: () => room.children.find ? room.children.find((n) => n.tagName === 'H2') : room.querySelector('h2'),
  };
  view.sectionHead = (s, v) => sectionHead(s.title, s.meta, s.key);
  return view;
}

