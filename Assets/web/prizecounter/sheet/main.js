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
import { sheetT, refusalKey } from './strings.js';
import { readPreview, platformOf, placesOf, isHere } from './wire.js';
import { roomModel, fxRoom, locate, slotOf, OWNED, FEATURED, groupOf, sheetTabs, badgeCount, goalEntry, labelOf, rowsOf, buyable } from './model.js';
import { createView } from './view.js';
import { createSound } from './sound.js';
import { createFx } from './fx.js';
import { setDiscoPalette } from './skins.js';
import { installFront } from './front.js';
import { installLive } from './live.js';
import { installJuice } from './juice.js';   // juice (Lane MOTION)

const mintIdem = () => Array.from(crypto.getRandomValues(new Uint8Array(16)), (b) => b.toString(16).padStart(2, '0')).join('');
const pressKey = (e) => e.key === 'Enter' || e.key === ' ' || e.code === 'Space' || e.code === 'NumpadEnter';
const SOLD_MS = 1700, NUDGE_MS = 7000;

export async function mountSheet(hostIn, { root, doc = root.ownerDocument, win = globalThis.window, now = () => performance.now(),
  frame = (f) => requestAnimationFrame(f), base = './', later = (f, ms) => setTimeout(f, ms), newIdem = mintIdem,
  loadIndex = () => fetch(`${base}assets/preview/index.json`).then((r) => (r.ok ? r.json() : null)), bind = null } = {}) {
  const host = typeof hostIn.ready === 'function' && hostIn.hosted !== undefined ? hostIn : asHost(hostIn);
  const init = (await host.ready()) || {};
  const t = sheetT(init.lexicon, init.companion);
  const motion = init.motion === 'off' ? 'off' : init.motion === 'less' || init.reducedMotion ? 'less' : 'full';
  const platform = platformOf(init);
  // the colour the host's pink filter draws (init.fogColour, '#rrggbb'): the Two-tone preview's colour one; absent = pink
  const fogColour = /^#[0-9a-f]{6}$/i.test(String(init.fogColour || '')) ? init.fogColour : null;
  root.classList.add('shroot');
  root.dataset.motion = motion;
  if (init.gentle) root.dataset.gentle = 'on'; else delete root.dataset.gentle;   // a reused page (sheet/reuse.js) mounts again
  // The raw shop-state body: state/shop.js reads today's rows from it, sheet/wire.js the rest. A body the page already
  // holds (an op's answer) is handed to shop.load() through `queued`, so both read the same body with no second trip.
  // A preview op (and the preview's buy) answers the whole fresh state: that becomes the raw body too.
  let raw = null, queued = null, rawRead = undefined, buyHadState = false;
  const fresh = (res) => !!(res && res.ok === true && Array.isArray(res.tabs));
  const request = (op, body, idem) => {
    if (op === 'shop-state' && queued) { const b = queued; queued = null; raw = b; return Promise.resolve(b); }
    return Promise.resolve(host.request(op, body, idem)).then((res) => {
      if (op === 'shop-state' || fresh(res)) raw = res;
      if (op === 'shop-buy') buyHadState = fresh(res);
      return res;
    });
  };
  const shop = createShop({ request, newIdem, tab: init.tab || null, wears: init.wears, rain: init.rainV2 === true });
  const guard = createPressGuard({ now, focused: typeof doc.hasFocus === 'function' ? doc.hasFocus() : true });
  const view = createView(doc, { t, sheet: init.sheet === true, base, platform, win, later });
  installFront(view, { t, locale: init.locale });
  const sound = createSound({ muted: motion === 'off' || init.muted === true, win });
  installLive(view, { t, base, motion, gentle: !!init.gentle, doc, win, onPop: (b) => { sound.unlock(); sound.pop(b); } });
  // The host's wallet star (init.walletAt, then wallet-at frames on resize) and the Disco hues (init.disco): shop frame lane.
  let walletAt = init.sheet === true && init.walletAt ? init.walletAt : null;
  if (typeof host.onWalletAt === 'function') host.onWalletAt((m) => { walletAt = { x: m.x, y: m.y }; });
  setDiscoPalette(init.disco);
  const onWallet = (k) => { if (init.sheet === true && typeof host.walletLand === 'function') host.walletLand(k); };
  // The host draws the stars over its own bar and counts its chip itself (init.walletFly, CONTRACT "Sheet additions").
  const onStars = init.sheet === true && init.walletFly === true && typeof host.walletStars === 'function' ? (plan) => { host.walletStars(plan); return true; } : null;
  const fx = createFx({ root: view.root, doc, base, motion, gentle: !!init.gentle, sound, wallet: () => view.wallet, walletAt: () => walletAt, onWallet, onStars });
  // ---- juice (Lane MOTION): movement, feedback and sound over each paint; decides nothing (sheet/juice.js) ----
  const juice = installJuice(view, { motion, gentle: !!init.gentle, doc, win, sound, fx, sheet: init.sheet === true, later });
  // ---- end juice ----
  root.replaceChildren(view.root);
  const ui = { tab: null, query: '', asking: false, wearing: new Map(), sold: null, index: null, focusTo: 'title', busy: false, idem: null,
    pv: readPreview(null), fx: null, fxNote: null, fxPending: false, hereOnly: false, spot: null, freshStamp: 0, fog2Try: null, nudge: null };
  // What the account wears now in the families the host wears in place (init.worn: the profile frame and picture, EMI's
  // hat and toy), so the Owned tab and the fitting room say Wearing from the first paint, not only after a Wear here.
  for (const id of Array.isArray(init.worn) ? init.worn : []) if (typeof id === 'string' && id) ui.wearing.set(slotOf(id), id);
  /** init.goes: the places a "take me there" can open from this door (absent = the line shows with no button). */
  const goes = Array.isArray(init.goes) ? init.goes.filter((g) => typeof g === 'string') : null;

  const st = () => shop.state();
  const pv = () => { if (raw !== rawRead) { rawRead = raw; ui.pv = readPreview(raw); } return ui.pv; };
  const away = (id) => !isHere(placesOf(ui.pv, id), platform);
  let gone = false;   // disposed: late timers and answers paint nothing
  function paint() {
    if (gone) return;
    const had = doc.activeElement && doc.activeElement.dataset ? doc.activeElement.dataset.key : null;
    const s = st(), card = shop.card(), p = pv(), sp = shop.sp();
    const fxe = ui.fx && locate(s, ui.fx, p);
    if (ui.fx && !fxe) ui.fx = null;
    const at = card && locate(s, card.itemId, p);
    const room = fxe ? fxRoom(fxe, { sp, t, pv: p, asking: ui.asking, pending: ui.fxPending, note: ui.fxNote, away: away(fxe.id), goes })
      : roomModel(card, { sp, t, wearing: ui.wearing, asking: ui.asking, pv: p, row: at && at.row, away: !!card && away(card.itemId), at, goes });
    view.paint({ status: shop.status(), state: s, sp, tier: s ? s.tier : 0, tabs: sheetTabs(shop.tabs(), p), tab: ui.tab, query: ui.query,
      index: ui.index, wearing: ui.wearing, sold: ui.sold, pick: fxe ? fxe.id : card ? card.itemId : null, pv: p, badge: badgeCount(s, p, sp), spot: ui.spot, freshStamp: ui.freshStamp, fog2Try: ui.fog2Try, fogColour, nudge: ui.nudge, platform, hereOnly: ui.hereOnly, room }, on);
    const to = ui.focusTo;
    ui.focusTo = null;
    if (to === 'title') view.title.focus();
    else if (to === 'room') (view.roomHeading() || view.title).focus();
    else if (had) { const back = find(view.root, (n) => n.dataset && n.dataset.key === had); if (back) back.focus(); }
  }
  /** A new confirm step: presses that began before it never count, and focus sits on a heading, never an action. */
  const step = () => { guard.card(); ui.focusTo = 'room'; };

  /** The receipt's wallet goes to the host, except in the preview: preview Sparkles are never the real wallet's. */
  const wallet = (sp) => { if (!pv().preview) host.wallet(sp); };
  /** The buy moment: stars leave the wallet and land on the fitting room, then the OWNED stamp lands with its knock. */
  function celebrate(id) {
    const e = locate(st(), id, pv()), paid = e && e.row.owned ? e.row.owned.paidSp : 0;
    const wait = paid > 0 ? fx.flyOut(view.room.children[0], paid) : 0;
    const stamp = () => { ui.sold = id; sound.thud(); paint(); if (motion !== 'off') later(() => { if (ui.sold === id) { ui.sold = null; paint(); } }, SOLD_MS); };
    if (wait) later(stamp, wait); else stamp();
  }
  function showNudge(text, id = null) {
    const n = ui.nudge = { text, id };
    later(() => { if (ui.nudge === n) { ui.nudge = null; paint(); } }, NUDGE_MS);
  }
  /** After a gain: the goal reached, else the first thing that just became affordable (the mockup's nudgeAfter). */
  function nudgeAfter(before) {
    const s = st(), p = pv(), sp = shop.sp(), g = goalEntry(s, p);
    if (g && !g.row.owned && before < g.row.priceSp && sp >= g.row.priceSp) { showNudge(t('shop_sheet_nudge_goal', labelOf(g, t)), g.id); return; }
    const hit = rowsOf(s, p).find((e) => buyable(e, { sp, tier: s.tier, pv: p }) && e.row.priceSp > before);
    if (hit) showNudge(t('shop_sheet_nudge_can', labelOf(hit, t)), hit.id);
  }
  /** A body the page already holds (an op's answer) becomes the state; without one, ask again. */
  async function adopt(res) { if (fresh(res)) queued = res; return shop.load(); }
  /** One preview op (shop-equip, shop-gift, shop-stamp, shop-goal): the answer is the fresh state, or a refusal line. */
  async function op(name, body) {
    if (ui.busy) return null;
    ui.busy = true;
    const res = await request(name, body);
    ui.busy = false;
    if (res && res.ok === true) await adopt(res);
    else if (ui.fx) ui.fxNote = t(refusalKey(res)); else showNudge(t(refusalKey(res)));
    paint();
    return res && res.ok === true ? res : null;
  }
  /** A buy the page makes itself: an effect row, or the stamp card's coupon on any 120 row. One idem per pick. */
  async function directBuy(id, coupon) {
    const s = st();
    if (ui.fxPending || !locate(s, id, pv())) return;
    ui.fxPending = true; ui.fxNote = null; paint();
    ui.idem = ui.idem || newIdem();
    const res = await request('shop-buy', { itemId: id, catalogVersion: s.catalogVersion, week: s.week, ...(coupon ? { coupon: true } : {}) }, ui.idem);
    ui.fxPending = false; ui.asking = false;
    if (res && res.ok === true && res.itemId === id) {
      ui.idem = null;
      await adopt(res);
      if (Number.isFinite(Number(res.sp))) wallet(Number(res.sp));
      step(); paint(); celebrate(id);
      return;
    }
    if (res && (res.reason === 'owned' || res.reason === 'stock_changed' || res.reason === 'catalog_changed')) await adopt(res.state || null);
    if (ui.fx) ui.fxNote = t(refusalKey(res)); else showNudge(t(refusalKey(res)));
    step(); paint();
  }

  function apply(ch) {
    if (!ch) return;
    for (const e of ch.effects || []) if (e.type === 'wallet') wallet(e.sp);
    if (ch.owned) {
      ui.asking = false; step();
      if (pv().preview && !buyHadState) adopt(null).then(() => { paint(); celebrate(ch.owned); });
    }
    if ('card' in ch && (!ch.card || ch.card.face !== 'buy')) ui.asking = false;
    if (ch.close) host.close(ch.close.reason);
    if (ch.signIn) host.signIn();
    if (ch.wear) {
      host.wear(ch.wear.itemId);
      if (ch.card && ch.card.noteKey === 'shop_worn') { ui.wearing.set(slotOf(ch.wear.itemId), ch.wear.itemId); step(); }
    }
    if (ch.buy) ch.buy.then(apply);
    paint();
    if (ch.owned && !(pv().preview && !buyHadState)) celebrate(ch.owned);
  }

  /** Open an item in the fitting room; the picked item again puts it back. An effect row is the sheet's own (ui.fx);
   * a state row goes through state/shop.js's card. */
  function pick(id) {
    const open = shop.card(), was = ui.fx || (open && open.itemId);
    if (open) shop.intent({ type: 'back' });
    ui.fx = null; ui.asking = false; ui.sold = null; ui.fxNote = null; ui.idem = null; ui.fog2Try = null;
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
    const picked = ui.fx || (shop.card() || {}).itemId, e = picked && locate(st(), picked, pv());
    if (id === 'yesclaim') return void directBuy(picked, true);
    if (id === 'save') return void op('shop-goal', { itemId: picked });
    if (ui.fx) {
      if (id === 'yes') return void directBuy(picked, false);
      const wear = { fxwear: [e.fx, picked], takeoff: [e.fx, null], mixon: ['mix', picked], mixoff: ['mix', null] }[id];
      if (wear) { sound.bell(sound.NOTES[3], 0.08, 0.6); return void op('shop-equip', { slot: wear[0], itemId: wear[1] }); }
      if (id === 'back') { ui.fx = null; ui.asking = false; ui.fog2Try = null; step(); paint(); }
      return;
    }
    if (id === 'yes' || id === 'wear') return apply(shop.intent({ type: 'select' }));
    ui.asking = false;
    apply(shop.intent({ type: 'back' }));
  }
  const on = {
    tab(id) { ui.tab = id; paint(); },
    pick, act,
    spotlight(id) { ui.spot = id; if ((ui.fx || (shop.card() || {}).itemId) !== id) pick(id); else paint(); },
    /** The Monday gift: the stars puff out of the box and count into the wallet. */
    async gift(viaKey) {
      if (!viaKey && !guard.ready()) return;
      const before = shop.sp();
      if (!(await op('shop-gift', {}))) return;
      const gain = shop.sp() - before;
      view.walletShown(before);
      fx.fly(view.giftBox, gain, (k) => view.walletShown(Math.round(before + gain * k)));
      nudgeAfter(before);
      if (ui.nudge) paint();
    },
    async stamp(viaKey) {
      if (!viaKey && !guard.ready()) return;
      ui.freshStamp = (pv().stamps || {}).day || 0;
      const ok = await op('shop-stamp', {});
      ui.freshStamp = 0;
      if (ok) sound.thud();
    },
    /** Two-tone's second colour: worn when Two-tone is owned, else only tried on here. */
    fog2(id) {
      const tw = locate(st(), 'fx.fog.twotone', pv());
      if (!(tw && tw.row.owned)) { ui.fog2Try = id; paint(); return; }
      ui.fog2Try = null;
      // The server takes a second colour only while Two-tone is ON (not_worn otherwise): picking a colour switches it on first.
      const on = ((pv() || {}).equip || {}).mix;
      (on ? Promise.resolve() : op('shop-equip', { slot: 'mix', itemId: 'fx.fog.twotone' })).then(() => op('shop-equip', { slot: 'fog2', itemId: id }));
    },
    nudgeView(id) { ui.nudge = null; ui.tab = (locate(st(), id, pv()) || {}).group || ui.tab; if ((ui.fx || (shop.card() || {}).itemId) !== id) pick(id); else paint(); },
    nudgeClose() { ui.nudge = null; paint(); },
    /** "See it on your profile" and the rest (sheet/shown.js): the host closes the sheet and opens the place. */
    go(place, viaKey) {
      if (!viaKey && !guard.ready()) return;
      const id = ui.fx || (shop.card() || {}).itemId;
      if (place && goes && goes.includes(place)) host.go(place, id || null);
    },
    /** Works here (website, phone): only what shows on this host. */
    here() { ui.hereOnly = !ui.hereOnly; paint(); },
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
    view.tips.key(e);   // a key puts a hover tip away; Tab lets the next focus show one (sheet/tips.js)
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
    if (b && b.tagName === 'BUTTON' && !b.disabled && view.root.contains(b) && typeof b.onpress === 'function') { sound.unlock(); b.onpress(true); }
  };
  // Pointer clicks: a key's synthetic click (detail 0) never acts, keys go through the guarded release above.
  view.root.addEventListener('click', (e) => {
    let b = e.target;
    while (b && b !== view.root && b.tagName !== 'BUTTON') b = b.parentNode;
    if (!b || b === view.root || b.disabled || typeof b.onpress !== 'function' || e.detail === 0) return;
    sound.unlock();   // the only door to sound: a click
    b.onpress(false);
  });
  view.search.addEventListener('input', () => { ui.query = view.search.value || ''; paint(); });
  const onFocus = () => guard.focus(true), onBlur = () => { guard.focus(false); view.tips.hide(); };
  win.addEventListener('keydown', onKeyDown);
  win.addEventListener('keyup', onKeyUp);
  win.addEventListener('focus', onFocus);
  win.addEventListener('blur', onBlur);
  if (typeof host.onWorn === 'function') host.onWorn((m) => {
    const id = m && m.id, ok = !!(m && m.ok === true);
    if (!ok && ui.wearing.get(slotOf(id)) === id) ui.wearing.delete(slotOf(id));
    apply(shop.worn(id, ok));
  });

  const dispose = () => {
    if (gone) return;
    gone = true;
    win.removeEventListener('keydown', onKeyDown); win.removeEventListener('keyup', onKeyUp);
    win.removeEventListener('focus', onFocus); win.removeEventListener('blur', onBlur);
    try { if (juice && typeof juice.dispose === 'function') juice.dispose(); } catch (e) { /* the juice is a bonus */ }
    try { sound.close(); } catch (e) { /* sound is a bonus */ }
    if (view.root.parentNode === root) root.replaceChildren();
  };
  if (typeof bind === 'function') bind(dispose);   // the reuse controller can drop this mount before its first answer

  paint();
  // The host slides the whole page down under its wallet (polish lane): the root's own slide on top of it showed bare
  // ground first, so the root starts in place; Gentle keeps the root's fade (the host never slides then).
  if (init.sheet === true && init.gentle) frame(() => view.root.classList.add('open'));
  else if (init.sheet === true) {
    const s = view.root.style;
    if (s) s.transition = 'none';
    view.root.classList.add('open');
    void view.root.offsetWidth;   // the style lands now, with no transition to run
    if (s) frame(() => { s.transition = ''; });
  }
  const [res, index] = await Promise.all([shop.load(), Promise.resolve().then(loadIndex).catch(() => null)]);
  ui.index = index;
  if (gone) return { shop, view, ui, pick, act, pv, dispose };
  const tabs = sheetTabs(shop.tabs(), pv());
  ui.tab = init.tab && tabs.includes(groupOf(init.tab)) ? groupOf(init.tab) : tabs.includes(FEATURED) ? FEATURED : groupOf(shop.tabId() || OWNED);
  ui.focusTo = doc.activeElement === view.title || doc.activeElement === doc.body || !doc.activeElement || !view.root.contains(doc.activeElement) ? 'title' : null;
  apply(res);
  // init.pick (CONTRACT "Studio doors"): a door names one item; the page opens on its tab with it in the fitting room
  const first = typeof init.pick === 'string' ? locate(st(), init.pick, pv()) : null;
  if (first) { ui.tab = first.group || ui.tab; pick(first.id); }
  return {
    shop, view, ui, pick, act, pv, dispose,
  };
}

function all(n) { const out = []; for (const c of n.children || []) out.push(c, ...all(c)); return out; }
function find(n, f) { return all(n).find(f) || null; }
