/* Listen: the game plays for your ears, and the play is timing. Tapping on the tick and on the spoken word is what pays.
 * Nonverbal beds are pre-rendered loops. Spoken words always use real recordings. */

import { EDGE_WORDS, PHONES_PROMPT } from '../../../data/content/index.js';
import { themedDraw } from '../../theme-text.js';
import { MANTRA } from '../../../data/words.js';
import { DARK, darkCss, mountSpiral, spiral, deck, ring, flash, bar, panel, btnCentre, meterRow, setLine, burst, railsRoom, ink, freeSpot, fill } from './kit.js';
import { ROUTE_COPY } from './copy.js';
import { mountGameWave } from '../../live-wave.js';
import { mountMetronome } from './metronome.js';
import { mountCountUp } from '../../../sim/prestige-endings.js';  // prestige hook (Renewal Sky): Count Up ending, spirals pay more

const LOOP = (name) => `assets/audio/ambient/${name}.ogg`;
const binauralOn = (ctx) => ctx.has('a_bin') && ctx.switches.isOn('a_bin');
const say = (slug) => slug.replace(/-/g, ' ');

export default {
  /* Low Hum: fades in under the game, as a wave behind the button. */
  a_hum: {
    mount(ctx) {
      if (!binauralOn(ctx)) ctx.audio.loop(LOOP('hum'), { bus: 'ambient', gain: 0.4, fadeMs: 2500 });  // owner 2026-11-07: half the old 0.8
      return { tick: mountGameWave(ctx) };
    },
  },

  /* Metronome: one tick a second. A tap on the tick pays it again. */
  a_tick: { mount: mountMetronome },

  /* Headphones: it asks once, they drop on, and the wave gets wider. */
  a_phones: {
    mount(ctx) {
      const icon = ctx.el('div', 'position:relative;width:28px;height:20px;', null, meterRow(ctx));
      ctx.el('div', 'position:absolute;left:3px;top:0;width:22px;height:22px;border:3px solid var(--ink);border-bottom-color:transparent;border-radius:50%;box-sizing:border-box;clip-path:inset(0 0 45% 0);', null, icon);
      [0, 21].forEach((x) => ctx.el('div', `position:absolute;left:${x}px;top:9px;width:7px;height:11px;border-radius:3px;background:var(--ink);`, null, icon));
      const landed = () => {
        if (ctx.store.get('on') !== false && !binauralOn(ctx)) { ctx.audio.loop(LOOP('hum'), { bus: 'ambient', gain: 0.2, pan: -0.8 }); ctx.audio.loop(LOOP('hum'), { bus: 'ambient', gain: 0.2, pan: 0.8 }); }
        if (!ctx.gentle) ctx.anim(icon, [{ transform: 'translateY(-60px)' }, { transform: 'translateY(0)' }], { duration: 500, easing: 'ease-in' });
      };
      if (ctx.store.get('on') !== undefined) { landed(); return {}; }
      ctx.modal({
        title: ROUTE_COPY.phonesTitle,
        body: PHONES_PROMPT.ask,
        buttons: [
          { label: PHONES_PROMPT.yes, onPress: () => { ctx.store.set('on', true); ctx.toast(PHONES_PROMPT.thanks[0]); landed(); } },
          { label: PHONES_PROMPT.no, onPress: () => { ctx.store.set('on', false); ctx.toast(PHONES_PROMPT.thanks[1]); landed(); } },
        ],
      });
      return {};
    },
  },

  /* Whispers: small words all over the page, each with a ripple, and a real whispered clip now and then. */
  a_whisp: {
    mount(ctx) {
      const words = themedDraw(ctx, 'edge', deck([...EDGE_WORDS, ...ROUTE_COPY.whispers, ...ROUTE_COPY.whispers], ctx)), clips = deck(MANTRA, ctx);
      const row = ctx.el('div', 'display:flex;align-items:center;gap:4px;font:12px Arial,Helvetica,sans-serif;color:var(--ink-soft);', null, meterRow(ctx));
      ctx.el('span', '', ROUTE_COPY.volume, row).className = 'routes-lbl';
      bar(ctx, row, 'width:40px;height:6px;', 'var(--accent-2)').set(0.1);
      const show = () => {
        const { x, y } = freeSpot(ctx);
        flash(ctx, 'floaters', words(), `left:${x}px;top:${y}px;font:italic 15px Arial,Helvetica,sans-serif;color:${ink(ctx, '#5a3cdc', '#cdbfff')};`, 1600);
        ring(ctx, x + 12, y + 8, 34, 'color-mix(in srgb,var(--accent) 50%,transparent)', 900);
      };
      show();
      ctx.every(ctx.gentle ? 1200 : 700, show);
      ctx.every(4600, () => ctx.voice(clips(), { gain: 0.16 }));
      return {};
    },
  },

  /* A Voice: one spoken word every few seconds, shown in a bubble. Tap while it talks and it pays. */
  a_voice: {
    mount(ctx) {
      const clips = deck(MANTRA, ctx);
      let open = 0;
      const speak = () => {
        const slug = clips();
        ctx.voice(slug, { gain: 0.36 });   // owner 2026-11-08: "super loud", 60% lower than the old 0.9
        open = ctx.state.clock + 1500;
        setLine(ctx, say(slug), 'font-size:21px;font-weight:700;color:var(--ink);', 1700);
        const b = btnCentre(ctx);
        ring(ctx, b.x, b.top - 12, 90, 'var(--accent-2)', 700);
      };
      speak();
      ctx.every(6000, speak);
      ctx.on('tap', ({ auto, x, y }) => {
        if (auto || ctx.state.clock > open) return;
        open = 0;
        const b = btnCentre(ctx), r = ctx.rates();
        ctx.pay(Math.max(r.click * 3, r.fps), 'voice', { x: x ?? b.x, y: y ?? b.top });
      });
      return {};
    },
  },

  /* Binaural Hum: the wave splits in two, one colour each side, with a dot sitting between them. */
  a_bin: {
    mount(ctx) {
      ctx.audio.loop(LOOP('hum-left'), { bus: 'ambient', gain: 0.3, pan: -1 });  // owner 2026-11-08: binaural lower
      ctx.audio.loop(LOOP('hum-right'), { bus: 'ambient', gain: 0.3, pan: 1 });
      const b = btnCentre(ctx);
      const tag = (side, text, colour) => ctx.el('div', `position:absolute;${side}:12px;top:${b.y - 96}px;font:600 13px Arial,Helvetica,sans-serif;color:${colour};z-index:3;`, text, 'back').className = 'routes-lbl';
      tag('left', ROUTE_COPY.left, 'var(--accent)');
      tag('right', ROUTE_COPY.right, '#2f8fd0');
      const dot = ctx.el('div', `position:absolute;left:50%;top:${b.y - 7}px;margin-left:-98px;width:14px;height:14px;border-radius:50%;background:#ffd24d;z-index:3;`, null, 'back');
      const dot2 = ctx.el('div', `position:absolute;left:50%;top:${b.y - 7}px;margin-left:84px;width:14px;height:14px;border-radius:50%;background:#ffd24d;z-index:3;`, null, 'back');
      for (const d of [dot, dot2]) ctx.anim(d, ctx.gentle ? [{ opacity: 0.5 }, { opacity: 1 }] : [{ transform: 'scale(.6)' }, { transform: 'scale(1.5)' }], { duration: 900, iterations: Infinity, direction: 'alternate' });
      return {};
    },
  },

  /* Rain: the page goes dark blue and it rains, drawn on one canvas, with rings where it lands. */
  a_rain: {
    mount(ctx) {
      ctx.vars(DARK);
      ctx.vars({ '--page': '#232a38', '--panel': '#2d3546', '--panel-short': '#262d3c', '--fill': '#3f4a60', '--line': '#6f7c96', '--btn': '#3a4358' });
      darkCss(ctx);
      ctx.audio.loop(LOOP('rain'), { bus: 'ambient', gain: 0.5, fadeMs: 3000 });
      const c = ctx.el('canvas', 'position:absolute;inset:0;width:100%;height:100%;z-index:1;', null, 'back');
      const g = c.getContext('2d');
      let drops = [], spent = 0, seen = null;
      const fit = () => {
        seen = ctx.bounds();
        c.width = seen.w; c.height = seen.h;
        const n = Math.min(70, Math.round(seen.w / 14));
        drops = Array.from({ length: n }, () => ({ x: ctx.rng() * seen.w, y: ctx.rng() * seen.h, v: 500 + ctx.rng() * 400 }));
      };
      fit();
      ctx.every(600, () => { if (!ctx.gentle) ring(ctx, 20 + ctx.rng() * (seen.w - 40), seen.h * (0.4 + ctx.rng() * 0.55), 34, 'rgba(159,198,238,.7)', 800, 'back'); });
      return {
        tick(dt) {
          spent += dt;
          if (spent < 1 / 24) return;
          const step = spent * (ctx.gentle ? 0.15 : 1);
          spent = 0;
          if (seen !== ctx.bounds()) fit();
          g.clearRect(0, 0, c.width, c.height);
          g.strokeStyle = 'rgba(159,198,238,.75)'; g.lineWidth = 1.5;
          g.beginPath();
          for (const d of drops) {
            d.y += d.v * step;
            if (d.y > seen.h) { d.y = -14; d.x = ctx.rng() * seen.w; }
            g.moveTo(d.x, d.y); g.lineTo(d.x, d.y + 13);
          }
          g.stroke();
        },
      };
    },
  },

  /* Late Spiral: the thing the other path got in the first minute arrives now, slowly, with one word. */
  a_spiral: {
    mount(ctx) {
      const sp = mountSpiral(ctx, { opacity: 0.5, fadeMs: 5000 });
      sp.speedMul = 0.7;
      mountCountUp(ctx);  // prestige hook: Count Up ending (tr_end)
      sp.el.style.zIndex = '0';
      ctx.after(2600, () => setLine(ctx, ROUTE_COPY.finally, 'font-size:22px;font-weight:700;color:#fff;text-shadow:0 1px 3px #000;', 2400));
      ctx.on('tap', () => { sp.kick = Math.min(0.8, sp.kick + 0.08); });
      return { off() { sp.dispose(); } };
    },
  },

  /* Chant: a word, and a second voice answers it from the other side. Rails on a wide screen, beside the button on a phone. */
  a_chant: {
    mount(ctx) {
      const clips = deck(MANTRA, ctx);
      const chant = () => {
        const slug = clips(), wide = railsRoom(ctx), b = btnCentre(ctx), { w, h } = ctx.bounds();
        const big = wide ? Math.min(40, (w - 800) / 2 / Math.max(5, say(slug).length) * 1.5) : 17;
        const y = wide ? h * 0.42 : b.y - 12, sideW = wide ? (w - 800) / 2 : (w - b.w) / 2 - 6;
        ctx.voice(slug, { gain: 0.9, pan: -0.7 });
        flash(ctx, 'floaters', say(slug), `left:3px;width:${sideW}px;top:${y}px;text-align:center;font:700 ${big}px/1 Arial,Helvetica,sans-serif;color:${ink(ctx, '#5a3cdc', '#d7c9ff')};`, 1500);
        ctx.after(550, () => {
          ctx.voice(slug, { gain: 0.45, pan: 0.7 });
          flash(ctx, 'floaters', say(slug), `right:3px;width:${sideW}px;top:${y + (wide ? 34 : 0)}px;text-align:center;font:italic 600 ${big * 0.85}px/1 Arial,Helvetica,sans-serif;color:${ink(ctx, '#c23a83', '#ffb0d6')};`, 1400);
        });
      };
      chant();
      ctx.every(3000, chant);
      return {};
    },
  },

  /* Close Mic: a word starts far away and comes right up to you, and the meter says how close. */
  a_close: {
    mount(ctx) {
      const clips = deck(MANTRA, ctx);
      const row = ctx.el('div', 'display:flex;align-items:center;gap:4px;font:12px Arial,Helvetica,sans-serif;color:var(--ink-soft);', null, meterRow(ctx));
      ctx.el('span', '', ROUTE_COPY.far, row).className = 'routes-lbl';
      const m = bar(ctx, row, 'width:50px;height:6px;', 'var(--accent)');
      ctx.el('span', 'color:var(--stamp);', ROUTE_COPY.close, row).className = 'routes-lbl';
      const come = () => {
        const slug = clips(), b = btnCentre(ctx), big = Math.min(ctx.bounds().w / Math.max(4, say(slug).length) * 1.4, 130);
        const e = ctx.el('div', `position:absolute;left:0;right:0;top:${Math.max(70, b.y - big / 2)}px;text-align:center;font:700 ${big}px/1 Arial,Helvetica,sans-serif;color:var(--accent-soft);text-shadow:0 0 8px #000;`, say(slug));
        const fin = ctx.gentle ? [{ opacity: 0 }, { opacity: 0.7 }, { opacity: 0 }] : [{ transform: 'scale(.12)', opacity: 0 }, { transform: 'scale(1)', opacity: 0.72, offset: 0.9 }, { transform: 'scale(1.1)', opacity: 0 }];
        ctx.anim(e, fin, { duration: 3200, easing: 'ease-in', fill: 'both' });
        ctx.after(3220, () => e.remove());
        m.set(0); m.set(1, 3000);
        ctx.after(3200, () => m.set(0, 800));
        ctx.after(2800, () => ctx.voice(slug, { gain: 1 }));
        ctx.after(2900, () => { const c = btnCentre(ctx); burst(ctx, c.x, c.y, 'var(--accent-soft)', 10, 60); });
      };
      ctx.after(300, come);
      ctx.every(11000, come);
      return {};
    },
  },
};
