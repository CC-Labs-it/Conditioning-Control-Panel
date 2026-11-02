/* Sissy: rose paper, serif, good manners. The signature: you look after the button, and it is judged. Dress it, match it,
 * hold it down and let go at the right moment, tick the rules, answer the mirror, pass the test. */

import { THEMES, ENDINGS } from '../../../data/content/index.js';
import { mountPodcast } from '../podcast-player.js';
import { bag, geo, sheet, done, frame, refillOnChange, sideFrames, pick, pop, railPanel } from './kit.js';
import { endingShow } from './endings.js';
import { COPY } from './copy.js';

const R = THEMES.sissy.rules;
const NICHES = ['lace', 'bows', 'pearls', 'frills', 'gloves', 'satin'];
const SETS = Object.keys(R.sets);
const GOLD = '#d9a521';
/* One look per fabric, used for every piece of that set. */
const LOOK = {
  satin: 'linear-gradient(135deg,#f0a8c2,#fff2f6 45%,#e18aab)',
  lace: 'radial-gradient(#c96a8c 2.5px,transparent 3px) 0 0/10px 10px,#fff',
  gingham: 'repeating-conic-gradient(#e7b6c8 0 25%,#fff 0 50%) 0 0/12px 12px',
  velvet: 'linear-gradient(135deg,#8d1830,#c24562 50%,#6d0f24)',
};
const LABEL_INK = { satin: '#7a2f4b', lace: '#7a2f4b', gingham: '#7a2f4b', velvet: '#fff' };

const VARS = {
  '--page': '#fbeef0', '--ink': '#7a2f4b', '--ink-soft': '#9a5470', '--ink-faint': '#b5738c', '--btn': '#f2c5d2',
  '--panel': '#fffafb', '--panel-short': '#f6e0e6', '--line': '#d9a0b4', '--fill': '#f2c5d2', '--ready': '#fff', '--stamp': '#c96a8c',
  '--font': 'Georgia,"Times New Roman",serif',
};
const CSS = `#btn{border:1px solid #c96a8c;border-radius:14px;color:#7a2f4b;background:linear-gradient(#fff6f8,#f2c5d2);font-style:italic;}
#btn.down{border-style:solid;}
.row{border-radius:6px;border-style:dashed;}
.toast{background:#7a2f4b;color:#fbeef0;border-radius:4px;font-style:italic;}
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

/** What the button is wearing. Each piece is plain css hung off the wrap; the wrap keeps the taps. */
function dressUp(ctx) {
  const wrap = ctx.parts.buttonWrap;
  const piece = (css) => ctx.el('div', `position:absolute;pointer-events:none;box-sizing:border-box;${css}`, null, wrap);
  return [
    /* ribbon */ piece('left:50%;top:-20px;width:60px;height:26px;margin-left:-30px;z-index:2;clip-path:polygon(0 0,50% 40%,100% 0,100% 100%,50% 60%,0 100%);border:0;'),
    /* collar */ piece('left:12%;right:12%;top:0;height:12px;border-radius:0 0 10px 10px;z-index:2;'),
    /* gloves */ piece('left:-18px;right:-18px;top:30%;height:16px;border-radius:8px;z-index:-1;'),
    /* skirt  */ piece('left:-6px;right:-6px;bottom:-14px;height:18px;z-index:-1;clip-path:polygon(10% 0,90% 0,100% 100%,0 100%);'),
    /* shoes  */ piece('left:50%;bottom:-22px;width:60px;height:9px;margin-left:-30px;border-radius:5px;z-index:-1;'),
  ];
}

export const START = { theme: 'sissy', vars: VARS, css: CSS, moment };

export default {
  si_head: {
    mount(ctx) {
      /* A lace edge round the whole page. */
      ctx.el('div', 'position:absolute;inset:4px;border:2px dotted #e7b6c8;border-radius:10px;', null, 'wash');
      sideFrames(ctx, ['lace', 'bows', 'pearls', 'satin'], 'border-radius:50%;outline:2px dotted #c96a8c;outline-offset:3px;');
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

  /* Bow Button */
  si_bow: {
    mount(ctx) {
      ctx.button.skin('si-bow');
      ctx.css(`#btn[data-skin=si-bow]{width:128px;height:90px;font-size:0;background:transparent;border:0;box-shadow:none;}
#btn[data-skin=si-bow]::after{content:'🎀';font-size:76px;line-height:90px;font-family:"Segoe UI Emoji","Apple Color Emoji","Noto Color Emoji",sans-serif;font-style:normal;}`);
      ctx.on('tap', () => {
        if (ctx.gentle || ctx.has('si_ward')) return;
        ctx.anim(ctx.button.el, [{ rotate: '0deg' }, { rotate: '-7deg' }, { rotate: '6deg' }, { rotate: '0deg' }], 340);
      });
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

  /* Button Wardrobe: five slots, four fabrics. Tap a slot to change it; the button wears it at once. */
  si_ward: {
    mount(ctx) {
      const b = bag(ctx);
      ctx.button.skin('si-dressed');
      ctx.css(`#btn[data-skin=si-dressed]{width:128px;height:56px;font-size:15px;color:var(--si-ink,#7a2f4b);border:2px solid #c96a8c;border-radius:12px;background:var(--si-body,#fff);text-shadow:0 0 3px rgba(255,255,255,.7);}
#btn-zone{min-height:112px;}`);
      const worn = ctx.store.get('worn') || (() => {
        /* Start mixed: a matching set has to be earned. */
        const w = R.slots.map((_, i) => SETS[(i + Math.floor(ctx.rng() * 4)) % 4]);
        if (new Set(w).size === 1) w[0] = SETS[(SETS.indexOf(w[0]) + 1) % 4];
        return w;
      })();
      ctx.store.set('worn', worn);
      const pieces = dressUp(ctx);
      const tiles = [];
      const root = ctx.world.ownerDocument.documentElement.style;
      const paint = () => {
        pieces.forEach((p, i) => { p.style.background = LOOK[worn[i]]; });
        root.setProperty('--si-body', LOOK[worn[3]]);
        root.setProperty('--si-ink', LABEL_INK[worn[3]]);
        tiles.forEach((t, i) => {
          t.swatch.style.background = LOOK[worn[i]];
          t.name.textContent = R.sets[worn[i]][i];
        });
        b.onWorn?.forEach((f) => f(worn));
      };
      const change = (i) => {
        worn[i] = SETS[(SETS.indexOf(worn[i]) + 1) % 4];
        ctx.store.set('worn', worn);
        paint();
        ctx.audio.play('assets/audio/sfx/ui_equip.mp3', { bus: 'sfx', gain: 0.6 });
        if (!ctx.gentle) ctx.anim(pieces[i], [{ transform: 'scale(1.3)' }, { transform: 'scale(1)' }], 200);
      };
      const tile = (i, parent, css) => {
        const t = ctx.el('button', `display:flex;align-items:center;gap:8px;width:100%;min-height:44px;padding:4px;background:transparent;border:1px solid var(--line);border-radius:6px;color:var(--ink);text-align:left;cursor:pointer;pointer-events:auto;font-size:13px;line-height:1.1;${css}`, null, parent);
        t.type = 'button';
        const swatch = ctx.el('span', 'flex:none;width:30px;height:30px;border-radius:5px;border:1px solid var(--line);', null, t);
        const col = ctx.el('span', 'min-width:0;', null, t);
        ctx.el('span', 'display:block;font-size:11px;color:var(--ink-soft);', R.slots[i], col);
        const name = ctx.el('span', 'display:block;', null, col);
        t.addEventListener('click', () => change(i));
        tiles[i] = { swatch, name };
      };
      const g = geo(ctx);
      let chip = null;
      if (g.wide) {
        const panel = railPanel(ctx, 'left', 'display:flex;flex-direction:column;gap:4px;border-radius:8px;');
        ctx.el('div', 'font-style:italic;', COPY.sissy.wardrobe, panel);
        b.wardPanel = panel;
        R.slots.forEach((_, i) => tile(i, panel));
      } else {
        /* A phone has no rail: one chip beside the button opens the same five slots. */
        chip = ctx.el('button', `position:absolute;left:8px;top:${g.cy - 22}px;width:56px;height:44px;padding:0;background:var(--panel);border:1px solid var(--line);border-radius:8px;color:var(--ink);font-size:12px;pointer-events:auto;cursor:pointer;`, COPY.sissy.wardrobeShort, 'floaters');
        chip.type = 'button';
        ctx.every(1500, () => { chip.style.top = `${geo(ctx).cy - 22}px`; });
        chip.addEventListener('click', () => {
          tiles.length = 0;
          const node = ctx.world.ownerDocument.createElement('div');
          node.style.cssText = 'display:flex;flex-direction:column;gap:6px;';
          R.slots.forEach((_, i) => tile(i, node));
          paint();
          ctx.modal({ title: COPY.sissy.wardrobe, node, buttons: [{ label: COPY.sissy.wardrobeDone }] });
        });
      }
      paint();
      return { off() { root.removeProperty('--si-body'); root.removeProperty('--si-ink'); b.wardPanel = null; } };
    },
  },

  /* Matching Set: all five the same doubles its pay. The first of each fabric also pays a lump. */
  si_set: {
    mount(ctx) {
      const b = bag(ctx);
      let on = false;
      ctx.mod({ click: (v) => (on ? v * 2 : v), fps: (v) => (on ? v + ctx.item.fps : v) });
      const line = ctx.el('div', `position:relative;display:flex;gap:6px;align-items:center;justify-content:center;height:20px;font-size:13px;color:var(--ink-soft);font-style:italic;`, null, 'meters');
      const paint = (worn) => {
        const counts = {};
        for (const s of worn) counts[s] = (counts[s] || 0) + 1;
        const [top, n] = Object.entries(counts).sort((a, c) => c[1] - a[1])[0];
        const was = on;
        on = n === 5;
        line.textContent = on ? COPY.sissy.matchDone(`${top[0].toUpperCase()}${top.slice(1)}`) : COPY.sissy.matchCount(n);
        line.style.color = on ? GOLD : '';
        line.style.fontWeight = on ? 700 : '';
        if (on && !was) {
          ctx.toast(pick(ctx, R.setDone));
          ctx.audio.play('assets/audio/sfx/unlock_card.mp3', { bus: 'sfx', gain: 0.7 });
          const g = geo(ctx);
          const done_ = ctx.store.get('sets') || [];
          if (!done_.includes(top)) {
            ctx.store.set('sets', [...done_, top]);
            ctx.pay(ctx.rates().fps * 20, 'si_set', { x: g.cx, y: g.cy });
          }
          if (!ctx.gentle) {
            for (let i = 0; i < 10; i++) {
              const s = ctx.el('div', `position:absolute;left:${g.cx}px;top:${g.cy}px;color:${GOLD};font-size:${14 + ctx.rng() * 12}px;`, '✦', 'over');
              const a = ctx.rng() * Math.PI * 2, d = 50 + ctx.rng() * 90;
              done(ctx.anim(s, [{ transform: 'translate(0,0)', opacity: 1 }, { transform: `translate(${Math.cos(a) * d}px,${Math.sin(a) * d}px)`, opacity: 0 }], { duration: 650, easing: 'ease-out' }), s);
            }
          }
        }
      };
      b.onWorn = b.onWorn || new Set();
      b.onWorn.add(paint);
      const w = ctx.state.store?.si_ward?.worn;
      if (w) paint([...w]);
      return { off() { b.onWorn?.delete(paint); } };
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
      const g = geo(ctx);
      let i = Math.floor(ctx.rng() * R.rules.length);
      const nextRule = () => R.rules[i++ % R.rules.length];
      const pay = (el) => {
        const r = el.getBoundingClientRect();
        ctx.pay(ctx.rates().fps * 2, 'si_rules', { x: r.left + r.width / 2, y: r.top });
        ctx.audio.play('assets/audio/sfx/tally_tick.mp3', { bus: 'sfx', gain: 0.6 });
      };
      if (g.wide) {
        const panel = railPanel(ctx, 'right', 'background:#fff;border-radius:2px;box-shadow:2px 2px 0 #f2c5d2;display:flex;flex-direction:column;gap:2px;');
        ctx.el('div', 'font-style:italic;margin-bottom:2px;', COPY.sissy.houseRules, panel);
        const fill = () => {
          for (let n = 0; n < 4; n++) {
            const rule = ctx.el('button', 'display:flex;align-items:center;gap:8px;width:100%;min-height:36px;padding:2px;background:transparent;border:0;color:var(--ink);text-align:left;font-size:14px;cursor:pointer;', null, panel);
            rule.type = 'button';
            const box = ctx.el('span', 'flex:none;width:18px;height:18px;border:1.5px solid #c96a8c;text-align:center;line-height:15px;font-weight:700;color:#c96a8c;', null, rule);
            ctx.el('span', '', nextRule(), rule);
            rule.addEventListener('click', () => {
              if (box.textContent) return;
              box.textContent = '✓';
              rule.style.opacity = 0.6;
              pay(rule);
              if (panel.querySelectorAll('button').length && [...panel.querySelectorAll('button span:first-child')].every((s) => s.textContent)) {
                ctx.say(pick(ctx, R.ruleDone));
                ctx.after(1000, () => { for (const b of [...panel.querySelectorAll('button')]) b.remove(); ctx.after(40000, fill); });
              }
            });
          }
        };
        fill();
      } else {
        /* A phone gets one rule at a time, right of the button. */
        const chip = ctx.el('button', `position:absolute;right:8px;top:${g.cy - 26}px;width:76px;min-height:52px;padding:2px 4px;background:#fff;border:1px solid var(--line);border-radius:4px;color:var(--ink);font-size:12px;line-height:1.1;pointer-events:auto;cursor:pointer;`, null, 'floaters');
        chip.type = 'button';
        const show = () => { chip.dataset.on = 1; chip.hidden = false; chip.textContent = `☐ ${nextRule()}`; };
        show();
        ctx.every(1500, () => { chip.style.top = `${geo(ctx).cy - 26}px`; });
        let n = 0;
        chip.addEventListener('click', () => {
          if (!chip.dataset.on) return;
          chip.dataset.on = '';
          chip.textContent = `☑ ${pick(ctx, R.ruleDone)}`;
          pay(chip);
          n += 1;
          ctx.after(1100, () => { chip.hidden = true; ctx.after(n % 4 === 0 ? 40000 : 7000, show); });
        });
      }
      return {};
    },
  },

  si_pod: { mount: (ctx) => mountPodcast(ctx, { theme: 'sissy', show: 'True Sissy', accent: '#c96a8c' }) },

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
