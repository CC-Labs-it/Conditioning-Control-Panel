/* sheet/main.js - the 2D counter sheet's entry: mountSheet(host, { root }). Same host seam and state model as the 3D
 * page (host.js, state/shop.js); sheet.html mounts it on the real host, mock/sheet.html on the mock.
 *   init.sheet true: the host shows it as a drop-down under its own wallet, so no wallet line and the sheet slides in.
 *   The preview wire (sheet/wire.js) rides on the same shop-state body: the page taps the raw body and reads what
 *   state/shop.js drops (the effects tab, places, gift, stamps, goal, ledger). Absent fields hide their parts.
 * Stray keys never spend (state/press-guard.js): only the heading has focus on open, a new step (a fresh pick, the
 * Yes step) focuses the fitting room's heading, and Space or Enter act on RELEASE of a deliberate press. Esc ladder:
 * the Yes step, then the fitting room, then close { reason: 'back' }. Tab stays inside the sheet. */

import { createShop } from '../state/shop.js';
import { asHost } from '../host.js';
import { createPressGuard, keyName } from '../state/press-guard.js';
import { sheetT } from './strings.js';
import { readPreview, platformOf, placesOf, isHere } from './wire.js';
import { roomModel, fxRoom, locate, slotOf, OWNED, FEATURED, groupOf, sheetTabs, badgeCount } from './model.js';
import { createView } from './view.js';
import { installFront } from './front.js';

const mintIdem = () => Array.from(crypto.getRandomValues(new Uint8Array(16)), (b) => b.toString(16).padStart(2, '0')).join('');
const pressKey = (e) => e.key === 'Enter' || e.key === ' ' || e.code === 'Space' || e.code === 'NumpadEnter';
const SOLD_MS = 1700;

export async function mountSheet(hostIn, { root, doc = root.ownerDocument, win = globalThis.window, now = () => performance.now(),
  frame = (f) => requestAnimationFrame(f), base = './', later = (f, ms) => setTimeout(f, ms), newIdem = mintIdem,
  loadIndex = () => fetch(`${base}assets/preview/index.json`).then((r) => (r.ok ? r.json() : null)) } = {}) {
  const host = typeof hostIn.ready === 'function' && hostIn.hosted !== undefined ? hostIn : asHost(hostIn);
  const init = (await host.ready()) || {};
  const t = sheetT(init.lexicon, init.companion);
  const motion = init.motion === 'off' ? 'off' : init.motion === 'less' || init.reducedMotion ? 'less' : 'full';
  const platform = platformOf(init);
  root.classList.add('shroot');
  root.dataset.motion = motion;
  if (init.gentle) root.dataset.gentle = 'on';
  // The raw shop-state body: state/shop.js reads today's rows from it, sheet/wire.js the rest. A body the page already
  // holds (an op's answer) is handed to shop.load() through `queued`, so both read the same body with no second trip.
  let raw = null, queued = null, rawRead = undefined;
  const request = (op, body, idem) => {
    if (op === 'shop-state' && queued) { const b = queued; queued = null; raw = b; return Promise.resolve(b); }
    return Promise.resolve(host.request(op, body, idem)).then((res) => { if (op === 'shop-state') raw = res; return res; });
  };
  const shop = createShop({ request, newIdem, tab: init.tab || null, wears: init.wears, rain: init.rainV2 === true });
  const guard = createPressGuard({ now, focused: typeof doc.hasFocus === 'function' ? doc.hasFocus() : true });
  const view = createView(doc, { t, sheet: init.sheet === true, base, platform });
  installFront(view, { t });
  root.replaceChildren(view.root);
  const ui = { tab: null, query: '', asking: false, wearing: new Map(), sold: null, index: null, focusTo: 'title',
    pv: readPreview(null), fx: null, fxNote: null, fxPending: false, hereOnly: false, spot: null, freshStamp: 0 };

  const st = () => shop.state();
  const pv = () => { if (raw !== rawRead) { rawRead = raw; ui.pv = readPreview(raw); } return ui.pv; };
  const away = (id) => !isHere(placesOf(ui.pv, id), platform);
  function paint() {
    const had = doc.activeElement && doc.activeElement.dataset ? doc.activeElement.dataset.key : null;
    const s = st(), card = shop.card(), p = pv(), sp = shop.sp();
    const fxe = ui.fx && locate(s, ui.fx, p);
    if (ui.fx && !fxe) ui.fx = null;
    const room = fxe ? fxRoom(fxe, { sp, t, pv: p, asking: ui.asking, pending: ui.fxPending, note: ui.fxNote, away: away(fxe.id) })
      : roomModel(card, { sp, t, wearing: ui.wearing, asking: ui.asking, pv: p, row: card && (locate(s, card.itemId, p) || {}).row, away: !!card && away(card.itemId) });
    view.paint({ status: shop.status(), state: s, sp, tier: s ? s.tier : 0, tabs: sheetTabs(shop.tabs(), p), tab: ui.tab, query: ui.query,
      index: ui.index, wearing: ui.wearing, sold: ui.sold, pick: fxe ? fxe.id : card ? card.itemId : null, pv: p, badge: badgeCount(s, p, sp), spot: ui.spot, freshStamp: ui.freshStamp, platform, hereOnly: ui.hereOnly, room }, on);
    const to = ui.focusTo;
    ui.focusTo = null;
    if (to === 'title') view.title.focus();
    else if (to === 'room') (view.roomHeading() || view.title).focus();
    else if (had) { const back = find(view.root, (n) => n.dataset && n.dataset.key === had); if (back) back.focus(); }
  }
  /** A new confirm step: presses that began before it never count, and focus sits on a heading, never an action. */
  const step = () => { guard.card(); ui.focusTo = 'room'; };

  function apply(ch) {
    if (!ch) return;
    for (const e of ch.effects || []) if (e.type === 'wallet') host.wallet(e.sp);
    if (ch.owned) { ui.asking = false; ui.sold = ch.owned; step(); if (motion !== 'off') later(() => { if (ui.sold === ch.owned) { ui.sold = null; paint(); } }, SOLD_MS); }
    if ('card' in ch && (!ch.card || ch.card.face !== 'buy')) ui.asking = false;
    if (ch.close) host.close(ch.close.reason);
    if (ch.signIn) host.signIn();
    if (ch.wear) {
      host.wear(ch.wear.itemId);
      if (ch.card && ch.card.noteKey === 'shop_worn') { ui.wearing.set(slotOf(ch.wear.itemId), ch.wear.itemId); step(); }
    }
    if (ch.buy) ch.buy.then(apply);
    paint();
  }

  /** Open an item in the fitting room; the picked item again puts it back. An effect row is the sheet's own (ui.fx);
   * a state row goes through state/shop.js's card. */
  function pick(id) {
    const open = shop.card(), was = ui.fx || (open && open.itemId);
    if (open) shop.intent({ type: 'back' });
    ui.fx = null; ui.asking = false; ui.sold = null; ui.fxNote = null;
    const at = was === id ? null : locate(st(), id, pv());
    if (at && at.fx) { ui.fx = at.id; step(); }
    else if (at) {
      for (let n = 10; shop.tabId() !== at.tab && n--;) shop.intent({ type: 'tabNext' });
      for (let n = 2; !shop.card() && n--;) shop.intent({ type: 'pick', bay: at.bay, slot: at.slot });
      step();
    }
    paint();
  }
  function act(id, viaKey, main) {
    if (main && !viaKey && !guard.ready()) return;   // a fresh step: a click lands only after it has been up a moment
    if (id === 'buy' || id === 'claim') { ui.asking = true; step(); paint(); return; }
    if (id === 'cancel') { ui.asking = false; step(); paint(); return; }
    if (ui.fx) { if (id === 'back') { ui.fx = null; ui.asking = false; step(); paint(); } return; }
    if (id === 'yes' || id === 'wear') return apply(shop.intent({ type: 'select' }));
    ui.asking = false;
    apply(shop.intent({ type: 'back' }));
  }
  const on = {
    tab(id) { ui.tab = id; paint(); },
    pick, act,
    spotlight(id) { ui.spot = id; if ((ui.fx || (shop.card() || {}).itemId) !== id) pick(id); else paint(); },
    gift() {}, stamp() {},
    close: () => host.close('back'),
    signIn: () => host.signIn(),
  };
  function ladder() {
    if (ui.asking && !(shop.card() || {}).pending && !ui.fxPending) { ui.asking = false; step(); paint(); return; }
    if (ui.fx || shop.card()) { act('back'); return; }
    host.close('back');
  }

  const visible = (n) => { for (let o = n; o && o !== view.root.parentNode; o = o.parentNode) if (o.hidden) return false; return true; };
  const focusables = () => all(view.root).filter((n) => (n.tagName === 'BUTTON' || n.tagName === 'INPUT') && !n.disabled && visible(n));
  const isField = (e) => !!(e.target && e.target.tagName === 'INPUT');
  const onKeyDown = (e) => {
    if (e.key === 'Escape') { e.preventDefault(); ladder(); return; }
    if (e.key === 'Tab') {
      const list = focusables(), i = list.indexOf(doc.activeElement);
      if (!list.length) return;
      if (e.shiftKey && i <= 0) { e.preventDefault(); list[list.length - 1].focus(); }
      else if (!e.shiftKey && (i < 0 || i === list.length - 1)) { e.preventDefault(); list[0].focus(); }
      return;
    }
    if (pressKey(e) && !isField(e)) { e.preventDefault(); guard.down(keyName(e), e.repeat); }
  };
  const onKeyUp = (e) => {
    if (!pressKey(e) || isField(e)) return;
    e.preventDefault();
    if (!guard.up(keyName(e))) return;
    const b = doc.activeElement;
    if (b && b.tagName === 'BUTTON' && !b.disabled && view.root.contains(b) && typeof b.onpress === 'function') b.onpress(true);
  };
  // Pointer clicks: a key's synthetic click (detail 0) never acts, keys go through the guarded release above.
  view.root.addEventListener('click', (e) => {
    let b = e.target;
    while (b && b !== view.root && b.tagName !== 'BUTTON') b = b.parentNode;
    if (!b || b === view.root || b.disabled || typeof b.onpress !== 'function' || e.detail === 0) return;
    b.onpress(false);
  });
  view.search.addEventListener('input', () => { ui.query = view.search.value || ''; paint(); });
  const onFocus = () => guard.focus(true), onBlur = () => guard.focus(false);
  win.addEventListener('keydown', onKeyDown);
  win.addEventListener('keyup', onKeyUp);
  win.addEventListener('focus', onFocus);
  win.addEventListener('blur', onBlur);
  if (typeof host.onWorn === 'function') host.onWorn((m) => {
    const id = m && m.id, ok = !!(m && m.ok === true);
    if (!ok && ui.wearing.get(slotOf(id)) === id) ui.wearing.delete(slotOf(id));
    apply(shop.worn(id, ok));
  });

  paint();
  if (init.sheet === true) frame(() => view.root.classList.add('open'));
  const [res, index] = await Promise.all([shop.load(), Promise.resolve().then(loadIndex).catch(() => null)]);
  ui.index = index;
  const tabs = sheetTabs(shop.tabs(), pv());
  ui.tab = init.tab && tabs.includes(groupOf(init.tab)) ? groupOf(init.tab) : tabs.includes(FEATURED) ? FEATURED : groupOf(shop.tabId() || OWNED);
  ui.focusTo = doc.activeElement === view.title || doc.activeElement === doc.body || !doc.activeElement ? 'title' : null;
  apply(res);
  return {
    shop, view, ui, pick, act, pv,
    dispose() {
      win.removeEventListener('keydown', onKeyDown); win.removeEventListener('keyup', onKeyUp);
      win.removeEventListener('focus', onFocus); win.removeEventListener('blur', onBlur);
      root.replaceChildren();
    },
  };
}

function all(n) { const out = []; for (const c of n.children || []) out.push(c, ...all(c)); return out; }
function find(n, f) { return all(n).find(f) || null; }
