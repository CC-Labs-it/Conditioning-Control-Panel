/* ui/shop.js - the goal row and the rolling window. Sim decides what is on offer; this file keeps the DOM stable: rows
 * are keyed by id, only numbers change between redraws, and a bought row leaves before the next slides in. */

import { BY_ID, FORKS } from '../data/items.js';
import { brandText } from '../data/brand.js';
import { CONFIG } from '../data/config.js';
import { whole, fmt } from '../sim/format.js';
import { iconFor } from './icons.js';

const setText = (el, text) => { if (el.textContent !== text) el.textContent = text; };

const payText = (it) => (it.fps ? `+${fmt(it.fps)}/s` : it.click ? `+${fmt(it.click)} a tap` : it.wall ? `+${fmt(it.wall)} a wall` : '');

export function createShop({ doc, engine, onPress }) {
  const box = doc.getElementById('shop'), goalEl = doc.getElementById('goal'), rowsEl = doc.getElementById('rows'), moreEl = doc.getElementById('more');
  const rows = new Map();
  let goalRow = null, size = 6;

  function makeRow(item, isGoal) {
    const el = doc.createElement('button');
    el.type = 'button';
    el.className = 'row in';
    el.dataset.id = item.id;
    const body = doc.createElement('span');
    body.className = 'body';
    const tag = isGoal ? doc.createElement('span') : null;
    if (tag) { tag.className = 'tag'; body.appendChild(tag); }
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
    if (isGoal) { const bar = doc.createElement('span'); bar.className = 'bar'; el.appendChild(bar); }
    el.append(iconFor(doc, item), body, cost);
    el.addEventListener('click', () => onPress(item.id, el));
    return { el, costNum, tag, bar: el.querySelector('.bar'), item };
  }

  function paint(r, isGoal) {
    const run = engine.run, it = r.item;
    const p = engine.price(it.id);
    const short = run.fuzz < p;
    r.el.classList.toggle('short', short);
    const fork = it.kind === 'fork';
    setText(r.costNum, fork ? (short ? `at ${whole(p)}` : 'Choose') : whole(p));
    if (isGoal) {
      setText(r.tag, fork ? 'Your choice' : it.kind === 'end' ? 'The end' : 'Next');
      const fill = `scaleX(${(short ? Math.min(1, run.fuzz / Math.max(1, p)) : 1).toFixed(3)})`;
      if (r.fill !== fill) { r.fill = fill; r.bar.style.transform = fill; }
      r.el.classList.toggle('ready', !short);
    }
  }

  function draw() {
    const run = engine.run;
    if (run.ended) { box.hidden = true; return; }
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
  }

  /** How many rows fit. Called on resize. */
  function fit() {
    const cols = doc.defaultView.innerWidth >= 760 ? 2 : 1;
    const avail = box.parentElement.clientHeight - box.offsetTop - (goalRow?.el.offsetHeight || 64) - 40;
    const fitN = Math.floor(avail / 62) * cols;
    size = Math.max(CONFIG.SHOP_MIN, Math.min(cols === 2 ? 6 : 5, fitN));
  }

  function denied(id) {
    const r = rows.get(id) || (goalRow?.item === BY_ID[id] ? goalRow : null);
    if (!r) return;
    r.el.classList.remove('no');
    void r.el.offsetWidth;
    r.el.classList.add('no');
  }

  function flush() { goalRow?.el.remove(); goalRow = null; for (const r of rows.values()) r.el.remove(); rows.clear(); }

  return { draw, fit, denied, flush, get size() { return size; } };
}
