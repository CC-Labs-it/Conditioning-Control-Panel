/* ui/unlocks/fate/museum/gift.js - ft_gift: the shop becomes the Gift Shop. The shop has no visible heading of its own,
 * so this hangs a small paper sign from two strings in the banner slot right above the tiles, and renames the shop's
 * accessible label. Nothing in ui/shop.js changes and prices stay the same. A purchase rocks the sign once; Gentle and
 * reduced motion keep it still. Buying it also starts loading the museum ride, so the ending opens quickly. */

import { GIFT } from '../../../../data/content/museum.js';
import { warmMuseum } from './ending.js';

const INK = '#2a2031';

const CSS = `
.fz-gift { position:relative; display:flex; justify-content:center; height:44px; pointer-events:none; }
.fz-gift-sign { position:relative; margin-top:12px; transform-origin:50% -12px; }
.fz-gift-sign::before, .fz-gift-sign::after { content:''; position:absolute; bottom:100%; width:2px; height:13px; background:${INK}; }
.fz-gift-sign::before { left:8px; transform:rotate(14deg); transform-origin:50% 100%; }
.fz-gift-sign::after { right:8px; transform:rotate(-14deg); transform-origin:50% 100%; }
.fz-gift-sign span { display:block; padding:4px 19px 5px 22px; background:#f7ecd2; color:#a42b3a; border:2.5px solid ${INK}; border-radius:4px 6px 4px 5px;
  font:800 13px/1.1 Georgia,'Times New Roman',serif; letter-spacing:.24em; box-shadow:3px 3px 0 rgba(42,32,49,.2); }
.fz-gift-sign span::before, .fz-gift-sign span::after { content:''; position:absolute; top:3px; width:5px; height:5px; border-radius:50%;
  background:#c49a3c; border:1.5px solid ${INK}; }
.fz-gift-sign span::before { left:6px; } .fz-gift-sign span::after { right:6px; }
`;

export function mountGift(ctx) {
  ctx.css(CSS);
  const banner = ctx.layer('banner');
  const shop = ctx.parts.shop;
  const doc = banner.ownerDocument;
  const row = ctx.el('div', '', null, banner);
  row.className = 'fz-gift';
  row.setAttribute('aria-hidden', 'true');
  const sign = doc.createElement('div');
  sign.className = 'fz-gift-sign';
  const word = doc.createElement('span');
  word.textContent = GIFT.sign;
  sign.append(word);
  row.append(sign);

  /* Keep the sign last in the banner, right over the tiles, when an ad slot arrives after it. */
  const Observer = doc.defaultView?.MutationObserver;
  if (Observer) {
    const keepLast = new Observer(() => { if (row.isConnected && banner.lastElementChild !== row) banner.appendChild(row); });
    keepLast.observe(banner, { childList: true });
    ctx.cleanup(() => keepLast.disconnect());
  }

  const was = shop?.getAttribute('aria-label');
  shop?.setAttribute('aria-label', GIFT.label);
  ctx.cleanup(() => { if (!shop) return; if (was == null) shop.removeAttribute('aria-label'); else shop.setAttribute('aria-label', was); });

  let rocking = null;
  ctx.on('buy', () => {
    if (ctx.gentle || ctx.reduced || rocking) return;
    rocking = ctx.anim(sign, [
      { transform: 'rotate(0deg)' }, { transform: 'rotate(5deg)', offset: 0.25 }, { transform: 'rotate(-3deg)', offset: 0.55 },
      { transform: 'rotate(1.2deg)', offset: 0.8 }, { transform: 'rotate(0deg)' },
    ], { duration: 1500, easing: 'ease-out' });
    const done = () => { rocking = null; };
    rocking.onfinish = done;
    rocking.oncancel = done;
  });

  warmMuseum().catch(() => {});
  return {};
}
