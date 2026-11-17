/* sheet/view.js - draws the sheet with any document (the page's, or tests/fake-dom.mjs). It decides nothing: every
 * button carries `onpress(viaKey)` from sheet/main.js, which runs the state model. Paper objects on a night counter:
 * paper index tabs, items standing on shelf planks with hanging price tags, the fitting room on the right. */

import { TAB_ICONS, LOCK } from '../hud/icons.js';
import { tabKey } from '../state/strings.js';
import { placeMark, placesOf, isHere, awayLine, PLACES } from './wire.js';
import { sections, statusOf, statusText, locate, tabLabelKey, nameOf, howOf, FEATURED } from './model.js';
import { itemArt } from './pictures.js';
import { PREVIEW_SLIP } from './strings.js';
import { createTips } from './tips.js';
import { tipText, roomExplain, placesTip, TAB_TIPS, SIGN_TIPS, HELP } from './explain.js';

/* The mockup's place icons: a monitor, a globe, a phone (decorative, currentColor). */
export const PLACE_ICONS = {
  desktop: '<svg viewBox="0 0 14 12" width="13" height="11" aria-hidden="true"><rect x="1" y="1" width="12" height="7.6" rx="1" fill="none" stroke="currentColor" stroke-width="1.6"/><path d="M4.6 11h4.8M7 8.6V11" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/></svg>',
  web: '<svg viewBox="0 0 12 12" width="11" height="11" aria-hidden="true"><circle cx="6" cy="6" r="4.8" fill="none" stroke="currentColor" stroke-width="1.5"/><path d="M1.2 6h9.6M6 1.2c-2.5 2.6-2.5 7 0 9.6M6 1.2c2.5 2.6 2.5 7 0 9.6" fill="none" stroke="currentColor" stroke-width="1.2"/></svg>',
  phone: '<svg viewBox="0 0 8 12" width="7" height="11" aria-hidden="true"><rect x="1" y="1" width="6" height="10" rx="1.3" fill="none" stroke="currentColor" stroke-width="1.6"/></svg>',
};
const TICK = '<svg viewBox="0 0 12 12" width="11" height="11" aria-hidden="true"><path d="M2 6.5l2.6 2.6L10 3.4" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/></svg>';

export function createView(doc, { t, sheet, base = './', win = globalThis.window, later, cancel }) {
  const el = (tag, cls, text) => { const n = doc.createElement(tag); if (cls) n.className = cls; if (text != null) n.textContent = text; return n; };
  const star = () => { const i = el('img', 'star'); i.src = `${base}star.png`; i.alt = ''; return i; };
  const button = (cls, text, key, onpress) => { const b = el('button', cls, text); b.type = 'button'; b.dataset.key = key; b.onpress = onpress; return b; };

  const root = el('section', 'sheet' + (sheet ? ' from-wallet' : ''));
  root.setAttribute('role', 'dialog'); root.setAttribute('aria-modal', 'true'); root.setAttribute('aria-labelledby', 's-title');
  const head = el('div', 's-head'), title = el('h1', 's-title', t('shop_title'));
  title.id = 's-title'; title.tabIndex = -1;
  const slip = el('span', 's-slip', PREVIEW_SLIP);
  slip.hidden = true;
  const wallet = el('p', 's-wallet'), walletN = el('span', 'n');
  wallet.append(star(), walletN); wallet.setAttribute('aria-live', 'polite'); wallet.hidden = !!sheet;
  const search = el('input', 's-search');
  search.type = 'search'; search.placeholder = t('shop_sheet_search'); search.setAttribute('aria-label', t('shop_sheet_search')); search.autocomplete = 'off';
  const close = button('s-close', t('shop_back'), 'close');
  close.append(el('kbd', null, 'Esc'));
  const titleBox = el('div', 's-titlebox');
  titleBox.append(title, slip);
  const here = button('herebtn', t('shop_sheet_works_here'), 'here', null);
  here.hidden = true;
  head.append(titleBox, here, wallet, search, close);
  const tabs = el('div', 's-tabs');
  tabs.setAttribute('role', 'tablist'); tabs.setAttribute('aria-label', t('shop_tabs'));
  const list = el('div', 's-list'), room = el('aside', 's-detail'), main = el('div', 's-main');
  room.setAttribute('aria-live', 'polite');
  main.append(list, room);
  const nudge = el('div', 'nudge');
  nudge.setAttribute('role', 'status'); nudge.hidden = true;
  root.append(head, tabs, main, nudge);
  // the hover tips (sheet/tips.js) and the shop's own "?" marks; the wallet's sits beside it and hides with it
  const tips = createTips({ doc, root, win, ...(later ? { later } : {}), ...(cancel ? { cancel } : {}) });
  /** A small "?" that shows one line about the shop's own mechanics: hover, keyboard focus or a press. None on touch. */
  const helpMark = (k, keep = false) => {
    const b = button('qm', null, 'help:' + k, null);   // the "?" is drawn by sheet.css (.qm::before): no text in the page's words
    b.setAttribute('aria-label', t('shop_sheet_help_mark'));
    b.onpress = () => tips.showNow(b);
    return tips.bind(b, t(HELP[k]), { keep });
  };
  const walletHelp = helpMark('wallet', true);
  head.insertBefore ? head.insertBefore(walletHelp, search) : head.append(walletHelp);

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
      // not enough: the pale sticker carries how many Sparkles are missing, readable at a glance (aria-hidden: the room says it in words)
      if (s.short && s.price > (v.sp || 0)) { const n = el('span', 'need', t('shop_sheet_tag_need', s.price - (v.sp || 0))); n.setAttribute('aria-hidden', 'true'); w.append(n); }
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
      return TAB_TIPS[id] ? tips.bind(b, t(TAB_TIPS[id])) : b;
    }));
  }

  /** One item standing on its plank. bare = picture only (the saving-for shelf). */
  function itemNode(e, v, on, bare = false, mixed = false) {
    const row = e.row, picked = v.pick === row.id;
    const b = button('it fam-' + (e.fx || e.tab), null, 'it:' + row.id, () => on.pick(row.id));
    b.setAttribute('aria-pressed', String(!!picked));
    b.dataset.v = String([...row.id].reduce((h, c) => (h * 31 + c.charCodeAt(0)) % 997, 7) % 4);   // a hand-placed tilt per item
    const stand = el('span', 'stand'), niche = el('span', 'niche'), st = statusOf(e, ctx(v));
    niche.append(view.thumb(e, v));
    if (e.x.isNew && !row.owned) niche.append(el('span', 'flag', t('shop_sheet_new')));
    // worn or switched on: a tick pin on the cubby, so the whole grid shows what is on at a glance
    if (st.kind === 'wearing' || st.kind === 'on') { b.classList.add('is-' + st.kind); const pin = el('span', 'wear-pin'); pin.innerHTML = TICK; pin.setAttribute('aria-hidden', 'true'); niche.append(pin); }
    const places = placesOf(v.pv, e.id), away = !isHere(places, v.platform);
    if (away) { b.classList.add('elsewhere'); if (mixed) niche.append(view.placeMark(places, true)); }   // a shelf head carries the mark; a mixed list marks each away item
    stand.append(niche);
    b.append(stand);
    tips.bind(b, tipText(e, t, st));
    if (bare) { b.setAttribute('aria-label', nameOf(e, t) + (away ? '. ' + awayLine(places, v.platform, t) : '')); return b; }
    const meta = el('span', 'meta');
    meta.append(statusNode(st, v), el('span', 'nm', nameOf(e, t)));
    if (howOf(e, t)) meta.append(el('span', 'how', howOf(e, t)));
    b.append(meta);
    return b;
  }
  function sectionHead(title, meta, fam) {
    const h = el('div', 'sh' + (fam ? ' fam-' + fam : '')), sign = el('h2', null, title);
    h.append(el('span', 'sq'), sign, el('span', 'rule'));
    if (SIGN_TIPS[fam]) tips.bind(sign, t(SIGN_TIPS[fam]), { focus: false });   // a sign takes no focus: hover, and screen readers
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
      for (const plank of s.planks) { const g = el('div', 'grid'); g.append(...plank.map((e) => itemNode(e, v, on, false, !!s.mixed))); sec.append(g); }
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
    // on touch there is no hover: the card's tip line sits under the name
    const tip = at && tips.touch() ? tipText(at, t) : null;
    if (tip) room.append(el('p', 'd-tip', tip));
    const what = at ? view.explainBlock(at) : null;
    if (what) room.append(what); else if (m.desc) room.append(el('p', 'd-where', m.desc));
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

  /** A paper slip under the head: a goal reached, something newly affordable, or a refusal. */
  function paintNudge(v, on) {
    const n = v.nudge;
    nudge.hidden = !n;
    if (!n) { nudge.replaceChildren(); return; }
    nudge.replaceChildren(star(), el('span', null, n.text));
    if (n.id) nudge.append(button('', t('shop_sheet_view'), 'nudge-view', () => on.nudgeView(n.id)));
    const x = button('x', '×', 'nudge-x', () => on.nudgeClose());
    x.setAttribute('aria-label', t('shop_sheet_dismiss'));
    nudge.append(x);
  }

  const view = {
    root, title, search, list, room, wallet, close, head, nudge,
    walletShown(n) { walletN.textContent = String(n); }, el, button, star, statusNode, itemNode, sectionHead: null, artNode,
    // Hooks the later parts fill (sheet/front.js, sheet/live.js, sheet/places.js); these defaults draw slice 1.
    front: null,
    thumb: (e, v) => artNode(itemArt(e.id, v.index, e.fx || e.tab, base)),
    monitor(at, v) { const p = el('div', 'prev'); p.append(artNode(itemArt(at ? at.id : '', v.index, at ? at.fx || at.tab : 'backroom', base), true)); return p; },
    idleRoom: () => [el('p', 'd-where', t('shop_sheet_pick'))],
    /** Works on: Desktop app, Website, Phone, each on or struck through, the one you are on underlined. */
    placeRow(e, v) {
      if (!e || !v.pv || !v.pv.places.size) return [];
      const at = placesOf(v.pv, e.id), p = el('p', 'd-place');
      p.append(el('span', null, t('shop_sheet_works_on')));
      for (const pl of PLACES) {
        const yes = at.includes(pl), c = el('span', 'pc ' + (yes ? 'on' : 'off') + (pl === v.platform ? ' here' : '')), i = el('span', 'ico');
        i.innerHTML = PLACE_ICONS[pl];
        c.append(i, el('span', null, t('shop_sheet_place_' + pl)));
        c.setAttribute('aria-label', t('shop_sheet_place_' + pl) + ', ' + t(yes ? 'shop_sheet_place_yes' : 'shop_sheet_place_no') + (pl === v.platform ? ', ' + t('shop_sheet_you_are_here') : ''));
        p.append(c);
      }
      return [p];
    },
    /** An item that does not show on this host: where it does, and that buying or wearing it here still counts. */
    awayNote(e, v) {
      if (!e || e.x.todo || (e.row.needs === 'prime' && v.tier < 2)) return [];
      const at = placesOf(v.pv, e.id);
      if (isHere(at, v.platform)) return [];
      return [el('p', 'd-away', awayLine(at, v.platform, t) + ' ' + t(e.row.owned ? 'shop_sheet_away_owned' : 'shop_sheet_away_buy'))];
    },
    roomExtra: () => [],
    tips, helpMark,
    /** The fitting room's "What it does": what it does, where it shows, how to turn it on or wear it (each when known). */
    explainBlock(e) {
      const x = roomExplain(e, t);
      if (!x.does && !x.at && !x.use) return null;
      const box = el('div', 'd-what');
      box.append(el('h3', null, t('shop_sheet_what_it_does')));
      if (x.does) box.append(el('p', 'd-does', x.does));
      for (const [k, text] of [['where', x.at], ['how', x.use]]) {
        if (!text) continue;
        const p = el('p', 'd-' + k);
        p.append(el('b', null, t('shop_sheet_lead_' + k)), el('span', null, text));
        box.append(p);
      }
      return box;
    },
    stage(e, v) { const s = el('div', 'stage'); s.append(view.thumb(e, v)); return s; },
    mini(e, v) { const s = el('span', 'mini-art'); s.append(view.thumb(e, v)); return s; },
    /** The place mark: the place icons and plain words; away = the paper tab an item wears where it does not show. */
    placeMark(places, away) {
      const m = placeMark(places, t), s = el('span', 'pl' + (m.all ? ' all' : '') + (away ? ' away' : ''));
      for (const p of places) { const i = el('span', 'ico'); i.innerHTML = PLACE_ICONS[p]; s.append(i); }
      s.append(el('span', null, m.text));
      return s;
    },
    hostNote() { const p = el('p', 'hostnote'); p.append(el('span', null, t('shop_sheet_host_note')), view.placeMark(['desktop'], false), el('span', null, t('shop_sheet_host_note_end'))); return [p]; },
    paint(v, on) {
      tips.reset();
      close.onpress = on.close;
      walletN.textContent = String(v.sp);
      wallet.setAttribute('aria-label', t('shop_wallet', v.sp));
      // In a sheet the host's own wallet shows the balance; the preview wallet is not the host's, so it shows here.
      wallet.hidden = (!!sheet && !(v.pv && v.pv.preview)) || v.status === 'signed_out';
      walletHelp.hidden = wallet.hidden || tips.touch();
      search.hidden = v.status !== 'ready';
      here.hidden = v.status !== 'ready' || v.platform === 'desktop' || !(v.pv && v.pv.places.size);
      here.setAttribute('aria-pressed', String(!!v.hereOnly));
      here.onpress = on.here;
      slip.hidden = !(v.pv && v.pv.preview);
      paintTabs(v, on); paintList(v, on); paintRoom(v, on); paintNudge(v, on);
    },
    roomHeading: () => room.children.find ? room.children.find((n) => n.tagName === 'H2') : room.querySelector('h2'),
  };
  view.sectionHead = (s, v) => {
    const h = sectionHead(s.title, s.meta, s.key);
    if (s.places && v.pv && v.pv.places.size) h.append(tips.bind(view.placeMark(s.places, false), placesTip(s.places, t), { focus: false }));
    return h;
  };
  return view;
}

