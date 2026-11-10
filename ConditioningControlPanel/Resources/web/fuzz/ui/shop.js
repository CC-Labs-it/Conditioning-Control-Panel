/* ui/shop.js - the goal tile and the rolling window of square tiles. Sim decides what is on offer; this file keeps the DOM
 * stable: tiles are keyed by id, only numbers change between redraws, and a bought tile leaves before the next slides in.
 * A tile shows its icon and price. Its name and description stay in it, hidden, for unlocks to rewrite; the tip shows them.
 * A mouse hovers to read and clicks to buy. A finger taps to read and taps again to buy. */

import { BY_ID, FORKS } from '../data/items.js';
import { brandText } from '../data/brand.js';
import { setRich } from './rich-text.js';
import { CONFIG } from '../data/config.js';
import { whole, fmt } from '../sim/format.js';
import { iconFor } from './icons.js';
import { createTip } from './shop-tip.js';
import { hueClass } from './palette.js';
import { goalState } from './purchase-feel.js';
import { ridersFor } from '../sim/economy.js';
import { SHOP_READY_CSS, hop, nextNudge, readyStep, NUDGE_GAP_MS } from './shop-ready.js';
import { offerBadge, paintBadges, repeatBadgeCss } from './repeat-badge.js';
import { devicePlatform, offeredWindow } from './platform.js';
import { NEXT_MAX, SECRET, ghostsFor, stripKey } from './shop-next-model.js';

const setText = (el, text) => { if (el.textContent !== text) el.textContent = text; };

/* A bundle shows what all of it pays together. */
const sum = (list, k) => list.reduce((t, x) => t + (x[k] || 0), 0);
const payText = (list) => { const f = sum(list, 'fps'), c = sum(list, 'click'), w = sum(list, 'wall'); return f ? `+${fmt(f)}/s` : c ? `+${fmt(c)} a tap` : w ? `+${fmt(w)} a wall` : ''; };

/* These two open a card that already explains them, so a finger goes straight there. */
const opensCard = (it) => it.kind === 'fork' || it.kind === 'end';

const cursorSamples = {
  trail: 'trails/trail-1.webp',
  starburst: 'trails/starburst-1.webp',
  word_comet: 'trails/word_comet-1.webp',
  cursor: 'cursors/cursor.png',
  hijack: 'cursors/hijack.png',
};

function tileArt(doc, item) {
  const sample = cursorSamples[item.id];
  if (!sample) {
    // The wrap holds a repeat sticker (ui/repeat-badge.js) at the icon's bottom right; the icon itself is unchanged.
    const wrap = doc.createElement('span');
    wrap.className = 'rep-ico';
    wrap.appendChild(iconFor(doc, item));
    return wrap;
  }
  const img = doc.createElement('img');
  img.className = 'ico shop-sample';
  img.alt = '';
  img.draggable = false;
  img.src = 'assets/shop/' + sample;
  return img;
}

const TILE_GAP = 12;   // TILE_MIN and the goal's width follow styles.css
const GHOST = { wide: 60, narrow: 48 };   // the Next up tiles' most height, as #next .ghost in styles.css

/** The live shop, for the Guide map's Buy button (ui/guide-map.js): what the shop offers right now, and the same press a
 *  tile makes (terms card, guards, price check, purchase feel all live behind it in ui/app.js press()). Null before a shop. */
export let shopDoor = null;

export function createShop({ doc, engine, onPress, onReady = () => {} }) {
  const box = doc.getElementById('shop'), goalEl = doc.getElementById('goal'), rowsEl = doc.getElementById('rows'), moreEl = doc.getElementById('more');
  const rows = new Map();
  let goalRow = null, size = 6, lastHop = -Infinity;
  /* A guest tile (EMI Takeover): one element another module owns, kept last in the grid. Never an engine row. */
  let guestEl = null;
  if (!doc.getElementById('shop-ready-css')) {
    const st = doc.createElement('style'); st.id = 'shop-ready-css'; st.textContent = SHOP_READY_CSS; doc.head.appendChild(st);
  }
  repeatBadgeCss(doc);
  const now = () => doc.defaultView.performance.now();
  /* Rows this device cannot use (ui/platform.js) never get a tile; the next rows move up. */
  const platform = devicePlatform(doc.defaultView, () => engine.env?.caps || {});
  const offered = (n) => offeredWindow((k) => engine.shop(k), n, platform.canOffer);
  /* Hops wait for a still page: not under Gentle or reduced motion (body.gentle covers both), a pause, or a hidden tab. */
  const moving = () => !doc.body.classList.contains('gentle') && !engine.paused && !engine.stopped && !engine.suspended && doc.visibilityState !== 'hidden';

  const tip = createTip({
    doc,
    buyLabel: (r) => r.ghost ? '' : r.waiting ? 'Wait for EMI' : (engine.funds(r.item.id) < engine.price(r.item.id) ? `${whole(engine.price(r.item.id) - engine.funds(r.item.id))} short` : 'Buy'),
    onBuy: (r) => { if (r.ghost) return; onPress(r.item.id, r.el); tip.hide(); },
  });

  /* Next up (owner go 2026-11-10): one row of ghost tiles under the shop, what comes after the tiles on show. Read only:
   * no price, no buy; a mouse hovers and a finger taps to read the card. A fork or ending keeps its secret. */
  let nextEl = doc.getElementById('next'), nextKey = '';
  if (!nextEl) {
    nextEl = doc.createElement('div');
    nextEl.id = 'next';
    nextEl.hidden = true;
    nextEl.setAttribute('aria-label', 'Next up');
    box.insertBefore(nextEl, moreEl);
  }
  const nextLabel = doc.createElement('span');
  nextLabel.className = 'nx-label';
  nextLabel.textContent = 'next';
  nextEl.replaceChildren(nextLabel);

  function makeGhost(g) {
    const el = doc.createElement('button');
    el.type = 'button';
    el.className = ['row', 'ghost', 'nx-' + g.kind, g.kind === 'item' ? hueClass(g.item.lane) : ''].filter(Boolean).join(' ');
    el.dataset.next = g.key;
    const secret = SECRET[g.kind];
    let art;
    if (g.kind === 'item') art = tileArt(doc, g.item);
    else if (g.kind === 'fork') art = iconFor(doc, { id: g.key, iconId: 'f1' });
    else { art = doc.createElement('span'); art.className = 'ico nx-q'; art.setAttribute('aria-hidden', 'true'); art.textContent = '?'; }
    const body = doc.createElement('span');
    body.className = 'body';
    const name = doc.createElement('b');
    name.textContent = secret ? secret.name : brandText(g.item.name);
    const what = doc.createElement('small');
    setRich(what, secret ? secret.what : g.item.what);
    body.append(name, what);
    el.append(art, body);
    el.setAttribute('aria-label', 'Next: ' + name.textContent);
    const r = { el, ghost: true, item: secret ? { id: g.key } : g.item };
    let pointer = '';
    el.addEventListener('pointerdown', (e) => { pointer = e.pointerType; });
    el.addEventListener('pointerenter', (e) => { if (e.pointerType !== 'touch') tip.show(r, false); });
    el.addEventListener('pointerleave', (e) => { if (e.pointerType !== 'touch' && tip.row === r) tip.hide(); });
    el.addEventListener('focus', () => { if (el.matches(':focus-visible') && !tip.row) tip.show(r, false); });
    el.addEventListener('blur', () => { if (tip.row === r && !tip.touch) tip.hide(); });
    el.addEventListener('click', () => {
      const finger = pointer === 'touch';
      pointer = '';
      if (!finger) return;
      if (tip.row === r) tip.hide(); else tip.show(r, true);
    });
    return el;
  }

  /** Rebuilt only when what it shows changes; hiding or showing it refits the shop (it takes a row of room). */
  function drawNext(win) {
    const shown = new Set([win.goal?.id, ...win.rows.map((it) => it.id)].filter(Boolean));
    const list = (engine.peek?.(NEXT_MAX + 2, size) || []).filter((it) => it && !shown.has(it.id));
    const ghosts = ghostsFor(list, NEXT_MAX, platform.canOffer);
    const key = stripKey(ghosts);
    if (key !== nextKey) {
      nextKey = key;
      nextEl.replaceChildren(nextLabel, ...ghosts.map(makeGhost));
    }
    if (nextEl.hidden !== !ghosts.length) { nextEl.hidden = !ghosts.length; fit(); }
  }

  function makeRow(item, isGoal) {
    const el = doc.createElement('button');
    el.type = 'button';
    el.className = `row in ${hueClass(item.lane)}`.trim();
    el.dataset.id = item.id;
    const body = doc.createElement('span');
    body.className = 'body';
    const tag = isGoal ? doc.createElement('span') : null;
    if (tag) tag.className = 'tag';
    const name = doc.createElement('b');
    name.textContent = brandText(item.name);
    const what = doc.createElement('small');
    setRich(what, item.kind === 'fork' ? FORKS[item.id].ask : item.what);
    const rep = doc.createElement('em');
    rep.className = 'rep';
    body.append(name, rep, what);
    const riders = ridersFor(engine.run, item.id).map((id) => BY_ID[id]);
    if (riders.length) {
      const also = doc.createElement('em');
      also.className = 'also';
      also.textContent = `Comes with ${riders.map((r) => brandText(r.name)).join(', ')}`;
      body.appendChild(also);
      el.classList.add('bundle');
    }
    const cost = doc.createElement('span');
    cost.className = 'cost';
    const costNum = doc.createElement('span');
    const pay = doc.createElement('i');
    pay.textContent = item.options ? 'Specialty' : item.rank ? 'Rank ' + (item.rank+1) : payText([item, ...riders]);
    cost.append(costNum, pay);
    const bar = doc.createElement('span');
    bar.className = 'bar';
    bar.setAttribute('aria-hidden', 'true');
    el.appendChild(bar);
    if (tag) el.appendChild(tag);
    const art = tileArt(doc, item);
    el.append(art, body, cost);
    el.setAttribute('aria-label', name.textContent);
    const r = { el, costNum, tag, bar: el.querySelector('.bar'), item, art: art.classList.contains('rep-ico') ? art : null, rep };
    let pointer = '';
    el.addEventListener('pointerdown', (e) => { pointer = e.pointerType; });
    el.addEventListener('pointerenter', (e) => { if (e.pointerType !== 'touch') tip.show(r, false); });
    el.addEventListener('pointerleave', (e) => { if (e.pointerType !== 'touch' && tip.row === r) tip.hide(); });
    el.addEventListener('focus', () => { if (el.matches(':focus-visible') && !tip.row) tip.show(r, false); });
    el.addEventListener('blur', () => { if (tip.row === r && !tip.touch) tip.hide(); });
    el.addEventListener('click', () => {
      const finger = pointer === 'touch';
      pointer = '';
      if (finger && !opensCard(item) && !(tip.row === r && tip.touch)) { tip.show(r, true); return; }
      const held = tip.row === r && tip.touch && !el.classList.contains('short');
      if (opensCard(item) || held) tip.hide();
      onPress(item.id, el);
    });
    return r;
  }

  function paint(r, isGoal) {
    const run = engine.run, it = r.item;
    const p = engine.price(it.id);
    const funds = engine.funds(it.id);
    const waiting = engine.purchaseHint(it.id);
    const short = funds < p || !!waiting;
    if ((r.waiting || '') !== waiting) {
      setRich(r.el.querySelector('small'), waiting || it.what);
      r.waiting = waiting;
    }
    r.el.classList.toggle('short', short);
    const badge = offerBadge(it, run);
    paintBadges(r.art, badge);
    setText(r.rep, badge ? badge.label : '');
    const fork = it.kind === 'fork';
    setText(r.costNum, waiting ? 'Wait for EMI' : fork ? (short ? `at ${whole(p)}` : 'Choose') : whole(p));
    const fill = `scaleY(${Math.max(0, Math.min(1, funds / Math.max(1, p))).toFixed(3)})`;
    if (r.fill !== fill) { r.fill = fill; r.bar.style.transform = fill; }
    const step = readyStep(r.buyable, !short);
    r.buyable = !short;
    r.el.classList.toggle('buyable', !short);
    if (step === 'jump') {
      r.nextHop = nextNudge(now());
      if (moving()) { hop(r.el, 'jump'); lastHop = now(); }
    } else if (step === 'arm') r.nextHop = nextNudge(now());
    if (isGoal) {
      setText(r.tag, fork ? 'Your choice' : it.kind === 'end' ? 'The end' : short ? 'Next' : 'Ready');
      r.el.classList.toggle('ready', !short);
      r.el.classList.toggle('near', short && funds / Math.max(1, p) >= .75);
      const state = goalState(r.affordable, !short, r.announced);
      r.affordable = state.affordable;
      r.announced = state.announced;
      if (state.announce) onReady(r.el);
    }
  }

  function draw() {
    const run = engine.run;
    if (run.ended) { tip.hide(); box.hidden = true; return; }
    if (!run.taps && !run.total && run.buys === 0) { box.hidden = true; return; }
    box.hidden = false;
    const win = offered(size);

    if (goalRow?.item !== win.goal) { goalRow?.el.remove(); goalRow = null; }
    if (win.goal && !goalRow) { goalRow = makeRow(win.goal, true); goalEl.appendChild(goalRow.el); }
    if (goalRow) paint(goalRow, true);

    const want = new Set(win.rows.map((i) => i.id));
    for (const [id, r] of rows) if (!want.has(id)) { r.el.remove(); rows.delete(id); }
    win.rows.forEach((item, i) => {
      let r = rows.get(item.id);
      if (!r) { r = makeRow(item, false); rows.set(item.id, r); }
      if (rowsEl.children[i] !== r.el) rowsEl.insertBefore(r.el, rowsEl.children[i] || null);
      paint(r, false);
    });
    if (guestEl && rowsEl.lastElementChild !== guestEl) rowsEl.appendChild(guestEl);
    drawNext(win);
    nudge();
    if (moreEl.hidden !== !win.more) moreEl.hidden = !win.more;
    if (win.more) setText(moreEl, `${win.more} more waiting behind these`);
    tip.refresh();
  }

  /** Buyable tiles that stay unbought hop again every 6 to 8 seconds, at most one tile per NUDGE_GAP_MS. */
  function nudge() {
    const colour = (engine.run.owned.colour && engine.switches.isOn('colour')) || engine.run.picks?.f5;
    box.classList.toggle('shop-grey', !colour);
    if (!moving()) return;
    const t = now();
    if (t - lastHop < NUDGE_GAP_MS) return;
    for (const r of [goalRow, ...rows.values()]) {
      if (!r?.buyable || !(t >= r.nextHop) || r.el.matches(':active')) continue;
      hop(r.el, 'nudge');
      lastHop = t;
      r.nextHop = nextNudge(t);
      return;
    }
  }

  /** How many tiles fit, from the window alone: the shop may still be hidden. Called on resize. */
  function fit() {
    const w = doc.defaultView.innerWidth, wide = w >= 760;
    const room = Math.min(760, w - 32) - (wide ? 118 : 96) - TILE_GAP;
    const cols = Math.max(1, Math.floor((room + TILE_GAP) / ((wide ? 84 : 68) + TILE_GAP)));
    const tile = (room - (cols - 1) * TILE_GAP) / cols;
    const strip = nextEl.hidden ? 0 : (wide ? GHOST.wide : GHOST.narrow) + TILE_GAP;   // the Next up row
    const avail = box.parentElement.clientHeight - box.offsetTop - 44 - strip;   // the shop starts 16px higher with 16px more top padding, room for a hop (ui/shop-ready.js)
    size = Math.max(CONFIG.SHOP_MIN, Math.min(6, Math.floor(avail / (tile + TILE_GAP)) * cols));
  }

  function denied(id) {
    const r = rows.get(id) || (goalRow?.item.id === id ? goalRow : null);
    if (!r) return;
    r.el.classList.remove('no');
    void r.el.offsetWidth;
    r.el.classList.add('no');
  }

  function flush() { tip.hide(); nextKey = ''; nextEl.replaceChildren(nextLabel); nextEl.hidden = true; goalRow?.el.remove(); goalRow = null; for (const r of rows.values()) r.el.remove(); rows.clear(); }

  shopDoor = {
    /** True when the shop would sell this row right now: open, on a tile or waiting behind them, and usable here. */
    offers(id) { if (engine.run.ended) return false; const w = offered(999); return w.goal?.id === id || w.rows.some((it) => it.id === id); },
    price: (id) => engine.price(id),
    funds: (id) => engine.funds(id),
    platform,
    /** The press a tile makes. `el` is where the purchase flies from. */
    press(id, el) { tip.hide(); onPress(id, el); },
    /** Show one guest tile after the rows (or none). The caller owns the element and its presses. */
    guest(el) { if (guestEl !== el) guestEl?.remove(); guestEl = el || null; if (guestEl && rowsEl.lastElementChild !== guestEl) rowsEl.appendChild(guestEl); },
  };

  return { draw, fit, denied, flush, closeTip: tip.hide, get size() { return size; } };
}
