/* hud/guide.js - the "How it works" card: three paper prints, each a numbered stamp, a real capture, a stamped title,
 * one line and a small caption (same treatment as Racing Thoughts' hud/howto.js). It sells nothing and holds no item:
 * no shelf key, pad press or tap gets past it, and it closes with nothing focused.
 *
 *   const g = createGuide(doc, { t, guard, motion });   // g.el goes in the HUD
 *   g.open(); g.close(); g.isOpen();
 *
 * Closing: Esc or Backspace at once; Space, Enter or pad A on a deliberate release (state/press-guard.js, so a stray
 * key can not wave it away unread); a click or tap on the button or the scrim once it has been up a moment.
 * main.js closes it for the keys and pads that reach the page from outside the card. */

import { keyName } from '../state/press-guard.js';

const OUT_MS = 160;
const BEATS = [
  { n: 1, img: 'earn', title: 'shop_howto_earn_title', gist: 'shop_howto_earn_gist', cap: 'shop_howto_earn_cap' },
  { n: 2, img: 'pick', title: 'shop_howto_pick_title', gist: 'shop_howto_pick_gist', cap: 'shop_howto_pick_cap' },
  { n: 3, img: 'buy-wear', title: 'shop_howto_buy_title', gist: 'shop_howto_buy_gist', cap: 'shop_howto_buy_cap' },
];
const shot = (name) => new URL(`../art/guide/${name}.webp`, import.meta.url).href;
const pressKey = (e) => e.key === 'Enter' || e.key === ' ' || e.code === 'Space' || e.code === 'NumpadEnter';

export function createGuide(doc, { t, guard, motion = 'full' }) {
  const el = (tag, cls, text) => { const n = doc.createElement(tag); if (cls) n.className = cls; if (text != null) n.textContent = text; return n; };
  const modal = el('div', 'pc-modal pc-guide-modal'), scrim = el('div', 'pc-scrim');
  const card = el('div', 'pc-card pc-guide');
  card.tabIndex = -1;
  card.setAttribute('role', 'dialog');
  card.setAttribute('aria-modal', 'true');
  card.setAttribute('aria-labelledby', 'pc-guide-title');
  const title = el('h2', 'pc-guide-title', t('shop_title'));
  title.id = 'pc-guide-title';
  const beats = el('ol', 'pc-gbs');
  for (const b of BEATS) {
    const li = el('li', 'pc-gb'), img = el('img', 'pc-gb-shot'), words = el('div', 'pc-gb-words'), n = el('span', 'pc-gb-n', String(b.n));
    n.setAttribute('aria-hidden', 'true');
    // The words carry all of it, so the picture stays out of a screen reader's way.
    img.setAttribute('alt', '');
    img.setAttribute('width', '520');
    img.setAttribute('height', '273');
    img.setAttribute('decoding', 'async');
    img.setAttribute('draggable', 'false');
    img.dataset.src = shot(b.img);
    words.append(el('h3', 'pc-gb-title', t(b.title)), el('p', 'pc-gb-gist', t(b.gist)), el('p', 'pc-gb-cap', t(b.cap)));
    li.append(n, img, words);
    beats.append(li);
  }
  const go = el('button', 'pc-btn pc-main pc-guide-go', t('shop_howto_go'));
  go.type = 'button';
  card.append(title, beats, go);
  modal.append(scrim, card);
  modal.hidden = true;

  let up = false, outTimer = 0, loaded = false;
  const hide = () => { modal.hidden = true; modal.classList.remove('pc-out'); };

  function open() {
    if (up) return;
    up = true;
    clearTimeout(outTimer);
    // The prints load when first asked for: most opens of the counter never show this card.
    if (!loaded) { loaded = true; for (const img of beats.querySelectorAll('.pc-gb-shot')) img.setAttribute('src', img.dataset.src); }
    modal.hidden = false;
    modal.classList.remove('pc-out');
    if (motion !== 'off') { modal.classList.remove('pc-in'); void modal.offsetWidth; modal.classList.add('pc-in'); }
    guard.card();
    card.focus();
  }
  function close() {
    if (!up) return;
    up = false;
    guard.card();   // the key that closed it, or the next one mashed, is not a shelf press
    modal.classList.remove('pc-in');
    if (modal.contains(doc.activeElement)) doc.activeElement.blur();
    if (motion === 'off') hide(); else { modal.classList.add('pc-out'); outTimer = setTimeout(hide, OUT_MS); }
  }
  // A key's synthetic click is dropped (the keyup below is its one route), and so is a tap inside the first moment.
  const click = (e) => { if (!(e && e.detail === 0) && guard.ready()) close(); };
  go.onclick = click;
  scrim.onclick = click;

  // Keys stay in the card. Arrows keep their native scroll on a window too small for the three prints.
  modal.addEventListener('keydown', (e) => {
    e.stopPropagation();
    if (pressKey(e)) { e.preventDefault(); guard.down(keyName(e), e.repeat); return; }
    if (e.key === 'Escape' || e.key === 'Backspace') { e.preventDefault(); close(); return; }
    if (e.key === 'Tab') { e.preventDefault(); (doc.activeElement === go ? card : go).focus(); }
  });
  modal.addEventListener('keyup', (e) => {
    e.stopPropagation();
    if (!pressKey(e)) return;
    e.preventDefault();
    if (guard.up(keyName(e))) close();
  });

  return { el: modal, open, close, isOpen: () => up, dispose() { clearTimeout(outTimer); } };
}
