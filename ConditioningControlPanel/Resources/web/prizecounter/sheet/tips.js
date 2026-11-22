/* sheet/tips.js - the sheet's own hover tips (never the browser's native title: no double tips). One paper tip with an
 * ink outline and a small tail, shown after a short hover or on keyboard focus, anchored to the thing it describes (never
 * the mouse): above it when there is room, else below, always inside the sheet, so it never covers its own price sticker.
 * Gone on leave, blur, scroll, a press or a key. It never takes focus and never catches a click (pointer-events: none).
 * Screen readers get the same words through aria-describedby (a hidden bank of spans, rebuilt each paint).
 * Touch (a coarse pointer or phone width): nothing pops up; the fitting room shows the line under the item's name. */

export const TIP_DELAY_MS = 450, TIP_FOCUS_MS = 120, TIP_GAP = 10, TIP_EDGE = 6;
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));

/** Where a tip of size `tip` goes for a target rect `a` inside the sheet rect `box` (all viewport px):
 * { x, y } relative to the sheet, below (flipped under the target), tail (the tail's x inside the tip). Pure. */
export function placeTip(a, tip, box, gap = TIP_GAP, edge = TIP_EDGE) {
  const roomAbove = a.top - box.top - edge, roomBelow = box.top + box.height - (a.top + a.height) - edge;
  const below = roomAbove < tip.height + gap && roomBelow > roomAbove;
  let y = below ? a.top + a.height + gap : a.top - tip.height - gap;
  y = clamp(y, box.top + edge, Math.max(box.top + edge, box.top + box.height - tip.height - edge));
  const cx = a.left + a.width / 2;
  const x = clamp(cx - tip.width / 2, box.left + edge, Math.max(box.left + edge, box.left + box.width - tip.width - edge));
  return { x: Math.round(x - box.left), y: Math.round(y - box.top), below, tail: Math.round(clamp(cx - x, 14, Math.max(14, tip.width - 14))) };
}

/** True when nothing can hover: a coarse pointer or a phone-width window. */
export function touchOf(win) {
  try {
    if (!win || typeof win.matchMedia !== 'function') return false;
    return win.matchMedia('(pointer: coarse)').matches || win.matchMedia('(max-width: 640px)').matches;
  } catch (e) { return false; }
}

/** createTips({ doc, root, win, later, cancel }) -> { bind(node, text, { focus }), reset(), hide(), touch(), el, shown() }.
 * root = the sheet (view.root): the tip and the bank live inside it, so a repaint of the list never drops them. */
export function createTips({ doc, root, win = globalThis.window, later = (f, ms) => setTimeout(f, ms), cancel = (h) => clearTimeout(h) }) {
  const el = (tag, cls) => { const n = doc.createElement(tag); if (cls) n.className = cls; return n; };
  const tip = el('div', 's-tip'), words = el('span'), bank = el('div', 's-tipbank');
  tip.setAttribute('role', 'tooltip'); tip.id = 's-tip'; tip.hidden = true; tip.append(words);
  bank.hidden = true;
  root.append(tip, bank);
  let n = 0, timer = null, on = null, viaKey = false;
  const touch = () => touchOf(win);

  function hide() {
    if (timer != null) { cancel(timer); timer = null; }
    if (on) { on = null; tip.hidden = true; tip.classList.remove('below'); }
  }
  function show(node) {
    timer = null;
    if (touch() || !root.contains(node) || !node.dataset || !node.dataset.tip) return;
    words.textContent = node.dataset.tip;
    tip.hidden = false; on = node;
    const a = node.getBoundingClientRect(), box = root.getBoundingClientRect(), r = tip.getBoundingClientRect();
    const p = placeTip(a, { width: r.width || tip.offsetWidth || 200, height: r.height || 40 }, box);
    tip.classList[p.below ? 'add' : 'remove']('below');
    tip.setAttribute('style', `left:${p.x}px;top:${p.y}px;--tail:${p.tail}px`);
  }
  function arm(node, ms) {
    if (timer != null) cancel(timer);
    if (on && on !== node) { on = null; tip.hidden = true; }
    timer = later(() => show(node), ms);
  }

  /** Give a node its tip: aria-describedby to the bank's span, a hover (mouse and pen only), and keyboard focus when it
   * can take focus. Never a title. Returns the node. */
  function bind(node, text, { focus = true, keep = false } = {}) {
    if (!node || !text) return node;
    const s = el('span'), id = 's-tip-' + (++n);
    s.id = id; s.textContent = text; bank.append(s);
    if (keep) s.dataset.keep = '1';
    node.setAttribute('aria-describedby', id);
    node.dataset.tip = text;
    if (typeof node.addEventListener !== 'function') return node;
    node.addEventListener('pointerenter', (e) => { if ((e && e.pointerType === 'touch') || touch()) return; arm(node, TIP_DELAY_MS); });
    node.addEventListener('pointerleave', () => { if (on === node || timer != null) hide(); });
    if (focus) {
      node.addEventListener('focus', () => { if (viaKey && !touch()) arm(node, TIP_FOCUS_MS); });
      node.addEventListener('blur', () => { if (on === node || timer != null) hide(); });
    }
    return node;
  }
  /** Before a repaint: the old targets go, so their bank spans and any tip go too (a kept one, the head's, stays). */
  function reset() { hide(); bank.replaceChildren(...Array.from(bank.children).filter((c) => c.dataset && c.dataset.keep)); }
  /** A "?" pressed: its tip at once (a second press puts it away). */
  function showNow(node) { if (on === node) { hide(); return; } if (timer != null) { cancel(timer); timer = null; } show(node); }

  // A press, a scroll or a wheel inside the sheet: the tip goes (the sheet's own listeners, so dispose has nothing extra
  // to release). Keys come through key() from sheet/main.js's keydown: Tab marks keyboard use, so the next focus shows
  // its tip, any other key hides it; a pointer press marks pointer use (a repaint's refocus then shows nothing).
  if (typeof root.addEventListener === 'function') {
    root.addEventListener('pointerdown', () => { viaKey = false; hide(); }, true);
    root.addEventListener('wheel', hide, { passive: true, capture: true });
    root.addEventListener('scroll', hide, true);
  }
  function key(e) { viaKey = true; if (!e || (e.key !== 'Tab' && e.key !== 'Shift')) hide(); }

  return { bind, reset, hide, showNow, key, touch, el: tip, bank, shown: () => on };
}
