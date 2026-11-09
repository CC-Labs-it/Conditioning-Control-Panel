/* ui/shop-tip.js - the card over a shop tile: live preview, name, what it does, price. It copies the tile's own hidden text,
 * so anything an unlock does to a row's words (censored, renamed, hidden, infected) shows here too. It sits outside the
 * world and the shop, under the cards and Wake Up. A mouse only looks through it; on a touch screen it holds the Buy button. */

import { _loadDemos } from './fork.js';
import { makeFace, SKIN_ITEMS } from './button-faces.js';

const NOT_COPIED = new Set(['in', 'out', 'no', 'ready', 'peek', 'short', 'buyable']);
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

  let cur = null, touch = false, stop = null, token = 0, stamp = '', goneAt = -1e9, goneKey = '';

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
    const parts = ['.tag', 'b', '.rep', 'small', '.also', '.cost'].map((sel) => src.querySelector(sel));
    const [tag, name, rep, what, also, cost] = parts;
    // The shop redraws this several times a second. Computed visibility (a style pass per part) is re-read at most
    // twice a second; class, hidden and text changes are seen at once.
    const t = doc.defaultView.performance.now();
    if (t - goneAt > 500) { goneAt = t; goneKey = parts.map((p) => (p && gone(p) ? 1 : 0)).join(''); }
    const next = [src.className, doc.body.className, goneKey, ...parts.map((p) => (p ? `${p.hidden}${p.className}:${p.innerHTML}` : '')), buyLabel(cur)].join('|');
    if (next === stamp) return;
    stamp = next;
    el.className = `row tip${touch ? ' touch' : ''}${src.classList.contains('short') ? ' tip-short' : ''} ${[...src.classList].filter((c) => c !== 'row' && !NOT_COPIED.has(c)).join(' ')}`;
    words.replaceChildren(...[tag, name, rep, what, also, cost].filter((p) => p && !gone(p)).map((p) => p.cloneNode(true)));
    buy.hidden = !touch;
    buy.textContent = buyLabel(cur);
    buy.disabled = src.classList.contains('short');
    place();
  }

  function startDemo(id) {
    stop?.();
    stop = null;
    const art = makeFace(doc,SKIN_ITEMS[id]);
    demo.classList.toggle('wardrobe-tip', Boolean(art));
    if (art) {
      demo.classList.remove('sample-still');
      demo.hidden = false;
      demo.replaceChildren(art);
      return;
    }
    const still = cur.el.querySelector('.shop-sample');
    demo.classList.toggle('sample-still', Boolean(still));
    if (still) {
      const image = still.cloneNode();
      image.className = '';
      demo.replaceChildren(image);
    }
    // Gentle can show the same still as the tile without playing the demo.
    const gentle = doc.body.classList.contains('gentle');
    demo.hidden = gentle && !still;
    if (gentle) return;
    const mine = ++token;
    _loadDemos().then((Demo) => {
      if (mine !== token || !cur) return;
      if (!Demo.has(id)) { demo.hidden = !still; place(); return; }
      demo.classList.remove('sample-still');
      demo.replaceChildren();
      stop = Demo.play(demo, id);
      place();
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
    goneAt = -1e9;
    el.hidden = true;
  }

  buy.addEventListener('click', () => { if (cur) onBuy(cur); });
  /* A press anywhere but the card and its own tile closes it, a finger's or a mouse's (a card opened by keyboard focus too).
   * A press on another tile closes this one; that tile's own click (or hover) opens its card. */
  doc.addEventListener('pointerdown', (e) => {
    if (cur && !el.contains(e.target) && !cur.el.contains(e.target)) hide();
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
