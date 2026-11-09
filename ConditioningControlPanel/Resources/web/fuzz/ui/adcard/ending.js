/* ui/adcard/ending.js - the Living Ad ending (o_out): the run prints itself as a shareable ad card. The take before the
 * fade (ui/adcard/stage.js livingAdTake) prints the page into the printer slot; here the poster comes back out of that
 * slot onto the paper stage, the title stamps down, the card builds in with its sounds (card.js onBeat), and stars and
 * stickers settle in around it. The player can replay it, save it as a GIF, a video or a picture, copy it, then continue
 * to the credits. Gentle visuals and reduced motion get fades only and the finished card, still. */

import { cardModel } from './model.js';
import { createAdCard, loadCardCss } from './card.js';
import { exportCard } from './export.js';
import { createStage, loadStageCss, stageLook, cue, beatCue } from './stage.js';
import { ADCARD_COPY } from '../../data/content/adcard.js';

const POP_IN = [{ opacity: 0, transform: 'translateY(14px) scale(.9)' }, { opacity: 1, transform: 'translateY(-3px) scale(1.03)', offset: 0.7 }, { transform: 'none' }];
const STAMP_IN = [{ opacity: 0, transform: 'scale(2.2)' }, { opacity: 1, transform: 'scale(.94)', offset: 0.62 }, { transform: 'none' }];

export function livingAdEnding(ctx) {
  return new Promise((done) => {
    const still = !!(ctx.reduced || ctx.gentle);
    const look = stageLook(ctx.state);
    const stage = createStage(ctx, ctx.world?.ownerDocument || document, look, { calm: still, slotUp: true });
    const sheet = ctx.el('div', '', null, 'ending');
    sheet.className = 'adst-sheet';
    stage.slot.parentNode.insertBefore(sheet, stage.slot);   // the slot stays above what rises out of it
    const doc = sheet.ownerDocument, win = doc.defaultView, B = ADCARD_COPY.buttons;
    const make = (tag, cls, text) => { const e = doc.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; };
    const wrap = make('div', 'adc-sheet');
    for (const [k, v] of Object.entries({ A: look.A, ink: look.ink })) wrap.style.setProperty('--st-' + k, v);
    const h = make('h2', 'adc-title');
    h.append(make('span', 'adst-ink', ADCARD_COPY.title));
    const p = make('p', 'adc-lede', ADCARD_COPY.lede);
    const mat = make('div', 'adc-mat');
    const box = make('div', 'adc-box');
    mat.append(box);
    const row = make('div', 'adc-row');
    const status = make('div', 'adc-status'); status.setAttribute('role', 'status');
    const prog = make('div', 'adc-prog'); prog.hidden = true;
    const bar = doc.createElement('i'); prog.append(bar);
    wrap.append(h, p, mat, row, prog, status);
    wrap.style.visibility = 'hidden';   // until the fonts and both sheets are in
    sheet.append(wrap);

    let ad = null, busy = false, gone = false;
    const buttons = [];
    const button = (label, fn, cls = '') => {
      const b = make('button', cls, label); b.type = 'button';
      b.addEventListener('click', fn); row.append(b); buttons.push(b); return b;
    };
    const lock = (on) => { busy = on; for (const b of buttons) if (!b.classList.contains('go')) b.disabled = on; };
    const finish = () => { if (gone) return; gone = true; done(); };

    async function run(kind) {
      if (busy || !ad) return;
      lock(true);
      status.textContent = '';
      prog.hidden = kind === 'png' || kind === 'copy'; bar.style.width = '0%';
      try {
        const out = await exportCard(ad, kind, {
          stop: () => gone,
          progress: (i, n, text) => { bar.style.width = (i / n * 100) + '%'; status.textContent = text; },
        });
        if (gone) return;
        status.textContent = out.status === 'copied' ? ADCARD_COPY.copied : out.status === 'nocopy' ? ADCARD_COPY.noCopy : out.status === 'cancelled' ? '' : ADCARD_COPY.saved;
      } catch (e) {
        if (!gone) { console.error(e); status.textContent = ADCARD_COPY.failed; }
      } finally {
        prog.hidden = true;
        if (!gone) lock(false);
      }
    }

    button(B.gif, () => run('gif'));
    button(B.video, () => run('video'));
    button(B.png, () => run('png'));
    button(B.copy, () => run('copy'));
    button(B.replay, () => { if (!busy) ad?.play(); });
    const go = button(B.next, finish, 'go');
    lock(true);

    ctx.cleanup(() => { gone = true; ad?.destroy(); });
    Promise.all([loadCardCss(doc), loadStageCss(doc)]).then(() => {
      if (gone) return;
      const model = cardModel({ run: ctx.state, life: ctx.life, name: ctx.identity?.displayName || '' });
      ad = createAdCard({ doc, box, model, reduced: still, onBeat: (n, i) => { const c = beatCue(n, i); if (c) cue(ctx, c); } });
      ad.build(true);   // paused at 0: the card waits, unseen, while its paper comes out of the slot
      wrap.style.visibility = '';
      const at = mat.getBoundingClientRect(), around = wrap.getBoundingClientRect();
      if (still) calmIntro(at, around); else intro(at, around);
    }).catch((e) => { console.error(e); wrap.style.visibility = ''; status.textContent = ADCARD_COPY.failed; lock(false); });

    const ready = () => { lock(false); go.focus({ preventScroll: true }); };

    function calmIntro(at, around) {
      ctx.anim(wrap, [{ opacity: 0 }, { opacity: 1 }], { duration: 700, fill: 'backwards' });
      ad.play();
      ctx.after(250, () => cue(ctx, 'still'));
      stage.slotAway(150);
      stage.decorate(around, 300);
      sheet.style.overflow = 'hidden auto';
      ready();
    }

    function intro(at, around) {
      /* 0.08 s the title stamps down, 0.38 s the lede strip, 0.42 s the printer works and the poster rises out of the
       * slot, 1.12 s it lands and the card builds in on it, then the slot drops away, the buttons pop up in turn and the
       * stars and stickers settle in around the poster. */
      ctx.anim(h, STAMP_IN, { duration: 340, delay: 80, easing: 'ease-in', fill: 'backwards' });
      ctx.after(290, () => cue(ctx, 'title'));
      ctx.anim(p, [{ opacity: 0, transform: 'translateY(-10px) rotate(-3deg)' }, { opacity: 1, transform: 'none' }], { duration: 360, delay: 380, easing: 'cubic-bezier(.2,.8,.2,1)', fill: 'backwards' });
      [420, 545].forEach((t) => ctx.after(t, () => cue(ctx, 'printer')));
      const H = win.innerHeight, slotTop = H - (stage.slot.offsetHeight || 52);
      const dy = Math.max(0, slotTop + 6 - at.top);
      ctx.anim(mat, [{ transform: `translateY(${dy.toFixed(0)}px)` }, { transform: 'translateY(-10px)', offset: 0.82 }, { transform: 'none' }], { duration: 700, delay: 420, easing: 'cubic-bezier(.3,.7,.3,1)', fill: 'backwards' });
      ctx.after(1120, () => {
        if (gone) return;
        cue(ctx, 'land');
        ad.play();
        stage.puff(at, 0);
        sheet.style.overflow = 'hidden auto';
      });
      stage.slotAway(1350);
      buttons.forEach((b, i) => ctx.anim(b, POP_IN, { duration: 300, delay: 1500 + i * 70, easing: 'ease-out', fill: 'backwards' }));
      ctx.anim(status, [{ opacity: 0 }, { opacity: 1 }], { duration: 300, delay: 1900, fill: 'backwards' });
      stage.decorate(around, 1300);
      ctx.after(1500, () => { if (!gone) ready(); });
    }
  });
}

/** The ending holds the screen this long at most (the engine's usual cap is 90 s; saving a GIF alone takes ~20 s). */
export const LIVING_AD_SHOW_MAX_MS = 30 * 60 * 1000;
