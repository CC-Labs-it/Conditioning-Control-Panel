/* card.js - the confirm card. cardModel is pure: which name, price, stamp, lines and buttons a face shows.
 * renderCard draws it with any document (the page's, or the fake one in tests). The state model owns every
 * decision: a button only sends an intent ('select' = the face's action, 'back' = keep looking). */

import { itemName, itemDesc } from '../state/strings.js';
import { STAR, LOCK } from './icons.js';
import { renderBars } from './bars.js';
import { kartBarsOf } from '../state/kart-bars.js';

const KEEP = { key: 'shop_keep_looking', intent: 'back', main: false };
const WEAR = { key: 'shop_wear', intent: 'select', main: true };
const GARAGE = { key: 'shop_wear_in_garage', intent: 'back', main: true };
/** An owned card's buttons by its wear mode (state/shop.js wearMode): no mode = the plain Wear. */
const ownedButtons = (wear) => (wear === null ? [KEEP] : wear === 'garage' ? [GARAGE] : [WEAR, KEEP]);
const FACES = {
  buy: { price: true, buttons: [{ key: 'shop_buy', intent: 'select', main: true }, KEEP] },
  owned: { stamp: 'owned', buttons: [{ key: 'shop_wear', intent: 'select', main: true }, KEEP] },
  prime: { price: true, stamp: 'prime', buttons: [KEEP] },
  short: { price: true, buttons: [KEEP] },
  soon: { buttons: [] },
  gone: { buttons: [] },
};

/** card = shop.card(); sp = the wallet now. */
export function cardModel(card, sp, t) {
  if (!card) return null;
  const f = FACES[card.face] || FACES.gone, lines = [];
  if (card.face === 'buy') lines.push({ cls: 'after', text: t('shop_after', Math.max(0, sp - card.priceSp)) });
  if (card.face === 'prime') lines.push({ cls: 'reason', text: t(card.reasonKey || 'shop_prime_reason') });
  if (card.face === 'short') lines.push({ cls: 'short', text: t('shop_short', card.short) });
  if (card.face === 'soon') lines.push({ cls: 'soon', text: t('shop_soon') });
  if (card.face === 'gone') lines.push({ cls: 'gone', text: t('shop_gone') });
  if (card.face === 'owned' && card.hintKey) lines.push({ cls: 'hint', text: t(card.hintKey) });
  const note = card.pending ? t('shop_pending') : card.noteKey ? t(card.noteKey) : null;
  return {
    face: card.face, name: itemName(t, card.itemId), desc: itemDesc(t, card.itemId), pending: !!card.pending,
    bars: kartBarsOf(card.itemId),
    price: f.price ? { text: String(card.priceSp), red: card.face === 'short', label: t('shop_price', card.priceSp) } : null,
    stamp: f.stamp ? t(f.stamp === 'owned' ? 'shop_owned' : 'shop_prime') : null, stampKind: f.stamp || null,
    lines, note,
    buttons: (card.face === 'owned' ? ownedButtons(card.wear) : f.buttons).map((b) => ({ label: t(b.key), intent: b.intent, main: b.main, disabled: !!card.pending && b.main })),
  };
}

/** press(event, button) -> false drops a click (hud/index.js: a key's synthetic click, a step too fresh to confirm). */
export function renderCard(doc, m, send, press = () => true) {
  const el = (tag, cls, text) => { const n = doc.createElement(tag); if (cls) n.className = cls; if (text != null) n.textContent = text; return n; };
  const card = el('div', `pc-card pc-face-${m.face}`);
  card.setAttribute('role', 'dialog');
  card.setAttribute('aria-modal', 'true');
  card.setAttribute('aria-labelledby', 'pc-card-name');
  card.setAttribute('aria-busy', String(m.pending));
  card.tabIndex = -1;
  const name = el('h2', 'pc-card-name', m.name);
  name.id = 'pc-card-name';
  card.append(name);
  if (m.desc) card.append(el('p', 'pc-card-desc', m.desc));
  if (m.bars) card.append(renderBars(doc, m.bars));
  if (m.stamp) {
    const s = el('span', `pc-stamp pc-stamp-${m.stampKind}`);
    if (m.stampKind === 'prime') s.innerHTML = LOCK;
    s.append(el('span', null, m.stamp));
    card.append(s);
  }
  if (m.price) {
    const p = el('p', 'pc-card-price' + (m.price.red ? ' pc-red' : ''));
    p.innerHTML = STAR;
    p.append(el('span', null, m.price.text));
    p.setAttribute('aria-label', m.price.label);
    card.append(p);
  }
  for (const l of m.lines) card.append(el('p', `pc-line pc-line-${l.cls}`, l.text));
  if (m.note) { const n = el('p', 'pc-note', m.note); n.setAttribute('role', 'status'); card.append(n); }
  if (m.buttons.length) {
    const row = el('div', 'pc-acts');
    for (const b of m.buttons) {
      const btn = el('button', 'pc-btn' + (b.main ? ' pc-main' : ''), b.label);
      btn.type = 'button';
      btn.disabled = b.disabled;
      btn.dataset.intent = b.intent;
      btn.onclick = (e) => { if (press(e, b)) send({ type: b.intent }); };
      row.append(btn);
    }
    card.append(row);
  }
  return card;
}
