/* ui/cards.js - one card at a time, queued. Every card closes: it has a button or closes itself. The pause card lives
 * in index.html and sits above this queue. */

import { brandText } from '../data/brand.js';

const GUARD_MS = 350;

export function createCards(doc, host) {
  const queue = [];
  let current = null;

  function build(spec) {
    const back = doc.createElement('div');
    back.className = spec.clear ? 'backdrop clear' : 'backdrop';
    const card = doc.createElement('div');
    card.className = `card ${spec.className || ''}`;
    card.setAttribute('role', 'dialog');
    card.setAttribute('aria-modal', 'true');
    if (spec.title) { const h = doc.createElement('h2'); h.textContent = brandText(spec.title); card.appendChild(h); }
    if (spec.body) { const p = doc.createElement('p'); p.textContent = brandText(spec.body); card.appendChild(p); }
    if (spec.node) card.appendChild(spec.node);
    const buttons = spec.buttons?.length ? spec.buttons : (spec.ms ? [] : [{ label: 'Close' }]);
    if (buttons.length) {
      const row = doc.createElement('div');
      row.className = 'card-buttons';
      buttons.forEach((b, i) => {
        const el = doc.createElement('button');
        el.type = 'button';
        el.textContent = brandText(b.label);
        if (i === 0) el.className = 'primary';
        if (b.css) el.style.cssText = b.css;
        el.addEventListener('click', () => { if (b.keepOpen) { b.onPress?.(); return; } spec.handle.close(); b.onPress?.(); });
        row.appendChild(el);
      });
      card.appendChild(row);
    }
    back.appendChild(card);
    if (spec.guard !== false) { card.classList.add('guard'); setTimeout(() => card.classList.remove('guard'), GUARD_MS); }
    return back;
  }

  function show(spec) {
    current = spec;
    spec.dom = build(spec);
    host.appendChild(spec.dom);
    spec.onOpen?.(spec.dom);
    if (spec.ms) spec.timer = setTimeout(() => spec.handle.close(), spec.ms);
  }

  function pump() { if (!current && queue.length) show(queue.shift()); }

  function open(spec) {
    const s = { ...spec };
    let closed = false;
    s.handle = {
      close() {
        if (closed) return;
        closed = true;
        clearTimeout(s.timer);
        const i = queue.indexOf(s);
        if (i >= 0) queue.splice(i, 1);
        if (current === s) {
          s.dom.remove();
          current = null;
          s.onClose?.();
          pump();
        }
      },
      get closed() { return closed; },
    };
    if (s.front) queue.unshift(s); else queue.push(s);
    pump();
    return s.handle;
  }

  return { open, get busy() { return !!current; } };
}
