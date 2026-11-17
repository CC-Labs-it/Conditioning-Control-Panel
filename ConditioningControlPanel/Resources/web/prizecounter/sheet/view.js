/* sheet/view.js - draws the sheet with any document (the page's, or tests/fake-dom.mjs). It decides nothing: every
 * button carries `onpress(viaKey)` from sheet/main.js, which runs the state model. Paper objects on a night counter:
 * paper index tabs, items standing on shelf planks with hanging price tags, the fitting room on the right. */

import { TAB_ICONS, LOCK } from '../hud/icons.js';
import { tabKey, itemName } from '../state/strings.js';
import { sections, statusOf, statusText, artOf, sheetTabs, OWNED, locate } from './model.js';

const TICK = '<svg viewBox="0 0 12 12" width="11" height="11" aria-hidden="true"><path d="M2 6.5l2.6 2.6L10 3.4" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/></svg>';

export function createView(doc, { t, sheet, base = './' }) {
  const el = (tag, cls, text) => { const n = doc.createElement(tag); if (cls) n.className = cls; if (text != null) n.textContent = text; return n; };
  const star = () => { const i = el('img', 'star'); i.src = `${base}star.png`; i.alt = ''; return i; };
  const button = (cls, text, key, onpress) => { const b = el('button', cls, text); b.type = 'button'; b.dataset.key = key; b.onpress = onpress; return b; };

  const root = el('section', 'sheet' + (sheet ? ' from-wallet' : ''));
  root.setAttribute('role', 'dialog'); root.setAttribute('aria-modal', 'true'); root.setAttribute('aria-labelledby', 's-title');
  const head = el('div', 's-head'), title = el('h1', 's-title', t('shop_title'));
  title.id = 's-title'; title.tabIndex = -1;
  const wallet = el('p', 's-wallet'), walletN = el('span', 'n');
  wallet.append(star(), walletN); wallet.setAttribute('aria-live', 'polite'); wallet.hidden = !!sheet;
  const search = el('input', 's-search');
  search.type = 'search'; search.placeholder = t('shop_sheet_search'); search.setAttribute('aria-label', t('shop_sheet_search')); search.autocomplete = 'off';
  const close = button('s-close', t('shop_back'), 'close');
  close.append(el('kbd', null, 'Esc'));
  head.append(title, wallet, search, close);
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
  function statusNode(s, big) {
    if (s.kind === 'price') {
      const w = el('span', 'tg' + (s.short ? ' short' : '') + (big ? ' big' : '')), tag = el('span', 'tag ' + s.tier);
      tag.append(star(), el('span', null, String(s.price)));
      w.setAttribute('aria-label', t('shop_price', s.price));
      w.append(tag);
      return w;
    }
    const b = el('span', 'bd bd-' + s.kind);
    if (s.kind === 'wearing') b.innerHTML = TICK;
    if (s.kind === 'prime') b.innerHTML = LOCK;
    b.append(el('span', null, statusText(s, t)));
    return b;
  }

  function paintTabs(v, on) {
    tabs.hidden = v.status !== 'ready' || !!v.query;
    tabs.replaceChildren(...sheetTabs(v.tabs).map((id) => {
      const b = button('s-tab' + (id === 'prime' ? ' prime' : ''), null, 'tab:' + id, () => on.tab(id));
      b.setAttribute('role', 'tab'); b.setAttribute('aria-selected', String(id === v.tab));
      if (id !== OWNED) { const g = el('span', 'ico'); g.innerHTML = TAB_ICONS[id] || ''; b.append(g); }
      b.append(el('span', null, t(id === OWNED ? 'shop_owned' : tabKey(id))));
      return b;
    }));
  }

  function itemNode(e, v, on) {
    const row = e.row, picked = v.card && v.card.itemId === row.id;
    const b = button('it fam-' + e.tab, null, 'it:' + row.id, () => on.pick(row.id));
    b.setAttribute('aria-pressed', String(!!picked));
    b.dataset.v = String([...row.id].reduce((h, c) => (h * 31 + c.charCodeAt(0)) % 997, 7) % 4);   // a hand-placed tilt per item
    const stand = el('span', 'stand'), niche = el('span', 'niche');
    niche.append(artNode(artOf(row.id, v.index, e.tab, base)));
    stand.append(niche);
    const meta = el('span', 'meta');
    meta.append(statusNode(statusOf(row, v.sp, v.tier, v.wearing)), el('span', 'nm', itemName(t, row.id)));
    b.append(stand, meta);
    return b;
  }

  function paintList(v, on) {
    if (v.status !== 'ready') {
      const p = el('p', 'empty', t(v.status === 'signed_out' ? 'shop_signed_out' : v.status === 'offline' ? 'shop_offline' : 'shop_loading'));
      p.setAttribute('role', 'status');
      list.replaceChildren(p);
      if (v.status === 'signed_out') list.append(button('btn gold', t('shop_sign_in'), 'signin', on.signIn));
      return;
    }
    list.replaceChildren(...sections(v.state, { tab: v.tab, query: v.query, t }).map((s) => {
      const sec = el('section', 'sec fam-' + s.key), h = el('div', 'sh');
      h.append(el('span', 'sq'), el('h2', null, s.title), el('span', 'rule'));
      if (s.meta) h.append(el('span', 'meta', s.meta));
      sec.append(h);
      if (s.empty) sec.append(el('p', 'empty', s.empty));
      for (const plank of s.planks) { const g = el('div', 'grid'); g.append(...plank.map((e) => itemNode(e, v, on))); sec.append(g); }
      return sec;
    }));
  }

  function paintRoom(v, on) {
    const m = v.room;
    root.classList[m ? 'remove' : 'add']('nopick');
    if (!m) { room.replaceChildren(el('p', 'd-where', t('shop_sheet_pick'))); return; }
    const at = locate(v.state, m.itemId), fam = at ? at.tab : 'backroom';
    const mon = el('div', 'mon fam-' + fam), prev = el('div', 'prev'), foot = el('div', 'mon-foot');
    prev.append(artNode(artOf(m.itemId, v.index, fam, base), true));
    if (v.sold === m.itemId) prev.append(el('span', 'sold', t('shop_owned')));
    foot.append(el('span', null, t(tabKey(fam))));
    mon.append(prev, foot);
    const name = el('h2', 'd-name', m.name);
    name.tabIndex = -1;
    room.replaceChildren(mon, name);
    if (m.desc) room.append(el('p', 'd-where', m.desc));
    if (at) { const pr = el('div', 'd-price'); pr.append(statusNode(statusOf(at.row, v.sp, v.tier, v.wearing), true)); room.append(pr); }
    for (const l of m.lines) room.append(el('p', 'd-line d-' + l.cls, l.text));
    const acts = el('div', 'd-act');
    if (m.ask) acts.append(el('p', 'd-ask', m.ask));
    for (const a of m.actions) {
      const b = button('btn' + (a.cls ? ' ' + a.cls : ''), a.label, 'act:' + a.id, (viaKey) => on.act(a.id, viaKey, a.main));
      b.disabled = !!a.disabled; b.dataset.main = a.main ? '1' : '';
      acts.append(b);
    }
    room.append(acts);
    if (m.note) { const n = el('p', 'd-note', m.note); n.setAttribute('role', 'status'); room.append(n); }
  }

  return {
    root, title, search, list, room, wallet, close,
    paint(v, on) {
      close.onpress = on.close;
      walletN.textContent = String(v.sp);
      wallet.setAttribute('aria-label', t('shop_wallet', v.sp));
      wallet.hidden = !!sheet || v.status === 'signed_out';
      search.hidden = v.status !== 'ready';
      paintTabs(v, on); paintList(v, on); paintRoom(v, on);
    },
    roomHeading: () => room.children.find ? room.children.find((n) => n.tagName === 'H2') : room.querySelector('h2'),
  };
}
