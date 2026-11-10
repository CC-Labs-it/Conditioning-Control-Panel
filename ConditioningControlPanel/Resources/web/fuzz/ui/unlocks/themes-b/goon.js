/* ui/unlocks/themes-b/goon.js - lane goon. Night amber, a dark room, one screen. The mechanic: hold the button, let go in the zone. */

import { THEMES } from '../../../data/content/themes.js';
import { mountPodcast } from '../podcast-player.js';
import { COPY } from './copy.js';
import { themeScreens } from '../themes-a/theme-screens.js';
import takeover, { emiTakeoverTake } from './emi-takeover.js';
import { buttonCentre, docOf, fresh, gallery, moment, motion, pick, ring, sfx, slot, tile } from './kit.js';

const R = THEMES.goon.rules;
const HOT = '#f2a33c';

export const goonPick = {
  /* The pick: lights off. Stage stays, the room is gone inside a second. */
  arrive(ctx) {
    moment(ctx, 'background:#000;', [{ opacity: 0 }, { opacity: 1, offset: 0.25 }, { opacity: 1, offset: 0.5 }, { opacity: 0 }], 1100);
    sfx(ctx, 'thud');
  },
};

export default {
  go_head: {
    mount(ctx) {
      /* The first pictures: three screens (owner 2026-11-08). The side columns wait for Goon Pictures. */
      themeScreens(ctx, { niches: ['goon'], frame: 'border:2px solid #333;border-radius:4px;box-shadow:0 0 16px rgba(242,163,60,.3);' });
      return {};
    },
  },

  /* Hold to Earn owns the meter and the release. Release Zone only turns the zone on. */
  go_hold: {
    mount(ctx) {
      const btn = ctx.button.el, wrap = ctx.parts.buttonWrap, win = docOf(ctx).defaultView;
      ctx.css('#btn{touch-action:none;}');
      const box = ctx.el('div', 'position:absolute;left:calc(100% + 14px);top:50%;width:20px;height:92px;margin-top:-46px;background:rgba(0,0,0,.45);border:1px solid var(--line);border-radius:4px;overflow:hidden;pointer-events:none;', null, wrap);
      const fillEl = ctx.el('div', `position:absolute;inset:0;background:${HOT};transform-origin:50% 100%;transform:scaleY(0);`, null, box);
      const zoneEl = ctx.el('div', 'position:absolute;left:0;right:0;border-top:2px solid #fff;border-bottom:2px solid #fff;display:none;', null, box);
      const mult = ctx.el('div', `position:absolute;left:calc(100% + 38px);top:50%;margin-top:-9px;font:700 14px var(--font);color:${HOT};pointer-events:none;`, '', wrap);

      let held = false, heldFor = 0, fill = 0, burnt = false, payT = 0, bank = 0, said = 0;
      let streak = ctx.store.get('streak') || 0;
      let zone = { lo: 0.62, hi: 0.8 };
      const newZone = () => {
        const size = Math.max(0.07, 0.18 - streak * 0.02);
        const lo = 0.5 + ctx.rng() * (0.9 - size - 0.5);
        zone = { lo, hi: lo + size };
        zoneEl.style.bottom = `${lo * 100}%`;
        zoneEl.style.height = `${size * 100}%`;
      };
      newZone();
      const multOf = () => 1 + Math.min(4, streak) * 0.5;
      const drawMult = () => { mult.textContent = '+'.repeat(Math.min(4, streak)); };
      drawMult();

      const down = (ev) => { if (ev.button > 0) return; held = true; heldFor = 0; payT = 0; said = 0; };
      const up = () => {
        if (!held) return;
        held = false;
        if (burnt || heldFor < 0.2 || !ctx.has('go_edge')) return;
        const { x, y } = buttonCentre(ctx);
        if (fill >= zone.lo && fill <= zone.hi) {
          const centre = (zone.lo + zone.hi) / 2, spot = Math.abs(fill - centre) < (zone.hi - zone.lo) * 0.18;
          const r = ctx.rates();
          const gain = (bank + r.click * 14 + r.fps * 1.5) * multOf() * (spot ? 2 : 1);
          ctx.pay(gain, 'release', { x, y });
          ctx.float(x, y - 44, pick(ctx, R.released), `color:${HOT};font-size:17px;font-weight:700;`);
          ring(ctx, HOT);
          sfx(ctx, spot ? 'chime2' : 'chime1');
          streak += 1;
          fill = 0;
        } else {
          ctx.float(x, y - 44, COPY.goon.notYet, 'color:var(--ink-soft);');
          streak = 0;
        }
        bank = 0;
        ctx.store.set('streak', streak);
        drawMult();
        newZone();
      };
      btn.addEventListener('pointerdown', down);
      win.addEventListener('pointerup', up);
      win.addEventListener('pointercancel', up);
      win.addEventListener('blur', up);
      ctx.on('pause', () => { held = false; });

      return {
        tick(dt) {
          const edge = ctx.has('go_edge');
          zoneEl.style.display = edge ? 'block' : 'none';
          if (held && !burnt) {
            heldFor += dt;
            if (heldFor > 0.16) {
              fill += dt * 0.24 * (1 + fill * 0.7);
              payT += dt;
              while (payT > 0.2) { payT -= 0.2; ctx.button.press(); bank += ctx.rates().click * 0.5; }
              if (edge) {
                const line = fill < 0.3 ? 0 : fill < zone.lo - 0.08 ? 1 : fill < zone.lo ? 2 : fill <= zone.hi ? 3 : -1;
                if (line > said || (line === 3 && said !== 3)) {
                  said = line;
                  const { x, y } = buttonCentre(ctx);
                  ctx.float(x, y - 44, R.edge[line], `color:${line === 3 ? '#fff' : 'var(--ink-soft)'};font-weight:700;`);
                  if (line === 3) sfx(ctx, 'tally_tick', 0.7);
                }
                if (fill >= 1) {
                  burnt = true; bank = 0; streak = 0;
                  ctx.store.set('streak', 0);
                  drawMult();
                  const c = buttonCentre(ctx);
                  ctx.float(c.x, c.y - 44, pick(ctx, R.overfill), 'color:#ff5a4a;font-weight:700;font-size:16px;');
                  sfx(ctx, 'thud');
                  fillEl.style.background = '#ff5a4a';
                  if (!ctx.gentle) ctx.anim(box, [{ transform: 'translateX(-3px)' }, { transform: 'translateX(3px)' }, { transform: 'none' }], 180);
                }
              }
              fill = Math.min(1, fill);
            }
          } else {
            fill = Math.max(0, fill - dt * (burnt ? 1.5 : 0.6));
            if (burnt && fill <= 0) { burnt = false; fillEl.style.background = HOT; newZone(); }
          }
          fillEl.style.transform = `scaleY(${fill})`;
        },
        off() {
          btn.removeEventListener('pointerdown', down);
          win.removeEventListener('pointerup', up);
          win.removeEventListener('pointercancel', up);
          win.removeEventListener('blur', up);
        },
      };
    },
  },

  /* The zone appears on the meter; go_hold draws it as soon as this is owned. */
  go_edge: {
    mount(ctx) {
      if (fresh(ctx)) { const { x, y } = buttonCentre(ctx); ctx.float(x, y - 44, R.edge[0], `color:${HOT};font-weight:700;font-size:16px;`); }
      return {};
    },
  },

  /* Every collection at once: columns creep up both sides of the room. */
  go_feed: { mount(ctx) { gallery(ctx, { niche: 'goon', mode: 'scroll' }); return {}; } },

  /* Tabs pile up across the top. Closing one pays a little, and opens two later. */
  go_tabs: {
    mount(ctx) {
      ctx.css('#stage{padding-top:70px !important;}');
      const max = ctx.bounds().w < 760 ? 6 : 14;
      const bar = ctx.el('div', 'position:absolute;left:max(6px,calc(50% - 380px));right:max(92px,calc(50% - 380px));top:38px;height:26px;display:flex;gap:1px;align-items:flex-end;pointer-events:none;');
      const titles = R.tabs;
      let n = ctx.store.get('n') || 0, closed = ctx.store.get('closed') || 0, owed = 0;
      const open = () => {
        if (bar.childElementCount >= max) bar.firstChild?.remove();
        const t = ctx.el('button', `flex:1 1 0;min-width:0;height:24px;padding:0 5px;text-align:left;background:#2a2019;color:#f7e6cf;border:1px solid ${HOT};border-bottom:0;border-radius:4px 4px 0 0;font:11px var(--font);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;pointer-events:auto;touch-action:manipulation;`, titles[n % titles.length], bar);
        t.type = 'button';
        n += 1;
        ctx.store.set('n', n);
        ctx.anim(t, [{ opacity: 0, transform: 'translateY(8px)' }, { opacity: 1, transform: 'none' }], 200);
        t.addEventListener('click', (ev) => {
          t.remove();
          closed += 1; owed += 2;
          ctx.store.set('closed', closed);
          ctx.pay(ctx.rates().fps * 0.6, 'tab', { x: ev.clientX, y: ev.clientY });
          sfx(ctx, 'chip_pop', 0.5);
        });
      };
      for (let i = 0; i < Math.min(max, 4 + closed); i++) open();
      ctx.every(5000, () => { open(); if (owed > 0) { owed -= 1; open(); } });
      return {};
    },
  },

  go_worn: { mount(ctx) { ctx.button.skin('worn'); } },

  /* Curtains shut. The page sinks, and one lit screen stays behind the counter. */
  go_cave: {
    mount(ctx) {
      ctx.vars({ '--page': '#060403', '--panel': '#150f0a', '--panel-short': '#0e0a07', '--line': '#3f2f20' });
      const b = ctx.bounds();
      ctx.el('div', 'position:absolute;inset:0;background:rgba(0,0,0,.7);', null, 'wash');
      const w = Math.min(640, b.w - 16), h = Math.min(210, b.h * 0.3);
      const left = (b.w - w) / 2;
      const screen = tile(ctx, 'wash', `left:${left}px;top:30px;width:${w}px;height:${h}px;border:3px solid #333;border-radius:4px;background:#0b1a22;box-shadow:0 0 70px 18px rgba(159,220,240,.4);`, 'goon');
      screen.el.style.opacity = 1;
      screen.el.querySelector('img').style.mixBlendMode = 'luminosity';
      ctx.every(12000, () => screen.fill());
      ctx.on('pictures', () => screen.fill());
      if (fresh(ctx)) {
        for (const side of ['left', 'right']) {
          const c = ctx.el('div', `position:absolute;top:0;bottom:0;${side}:0;width:51%;background:repeating-linear-gradient(90deg,#3f2714 0 22px,#2d1b0e 22px 44px);`, null, 'over');
          const off = side === 'left' ? '-101%' : '101%';
          ctx.anim(c, ctx.gentle ? [{ opacity: 0 }, { opacity: 1, offset: 0.3 }, { opacity: 1, offset: 0.7 }, { opacity: 0 }] : [{ transform: `translateX(${off})` }, { transform: 'none', offset: 0.45 }, { transform: 'none', offset: 0.75 }, { opacity: 1, offset: 0.8 }, { opacity: 0 }], { duration: 1500, easing: 'ease-in-out', fill: 'forwards' });
          ctx.after(1560, () => c.remove());
        }
      }
      return {};
    },
  },

  /* It says twenty minutes. The hands say otherwise, quickly. */
  go_clock: {
    mount(ctx) {
      const s = slot(ctx, 'display:flex;gap:10px;align-items:center;');
      let hand = null;
      if (s.roomy) {
        const face = ctx.el('div', 'position:relative;flex:none;width:48px;height:48px;border:2px solid var(--ink);border-radius:50%;', null, s.el);
        hand = ctx.el('div', `position:absolute;left:21px;top:3px;width:2px;height:20px;background:${HOT};transform-origin:50% 100%;`, null, face);
        motion(ctx, hand, [{ transform: 'rotate(0)' }, { transform: 'rotate(360deg)' }], { duration: 1200, iterations: Infinity, easing: 'linear' });
      }
      const line = ctx.el('div', 'min-width:0;', '', s.el);
      let i = 0;
      const draw = () => {
        const real = ctx.state.clock / 60000, shown = Math.max(1, Math.floor(real / 5) + 1);
        line.textContent = R.clock[i % R.clock.length].replace('{min}', shown);
      };
      draw();
      ctx.every(6000, () => { i += 1; draw(); });
      return {};
    },
  },

  go_pod: { mount(ctx) { return mountPodcast(ctx, { theme: 'goon', show: 'Still Going' }); } },

  /* Thousands online, nodding. All made up, and it says so if you read closely. */
  go_pals: {
    mount(ctx) {
      const s = slot(ctx);
      const line = ctx.el(s.roomy ? 'div' : 'span', '', '', s.el);
      let n = 4812, i = 0;
      const draw = () => { line.textContent = R.pals[i % R.pals.length].replace('{n}', ctx.fmt(n)); };
      draw();
      ctx.every(1600, () => { n += pick(ctx, [3, 7, -2, 11]); draw(); });
      ctx.every(9000, () => { i += 1; });
      if (s.roomy) {
        const grid = ctx.el('div', 'display:grid;grid-template-columns:repeat(5,1fr);gap:3px;margin-top:6px;', null, s.el);
        for (let k = 0; k < 10; k++) {
          const cell = ctx.el('div', 'position:relative;height:34px;background:#2a2019;border:1px solid var(--line);overflow:hidden;', null, grid);
          const head = ctx.el('div', `position:absolute;left:50%;top:6px;width:13px;height:13px;margin-left:-6px;border-radius:50%;background:${HOT};`, null, cell);
          ctx.el('div', 'position:absolute;left:10%;right:10%;top:22px;height:18px;border-radius:50%;background:#7a4a1c;', null, cell);
          motion(ctx, head, [{ transform: 'translateY(0)' }, { transform: 'translateY(5px)' }, { transform: 'translateY(0)' }], { duration: 1400, iterations: Infinity, delay: -k * 90 });
        }
      }
      return {};
    },
  },

  ...takeover,

  /* EMI Takeover (./emi-takeover.js): the take fills the screen with her and ends in the dark, then the engine goes
   * straight to the run recap and its poster (owner 2026-11-09: no "four hours later" card). */
  go_end: {
    mount() { return {}; },
    fadeColour: '#000',
    fadeMs: 200,
    take: emiTakeoverTake,
  },
};
