/* ui/unlocks/basics/look2.js - the late look rows: big and tiny. Since the Button Shop (owner 2026-11-08) these are SIZES, not
 * looks: #btn[data-size] layers on the plain button and every art face alike, and the shop's Size switch picks between them.
 * Each size leaves a rectangle of at least 48 px to press, whatever it draws, so nothing here can make the button hard to finish.
 * Big grows in place (owner 2026-11-10): it fills the space its zone already has and the page never moves (../../button-size.js). */

import { buttonSize } from '../../button-shop.js';
import { SIZE_CSS, bigFit } from '../../button-size.js';
import { baseCss, burst, btnCenter, docOf, fresh, sfx, squish } from './fx.js';
import dualWield from './dual-wield.js';


/* Both rows share one painter: whichever is mounted keeps #btn[data-size] in step with the pick. */
function mountSize(ctx, mine) {
  baseCss(ctx);
  ctx.css(SIZE_CSS);
  const doc = docOf(ctx), el = ctx.button.el;
  /* The pick lives with the Button Shop (store.bubble.size); a fresh buy asks it to wear this size. */
  if (fresh(ctx)) ctx.emit('button-size', { size: mine, bought: true });
  let shown = null, fitKey = '';
  /* A big art face zooms to fit its zone: measured once at zoom 1, then refit only when the face, its size or the zone
   * changes (checked once a second and on resize, never per frame). The zone itself is never touched. */
  const keyOf = () => {
    const face = el.querySelector(':scope > .fz-face'), zone = el.closest('#btn-zone');
    return face && zone ? `${el.dataset.artFace}|${face.offsetWidth}x${face.offsetHeight}|${zone.clientWidth}x${zone.clientHeight}` : '';
  };
  const fit = () => {
    const face = el.dataset.size === 'big' && el.dataset.artFace ? el.querySelector(':scope > .fz-face') : null;
    const zone = el.closest('#btn-zone');
    if (!face || !zone) { if (fitKey) { el.style.removeProperty('--fz-big'); fitKey = ''; } return; }
    if (keyOf() === fitKey) return;
    el.style.setProperty('--fz-big', '1');
    const z = bigFit({ zoneW: zone.clientWidth, zoneH: zone.clientHeight, faceW: face.offsetWidth, faceH: face.offsetHeight });
    el.style.setProperty('--fz-big', String(z));
    fitKey = keyOf();
  };
  const paint = () => {
    const size = buttonSize(ctx.state);
    if (size !== shown) {
      shown = size;
      if (size) el.dataset.size = size; else delete el.dataset.size;
      fitKey = '';
      /* The button changed size, the page did not: effects that cache the button's box read it again. */
      doc.defaultView.setTimeout(() => doc.defaultView.dispatchEvent(new Event('resize')), 60);
    }
    fit();
  };
  paint();
  ctx.on('button-size', paint);
  ctx.on('switch', paint);
  ctx.every(1000, paint);
  const onResize = () => fit();
  doc.defaultView.addEventListener('resize', onResize);
  if (fresh(ctx)) {
    const c = btnCenter(ctx);
    squish(ctx, el, 1.4);
    burst(ctx, c.x, c.y, { n: 18, reach: 110, size: 8 });
    sfx(ctx, 'sfx/ui_unlock.mp3', 0.5);
  }
  ctx.cleanup(() => {
    doc.defaultView.removeEventListener('resize', onResize);
    el.style.removeProperty('--fz-big');
    /* This row went away (switched off): the other size row, if mounted, repaints on its own timer. */
    const size = buttonSize({ ...ctx.state, owned: { ...ctx.state.owned, [mine === 'big' ? 'bigbtn' : 'tinybtn']: 0 } });
    if (size) el.dataset.size = size; else delete el.dataset.size;
  });
  return {};
}

export default {
  /* Big Button: fills the button's own spot, as wide as 520 px, and grows in place: the counter, shop and dock never move. */
  bigbtn: { mount: (ctx) => mountSize(ctx, 'big') },
  /* Tiny Button: nearly nothing to see, a dashed ring to aim for, and a pressable area that stays finger sized. */
  tinybtn: { mount: (ctx) => mountSize(ctx, 'tiny') },
  /* Double Wielding: the right mouse button presses too (./dual-wield.js). */
  ...dualWield,
};
