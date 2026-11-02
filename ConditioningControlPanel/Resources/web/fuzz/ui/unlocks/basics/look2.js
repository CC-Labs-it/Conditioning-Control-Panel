/* ui/unlocks/basics/look2.js - the late look rows: heart, mirror, spiral, big, tiny. Each skin leaves a rectangle of at
 * least 48 px to press, whatever it draws, so nothing here can make the button hard to finish with. */

import { beatAt, baseCss, burst, btnCenter, docOf, fresh, sfx, spiralCanvas, squish } from './fx.js';

function wear(ctx, name, css) {
  baseCss(ctx);
  ctx.css(css);
  ctx.button.skin(name);
  if (fresh(ctx)) {
    const c = btnCenter(ctx);
    squish(ctx, ctx.button.el, 1.4);
    burst(ctx, c.x, c.y, { n: 18, reach: 110, size: 8 });
    sfx(ctx, 'sfx/ui_unlock.mp3', 0.5);
    docOf(ctx).defaultView.setTimeout(() => docOf(ctx).defaultView.dispatchEvent(new Event('resize')), 450);
  }
}

const HEART = encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 29"><path d="M16 29C16 29 0 19 0 9 0 3 5 0 9 0c3 0 6 2 7 5 1-3 4-5 7-5 4 0 9 3 9 9 0 10-16 20-16 20z" fill="#e8306f"/><ellipse cx="9" cy="8" rx="4" ry="2.6" fill="#fff" opacity=".45" transform="rotate(-25 9 8)"/></svg>');

export default {
  /* Heart Button: beats on the same clock as the Heartbeat row, and squashes hard when pressed. */
  heartbtn: {
    mount(ctx) {
      wear(ctx, 'heart', `
#btn[data-skin=heart] { width: 104px; height: 94px; border: 0; box-shadow: none; color: #fff; font-weight: 700; background: url("data:image/svg+xml,${HEART}") center/84% auto no-repeat; text-shadow: 0 1px 2px rgba(120,0,50,.6); }
#btn[data-skin=heart].down { border: 0; box-shadow: none; transform: scale(1.14,.8); }
`);
      const el = ctx.button.el;
      ctx.on('tap', () => { if (el.dataset.skin === 'heart') squish(ctx, el, 1.2); });
      return {
        tick(dt, now) {
          if (el.dataset.skin !== 'heart') return;
          const k = ctx.gentle ? 1 : 1 + beatAt(now) * 0.12;
          el.style.backgroundSize = `${(84 * k).toFixed(1)}% auto`;
        },
        off() { el.style.backgroundSize = ''; },
      };
    },
  },

  /* Mirror Button: chrome, a sheen that crosses it, and a dim reflection under it. */
  mirror: {
    mount(ctx) {
      wear(ctx, 'mirror', `
@keyframes fz-sheen { 0%, 40% { background-position: 170% 0, 0 0; } 100% { background-position: -70% 0, 0 0; } }
#btn[data-skin=mirror] { width: 160px; height: 58px; color: #123; font-weight: 700; border: 1px solid #fff; border-radius: 6px; box-shadow: 0 1px 6px rgba(0,0,0,.35); background: linear-gradient(105deg, transparent 42%, rgba(255,255,255,.85) 50%, transparent 58%) no-repeat, linear-gradient(160deg, #fff, #b8c4d4 45%, #7d8ca0 55%, #e6edf5); background-size: 200% 100%, 100% 100%; animation: fz-sheen 2.4s ease-in-out infinite; }
#btn[data-skin=mirror].down { border-style: solid; }
#btn[data-skin=mirror]::after { content: "Click"; position: absolute; left: 0; right: 0; top: 100%; height: 58px; display: grid; place-items: center; color: #123; font-weight: 700; border-radius: 6px; pointer-events: none; scale: 1 -1; opacity: .28; background: linear-gradient(160deg, #fff, #b8c4d4 45%, #7d8ca0 55%, #e6edf5); -webkit-mask-image: linear-gradient(to top, #000, transparent 75%); mask-image: linear-gradient(to top, #000, transparent 75%); }
body.gentle #btn[data-skin=mirror] { animation: none; }
`);
      return {};
    },
  },

  /* Spiral Button: from the Loom, turning, with an extra full turn on every press. */
  spirbtn: {
    mount(ctx) {
      const el = ctx.button.el;
      wear(ctx, 'spiral', `
#btn[data-skin=spiral] { width: 96px; height: 96px; border-radius: 50%; color: transparent; overflow: hidden; border: 2px solid #fff; box-shadow: 0 2px 8px rgba(0,0,0,.4); padding: 0; background: #fff; }
#btn[data-skin=spiral].down { border-style: solid; }
#btn:not([data-skin=spiral]) canvas.fz-sp { display: none; }
canvas.fz-sp { position: absolute; inset: 0; width: 100%; height: 100%; pointer-events: none; }
`);
      const sp = spiralCanvas(ctx, '', el, { px: 192, bg: '#ffffff', colors: ['#ff4fa8', '#6a3cff'], turnMs: 9000 });
      sp.el.className = 'fz-sp';
      ctx.on('tap', () => {
        if (el.dataset.skin !== 'spiral' || ctx.gentle) return;
        ctx.anim(sp.el, [{ transform: 'rotate(0deg)' }, { transform: 'rotate(-360deg)' }], { duration: 700, easing: 'ease-out' });
      });
      return {};
    },
  },

  /* Big Button: a third of the screen. The zone grows with it, so it pushes the shop down and never lies under it. */
  bigbtn: {
    mount(ctx) {
      wear(ctx, 'big', `
#btn[data-skin=big] { width: min(70vw, 520px); height: 30vh; font-size: clamp(30px, 7vh, 56px); border-radius: 22px; }
#btn-zone:has(#btn[data-skin=big]) { height: calc(30vh + 16px); }
@media (max-height: 620px) { #btn[data-skin=big] { height: 22vh; } #btn-zone:has(#btn[data-skin=big]) { height: calc(22vh + 12px); } }
`);
      return {};
    },
  },

  /* Tiny Button: nearly nothing to see, a dashed ring to aim for, and a pressable area that stays finger sized. */
  tinybtn: {
    mount(ctx) {
      wear(ctx, 'tiny', `
#btn[data-skin=tiny] { width: 18px; height: 12px; padding: 0; font-size: 0; border-radius: 3px; box-shadow: none; }
#btn[data-skin=tiny]::before { content: ""; position: absolute; left: 50%; top: 50%; width: 56px; height: 56px; margin: -28px 0 0 -28px; }
#btn[data-skin=tiny]::after { content: ""; position: absolute; left: 50%; top: 50%; width: 46px; height: 46px; margin: -23px 0 0 -23px; border: 2px dashed var(--stamp); border-radius: 50%; pointer-events: none; }
#btn[data-skin=tiny].down { transform: scale(.8); box-shadow: none; }
`);
      return {};
    },
  },
};

