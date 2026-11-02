/* ui/unlocks/extras/quiz.js - Pop-Up Question and the Button Trouble lane. One card at a time, floating in `over`, no
 * backdrop: the game keeps running behind it. Every card closes: it answers, or it gives up by itself after a while, and
 * any answer pays except a melted one, which pays nothing and costs nothing. tricks.js holds the 20 ways to be annoying. */

import { QUESTIONS, TRICKS } from '../../../data/content/questions.js';
import { QUIZ_COPY as C } from './copy.js';
import { FONT, cardUp, h, pick } from './kit.js';
import { TRICK_FNS } from './tricks.js';

const CSS = `
.xq{position:absolute;z-index:5;box-sizing:border-box;background:#fff;color:#111;border:1px solid #333;border-radius:5px;box-shadow:3px 4px 0 rgba(0,0,0,.28);font:600 15px/1.2 ${FONT};pointer-events:auto;user-select:none;-webkit-user-select:none;overflow:hidden;}
.xq-t{position:relative;height:24px;padding-left:8px;background:#7b5cff;color:#fff;font:600 13px/24px ${FONT};}
.xq-tb{position:absolute;left:0;bottom:0;width:100%;height:3px;background:#ffd6ec;transform-origin:0 50%;}
.xq-q{padding:10px 12px 8px;font-size:19px;line-height:1.2;text-align:center;}
.xq-z{position:relative;margin:0 8px 10px;}
.xq-a{position:absolute;box-sizing:border-box;min-height:44px;padding:4px 6px;background:#d4d0c8;color:#111;border:2px outset #fff;font:600 15px/1.1 ${FONT};touch-action:manipulation;transition:left .2s ease-in-out,top .2s ease-in-out,opacity .3s;}
.xq-a:active{border-style:inset;}
.xq-a.yes{background:#ff69b4;color:#fff;border:1.5px solid #c23c84;}
.xq-note{position:absolute;font:600 12px ${FONT};color:#555;text-align:center;pointer-events:none;}
.xq-bar{position:absolute;height:6px;background:#eee;border:1px solid #999;box-sizing:border-box;overflow:hidden;pointer-events:none;}
.xq-bar i{display:block;height:100%;background:#7b5cff;transform-origin:0 50%;transform:scaleX(0);}
.xq-hold{touch-action:none;}
.xq-tile{position:relative;display:grid;place-items:center;background:#e9e6f4;border:1px solid #8d94b5;font:24px/1 "Segoe UI Emoji","Apple Color Emoji","Noto Color Emoji",sans-serif;touch-action:manipulation;padding:0;transition:opacity .25s,outline .1s;}
.xq-tile.on{outline:3px solid #3b7ddd;outline-offset:-3px;}
.xq-grid{display:grid;grid-template-columns:repeat(3,1fr);gap:4px;}
.xq-row{display:flex;gap:8px;margin-top:8px;}
.xq-row .xq-a{position:relative;flex:1;}
.xq-pad{display:flex;flex-wrap:wrap;gap:4px;justify-content:center;margin-top:8px;}
.xq-pad button{width:38px;height:38px;background:#d4d0c8;border:2px outset #fff;font:700 15px ${FONT};color:#111;touch-action:manipulation;padding:0;}
.xq-wob{display:flex;flex-wrap:wrap;justify-content:center;gap:0 3px;padding:8px 4px;background:repeating-linear-gradient(20deg,#e8e4d8 0 5px,#d8d4c6 5px 10px);border:1px solid #888;font:700 22px Georgia,serif;color:#333;}
.xq-wob span{display:inline-block;animation:xqw 1.1s ease-in-out infinite alternate;}
@keyframes xqw{from{translate:0 -2px;}to{translate:0 3px;}}
body.gentle .xq-wob span{animation:none;}
.xq-typed{min-height:26px;margin-top:6px;padding:2px 6px;border:1.5px solid #555;background:#fff;font:600 15px/22px ${FONT};white-space:pre-wrap;}
.xq-stick{position:absolute;display:grid;place-items:center;box-sizing:border-box;border:1px solid #333;box-shadow:1px 1px 0 rgba(0,0,0,.3);font:600 13px ${FONT};color:#333;touch-action:none;cursor:grab;}
.xq-bub{position:absolute;display:grid;place-items:center;text-align:center;border-radius:50%;border:1px solid rgba(40,90,140,.6);background:radial-gradient(circle at 32% 28%,#fff 0 10%,rgba(255,255,255,.2) 28%,rgba(120,190,255,.55) 72%,rgba(60,130,210,.75));color:#134;font:700 13px/1.05 ${FONT};padding:6px;touch-action:manipulation;will-change:transform;}
.xq-slide{position:absolute;left:18px;right:18px;height:8px;background:#ccc;border:1px solid #888;border-radius:4px;touch-action:none;}
.xq-knob{position:absolute;top:-12px;width:26px;height:30px;margin-left:-13px;background:#ff69b4;border:1px solid #333;border-radius:5px;touch-action:none;}
.xq-cur{position:absolute;width:44px;height:44px;margin:-6px 0 0 -6px;z-index:4;touch-action:manipulation;}
.xq-cur i{position:absolute;left:6px;top:6px;width:22px;height:30px;background:#ff2f92;clip-path:polygon(0 0,0 100%,30% 74%,52% 100%,70% 90%,48% 64%,100% 64%);}
`;

const POP = 'assets/audio/pop/Pop.mp3';
const FIRST_MS = 26000, EVERY_MS = 48000, EVERY_JITTER = 18000, GIVE_UP_MS = 30000;

function createQuiz(ctx) {
  ctx.css(CSS);
  const host = ctx.el('div', 'position:absolute;inset:0;z-index:5;pointer-events:none;', null, 'over');
  let open = null, lastId = null;

  const ownedTricks = () => Object.keys(TRICKS).filter((t) => ctx.has(TRICKS[t].unlock));

  /** Mostly the newest few tricks, sometimes any: what you just bought should show up, the old ones still surprise. */
  function chooseTrick() {
    const all = ownedTricks();
    if (all.length > 1 && ctx.rng() < 0.15) return all[Math.floor(ctx.rng() * all.length)];
    const recent = all.slice(-4);
    return recent[Math.floor(ctx.rng() * recent.length)];
  }

  function chooseQuestion(trick) {
    const pool = QUESTIONS.filter((q) => q.trick === trick);
    let q = pick(ctx, pool);
    if (q.id === lastId && pool.length > 1) q = pool[(pool.indexOf(q) + 1) % pool.length];
    lastId = q.id;
    return q;
  }

  function place(card, cardH) {
    const { w, h: H } = ctx.bounds();
    const cw = card.offsetWidth;
    const b = ctx.button.el.getBoundingClientRect();
    /* Below the goal row, so tapping and the next buy go on; beside the shop on a wide screen. */
    let x, y;
    if (w >= 1120) {
      const side = ctx.rng() < 0.5;
      x = side ? 12 + ctx.rng() * Math.max(0, (w - 780) / 2 - cw - 12) : w - cw - 12 - ctx.rng() * Math.max(0, (w - 780) / 2 - cw - 12);
      y = 60 + ctx.rng() * Math.max(0, H - cardH - 130);
    } else {
      x = (w - cw) / 2 + (ctx.rng() - 0.5) * Math.max(0, w - cw - 16);
      const goal = ctx.parts.shop.querySelector('#goal')?.getBoundingClientRect();
      const below = (goal?.height ? goal.bottom : b.bottom) + 8;
      const room = H - below - 8;
      if (cardH > room) {
        card.style.transformOrigin = '50% 100%';
        card.style.scale = String(Math.max(0.68, room / cardH));
      }
      y = Math.min(below, H - cardH - 8);
    }
    card.style.left = `${Math.max(4, x)}px`;
    card.style.top = `${Math.max(46, y)}px`;
  }

  function close(paid) {
    const o = open;
    if (!o) return;
    open = null;
    for (const c of o.cancels) c();
    for (const a of o.anims) { try { a.cancel(); } catch { /* gone */ } }
    o.card.style.pointerEvents = 'none';
    const a = ctx.anim(o.card, paid ? [{ transform: 'none', opacity: 1 }, { transform: 'scale(1.06)', opacity: 0 }] : [{ opacity: 1 }, { opacity: 0 }], { duration: paid ? 170 : 320, easing: 'ease-out' });
    a.onfinish = () => o.card.remove();
    a.oncancel = () => o.card.remove();
  }

  /** Ask now. The new one replaces whatever was up. */
  function ask(trick = chooseTrick()) {
    if (open) close(false);
    const q = chooseQuestion(trick);
    const { w } = ctx.bounds();
    const W = Math.min(340, w - 16);
    const card = h('div', `width:${W}px;left:-999px;top:0;`, null, host);
    card.className = 'xq';
    const title = h('div', '', C.title, card);
    title.className = 'xq-t';
    const tb = h('div', '', null, title);
    tb.className = 'xq-tb';
    const text = h('div', '', ctx.brand.text(q.text), card);
    text.className = 'xq-q';
    const zone = h('div', '', null, card);
    zone.className = 'xq-z';
    const zw = W - 18;

    const o = { card, cancels: [], anims: [], trick };
    open = o;

    /* What a trick gets. Everything it starts goes in o, so it stops when the card goes. */
    const onlyYes = ctx.has('onlyyes') && trick !== 'mono';
    let alsoYes = pick(ctx, C.alsoYes);
    const label = (ans) => (onlyYes && ans.tone === 'no' ? alsoYes : ans.label);
    const tone = (ans) => (onlyYes ? 'yes' : ans.tone);
    const t = {
      ctx, q, card, zone, zw, W, gentle: ctx.gentle,
      yes: q.answers.find((a) => a.tone !== 'no') || q.answers[0],
      no: q.answers.find((a) => a.tone === 'no') || q.answers[q.answers.length - 1],
      label, tone,
      after(ms, fn) { const c = ctx.after(ms, () => { if (open === o) fn(); }); o.cancels.push(c); return c; },
      every(ms, fn) { const c = ctx.every(ms, () => { if (open === o) fn(); }); o.cancels.push(c); return c; },
      frame(fn) { const c = ctx.frame((dt, now) => { if (open === o) fn(dt, now); }); o.cancels.push(c); return c; },
      anim(el, kf, opts) { const a = ctx.anim(el, kf, opts); o.anims.push(a); return a; },
      el: h,
      /* An answer button at x,y. It answers on press unless the trick says otherwise. */
      btn(ans, x, y, bw, bh = 46, parent = zone) {
        const b = h('button', `left:${x}px;top:${y}px;width:${bw}px;height:${bh}px;`, ctx.brand.text(label(ans)), parent);
        b.type = 'button';
        b.className = `xq-a${tone(ans) === 'no' ? '' : ans === t.yes ? ' yes' : ''}`;
        b.addEventListener('click', () => { if (!b.blocked) t.answer(ans, b); });
        return b;
      },
      note(text2, x, y, nw, parent = zone) {
        const n = h('div', `left:${x}px;top:${y}px;width:${nw}px;`, ctx.brand.text(text2), parent);
        n.className = 'xq-note';
        return n;
      },
      float(x, y, text2, css) { ctx.float(x, y, text2, css); },
      /* Answer it: pays and closes. */
      answer(ans, b, mult = 1) {
        if (open !== o) return;
        const r = (b || card).getBoundingClientRect();
        const rates = ctx.rates();
        const secs = tone(ans) === 'no' ? 2 : 4;
        const amount = Math.max(rates.fps * secs, rates.click * 10) * mult;
        ctx.pay(amount, 'quiz', { x: r.left + r.width / 2, y: r.top });
        ctx.audio.play(tone(ans) === 'no' ? 'assets/audio/sfx/ui_click.mp3' : 'assets/audio/sfx/chime1.mp3', { bus: 'sfx', gain: 0.7 });
        close(true);
      },
      pop() { ctx.audio.play(POP, { bus: 'sfx', gain: 0.7 }); },
      deny() { ctx.audio.play('assets/audio/sfx/ui_denied.mp3', { bus: 'sfx', gain: 0.6 }); },
      nudge(el) { if (!ctx.gentle) t.anim(el, [{ translate: '-4px 0' }, { translate: '4px 0' }, { translate: '0 0' }], { duration: 180 }); },
    };

    const giveUpMs = (TRICK_FNS[trick]?.extraMs || 0) + GIVE_UP_MS;
    (TRICK_FNS[trick]?.draw || TRICK_FNS.none.draw)(t);

    const cardH = card.offsetHeight;
    place(card, cardH);
    t.anim(tb, [{ transform: 'scaleX(1)' }, { transform: 'scaleX(0)' }], { duration: giveUpMs, easing: 'linear', fill: 'forwards' });
    ctx.anim(card, ctx.gentle ? [{ opacity: 0 }, { opacity: 1 }] : [{ opacity: 0, translate: '0 12px' }, { opacity: 1, translate: '0 0' }], { duration: 240, easing: 'ease-out' });
    ctx.audio.play('assets/audio/sfx/chip_pop.mp3', { bus: 'sfx', gain: 0.5 });
    t.after(giveUpMs, () => { ctx.toast(pick(ctx, C.giveUp)); close(false); });
    return o;
  }

  /* The clock: nothing pops over a card or while one is up. A late one waits for the next round. */
  function maybeAsk() {
    if (open || cardUp()) return;
    ask();
  }
  let stop = null;
  const schedule = (ms) => { stop?.(); stop = ctx.after(ms, () => { maybeAsk(); schedule(EVERY_MS + ctx.rng() * EVERY_JITTER); }); };
  schedule(FIRST_MS);

  return { ask, close: () => close(false), get open() { return open; } };
}

let quiz = null;

export const QUIZ_UNLOCKS = {
  quiz: {
    mount(ctx) {
      quiz = createQuiz(ctx);
      if (!ctx.store.get('asked')) { ctx.store.set('asked', 1); ctx.after(650, () => quiz?.ask('none')); }
      return { ask: quiz.ask, off() { quiz?.close(); quiz = null; } };
    },
  },
};

/* Every trick row: the first time it is owned, it shows itself within a second. */
for (const [trick, def] of Object.entries(TRICKS)) {
  if (def.unlock === 'quiz') continue;
  QUIZ_UNLOCKS[def.unlock] = {
    mount(ctx) {
      if (!ctx.store.get('shown')) { ctx.store.set('shown', 1); ctx.after(650, () => quiz?.ask(trick)); }
      return {};
    },
  };
}
