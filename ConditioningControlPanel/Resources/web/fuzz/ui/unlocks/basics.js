/* Lanes: trunk look side. Bulky entries live in ./basics/. See ENGINE.md. */

import look from './basics/look.js';
import look2 from './basics/look2.js';
import trunk from './basics/trunk.js';
import side from './basics/side.js';
import side2 from './basics/side2.js';
import { btnCenter, fresh, rings } from './basics/fx.js';
import { createTapPhrase } from './basics/tap-feel.js';

const CORNER_NEAR = 6;

/** One bouncing button. Speed is set from the page size so every screen gets the same wall hits per second. */
function bouncer(ctx) {
  const W = 52, H = 22;
  const el = ctx.el('div', `position:absolute;left:0;top:0;width:${W}px;height:${H}px;box-sizing:border-box;background:var(--btn-fill);border:2px outset #fff;will-change:transform;`);
  el.dataset.stray = '1';
  const b = ctx.bounds();
  const main = ctx.button.el.getBoundingClientRect?.();
  let x = Math.min(b.w - W, Math.max(0, (main?.left ?? b.w / 2) - 10));
  let y = Math.min(b.h - H, Math.max(0, (main?.top ?? b.h / 2) - 30));
  const sign = () => (ctx.rng() < 0.5 ? -1 : 1);
  const a = 0.8 + ctx.rng() * 0.4;
  let dx = sign() * a, dy = sign() * (2 - a);
  el.style.transform = `translate(${x}px,${y}px)`;
  return {
    el,
    step(dt) {
      const { w, h } = ctx.bounds();
      const r = ctx.rates().walls * (ctx.gentle ? 0.5 : 1);
      // Half the hits on each axis: speed = hits/s * travel / 2.
      x += dx * r * 0.5 * (w - W) * dt;
      y += dy * r * 0.5 * (h - H) * dt;
      let hx = false, hy = false;
      if (x < 0) { x = 0; dx = Math.abs(dx); hx = true; } else if (x > w - W) { x = w - W; dx = -Math.abs(dx); hx = true; }
      if (y < 0) { y = 0; dy = Math.abs(dy); hy = true; } else if (y > h - H) { y = h - H; dy = -Math.abs(dy); hy = true; }
      // Near enough to a corner counts, and snaps there: two walls on one exact frame would almost never happen.
      if (hx && !hy) { if (y < CORNER_NEAR) { y = 0; dy = Math.abs(dy); hy = true; } else if (y > h - H - CORNER_NEAR) { y = h - H; dy = -Math.abs(dy); hy = true; } }
      if (hy && !hx) { if (x < CORNER_NEAR) { x = 0; dx = Math.abs(dx); hx = true; } else if (x > w - W - CORNER_NEAR) { x = w - W; dx = -Math.abs(dx); hx = true; } }
      el.style.transform = `translate(${x}px,${y}px)`;
      if (hx || hy) {
        const gain = ctx.wallHit({ x: x + W / 2, y: y + H / 2, corner: hx && hy });
        if (gain) ctx.float(x + W / 2, y, `+${ctx.fmt(gain)}`, 'color:var(--stamp);');
      }
    },
  };
}

export default {
  ...look,
  ...look2,
  ...trunk,
  ...side,
  ...side2,

  /* Bouncing Button: a second button that pays on every wall hit. Stacks to 12. */
  stray: {
    mount(ctx) {
      const list = [];
      const grow = (n) => { while (list.length < n) list.push(bouncer(ctx)); };
      grow(ctx.count);
      return { grow, tick(dt) { for (const s of list) s.step(dt); } };
    },
  },

  /* Per Second: the rate under the counter, walls included. */
  fpsv: {
    mount(ctx) {
      const el = ctx.el('div', 'order:1;font-size:16px;', '', ctx.layer('meters'));
      if (fresh(ctx)) ctx.anim(el, [{ opacity: 0, transform: 'translateY(-6px)' }, { opacity: 1, transform: 'none' }], { duration: 300 });
      const draw = () => {
        const r = ctx.rates();
        const rate = r.fps + r.wall * r.walls * (ctx.state.owned.stray || 0);
        el.textContent = `${ctx.fmt(rate)} ${ctx.brand.currencyPlural} per second`;
      };
      draw();
      ctx.every(250, draw);
      return {};
    },
  },

  /* Click Sound: a tactile click under a small plucked phrase; a purchase resolves it. */
  sound: {
    mount(ctx) {
      const phrase = createTapPhrase();
      const pluck = (n, gain) => ctx.audio.play(`assets/audio/pluck/wood_${String(n).padStart(2, '0')}.wav`, { bus: 'sfx', gain });
      ctx.on('tap', ({ auto }) => {
        const cue = phrase.tap(ctx.state.clock, auto);
        if (!cue) return;
        ctx.audio.play('assets/audio/sfx/ui_click.mp3', { bus: 'sfx', gain: cue.gain });
        if (cue.note != null) pluck(cue.note, .16);
      });
      ctx.on('buy', () => {
        phrase.reset();
        pluck(6, .34);
        ctx.after(100, () => pluck(3, .25));
      });
      ctx.on('pause', () => phrase.reset());
      // A muted player still sees that the button found its voice.
      if (fresh(ctx)) { const c = btnCenter(ctx); rings(ctx, c.x, c.y); ctx.float(c.x, c.y - 34, 'click', 'font-size:20px;font-weight:700;'); ctx.audio.play('assets/audio/sfx/ui_click.mp3', { bus: 'sfx', gain: 0.25 }); }
      return {};
    },
  },
};
