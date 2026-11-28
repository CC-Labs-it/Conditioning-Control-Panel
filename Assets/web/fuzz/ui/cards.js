/* ui/cards.js - one card at a time, queued. Every card closes: it has a button or closes itself. The pause card lives
 * in index.html and sits above this queue. A card the pause card itself asked for (`overPause`, the save carry's
 * incoming run) lifts the queue over it while it shows, so it can be answered at once. It never waits behind another
 * card: an open ordinary card (say the corner note, inert under the pause) steps aside, hidden and kept, and comes back
 * as it was once the overPause card is answered. Still one card showing at a time. */

import { brandText } from '../data/brand.js';
import { setRich } from './rich-text.js';

const GUARD_MS = 350;

export function createCards(doc, host) {
  const queue = [];
  /* The pause card's hold (ui/app.js holdWorld): the queue is inert under it, unless the card showing is one the pause
     card asked for (overPause), which then sits over it and takes presses and keys. */
  let held = false;
  const lift = () => {
    const over = !!current?.overPause;
    host.classList?.[over ? 'add' : 'remove']('over-pause');
    host.inert = held && !over;
  };
  let current = null;

  function build(spec) {
    const back = doc.createElement('div');
    /* corner: a quiet note (ui/save-sleep.js) in the bottom corner, no backdrop; the page under it stays live. */
    back.className = spec.corner ? 'backdrop clear corner' : spec.clear ? 'backdrop clear' : 'backdrop';
    const card = doc.createElement('div');
    card.className = `card ${spec.className || ''}`;
    card.setAttribute('role', 'dialog');
    card.setAttribute('aria-modal', 'true');
    if (spec.title) { const h = doc.createElement('h2'); h.textContent = brandText(spec.title); card.appendChild(h); }
    if (spec.body) { const p = doc.createElement('p'); setRich(p, spec.body); card.appendChild(p); }
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
        /* onPress gets the press event: ui/nudges.js realPress reads its isTrusted. */
        el.addEventListener('click', (ev) => { if (b.keepOpen) { b.onPress?.(ev); return; } spec.handle.close(); b.onPress?.(ev); });
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
    lift();
    spec.onOpen?.(spec.dom);
    if (spec.ms) spec.timer = setTimeout(() => spec.handle.close(), spec.ms);
  }

  /* Cards an overPause card stepped in front of: hidden, never closed; the last one back first. A timed card keeps no
     clock while aside and gets its whole time again when it is back. */
  const aside = [];
  function stepAside() {
    clearTimeout(current.timer);
    current.dom.hidden = true;
    aside.push(current);
    current = null;
  }
  function comeBack() {
    current = aside.pop();
    current.dom.hidden = false;
    lift();
    const s = current;
    if (s.ms) s.timer = setTimeout(() => s.handle.close(), s.ms);
  }

  function pump() {
    if (current) return;
    if (queue[0]?.overPause) show(queue.shift());
    else if (aside.length) comeBack();
    else if (queue.length) show(queue.shift());
  }

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
        const j = aside.indexOf(s);
        if (j >= 0) { aside.splice(j, 1); s.dom.remove(); s.onClose?.(); }
        if (current === s) {
          s.dom.remove();
          current = null;
          lift();
          s.onClose?.();
          pump();
        }
      },
      get closed() { return closed; },
    };
    if (s.front || s.overPause) queue.unshift(s); else queue.push(s);
    if (s.overPause && current && !current.overPause) stepAside();
    pump();
    return s.handle;
  }

  return { open, hold(on) { held = !!on; lift(); }, get busy() { return !!current; } };
}
