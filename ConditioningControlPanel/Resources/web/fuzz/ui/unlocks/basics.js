/* Lanes: trunk look side. Bulky entries live in ./basics/. See ENGINE.md. */

import look from './basics/look.js';
import { mountScoreHeat } from './basics/score-heat.js';
import look2 from './basics/look2.js';
import trunk from './basics/trunk.js';
import side from './basics/side.js';
import side2 from './basics/side2.js';
import { btnCenter, fresh, rings } from './basics/fx.js';
import { createTapPhrase } from './basics/tap-feel.js';

import { bindBounce, FACE_CSS } from '../bouncing-art.js';
import { squashScale } from './arcade/breakout-core/contact.js';
import { createOrbits, snapDiag } from './basics/pair-orbit.js';
import { STRAY_WORDS, THEME_STRAY_WORDS } from '../../data/content/side.js';

/* Owner 2026-11-08: the little copies read plain triggers; once a theme is picked they glitch over to that theme's words
 * (and a Second Theme's). A word changes every second wall hit. Gentle and reduced motion swap with a plain fade. */
const STRAY_CSS = `
[data-stray][data-bounce-art=dvd] strong { display:block; }
[data-stray].stray-glitch strong { text-shadow: 2px 0 #ff2bd6, -2px 0 #2bf0ff; animation: stray-glitch .48s steps(6) 1; }
[data-stray].stray-blip strong { text-shadow: 1px 0 #ff2bd6, -1px 0 #2bf0ff; animation: stray-glitch .22s steps(3) 1; }
@keyframes stray-glitch { 0% { clip-path: inset(0 0 58% 0); translate: -3px 0; } 20% { clip-path: inset(42% 0 18% 0); translate: 3px 0; }
  40% { clip-path: inset(8% 0 52% 0); translate: -2px 0; } 60% { clip-path: inset(62% 0 0 0); translate: 2px 0; } 80% { clip-path: inset(0 0 30% 0); translate: -1px 0; } 100% { clip-path: inset(0); translate: 0 0; } }
[data-stray].stray-fade strong { animation: stray-fade .3s ease-out 1; }
@keyframes stray-fade { from { opacity: 0; } to { opacity: 1; } }
`;
const GLYPHS = '#%&@$*?!/<>=+01';
/** The run's themes (picked, plus a Second Theme), as keys of THEME_STRAY_WORDS. */
function strayThemes(ctx) {
  const first = ctx.pick('f5'), second = ctx.has('taste2') ? ctx.state.store?.taste2?.theme : null;
  return [first, second].filter((t, i, all) => t && Object.hasOwn(THEME_STRAY_WORDS, t) && all.indexOf(t) === i);
}

const CORNER_NEAR = 6;

/** One bouncing button. Speed is set from the page size so every screen gets the same wall hits per second. */
function bouncer(ctx,index) {
  /* Owner 2026-11-08: every copy presses like the main button. Only its own small box takes the pointer; a trusted
   * pointerdown goes through ctx.button.pressFrom (ui/app.js), the same engine.tap #btn uses, at the copy's position. */
  const el = ctx.el('div','position:absolute;left:0;top:0;will-change:transform;pointer-events:auto;touch-action:manipulation;cursor:pointer;user-select:none;-webkit-user-select:none;');
  el.dataset.stray = '1';
  let pressAt = -1;
  el.addEventListener('pointerdown', (ev) => {
    if (ev.button !== 0 && ev.button !== 2) return;
    ev.preventDefault?.();
    if (ctx.button.pressFrom?.(ev) > 0) pressAt = 0;
  });
  el.addEventListener('contextmenu', (ev) => ev.preventDefault());
  /* `shown` is the theme set the words follow: a fresh pick waits for its turn below, so the glitch is never under the reveal. */
  let shown = strayThemes(ctx), key = shown.join(), hits = 0, wordAt = index * 5, scrambleTimer = 0, clearTimer = 0;
  const pickWord = () => { const list = shown.length ? shown.flatMap((t) => THEME_STRAY_WORDS[t]) : STRAY_WORDS; wordAt = (wordAt + 1 + Math.floor(Math.random() * 3)) % list.length; return list[wordAt]; };
  const art = bindBounce(ctx,el,{index,caption:pickWord()});
  /** Swap the word. Crossing into a theme (or adding a second) scrambles and glitches; later theme words blip; Gentle fades. */
  const swap = () => {
    const live = strayThemes(ctx);
    if (shown.length && live.join() !== shown.join()) shown = live;   // a Second Theme picked later
    const now = shown.join(), crossed = now !== key;
    key = now;
    const next = pickWord();
    clearTimeout(scrambleTimer); clearTimeout(clearTimer);
    el.classList.remove('stray-glitch', 'stray-blip', 'stray-fade');
    const cls = el.dataset.bounceArt !== 'dvd' ? '' : ctx.gentle || ctx.reduced ? 'stray-fade' : crossed ? 'stray-glitch' : now ? 'stray-blip' : '';
    if (!cls) { art.setCaption(next); return; }
    void el.offsetWidth;
    el.classList.add(cls);
    clearTimer = setTimeout(() => el.classList.remove(cls), 520);
    if (cls !== 'stray-glitch') { art.setCaption(next); return; }
    const label = el.querySelector('strong');
    let n = 0;
    const scramble = () => {
      if (++n > 5 || !label) { art.setCaption(next); return; }
      label.textContent = Array.from(next, (ch) => (ch === ' ' ? ' ' : GLYPHS[Math.floor(Math.random() * GLYPHS.length)])).join('');
      scrambleTimer = setTimeout(scramble, 70);
    };
    scramble();
  };
  /* The theme pick turns every copy over once the theme reveal has cleared (about 4.5 s), staggered a little. A Second Theme shows on the next swap. */
  ctx.on('fork', ({ fork }) => { if (fork === 'f5') ctx.after(4500 + index * 90, () => { shown = strayThemes(ctx); swap(); }); });
  /* A Second Theme joins on its next word, with the same glitch. */
  ctx.on('buy', ({ item }) => { if (item?.id === 'taste2') ctx.after(4500 + index * 90, () => { shown = strayThemes(ctx); swap(); }); });
  const initial = art.sync();
  const b = ctx.bounds();
  const main = ctx.button.el.getBoundingClientRect?.();
  let x = Math.min(b.w - initial.w, Math.max(0, (main?.left ?? b.w / 2) - 10));
  let y = Math.min(b.h - initial.h, Math.max(0, (main?.top ?? b.h / 2) - 30));
  const sign = () => (ctx.rng() < 0.5 ? -1 : 1);
  const a = 0.8 + ctx.rng() * 0.4;
  let dx = sign() * a, dy = sign() * (2 - a);
  const inks = ['var(--accent)','var(--accent-2)','var(--ink)'];
  let ink = index % inks.length;
  el.style.transform = `translate(${x}px,${y}px)`;
  /* Wall feel (owner 2026-11-08), spaced out by rows the copies already have: a plain bounce first, Wall Pop adds the
   * Breakout brick squash along the hit axis (overshoot and settle), Corner Bonus adds a wobble. Little EMIs (and a copy
   * of an EMI face) get both and flinch on each hit. Gentle and reduced motion: a plain bounce. */
  let hitAt = -1, hitAxis = 'x', hitOrigin = '50% 50%', origin = '', flinch = 0;
  const SQUASH_S = .32, WOBBLE_S = .6, PRESS_S = .14;
  /* The squash pivots on the wall it hit, so the flattened side stays on the wall instead of lifting off it. */
  const pivot = (o) => { if (o !== origin) { origin = o; el.style.transformOrigin = o; } };
  const emiCopy = () => el.dataset.bounceArt === 'emi' || Boolean(el.querySelector('.xe-f'));
  function feel(dt) {
    if (ctx.gentle || ctx.reduced) { hitAt = pressAt = -1; return ''; }
    const emi = emiCopy();
    let sx = 1, sy = 1, rot = 0;
    if (hitAt >= 0) {
      hitAt += dt;
      if ((emi || ctx.has('wallpop')) && hitAt < SQUASH_S) {
        const { along, across } = squashScale(1 - hitAt / SQUASH_S);
        if (hitAxis === 'x') { sx = along; sy = across; } else if (hitAxis === 'y') { sx = across; sy = along; } else sx = sy = (along + across) / 2;
      }
      if ((emi || ctx.has('corner')) && hitAt < WOBBLE_S) rot = 7 * Math.exp(-hitAt * 7) * Math.sin(hitAt * 34) * (dx * dy < 0 ? -1 : 1);
      if (hitAt >= Math.max(SQUASH_S, WOBBLE_S)) hitAt = -1;
    }
    pivot(hitAt >= 0 ? hitOrigin : '50% 50%');
    if (pressAt >= 0) {
      pressAt += dt;
      const k = pressAt < PRESS_S ? Math.sin(pressAt / PRESS_S * Math.PI) * .12 : 0;
      sx *= 1 - k; sy *= 1 - k;
      if (pressAt >= PRESS_S) pressAt = -1;
    }
    return sx === 1 && sy === 1 && !rot ? '' : ` rotate(${rot.toFixed(2)}deg) scale(${sx.toFixed(3)},${sy.toFixed(3)})`;
  }
  /** An EMI copy flinches on a wall hit (shocked pose and face), then goes back. Never under Gentle or reduced motion. */
  function react() {
    if (ctx.gentle || ctx.reduced || !emiCopy()) return;
    const img = el.dataset.bounceArt === 'emi' ? el.querySelector('img') : null;
    const faces = [...el.querySelectorAll('.xe-f')];
    if (!flinch) {
      const src = img?.getAttribute('src');
      if (img) img.src = 'assets/img/emi/body-shock.png';
      for (const f of faces) f.paint?.('>_<');
      /* data-flinch holds a face copy's live mirroring (ui/bouncing-art.js) until the flinch is over. */
      el.dataset.flinch = '1';
      flinch = { restore() { delete el.dataset.flinch; if (img && src && img.isConnected) img.src = src; for (const f of faces) f.paint?.(f.dataset.m === 'annoyed' ? '¬_¬' : f.dataset.m === 'rage' ? '>_<' : '0_0'); } };
    }
    clearTimeout(flinch.timer);
    flinch.timer = setTimeout(() => { flinch.restore(); flinch = 0; }, 380);
  }
  /* Last frame's size and px-per-unit, for the pair spiral (ui/unlocks/basics/pair-orbit.js) that runs before each step. */
  let lastW = 0, lastH = 0, ux = 0, uy = 0;
  return {
    el,
    /** Centre and px/s velocity, or null before the first step or while the copy cannot move. */
    body() {
      if (!(ux > 0 && uy > 0) || !el.isConnected) return null;
      return { x: x + lastW / 2, y: y + lastH / 2, vx: dx * ux, vy: dy * uy, w: lastW, h: lastH };
    },
    /** Take a body back after the spiral turned or pushed it. */
    apply(b) {
      if (!b || !(ux > 0 && uy > 0)) return;
      x = b.x - lastW / 2; y = b.y - lastH / 2;
      dx = b.vx / ux; dy = b.vy / uy;
    },
    /** A spiral ended: back to the usual diagonal lean and wall-hit rate. */
    settle() { [dx, dy] = snapDiag(dx, dy); },
    /** Touched another copy: the wall feel on the touching side, and an EMI flinch. */
    bump(axis, nx, ny) {
      if (ctx.gentle || ctx.reduced) return;
      hitAt = 0; hitAxis = axis;
      hitOrigin = axis === 'x' ? `${nx > 0 ? '100%' : '0%'} 50%` : `50% ${ny > 0 ? '100%' : '0%'}`;
      react();
    },
    step(dt) {
      const {w:W,h:H} = art.sync();
      const { w, h } = ctx.bounds();
      const r = ctx.rates().walls * (ctx.gentle ? 0.5 : 1);
      lastW = W; lastH = H; ux = r * 0.5 * (w - W); uy = r * 0.5 * (h - H);
      // Half the hits on each axis: speed = hits/s * travel / 2.
      x += dx * r * 0.5 * (w - W) * dt;
      y += dy * r * 0.5 * (h - H) * dt;
      let hx = false, hy = false;
      if (x < 0) { x = 0; dx = Math.abs(dx); hx = true; } else if (x > w - W) { x = w - W; dx = -Math.abs(dx); hx = true; }
      if (y < 0) { y = 0; dy = Math.abs(dy); hy = true; } else if (y > h - H) { y = h - H; dy = -Math.abs(dy); hy = true; }
      // Near enough to a corner counts, and snaps there: two walls on one exact frame would almost never happen.
      if (hx && !hy) { if (y < CORNER_NEAR) { y = 0; dy = Math.abs(dy); hy = true; } else if (y > h - H - CORNER_NEAR) { y = h - H; dy = -Math.abs(dy); hy = true; } }
      if (hy && !hx) { if (x < CORNER_NEAR) { x = 0; dx = Math.abs(dx); hx = true; } else if (x > w - W - CORNER_NEAR) { x = w - W; dx = -Math.abs(dx); hx = true; } }
      if (hx || hy) {
        hitAt = 0; hitAxis = hx && hy ? 'xy' : hx ? 'x' : 'y';
        hitOrigin = `${!hx ? '50%' : x <= 0 ? '0%' : '100%'} ${!hy ? '50%' : y <= 0 ? '0%' : '100%'}`;
        react();
      }
      el.style.transform = `translate(${x}px,${y}px)` + feel(dt);
      if (hx || hy) {
        if (ctx.has('colour') && el.dataset.bounceArt === 'dvd') {
          ink = (ink + 1) % inks.length;
          el.style.setProperty('--bounce-ink',inks[ink]);
        }
        if (++hits % 2 === 0) swap(false);
        const gain = ctx.wallHit({ x: x + W / 2, y: y + H / 2, corner: hx && hy });
        if (gain) ctx.float(x + W / 2, y, `+${ctx.fmt(gain)}`, 'color:var(--stamp);');
      }
    },
  };
}

export default {
  ...look,
  ...look2,
  score_heat: { mount: mountScoreHeat },
  ...trunk,
  ...side,
  ...side2,

  /* Bouncing Button: a second button that pays on every wall hit. Stacks to 12. */
  stray: {
    mount(ctx) {
      ctx.css(FACE_CSS);
      ctx.css(STRAY_CSS);
      const list = [];
      const grow = (n) => { while (list.length < n) list.push(bouncer(ctx,list.length)); };
      grow(ctx.count);
      /* Owner 2026-11-08: copies that pass close spiral into each other and bounce apart. Arrives with Corner Bonus (the
       * later of the two wall-feel rows); Gentle and reduced motion keep straight paths and pass through each other. */
      const orbits = createOrbits();
      const spiral = (dt) => {
        if (list.length < 2 || !ctx.has('corner') || ctx.gentle || ctx.reduced) { orbits.clear(); return; }
        const bodies = list.map((s) => s.body());
        const events = orbits.step(bodies, dt);
        bodies.forEach((b, i) => { if (b) list[i].apply(b); });
        for (const e of events) {
          if (e.type === 'capture') { ctx.emit('stray:spiral', { i: e.i, j: e.j }); continue; }
          list[e.i].settle(); list[e.j].settle();
          if (e.type !== 'contact') continue;
          list[e.i].bump(e.axis, e.nx, e.ny); list[e.j].bump(e.axis, -e.nx, -e.ny);
          ctx.emit('stray:bump', { x: e.x, y: e.y });
        }
      };
      return { grow, tick(dt) { spiral(dt); for (const s of list) s.step(dt); } };
    },
  },

  /* Per Second: the rate under the counter, walls included. */
  fpsv: {
    mount(ctx) {
      const el = ctx.el('div', 'order:1;font-size:16px;', '', ctx.layer('meters'));
      if (fresh(ctx)) ctx.anim(el, [{ opacity: 0, transform: 'translateY(-6px)' }, { opacity: 1, transform: 'none' }], { duration: 300 });
      const draw = () => {
        const r = ctx.rates();
        const rate = r.fps + r.bounce * r.walls * (ctx.state.owned.stray || 0);
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
