/* ui/shop.js - the goal tile and the rolling window of square tiles. Sim decides what is on offer; this file keeps the DOM
 * stable: tiles are keyed by id, only numbers change between redraws, and a bought tile leaves before the next slides in.
 * A tile shows its icon and price. Its name and description stay in it, hidden, for unlocks to rewrite; the tip shows them.
 * A mouse hovers to read and clicks to buy. A finger taps to read and taps again to buy. */

import { BY_ID, FORKS } from '../data/items.js';
import { brandText } from '../data/brand.js';
import { CONFIG } from '../data/config.js';
import { whole, fmt } from '../sim/format.js';
import { iconFor } from './icons.js';
import { createTip } from './shop-tip.js';
import { hueClass } from './palette.js';
import { goalState } from './purchase-feel.js';

const setText = (el, text) => { if (el.textContent !== text) el.textContent = text; };

const payText = (it) => (it.fps ? `+${fmt(it.fps)}/s` : it.click ? `+${fmt(it.click)} a tap` : it.wall ? `+${fmt(it.wall)} a wall` : '');

/* These two open a card that already explains them, so a finger goes straight there. */
const opensCard = (it) => it.kind === 'fork' || it.kind === 'end';

const TILE_GAP = 6;   // TILE_MIN and the goal's width follow styles.css

export function createShop({ doc, engine, onPress, onReady = () => {} }) {
  const box = doc.getElementById('shop'), goalEl = doc.getElementById('goal'), rowsEl = doc.getElementById('rows'), moreEl = doc.getElementById('more');
  const rows = new Map();
  let goalRow = null, size = 6;

  const tip = createTip({
    doc,
    buyLabel: (r) => (engine.run.fuzz < engine.price(r.item.id) ? `${whole(engine.price(r.item.id) - engine.run.fuzz)} short` : 'Buy'),
    onBuy: (r) => { onPress(r.item.id, r.el); tip.hide(); },
  });

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
    what.textContent = brandText(item.kind === 'fork' ? FORKS[item.id].ask : item.what);
    body.append(name, what);
    const cost = doc.createElement('span');
    cost.className = 'cost';
    const costNum = doc.createElement('span');
    const pay = doc.createElement('i');
    pay.textContent = payText(item);
    cost.append(costNum, pay);
    const bar = doc.createElement('span');
    bar.className = 'bar';
    bar.setAttribute('aria-hidden', 'true');
    el.appendChild(bar);
    if (tag) el.appendChild(tag);
    el.append(iconFor(doc, item), body, cost);
    el.setAttribute('aria-label', name.textContent);
    const r = { el, costNum, tag, bar: el.querySelector('.bar'), item };
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
    const short = run.fuzz < p;
    r.el.classList.toggle('short', short);
    const fork = it.kind === 'fork';
    setText(r.costNum, fork ? (short ? `at ${whole(p)}` : 'Choose') : whole(p));
    const fill = `scaleY(${Math.max(0, Math.min(1, run.fuzz / Math.max(1, p))).toFixed(3)})`;
    if (r.fill !== fill) { r.fill = fill; r.bar.style.transform = fill; }
    if (isGoal) {
      setText(r.tag, fork ? 'Your choice' : it.kind === 'end' ? 'The end' : short ? 'Next' : 'Ready');
      r.el.classList.toggle('ready', !short);
      r.el.classList.toggle('near', short && run.fuzz / Math.max(1, p) >= .75);
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
    const win = engine.shop(size);

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
    if (moreEl.hidden !== !win.more) moreEl.hidden = !win.more;
    if (win.more) setText(moreEl, `${win.more} more waiting behind these`);
    tip.refresh();
  }

  /** How many tiles fit, from the window alone: the shop may still be hidden. Called on resize. */
  function fit() {
    const w = doc.defaultView.innerWidth, wide = w >= 760;
    const room = Math.min(760, w - 32) - (wide ? 118 : 96) - TILE_GAP;
    const cols = Math.max(1, Math.floor((room + TILE_GAP) / ((wide ? 84 : 68) + TILE_GAP)));
    const tile = (room - (cols - 1) * TILE_GAP) / cols;
    const avail = box.parentElement.clientHeight - box.offsetTop - 28;
    size = Math.max(CONFIG.SHOP_MIN, Math.min(6, Math.floor(avail / (tile + TILE_GAP)) * cols));
  }

  function denied(id) {
    const r = rows.get(id) || (goalRow?.item === BY_ID[id] ? goalRow : null);
    if (!r) return;
    r.el.classList.remove('no');
    void r.el.offsetWidth;
    r.el.classList.add('no');
  }

  function flush() { tip.hide(); goalRow?.el.remove(); goalRow = null; for (const r of rows.values()) r.el.remove(); rows.clear(); }

  return { draw, fit, denied, flush, closeTip: tip.hide, get size() { return size; } };
}
