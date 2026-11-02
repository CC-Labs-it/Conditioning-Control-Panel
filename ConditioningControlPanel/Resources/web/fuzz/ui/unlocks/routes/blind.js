/* No Pictures: nothing is shown, and the words do it instead. Frames stay empty on purpose, and the small tasks here are
 * answering things, which all pay, because the answer was never the point. */

import { IMAGINE, CAPTIONS, STORIES, RADIO_LINES, FILL_BLANKS, INK_ANSWERS, INK_REPLIES, FALSE_MEMORIES } from '../../../data/content/index.js';
import { panel, deck, flash, tone, ring, btnCentre, fill, docOf, railsRoom, meterRow } from './kit.js';
import { ROUTE_COPY } from './copy.js';
import { WOOD } from './slides.js';

const hall = { frame: null };
const PAPER = '#fbf7ea';
const SERIF = 'Georgia,"Times New Roman",serif';

/** Text sized to whatever box the phone left it. */
const fits = (box, chars, max = 21) => Math.max(11, Math.min(max, Math.sqrt(((box.w - 24) * (box.h - 40)) / Math.max(10, chars)) * 0.72));

export default {
  /* Empty Frame: a real frame, with a dashed nothing in it, asking you to do the work. */
  b_frame: {
    mount(ctx) {
      tone(ctx, { '--page': '#ece8dd', '--panel': '#f6f3ea', '--panel-short': '#ddd8ca', '--line': '#a79f8c', '--fill': '#cfc7b2', '--btn': '#e4dfd0' });
      const size = { w: 200, h: 230 };
      const el = panel(ctx, { side: 'left', height: 230, css: `${WOOD}background:#fbf8ef;`, place(s) { size.w = s.w; size.h = s.h; } });
      const hole = ctx.el('div', 'position:absolute;inset:6px;border:2px dashed #b5ad98;', null, el);
      void hole;
      hall.frame = { el, size };
      const next = deck(IMAGINE, ctx);
      const show = () => {
        const t = next();
        hall.say(t);
      };
      hall.say = (t, ms = 2600) => flash(ctx, el, t, `left:9px;right:9px;top:${size.h < 170 ? '8px' : '38%'};text-align:center;color:#6b5f49;font:italic ${fits(size, t.length)}px/1.15 ${SERIF};`, ms);
      ctx.after(400, show);
      const stop = ctx.every(5200, () => { if (!hall.busy) show(); });
      if (!ctx.gentle) ctx.anim(el, [{ transform: 'translateY(-16px) scale(.9)', opacity: 0 }, { transform: 'none', opacity: 1 }], { duration: 350, easing: 'cubic-bezier(.2,1.4,.5,1)' });
      return { off() { stop(); hall.frame = null; } };
    },
  },

  /* Captions: a dry label under the nothing, like in a gallery. */
  b_caption: {
    mount(ctx) {
      const next = deck(CAPTIONS, ctx);
      const f = hall.frame;
      const plate = ctx.el('div', `position:absolute;left:6px;right:6px;bottom:6px;max-height:48%;overflow:hidden;min-height:22px;padding:2px 4px;background:rgba(251,247,234,.92);border-top:1px solid #b5ad98;color:#3a3226;text-align:center;font:italic 14px/1.2 ${SERIF};`, '', f?.el);
      const show = () => {
        plate.textContent = next();
        plate.style.fontSize = `${f && f.size.h < 130 ? 11 : 14}px`;
        ctx.anim(plate, [{ opacity: 0 }, { opacity: 1 }], { duration: 300 });
      };
      show();
      ctx.every(4600, show);
      return {};
    },
  },

  /* Short Story: three slow lines on paper. A new one each minute. */
  b_story: {
    mount(ctx) {
      const next = deck(STORIES, ctx), size = { w: 200, h: 200 };
      const el = panel(ctx, { side: 'right', height: 210, css: `background:${PAPER};border:1px solid #bbb2a0;box-shadow:0 1px 3px rgba(0,0,0,.25);overflow:hidden;box-sizing:border-box;padding:6px;`, place(s) { size.w = s.w; size.h = s.h; } });
      const lines = [0, 1, 2].map(() => ctx.el('div', `opacity:0;color:#3a3226;text-align:center;margin:3px 0;transition:opacity 1s;font:italic 16px/1.2 ${SERIF};`, '', el));
      const tell = () => {
        const s = next();
        lines.forEach((l, i) => { l.style.opacity = '0'; ctx.after(600, () => { l.textContent = s.lines[i]; }); ctx.after(800 + i * 1300, () => { l.style.opacity = '1'; }); });
        // Three lines must fit whatever box the phone left: measured once a story, never per frame.
        ctx.after(620, () => { let px = 20; do { lines.forEach((l) => { l.style.fontSize = `${px}px`; }); px -= 1; } while (px >= 10 && el.scrollHeight > el.clientHeight); });
      };
      tell();
      ctx.every(60000, tell);
      return {};
    },
  },

  /* Distant Radio: a dial hunting between stations, and a far-off voice in the gaps. The voice is silent until recorded; its words still arrive. */
  b_radio: {
    mount(ctx) {
      const next = deck(RADIO_LINES, ctx);
      ctx.audio.loop('assets/audio/ambient/radio.mp3', { bus: 'ambient', gain: 0.5 });
      const wide = railsRoom(ctx);
      const host = wide ? panel(ctx, { side: 'left', height: 130, css: 'background:#6b4a2b;border-radius:8px;box-sizing:border-box;padding:8px;' }) : ctx.el('div', 'width:120px;', null, meterRow(ctx));
      const dial = ctx.el('div', 'position:relative;height:18px;background:#f3e6c0;border:1px solid #3a2815;box-sizing:border-box;overflow:hidden;', null, host);
      const needle = ctx.el('div', 'position:absolute;top:0;left:2px;width:2px;height:100%;background:#b3121d;', null, dial);
      if (!ctx.gentle) ctx.anim(needle, [{ left: '2%' }, { left: '96%' }], { duration: 3200, iterations: Infinity, direction: 'alternate', easing: 'ease-in-out' });
      if (wide) {
        const text = ctx.el('div', `margin-top:8px;color:#f3e6c0;font:italic 14px/1.25 ${SERIF};min-height:60px;`, '', host);
        const say = () => { const line = next(); text.textContent = line; ctx.anim(text, [{ opacity: 0 }, { opacity: 1 }, { opacity: 1, offset: 0.85 }, { opacity: 0.2 }], { duration: 3100 }); };
        say();
        ctx.every(3200, say);
      } else {
        ctx.say(next());
        ctx.every(9000, () => ctx.say(next()));
      }
      return {};
    },
  },

  /* Fill the Blank: pick a word. Later it gets quoted back at you, in writing. */
  b_fill: {
    mount(ctx) {
      const pick = deck(FILL_BLANKS, ctx);
      const said = ctx.store.get('said') || [];
      const ask = () => {
        const b = pick();
        ctx.modal({
          title: b.text,
          body: ROUTE_COPY.fillNote,
          buttons: b.options.map((word) => ({
            label: word,
            onPress() {
              said.push(fill(b.echo, { word }));
              if (said.length > 8) said.shift();
              ctx.store.set('said', said);
              const c = btnCentre(ctx);
              ctx.pay(ctx.rates().fps * 4, 'blank', { x: c.x, y: c.y });
              ctx.after(40000 + ctx.rng() * 30000, echo);
            },
          })),
        });
      };
      const echo = () => {
        const line = said.shift();
        if (!line) return;
        ctx.store.set('said', said);
        if (hall.say) { hall.busy = true; hall.say(line, 4200); ctx.after(4300, () => { hall.busy = false; }); }
        ctx.toast(line);
      };
      ctx.after(900, ask);
      ctx.every(180000, ask);
      if (said.length) ctx.after(20000, echo);
      return {};
    },
  },

  /* Inkblot: say what you see. All four pay. The reply is never about the answer. */
  b_ink: {
    mount(ctx) {
      const answers = deck(INK_ANSWERS, ctx), replies = deck(INK_REPLIES, ctx);
      const doc = docOf(ctx);
      const blot = () => {
        const c = doc.createElement('canvas');
        c.width = 240; c.height = 180;
        c.style.cssText = 'display:block;margin:0 auto 8px;background:#f7f3e8;max-width:100%;';
        const g = c.getContext('2d');
        g.fillStyle = '#17132c';
        for (let i = 0; i < 60; i++) {
          const d = ctx.rng() ** 1.5 * 104, y = 14 + ctx.rng() * 152, rx = 2 + ctx.rng() * (26 - d / 5), ry = 2 + ctx.rng() * 16, rot = ctx.rng() * 3.14;
          for (const [x, a] of [[120 + d, rot], [120 - d, -rot]]) { g.beginPath(); g.ellipse(x, y, Math.max(1.5, rx), ry, a, 0, 6.283); g.fill(); }
        }
        return c;
      };
      const ask = () => {
        ctx.modal({
          title: ROUTE_COPY.inkTitle,
          node: blot(),
          buttons: answers().map((label) => ({
            label,
            onPress() {
              const b = btnCentre(ctx);
              ctx.pay(ctx.rates().fps * 8, 'ink', { x: b.x, y: b.y });
              ring(ctx, b.x, b.y, 180, '#17132c', 600);
              ctx.toast(replies());
            },
          })),
        });
      };
      ctx.after(800, ask);
      ctx.every(150000, ask);
      return {};
    },
  },

  /* False Memory: the frame starts remembering a picture you never saw, and it is sure about it. */
  b_memory: {
    mount(ctx) {
      const next = deck(FALSE_MEMORIES, ctx);
      const tell = () => {
        if (!hall.say) return;
        const [a, b] = next();
        hall.busy = true;
        hall.say(a, 2200);
        ctx.after(2300, () => hall.say(b, 2800));
        ctx.after(5200, () => { hall.busy = false; });
      };
      ctx.after(600, tell);
      ctx.every(18000, tell);
      return {};
    },
  },
};
