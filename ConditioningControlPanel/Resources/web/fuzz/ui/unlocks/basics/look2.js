/* ui/unlocks/basics/look2.js - the late look rows: big and tiny. Since the Button Shop (owner 2026-11-08) these are SIZES, not
 * looks: #btn[data-size] layers on the plain button and every art face alike, and the shop's Size switch picks between them.
 * Each size leaves a rectangle of at least 48 px to press, whatever it draws, so nothing here can make the button hard to finish. */

import { buttonSize } from '../../button-shop.js';
import { baseCss, burst, btnCenter, docOf, fresh, sfx, squish } from './fx.js';
import dualWield from './dual-wield.js';

const SIZE_CSS = `
#btn[data-size=big]:not([data-art-face]) { width: min(70vw, 520px); height: 30vh; font-size: clamp(30px, 7vh, 56px); border-radius: 22px; }
#btn[data-size=big][data-art-face] > .fz-face { zoom: 1.7; }
#btn[data-size=big][data-art-face=tr-pendulum] > .fz-face { zoom: 1.45; }
#btn-zone:has(#btn[data-size=big]) { height: calc(30vh + 16px); }
#btn-zone:has(#btn[data-size=big][data-art-face=tr-pendulum]) { height: max(calc(30vh + 16px), 230px); }
@media (max-height: 620px), (max-width: 759px) {
  #btn[data-size=big]:not([data-art-face]) { height: 22vh; }
  #btn[data-size=big][data-art-face] > .fz-face { zoom: 1.3; }
  #btn[data-size=big][data-art-face=tr-pendulum] > .fz-face { zoom: 1.05; }
  #btn-zone:has(#btn[data-size=big]) { height: calc(22vh + 12px); }
  #btn-zone:has(#btn[data-size=big][data-art-face=tr-pendulum]) { height: 180px; }
}
#btn[data-size=tiny]:not([data-art-face]) { width: 18px; height: 12px; min-height: 0; padding: 0; font-size: 0; border-radius: 3px; box-shadow: none; }
#btn[data-size=tiny]:not([data-art-face])::before { content: ""; position: absolute; left: 50%; top: 50%; width: 56px; height: 56px; margin: -28px 0 0 -28px; }
#btn[data-size=tiny]::after { content: ""; position: absolute; left: 50%; top: 50%; width: 46px; height: 46px; margin: -23px 0 0 -23px; border: 2px dashed var(--stamp); border-radius: 50%; pointer-events: none; }
#btn[data-size=tiny]:not([data-art-face]).down { transform: scale(.8); box-shadow: none; }
#world #btn[data-size=tiny][data-art-face] { min-height: 48px; min-width: 48px; }
#btn[data-size=tiny][data-art-face] > .fz-face { zoom: .5; }
#world #btn[data-size=tiny][data-art-face]::after { display: block; width: calc(100% + 14px); height: calc(100% + 14px); margin: 0; translate: -50% -50%; border-radius: 12px; }
#world #btn[data-size=tiny][data-art-face=bubble]::after { border-radius: 50%; }
`;

/* Both rows share one painter: whichever is mounted keeps #btn[data-size] in step with the pick. */
function mountSize(ctx, mine) {
  baseCss(ctx);
  ctx.css(SIZE_CSS);
  const doc = docOf(ctx), el = ctx.button.el;
  /* The pick lives with the Button Shop (store.bubble.size); a fresh buy asks it to wear this size. */
  if (fresh(ctx)) ctx.emit('button-size', { size: mine, bought: true });
  let shown = null;
  const paint = () => {
    const size = buttonSize(ctx.state);
    if (size === shown) return;
    shown = size;
    if (size) el.dataset.size = size; else delete el.dataset.size;
    doc.defaultView.setTimeout(() => doc.defaultView.dispatchEvent(new Event('resize')), 450);
  };
  paint();
  ctx.on('button-size', paint);
  ctx.on('switch', paint);
  ctx.every(1000, paint);
  if (fresh(ctx)) {
    const c = btnCenter(ctx);
    squish(ctx, el, 1.4);
    burst(ctx, c.x, c.y, { n: 18, reach: 110, size: 8 });
    sfx(ctx, 'sfx/ui_unlock.mp3', 0.5);
  }
  ctx.cleanup(() => {
    /* This row went away (switched off): the other size row, if mounted, repaints on its own timer. */
    const size = buttonSize({ ...ctx.state, owned: { ...ctx.state.owned, [mine === 'big' ? 'bigbtn' : 'tinybtn']: 0 } });
    if (size) el.dataset.size = size; else delete el.dataset.size;
  });
  return {};
}

export default {
  /* Big Button: a third of the screen, a bit less on a phone. The zone grows with it, so it pushes the shop down and never lies under it. */
  bigbtn: { mount: (ctx) => mountSize(ctx, 'big') },
  /* Tiny Button: nearly nothing to see, a dashed ring to aim for, and a pressable area that stays finger sized. */
  tinybtn: { mount: (ctx) => mountSize(ctx, 'tiny') },
  /* Double Wielding: the right mouse button presses too (./dual-wield.js). */
  ...dualWield,
};
