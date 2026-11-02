/* Drone: black, green mono, chrome. The signature: you do what you are told. Taps pay nothing until a task asks for one,
 * then ten times over; the arm takes the taps away, the hive ranks you, and the update freezes the shop when it likes. */

import { THEMES, ENDINGS, FAKE_UPDATE } from '../../../data/content/index.js';
import { mountPodcast } from '../podcast-player.js';
import { bag, geo, q, sheet, done, frame, refillOnChange, sideFrames, startTheme, shopWords, modChip, pick, railPanel } from './kit.js';
import { endingShow } from './endings.js';

const R = THEMES.drone.rules;
const NICHES = ['shiny', 'chrome', 'visor', 'gloss', 'mirror', 'latex'];
const MONO = 'Consolas,"Courier New",monospace';

const VARS = {
  '--page': '#10161a', '--ink': '#dfe6ea', '--ink-soft': '#9aa8b0', '--ink-faint': '#6c7a82', '--btn': '#bfc9cf',
  '--panel': '#1a2226', '--panel-short': '#141a1e', '--line': '#46535a', '--fill': '#2a3a3a', '--stamp': '#3dff8e',
  '--font': MONO,
};
const CSS = `#btn{border:0;border-radius:3px;color:#111;font-weight:700;letter-spacing:.04em;text-transform:uppercase;font-size:15px;background:linear-gradient(135deg,#f6f8f9,#8d99a1 40%,#e6ebee 62%,#6f7a82);box-shadow:0 2px 0 #333;}
#btn.down{border-style:none;box-shadow:none;transform:translateY(2px);}
.row{border-radius:2px;}
#goal .row.ready{animation-name:dr-ready;}
@keyframes dr-ready{50%{background:#26413a;}}
.toast{background:#3dff8e;color:#070b09;border-radius:0;}`;

/** A hard scan from the top, a line of boot text. No easing: machines do not ease. */
function moment(ctx) {
  ctx.audio.play('assets/audio/sfx/rank_up.mp3', { bus: 'sfx', gain: 0.6 });
  ctx.voice('obey');
  const s = sheet(ctx, `background:#070b09;display:grid;place-items:center;color:#3dff8e;font:700 clamp(24px,6vw,56px) ${MONO};text-align:center;`, 'UNIT ONLINE');
  if (ctx.gentle) { done(ctx.anim(s, [{ opacity: 0 }, { opacity: 1, offset: 0.3 }, { opacity: 0 }], { duration: 1200 }), s); return; }
  done(ctx.anim(s, [
    { clipPath: 'inset(0 0 100% 0)', offset: 0 },
    { clipPath: 'inset(0 0 0 0)', offset: 0.3 },
    { clipPath: 'inset(0 0 0 0)', offset: 0.7 },
    { clipPath: 'inset(100% 0 0 0)', offset: 1 },
  ], { duration: 1100, easing: 'linear' }), s);
}

const rollSerial = (ctx) => {
  let n = ctx.store.get('serial');
  if (!n) { n = 1000 + Math.floor(ctx.rng() * 8999); if (n === 7) n = 4017; ctx.store.set('serial', n); }
  return n;
};

export default {
  dr_head: {
    mount(ctx) {
      const theme = startTheme(ctx, { theme: 'drone', vars: VARS, css: CSS, moment });
      sideFrames(ctx, ['shiny', 'chrome', 'visor', 'mirror'], 'border-radius:2px;outline:1px solid #46535a;');
      modChip(ctx, 'drone');
      return { off() { theme.off(); } };
    },
  },

  /* Unit Number: the plate, and the words you and your are gone from every row. */
  dr_serial: {
    mount(ctx) {
      const b = bag(ctx);
      const serial = rollSerial(ctx);
      const label = R.serial.replace('{serial}', serial);
      b.unit = label;
      const plate = ctx.el('div', `position:relative;display:flex;align-items:center;gap:8px;min-width:170px;height:26px;padding:0 10px;margin-top:2px;border-radius:3px;background:linear-gradient(135deg,#eef2f4,#8f9aa2 45%,#dfe5e8 70%,#7f8a92);color:#111;font:700 14px ${MONO};`, null, 'meters');
      ctx.el('span', 'flex:none;width:8px;height:8px;border-radius:50%;background:#10161a;', null, plate);
      const name = ctx.el('span', 'flex:1;text-align:center;letter-spacing:.06em;', null, plate);
      const first = !ctx.store.get('typed');
      ctx.store.set('typed', 1);
      if (first && !ctx.gentle) {
        name.textContent = 'YOUR NAME';
        const strike = ctx.el('span', 'position:absolute;left:30px;right:12px;top:50%;height:2px;background:#a3232b;transform-origin:0 50%;', null, plate);
        done(ctx.anim(strike, [{ transform: 'scaleX(0)' }, { transform: 'scaleX(1)' }], { duration: 400, fill: 'forwards', delay: 350 }), strike);
        const u = label.toUpperCase();
        ctx.after(800, () => { name.textContent = ''; for (let i = 0; i < u.length; i++) ctx.after(i * 60, () => { name.textContent += u[i]; ctx.audio.play('assets/audio/sfx/tally_tick.mp3', { bus: 'sfx', gain: 0.25 }); }); });
      } else name.textContent = label.toUpperCase();
      const handle = shopWords(ctx, (text) => text
        .replace(/\bYour\b/g, 'This unit\'s').replace(/\byour\b/g, 'this unit\'s')
        .replace(/\bYou\b/g, 'This unit').replace(/\byou\b/g, 'this unit'));
      return { off() { handle.off(); b.unit = null; } };
    },
  },

  /* Terminal Look: chrome goes, green takes the page from the top down. */
  dr_term: {
    mount(ctx) {
      const first = !ctx.store.get('seen');
      ctx.store.set('seen', 1);
      ctx.vars({
        '--page': '#070b09', '--ink': '#3dff8e', '--ink-soft': '#2fd175', '--ink-faint': '#1c8a4f', '--panel': '#0b130f', '--panel-short': '#070d0a',
        '--line': '#1c6b42', '--fill': '#123a26', '--btn': '#0f2a1d',
      });
      ctx.css(`#counter{text-shadow:0 0 8px rgba(61,255,142,.45);}
.row{border-style:solid;}
.row b::before{content:'> ';}
#goal .row{border-color:#3dff8e;}`);
      ctx.el('div', 'position:absolute;inset:0;background:repeating-linear-gradient(0deg,rgba(0,0,0,.22) 0 2px,transparent 2px 4px);', null, 'over');
      if (first) {
        const s = sheet(ctx, `background:#070b09;color:#3dff8e;font:600 18px/1.5 ${MONO};padding:18% 12%;white-space:pre-line;`, R.boot.join('\n'));
        if (ctx.gentle) done(ctx.anim(s, [{ opacity: 1 }, { opacity: 0 }], { duration: 900 }), s);
        else done(ctx.anim(s, [{ clipPath: 'inset(0 0 0 0)', offset: 0 }, { clipPath: 'inset(0 0 0 0)', offset: 0.55 }, { clipPath: 'inset(100% 0 0 0)', offset: 1 }], { duration: 1400, easing: 'linear' }), s);
      }
      return {};
    },
  },

  /* Drone Pictures: a wall of tiles behind the page, a scan line, one tile changing at a time. */
  dr_feed: {
    mount(ctx) {
      const { w, h } = ctx.bounds();
      const size = w >= 760 ? 150 : 96;
      const cols = Math.ceil(w / size), rows = Math.min(Math.ceil(h / size), 8);
      const shown = Math.min(cols * rows, 14);
      /* A sparse wall: the gutters and the top get tiles, what the shop would cover anyway stays empty. */
      const spots = [];
      for (let r = 0; r < rows; r++) for (let c = 0; c < cols; c++) spots.push([c, r]);
      const score = ([c, r]) => (w >= 760 ? Math.abs((c + 0.5) * size - w / 2) : -r * 100);
      spots.sort((a, b) => score(b) - score(a));
      const tiles = spots.slice(0, shown).map(([c, r], i) => frame(ctx, `position:absolute;left:${c * size}px;top:${r * size}px;width:${size - 2}px;height:${size - 2}px;opacity:.42;`, NICHES[i % NICHES.length]));
      refillOnChange(ctx, tiles);
      let i = 0;
      ctx.every(1400, () => { tiles[i++ % tiles.length].next(pick(ctx, NICHES)); });
      if (!ctx.gentle) {
        const scan = ctx.el('div', 'position:absolute;left:0;right:0;top:0;height:3px;background:#3dff8e;box-shadow:0 0 10px #3dff8e;opacity:.7;will-change:transform;', null, 'back');
        ctx.anim(scan, [{ transform: 'translateY(0)' }, { transform: `translateY(${h}px)` }], { duration: 5200, iterations: Infinity, easing: 'linear' });
      }
      return {};
    },
  },

  /* Hexagon Button */
  dr_hex: {
    mount(ctx) {
      ctx.button.skin('dr-hex');
      ctx.css(`#btn[data-skin=dr-hex]{width:112px;height:96px;font-size:14px;border:0;border-radius:0;box-shadow:none;clip-path:polygon(25% 0,75% 0,100% 50%,75% 100%,25% 100%,0 50%);background:linear-gradient(135deg,#f6f8f9,#8d99a1 40%,#e6ebee 62%,#6f7a82);}
#btn[data-skin=dr-hex].down{transform:scale(.93);filter:brightness(1.25);}
#btn-zone{min-height:104px;}`);
      return {};
    },
  },

  /* Task Queue: a tap with no task pays nothing. A tap on a task pays ten times. */
  dr_task: {
    mount(ctx) {
      const b = bag(ctx);
      let open = false, n = ctx.store.get('n') || 1, until = 0, next = ctx.state.clock + 900;
      ctx.mod({ click: (v) => (open ? v * 10 : 0) });
      const box = ctx.el('div', `position:relative;width:min(280px,84vw);height:26px;margin-top:2px;overflow:hidden;text-align:center;font:700 14px/26px ${MONO};color:var(--ink-soft);background:var(--panel);border:1px solid var(--line);`, null, 'meters');
      const bar = ctx.el('div', 'position:absolute;inset:0;background:#3dff8e;transform-origin:0 50%;transform:scaleX(0);', null, box);
      const text = ctx.el('div', 'position:relative;white-space:nowrap;', pick(ctx, R.taskIdle), box);
      const idle = () => { text.style.color = ''; text.textContent = pick(ctx, R.taskIdle); bar.style.transform = 'scaleX(0)'; };
      const show = () => {
        open = true;
        const left = 1800 + ctx.rng() * 1600;
        until = ctx.state.clock + left;
        b.taskMs = left;
        text.textContent = pick(ctx, R.tasks).replace(/\{n\}/g, n);
        text.style.color = '#070b09';
        ctx.audio.play('assets/audio/sfx/ui_equip.mp3', { bus: 'sfx', gain: 0.55 });
        if (!ctx.gentle) ctx.anim(box, [{ transform: 'scale(1.08)' }, { transform: 'scale(1)' }], 200);
      };
      ctx.on('tap', (e) => {
        if (!open) {
          if (!e.auto) { const g = geo(ctx); ctx.float(e.x ?? g.cx, (e.y ?? g.cy) - 18, '0', 'color:#6c7a82;font-size:16px;'); }
          return;
        }
        open = false;
        n += 1;
        ctx.store.set('n', n);
        next = ctx.state.clock + 1500 + ctx.rng() * 3000;
        const g = geo(ctx);
        ctx.float(e.x ?? g.cx, (e.y ?? g.cy) - 34, `x10 +${ctx.fmt(e.gain)}`, 'color:#3dff8e;font-size:22px;font-weight:700;');
        ctx.audio.play('assets/audio/sfx/rank_settle.mp3', { bus: 'sfx', gain: 0.5 });
        idle();
      });
      ctx.frame(() => {
        const t = ctx.state.clock;
        if (open) {
          const left = Math.max(0, until - t);
          bar.style.transform = `scaleX(${(left / (b.taskMs || 1)).toFixed(3)})`;
          if (left <= 0) { open = false; next = t + 1200 + ctx.rng() * 2000; idle(); }
        } else if (t >= next) show();
      });
      return {};
    },
  },

  /* Auto Tap: an arm taps once a second. It takes the tasks too. Manual input no longer required. */
  dr_auto: {
    mount(ctx) {
      let g = geo(ctx);
      /* A piston from the right: the row above the button is full of readings, the side of it is free. */
      const arm = ctx.el('div', 'position:absolute;left:0;top:0;width:70px;height:44px;will-change:transform;', null, 'floaters');
      ctx.el('div', 'position:absolute;left:10px;right:0;top:16px;height:12px;background:linear-gradient(#7f8a92,#eef2f4,#7f8a92);', null, arm);
      ctx.el('div', 'position:absolute;left:0;top:0;bottom:0;width:12px;border-radius:2px;background:linear-gradient(90deg,#eef2f4,#7f8a92);', null, arm);
      const gears = [-1, 1].map((d) => {
        const e = ctx.el('div', 'position:absolute;left:0;top:0;width:34px;height:34px;text-align:center;font:400 34px/34px "Segoe UI Symbol",serif;color:#9aa8b0;', '⚙', 'floaters');
        e.dataset.d = d;
        if (!ctx.gentle) ctx.anim(e, [{ rotate: '0deg' }, { rotate: `${d * 360}deg` }], { duration: 5000, iterations: Infinity, easing: 'linear' });
        return e;
      });
      const place = () => {
        g = geo(ctx);
        arm.style.transform = `translate(${g.cx + g.bw / 2 + 22}px,${g.cy - 22}px)`;
        gears.forEach((e) => { e.style.left = `${g.cx - g.bw / 2 - 40 - (+e.dataset.d + 1) * 19}px`; e.style.top = `${g.cy - 17 + +e.dataset.d * 12}px`; });
      };
      place();
      ctx.every(1000, place);
      ctx.every(1000, () => {
        if (!ctx.gentle) ctx.anim(arm, [{ translate: '0 0' }, { translate: '-22px 0', offset: 0.4 }, { translate: '-22px 0', offset: 0.55 }, { translate: '0 0' }], { duration: 700 });
        ctx.after(280, () => ctx.button.press());
      });
      return {};
    },
  },

  dr_pod: { mount: (ctx) => mountPodcast(ctx, { theme: 'drone', show: 'True Drone', accent: '#3dff8e' }) },

  /* Hive Feed: fake units report in, yours gets a rank, and rank pays once. Rail on a wide page, one line on a phone. */
  dr_hive: {
    mount(ctx) {
      const STEPS = [0, 200, 600, 1500, 3000];
      const g = geo(ctx);
      const wide = g.wide;
      const panel = wide
        ? railPanel(ctx, 'left', `font:13px/1.3 ${MONO};display:flex;flex-direction:column;overflow:hidden;`)
        : ctx.el('div', `display:flex;gap:8px;align-items:center;max-width:94vw;height:20px;font:12px/20px ${MONO};color:var(--ink-soft);white-space:nowrap;overflow:hidden;`, null, 'meters');
      const rank = ctx.el('div', wide ? 'font-weight:700;margin-bottom:6px;color:var(--stamp);' : 'flex:none;font-weight:700;color:var(--stamp);', null, panel);
      const log = ctx.el('div', wide ? 'display:flex;flex-direction:column;overflow:hidden;' : 'min-width:0;overflow:hidden;text-overflow:ellipsis;', null, panel);
      let tier = ctx.store.get('tier') || 0, seat = 9;
      const paintRank = () => { rank.textContent = `#${seat} ${R.rank[tier]}`; };
      paintRank();
      let i = Math.floor(ctx.rng() * R.hive.length);
      const push = () => {
        const line = R.hive[i++ % R.hive.length];
        if (!wide) { log.textContent = line; return; }
        if (log.children.length > 9) log.firstChild.remove();
        const e = ctx.el('div', 'padding:1px 0;', line, log);
        if (!ctx.gentle) ctx.anim(e, [{ opacity: 0, transform: 'translateY(6px)' }, { opacity: 1, transform: 'none' }], 200);
      };
      for (let k = 0; k < (wide ? 5 : 1); k++) push();
      ctx.every(wide ? 2200 : 4000, push);
      ctx.every(3000, () => { seat = Math.max(2, Math.min(14, seat + (ctx.rng() < 0.5 ? -1 : 1))); paintRank(); });
      ctx.on('tap', () => {
        const want = STEPS.findLastIndex((s) => ctx.state.taps >= s + (ctx.store.get('base') ?? 0));
        if (want > tier) {
          tier = want;
          ctx.store.set('tier', tier);
          paintRank();
          const r = rank.getBoundingClientRect();
          ctx.pay(ctx.rates().fps * 15, 'dr_hive', { x: r.left + 40, y: r.top + 8 });
          ctx.toast(`Rank: ${R.rank[tier]}. Unit 7 is still ahead.`);
          ctx.audio.play('assets/audio/sfx/rank_up.mp3', { bus: 'sfx', gain: 0.5 });
        }
      });
      if (ctx.store.get('base') === undefined) ctx.store.set('base', ctx.state.taps);
      return {};
    },
  },

  /* Fake Update: a bar over the shop for ten seconds, then it pays. Wake Up and the button still work. */
  dr_update: {
    mount(ctx) {
      const MS = 10000;
      const run = () => {
        const r = q(ctx, '#shop')?.getBoundingClientRect?.();
        if (!r || !r.width) return;
        const cover = ctx.el('div', `position:absolute;left:${r.left}px;top:${r.top}px;width:${r.width}px;height:${r.height}px;pointer-events:auto;display:flex;flex-direction:column;justify-content:center;gap:8px;padding:14px;box-sizing:border-box;background:rgba(7,11,9,.86);color:var(--stamp);font:600 15px ${MONO};border:1px solid var(--line);text-align:center;`, null, 'over');
        ctx.el('div', 'font-size:20px;', FAKE_UPDATE.title, cover);
        const step = ctx.el('div', 'min-height:20px;', FAKE_UPDATE.steps[0], cover);
        const track = ctx.el('div', 'height:12px;border:1px solid currentColor;overflow:hidden;', null, cover);
        const fill = ctx.el('div', 'height:100%;background:currentColor;transform-origin:0 50%;transform:scaleX(0);', null, track);
        ctx.el('div', 'font-size:12px;opacity:.8;', FAKE_UPDATE.warn, cover);
        let t = 0, last = 0;
        const stop = ctx.frame((dt) => {
          t += dt * 1000;
          const v = Math.min(1, t / MS);
          fill.style.transform = `scaleX(${(v < 0.9 ? v : 0.9 + (v - 0.9) * 0.4 + (v >= 1 ? 0.06 : 0)).toFixed(3)})`;
          const k = Math.min(FAKE_UPDATE.steps.length - 1, Math.floor(v * FAKE_UPDATE.steps.length));
          if (k !== last) { last = k; step.textContent = FAKE_UPDATE.steps[k]; ctx.audio.play('assets/audio/sfx/tally_tick.mp3', { bus: 'sfx', gain: 0.3 }); }
          if (v < 1) return;
          stop();
          cover.remove();
          ctx.pay(ctx.rates().fps * 20, 'dr_update', { x: r.left + r.width / 2, y: r.top + 40 });
          ctx.toast(FAKE_UPDATE.done);
          ctx.audio.play('assets/audio/sfx/unlock_card.mp3', { bus: 'sfx', gain: 0.6 });
        });
      };
      /* The first one lands on the buy; a reload just waits for the next. */
      if (!ctx.store.get('ran')) { ctx.store.set('ran', 1); run(); }
      ctx.every(180000, run);
      return {};
    },
  },

  dr_end: {
    mount() { return {}; },
    ending(ctx) { return endingShow(ctx, ENDINGS.dr_end, 'drone'); },
  },
};
