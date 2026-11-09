/* ui/adcard/ending.js - the Living Ad ending (o_out): the run prints itself as a shareable ad card. The player can
 * replay it, save it as a GIF, a video or a picture, copy it, then continue to the credits. Gentle visuals and reduced
 * motion get the finished card, still. */

import { cardModel } from './model.js';
import { createAdCard, loadCardCss } from './card.js';
import { exportCard } from './export.js';
import { ADCARD_COPY } from '../../data/content/adcard.js';

export function livingAdEnding(ctx) {
  return new Promise((done) => {
    const sheet = ctx.el('div', 'position:absolute;inset:0;overflow:auto;display:grid;place-items:center;padding:18px 12px;box-sizing:border-box;'
      + 'background:#1d0b18;color:#fff;font-family:"Segoe UI",system-ui,sans-serif;pointer-events:auto;', null, 'ending');
    const doc = sheet.ownerDocument, B = ADCARD_COPY.buttons;
    const wrap = doc.createElement('div'); wrap.className = 'adc-sheet';
    const h = doc.createElement('h2'); h.textContent = ADCARD_COPY.title;
    const p = doc.createElement('p'); p.textContent = ADCARD_COPY.lede;
    const box = doc.createElement('div'); box.className = 'adc-box';
    const row = doc.createElement('div'); row.className = 'adc-row';
    const status = doc.createElement('div'); status.className = 'adc-status'; status.setAttribute('role', 'status');
    const prog = doc.createElement('div'); prog.className = 'adc-prog'; prog.hidden = true;
    const bar = doc.createElement('i'); prog.append(bar);
    wrap.append(h, p, box, row, prog, status);
    sheet.append(wrap);

    let ad = null, busy = false, gone = false;
    const still = ctx.reduced || ctx.gentle;
    const buttons = [];
    const button = (label, fn, cls = '') => {
      const b = doc.createElement('button'); b.type = 'button'; b.textContent = label; if (cls) b.className = cls;
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
    button(B.next, finish, 'go');
    lock(true);

    ctx.cleanup(() => { gone = true; ad?.destroy(); });
    loadCardCss(doc).then(() => {
      if (gone) return;
      const model = cardModel({ run: ctx.state, life: ctx.life, name: ctx.identity?.displayName || '' });
      ad = createAdCard({ doc, box, model, reduced: still });
      ad.play();
      lock(false);
      buttons[buttons.length - 1].focus({ preventScroll: true });
    }).catch((e) => { console.error(e); status.textContent = ADCARD_COPY.failed; lock(false); });
  });
}

/** The ending holds the screen this long at most (the engine's usual cap is 90 s; saving a GIF alone takes ~20 s). */
export const LIVING_AD_SHOW_MAX_MS = 30 * 60 * 1000;
