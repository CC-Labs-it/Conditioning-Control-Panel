/* Watch: the screen does the work. Words cross it, the spiral pulls, and the bonuses come from looking and holding still. */

import { BOUNCE_WORDS, BIG_WORDS, EDGE_WORDS, STILL_WORDS, STARE_LINES } from '../../../data/content/index.js';
import { DARK, darkCss, mountSpiral, spiral, deck, burst, ring, flash, bar, claim, release, docOf, btnCentre, setLine, dropLine, meterRow } from './kit.js';
import { ROUTE_COPY } from './copy.js';

const COLOURS = ['#ff69b4', '#7bd0ff', '#ffd24d', '#9dff9d', '#d9a6ff'];

/** The DVD logo's colour change, in the run's colours once a theme is picked. */
const wordColour = (ctx, c) => {
  const p = ctx.palette();
  return p.themes.length ? [p.a, p.b, `color-mix(in srgb,${p.a} 55%,#fff)`, `color-mix(in srgb,${p.b} 55%,#fff)`, p.a][c] : COLOURS[c];
};

/** A word on the old DVD path: a triangle wave on each axis, so true corners really happen, about every 24 s. */
function dvdWord(ctx, startText, { crossX = 8, crossY = 6, phase = 0, size = 1 } = {}) {
  const px = Math.round(Math.max(20, Math.min(46, ctx.bounds().w * 0.05)) * size);
  const el = ctx.el('div', `position:absolute;left:0;top:0;font-weight:700;font-size:${px}px;line-height:1;white-space:nowrap;color:${wordColour(ctx, 0)};will-change:transform;font-family:Arial,Helvetica,sans-serif;text-shadow:0 0 5px #000,0 1px 2px #000;`, startText);
  const nextWord = deck(BOUNCE_WORDS, ctx);
  let w = 0, h = 0, seen = null;
  const measure = () => { w = el.offsetWidth; h = el.offsetHeight; seen = ctx.bounds(); };
  measure();
  let t = phase, fx = null, fy = null, c = 0;
  const tri = (u) => { const m = u % 2; return m < 1 ? m : 2 - m; };
  return {
    el,
    step(dt) {
      if (seen !== ctx.bounds()) measure();
      const slow = ctx.gentle ? 0.5 : 1;
      t += dt * slow;
      const b = ctx.bounds(), ux = t / crossX, uy = t / crossY;
      const x = (b.w - w) * tri(ux), y = (b.h - h) * tri(uy);
      el.style.transform = `translate(${x}px,${y}px)`;
      const ix = Math.floor(ux), iy = Math.floor(uy);
      const hx = fx !== null && ix !== fx, hy = fy !== null && iy !== fy;
      fx = ix; fy = iy;
      if (!hx && !hy) return;
      const corner = hx && hy;
      c = (c + 1) % COLOURS.length;
      el.style.color = wordColour(ctx, c);
      const gain = ctx.wallHit({ x: x + w / 2, y: y + h / 2, corner });
      if (corner) {
        const r = ctx.rates(), big = Math.max(r.wall * 25, r.fps * 2);
        ctx.pay(big, 'corner', { x: x + w / 2, y: y + h / 2 });
        burst(ctx, x + w / 2, y + h / 2, 'var(--accent)', 18, 110);
        ctx.audio.play('assets/audio/sfx/golden_pop.mp3', { bus: 'sfx', gain: 0.6 });
        ctx.toast(ROUTE_COPY.corner);
        el.textContent = nextWord();
        measure();
      } else if (gain) {
        ctx.float(x + w / 2, y > b.h / 2 ? y - 14 : y + h + 8, `+${ctx.fmt(gain)}`, `color:${wordColour(ctx, c)};font-weight:700;`);
      }
    },
  };
}

export default {
  /* Spiral: dark page, the Loom's spiral behind everything. Tapping gives it a small push. */
  e_spiral: {
    mount(ctx) {
      ctx.vars(DARK);
      darkCss(ctx);
      const sp = mountSpiral(ctx, { opacity: 0.55, fadeMs: 1600 });
      sp.speedMul = 0.8;
      ctx.on('tap', () => { sp.kick = Math.min(1.2, sp.kick + 0.14); });
      return { off() { sp.dispose(); } };
    },
  },

  /* Faster Spiral: speed comes in five steps, up and back down, and a little meter shows which one you are on. */
  e_speed: {
    mount(ctx) {
      const sp = spiral();
      const row = ctx.el('div', 'display:flex;align-items:flex-end;gap:3px;height:20px;', null, meterRow(ctx));
      const bars = [0, 1, 2, 3, 4].map((i) => ctx.el('div', `width:9px;height:${6 + i * 3}px;background:rgba(255,255,255,.25);border-radius:1px;transition:background .3s;`, null, row));
      let n = 2, dir = 1;
      const paint = () => {
        bars.forEach((b, i) => { b.style.background = i <= n ? 'var(--accent)' : 'rgba(255,255,255,.25)'; });
        if (sp) sp.speedMul = 0.6 + n * 0.75;
      };
      paint();
      if (sp) { sp.kick = 2; sp.pulse(); }
      ctx.every(3000, () => {
        n += dir;
        if (n >= 4) dir = -1;
        if (n <= 0) dir = 1;
        paint();
        if (n === 4) sp?.pulse();
      });
      return { off() { if (sp) sp.speedMul = 0.8; } };
    },
  },

  /* Bouncing Word: the DVD logo, except it is a word. Walls pay a little, a true corner pays a lot. */
  e_bounce: {
    mount(ctx) {
      const w = dvdWord(ctx, 'DROP', { crossX: 8, crossY: 6, phase: 1.3 });
      return { tick: (dt) => w.step(dt) };
    },
  },
  e_words: {
    mount(ctx) {
      const a = dvdWord(ctx, 'SINK', { crossX: 9, crossY: 7.2, phase: 3.1, size: 0.85 });
      const b = dvdWord(ctx, 'SOFT', { crossX: 7, crossY: 10.5, phase: 6.7, size: 0.85 });
      return { tick: (dt) => { a.step(dt); b.step(dt); } };
    },
  },

  /* Big Word: a huge word fades through the middle. Tap while it is up and it pays once. */
  e_flash: {
    mount(ctx) {
      const next = deck(BIG_WORDS, ctx);
      let up = false;
      const show = () => {
        const b = btnCentre(ctx), word = next(), big = Math.min(ctx.bounds().w / Math.max(4, word.length) * 1.25, 110);
        flash(ctx, 'floaters', word, `left:0;right:0;top:${Math.max(60, b.y - big * 0.62)}px;text-align:center;font:700 ${big}px/1 Arial,Helvetica,sans-serif;color:#fff;opacity:.7;text-shadow:0 0 18px #ff69b4;`, 1700);
        up = true;
        ctx.voice(word.toLowerCase().replace(' ', '-'), { gain: 0.5 });
        ctx.after(1600, () => { up = false; });
      };
      ctx.after(400, show);
      ctx.every(7000, show);
      ctx.on('tap', ({ x, y }) => {
        if (!up) return;
        up = false;
        const r = ctx.rates();
        ctx.pay(Math.max(r.click * 4, r.fps), 'bigword', { x: x ?? btnCentre(ctx).x, y: y ?? btnCentre(ctx).y });
      });
      return {};
    },
  },

  /* Edge Words: small words fade in at the edges, all round the screen. */
  e_sublim: {
    mount(ctx) {
      const next = deck(EDGE_WORDS, ctx);
      const spot = () => {
        const { w, h } = ctx.bounds(), s = Math.floor(ctx.rng() * 4), u = ctx.rng();
        if (s === 0) return `left:${8 + u * (w - 90)}px;top:40px;`;
        if (s === 1) return `left:${8 + u * (w - 90)}px;top:${h - 26}px;`;
        if (s === 2) return `left:8px;top:${44 + u * (h - 80)}px;`;
        return `right:8px;top:${44 + u * (h - 80)}px;`;
      };
      const show = () => flash(ctx, 'over', next(), `${spot()}font:15px Arial,Helvetica,sans-serif;color:var(--accent-soft);text-shadow:0 0 6px #000;`, 1300);
      show();
      ctx.every(ctx.gentle ? 1400 : 600, show);
      return {};
    },
  },

  /* Stare Break: now and then it asks you to stop tapping and look. Six seconds of nothing pays thirty seconds of pay. */
  e_stare: {
    mount(ctx) {
      const nextStart = deck(STARE_LINES.start, ctx), nextDone = deck(STARE_LINES.done, ctx), nextBroken = deck(STARE_LINES.broken, ctx);
      let on = false, ends = null, cancel = null, label = null, meter = null;
      const stop = () => {
        on = false; cancel?.(); cancel = null;
        dropLine(label); meter?.el.remove(); label = meter = null;
        spiral()?.restore(700);
        release('stare');
      };
      const start = () => {
        if (on) return;
        if (!claim(ctx, 'stare', 7000)) { ctx.after(3000, start); return; }
        on = true;
        const b = btnCentre(ctx);
        label = setLine(ctx, nextStart(), 'font-weight:700;color:#fff;text-shadow:0 1px 4px #000;', 0);
        meter = bar(ctx, 'floaters', `position:absolute;left:50%;margin-left:-90px;width:180px;top:${b.top - 8}px;height:5px;`, 'var(--accent)');
        meter.set(1, 6000);
        spiral()?.setOpacity(0.95, 500);
        cancel = ctx.after(6000, () => {
          const r = ctx.rates(), at = btnCentre(ctx);
          ctx.pay(Math.max(r.fps * 30, r.click * 30), 'stare', { x: at.x, y: at.top });
          burst(ctx, at.x, at.y, 'var(--accent)', 16, 100);
          ctx.toast(nextDone());
          ctx.audio.play('assets/audio/sfx/reveal_chime.mp3', { bus: 'sfx', gain: 0.6 });
          stop();
        });
      };
      ctx.on('tap', ({ auto }) => {
        if (!on || auto) return;
        ctx.toast(nextBroken());
        stop();
      });
      ctx.after(1500, start);
      ctx.every(60000, start);
      return { off() { release('stare'); } };
    },
  },

  /* Hold Still Word: only keeping completely still shows it, and moving at all takes it away. */
  e_blink: {
    mount(ctx) {
      const next = deck(STILL_WORDS, ctx);
      const win = docOf(ctx).defaultView;
      let still = 0, shown = null, paid = false;
      const hide = () => { if (shown) { dropLine(shown); shown = null; } };
      const move = () => { still = 0; paid = false; hide(); };
      win.addEventListener('pointermove', move, { passive: true });
      win.addEventListener('pointerdown', move, { passive: true });
      win.addEventListener('keydown', move);
      ctx.on('tap', ({ auto }) => { if (!auto) move(); });
      return {
        tick(dt) {
          still += dt;
          if (still > 2.5 && !shown) {
            shown = setLine(ctx, next(), 'color:var(--accent-soft);text-shadow:0 1px 3px #000;', 0);
            ctx.anim(shown, [{ opacity: 0 }, { opacity: 1 }], { duration: 1400 });
          }
          if (still > 6 && !paid) {
            paid = true;
            const b = btnCentre(ctx), r = ctx.rates();
            ctx.pay(Math.max(r.fps * 3, r.click * 3), 'still', { x: b.x, y: b.bottom + 14 });
          }
        },
        off() {
          win.removeEventListener('pointermove', move);
          win.removeEventListener('pointerdown', move);
          win.removeEventListener('keydown', move);
        },
      };
    },
  },

  /* Pink Pulse: the page breathes pink, four seconds a round, and the button breathes with it. */
  e_pulse: {
    mount(ctx) {
      const glow = ctx.el('div', 'position:absolute;left:50%;top:30%;width:150vmax;height:150vmax;margin:-75vmax 0 0 -75vmax;border-radius:50%;background:radial-gradient(circle,rgba(255,180,220,.55) 0,rgba(255,105,180,.42) 30%,rgba(255,105,180,0) 62%);opacity:.25;', null, 'wash');
      ctx.anim(glow, [{ opacity: 0.12 }, { opacity: 0.8 }], { duration: 2000, iterations: Infinity, direction: 'alternate', easing: 'ease-in-out' });
      if (!ctx.gentle) ctx.anim(ctx.button.el, [{ transform: 'scale(.94)' }, { transform: 'scale(1.08)' }], { duration: 2000, iterations: Infinity, direction: 'alternate', easing: 'ease-in-out', composite: 'add' });
      const word = ctx.el('div', 'font:600 15px Arial,Helvetica,sans-serif;color:var(--accent-soft);width:30px;', 'In', meterRow(ctx));
      let out = false;
      ctx.every(2000, () => { out = !out; word.textContent = out ? 'Out' : 'In'; });
      return {};
    },
  },

  /* Dark Edges: the edges close in until you tap. The longer you let them, the more that tap pays. */
  e_last: {
    mount(ctx) {
      const v = ctx.el('div', 'position:absolute;inset:0;background:radial-gradient(ellipse at 50% 22%,transparent 22%,rgba(0,0,0,.9) 72%);opacity:0;', null, 'over');
      let dark = 0;
      ctx.on('tap', ({ x, y, auto }) => {
        if (auto || dark < 0.15) { if (!auto) dark = 0; return; }
        const r = ctx.rates(), at = btnCentre(ctx);
        ctx.pay(Math.max(r.fps, r.click) * dark * 12, 'edges', { x: x ?? at.x, y: y ?? at.y });
        if (dark > 0.6) { ring(ctx, at.x, at.y, 260, '#fff', 600, 'over'); ctx.voice('release', { gain: 0.6 }); }
        ctx.anim(v, [{ opacity: dark * 0.82 }, { opacity: 0 }], { duration: 300, easing: 'ease-out' });
        dark = 0;
      });
      ctx.every(200, () => {
        dark = Math.min(1, dark + 0.2 / 28);
        v.style.opacity = String(dark * 0.82);
      });
      return {};
    },
  },
};

