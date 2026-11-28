/* ui/unlocks/extras/quiz.js - Pop-Up Question and the Button Trouble lane. One card at a time inside a movable window. No backdrop: the
 * game keeps running behind it. Four answers, one of them what the game wants. That one pays a lot, any other a little,
 * and the card then shows which it was. Every card closes: it answers, or it gives up by itself after a while. A melted
 * answer pays nothing and costs nothing. tricks.js holds the 20 ways to be annoying. */

import { QUESTIONS, TRICKS, quizPay } from '../../../data/content/questions.js';
import { QUIZ_THEMES, lessonAnswer, takeThemedQuestion } from '../../../data/content/themed-questions.js';
import { QUIZ_COPY as C } from './copy.js';
import { FONT, cardUp, h, pick } from './kit.js';
import { getEmi } from './emi.js';
import { attachQuestion, createQuizReactions } from './quiz-mascot.js';
import { TRICK_FNS } from './tricks.js';
import { STAGE_RETRY_MS, quizStage, stageBonus, stageGap } from './quiz-stages.js';

const CSS = `
.xq-dock{left:var(--question-x,0px)!important;top:var(--question-y,0px)!important;max-height:none!important;transform:scale(var(--question-scale,1));transform-origin:0 0;}
.xq-dock>.dk-b{overflow:clip;pointer-events:none;}
.xq-shell{box-sizing:border-box;width:100%;overflow:clip;pointer-events:none;}
.xq-stack{min-width:0;}
.xq-dock .xq,.xq-dock .xq-idle{pointer-events:auto;}
.xq-dock .xq{transform-origin:var(--question-origin,left center);}

.xq{position:absolute;z-index:5;box-sizing:border-box;background:color-mix(in srgb,var(--niche) 10%,#fff);color:#111;border:1px solid color-mix(in srgb,var(--niche) 55%,#000);border-radius:8px;box-shadow:0 3px 10px rgba(0,0,0,.18);font:600 15px/1.2 ${FONT};pointer-events:auto;user-select:none;-webkit-user-select:none;overflow:hidden;transform-origin:left 54px;}
.xq-t{position:relative;height:24px;padding-left:8px;background:linear-gradient(90deg,var(--niche),var(--niche-2));color:var(--niche-ink);font:600 13px/24px ${FONT};}
.xq-tb{position:absolute;left:0;bottom:0;width:100%;height:3px;background:var(--niche-soft);transform-origin:0 50%;}
.xq-q{padding:7px 9px 6px;font-size:16px;line-height:1.2;text-align:center;}
.xq-q.said{font-weight:700;}
.xq-lesson{padding:0 8px 6px;text-align:center;font-size:11px;color:#555;font-variant-numeric:tabular-nums;}
.xq-compact .xq-q{font-size:15px;line-height:1.25;}
.xq-compact .xq-a{font-size:12px;line-height:1.2;}
.xq-z{position:relative;margin:0 8px 10px;}
.xq-a{position:absolute;box-sizing:border-box;min-height:44px;padding:4px 6px;background:color-mix(in srgb,var(--niche) 26%,#fff);color:#111;border:2px solid color-mix(in srgb,var(--niche) 60%,#000);border-bottom-width:4px;border-radius:4px;font:600 15px/1.1 ${FONT};touch-action:manipulation;transition:left .2s ease-in-out,top .2s ease-in-out,opacity .3s;}
.xq-a:active{border-bottom-width:2px;}
.xq-a.ok,.xq-bub.ok{background:#1f9d55;color:#fff;border:2px solid #14663a;opacity:1 !important;}
.xq-a.bad,.xq-bub.bad{background:#a3232b;color:#fff;border:2px solid #6b1219;opacity:1 !important;}
.xq-done button,.xq-done .xq-stick{pointer-events:none;}
.xq-idle{width:fit-content;margin:0;white-space:nowrap;padding:4px 8px;border-radius:6px;background:color-mix(in srgb,var(--panel) 88%,transparent);font-size:11px;color:var(--ink-soft);font-variant-numeric:tabular-nums;}
.xq-idle[hidden]{display:none;}
.xq-note{position:absolute;font:600 12px ${FONT};color:#555;text-align:center;pointer-events:none;}
.xq-bar{position:absolute;height:6px;background:#eee;border:1px solid #999;box-sizing:border-box;overflow:hidden;pointer-events:none;}
.xq-bar i{display:block;height:100%;background:linear-gradient(90deg,var(--niche),var(--niche-2));transform-origin:0 50%;transform:scaleX(0);}
.xq-hold{touch-action:none;}
.xq-tile{position:relative;display:grid;place-items:center;background:color-mix(in srgb,var(--niche) 16%,#fff);border:1px solid color-mix(in srgb,var(--niche) 60%,#000);font:24px/1 "Segoe UI Emoji","Apple Color Emoji","Noto Color Emoji",sans-serif;touch-action:manipulation;padding:0;transition:opacity .25s,outline .1s;}
.xq-tile.on{outline:3px solid var(--niche-2);outline-offset:-3px;}
.xq-grid{display:grid;grid-template-columns:repeat(3,1fr);gap:4px;}
.xq-row{display:grid;grid-template-columns:1fr 1fr;gap:6px;margin-top:8px;}
.xq-row .xq-a{position:relative;}
.xq-pad{display:flex;flex-wrap:wrap;gap:4px;justify-content:center;margin-top:8px;}
.xq-pad button{width:38px;height:38px;background:color-mix(in srgb,var(--niche) 26%,#fff);border:2px solid color-mix(in srgb,var(--niche) 60%,#000);font:700 15px ${FONT};color:#111;touch-action:manipulation;padding:0;}
.xq-wob{display:flex;flex-wrap:wrap;justify-content:center;gap:0 3px;padding:8px 4px;background:repeating-linear-gradient(20deg,#e8e4d8 0 5px,#d8d4c6 5px 10px);border:1px solid #888;font:700 22px Georgia,serif;color:#333;}
.xq-wob span{display:inline-block;animation:xqw 1.1s ease-in-out infinite alternate;}
@keyframes xqw{from{translate:0 -2px;}to{translate:0 3px;}}
body.gentle .xq-wob span{animation:none;}
.xq-typed{min-height:26px;margin-top:6px;padding:2px 6px;border:1.5px solid #555;background:#fff;font:600 15px/22px ${FONT};white-space:pre-wrap;}
.xq-stick{position:absolute;display:grid;place-items:center;box-sizing:border-box;border:1px solid #333;box-shadow:1px 1px 0 rgba(0,0,0,.3);font:600 13px ${FONT};color:#333;touch-action:none;cursor:grab;}
.xq-bub{position:absolute;display:grid;place-items:center;text-align:center;border-radius:50%;border:1px solid rgba(40,90,140,.6);background:radial-gradient(circle at 32% 28%,#fff 0 10%,rgba(255,255,255,.2) 28%,rgba(120,190,255,.55) 72%,rgba(60,130,210,.75));color:#134;font:700 13px/1.05 ${FONT};padding:6px;touch-action:manipulation;will-change:transform;}
.xq-slide{position:absolute;left:18px;right:18px;height:8px;background:#ccc;border:1px solid #888;border-radius:4px;touch-action:none;}
.xq-knob{position:absolute;top:-12px;width:26px;height:30px;margin-left:-13px;background:var(--niche);border:1px solid #333;border-radius:5px;touch-action:none;}
.xq-cur{position:absolute;width:44px;height:44px;margin:-6px 0 0 -6px;z-index:4;touch-action:manipulation;}
.xq-cur i{position:absolute;left:6px;top:6px;width:22px;height:30px;background:#ff2f92;clip-path:polygon(0 0,0 100%,30% 74%,52% 100%,70% 90%,48% 64%,100% 64%);}
`;

const POP = 'assets/audio/pop/Pop.mp3';
const FIRST_MS = 26000, EVERY_MS = 48000, EVERY_JITTER = 18000, GIVE_UP_MS = 30000;

function createQuiz(ctx) {
  ctx.css(CSS);
  const emi = getEmi(ctx);
  const spot = ctx.dock({ anchor:emi, order:-1, min:false, title:C.title, icon:ctx.item.emoji, width:284 });
  spot.dock.root.classList.add('xq-dock');
  const shell = ctx.el('div', '', null, spot); shell.className = 'xq-shell'; shell.dataset.dockSurface = '';
  const mascot = createQuizReactions(ctx, emi);
  const stack = ctx.el('div', '', null, shell); stack.className = 'xq-stack';
  const idle = ctx.el('div', '', '', stack);
  idle.className = 'xq-idle';
  idle.dataset.dockSurface = '';
  const host = ctx.el('div', 'position:relative;', null, stack);
  const align = attachQuestion(ctx, emi, spot, idle);
  let open = null, exiting = null, lastId = null, dueAt = 0;
  let lesson = lessonAnswer(ctx.store.get('lesson'), false).progress;

  const liveRow = (id) => ctx.has(id) && ctx.switches.isOn(id);
  const ownedTricks = () => Object.keys(TRICKS).filter((t) => liveRow(TRICKS[t].unlock));

  /** Mostly the newest few tricks, sometimes any: what you just bought should show up, the old ones still surprise. */
  function chooseTrick() {
    const all = ownedTricks();
    if (all.length > 1 && ctx.rng() < 0.15) return all[Math.floor(ctx.rng() * all.length)];
    const recent = all.slice(-4);
    return recent[Math.floor(ctx.rng() * recent.length)] || 'none';
  }

  function chooseQuestion(trick) {
    const theme = ctx.pick('f5');
    const decks = ctx.store.get('questionDecks') || {};
    const themed = takeThemedQuestion(theme, trick, decks[theme], ctx.rng);
    if (themed) {
      ctx.store.set('questionDecks', { ...decks, [theme]: themed.deck });
      return themed.question;
    }
    const pool = QUESTIONS.filter((q) => q.trick === trick);
    let q = pick(ctx, pool);
    if (q.id === lastId && pool.length > 1) q = pool[(pool.indexOf(q) + 1) % pool.length];
    lastId = q.id;
    return q;
  }

  const cardWidth = () => Math.max(160, Math.min(ctx.bounds().w - 16, host.clientWidth || parseFloat(spot.parentElement.style.width) || 288));

  function discardExit() {
    if (!exiting) return;
    const previous = exiting;
    exiting = null;
    previous.animation.cancel();
    previous.card.remove();
  }

  function close() {
    const o = open;
    if (!o) return;
    open = null;
    mascot.idle();
    spot.dock?.setReady(false);
    for (const c of o.cancels) c();
    for (const a of o.anims) { try { a.cancel(); } catch { /* gone */ } }
    o.card.inert = true;
    o.card.style.pointerEvents = 'none';
    const calm = ctx.gentle || ctx.reduced;
    const frames = calm ? [{ opacity: 1 }, { opacity: 0 }] : [
      { opacity: 1, transform: 'none' },
      { opacity: 0, transform: 'translateX(-4px) scale(.88,.95)' },
    ];
    const animation = ctx.anim(o.card, frames, { duration: calm ? 260 : 155, easing: 'ease-in', fill: 'both' });
    const outgoing = { card: o.card, animation };
    exiting = outgoing;
    const finish = () => {
      o.card.remove();
      if (exiting !== outgoing) return;
      exiting = null;
      if (open || !idle.isConnected) return;
      idle.hidden = false;
      align();
      ctx.anim(idle, [{ opacity: 0 }, { opacity: 1 }], { duration: calm ? 260 : 130, easing: 'ease-out' });
    };
    animation.finished.then(finish, finish);
  }

  /** Ask now. The new one replaces whatever was up. */
  function ask(trick = chooseTrick()) {
    if (ctx.paused) return null;
    if (open) close();
    discardExit();
    const q = chooseQuestion(trick);
    const W = cardWidth();
    // The whole quiz scales with its dock; keep the active trick in its original coordinates.
    const card = h('div', `position:relative;width:${W}px;`, null, host);
    card.className = W < 240 ? 'xq xq-compact' : 'xq';
    card.dataset.quizTheme = ctx.pick('f5') || '';
    card.dataset.quizQuestion = q.id;
    /* Trick-specific DOM handlers also obey the host pause, including dragging and held answers. */
    for (const event of ['pointerdown', 'pointermove', 'pointerup', 'click', 'keydown']) card.addEventListener(event, (ev) => {
      if (!ctx.paused) return;
      ev.preventDefault(); ev.stopImmediatePropagation();
    }, true);
    const themeName = QUIZ_THEMES[ctx.pick('f5')];
    const title = h('div', '', themeName ? `${themeName} quiz` : C.title, card);
    title.className = 'xq-t';
    title.dataset.dockSurface = '';
    const tb = h('div', '', null, title);
    tb.className = 'xq-tb';
    const text = h('div', '', ctx.brand.text(q.text), card);
    text.className = 'xq-q';
    text.dataset.dockSurface = '';
    const lessonLabel = h('div', '', `Lesson ${lesson}/3`, card);
    lessonLabel.className = 'xq-lesson';
    const zone = h('div', '', null, card);
    zone.className = 'xq-z';
    zone.dataset.dockInteractive = '';
    const zw = W - 18;

    const o = { card, cancels: [], anims: [], trick, done: false, buttons: new Map(), melted: 0 };
    open = o;
    mascot.ask();
    idle.hidden = true; spot.dock?.setReady(true, 'Question ready');
    const live = () => open === o && !o.done && !ctx.paused;

    /* What a trick gets. Everything it starts goes in o, so it stops when the card goes. */
    /* Only Yes: every wrong answer turns into another way of saying yes. All four pay. */
    const onlyYes = liveRow('onlyyes') && trick !== 'mono';
    const yeses = new Map();
    const label = (ans) => {
      if (!onlyYes || ans.tone === 'yes') return ans.label;
      if (!yeses.has(ans)) yeses.set(ans, C.alsoYes[yeses.size % C.alsoYes.length]);
      return yeses.get(ans);
    };
    const tone = (ans) => (onlyYes ? 'yes' : ans.tone);
    const yes = q.answers.find((a) => a.tone === 'yes') || q.answers[0];
    const t = {
      ctx, q, card, zone, zw, W, gentle: ctx.gentle,
      yes,
      wrongs: q.answers.filter((a) => a !== yes),
      label, tone,
      after(ms, fn) { const c = ctx.after(ms, () => { if (live()) fn(); }); o.cancels.push(c); return c; },
      every(ms, fn) { const c = ctx.every(ms, () => { if (live()) fn(); }); o.cancels.push(c); return c; },
      frame(fn) { const c = ctx.frame((dt, now) => { if (live()) fn(dt, now); }); o.cancels.push(c); return c; },
      anim(el, kf, opts) { const a = ctx.anim(el, kf, opts); o.anims.push(a); return a; },
      el: h,
      /* An answer button at x,y. It answers on press unless the trick says otherwise. */
      btn(ans, x, y, bw, bh = 46, parent = zone) {
        const b = h('button', `left:${x}px;top:${y}px;width:${bw}px;height:${bh}px;`, ctx.brand.text(label(ans)), parent);
        b.type = 'button';
        b.className = 'xq-a';
        o.buttons.set(ans, b);
        b.addEventListener('click', () => { if (!b.blocked) t.answer(ans, b); });
        return b;
      },
      /* Tell the card which button stands for an answer, for one a trick drew itself. */
      mark(ans, b) { o.buttons.set(ans, b); },
      /* A melted answer: it scores nothing, and each one takes a little off the right answer's pay. */
      melted() { o.melted += 1; },
      note(text2, x, y, nw, parent = zone) {
        const n = h('div', `left:${x}px;top:${y}px;width:${nw}px;`, ctx.brand.text(text2), parent);
        n.className = 'xq-note';
        return n;
      },
      float(x, y, text2, css) { ctx.float(x, y, text2, css); },
      /* Answer it: pays, shows which one the game wanted, and closes. */
      answer(ans, b, mult = 1) {
        if (!live()) return;
        o.done = true;
        spot.dock?.setReady(false);
        const right = tone(ans) !== 'no';
        const r = (b || card).getBoundingClientRect();
        const x = r.left + r.width / 2, y = r.top;
        const amount = quizPay(ctx.rates(), right) * mult * Math.max(0.4, 1 - o.melted * 0.2);
        const result = lessonAnswer(lesson, right);
        lesson = result.progress;
        mascot.answer(right, result.complete);
        ctx.store.set('lesson', lesson);
        const bonus = result.complete ? quizPay(ctx.rates(), false) * 3 : 0;
        /* The stage bonus (quiz-stages.js): every answer, right or wrong, in the same payment, popped on its own. */
        const stage = quizStage(ctx.has), extra = stageBonus(ctx.rates(), stage);
        const asked = amount + bonus + extra;
        const credited = ctx.pay(asked, 'quiz', { x, y });
        const stagePaid = asked > 0 ? credited * (extra / asked) : 0;
        if (stagePaid > 0 && !ctx.gentle && !ctx.reduced) ctx.float(x, y - 26, C.stagePop.replace('{n}', ctx.fmt(stagePaid)), 'color:var(--stamp);font-weight:700;');
        ctx.audio.play(right ? 'assets/audio/sfx/chime1.mp3' : 'assets/audio/sfx/ui_click.mp3', { bus: 'sfx', gain: 0.18 });
        for (const c of o.cancels) c();
        o.cancels.length = 0;
        for (const a of o.anims) { try { a.cancel(); } catch { /* gone */ } }
        o.anims.length = 0;
        card.classList.add('xq-done');
        text.textContent = `${pick(ctx, right ? C.right : C.wrong)} +${ctx.fmt(credited)} ${ctx.brand.currencyPlural}`;
        lessonLabel.textContent = result.complete ? 'Lesson complete. Bonus included.' : `Lesson ${lesson}/3`;
        if (stagePaid > 0) lessonLabel.textContent += ' ' + C.stageNote.replace('{s}', String(stage)).replace('{n}', ctx.fmt(stagePaid));
        text.classList.add('said');
        b?.classList.add(right ? 'ok' : 'bad');
        if (!right) o.buttons.get(t.yes)?.classList.add('ok');
        if (right && !ctx.gentle && !ctx.reduced) ctx.anim(card, [{ transform: 'none' }, { transform: 'scale(1.025)', offset: 0.35 }, { transform: 'none' }], { duration: 240, easing: 'ease-out' });
        ctx.after(result.complete ? 1400 : 1100, () => { if (open === o) close(); });
      },
      pop() { if (!ctx.paused) ctx.audio.play(POP, { bus: 'sfx', gain: 0.08 }); },
      deny() { if (!ctx.paused) ctx.audio.play('assets/audio/sfx/ui_denied.mp3', { bus: 'sfx', gain: 0.12 }); },
      nudge(el) { if (!ctx.gentle) t.anim(el, [{ translate: '-4px 0' }, { translate: '4px 0' }, { translate: '0 0' }], { duration: 180 }); },
    };

    const giveUpMs = (TRICK_FNS[trick]?.extraMs || 0) + GIVE_UP_MS;
    (TRICK_FNS[trick]?.draw || TRICK_FNS.none.draw)(t);
    align();

    t.anim(tb, [{ transform: 'scaleX(1)' }, { transform: 'scaleX(0)' }], { duration: giveUpMs, easing: 'linear', fill: 'forwards' });
    const calm = ctx.gentle || ctx.reduced;
    const entrance = calm ? [{ opacity: 0 }, { opacity: 1 }] : [
      { opacity: 0, transform: 'translateX(-4px) scale(.86,.94)' },
      { opacity: 1, transform: 'scale(1.012,1.008)', offset: .72 },
      { opacity: 1, transform: 'none' },
    ];
    t.anim(card, entrance, { duration: calm ? 260 : 215, easing: 'cubic-bezier(.2,.75,.25,1)' });
    ctx.audio.play('assets/audio/sfx/chip_pop.mp3', { bus: 'sfx', gain: 0.08 });
    t.after(giveUpMs, () => { ctx.toast(pick(ctx, C.giveUp)); close(); });
    return o;
  }

  /* The clock: nothing pops over a card or while one is up. Before the first stage a late one waits for the next round;
   * from stage 1 it tries again a few seconds later, so the faster pace holds (quiz-stages.js). */
  const nextGap = () => stageGap(quizStage(ctx.has), ctx.rng()) ?? EVERY_MS + ctx.rng() * EVERY_JITTER;
  let stop = null;
  const schedule = (ms) => {
    stop?.();
    dueAt = ctx.state.clock + ms;
    stop = ctx.after(ms, () => {
      if (!open && !cardUp()) ask();
      else if (quizStage(ctx.has)) { schedule(STAGE_RETRY_MS); return; }
      schedule(nextGap());
    });
  };
  /* A new stage takes effect at once: a wait longer than its gap is cut short. */
  const retime = () => { const g = nextGap(); if (dueAt - ctx.state.clock > g) schedule(g); };
  schedule(FIRST_MS);
  const drawIdle = () => {
    const s = Math.max(0, Math.ceil((dueAt - ctx.state.clock) / 1000));
    idle.hidden = !!open || !!exiting;
    idle.textContent = C.idleNext.replace('{n}', String(s));
    align();
  };
  drawIdle();
  spot.dock?.setReady(false);
  ctx.cleanup(() => spot.dock?.setReady(false));
  ctx.every(1000, drawIdle);

  return { ask, close, retime, get open() { return open; } };
}

let quiz = null;
/* A stage brings several trick rows at once and each wants to show itself: only the first in a moment asks. */
let lastShow = -1e9;

export const QUIZ_UNLOCKS = {
  quiz: {
    mount(ctx) {
      quiz = createQuiz(ctx);
      if (!ctx.store.get('asked')) { ctx.store.set('asked', 1); ctx.after(650, () => quiz?.ask('none')); }
      return { ask: quiz.ask, close: quiz.close, off() { quiz?.close(); quiz = null; } };
    },
  },
};

/* Every trick row: the first time it is owned, it shows itself within a second. */
for (const [trick, def] of Object.entries(TRICKS)) {
  if (def.unlock === 'quiz') continue;
  QUIZ_UNLOCKS[def.unlock] = {
    mount(ctx) {
      if (!ctx.store.get('shown')) {
        ctx.store.set('shown', 1);
        if (Math.abs(ctx.state.clock - lastShow) > 1500) { lastShow = ctx.state.clock; ctx.after(650, () => quiz?.ask(trick)); }
      }
      if (ctx.item?.qstage) quiz?.retime();
      return {};
    },
  };
}
