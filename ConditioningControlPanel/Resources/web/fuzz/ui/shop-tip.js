/* ui/shop-tip.js - the card over a shop tile: live preview, name, what it does, price. It copies the tile's own hidden text,
 * so anything an unlock does to a row's words (censored, renamed, hidden, infected) shows here too. It sits outside the
 * world and the shop, under the cards and Wake Up. A mouse only looks through it; on a touch screen it holds the Buy button. */

import { _loadDemos } from './fork.js';

const NOT_COPIED = new Set(['in', 'out', 'no', 'ready', 'peek', 'short']);
const MARGIN = 8;

const gone = (node) => {
  if (!node) return true;
  const s = getComputedStyle(node);
  return s.display === 'none' || s.visibility === 'hidden';
};

export function createTip({ doc, buyLabel, onBuy }) {
  const el = doc.createElement('div');
  el.id = 'tip';
  el.hidden = true;
  const demo = doc.createElement('div');
  demo.className = 'tip-demo';
  const words = doc.createElement('div');
  const buy = doc.createElement('button');
  buy.type = 'button';
  buy.className = 'tip-buy';
  el.append(demo, words, buy);
  doc.body.appendChild(el);

  let cur = null, touch = false, stop = null, token = 0, stamp = '';

  function place() {
    const b = cur.el.getBoundingClientRect(), win = doc.defaultView;
    const w = el.offsetWidth, h = el.offsetHeight;
    const x = Math.max(MARGIN, Math.min(win.innerWidth - w - MARGIN, b.left + b.width / 2 - w / 2));
    let y = b.top - h - MARGIN;
    if (y < MARGIN) y = b.bottom + MARGIN;
    if (y + h > win.innerHeight - MARGIN) y = Math.max(MARGIN, win.innerHeight - h - MARGIN);
    el.style.left = `${Math.round(x)}px`;
    el.style.top = `${Math.round(y)}px`;
  }

  function sync() {
    const src = cur.el;
    const parts = ['.tag', 'b', 'small', '.cost'].map((sel) => src.querySelector(sel));
    const [tag, name, what, cost] = parts;
    const next = [src.className, ...parts.map((p) => (p ? `${gone(p)}:${p.innerHTML}` : '')), buyLabel(cur)].join('|');
    if (next === stamp) return;
    stamp = next;
    el.className = `row tip${touch ? ' touch' : ''}${src.classList.contains('short') ? ' tip-short' : ''} ${[...src.classList].filter((c) => c !== 'row' && !NOT_COPIED.has(c)).join(' ')}`;
    words.replaceChildren(...[tag, name, what, cost].filter((p) => !gone(p)).map((p) => p.cloneNode(true)));
    buy.hidden = !touch;
    buy.textContent = buyLabel(cur);
    buy.disabled = src.classList.contains('short');
    place();
  }

  function startDemo(id) {
    stop?.();
    stop = null;
    /* Previews are not checked against the 3 Hz rule, so Gentle goes without them. */
    demo.hidden = doc.body.classList.contains('gentle');
    if (demo.hidden) return;
    const mine = ++token;
    _loadDemos().then((Demo) => {
      if (mine !== token || !cur) return;
      if (!Demo.has(id)) { demo.hidden = true; place(); return; }
      stop = Demo.play(demo, id);
    }).catch((e) => console.error('demos failed', e));
  }

  function show(row, viaTouch) {
    if (cur === row && touch === viaTouch) return;
    hide();
    cur = row;
    touch = viaTouch;
    row.el.classList.toggle('peek', touch);
    el.hidden = false;
    sync();
    startDemo(row.item.id);
    place();
  }

  function hide() {
    if (!cur) return;
    token += 1;
    stop?.();
    stop = null;
    demo.innerHTML = '';
    cur.el.classList.remove('peek');
    cur = null;
    stamp = '';
    el.hidden = true;
  }

  buy.addEventListener('click', () => { if (cur) onBuy(cur); });
  doc.addEventListener('pointerdown', (e) => {
    if (cur && touch && !el.contains(e.target) && !e.target.closest?.('#rows .row, #goal .row')) hide();
  }, true);

  return {
    show, hide,
    get row() { return cur; },
    get touch() { return touch; },
    /** Called from the shop's redraw: follows the tile's words and goes when the tile does. */
    refresh() {
      if (!cur) return;
      if (!cur.el.isConnected) hide();
      else sync();
    },
  };
}
