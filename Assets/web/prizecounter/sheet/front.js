/* sheet/front.js - the Featured tab, drawn as the owner's mockup (C:/wt-shop/ref/prize-counter-v4.html renderFront):
 * the spotlight for "New this week" (a lit stage, the big name, its tag, one "Try it on", "Also new" chips), "Gifts and
 * stamps" (the gift box, the paper stamp card with its tear-off coupon), "Saving for" beside the till receipt "Earned and
 * spent", then "You can buy now". sheet/model.js frontModel says what; every part whose wire field is absent is left out.
 * Buttons carry onpress like the rest of the sheet; on.gift / on.stamp / on.view are sheet/main.js's. */

import { frontModel, labelOf, statusOf, ledgerText, badgeCount, sectionShowsKey } from './model.js';
import { placesOf, isHere } from './wire.js';
import { SIGN_TIPS, tipText } from './explain.js';

/** The front signs that carry a "?" about the shop's own mechanics (sheet/explain.js HELP), and the one that is hover only. */
const HELP_OF = { gift: 'gift', save: 'saving', can: 'can' };

/* Scissors on the coupon's cut line (decorative, currentColor). */
const SCISSORS = '<svg viewBox="0 0 16 16" aria-hidden="true"><circle cx="4" cy="12" r="2.4" fill="none" stroke="currentColor" stroke-width="1.7"/><circle cx="12" cy="12" r="2.4" fill="none" stroke="currentColor" stroke-width="1.7"/><path d="M5.7 10.1L12.2 1.6M10.3 10.1L3.8 1.6" stroke="currentColor" stroke-width="1.7" stroke-linecap="round"/></svg>';

/** The gift's next day: an ISO date reads as its weekday in the host's language, plain words stay as sent. */
export function nextDay(next, locale, t) {
  if (!next) return t('shop_sheet_gift_next');
  if (!/^\d{4}-\d{2}-\d{2}/.test(next)) return next;
  const d = new Date(next);
  try { return isNaN(d) ? t('shop_sheet_gift_next') : new Intl.DateTimeFormat(locale || 'en', { weekday: 'long', timeZone: 'UTC' }).format(d); } catch (e) { return t('shop_sheet_gift_next'); }
}

export function installFront(view, { t, locale }) {
  const { el, button, star } = view;
  const head = (title, meta, tone) => {
    const h = el('div', 'sh' + (tone ? ' tone-' + tone : ''));   // tone = the sign's candy colour (sheet.css .tone-*)
    const sign = el('h2', null, title);
    h.append(el('span', 'sq'), sign);
    if (HELP_OF[tone] && !view.tips.touch()) h.append(view.helpMark(HELP_OF[tone]));
    else if (SIGN_TIPS[tone]) view.tips.bind(sign, t(SIGN_TIPS[tone]), { focus: false });
    h.append(el('span', 'rule'));
    if (meta) h.append(el('span', 'meta', meta));
    return h;
  };
  const ctx = (v) => ({ sp: v.sp, tier: v.tier, wearing: v.wearing, pv: v.pv, platform: v.platform, hereOnly: v.hereOnly, spot: v.spot });
  const here = (v, e) => isHere(placesOf(v.pv, e.id), v.platform);
  const where = (e) => (e.x.special ? t('shop_sheet_twotone_where') : sectionShowsKey(e.fx || e.tab) ? t(sectionShowsKey(e.fx || e.tab)) + '.' : '');

  function spotlight(f, v, on) {
    const e = f.spot, s = el('section', 'spot fam-' + (e.fx || e.tab));
    s.setAttribute('aria-labelledby', 'spot-h');
    // the stage under a striped awning, a marquee of bulbs along its lip (sheet-juice keys on .bulb)
    const box = el('div', 'stage-box'), lip = el('div', 'marquee');
    for (let i = 0; i < 13; i++) lip.append(el('span', 'bulb'));
    lip.setAttribute('aria-hidden', 'true');
    const awning = el('div', 'awning');
    awning.setAttribute('aria-hidden', 'true');
    box.append(awning, view.stage(e, v), lip);
    s.append(box);
    const copy = el('div', 'spot-copy'), h = el('h2', null, labelOf(e, t));
    h.id = 'spot-h';
    copy.append(el('p', 'kick', t('shop_sheet_new_week')), h);
    if (where(e)) copy.append(el('p', 'spot-where', where(e)));
    if (!here(v, e)) copy.append(view.placeMark(placesOf(v.pv, e.id), true));
    const buy = el('div', 'spot-buy'), trying = v.pick === e.id;
    buy.append(view.statusNode(statusOf(e, ctx(v)), v, true));
    const go = button('btn', t(trying ? 'shop_sheet_trying' : 'shop_sheet_try_on'), 'spot:' + e.id, () => on.pick(e.id));
    go.disabled = trying;
    buy.append(go);
    copy.append(buy);
    if (f.also.length) {
      const also = el('div', 'also');
      also.append(el('span', null, t('shop_sheet_also_new')));
      for (const o of f.also) {
        const m = button('mini fam-' + (o.fx || o.tab), null, 'also:' + o.id, () => on.spotlight(o.id));
        m.append(view.mini(o, v), el('span', null, labelOf(o, t)));
        view.tips.bind(m, tipText(o, t));
        also.append(m);
      }
      copy.append(also);
    }
    s.append(copy);
    return s;
  }

  function giftBlock(g, on) {
    const box = el('div', 'gift' + (g.ready ? '' : ' opened')), art = el('div', 'gbox'), lid = el('i', 'gl');
    lid.append(el('i', 'gw'), el('i', 'gw'));
    art.append(lid, el('i', 'gb'), el('i', 'gt'));
    art.setAttribute('aria-hidden', 'true');
    view.giftBox = art;
    const b = el('div');
    b.append(el('h3', null, t('shop_sheet_gift')), el('p', null, g.ready ? t('shop_sheet_gift_ready') : t('shop_sheet_gift_opened', nextDay(g.next, locale, t))));
    const btn = button('btn gold', null, 'gift', (viaKey) => on.gift(viaKey));
    if (g.ready) btn.append(el('span', null, t('shop_sheet_gift_open', g.amount)), star());
    else btn.textContent = t('shop_sheet_gift_done');
    btn.disabled = !g.ready;
    b.append(btn);
    box.append(art, b);
    return box;
  }

  /* The one stamp card (one per account, 14 days from the first stamp). Once it is over (s.done) it stays as a finished
   * card with its stamps and no Stamp button while an earned coupon waits; sheet/model.js frontModel drops it after. */
  function stampCard(s, v, on) {
    const done = s.done, stamped = s.stamped.includes(s.day), c = el('div', 'scard on-paper' + (done ? ' done' : '')), h = el('div', 'scard-h');
    h.append(el('b', null, t('shop_sheet_stamp_card')));
    if (!view.tips.touch()) h.append(view.helpMark('stamps'));
    h.append(el('span', null, t(done ? 'shop_sheet_stamp_finished' : 'shop_sheet_stamp_days')));
    const dots = el('div', 'dots');
    for (let d = 1; d <= 14; d++) {
      const on1 = s.stamped.includes(d), today = !done && d === s.day, sp = el('span', on1 ? 'on' + (v.freshStamp === d ? ' fresh' : '') : today ? 'today' : null);
      if (on1) sp.append(el('i')); else sp.textContent = String(d);
      sp.setAttribute('role', 'img');
      sp.setAttribute('aria-label', t(on1 ? 'shop_sheet_stamp_stamped' : today ? 'shop_sheet_stamp_today' : 'shop_sheet_stamp_day', d));
      dots.append(sp);
    }
    const n = s.stamped.length, price = v.pv && v.pv.couponPrice;
    const cp = el('div', 'coupon' + (s.couponReady ? ' ready' : s.couponUsed ? ' used' : ''));
    const snip = el('span', 'snip');
    snip.innerHTML = SCISSORS; snip.setAttribute('aria-hidden', 'true');
    cp.append(snip, el('b', null, t('shop_sheet_coupon')), el('span', null, price ? t('shop_sheet_coupon_terms', s.couponAt, price) : t('shop_sheet_coupon_terms_any', s.couponAt)),
      el('em', null, s.couponReady ? t('shop_sheet_coupon_ready') : s.couponUsed ? t('shop_sheet_coupon_used') : t('shop_sheet_coupon_count', Math.min(n, s.couponAt), s.couponAt)));
    const foot = el('div', 'scard-f');
    if (done) foot.append(el('p', 'scard-over', t(s.couponReady ? 'shop_sheet_stamp_over_keep' : 'shop_sheet_stamp_over', n)));
    else {
      const btn = button('btn ink', t(stamped ? 'shop_sheet_stamped_today' : 'shop_sheet_stamp_go', s.day), 'stamp', (viaKey) => on.stamp(viaKey));
      btn.disabled = stamped;
      foot.append(btn);
    }
    c.append(h, dots, cp, foot);
    return c;
  }

  function savingFor(f, v, on) {
    const s = el('section', 'sec save-sec');
    s.append(head(t('shop_sheet_saving_for'), f.goal ? t('shop_sheet_saving_meta') : null, 'save'));
    if (!f.goal) { s.append(el('p', 'empty', t('shop_sheet_saving_none'))); return s; }
    const g = f.goal, row = el('div', 'save'), b = el('div', 'save-b');
    row.append(view.itemNode(g.entry, v, on, true));
    b.append(el('p', 'save-t', labelOf(g.entry, t)));
    const m = el('div', 'meter'), mi = el('i');
    mi.setAttribute('style', 'width:' + Math.min(100, Math.round(g.have / Math.max(1, g.price) * 100)) + '%');
    m.append(mi); m.setAttribute('role', 'img'); m.setAttribute('aria-label', t('shop_sheet_saved_of', g.have, g.price));
    const n = el('div', 'save-n');
    n.append(el('span', null, `${g.have} / ${g.price}`), el('b', null, g.left ? t('shop_sheet_to_go', g.left) : t('shop_sheet_saved_ready')));
    b.append(m, n, button('btn line', t('shop_sheet_view_item'), 'goal-view', () => on.pick(g.entry.id)));
    row.append(b);
    s.append(row);
    return s;
  }

  function receipt(f, v) {
    const s = el('section', 'sec'), r = el('div', 'rcpt');
    s.append(head(t('shop_sheet_receipt'), null, 'rcpt'));
    const line = (text, n, cls) => { const d = el('div', cls); d.append(el('span', null, text), el('i'), el('b', n > 0 && !cls ? 'pos' : null, cls ? String(n) : (n > 0 ? '+' : '') + n)); return d; };
    for (const l of f.ledger) r.append(line(ledgerText(v.state, v.pv, t, l), l.n));
    r.append(line(t('shop_sheet_in_wallet'), v.sp, 'sum'));
    s.append(r);
    return s;
  }

  view.front = (v, on) => {
    const f = frontModel(v.state, ctx(v)), out = [];
    if (f.hostNote) out.push(...view.hostNote(v));
    if (f.spot) out.push(spotlight(f, v, on));
    if (f.gift || f.stamps) {
      const s = el('section', 'sec'), row = el('div', 'today');
      s.append(head(t('shop_sheet_gifts_stamps'), badgeCount(v.state, v.pv, v.sp) ? null : t('shop_sheet_nothing_waiting'), 'gift'));
      if (f.gift) row.append(giftBlock(f.gift, on));
      if (f.stamps) row.append(stampCard(f.stamps, v, on));
      if (!f.gift || !f.stamps) row.classList.add('one');
      s.append(row);
      out.push(s);
    }
    if (f.goal !== undefined || f.ledger) {
      const pair = el('div', 'pair');
      if (f.goal !== undefined) pair.append(savingFor(f, v, on));
      if (f.ledger) pair.append(receipt(f, v));
      out.push(pair);
    }
    if (f.can.length) {
      const s = el('section', 'sec'), g = el('div', 'grid');
      s.append(head(t('shop_sheet_can_buy'), t('shop_sheet_can_buy_meta', v.sp), 'can'));
      g.append(...f.can.map((e) => view.itemNode(e, v, on, false, true)));
      s.append(g);
      out.push(s);
    }
    return out;
  };
}
