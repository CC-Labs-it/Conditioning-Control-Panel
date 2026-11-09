/* Sissy: rose paper, serif, good manners. The signature: you look after the button, and it is judged. Hold it down and let go at the right moment, tick the rules, answer the mirror, pass the test. */

import { THEMES, ENDINGS } from '../../../data/content/index.js';
import { mountPodcast } from '../podcast-player.js';
import { bag, geo, sheet, done, frame, refillOnChange, pick, pop, railPanel } from './kit.js';
import { setRich } from '../../rich-text.js';
import { themeScreens } from './theme-screens.js';
import { endingShow } from './endings.js';
import { COPY } from './copy.js';
import { THEME_PAGES } from '../../palette.js';

const R = THEMES.sissy.rules;
const NICHES = ['lace', 'bows', 'pearls', 'frills', 'gloves', 'satin'];
const GOLD = '#d9a521';

const VARS = {
  ...THEME_PAGES.sissy,
  '--font': 'Georgia,"Times New Roman",serif',
};
const CSS = `#btn{border:1px solid var(--btn-edge);border-radius:14px;font-style:italic;}
#btn.down{border-style:solid;}
.row{border-radius:6px;border-style:dashed;}
.toast{border-radius:4px;font-style:italic;}
#counter{font-style:italic;}`;

/** Two curtains part on the new page. */
function moment(ctx) {
  ctx.audio.play('assets/audio/sfx/reveal_chime.mp3', { bus: 'sfx', gain: 0.7 });
  if (ctx.gentle) {
    const s = sheet(ctx, 'background:#c96a8c;display:grid;place-items:center;color:#fff;font:italic clamp(30px,7vw,60px) Georgia,serif;', COPY.sissy.moment);
    done(ctx.anim(s, [{ opacity: 0 }, { opacity: 1, offset: 0.3 }, { opacity: 0 }], { duration: 1300 }), s);
    return;
  }
  const title = sheet(ctx, 'display:grid;place-items:center;color:#fff;font:italic clamp(30px,7vw,60px) Georgia,serif;z-index:2;text-shadow:0 2px 0 #8d1830;', COPY.sissy.moment);
  const cloth = 'position:absolute;top:0;bottom:0;width:50.5%;background:repeating-linear-gradient(90deg,#c96a8c 0 16px,#b5587b 16px 32px);pointer-events:none;';
  const l = ctx.el('div', `${cloth}left:0;`, null, 'over'), r = ctx.el('div', `${cloth}right:0;`, null, 'over');
  const o = { duration: 1250, easing: 'cubic-bezier(.7,0,.3,1)', fill: 'forwards' };
  done(ctx.anim(l, [{ transform: 'translateX(0)', offset: 0 }, { transform: 'translateX(0)', offset: 0.42 }, { transform: 'translateX(-100%)', offset: 1 }], o), l);
  done(ctx.anim(r, [{ transform: 'translateX(0)', offset: 0 }, { transform: 'translateX(0)', offset: 0.42 }, { transform: 'translateX(100%)', offset: 1 }], o), r);
  done(ctx.anim(title, [{ opacity: 1, offset: 0 }, { opacity: 1, offset: 0.4 }, { opacity: 0, offset: 0.65 }, { opacity: 0, offset: 1 }], o), title);
  l.parentElement.appendChild(title);
}

export const START = { theme: 'sissy', vars: VARS, css: CSS, moment };

export default {
  si_head: {
    mount(ctx) {
      /* A lace edge round the whole page. */
      ctx.el('div', 'position:absolute;inset:4px;border:2px dotted #e7b6c8;border-radius:10px;', null, 'wash');
      /* The first pictures: three screens, not a wall (owner 2026-11-08). The cameo row waits for Sissy Pictures. */
      themeScreens(ctx, { niches: ['lace', 'bows', 'pearls', 'satin'], frame: 'border:2px dotted #c96a8c;border-radius:16px;' });
      /* A taste of the signature before House Rules is bought: one small manner beside the button, and it pays. */
      const manner = ctx.el('button', 'position:absolute;left:0;top:0;width:84px;min-height:48px;padding:2px 4px;background:#fff;border:1px solid var(--line);border-radius:4px;color:var(--ink);font:12px/1.1 Georgia,serif;pointer-events:auto;cursor:pointer;opacity:0;visibility:hidden;', null, 'floaters');
      manner.type = 'button';
      let i = Math.floor(ctx.rng() * R.rules.length), shown = false;
      const say = () => {
        if (shown || ctx.has('si_rules')) return;
        shown = true;
        const g = geo(ctx);
        manner.textContent = `☐ ${R.rules[i++ % R.rules.length]}`;
        manner.style.transform = `translate(${Math.min(g.w - 92, g.cx + g.bw / 2 + 12)}px,${g.cy - 24}px)`;
        manner.style.visibility = 'visible';
        ctx.anim(manner, [{ opacity: 0 }, { opacity: 1 }], { duration: 300, fill: 'forwards' });
      };
      const gone = () => { shown = false; manner.style.visibility = 'hidden'; };
      manner.addEventListener('click', () => {
        if (!shown) return;
        const r = manner.getBoundingClientRect();
        ctx.pay(ctx.rates().fps * 2, 'si_head', { x: r.left + r.width / 2, y: r.top });
        ctx.audio.play('assets/audio/sfx/tally_tick.mp3', { bus: 'sfx', gain: 0.6 });
        manner.textContent = `☑ ${pick(ctx, R.ruleDone)}`;
        ctx.after(800, gone);
      });
      ctx.after(2500, say);
      ctx.every(9000, say);
      return {};
    },
  },

  /* Sissy Pictures: oval cameos with a dotted edge, changing one at a time. */
  si_feed: {
    mount(ctx) {
      const { w } = ctx.bounds();
      const narrow = w < 760;
      const list = [];
      const oval = (css, n) => {
        const f = frame(ctx, `position:absolute;border-radius:50%;outline:2px dotted #c96a8c;outline-offset:3px;${css}`, n);
        list.push(f);
      };
      if (narrow) {
        /* Under the shop's top edge there is room for a row of small ones behind the counter. */
        [0, 1, 2].forEach((i) => oval(`left:${10 + i * 31}%;top:8px;width:26%;aspect-ratio:3/4;opacity:.22;`, NICHES[i]));
      } else {
        /* A row of cameos along the bottom, where the shop leaves the page showing. */
        const n = Math.min(5, Math.floor((w - 460) / 150));
        for (let i = 0; i < n; i++) oval(`left:calc(50% + ${(i - (n - 1) / 2) * 150 - 58}px);bottom:16px;width:116px;aspect-ratio:3/4;opacity:.85;`, NICHES[i]);
      }
      refillOnChange(ctx, list);
      let i = 0;
      ctx.every(2400, () => { list[i++ % list.length].next(pick(ctx, NICHES)); });
      return {};
    },
  },

  /* Curtsy: hold the button and let go in the gold zone. Neater pays more. A quick tap is still just a tap. */
  si_curtsy: {
    mount(ctx) {
      const doc = ctx.world.ownerDocument;
      const el = ctx.button.el;
      const FULL = 1750, ZONE = [0.62, 0.8], MID = (ZONE[0] + ZONE[1]) / 2;
      let downAt = 0, held = false, v = 0;
      const wrap = ctx.el('div', 'position:absolute;left:0;top:0;width:190px;height:14px;opacity:0;transition:opacity .12s;border-radius:7px;background:#fff;border:1px solid #c96a8c;overflow:hidden;', null, 'floaters');
      ctx.el('div', `position:absolute;left:${ZONE[0] * 100}%;width:${(ZONE[1] - ZONE[0]) * 100}%;top:0;bottom:0;background:${GOLD};opacity:.6;`, null, wrap);
      const fill = ctx.el('div', 'position:absolute;inset:0;background:#c96a8c;opacity:.75;transform-origin:0 50%;transform:scaleX(0);', null, wrap);
      const start = (ev) => {
        if (ev.button > 0) return;
        downAt = performance.now();
        const g = geo(ctx);
        wrap.style.transform = `translate(${g.cx - 95}px,${g.cy + g.bh / 2 + 8}px)`;
      };
      const end = (ev) => {
        if (!downAt) return;
        const wasHeld = held;
        downAt = 0; held = false;
        wrap.style.opacity = 0;
        el.style.translate = '';
        fill.style.transform = 'scaleX(0)';
        if (!wasHeld || ev.type === 'pointercancel') return;
        const miss = Math.abs(v - MID) / ((ZONE[1] - ZONE[0]) / 2);
        const grade = v < ZONE[0] - 0.2 || v > ZONE[1] + 0.15 ? 0 : miss > 1.6 ? 1 : miss > 1 ? 1 : miss > 0.6 ? 2 : miss > 0.25 ? 3 : 4;
        const g = geo(ctx);
        ctx.float(g.cx, g.cy - g.bh, R.curtsy[grade], `color:${grade >= 3 ? GOLD : '#7a2f4b'};font-size:20px;font-style:italic;`);
        if (grade < 2) { ctx.audio.play('assets/audio/sfx/ui_denied.mp3', { bus: 'sfx', gain: 0.35 }); return; }
        const mult = [0, 0, 3, 6, 12][grade];
        ctx.pay(ctx.rates().click * mult, 'si_curtsy', { x: g.cx, y: g.cy });
        ctx.audio.play(`assets/audio/sfx/chime${grade - 1}.mp3`, { bus: 'sfx', gain: 0.6 });
      };
      el.addEventListener('pointerdown', start);
      doc.addEventListener('pointerup', end);
      doc.addEventListener('pointercancel', end);
      ctx.on('pause', () => end({ type: 'pointercancel' }));
      ctx.frame(() => {
        if (!downAt) return;
        const t = performance.now() - downAt;
        if (!held && t > 170) { held = true; wrap.style.opacity = 1; }
        if (!held) return;
        v = Math.min(1, (t - 170) / FULL);
        fill.style.transform = `scaleX(${v.toFixed(3)})`;
        if (!ctx.gentle) el.style.translate = `0 ${(v * 22).toFixed(1)}px`;
      });
      return { off() { el.removeEventListener('pointerdown', start); doc.removeEventListener('pointerup', end); doc.removeEventListener('pointercancel', end); el.style.translate = ''; } };
    },
  },

  /* House Rules: four small manners, each tick pays. Then a rest, then four more. */
  si_rules: {
    mount(ctx) {
      let i = Math.floor(ctx.rng() * R.rules.length);
      const panel = railPanel(ctx, 'right', 'background:#fff;color:#562a40;border-radius:6px;padding:7px;display:flex;flex-direction:column;gap:2px;');
      const dock = panel.parentElement.dock;
      ctx.el('div', 'font-style:italic;margin-bottom:2px;', COPY.sissy.houseRules, panel);
      const sequential = ctx.bounds().w < 1100;
      let remaining = 0;
      const fill = () => {
        remaining = 4; dock?.setReady(true, 'Rules ready');
        for (let n = 0; n < 4; n++) {
          const rule = ctx.el('button', 'display:flex;align-items:center;gap:7px;width:100%;min-height:36px;padding:2px;background:transparent;border:0;color:inherit;text-align:left;font-size:13px;cursor:pointer;', null, panel);
          rule.type = 'button'; rule.disabled = sequential && n > 0;
          if (rule.disabled) rule.style.opacity = '.4';
          const box = ctx.el('span', 'flex:none;width:18px;height:18px;border:1.5px solid #c96a8c;text-align:center;line-height:15px;font-weight:700;color:#c96a8c;', '', rule);
          ctx.el('span', '', R.rules[i++ % R.rules.length], rule);
          rule.addEventListener('click', () => {
            if (ctx.paused || rule.disabled) return;
            rule.disabled = true; box.textContent = '✓'; rule.style.opacity = '.6';
            remaining -= 1; dock?.setReady(!sequential && remaining > 0, 'Rules ready');
            if (sequential && remaining) ctx.after(8100, () => {
              const next = panel.querySelectorAll('button')[n + 1];
              if (next) { next.disabled = false; next.style.opacity = '1'; dock?.setReady(true, 'Rule ready'); }
            });
            const r = rule.getBoundingClientRect();
            ctx.pay(ctx.rates().fps * 2, 'si_rules', { x: r.left + r.width / 2, y: r.top });
            ctx.audio.play('assets/audio/sfx/tally_tick.mp3', { bus: 'sfx', gain: 0.6 });
            if (!remaining) {
              ctx.say(pick(ctx, R.ruleDone));
              ctx.after(sequential ? 41100 : 41000, () => { for (const b of [...panel.querySelectorAll('button')]) b.remove(); fill(); });
            }
          });
        }
      };
      fill();
      return {};
    },
  },

  si_pod: { mount: (ctx) => mountPodcast(ctx, { theme: 'sissy', show: 'Manners Hour', accent: '#c96a8c' }) },

  /* Order Tracking: the button becomes a courier card (ui/button-face-art.js 'si-parcel'); each tap moves the parcel a step. */
  si_track: {
    mount(ctx) {
      ctx.button.skin('si-parcel');
      return {};
    },
  },

  /* Posture Check: now and then a card asks you to sit up straight. Three still seconds (no pointer, no tap) pay. It waits
   * its turn behind the other sissy cards, sits below the button, never covers Wake Up, and stops while paused. */
  si_posture: {
    mount(ctx) {
      const doc = ctx.world.ownerDocument;
      const b_ = bag(ctx);
      const HOLD = 3, OPEN_MS = 14000;
      let card = null, still = 0, lastX = null, lastY = null;
      const fidget = () => { if (card) still = 0; };
      const onMove = (e) => {
        if (lastX != null && Math.hypot(e.clientX - lastX, e.clientY - lastY) < 4) return;
        lastX = e.clientX; lastY = e.clientY; fidget();
      };
      doc.addEventListener('pointermove', onMove, true);
      doc.addEventListener('pointerdown', fidget, true);
      doc.addEventListener('keydown', fidget, true);
      const schedule = () => ctx.after(60000 + ctx.rng() * 60000, show);
      function show() {
        if (b_.popBusy || ctx.paused) { ctx.after(9000, show); return; }
        b_.popBusy = true;
        still = 0;
        const p = pop(ctx, `width:min(250px,84vw);padding:11px 13px;background:#fff;border:1px solid #c96a8c;border-radius:8px;text-align:center;font-family:Georgia,serif;color:#7a2f4b;top:${geo(ctx).stageBottom + 8}px;box-shadow:3px 3px 0 #f2c5d2;`, OPEN_MS, () => {
          b_.popBusy = false; card = null; schedule();
        });
        ctx.el('div', 'font:700 11px/1 Consolas,monospace;letter-spacing:1px;color:#b5587b;margin-bottom:6px;', COPY.sissy.postureHead.toUpperCase(), p.el);
        ctx.el('div', 'font:italic 17px/1.2 Georgia,serif;margin-bottom:9px;', pick(ctx, R.posture.ask), p.el);
        const track = ctx.el('div', 'height:8px;border-radius:4px;background:#f2c5d2;overflow:hidden;', null, p.el);
        const bar = ctx.el('div', 'height:100%;width:100%;background:#c96a8c;transform-origin:0 50%;transform:scaleX(0);', null, track);
        const hint = ctx.el('div', 'font:600 12px/1 Georgia,serif;margin-top:6px;color:#9a6a80;', COPY.sissy.postureHold, p.el);
        card = { p, bar, hint, done: false };
        const at = () => { const r = p.el.getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top }; };
        const stopFrame = ctx.frame((dt) => {
          if (!card || card.done || ctx.paused) return;
          still = Math.min(HOLD, still + dt);
          bar.style.transform = `scaleX(${(still / HOLD).toFixed(3)})`;
          if (still < HOLD) return;
          card.done = true;
          ctx.pay(ctx.rates().fps * 10, 'si_posture', at());
          setRich(hint, pick(ctx, R.posture.done));
          hint.style.color = GOLD;
          ctx.audio.play('assets/audio/sfx/chime3.mp3', { bus: 'sfx', gain: 0.45 });
          ctx.after(1400, () => p.close());
        });
        ctx.after(OPEN_MS - 1600, () => { if (card && !card.done && card.p === p) hint.textContent = pick(ctx, R.posture.missed); });
        ctx.after(OPEN_MS + 50, stopFrame);
      }
      ctx.on('pause', fidget);
      ctx.after(20000, show);
      return { off() { doc.removeEventListener('pointermove', onMove, true); doc.removeEventListener('pointerdown', fidget, true); doc.removeEventListener('keydown', fidget, true); } };
    },
  },

  /* Thank-You Notes: a purchase that took a real bite of the bank (5% or more) gets a small handwritten note for four seconds.
   * It floats on the right below Wake Up, takes no clicks, and comes at most once every 20 seconds. */
  si_notes: {
    mount(ctx) {
      let last = -Infinity, i = Math.floor(ctx.rng() * R.notes.length);
      ctx.on('buy', ({ item, cost }) => {
        if (!cost || item?.id === 'si_notes') return;
        const bank = ctx.state.fuzz + cost;
        if (cost < bank * 0.05 || ctx.state.clock - last < 20000) return;
        last = ctx.state.clock;
        const tilt = (ctx.rng() * 6 - 3).toFixed(1);
        /* A phone has no free right side: the note sits low, over the empty page under the shop. */
        const spot = ctx.bounds().w < 600 ? 'left:50%;bottom:76px;translate:-50% 0;' : 'right:16px;top:clamp(64px,18%,160px);';
        const note = ctx.el('div', `position:absolute;${spot}width:min(230px,70vw);padding:12px 14px 10px;box-sizing:border-box;pointer-events:none;background:#fffdf7 linear-gradient(#fffdf7 0 26px,#f6d9e4 26px 27px) 0 6px/100% 27px;border:1px solid #e3c2cf;border-radius:3px;box-shadow:2px 3px 0 #f2c5d2,0 6px 14px #5a2a4022;color:#5a2440;font:italic 16px/27px "Segoe Print","Bradley Hand","Comic Sans MS",cursive;rotate:${tilt}deg;`, null, 'over');
        ctx.el('div', '', R.notes[i++ % R.notes.length], note);
        ctx.el('div', 'text-align:right;font-size:13px;color:#b5587b;', pick(ctx, R.noteSign), note);
        const show = ctx.gentle
          ? [{ opacity: 0 }, { opacity: 1, offset: 0.12 }, { opacity: 1, offset: 0.85 }, { opacity: 0 }]
          : [{ opacity: 0, transform: 'translate(40px,-12px) scale(.9)' }, { opacity: 1, transform: 'none', offset: 0.1 }, { opacity: 1, transform: 'translateY(-4px)', offset: 0.85 }, { opacity: 0, transform: 'translateY(-14px)' }];
        done(ctx.anim(note, show, { duration: 4000, easing: 'ease-out', fill: 'forwards' }), note);
        ctx.audio.play('assets/audio/sfx/chip_pop.mp3', { bus: 'sfx', gain: 0.3 });
      });
      return {};
    },
  },

  /* Mirror Check: it asks how it looks. Every answer pays, and it leaves on its own if you ignore it. */
  si_mirror: {
    mount(ctx) {
      const b_ = bag(ctx);
      const show = () => {
        if (b_.popBusy) { ctx.after(9000, show); return; }
        b_.popBusy = true;
        const a = pick(ctx, R.mirror.answers);
        let b = pick(ctx, R.mirror.answers);
        if (b === a) b = R.mirror.answers[(R.mirror.answers.indexOf(a) + 1) % R.mirror.answers.length];
        const p = pop(ctx, `width:210px;text-align:center;top:${geo(ctx).stageBottom + 8}px;`, 8000, () => { b_.popBusy = false; });
        const m = ctx.el('div', `width:150px;height:126px;margin:0 auto;border-radius:50%;box-sizing:border-box;border:6px solid ${GOLD};display:grid;place-items:center;padding:14px;font:600 16px Georgia,serif;color:#7a2f4b;background:linear-gradient(135deg,#e8f4fa,#fff 40%,#c5dfec 60%,#f4fbff);box-shadow:0 3px 12px rgba(122,47,75,.35);`, pick(ctx, R.mirror.ask), p.el);
        const row = ctx.el('div', 'display:flex;gap:8px;justify-content:center;margin-top:8px;', null, p.el);
        for (const label of [a, b]) {
          const btn = ctx.el('button', 'min-width:84px;min-height:44px;background:#f2c5d2;border:1px solid #c96a8c;border-radius:22px;color:#7a2f4b;font:600 15px Georgia,serif;cursor:pointer;', label, row);
          btn.type = 'button';
          btn.addEventListener('click', () => {
            const r = btn.getBoundingClientRect();
            ctx.pay(ctx.rates().fps * 6, 'si_mirror', { x: r.left + r.width / 2, y: r.top });
            m.textContent = pick(ctx, R.mirror.replies);
            ctx.audio.play('assets/audio/sfx/chime3.mp3', { bus: 'sfx', gain: 0.5 });
            ctx.after(650, () => p.close());
            row.style.pointerEvents = 'none';
          });
        }
      };
      show();
      ctx.every(70000, show);
      return {};
    },
  },

  /* Manners Test: a timed question. Pass and taps pay triple for half a minute. Fail and nothing happens, politely. */
  si_class: {
    mount(ctx) {
      let until = 0, i = Math.floor(ctx.rng() * R.manners.length);
      const BOOST_MS = 30000, TIME_MS = 5500;
      ctx.mod({ click: (v, s) => (s.clock < until ? v * 3 : v) });
      const tag = ctx.el('div', `height:0;overflow:hidden;font-size:14px;font-weight:700;color:${GOLD};font-style:italic;`, COPY.sissy.testBoost, 'meters');
      const b_ = bag(ctx);
      const show = () => {
        if (b_.popBusy) { ctx.after(9000, show); return; }
        b_.popBusy = true;
        const q_ = R.manners[i++ % R.manners.length];
        const p = pop(ctx, `width:min(300px,84vw);padding:12px;background:#fff;border:1px solid #c96a8c;border-radius:8px;text-align:center;font-family:Georgia,serif;color:#7a2f4b;top:${geo(ctx).stageBottom + 8}px;box-shadow:3px 3px 0 #f2c5d2;`, TIME_MS + 300, () => { b_.popBusy = false; });
        ctx.el('div', 'font-size:16px;margin-bottom:6px;', `🫖 ${q_.ask}`, p.el);
        const track = ctx.el('div', 'height:6px;border-radius:3px;background:#f2c5d2;overflow:hidden;margin-bottom:10px;', null, p.el);
        const bar = ctx.el('div', 'height:100%;background:#c96a8c;transform-origin:0 50%;', null, track);
        const clock = ctx.anim(bar, [{ transform: 'scaleX(1)' }, { transform: 'scaleX(0)' }], { duration: TIME_MS, easing: 'linear', fill: 'forwards' });
        const row = ctx.el('div', 'display:flex;gap:8px;justify-content:center;', null, p.el);
        const opts = ctx.rng() < 0.5 ? [q_.right, q_.wrong] : [q_.wrong, q_.right];
        let answered = false;
        for (const label of opts) {
          const b = ctx.el('button', 'flex:1;min-height:44px;background:#fbeef0;border:1px solid #c96a8c;border-radius:6px;color:#7a2f4b;font:600 15px Georgia,serif;cursor:pointer;', label, row);
          b.type = 'button';
          b.addEventListener('click', () => {
            if (answered) return;
            answered = true;
            clock.pause();
            if (label === q_.right) {
              until = ctx.state.clock + BOOST_MS;
              tag.style.height = '20px';
              ctx.after(BOOST_MS, () => { tag.style.height = '0'; });
              b.style.background = GOLD;
              ctx.toast(COPY.sissy.testPass);
              ctx.audio.play('assets/audio/sfx/pb_fanfare.mp3', { bus: 'sfx', gain: 0.5 });
            } else {
              ctx.toast(COPY.sissy.testFail);
            }
            ctx.after(700, () => p.close());
          });
        }
      };
      show();
      ctx.every(165000, show);
      return {};
    },
  },

  si_end: {
    mount() { return {}; },
    ending(ctx) { return endingShow(ctx, ENDINGS.si_end, 'sissy'); },
  },
};
