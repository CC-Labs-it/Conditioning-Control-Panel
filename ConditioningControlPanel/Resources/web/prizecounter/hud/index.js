/* hud/index.js - the HUD over the counter (lane B3-W4): tab stamps, wallet chip, week tag, preview box, confirm card,
 * and the plain list when the scene cannot load. It draws what the state model says and sends intents back; it never
 * decides. Every effect has an IN and an OUT; Gentle drops movement, motion 'off' cuts.
 *
 *   const hud = createHud(root, { t, send, goTab, signIn, motion, gentle, list });
 *   hud.paint(view); hud.receipt(sp, {x, y}); hud.previewSlot   // lane B3-W5 fills the slot */

import { tabKey, itemName, itemDesc } from '../state/strings.js';
import { TAB_ICONS, STAR, LOCK, BACK } from './icons.js';
import { cardModel, renderCard } from './card.js';
import { countDown, weekNo } from './wallet.js';

const STATUS = { loading: 'shop_loading', offline: 'shop_offline' };
const OUT_MS = 160;

export function createHud(root, { t, send, goTab, signIn = () => {}, motion = 'full', gentle = false, list = false }) {
  const doc = root.ownerDocument;
  const el = (tag, cls, text) => { const n = doc.createElement(tag); if (cls) n.className = cls; if (text != null) n.textContent = text; return n; };
  const hud = el('div', 'pc-hud');
  hud.dataset.motion = motion;
  if (gentle) hud.dataset.gentle = 'on';

  const back = el('button', 'pc-back');
  back.type = 'button'; back.innerHTML = BACK; back.setAttribute('aria-label', t('shop_back'));
  back.onclick = () => { back.blur(); send({ type: 'back' }); };
  const week = el('span', 'pc-week');
  const wallet = el('p', 'pc-wallet'), walletN = el('span', 'pc-wallet-n');
  wallet.innerHTML = STAR; wallet.append(walletN); wallet.setAttribute('aria-live', 'polite');
  const top = el('div', 'pc-top');
  top.append(back, week, wallet);

  const tabs = el('nav', 'pc-tabs'), tabRow = el('div', 'pc-tab-row');
  tabs.setAttribute('aria-label', t('shop_tabs'));
  tabRow.setAttribute('role', 'tablist');
  const hint = (a, b) => { const h = el('span', 'pc-hint'); h.setAttribute('aria-hidden', 'true'); h.append(el('kbd', null, a), el('kbd', null, b)); return h; };
  tabs.append(hint('Q', 'LB'), tabRow, hint('E', 'RB'));

  const listEl = el('div', 'pc-list');
  const peek = el('aside', 'pc-peek'), previewSlot = el('div', 'pc-peek-view');
  const peekName = el('p', 'pc-peek-name'), peekPrice = el('p', 'pc-peek-price'), peekDesc = el('p', 'pc-peek-desc');
  peek.append(previewSlot, peekName, peekPrice, peekDesc);
  peek.onclick = () => send({ type: 'select' });
  const status = el('p', 'pc-status');
  status.setAttribute('role', 'status');
  const modal = el('div', 'pc-modal'), scrim = el('div', 'pc-scrim');
  scrim.onclick = () => send({ type: 'back' });
  modal.append(scrim);
  modal.hidden = true;
  const fx = el('div', 'pc-fx');
  // Signed out: the shelves sit greyed behind one button that asks the host for its sign-in.
  const gate = el('div', 'pc-gate'), gateBtn = el('button', 'pc-btn pc-main', t('shop_sign_in'));
  gateBtn.type = 'button'; gateBtn.title = t('shop_signed_out');
  gateBtn.onclick = () => { gateBtn.blur(); signIn(); };
  gate.append(gateBtn);
  gate.hidden = true;
  hud.append(top, tabs, listEl, peek, status, gate, modal, fx);
  root.append(hud);

  let shown = 0, counting = null, tabIds = '', peekKey = '', cardKey = null, card = null, outTimer = 0;
  let peekBand = 0;   // the preview strip's last height: it hides between items, the framing should not jump
  const setN = (n) => { shown = n; walletN.textContent = String(n); wallet.setAttribute('aria-label', t('shop_wallet', n)); };
  const pulse = (node, cls) => { if (motion === 'off') return; node.classList.remove(cls); void node.offsetWidth; node.classList.add(cls); };

  function paintTabs(v) {
    const ids = v.tabs.join();
    if (ids !== tabIds) {
      tabIds = ids;
      tabRow.replaceChildren(...v.tabs.map((id) => {
        const b = el('button', 'pc-tab' + (id === 'prime' ? ' pc-tab-prime' : ''));
        b.type = 'button'; b.dataset.tab = id; b.innerHTML = TAB_ICONS[id] || '';
        b.setAttribute('role', 'tab'); b.setAttribute('aria-label', t(tabKey(id))); b.title = t(tabKey(id));
        b.onclick = () => { b.blur(); goTab(id); };
        return b;
      }));
    }
    for (const b of tabRow.children) b.setAttribute('aria-selected', String(b.dataset.tab === v.tabId));
  }

  // The focused row's look on a tag: price, or a stamp; red when the wallet is short.
  const rowLook = (row, v) => {
    if (row.owned) return { stamp: t('shop_owned') };
    if (row.sale !== 'on') return { line: t('shop_soon') };
    const prime = row.needs === 'prime' && v.tier < 2;
    return { price: String(row.priceSp), prime, red: !prime && row.priceSp > v.sp };
  };
  function fillPrice(node, look) {
    node.className = 'pc-peek-price' + (look.red ? ' pc-red' : '');
    node.replaceChildren();
    if (look.stamp) return node.append(el('span', 'pc-stamp pc-stamp-owned', look.stamp));
    if (look.line) return node.append(el('span', 'pc-soon', look.line));
    node.innerHTML = (look.prime ? LOCK : '') + STAR;
    node.append(el('span', null, look.price));
  }
  function paintPeek(v) {
    const row = v.focus && v.focus.row;
    peek.hidden = !row || v.status !== 'ready';
    if (!row) return;
    const key = `${row.id}|${row.owned ? 1 : 0}|${v.sp}|${v.tier}`;
    if (key === peekKey) return;
    if (!peekKey.startsWith(row.id + '|')) pulse(peek, 'pc-swap');
    peekKey = key;
    peekName.textContent = itemName(t, row.id);
    const desc = itemDesc(t, row.id);
    peekDesc.textContent = desc || '';
    peekDesc.hidden = !desc;
    fillPrice(peekPrice, rowLook(row, v));
  }

  function paintList(v) {
    listEl.hidden = !list || v.status !== 'ready';
    if (listEl.hidden) return;
    listEl.replaceChildren(...v.shelves.map((s, bay) => {
      const ul = el('ul', 'pc-shelf');
      s.rows.forEach((r, slot) => {
        if (!r) return;
        const b = el('button', 'pc-item'), p = el('span');
        b.type = 'button';
        b.append(el('span', 'pc-item-name', itemName(t, r.id)), p);
        fillPrice(p, rowLook(r, v));
        if (v.focus && v.focus.bay === bay && v.focus.slot === slot) b.classList.add('pc-focus');
        b.onclick = () => send({ type: 'pick', bay, slot });
        const li = el('li'); li.append(b); ul.append(li);
      });
      return ul;
    }));
  }

  let gateTimer = 0;
  function paintGate(on) {
    if (on === !gate.hidden && !gate.classList.contains('pc-out')) return;
    clearTimeout(gateTimer);
    if (on) { gate.classList.remove('pc-out'); gate.hidden = false; pulse(gate, 'pc-in'); return; }
    if (gate.hidden) return;
    if (motion === 'off') { gate.hidden = true; return; }
    gate.classList.add('pc-out');
    gateTimer = setTimeout(() => { gate.hidden = true; gate.classList.remove('pc-out', 'pc-in'); }, OUT_MS);
  }

  const buttons = () => (card ? [...card.querySelectorAll('button')].filter((b) => !b.disabled) : []);
  function paintCard(v) {
    const m = cardModel(v.card, v.sp, t);
    const key = m && JSON.stringify(m);
    if (key === cardKey) return;
    const wasOpen = !!cardKey, had = doc.activeElement && doc.activeElement.dataset ? doc.activeElement.dataset.intent : null;
    cardKey = key;
    clearTimeout(outTimer);
    if (!m) {
      modal.classList.remove('pc-in');
      if (modal.contains(doc.activeElement)) doc.activeElement.blur();
      const hide = () => { modal.hidden = true; if (card) card.remove(); card = null; };
      if (motion === 'off') hide(); else { modal.classList.add('pc-out'); outTimer = setTimeout(hide, OUT_MS); }
      return;
    }
    const next = renderCard(doc, m, send);
    if (card) card.replaceWith(next); else modal.append(next);
    card = next;
    modal.hidden = false;
    modal.classList.remove('pc-out');
    if (!wasOpen) pulse(modal, 'pc-in'); else modal.classList.remove('pc-in');
    const bs = buttons();
    // While pending the card itself holds focus (a stray Enter must not keep looking); after, back to where it was or the action.
    (m.pending ? card : bs.find((b) => b.dataset.intent === had) || bs.find((b) => b.classList.contains('pc-main')) || bs[0] || card).focus();
  }
  // Keys inside the card stay in the card: Esc keeps looking, Tab and arrows walk its buttons, Enter and Space press.
  modal.addEventListener('keydown', (e) => {
    e.stopPropagation();
    if (e.key === 'Escape' || e.key === 'Backspace') { e.preventDefault(); send({ type: 'back' }); return; }
    const step = { Tab: e.shiftKey ? -1 : 1, ArrowLeft: -1, ArrowUp: -1, ArrowRight: 1, ArrowDown: 1 }[e.key];
    if (!step) return;
    e.preventDefault();
    const bs = buttons();
    if (!bs.length) { if (card) card.focus(); return; }
    bs[(bs.indexOf(doc.activeElement) + step + bs.length) % bs.length].focus();
  });

  function fly(pos) {
    const n = gentle || motion === 'off' ? 0 : motion === 'less' ? 3 : 7;
    if (!n || typeof fx.animate !== 'function') return;
    const box = hud.getBoundingClientRect(), w = wallet.getBoundingClientRect();
    const end = pos || (() => { const p = peek.getBoundingClientRect(); return { x: p.left - box.left + p.width / 2, y: p.top - box.top + 24 }; })();
    const x0 = w.left - box.left + 18, y0 = w.top - box.top + w.height / 2;
    for (let i = 0; i < n; i++) {
      const s = el('i', 'pc-star pc-fly');
      fx.append(s);
      const mx = (x0 + end.x) / 2 + (i - n / 2) * 26, my = Math.min(y0, end.y) - 60 - (i % 3) * 18;
      const a = s.animate([
        { transform: `translate(${x0}px,${y0}px) scale(0)`, opacity: 0 },
        { transform: `translate(${x0}px,${y0}px) scale(1)`, opacity: 1, offset: 0.12 },
        { transform: `translate(${mx}px,${my}px) scale(1.3) rotate(90deg)`, opacity: 1, offset: 0.55 },
        { transform: `translate(${end.x}px,${end.y}px) scale(0.2) rotate(180deg)`, opacity: 0 },
      ], { duration: 560 + i * 45, delay: i * 30, easing: 'cubic-bezier(.45,0,.3,1)', fill: 'both' });
      a.onfinish = () => s.remove();
    }
  }

  return {
    el: hud, previewSlot,
    /** The bands (px from the HUD's top and bottom) a phone keeps the shelf out of: under the tabs, over the preview strip. */
    insets() {
      const box = hud.getBoundingClientRect(), above = (tabs.hidden ? top : tabs).getBoundingClientRect();
      if (!peek.hidden) peekBand = Math.max(0, box.bottom - peek.getBoundingClientRect().top);
      return { top: Math.max(0, above.bottom - box.top), bottom: peekBand };
    },
    paint(v) {
      if (!counting) setN(v.sp);
      const wk = weekNo(v.week);
      week.hidden = !wk;
      if (wk) week.textContent = t('shop_week', wk);
      status.textContent = STATUS[v.status] ? t(STATUS[v.status]) : '';
      status.hidden = !STATUS[v.status];
      tabs.hidden = !v.tabs.length;
      paintGate(v.status === 'signed_out');
      wallet.hidden = v.status === 'signed_out';
      paintTabs(v);
      paintPeek(v);
      paintList(v);
      paintCard(v);
    },
    /** A receipt landed: the chip counts down to `sp` while a few sparkles fly to `pos` (the item on screen). */
    receipt(sp, pos) {
      if (counting) counting.stop();
      const run = countDown(shown, sp, { ms: motion === 'off' ? 0 : motion === 'less' ? 450 : 700, onStep: setN });
      counting = run;
      pulse(wallet, 'pc-hit');
      fly(pos);
      return run.done.then(() => { if (counting === run) counting = null; wallet.classList.remove('pc-hit'); });
    },
    dispose() { clearTimeout(outTimer); clearTimeout(gateTimer); if (counting) counting.stop(); hud.remove(); },
  };
}
