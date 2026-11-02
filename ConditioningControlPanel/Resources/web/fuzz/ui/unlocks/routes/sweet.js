/* Rest: this route pays you to leave the game alone. Every row here rewards not tapping, and a tap costs you the build-up.
 * The numbers only ever go back to where they started: nothing is taken away from the wallet. */

import { PRAISE, PRAISE_NAMED, REST_LINES, TUCK_LINES, GLOW_LINES, WARM_LINES, NAP_LINES } from '../../../data/content/index.js';
import { PLUCKS } from '../../../audio/kit.js';
import { deck, fill, btnCentre, setLine, meterRow, bar, ring, burst, tone, onDark, docOf, ink } from './kit.js';
import { ROUTE_COPY } from './copy.js';

/** Fires on real taps only and answers how long the hands have been off, in game seconds. */
function restClock(ctx) {
  let last = ctx.state.clock;
  ctx.on('tap', ({ auto }) => { if (!auto) last = ctx.state.clock; });
  return { get seconds() { return (ctx.state.clock - last) / 1000; }, reset() { last = ctx.state.clock; } };
}
const fitsLine = (ctx) => (s) => s.length <= (ctx.bounds().w < 760 ? 40 : 90);

export default {
  /* Praise: soft words. More often while you rest. */
  s_good: {
    mount(ctx) {
      tone(ctx, { '--page': '#fff0e6', '--panel': '#fff7f0', '--panel-short': '#f3e2d6', '--line': '#d9b9a3', '--fill': '#f5cdb0', '--btn': '#f7e3d4' });
      const rest = restClock(ctx), next = deck(PRAISE.filter(fitsLine(ctx)), ctx);
      const css = `font-style:italic;font-size:20px;color:${ink(ctx, '#a8246a', '#ffb0d6')};`;
      const say = () => setLine(ctx, next(), css, 2600);
      ctx.after(500, say);
      let gap = 0;
      ctx.every(1000, () => {
        gap += 1;
        if (gap >= (rest.seconds > 5 ? 6 : 13)) { gap = 0; say(); }
      });
      return {};
    },
  },

  /* Rest Bonus: ten seconds with no taps and all your per-second pay goes up by half, until you touch it. */
  s_rest: {
    mount(ctx) {
      const rest = restClock(ctx), resting = deck(REST_LINES.resting, ctx), broken = deck(REST_LINES.broken, ctx);
      const row = ctx.el('div', 'display:flex;align-items:center;gap:5px;font:12px Arial,Helvetica,sans-serif;color:var(--ink-soft);', REST_LINES.label, meterRow(ctx));
      const m = bar(ctx, row, 'width:60px;height:7px;', '#f0a060');
      const x = ctx.el('b', 'font-size:15px;color:#d4488f;opacity:.25;transition:opacity .3s;', 'More', row);
      let lit = false;
      ctx.mod({ fps: (v) => (lit ? v * 1.5 : v) });
      ctx.on('tap', ({ auto }) => {
        if (auto) return;
        if (lit) { setLine(ctx, broken(), `font-style:italic;color:${ink(ctx, '#7a4a5e', '#ffb0d6')};`, 2000); }
        lit = false; x.style.opacity = '.25'; m.set(0);
      });
      ctx.every(250, () => {
        const p = Math.min(1, rest.seconds / 10);
        if (!lit) m.set(p);
        if (p >= 1 && !lit) {
          lit = true; x.style.opacity = '1';
          m.set(1);
          const b = btnCentre(ctx);
          ring(ctx, b.x, b.y, 200, '#f0a060', 900);
          setLine(ctx, resting(), `font-style:italic;color:${ink(ctx, '#d4488f', '#ffb0d6')};`, 2400);
          ctx.audio.play('assets/audio/sfx/chime1.mp3', { bus: 'sfx', gain: 0.5 });
          ctx.anim(x, ctx.gentle ? [{ opacity: 0.25 }, { opacity: 1 }] : [{ transform: 'scale(2)' }, { transform: 'scale(1)' }], { duration: 400 });
        }
      });
      return {};
    },
  },

  /* Warm Colours: the corners come off everything and the page warms up. */
  s_blanket: {
    mount(ctx) {
      const warm = deck(WARM_LINES, ctx);
      if (onDark(ctx)) ctx.vars({ '--page': '#2a1a14', '--panel': '#3a261c', '--panel-short': '#2f1f18', '--line': '#7d5a45', '--fill': '#6d4632', '--btn': '#5a3a2a' });
      else ctx.vars({ '--page': '#ffd9bb', '--panel': '#fff0e2', '--panel-short': '#f7dcc6', '--line': '#d9a079', '--fill': '#ffbf91', '--btn': '#ffb98a', '--ink': '#3d1f12', '--ink-soft': '#6b3f2a' });
      ctx.css('.row,#goal .row{border-radius:14px}#btn{border-radius:18px;border:0;box-shadow:0 3px 0 rgba(90,40,10,.35)}#btn.down{box-shadow:none;transform:translateY(2px) scale(.97)}.rail > *{border-radius:12px}body{transition:background 2.4s}');
      const v = ctx.el('div', 'position:absolute;inset:0;background:radial-gradient(ellipse at center,transparent 45%,rgba(255,140,70,.35) 100%);opacity:0;', null, 'wash');
      ctx.anim(v, [{ opacity: 0 }, { opacity: 1 }], { duration: 2400, fill: 'forwards' });
      ctx.after(600, () => ctx.toast(warm()));
      return {};
    },
  },

  /* Slow Music: a plucked tune that does not resolve, drawn on four strings behind the page. */
  s_lull: {
    mount(ctx) {
      const notes = [3, 6, 8, 10, 8, 6, 5, 8, 3, 6, 10, 13, 10, 8, 5, 3];
      const c = ctx.el('canvas', 'position:absolute;left:0;width:100%;height:120px;z-index:2;opacity:.7;', null, 'back');
      c.width = 480; c.height = 120;
      const g = c.getContext('2d'), strings = [0, 0, 0, 0], tints = ['#7b5cff', '#a35cff', '#d45ca8', '#ff69b4'];
      const place = () => { c.style.top = `${btnCentre(ctx).y - 60}px`; };
      place();
      let i = 0, t = 0, spent = 0;
      const pluck = () => {
        const n = notes[i++ % notes.length], s = n % 4;
        strings[s] = 1;
        ctx.audio.play(PLUCKS[n], { bus: 'ambient', gain: 1.2 });
        const { w } = ctx.bounds();
        ctx.float(w * (0.12 + s * 0.25), btnCentre(ctx).y - 40 + s * 22, '♪', `color:${tints[s]};font-size:20px;`);
      };
      pluck();
      ctx.every(1100, pluck);
      ctx.after(800, () => ctx.toast(WARM_LINES[4]));
      return {
        tick(dt) {
          spent += dt;
          if (spent < 1 / 24) return;
          t += ctx.gentle ? 0 : spent;
          for (let s = 0; s < 4; s++) strings[s] = Math.max(0.03, strings[s] - spent * 0.55);
          spent = 0;
          g.clearRect(0, 0, 480, 120);
          for (let s = 0; s < 4; s++) {
            g.beginPath();
            const y = 18 + s * 28;
            for (let x = 0; x <= 480; x += 6) {
              const env = Math.sin((x / 480) * Math.PI);
              g.lineTo(x, y + Math.sin(x * (0.03 + s * 0.012) - t * 12) * strings[s] * 14 * env);
            }
            g.strokeStyle = tints[s]; g.lineWidth = 2; g.stroke();
          }
        },
      };
    },
  },

  /* Quiet Taps: the longer your hands rest, the softer the next tap, and the softest one pays the most. */
  s_tuck: {
    mount(ctx) {
      const rest = restClock(ctx), lines = deck(TUCK_LINES, ctx);
      const row = ctx.el('div', 'display:flex;align-items:flex-end;gap:2px;height:20px;', null, meterRow(ctx));
      const ticks = [0, 1, 2, 3, 4].map((i) => ctx.el('div', `width:6px;height:${5 + i * 3}px;background:#d4488f;transition:opacity .4s;`, null, row));
      let hush = 0;
      const paint = () => ticks.forEach((t, i) => { t.style.opacity = i < 5 - hush ? 1 : 0.15; });
      paint();
      ctx.every(1000, () => { hush = Math.min(5, Math.floor(rest.seconds / 5)); paint(); });
      ctx.on('tap', ({ auto, x, y }) => {
        if (auto) return;
        const b = btnCentre(ctx), h = hush;
        const v = 1 - h / 5;
        ctx.audio.play('assets/audio/pop/Pop2.mp3', { bus: 'sfx', gain: 0.1 + v * 0.8 });
        ring(ctx, b.x, b.y, 40 + v * 170, '#d4488f', 600);
        if (h >= 2) {
          const r = ctx.rates();
          ctx.pay(r.fps * h * 2.5, 'quiet', { x: x ?? b.x, y: y ?? b.y });
          setLine(ctx, lines(), `font-style:italic;font-size:${19 - h}px;color:${ink(ctx, '#7a4a5e', '#ffb0d6')};`, 2200);
        }
        hush = 0;
        paint();
      });
      return {};
    },
  },

  /* Warm Glow: your per-second pay keeps climbing for as long as you do nothing, up to three times. A tap puts the fire out. */
  s_glow: {
    mount(ctx) {
      const rest = restClock(ctx), lines = deck(GLOW_LINES, ctx);
      const mul = () => 1 + Math.min(2, rest.seconds / 60);
      ctx.mod({ fps: (v) => v * mul() });
      const g = ctx.el('div', 'position:absolute;left:50%;top:150px;width:90vmin;height:90vmin;margin:-45vmin 0 0 -45vmin;border-radius:50%;background:radial-gradient(circle,rgba(255,226,168,.95) 0,rgba(255,154,74,.75) 35%,rgba(255,154,74,0) 70%);transform:scale(.2);opacity:.4;transition:transform .9s linear,opacity .9s;', null, 'wash');
      const num = ctx.el('b', 'font:700 15px Arial,Helvetica,sans-serif;color:#d9742a;', '', meterRow(ctx));
      let wasHigh = false, n = 0;
      ctx.every(500, () => {
        const m = mul();
        g.style.transform = `scale(${0.2 + (m - 1) * 0.9})`;
        g.style.opacity = String(0.4 + (m - 1) * 0.3);
        num.textContent = `x${(ctx.rates().fps / Math.max(1, ctx.state.base.fps)).toFixed(2)}`;
        if (m > 1.25) wasHigh = true;
        if (++n % 30 === 0 && m > 1.2) ctx.toast(lines());
      });
      ctx.on('tap', ({ auto }) => {
        if (auto || !wasHigh) return;
        wasHigh = false;
        g.style.transition = 'transform .25s ease-in,opacity .25s';
        g.style.transform = 'scale(.2)'; g.style.opacity = '.4';
        ctx.after(300, () => { g.style.transition = 'transform .9s linear,opacity .9s'; });
      });
      return {};
    },
  },

  /* Away Earnings: leave the tab and it keeps your per-second pay running, for up to ten minutes. */
  s_nap: {
    mount(ctx) {
      const doc = docOf(ctx), MAX = 10 * 60 * 1000;
      const titles = deck(NAP_LINES.tabTitles, ctx), welcome = deck(NAP_LINES.welcome, ctx);
      const tag = ctx.el('div', 'font:12px Arial,Helvetica,sans-serif;color:var(--ink-soft);', ROUTE_COPY.napTag, meterRow(ctx));
      if (!ctx.gentle) ctx.anim(tag, [{ opacity: 0.4 }, { opacity: 1 }], { duration: 1500, iterations: Infinity, direction: 'alternate' });
      let oldTitle = null;
      const wake = (ms) => {
        if (ms < 15000) return;
        const capped = ms >= MAX, ask = ctx.rates().fps * Math.min(ms, MAX) / 1000;
        const b = btnCentre(ctx);
        const pay = ctx.pay(ask, 'away', { x: b.x, y: b.y });
        burst(ctx, b.x, b.y, '#ffd24d', 14, 100);
        ctx.modal({ title: ROUTE_COPY.napTitle, body: fill(welcome(), { pay: ctx.fmt(pay) }) + (capped ? ` ${NAP_LINES.capped}` : ''), ms: 0, buttons: [{ label: ROUTE_COPY.napOk }] });
      };
      const onVis = () => {
        if (doc.hidden) {
          ctx.store.set('left', Date.now());
          oldTitle = doc.title;
          doc.title = titles();
        } else {
          if (oldTitle !== null) { doc.title = oldTitle; oldTitle = null; }
          const left = ctx.store.get('left');
          ctx.store.set('left', 0);
          if (left) wake(Date.now() - left);
        }
      };
      doc.addEventListener('visibilitychange', onVis);
      const before = ctx.store.get('left');
      if (before) { ctx.store.set('left', 0); ctx.after(800, () => wake(Date.now() - before)); }
      ctx.every(5000, () => ctx.store.set('seen', Date.now()));
      return {
        off() {
          doc.removeEventListener('visibilitychange', onVis);
          if (oldTitle !== null) doc.title = oldTitle;
        },
      };
    },
  },

  /* Personal Praise: whatever you just bought, someone is very impressed. */
  s_good2: {
    mount(ctx) {
      const next = deck(PRAISE_NAMED, ctx);
      const css = `font-style:italic;font-size:20px;color:${ink(ctx, '#a8246a', '#ffb0d6')};`;
      const praise = (name) => {
        const line = fill(next(), { item: name });
        setLine(ctx, line, `${css}white-space:normal;`, 3200);
      };
      ctx.after(1200, () => praise(ctx.item.name));
      ctx.on('buy', ({ item }) => {
        if (item.id === ctx.item.id) return;
        ctx.after(500, () => {
          praise(item.name);
          const b = btnCentre(ctx);
          burst(ctx, b.x, b.top - 12, '#ffb36b', 8, 50);
        });
      });
      return {};
    },
  },
};
