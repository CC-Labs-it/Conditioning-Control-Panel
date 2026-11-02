/* Trance: night blue, everything slow. The signature: this theme pays you to stop. Hands off and the rate climbs; a tap
 * at the top of the breath is the one tap that does not break it. Spec: ui/demos/themes1.js. */

import { THEMES, ENDINGS } from '../../../data/content/index.js';
import { createSpiral } from '../../../fx/spiral.js';
import { mountPodcast } from '../podcast-player.js';
import { bag, geo, counterEl, sheet, done, frame, sideFrames, gentleSwitch } from './kit.js';
import { endingShow } from './endings.js';
import { COPY } from './copy.js';
import { THEME_TASTES } from '../theme-skins.js';
import { THEME_PAGES } from '../../palette.js';

const R = THEMES.trance.rules;
const NICHES = ['spirals', 'soft voices', 'pendulum', 'eyes closed'];
const BREATH_MS = 8000;
const BREATH_OPEN = [0.38, 0.62];       // the top of the breath, as a share of one cycle
const breathAt = (now) => ((now % BREATH_MS) + BREATH_MS) % BREATH_MS / BREATH_MS;
const isOpen = (now) => { const p = breathAt(now); return p >= BREATH_OPEN[0] && p <= BREATH_OPEN[1]; };
const SWING_MS = 3900;

const VARS = {
  ...THEME_PAGES.trance,
  '--font': '"Barlow","Segoe UI",system-ui,sans-serif',
};
const CSS = `#btn{border:1px solid var(--btn-edge);border-radius:27px;}
#btn.down{border-style:solid;}
.row{border-radius:8px;}
.toast{border-radius:12px;}`;

/** Night falls from the edges in; the spiral is already turning when it clears. */
function moment(ctx) {
  ctx.audio.play('assets/audio/sfx/reveal_chime.mp3', { bus: 'sfx', gain: 0.7 });
  ctx.voice('sink');
  const g = geo(ctx);
  const s = sheet(ctx, 'background:#15123a;display:grid;place-items:center;color:#dfe4ff;font-size:clamp(34px,8vw,72px);letter-spacing:.08em;');
  const word = ctx.el('div', '', COPY.trance.moment, s);
  if (ctx.gentle) { done(ctx.anim(s, [{ opacity: 0 }, { opacity: 1, offset: 0.3 }, { opacity: 1, offset: 0.6 }, { opacity: 0 }], { duration: 1400 }), s); return; }
  const R0 = Math.hypot(g.w, g.h);
  ctx.anim(word, [{ transform: 'translateY(-46px)', opacity: 0, letterSpacing: '.5em' }, { transform: 'translateY(0)', opacity: 1, letterSpacing: '.08em', offset: 0.36 }, { transform: 'translateY(16px)', opacity: 1, letterSpacing: '.06em', offset: 0.62 }, { transform: 'translateY(80px)', opacity: 0, letterSpacing: '.02em' }], { duration: 2000, easing: 'ease-in-out', fill: 'forwards' });
  done(ctx.anim(s, [
    { clipPath: `circle(0px at ${g.cx}px ${g.cy}px)`, opacity: 1, offset: 0, easing: 'ease-out' },
    { clipPath: `circle(${R0}px at ${g.cx}px ${g.cy}px)`, opacity: 1, offset: 0.3 },
    { clipPath: `circle(${R0}px at ${g.cx}px ${g.cy}px)`, opacity: 1, offset: 0.62, easing: 'ease-in-out' },
    { clipPath: `circle(${R0}px at ${g.cx}px ${g.cy}px)`, opacity: 0, offset: 1 },
  ], { duration: 2000 }), s);
}

/** A line of its own that fades in and out above the button. Calm words never pile up. */
function murmur(ctx, text, css = '') {
  const g = geo(ctx);
  const e = ctx.el('div', `position:absolute;left:0;right:0;top:${g.cy + g.bh / 2 + 4}px;text-align:center;color:var(--ink-soft);font-size:15px;${css}`, text, 'floaters');
  done(ctx.anim(e, [{ opacity: 0 }, { opacity: 1, offset: 0.2 }, { opacity: 1, offset: 0.7 }, { opacity: 0 }], { duration: 2600 }), e);
}

export const START = { theme: 'trance', vars: VARS, css: CSS, moment };

export default {
  tr_head: {
    mount(ctx) {
      /* One spiral is enough on a phone: a spiral bought on the Watch route keeps turning, this one stays home. */
      let sp = null;
      if (!ctx.has('e_spiral') && !ctx.has('a_spiral')) {
        const c = ctx.el('canvas', 'position:absolute;left:50%;top:50%;width:110vmax;height:110vmax;transform:translate(-50%,-50%);opacity:.2;', null, 'back');
        sp = createSpiral(c, { reduced: ctx.gentle, manual: true, ctx });
        sp.show();
        ctx.frame((dt) => sp.step(dt));
        ctx.every(500, () => sp.setStill(ctx.gentle));
      }
      sideFrames(ctx, ['spirals', 'soft voices', 'hypnosis', 'pendulum'], 'border-radius:12px;opacity:.85;');
      return { off() { sp?.dispose(); } };
    },
  },

  /* Stillness Bonus: earnings climb while the hands stay off. */
  tr_still: {
    mount(ctx) {
      const b = bag(ctx);
      let last = ctx.state.clock, lineAt = 0, li = 0;
      const stillFor = (now) => Math.max(0, now - last) / 1000;
      const mult = (clock) => 1 + Math.min(1, stillFor(clock) / 40);
      ctx.mod({ fps: (v, s) => v * mult(s.clock) });
      const line = ctx.el('div', 'display:flex;align-items:center;justify-content:center;gap:12px;height:22px;font-size:14px;color:var(--ink-soft);', null, 'meters');
      b.meterRow = line;
      const box = ctx.el('span', 'display:flex;align-items:center;gap:6px;', null, line);
      ctx.el('span', '', COPY.trance.handsOff, box);
      const track = ctx.el('span', 'position:relative;width:clamp(48px,12vw,110px);height:8px;border-radius:4px;background:rgba(255,255,255,.14);overflow:hidden;', null, box);
      const fill = ctx.el('span', 'position:absolute;inset:0;border-radius:4px;background:linear-gradient(90deg,#b79cff,#8fd0ff);transform-origin:0 50%;transform:scaleX(0);', null, track);
      const num = ctx.el('span', 'min-width:2em;color:var(--stamp);font-variant-numeric:tabular-nums;', '', box);
      ctx.on('tap', (e) => {
        if (e.auto) return;
        if (ctx.has('tr_breath') && b.breathOpenNow?.()) return;   // the one tap that does not count as moving
        const had = stillFor(ctx.state.clock);
        last = ctx.state.clock;
        if (had > 8) murmur(ctx, R.still[4]);
      });
      ctx.frame((dt, now) => {
        const m = mult(ctx.state.clock);
        fill.style.transform = `scaleX(${(m - 1).toFixed(3)})`;
        if (now > lineAt) {
          lineAt = now + 500;
          num.textContent = `x${(ctx.rates().fps / Math.max(1, ctx.state.base.fps)).toFixed(1)}`;
          const s = stillFor(ctx.state.clock);
          if (s > 12 + li * 12 && li < 4) { murmur(ctx, R.still[li]); li += 1; }
          if (s < 2) li = 0;
        }
      });
      return {};
    },
  },

  /* Breathing Ring: tap at the top of the breath for a much bigger tap. */
  tr_breath: {
    mount(ctx) {
      const b = bag(ctx);
      let clock = 0;
      b.breathOpenNow = () => isOpen(clock);
      ctx.mod({ click: (v, s, now) => (isOpen(now) ? v * 4 : v) });
      const size = () => (ctx.bounds().w < 760 ? 148 : 170);
      let g = geo(ctx), D = size();
      const wrap = ctx.el('div', `position:absolute;left:0;top:0;width:${D}px;height:${D}px;`, null, 'floaters');
      const rim = ctx.el('div', 'position:absolute;inset:0;border-radius:50%;box-sizing:border-box;border:2px dashed #8fd0ff;opacity:.55;', null, wrap);
      const ring = ctx.el('div', 'position:absolute;inset:0;border-radius:50%;background:radial-gradient(circle,rgba(143,208,255,.05),rgba(143,208,255,.42));will-change:transform;', null, wrap);
      const place = () => { g = geo(ctx); D = size(); wrap.style.width = wrap.style.height = `${D}px`; wrap.style.transform = `translate(${g.cx - D / 2}px,${g.cy - D / 2}px)`; };
      place();
      ctx.every(1000, place);
      const word = ctx.el('div', `position:absolute;left:0;right:0;top:-2px;text-align:center;font-size:13px;color:var(--ink-soft);`, null, wrap);
      let shownOpen = false, shownWord = -1;
      ctx.frame((dt, now) => {
        clock = now;
        const p = breathAt(now);
        const up = p < 0.5 ? p * 2 : (1 - p) * 2;                     // 0 at empty, 1 at the top
        const eased = up * up * (3 - 2 * up);
        if (ctx.gentle) ring.style.opacity = (0.2 + eased * 0.8).toFixed(2);
        else { ring.style.opacity = 1; ring.style.transform = `scale(${(0.45 + eased * 0.55).toFixed(3)})`; }
        const open = isOpen(now);
        if (open !== shownOpen) { shownOpen = open; rim.style.opacity = open ? 1 : 0.55; rim.style.borderStyle = open ? 'solid' : 'dashed'; }
        const w = p < 0.38 ? 0 : p <= 0.62 ? 2 : 1;
        if (w !== shownWord) { shownWord = w; word.textContent = w === 2 ? R.breath[2] : w === 0 ? R.breath[0] : R.breath[1]; }
      });
      ctx.on('tap', (e) => {
        if (!isOpen(clock) || e.auto) return;
        ctx.float(g.cx, g.cy - g.bh, COPY.trance.breathPay(ctx.fmt(e.gain)), 'color:#8fd0ff;font-size:20px;');
        ctx.audio.play('assets/audio/sfx/chime1.mp3', { bus: 'sfx', gain: 0.5 });
        if (!ctx.gentle) ctx.anim(rim, [{ transform: 'scale(1)' }, { transform: 'scale(1.08)' }, { transform: 'scale(1)' }], 360);
      });
      return { off() { b.breathOpenNow = null; } };
    },
  },

  /* Trance Pictures: a slow belt of pictures behind the counter. */
  tr_feed: {
    mount(ctx) {
      const W = () => (ctx.bounds().w >= 760 ? 190 : 120);
      const belt = [];
      const spawn = (lead = 0) => {
        if (belt.length >= 5) return;
        const w = W(), { w: pw } = ctx.bounds(), g = geo(ctx);
        const h = Math.min(g.stageBottom - 8, w * 1.3);
        const f = frame(ctx, `position:absolute;left:0;top:6px;width:${w}px;height:${h}px;border-radius:10px;opacity:.5;will-change:transform;`, NICHES[Math.floor(ctx.rng() * NICHES.length)]);
        belt.push(f);
        const ms = ((pw + w) / 34) * 1000;
        const a = ctx.anim(f.el, [{ transform: `translateX(${pw}px)` }, { transform: `translateX(${-w}px)` }], { duration: ms, easing: 'linear' });
        if (lead) a.currentTime = lead;
        a.finished?.then(() => { f.el.remove(); const n = belt.indexOf(f); if (n >= 0) belt.splice(n, 1); }, () => {});
      };
      ctx.on('pictures', () => { for (const f of belt) f.next(); });
      gentleSwitch(ctx, () => {
        spawn(); spawn(5200); spawn(10400);
        const stop = ctx.every(5600, () => spawn());
        return () => { stop(); for (const f of [...belt]) f.el.remove(); belt.length = 0; };
      }, () => {
        /* Gentle: no belt. Three still frames that change one at a time. */
        const list = [0, 1, 2].map((i) => frame(ctx, `position:absolute;left:${6 + i * 33}%;top:6px;width:30%;max-width:190px;height:${Math.min(geo(ctx).stageBottom - 8, 220)}px;border-radius:10px;opacity:.5;`, NICHES[i]));
        belt.push(...list);
        let k = 0;
        const stop = ctx.every(8000, () => list[k++ % 3].next());
        return () => { stop(); for (const f of list) f.el.remove(); belt.length = 0; };
      });
      return {};
    },
  },

  /* Pendulum Button: it swings. A tap as it passes the middle pays double. */
  tr_skin: {
    mount(ctx) {
      const b = ctx.button;
      b.skin('tr-pendulum');
      ctx.css(THEME_TASTES.trance.css);
      const mid = (now) => Math.abs(Math.sin((now / SWING_MS) * Math.PI * 2)) < 0.26;
      ctx.mod({ click: (v, s, now) => (!ctx.gentle && mid(now) ? v * 2 : v) });
      const el = b.el;
      let clock = 0;
      ctx.frame((dt, now) => { clock = now; if (!ctx.gentle) el.style.rotate = `${(Math.sin((now / SWING_MS) * Math.PI * 2) * 15).toFixed(2)}deg`; else el.style.rotate = ''; });
      ctx.on('tap', (e) => {
        if (e.auto || ctx.gentle || !mid(clock)) return;
        ctx.float(e.x ?? ctx.bounds().w / 2, (e.y ?? 150) - 26, COPY.trance.middle, 'color:#b79cff;');
      });
      return { off() { el.style.rotate = ''; } };
    },
  },

  tr_pod: { mount: (ctx) => mountPodcast(ctx, { theme: 'trance', show: 'True Trance', accent: '#8fd0ff' }) },

  /* Count Down: every few minutes, a slow ten to one that pays at the end. */
  tr_count: {
    mount(ctx) {
      const STEP = 2200;
      const run = () => {
        const g0 = geo(ctx), side = Math.max(96, g0.cx - g0.bw / 2 - 20);
        const box = ctx.el('div', `position:absolute;left:8px;top:${g0.cy - 44}px;width:${Math.min(side, 240)}px;text-align:center;color:#8fd0ff;`, null, 'floaters');
        const big = ctx.el('div', `position:absolute;left:0;right:0;top:${g0.cy - 90}px;text-align:center;font-size:180px;line-height:1;opacity:0;color:#8fd0ff;`, null, 'floaters');
        const line = ctx.el('div', 'font-size:15px;line-height:1.15;color:var(--ink);', null, box);
        const dots = ctx.el('div', 'margin-top:6px;line-height:8px;', null, box);
        const pips = [];
        for (let i = 0; i < 10; i++) pips.push(ctx.el('span', 'display:inline-block;width:7px;height:7px;margin:0 2px;border-radius:50%;background:#b79cff;opacity:.2;', null, dots));
        let n = 10;
        const show = () => {
          big.textContent = n;
          line.textContent = R.count[10 - n];
          ctx.anim(big, [{ opacity: 0 }, { opacity: 0.42, offset: 0.25 }, { opacity: 0.3 }], { duration: STEP, fill: 'forwards' });
          pips.forEach((p, i) => { p.style.opacity = i <= 10 - n ? 1 : 0.2; });
          ctx.audio.play('assets/audio/sfx/count_tick.mp3', { bus: 'sfx', gain: 0.5 });
        };
        show();
        const stop = ctx.every(STEP, () => {
          n -= 1;
          if (n >= 1) { show(); return; }
          stop();
          const g = geo(ctx);
          ctx.pay(ctx.rates().fps * 25, 'tr_count', { x: g.w / 2, y: g.h * 0.3 });
          ctx.voice('deeper');
          ctx.audio.play('assets/audio/sfx/reveal_chime.mp3', { bus: 'sfx', gain: 0.6 });
          done(ctx.anim(box, [{ opacity: 1 }, { opacity: 0 }], { duration: 900, fill: 'forwards' }), box);
          done(ctx.anim(big, [{ opacity: 0.3 }, { opacity: 0 }], { duration: 900, fill: 'forwards' }), big);
        });
      };
      run();
      ctx.every(170000, run);
      return {};
    },
  },

  /* Fading Page: the shop and counter fade while you sit still. Any move brings them back at once. */
  tr_fade: {
    mount(ctx) {
      const doc = ctx.world.ownerDocument;
      const parts = [ctx.parts.shop, counterEl(ctx), ctx.layer('meters'), ctx.layer('banner')].filter(Boolean);
      for (const p of parts) p.style.transition = 'opacity .35s';
      let last = ctx.state.clock, shown = 1;
      const wake = () => { last = ctx.state.clock; paint(1); };
      const paint = (o) => { if (o === shown) return; shown = o; for (const p of parts) p.style.opacity = o; };
      ctx.every(300, () => {
        const s = (ctx.state.clock - last) / 1000;
        paint(s < 6 ? 1 : Math.max(0.1, +(1 - (s - 6) / 10).toFixed(2)));
      });
      const opts = { passive: true, capture: true };
      for (const ev of ['pointerdown', 'pointermove', 'keydown']) doc.addEventListener(ev, wake, opts);
      return {
        off() {
          for (const ev of ['pointerdown', 'pointermove', 'keydown']) doc.removeEventListener(ev, wake, opts);
          for (const p of parts) { p.style.opacity = ''; p.style.transition = ''; }
        },
      };
    },
  },

  /* Slow Everything: effects drag while earnings rise. A tap that is not on the breath lifts you back a little. */
  tr_deep: {
    mount(ctx) {
      const b = bag(ctx);
      let depth = ctx.store.get('depth') || 0, lineAt = 0, li = 0;
      ctx.mod({ fps: (v) => v * (1 + depth) });
      ctx.on('tap', (e) => { if (!e.auto && !b.breathOpenNow?.()) depth = Math.max(0, depth - 0.08); });
      const box = ctx.el('span', 'display:flex;align-items:center;gap:6px;', null, b.meterRow || ctx.layer('meters'));
      ctx.el('span', '', COPY.trance.deeper, box);
      const track = ctx.el('span', 'position:relative;width:clamp(48px,12vw,110px);height:8px;border-radius:4px;background:rgba(255,255,255,.14);overflow:hidden;', null, box);
      const fill = ctx.el('span', 'position:absolute;inset:0;background:#b79cff;transform-origin:0 50%;', null, track);
      const pct = ctx.el('span', 'min-width:2em;color:var(--stamp);font-variant-numeric:tabular-nums;', null, box);
      /* A hold multiplies, so each step lets go of the last. Down to 0.4: slow, and the breath still comes round. */
      let release = ctx.speed(1 - 0.6 * depth);
      ctx.every(600, () => {
        ctx.store.set('depth', depth);
        release();
        release = ctx.speed(+(1 - 0.6 * depth).toFixed(2));
      });
      ctx.frame((dt, now) => {
        depth = Math.min(1, depth + dt / 45);
        fill.style.transform = `scaleX(${depth.toFixed(3)})`;
        if (now > lineAt) {
          lineAt = now + 500;
          pct.textContent = `x${(ctx.rates().fps / Math.max(1, ctx.state.base.fps)).toFixed(1)}`;
          if (depth > 0.3 * (li + 1) && li < R.deep.length) { murmur(ctx, R.deep[li]); li += 1; }
        }
      });
      return {};
    },
  },

  tr_end: {
    mount() { return {}; },
    ending(ctx) { return endingShow(ctx, ENDINGS.tr_end, 'trance'); },
  },
};
