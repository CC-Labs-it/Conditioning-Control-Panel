/* ui/unlocks/extras/combos.js - rewards for a particular two-route pick. Each one arrives with a small "you found a
 * secret" stamp, then does its thing. Audio is recorded clips only: nothing is recorded for these yet, so the voice parts
 * show as captions and stay silent. Lines: data/content/combos.js, blind.js. */

import { AUDIO_DRAMA, COMBO_LINES, INDUCTION, SLEEP_STORY } from '../../../data/content/combos.js';
import { LAZY_CAPTIONS } from '../../../data/content/blind.js';
import { drawSpiral, normalizeParams } from '../../../../arcademy/engine/loom/loomSpiral.js';
import { COMBO_COPY as C } from './copy.js';
import { FONT, captions, cardUp, dockTop, h, pick, picker, slot } from './kit.js';

/** The secret stamp: two ingredients slide together into the name. Once per run. */
function found(ctx) {
  if (ctx.store.get('found')) return;
  ctx.store.set('found', 1);
  const { w, h: H } = ctx.bounds();
  const el = ctx.el('div', `position:absolute;left:50%;top:${Math.round(H * 0.3)}px;translate:-50% 0;z-index:8;padding:8px 18px;background:var(--toast-bg);color:var(--toast-ink);border:2px solid var(--btn-edge);border-radius:20px;font:700 18px ${FONT};white-space:nowrap;max-width:${w - 24}px;`, ctx.brand.text(ctx.item.name), 'over');
  ctx.audio.play('assets/audio/sfx/reveal_chime.mp3', { bus: 'sfx', gain: 0.7 });
  const frames = ctx.gentle
    ? [{ opacity: 0 }, { opacity: 1, offset: 0.2 }, { opacity: 1, offset: 0.8 }, { opacity: 0 }]
    : [{ opacity: 0, scale: 0.5 }, { opacity: 1, scale: 1.1, offset: 0.14 }, { opacity: 1, scale: 1, offset: 0.24 }, { opacity: 1, scale: 1, offset: 0.8 }, { opacity: 0, scale: 1.05 }];
  ctx.anim(el, frames, { duration: 2000, fill: 'forwards' }).onfinish = () => el.remove();
}

const combo = (mount) => ({ mount(ctx) { found(ctx); return mount(ctx) || {}; } });

const card = (extra = '') => `background:#fff;color:#111;border:1.5px solid #333;border-radius:4px;font:600 13px/1.2 ${FONT};${extra}`;

export const COMBO_UNLOCKS = {
  /* Watch + Rest: everything soft and slow. */
  x_dream: combo((ctx) => {
    ctx.css('#counter,#btn-zone{filter:blur(.7px);transition:filter 2s;}');
    const blob = ctx.el('div', 'position:absolute;left:50%;top:20px;width:min(90vw,560px);height:300px;margin-left:calc(min(90vw,560px)/-2);border-radius:50%;background:radial-gradient(closest-side,rgba(255,179,218,.9),rgba(255,179,218,0));', null, 'back');
    if (!ctx.gentle) ctx.anim(blob, [{ transform: 'scale(.86)', opacity: 0.6 }, { transform: 'scale(1.12)', opacity: 1 }], { duration: 4000, iterations: Infinity, direction: 'alternate', easing: 'ease-in-out' });
    const line = picker(ctx, COMBO_LINES.x_dream);
    ctx.say(line());
    ctx.every(31000, () => ctx.say(line()));
  }),

  /* Watch + Tap: a word with eyes in it, and the eyes follow you. */
  x_front: combo((ctx) => {
    const r = ctx.parts.counter?.getBoundingClientRect();
    const narrow = ctx.bounds().w < 760;
    const size = narrow ? 40 : 54;
    const word = ctx.el('div', `position:absolute;left:50%;top:${Math.round((r?.top ?? 44) + (narrow ? 66 : 76))}px;translate:-50% 0;display:flex;align-items:center;gap:2px;font:800 ${size}px/1 ${FONT};color:var(--ink);opacity:.82;`, null, 'floaters');
    if (narrow) { word.style.left = 'auto'; word.style.right = '6px'; word.style.translate = 'none'; word.style.top = `${Math.round((r?.bottom ?? 90) + 34)}px`; word.style.fontSize = '26px'; }
    const eyeSize = narrow ? 24 : size - 8;
    const pupils = [];
    h('span', '', 'L', word);
    for (let i = 0; i < 2; i++) {
      const eye = h('span', `position:relative;display:inline-block;width:${eyeSize}px;height:${eyeSize}px;box-sizing:border-box;background:#fff;border:${narrow ? 2 : 3}px solid currentColor;border-radius:50%;`, null, word);
      pupils.push(h('i', `position:absolute;left:50%;top:50%;width:${eyeSize * 0.34}px;height:${eyeSize * 0.34}px;margin:${-eyeSize * 0.17}px 0 0 ${-eyeSize * 0.17}px;border-radius:50%;background:#111;transition:transform .12s;`, null, eye));
    }
    h('span', '', 'K', word);
    const look = (x, y) => {
      const wr = word.getBoundingClientRect();
      const dx = x - (wr.left + wr.width / 2), dy = y - (wr.top + wr.height / 2);
      const d = Math.max(1, Math.hypot(dx, dy)), m = eyeSize * 0.26;
      for (const p of pupils) p.style.transform = `translate(${(dx / d) * m}px,${(dy / d) * m}px)`;
    };
    const onMove = (ev) => look(ev.clientX, ev.clientY);
    document.addEventListener('pointermove', onMove, { passive: true });
    ctx.on('tap', ({ x, y }) => { if (x != null) look(x, y); });
    const line = picker(ctx, COMBO_LINES.x_front);
    ctx.every(29000, () => ctx.say(line()));
    return { off() { document.removeEventListener('pointermove', onMove); } };
  }),

  /* Listen + Rest: the sleep story, as captions until someone records it. */
  x_story: combo((ctx) => {
    const cap = captions(ctx, '📕');
    const s = { next: null };
    const go = () => { let i = ctx.store.get('at') || 0; if (i >= SLEEP_STORY.length) i = 0; ctx.store.set('at', i + 1); cap.show(SLEEP_STORY[i], 7200); };
    s.next = go;
    ctx.after(2400, go);
    ctx.every(9000, go);
  }),

  /* Listen + Tap: the voice counts your taps. Silent until recorded, so the count shows at the button. */
  x_drill: combo((ctx) => {
    const said = COMBO_LINES.x_drill;
    let n = 0, lastShown = 0;
    ctx.on('tap', () => {
      const word = said[n++ % said.length];
      const t = ctx.state.clock;
      if (t - lastShown < 180) return;
      lastShown = t;
      const b = ctx.button.el.getBoundingClientRect();
      ctx.float(b.left + b.width / 2, b.top - 6, word, `font:800 26px ${FONT};color:#a3232b;`);
    });
  }),

  /* Listen + No Pictures: one episode, in the dark, in captions and a wave. There is no episode two. */
  x_audio: combo((ctx) => {
    const { el, wide } = slot(ctx, `position:relative;width:${ctx.bounds().w >= 1100 ? 170 : 82}px;height:${ctx.bounds().w >= 1100 ? 96 : 56}px;background:#1b1626;border:2px dashed #6a6490;color:#fff;overflow:hidden;`);
    const bars = wide ? 11 : 6;
    for (let i = 0; i < bars; i++) {
      const b = h('i', `position:absolute;left:${6 + i * (wide ? 14 : 12)}px;top:${wide ? 12 : 8}px;width:${wide ? 8 : 7}px;height:${wide ? 54 : 30}px;background:var(--accent);border-radius:3px;transform-origin:50% 50%;`, null, el);
      if (ctx.gentle) b.style.transform = `scaleY(${0.3 + (i % 3) * 0.2})`;
      else ctx.anim(b, [{ transform: 'scaleY(.2)' }, { transform: 'scaleY(1)' }], { duration: 500 + (i * 83) % 420, iterations: Infinity, direction: 'alternate', easing: 'ease-in-out' });
    }
    h('div', `position:absolute;left:0;right:0;bottom:2px;text-align:center;font:600 11px ${FONT};`, AUDIO_DRAMA.title, el);
    const cap = captions(ctx, '📡');
    let i = ctx.store.get('at') || 0;
    const next = () => {
      if (i >= AUDIO_DRAMA.lines.length) { cap.show(AUDIO_DRAMA.end, 8000); i = 0; ctx.store.set('at', 0); return; }
      cap.show(AUDIO_DRAMA.lines[i], 7000);
      i += 1;
      ctx.store.set('at', i);
    };
    ctx.after(2600, next);
    ctx.every(13000, next);
  }),

  /* Slideshow + Fewer Ads: a velvet rope in front of the art. Touching it is noticed. */
  x_gallery: combo((ctx) => {
    const line = picker(ctx, COMBO_LINES.x_gallery);
    const b = { top: ctx.parts.buttonWrap.getBoundingClientRect().top - 24 };
    const { w } = ctx.bounds();
    const rw = Math.min(760, w - 8);
    const wrap = ctx.el('div', `position:absolute;left:${(w - rw) / 2}px;top:${Math.round((b?.top ?? 100) - 8)}px;width:${rw}px;height:60px;z-index:1;`, null, 'floaters');
    wrap.innerHTML = `<svg width="${rw}" height="60" viewBox="0 0 ${rw} 60" style="display:block;overflow:visible"><path d="M8 8 Q${rw / 4} 50 ${rw / 2 - 100} 14" stroke="#d62a50" stroke-width="5" fill="none"/><path d="M${rw / 2 + 100} 14 Q${(rw * 3) / 4} 50 ${rw - 8} 8" stroke="#d62a50" stroke-width="5" fill="none"/><rect x="3" y="4" width="8" height="56" rx="3" fill="#d9b44a"/><rect x="${rw - 11}" y="4" width="8" height="56" rx="3" fill="#d9b44a"/></svg>`;
    /* Tappable posts only, so the rope can never get between a finger and the button. */
    for (const left of [0, rw - 44]) {
      const hit = h('button', `position:absolute;left:${left}px;top:0;width:44px;height:60px;background:transparent;border:0;pointer-events:auto;touch-action:manipulation;`, null, wrap);
      hit.type = 'button';
      hit.setAttribute('aria-label', 'Rope');
      hit.addEventListener('click', () => {
        const sway = wrap.firstElementChild;
        if (!ctx.gentle) ctx.anim(sway, [{ transform: 'scaleY(1)' }, { transform: 'scaleY(.7)' }, { transform: 'scaleY(1)' }], { duration: 400 });
        ctx.toast(line());
      });
    }
    ctx.after(900, () => ctx.toast(COMBO_LINES.x_gallery[0]));
  }),

  /* Swipe Feed + More Ads: a little feed where every third post is an ad. */
  x_infinite: combo((ctx) => {
    const wideNow = ctx.bounds().w >= 1100;
    const pw = wideNow ? 110 : 70, ph = wideNow ? 150 : 62, rowH = wideNow ? 46 : 28;
    const { el } = slot(ctx, `position:relative;width:${pw}px;height:${ph}px;background:#111;border:2px solid #111;border-radius:7px;overflow:hidden;`);
    const lines = COMBO_LINES.x_infinite;
    const stack = h('div', 'position:absolute;left:0;top:0;width:100%;will-change:transform;', null, el);
    const COUNT = 12;
    for (let i = 0; i < COUNT; i++) {
      const ad = i % 3 === 2;
      h('div', `height:${rowH - 3}px;margin:0 2px 3px;display:grid;place-items:center;text-align:center;overflow:hidden;font:600 ${wideNow ? 11 : 9}px/1 ${FONT};background:${ad ? '#ffe36b' : ['#4a3a7a', '#2d6a6a'][i % 3]};color:${ad ? '#111' : '#fff'};`, ad ? ctx.brand.text(lines[i % lines.length]) : C.post, stack);
    }
    let y = 0;
    const loop = rowH * 3;
    return {
      tick(dt) {
        if (ctx.gentle) return;
        y = (y + dt * rowH * 0.8) % loop;
        stack.style.transform = `translateY(${-y}px)`;
      },
    };
  }),

  /* No Pictures + Censored: an empty frame with a black bar over it. Peak. */
  x_nothing: combo((ctx) => {
    const wideNow = ctx.bounds().w >= 1100;
    const fw = wideNow ? 150 : 76, fh = wideNow ? 110 : 56;
    const { el } = slot(ctx, `position:relative;width:${fw}px;height:${fh}px;background:#fff;border:3px solid #8a6f3a;`);
    const bar = h('div', `position:absolute;left:-8px;right:-8px;top:42%;height:18%;background:#000;`, null, el);
    ctx.anim(bar, [{ transform: 'scaleX(0)' }, { transform: 'scaleX(1)' }], { duration: 600, easing: 'ease-out' });
    const line = picker(ctx, COMBO_LINES.x_nothing);
    el.addEventListener('click', () => ctx.toast(line()));
    ctx.every(33000, () => ctx.say(line()));
  }),

  /* Tap + Chastity: asking is refused, and refused taps and buys pay double. */
  x_later: combo((ctx) => {
    const line = picker(ctx, COMBO_LINES.x_later);
    const { el } = slot(ctx, 'display:flex;align-items:center;gap:6px;');
    h('span', 'font:24px/1 "Segoe UI Emoji","Apple Color Emoji","Noto Color Emoji",sans-serif;', '🔒', el);
    const beg = h('button', card('min-height:44px;padding:4px 10px;touch-action:manipulation;'), C.letMeOut, el);
    beg.type = 'button';
    let cool = 0;
    const refused = (x, y) => {
      if (ctx.state.clock < cool) return;
      cool = ctx.state.clock + 300;
      const r = ctx.rates();
      ctx.pay(r.click * 2, 'x_later', { x, y });
      ctx.toast(line());
      ctx.audio.play('assets/audio/sfx/ui_denied.mp3', { bus: 'sfx', gain: 0.5 });
    };
    beg.addEventListener('click', () => { const r = beg.getBoundingClientRect(); refused(r.left + r.width / 2, r.top); });
    /* A locked main button says no as well. */
    const onBtn = (ev) => { if (ctx.button.el.classList.contains('locked')) refused(ev.clientX, ev.clientY); };
    ctx.button.el.addEventListener('pointerdown', onBtn);
    /* Now and then a buy is refused too, and the refusal pays. Pressing again goes through. */
    let nextNo = 0;
    ctx.guardBuy(() => {
      if (ctx.state.clock < nextNo || ctx.rng() > 0.34) return null;
      nextNo = ctx.state.clock + 20000;
      const r = ctx.rates(), s = ctx.parts.shop.getBoundingClientRect();
      ctx.pay(Math.max(r.click * 2, r.fps * 2), 'x_later', { x: s.left + s.width / 2, y: s.top + 20 });
      return { refuse: line() };
    });
    return { off() { ctx.button.el.removeEventListener('pointerdown', onBtn); } };
  }),

  /* Listen + Bimbo: the podcast gains a second voice. Pairs show as captions until the show's clips arrive. */
  x_listener: combo((ctx) => {
    const cap = captions(ctx, '🎙️');
    const pairs = COMBO_LINES.x_listener;
    let i = ctx.store.get('at') || 0;
    const turn = () => {
      const [a, b] = pairs[i++ % pairs.length];
      ctx.store.set('at', i);
      cap.show(`${C.cohostA}: ${a}`, 2600);
      ctx.after(3000, () => cap.show(`${C.cohostB}: ${b}`, 2600));
    };
    ctx.after(2200, turn);
    ctx.every(17000, turn);
  }),

  /* Tap + Drone: a task card, double the pay, a stamp nobody asked for. */
  x_compl: combo((ctx) => {
    const line = picker(ctx, COMBO_LINES.x_compl);
    const { el } = slot(ctx, card(`position:relative;width:${ctx.bounds().w >= 1100 ? 150 : 122}px;height:56px;padding:3px 34px 3px 5px;font-size:12px;`));
    const text = h('div', '', COMBO_LINES.x_compl[1], el);
    const pay = h('div', 'font-weight:500;color:#555;', '', el);
    const stamp = h('div', `position:absolute;right:4px;top:8px;width:34px;height:34px;box-sizing:border-box;border:3px solid #c21f2f;border-radius:50%;color:#c21f2f;display:grid;place-items:center;font:800 20px ${FONT};transform:rotate(-12deg);opacity:0;`, 'A', el);
    let n = 0;
    const need = 3;
    const show = () => { pay.textContent = `${n} / ${need}`; };
    show();
    ctx.on('tap', () => {
      n += 1;
      if (n < need) { show(); return; }
      n = 0;
      const r = el.getBoundingClientRect();
      const amount = ctx.rates().click * need * 2;
      const got = ctx.pay(amount, 'x_compl', { x: r.left + r.width / 2, y: r.top });
      pay.textContent = C.taskDone.replace('{n}', ctx.fmt(got));
      stamp.style.opacity = '1';
      ctx.anim(stamp, ctx.gentle ? [{ opacity: 0 }, { opacity: 1 }] : [{ transform: 'rotate(-12deg) scale(1.8)', opacity: 0 }, { transform: 'rotate(-12deg) scale(1)', opacity: 1 }], { duration: 220 });
      ctx.after(900, () => { stamp.style.opacity = '0'; show(); });
      if (ctx.rng() < 0.12) text.textContent = line();
    });
  }),

  /* Swipe Feed + Goon: a streak that never drops, and a clock that runs too fast. */
  x_fourhr: combo((ctx) => {
    const m = ctx.el('div', `font:600 15px ${FONT};color:#a3232b;font-variant-numeric:tabular-nums;`, '', 'meters');
    let n = ctx.store.get('n') || 0;
    const draw = () => {
      const min = Math.floor(ctx.state.clock / 15000);
      m.textContent = `🔥 ${C.streak.replace('{n}', ctx.fmt(n))}  ${Math.floor(min / 60)}:${String(min % 60).padStart(2, '0')}`;
    };
    ctx.on('tap', () => { n += 1; ctx.store.set('n', n); draw(); });
    ctx.every(1000, () => { n += 1; ctx.store.set('n', n); draw(); });
    draw();
    const line = picker(ctx, COMBO_LINES.x_fourhr);
    ctx.every(37000, () => ctx.say(line()));
  }),

  /* Fewer Ads + Sissy: the house rules, on gold paper. */
  x_manners: combo((ctx) => {
    const wideNow = ctx.bounds().w >= 1100;
    const { el } = slot(ctx, `position:relative;width:${wideNow ? 190 : 150}px;height:${wideNow ? 96 : 60}px;padding:4px 8px;background:linear-gradient(135deg,#f7dc8a,#e0b23f 55%,#fbeeb8);border:1px solid #9a7a22;box-shadow:2px 3px 0 rgba(0,0,0,.25);color:#3a2a06;font:500 ${wideNow ? 13 : 11}px/1.2 Georgia,serif;text-align:center;overflow:hidden;`);
    if (!ctx.gentle) ctx.anim(el, [{ transform: 'translateY(-60px)', opacity: 0 }, { transform: 'none', opacity: 1 }], { duration: 380, easing: 'ease-out' });
    h('div', 'font-weight:700;', `🎀 ${C.gold}`, el);
    const rule = h('div', 'margin-top:3px;', '', el);
    const line = picker(ctx, COMBO_LINES.x_manners);
    const next = () => { rule.textContent = ctx.brand.text(line()); ctx.anim(rule, [{ opacity: 0 }, { opacity: 1 }], { duration: 400 }); };
    next();
    ctx.every(11000, next);
    el.addEventListener('click', next);
  }),

  /* Listen + Trance: about five minutes of induction, in captions, with the eyelids slowly coming down. It always
     counts back up, and the lids always lift. Gentle keeps the spiral still and the lids off. */
  x_induct: combo((ctx) => {
    const cap = captions(ctx, '🕯️');
    /* A small disc with the clock on it, like the demo. The Loom's canvas is opaque (dark ground, hot pink arms), so spread
       over the page at any opacity it reads as loud stripes; it stays in a disc of its own at full strength. */
    const big = ctx.bounds().w >= 1100;
    const D = big ? 150 : 60;
    const { el: disc } = slot(ctx, `position:relative;width:${D}px;height:${D}px;border-radius:50%;overflow:hidden;background:#14060f;`);
    const canvas = h('canvas', 'position:absolute;inset:0;width:100%;height:100%;', null, disc);
    canvas.width = canvas.height = D * 2;
    const g = canvas.getContext('2d', { alpha: false });
    const params = normalizeParams({ arms: 4, turns: 2, style: 'log', duty: 0.5, speed: 2, colors: ['#ff69b4'], bg: '#14060f' });
    let phase = 0, spent = 0;
    drawSpiral(g, D * 2, params, phase);
    ctx.frame((dt) => {
      spent += dt;
      if (ctx.gentle || spent < 1 / 20) return;
      phase = (phase + spent * 0.08) % 1;
      spent = 0;
      drawSpiral(g, D * 2, params, phase);
    });
    const clock = h('div', `position:absolute;inset:0;display:grid;place-items:center;color:#fff;font:800 ${big ? 26 : 13}px ${FONT};font-variant-numeric:tabular-nums;text-shadow:0 0 4px #000;`, '', disc);
    const showClock = (left) => { clock.textContent = `${Math.floor(left / 60)}:${String(left % 60).padStart(2, '0')}`; };
    const lid = (top) => ctx.gentle ? null : ctx.el('div', `position:absolute;left:0;right:0;${top ? 'top:0' : 'bottom:0'};height:22vh;background:linear-gradient(${top ? 'to bottom' : 'to top'},rgba(0,0,0,.72),rgba(0,0,0,0));transform:scaleY(0);transform-origin:50% ${top ? '0' : '100%'};`, null, 'over');
    const lids = [lid(true), lid(false)];
    const setLids = (f) => { for (const l of lids) if (l) l.style.transform = `scaleY(${f})`; };
    const STEP = 10000;
    let i = ctx.store.get('at') || 0;
    const stopAt = INDUCTION.length;
    const depth = (k) => (k < 18 ? k / 18 : k > 24 ? Math.max(0, 1 - (k - 24) / 5) : 1);
    let stop = null;
    const next = () => {
      if (i >= stopAt) {
        stop?.();
        setLids(0);
        showClock(0);
        cap.show(C.inductDone, 5000);
        return;
      }
      cap.show(INDUCTION[i], STEP - 800);
      setLids(depth(i));
      showClock((stopAt - i) * (STEP / 1000));
      i += 1;
      ctx.store.set('at', i);
    };
    for (const l of lids) if (l) l.style.transition = 'transform 8s ease-in-out';
    ctx.after(2400, next);
    stop = ctx.every(STEP, next);
    showClock((stopAt - i) * (STEP / 1000));
  }),

  /* More Ads + Infection: little ads breed across the page, then wither. Tapping one pays once. */
  x_viral: combo((ctx) => {
    const copy = COMBO_LINES.x_viral;
    const MAX = ctx.bounds().w < 760 ? 8 : 18;
    const live = new Set();
    const AW = 92, AH = 30;
    const free = (x, y) => {
      const b = ctx.button.el.getBoundingClientRect();
      if (x + AW > b.left - 8 && x < b.right + 8 && y + AH > b.top - 8 && y < b.bottom + 8) return false;
      /* Never over the shop rows: they stay pressable, however viral this gets. */
      const shop = ctx.parts.shop;
      const top = shop.querySelector('#goal')?.getBoundingClientRect();
      const more = shop.querySelector('#more');
      const end = (more?.offsetParent ? more : shop.querySelector('#rows'))?.getBoundingClientRect();
      /* Nor over the number: that is the one thing on the page everybody reads. */
      for (const part of [ctx.parts.counter, ctx.layer('meters')]) {
        const c = part?.getBoundingClientRect();
        if (c && x + AW > c.left + c.width * 0.1 && x < c.right - c.width * 0.1 && y + AH > c.top && y < c.bottom) return false;
      }
      if (!top?.height) return false;
      if (y + AH > dockTop(ctx) - 4) return false;
      if (top && end && x + AW > top.left - 4 && x < top.right + 4 && y + AH > top.top - 4 && y < end.bottom + 4) return false;
      return ![...live].some((a) => Math.abs(a.x - x) < AW && Math.abs(a.y - y) < AH);
    };
    const spawn = (near) => {
      if (live.size >= MAX || cardUp()) return;
      const { w, h: H } = ctx.bounds();
      for (let tries = 0; tries < 12; tries++) {
        const x = near ? near.x + (ctx.rng() - 0.5) * 240 : ctx.rng() * (w - AW);
        const y = near ? near.y + (ctx.rng() - 0.5) * 120 : 44 + ctx.rng() * (H - 100);
        if (x < 2 || y < 40 || x > w - AW - 2 || y > H - AH - 4 || !free(x, y)) continue;
        const a = { x, y, el: null, paid: false };
        a.el = h('button', `position:absolute;left:${x}px;top:${y}px;width:${AW}px;height:${AH}px;padding:0 2px;pointer-events:auto;background:#ffe36b;color:#111;border:1px solid #8a6a10;font:700 11px ${FONT};touch-action:manipulation;`, ctx.brand.text(pick(ctx, copy)), adHost);
        a.el.type = 'button';
        a.el.addEventListener('click', () => {
          ctx.emit('ad', { kind: 'viral' });
          ctx.pay(ctx.rates().fps * ctx.config.AD_CLICK_PAYS_SECONDS, 'ad', { x: x + AW / 2, y });
          kill(a);
        });
        ctx.anim(a.el, [{ transform: 'scale(.3)', opacity: 0 }, { transform: 'none', opacity: 1 }], { duration: 200, easing: 'ease-out' });
        live.add(a);
        a.cancel = ctx.after(16000 + ctx.rng() * 9000, () => kill(a));
        return;
      }
    };
    const kill = (a) => {
      if (!live.delete(a)) return;
      a.cancel?.();
      const f = ctx.anim(a.el, [{ opacity: 1 }, { opacity: 0 }], { duration: 300 });
      f.onfinish = () => a.el.remove();
    };
    const adHost = ctx.el('div', 'position:absolute;inset:0;z-index:1;pointer-events:none;', null, 'over');
    ctx.every(1700, () => {
      /* A caption or a widget may have arrived since this one landed. */
      const b = ctx.button.el.getBoundingClientRect();   // and the button may have grown under one
      for (const a of [...live]) if (a.y + AH > dockTop(ctx) - 4 || (a.x + AW > b.left - 4 && a.x < b.right + 4 && a.y + AH > b.top - 4 && a.y < b.bottom + 4)) kill(a);
      const list = [...live];
      spawn(list.length && ctx.rng() < 0.8 ? list[Math.floor(ctx.rng() * list.length)] : null);
    });
    ctx.after(300, () => spawn(null));
    ctx.after(700, () => spawn(null));
  }),

  /* No Pictures + Bimbo: the captions under the empty frame give up. */
  x_what: combo((ctx) => {
    const wideNow = ctx.bounds().w >= 1100;
    const { el } = slot(ctx, `position:relative;width:${wideNow ? 170 : 110}px;height:${wideNow ? 110 : 56}px;background:#fff;border:2px dashed #999;display:grid;align-items:end;justify-items:center;`);
    const cap = h('div', `padding:2px 4px;font:600 ${wideNow ? 14 : 12}px/1.1 ${FONT};color:#222;text-align:center;`, '', el);
    let i = ctx.store.get('at') || 0;
    const next = () => {
      cap.textContent = LAZY_CAPTIONS[i % LAZY_CAPTIONS.length];
      i += 1;
      ctx.store.set('at', i);
      ctx.anim(cap, [{ opacity: 0 }, { opacity: 1 }], { duration: 280 });
    };
    next();
    ctx.every(4200, next);
  }),
};
