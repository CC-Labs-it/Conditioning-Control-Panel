/* ui/unlocks/basics/look.js - the look lane: what the player buys in the first ten minutes. Every entry lands visibly
 * within a second of the press. Button skins keep a rectangle to press, so any skin stays completable. */

import { TAB_TITLES } from '../../../data/content/index.js';
import { whole } from '../../../sim/format.js';
import { baseCss, burst, btnCenter, chip, docOf, fresh, play, pop, PALETTES, setPalette, sfx, spawn, squish, tools } from './fx.js';

/* ---- button wardrobe: latest skin wins, the chip goes back to any owned one ---- */
const SKIN_OF = Object.freeze({ rounded: 'round', bubble: 'bubble', watch: 'watch', heartbtn: 'heart', mirror: 'mirror', spirbtn: 'spiral', bigbtn: 'big', tinybtn: 'tiny' });
const SKIN_LABEL = Object.freeze({ round: 'Round', bubble: 'Bubble', watch: 'Watch', heart: 'Heart', mirror: 'Mirror', spiral: 'Spiral', big: 'Big', tiny: 'Tiny' });

function wardrobe(ctx) {
  let foreign = null;
  const el = ctx.button.el;
  const owned = () => Object.keys(SKIN_OF).filter((id) => ctx.has(id)).map((id) => SKIN_OF[id]);
  const label = () => { b.textContent = `Button: ${SKIN_LABEL[el.dataset.skin] || 'Theme'}`; };
  const b = chip(ctx, '', () => {
    const cur = el.dataset.skin;
    if (cur && !SKIN_LABEL[cur]) foreign = cur;
    const list = foreign ? [...owned(), foreign] : owned();
    const next = list[(list.indexOf(cur) + 1) % list.length];
    ctx.store.set('choice', next);
    ctx.button.skin(next);
    label();
    squish(ctx, el);
    bumpZone(ctx);
  });
  const chosen = ctx.store.get('choice');
  if (chosen) ctx.after(40, () => { ctx.button.skin(chosen); label(); bumpZone(ctx); });
  ctx.on('buy', ({ item }) => { if (SKIN_OF[item.id]) { ctx.store.set('choice', null); ctx.after(30, label); } });
  ctx.every(1000, label);
  label();
}

/** A skin that changes the button zone changes how many shop rows fit: let the shop measure again. */
function bumpZone(ctx) {
  ctx.after(450, () => docOf(ctx).defaultView.dispatchEvent(new Event('resize')));
}

/** The skin arrives with a squash and a little party, sized to the buy. */
function wear(ctx, name, css) {
  baseCss(ctx);
  if (css) ctx.css(css);
  ctx.button.skin(name);
  if (fresh(ctx)) {
    const c = btnCenter(ctx);
    squish(ctx, ctx.button.el);
    burst(ctx, c.x, c.y, { n: 12, reach: 70 });
    sfx(ctx, 'sfx/ui_equip.mp3', 0.5);
  }
}

/* ---- a page wipe from the button ---- */
function wipe(ctx, color, then) {
  if (ctx.gentle || !fresh(ctx)) { then(); return; }
  const c = btnCenter(ctx), { w, h } = ctx.bounds();
  const r = Math.hypot(Math.max(c.x, w - c.x), Math.max(c.y, h - c.y));
  const d = spawn(ctx, `inset:0;background:${color};`, null, 'back');
  const a = play(ctx, d, [{ clipPath: `circle(0px at ${c.x}px ${c.y}px)` }, { clipPath: `circle(${r}px at ${c.x}px ${c.y}px)` }], { duration: 800, easing: 'ease-out', fill: 'forwards' });
  a.finished?.then(then, then);
}

export default {
  /* Round Corners: the first change the button ever gets. */
  rounded: {
    mount(ctx) { wear(ctx, 'round', '#btn[data-skin=round] { border-radius: 16px; }'); return {}; },
  },

  /* Disguised Tab: the real tab title goes boring, and a little tab flies up so the buy is seen on screen too. */
  title: {
    mount(ctx) {
      const doc = docOf(ctx), was = doc.title;
      const link = doc.querySelector('link[rel="icon"]');
      const icon = ctx.el('link', '', null, doc.head);
      icon.rel = 'icon';
      icon.href = 'data:image/svg+xml,' + encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16"><rect width="16" height="16" rx="2" fill="#1d7a45"/><path d="M4 4l8 8M12 4l-8 8" stroke="#fff" stroke-width="2"/></svg>');
      let i = ctx.store.get('i') || 0;
      const set = () => { doc.title = TAB_TITLES[i % TAB_TITLES.length]; ctx.store.set('i', i); };
      set();
      ctx.every(60000, () => { i += 1; set(); });
      if (fresh(ctx)) {
        const tab = spawn(ctx, 'left:50%;top:8px;margin-left:-110px;width:220px;padding:6px 10px;background:#d4d0c8;color:#000;border:1px solid #6e6a62;border-radius:7px 7px 0 0;font:600 14px "Segoe UI",system-ui,sans-serif;white-space:nowrap;overflow:hidden;', '');
        const sq = docOf(ctx).createElement('i');
        sq.style.cssText = 'display:inline-block;width:12px;height:12px;background:#1d7a45;border-radius:2px;margin-right:6px;vertical-align:-1px;';
        tab.append(sq, TAB_TITLES[0]);
        play(ctx, tab, [{ transform: 'translateY(90px)', opacity: 0 }, { transform: 'translateY(60px)', opacity: 1, offset: 0.14 }, { transform: 'translateY(60px)', opacity: 1, offset: 0.7 }, { transform: 'translateY(-50px)', opacity: 0 }], { duration: 2200, easing: 'ease-in-out' });
      }
      return { off() { doc.title = was; if (link) link.remove(); } };
    },
  },

  /* Floating Numbers */
  float: {
    mount(ctx) {
      const shoot = (gain, x, y) => {
        const size = Math.min(30, 15 + Math.log10(Math.max(1, gain)) * 3);
        ctx.float(x + (ctx.rng() - 0.5) * 60, y - 18, `+${ctx.fmt(gain)}`, `color:var(--stamp);font-weight:700;font-size:${size}px;`);
      };
      ctx.on('tap', ({ gain, x, y }) => {
        if (x == null) { const c = btnCenter(ctx); shoot(gain, c.x, c.y); } else shoot(gain, x, y);
      });
      if (fresh(ctx)) { const c = btnCenter(ctx); for (let i = 0; i < 3; i++) ctx.after(i * 90, () => shoot(ctx.rates().click, c.x, c.y)); }
      return {};
    },
  },

  /* Colour: the grey wipes away from the button. */
  colour: {
    mount(ctx) {
      baseCss(ctx);
      ctx.css(`
body { transition: background-color .6s, color .6s; }
#btn { background: var(--btn); border: 2px solid var(--btn-edge); box-shadow: 0 3px 0 var(--btn-edge); font-weight: 600; }
#btn.down { border-style: solid; box-shadow: none; transform: translateY(3px) scale(.98); }
.row { border-radius: 6px; }
#goal .row { border-color: var(--btn-edge); }
`);
      const { '--page': page, ...rest } = PALETTES.pink;
      ctx.vars(rest);
      wipe(ctx, page, () => ctx.vars({ '--page': page }));
      if (fresh(ctx)) { sfx(ctx, 'sfx/reveal_chime.mp3', 0.6); squish(ctx, ctx.button.el); }
      return {};
    },
  },

  /* Wall Pop: every wall gets a small burst away from the wall, and a soft pop. */
  wallpop: {
    mount(ctx) {
      const pops = ['pop/Pop.mp3', 'pop/Pop2.mp3', 'pop/Pop3.mp3'];
      let lastPop = 0, clock = 0;
      const blast = (x, y, n = 8) => {
        const { w, h } = ctx.bounds();
        const hit = [x < 50 ? 0 : null, x > w - 50 ? Math.PI : null, y < 30 ? Math.PI / 2 : null, y > h - 30 ? -Math.PI / 2 : null].find((a) => a != null);
        const a = hit ?? 0;
        burst(ctx, Math.max(0, Math.min(w, x)), Math.max(0, Math.min(h, y)), { n, reach: 42, size: 6, from: a - 1.3, to: a + 1.3 });
      };
      ctx.on('wall', ({ x, y }) => {
        blast(x, y);
        if (clock - lastPop > 140) { lastPop = clock; sfx(ctx, pops[Math.floor(ctx.rng() * 3)], 0.35); }
      });
      if (fresh(ctx)) { const { w, h } = ctx.bounds(); blast(0, h * 0.4, 12); blast(w, h * 0.6, 12); blast(w * 0.5, 0, 12); }
      return { tick(dt) { clock += dt * 1000; } };
    },
  },

  /* Nicer Font: loads first, so the swap is one soft change and not a flash of fallback text. */
  font: {
    mount(ctx) {
      const doc = docOf(ctx);
      ctx.css(`@font-face { font-family: "Fredoka"; src: url("assets/fonts/fredoka-latin.woff2") format("woff2"); font-weight: 300 700; font-display: swap; }`);
      const swap = () => {
        ctx.vars({ '--font': '"Fredoka", "Segoe UI", system-ui, sans-serif', '--shop-font': '"Fredoka", "Segoe UI", system-ui, sans-serif' });
        if (fresh(ctx)) for (const id of ['stage', 'shop']) ctx.anim(doc.getElementById(id), [{ opacity: 0.2 }, { opacity: 1 }], { duration: 400 });
      };
      if (doc.fonts?.load) doc.fonts.load('16px "Fredoka"').then(swap, swap); else swap();
      return {};
    },
  },

  /* Confetti: every purchase, sized to how much of the bank it took. */
  confetti: {
    mount(ctx) {
      const COLS = ['#ff4fa8', '#7b5cff', '#f0c24b', '#29b6c9'];
      const shower = (n) => {
        const { w } = ctx.bounds(), c = btnCenter(ctx);
        if (ctx.gentle) { burst(ctx, c.x, c.y, { color: COLS[0], reach: 90 }); return; }
        for (let i = 0; i < n; i++) {
          const x = c.x + (ctx.rng() - 0.5) * Math.min(w, 340), up = c.y - 40 - ctx.rng() * 90, down = ctx.bounds().h + 20, spin = 200 + ctx.rng() * 500;
          const d = spawn(ctx, `width:7px;height:10px;background:${COLS[i % 4]};`);
          play(ctx, d, [
            { transform: `translate(${c.x}px,${c.y}px) rotate(0)`, opacity: 1 },
            { transform: `translate(${(x + c.x) / 2}px,${up}px) rotate(${spin * 0.3}deg)`, opacity: 1, offset: 0.28, easing: 'ease-in' },
            { transform: `translate(${x}px,${down}px) rotate(${spin}deg)`, opacity: 0.8 },
          ], { duration: 1300 + ctx.rng() * 500, easing: 'ease-out' });
        }
      };
      ctx.on('buy', ({ cost }) => shower(Math.min(28, 10 + Math.round(Math.log10(Math.max(1, cost)) * 3))));
      if (fresh(ctx)) shower(30);
      return {};
    },
  },

  /* Bubble Button: the button and its copies. A bubble pops when pressed. The wardrobe chip lives here: swap back any time. */
  bubble: {
    mount(ctx) {
      wear(ctx, 'bubble', `
#btn[data-skin=bubble] { width: 92px; height: 92px; border-radius: 50%; color: transparent; border: 1.5px solid rgba(120,170,220,.9); box-shadow: inset 0 -6px 14px rgba(120,190,255,.35); background: radial-gradient(circle at 32% 28%, #fff 0 9%, rgba(255,255,255,.4) 16%, rgba(190,225,255,.5) 55%, rgba(120,190,255,.8)); }
#btn[data-skin=bubble].down { border-style: solid; box-shadow: inset 0 -6px 14px rgba(120,190,255,.35); }
#world:has(#btn[data-skin=bubble]) [data-stray] { border-radius: 50%; width: 26px !important; height: 26px !important; border: 1.5px solid rgba(120,170,220,.9) !important; background: radial-gradient(circle at 32% 28%, #fff 0 12%, rgba(190,225,255,.5) 50%, rgba(120,190,255,.8)) !important; }
`);
      wardrobe(ctx);
      ctx.on('tap', ({ x, y }) => {
        if (ctx.button.el.dataset.skin !== 'bubble') return;
        sfx(ctx, `pop/Pop${['', '2', '3'][Math.floor(ctx.rng() * 3)]}.mp3`, 0.4);
        const c = x == null ? btnCenter(ctx) : { x, y };
        burst(ctx, c.x, c.y, { color: 'rgba(120,190,255,.9)', n: 6, reach: 40, size: 5 });
      });
      return {};
    },
  },

  /* Pink Cursor: a pink arrow, and a ring wherever anyone presses, finger or mouse. */
  cursor: {
    mount(ctx) {
      const arrow = encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="24" height="33" viewBox="0 0 12 17"><path d="M0 0v14l3.5-3 2.4 5.5 2.5-1-2.5-5.4h5z" fill="#ff4fa8" stroke="#fff" stroke-width=".8"/></svg>');
      ctx.css(`html, body, button, .row, #btn { cursor: url("data:image/svg+xml,${arrow}") 1 1, auto !important; }`);
      const win = docOf(ctx).defaultView;
      const ring = (x, y) => {
        const d = spawn(ctx, `width:44px;height:44px;margin:-22px 0 0 -22px;box-sizing:border-box;border:3px solid #ff4fa8;border-radius:50%;left:${x}px;top:${y}px;`);
        play(ctx, d, [{ transform: 'scale(.3)', opacity: 1 }, { transform: 'scale(1.1)', opacity: 0 }], { duration: ctx.gentle ? 400 : 480, easing: 'ease-out' });
      };
      const onDown = (e) => ring(e.clientX, e.clientY);
      win.addEventListener('pointerdown', onDown, true);
      if (fresh(ctx)) { const c = btnCenter(ctx); ring(c.x, c.y); ctx.after(180, () => ring(c.x, c.y)); }
      return { off() { win.removeEventListener('pointerdown', onDown, true); } };
    },
  },

  /* Rolling Counter: odometer digits over the real count, which stays in place for Gentle. */
  counter: {
    mount(ctx) {
      const doc = docOf(ctx);
      ctx.css(`
#count { display: none; }
body.gentle #count { display: inline; }
body.gentle #fz-roll { display: none; }
#fz-roll { display: inline-flex; vertical-align: top; height: 1.1em; overflow: hidden; white-space: nowrap; }
#fz-roll > span { flex: none; display: block; height: 1.1em; line-height: 1.1; }
#fz-roll .d { height: 11em; transition: transform .45s cubic-bezier(.2,.8,.3,1); }
#fz-roll .d > span { display: block; height: 1.1em; }
`);
      const roll = ctx.el('span', '', null, doc.getElementById('counter'));
      roll.id = 'fz-roll';
      doc.getElementById('counter').insertBefore(roll, doc.getElementById('count'));
      const digits = [];
      let shown = '';
      const STACK = Array.from({ length: 10 }, (_, i) => `<span>${i}</span>`).join('');
      const draw = () => {
        const text = whole(ctx.state.fuzz);
        if (text === shown) return;
        shown = text;
        while (digits.length > text.length) digits.shift().el.remove();
        while (digits.length < text.length) {
          const e = doc.createElement('span');
          roll.insertBefore(e, roll.firstChild);
          digits.unshift({ el: e, ch: null });
        }
        text.split('').forEach((ch, i) => {
          const d = digits[i];
          if (d.ch === ch) return;
          const was = d.ch;
          d.ch = ch;
          // A digit changing faster than it can roll just changes: only the slow digits get the odometer.
          const t = docOf(ctx).defaultView.performance.now();
          d.el.style.transition = t - (d.t || 0) < 300 ? 'none' : '';
          d.t = t;
          if (ch === ',') { d.el.className = 'c'; d.el.textContent = ','; d.el.style.transform = ''; return; }
          if (d.el.className !== 'd' || was === ',') { d.el.className = 'd'; d.el.innerHTML = STACK; }
          d.el.style.transform = `translateY(${-1.1 * Number(ch)}em)`;
        });
      };
      draw();
      ctx.every(70, draw);
      return {};
    },
  },

  /* Cursor Trail: a small pool of soft dots, so the trail costs the same however fast the hand moves. */
  trail: {
    mount(ctx) {
      const win = docOf(ctx).defaultView;
      const pool = Array.from({ length: 14 }, () => ctx.el('div', 'position:absolute;left:0;top:0;width:18px;height:18px;margin:-9px 0 0 -9px;border-radius:50%;background:rgba(240,60,150,.45);opacity:0;pointer-events:none;will-change:transform,opacity;', null, 'over'));
      let i = 0, last = 0;
      const drop = (x, y) => {
        if (ctx.gentle) return;
        const d = pool[i++ % pool.length];
        ctx.anim(d, [{ transform: `translate(${x}px,${y}px) scale(1)`, opacity: 0.9 }, { transform: `translate(${x}px,${y}px) scale(.3)`, opacity: 0 }], { duration: 700, easing: 'linear' });
      };
      const onMove = (e) => { const t = win.performance.now(); if (t - last < 35) return; last = t; drop(e.clientX, e.clientY); };
      win.addEventListener('pointermove', onMove);
      if (fresh(ctx)) {
        const c = btnCenter(ctx);
        for (let k = 0; k < 14; k++) ctx.after(k * 45, () => { const a = k / 14 * Math.PI * 2; drop(c.x + Math.cos(a) * 90, c.y + Math.sin(a) * 40); });
      }
      return { off() { win.removeEventListener('pointermove', onMove); } };
    },
  },

  /* Pocket Watch: swings on `rotate` from a pivot above it, so the pressed state and the swap still work. Slow enough to catch. */
  watch: {
    mount(ctx) {
      wear(ctx, 'watch', `
@keyframes fz-swing { from { rotate: -16deg; } to { rotate: 16deg; } }
#btn[data-skin=watch] { width: 84px; height: 84px; border-radius: 50%; border: 4px solid #b8902a; color: #5a4310; font-weight: 700; font-size: 16px; box-shadow: 0 3px 8px rgba(70,40,0,.4); background: radial-gradient(circle at 35% 30%, #fff7d8, #f0cf70); transform-origin: 50% -30px; animation: fz-swing 1.5s ease-in-out infinite alternate; }
#btn[data-skin=watch].down { border-style: solid; }
#btn[data-skin=watch]::before { content: ""; position: absolute; left: 50%; bottom: 100%; width: 3px; height: 34px; margin-left: -1.5px; pointer-events: none; background: repeating-linear-gradient(#8a6d1f 0 4px, #d9b94f 4px 8px); }
body.gentle #btn[data-skin=watch] { animation: none; }
`);
      ctx.every(1500, () => { if (ctx.button.el.dataset.skin === 'watch' && !ctx.gentle) sfx(ctx, 'sfx/tally_tick.mp3', 0.12); });
      return {};
    },
  },

  /* Page Colour: four pots. Buying it moves off pink at once, so the pots explain themselves. */
  paint: {
    mount(ctx) {
      baseCss(ctx);
      ctx.css(`.fz-pot { width: 34px; min-height: 34px; padding: 0; border: 0; background: transparent; touch-action: manipulation; pointer-events: auto; } .fz-pot i { display: block; width: 24px; height: 24px; margin: auto; border-radius: 50%; border: 2px solid #fff; box-shadow: 0 0 0 1px rgba(0,0,0,.5); transition: scale .15s; } .fz-pot[aria-pressed=true] i { scale: 1.25; }`);
      const row = ctx.el('span', 'display:inline-flex;gap:0;', null, tools(ctx));
      const dots = {};
      const paint = (name, by = true) => {
        setPalette(ctx, name);
        ctx.store.set('name', name);
        for (const [k, d] of Object.entries(dots)) d.setAttribute('aria-pressed', String(k === name));
        if (by) { ctx.emit('palette', { name }); }
      };
      for (const [name, col] of [['pink', '#ff69b4'], ['violet', '#7b5cff'], ['teal', '#16a596'], ['black', '#111']]) {
        const b = ctx.el('button', '', null, row);
        b.type = 'button';
        b.className = 'fz-pot';
        b.setAttribute('aria-label', `Page colour ${name}`);
        const i = docOf(ctx).createElement('i');
        i.style.background = col;
        b.appendChild(i);
        b.addEventListener('click', () => { paint(name); squish(ctx, b); sfx(ctx, 'sfx/ui_equip.mp3', 0.4); });
        dots[name] = b;
        if (fresh(ctx)) ctx.anim(b, [{ scale: 0 }, { scale: 1.3, offset: 0.7 }, { scale: 1 }], { duration: 320, delay: 80 * Object.keys(dots).length, fill: 'backwards' });
      }
      const pick = ctx.store.get('name');
      paint(pick || 'violet', false);
      return {};
    },
  },

  /* Dark Mode: goes on when bought, and a switch stays for changing your mind. A colour pot switches it back off. */
  dark: {
    mount(ctx) {
      baseCss(ctx);
      let on = ctx.store.get('on') ?? true, base = null;
      const set = (v) => {
        on = v;
        ctx.store.set('on', on);
        b.textContent = on ? 'Dark: on' : 'Dark: off';
      };
      const b = chip(ctx, '', () => {
        set(!on);
        if (on) setPalette(ctx, 'dark'); else setPalette(ctx, base || 'pink');
        sfx(ctx, 'sfx/ui_equip.mp3', 0.4);
      });
      ctx.on('palette', ({ name }) => { base = name; if (on) set(false); });
      set(on);
      ctx.after(0, () => { if (on) setPalette(ctx, 'dark'); });
      return {};
    },
  },

  /* Corner Bonus: glowing corners, a big payout. The bouncer snaps to a true corner when it comes close. */
  corner: {
    mount(ctx) {
      const glow = (css, at) => ctx.el('div', `position:absolute;${css};width:90px;height:90px;background:radial-gradient(circle at ${at},rgba(240,194,75,.75),transparent 70%);pointer-events:none;`, null, 'wash');
      const spots = [glow('left:0;top:0', '0 0'), glow('right:0;top:0', '100% 0'), glow('left:0;bottom:0', '0 100%'), glow('right:0;bottom:0', '100% 100%')];
      if (!ctx.gentle) for (const s of spots) ctx.anim(s, [{ opacity: 0.45 }, { opacity: 1 }], { duration: 1600, iterations: Infinity, direction: 'alternate', easing: 'ease-in-out' });
      const party = (x, y) => {
        const { w, h } = ctx.bounds();
        const cx = x < w / 2 ? 0 : w, cy = y < h / 2 ? 0 : h;
        const aim = Math.atan2(h / 2 - cy, w / 2 - cx);
        burst(ctx, cx, cy, { color: '#f0c24b', n: 24, reach: 150, size: 9, ms: 900, from: aim - 1, to: aim + 1 });
        pop(ctx, Math.max(70, Math.min(w - 70, cx)), Math.max(60, Math.min(h - 60, cy)), 'x10', { color: '#c79400', size: 44, ms: 1300 });
      };
      ctx.on('wall', ({ corner, x, y }) => {
        if (!corner) return;
        ctx.pay(ctx.rates().wall * 9, 'corner', { x, y });
        ctx.emit('cornered', { x, y });
        party(x, y);
        sfx(ctx, 'sfx/pb_fanfare.mp3', 0.5);
      });
      if (fresh(ctx)) { const { w } = ctx.bounds(); party(0, 0); ctx.after(220, () => party(w, 0)); }
      return {};
    },
  },
};
