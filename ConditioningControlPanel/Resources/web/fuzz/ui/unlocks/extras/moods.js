/* ui/unlocks/extras/moods.js - Run Moods, the first card of every run after the first. The rules are numbers in
 * sim/moods.js; this file is how you can tell: the start card rolls and lands, and a chip under the counter says which
 * mood you are in. Tap the chip for the rule, there is no hover. The start card itself is main.js's, dressed from outside. */

import { MOODS, MOOD_CARD } from '../../../data/content/combos.js';
import { EMI_BUY_ANY, EMI_EARLY } from '../../../data/content/emi.js';
import { BY_ID } from '../../../data/items.js';
import { moodOf } from '../../../sim/moods.js';
import { MOOD_COPY as C } from './copy.js';
import { getEmi } from './emi.js';
import { arrive, buyLine } from './emi-unlocks.js';
import { FONT, h, picker } from './kit.js';

const EMOJI_FONT = '"Segoe UI Emoji","Apple Color Emoji","Noto Color Emoji",sans-serif';
const contentOf = (id) => MOODS.find((m) => m.id === id);

/** Turn main.js's plain "Today" card into the roll. It is on screen by the time the clock first runs. */
function dressCard(ctx, id) {
  const m = contentOf(id);
  ctx.after(1, () => {
    const card = document.querySelector('#cards .card');
    const title = card?.querySelector('h2');
    if (!title || title.textContent !== 'Today') return;
    title.textContent = MOOD_CARD.title;
    const body = card.querySelector('p');
    if (body) body.textContent = `${m.name}. ${m.rule}`;
    const ok = card.querySelector('.card-buttons button');
    if (ok) ok.textContent = MOOD_CARD.button;
    if (ctx.gentle) return;
    const row = document.createElement('div');
    row.style.cssText = 'display:flex;justify-content:space-between;gap:2px;margin:2px 0 10px;';
    const list = MOODS.map((x) => h('span', `flex:1;text-align:center;padding:3px 0;font:22px/1.3 ${EMOJI_FONT};opacity:.35;border-radius:5px;`, BY_ID[x.id].emoji, row));
    card.insertBefore(row, body);
    const end = MOODS.indexOf(m);
    let i = 0, steps = MOODS.length * 2 + end;
    const paint = () => { list.forEach((e, j) => { const on = j === i % list.length; e.style.opacity = on ? '1' : '.35'; e.style.background = on ? '#ffdcee' : 'none'; }); };
    if (body) body.style.visibility = 'hidden';
    paint();
    const stop = ctx.every(110, () => {
      i += 1;
      paint();
      steps -= 1;
      if (steps > 0) return;
      stop();
      if (body) body.style.visibility = '';
      ctx.audio.play('assets/audio/sfx/reveal_chime.mp3', { bus: 'sfx', gain: 0.6 });
      ctx.anim(list[end], [{ transform: 'scale(1.5)' }, { transform: 'none' }], { duration: 240, easing: 'ease-out' });
    });
  });
}

/** The chip under the counter. Press it to read the rule. */
function chip(ctx, id) {
  const m = contentOf(id);
  const b = ctx.el('button', `pointer-events:auto;margin-top:2px;padding:0 8px;min-height:24px;background:transparent;color:var(--ink-soft);border:1px solid var(--line);border-radius:12px;font:600 12px ${FONT};touch-action:manipulation;white-space:nowrap;`, `${BY_ID[id].emoji} ${m.name}`, 'meters');
  b.type = 'button';
  b.addEventListener('click', () => ctx.toast(m.rule));
  return b;
}

const mood = (fn) => ({
  mount(ctx) {
    const id = ctx.item.id;
    if (ctx.state.clock < 500) dressCard(ctx, id);
    const b = chip(ctx, id);
    return fn?.(ctx, b) || {};
  },
});

export const MOOD_UNLOCKS = {
  /* Busy: a faint band of static rolls down the page now and then. The bouncers are in the sim. */
  m_static: mood((ctx) => {
    ctx.sayRate(0.5);
    if (ctx.gentle) return;
    const band = ctx.el('div', 'position:absolute;left:0;right:0;top:0;height:60px;opacity:0;background:repeating-linear-gradient(0deg,rgba(0,0,0,.09) 0 1px,transparent 1px 3px);', null, 'wash');
    const roll = () => { ctx.anim(band, [{ transform: 'translateY(-60px)', opacity: 0.8 }, { transform: `translateY(${ctx.bounds().h}px)`, opacity: 0.8 }], { duration: 3600, easing: 'linear' }); };
    ctx.after(1200, roll);
    ctx.every(19000, roll);
  }),

  /* Pink Day: a blush on the page and a few petals. Gentle keeps the blush. */
  m_pink: mood((ctx) => {
    ctx.el('div', 'position:absolute;inset:0;background:rgba(255,105,180,.1);', null, 'wash');
    if (ctx.gentle) return;
    const { w, h: H } = ctx.bounds();
    const count = w < 760 ? 4 : 7;
    for (let i = 0; i < count; i++) {
      const p = ctx.el('div', `position:absolute;left:${Math.round(ctx.rng() * (w - 24))}px;top:-30px;font:18px/1 ${EMOJI_FONT};opacity:.7;`, '🌸', 'floaters');
      const sway = (ctx.rng() - 0.5) * 80;
      ctx.anim(p, [{ transform: 'translate(0,0) rotate(0deg)' }, { transform: `translate(${sway}px,${H + 60}px) rotate(240deg)` }], { duration: 9000 + ctx.rng() * 6000, delay: -ctx.rng() * 9000, iterations: Infinity, easing: 'linear' });
    }
  }),

  /* Quiet: silent until Headphones or Rain, and the chip tells you so. */
  m_quiet: mood((ctx, b) => {
    const until = moodOf(ctx.state)?.silentUntil || [];
    const name = contentOf('m_quiet').name;
    const heard = () => until.some((id) => ctx.has(id));
    const paint = () => { b.textContent = `${heard() ? '🔊' : '🔇'} ${name}`; };
    paint();
    let was = heard();
    ctx.on('buy', () => { const now = heard(); paint(); if (now && !was) ctx.toast(C.soundOn); was = now; });
    if (!was) ctx.after(1500, () => ctx.toast(C.soundOff));
  }),

  /* Rush: a car goes past, for no reason. */
  m_rush: mood((ctx) => {
    if (ctx.gentle) return;
    const r = ctx.parts.counter?.getBoundingClientRect();
    const car = ctx.el('div', `position:absolute;left:0;top:${Math.round((r?.bottom ?? 90) + 2)}px;font:22px/1 ${EMOJI_FONT};transform:translateX(-40px) scaleX(-1);`, '🏎️', 'floaters');
    const go = () => { const { w } = ctx.bounds(); ctx.anim(car, [{ transform: 'translateX(-40px) scaleX(-1)' }, { transform: `translateX(${w + 40}px) scaleX(-1)` }], { duration: 1500, easing: 'ease-in' }); };
    ctx.after(1000, go);
    ctx.every(23000, go);
  }),

  /* Lucky: a clover for every golden button. */
  m_lucky: mood((ctx) => {
    ctx.on('golden', () => {
      const { w } = ctx.bounds();
      ctx.float(w / 2, 120, '🍀', 'font-size:28px;');
    });
  }),

  /* Broke: every ad press says what it was worth to you. */
  m_broke: mood((ctx) => {
    ctx.on('ad', () => {
      const r = ctx.parts.counter?.getBoundingClientRect();
      ctx.float((r?.left ?? 100) + (r?.width ?? 100) / 2, (r?.bottom ?? 90), 'x2', 'color:#1f9d55;font:800 20px sans-serif;');
    });
  }),

  /* Watched: EMI turns up at 1,000 and has a view on everything. */
  m_watch: mood((ctx) => {
    const at = moodOf(ctx.state)?.emiAt ?? 1000;
    const early = picker(ctx, EMI_EARLY);
    const any = picker(ctx, EMI_BUY_ANY);
    let e = null;
    const come = () => {
      e = getEmi(ctx);
      e.early = true;
      if (!ctx.store.get('came')) { ctx.store.set('came', 1); arrive(ctx, e); ctx.after(500, () => { e.mood('glee', 1400); e.say(EMI_EARLY[0]); }); }
      ctx.every(16000, () => { if (!ctx.has('em_head') && e.pos.s === 1) e.say(early(), 3400); });
      ctx.on('buy', ({ item }) => { if (!ctx.has('em_chat')) ctx.after(260, () => e.say(buyLine(ctx, item, any), 3000)); });
      ctx.on('tap', () => { if (ctx.state.taps % 40 === 0 && !ctx.has('em_chat')) e.say(early(), 2600); });
    };
    if (ctx.state.total >= at) come();
    else { const stop = ctx.every(200, () => { if (ctx.state.total >= at) { stop(); come(); } }); }
    return { tick() { e?.tick(); } };
  }),

  /* The roll itself is every mood's first card, above. This row is the rule of the game, not a thing on screen. */
  m_mood: { mount() { return {}; } },
};
