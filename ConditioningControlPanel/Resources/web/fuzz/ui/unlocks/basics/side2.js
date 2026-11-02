/* ui/unlocks/basics/side2.js - Side Toys part two: Flash Picture, Screen Blur, Real Sparkles, Falling Words, Voice Reactions,
 * Listening Test, Toy Link, Friend Remote. Words are recorded clips or nothing. App-only rows say so and do nothing here. */

import { APP_ONLY, DRIFT_LINES, FALLING_WORDS, LISTENING_TEST, OPINIONS, REMOTE } from '../../../data/content/index.js';
import { LISTEN_NAME, REMOTE_SOLO } from './copy.js';
import { burst, btnCenter, chip, docOf, fresh, play, pop, setPalette, sfx, spawn, squish } from './fx.js';

const pick = (ctx, list) => list[Math.floor(ctx.rng() * list.length)];
const worth = (ctx, seconds) => { const r = ctx.rates(); return Math.max(r.fps, r.click * 2, 2) * seconds; };

export default {
  /* Flash Picture: a soft fade, never a flash. It ignores the pointer, so play carries on underneath. */
  flash: {
    mount(ctx) {
      const show = async () => {
        const pic = await ctx.pictures.next({ niche: 'picture' });
        const f = spawn(ctx, `inset:0;display:grid;place-items:center;background:rgba(0,0,0,.35);`);
        if (pic) {
          const img = docOf(ctx).createElement('img');
          img.src = pic.url;
          img.alt = '';
          img.style.cssText = 'max-width:100%;max-height:100%;object-fit:contain;';
          f.appendChild(img);
          if (pic.credit) { const c = spawn(ctx, 'left:auto;top:auto;right:8px;bottom:8px;position:absolute;font-size:12px;color:#fff;', pic.credit, f); c.style.left = 'auto'; }
        } else {
          const box = spawn(ctx, 'position:static;width:min(60vw,320px);height:min(60vh,420px);display:grid;place-items:center;padding:12px;text-align:center;border:2px dashed #fff;color:#fff;font-style:italic;', pick(ctx, DRIFT_LINES), f);
          box.style.position = 'relative';
        }
        play(ctx, f, [{ opacity: 0 }, { opacity: 0.92, offset: 0.3 }, { opacity: 0.92, offset: 0.55 }, { opacity: 0 }], { duration: 2000, easing: 'ease-in-out' });
      };
      if (fresh(ctx)) ctx.after(400, show);
      ctx.every(28000, show);
      return {};
    },
  },

  /* Screen Blur: a veil that blurs what is behind it, so the page is never filtered. Input goes straight through it. */
  drain: {
    mount(ctx) {
      const blur = () => {
        const v = spawn(ctx, 'inset:0;-webkit-backdrop-filter:blur(6px);backdrop-filter:blur(6px);');
        play(ctx, v, [{ opacity: 0 }, { opacity: 1, offset: 0.45 }, { opacity: 1, offset: 0.6 }, { opacity: 0 }], { duration: 6000, easing: 'ease-in-out' });
        ctx.say(pick(ctx, DRIFT_LINES));
      };
      if (fresh(ctx)) blur();
      ctx.every(42000, blur);
      return {};
    },
  },

  /* Real Sparkles: on the web it is a sign and a star that goes nowhere, and says so. */
  sparkle: {
    mount(ctx) {
      const app = ctx.hosted && ctx.caps.sparkles;
      const c = chip(ctx, `✦ ${APP_ONLY.sparkle.tag}`, () => ctx.toast(app ? APP_ONLY.sparkle.app : APP_ONLY.sparkle.web));
      ctx.on('box', ({ x, y }) => {
        const r = c.getBoundingClientRect();
        const s = spawn(ctx, 'width:22px;height:22px;background:#19b5d6;clip-path:polygon(50% 0,62% 38%,100% 50%,62% 62%,50% 100%,38% 62%,0 50%,38% 38%);');
        play(ctx, s, [{ transform: `translate(${x}px,${y}px) scale(1)`, opacity: 1 }, { transform: `translate(${r.left + r.width / 2}px,${r.top}px) scale(.6) rotate(180deg)`, opacity: 0.9 }], { duration: ctx.gentle ? 400 : 800, easing: 'ease-in' });
        ctx.after(820, () => squish(ctx, c, 1.2));
      });
      if (fresh(ctx)) ctx.toast(APP_ONLY.sparkle.web);
      return {};
    },
  },

  /* Falling Words: three kinds. All catchable, none costly to miss. */
  triggers: {
    mount(ctx) {
      const COLS = { crit: '#f0c24b', mute: '#8fd4ff', speed: '#ff9ccf' };
      let fastUntil = 0, now = 0, n = 0;
      ctx.mod({ walls: (v, s, t) => (t < fastUntil ? v * 1.6 : v) });
      const fall = () => {
        const { w, h } = ctx.bounds();
        const word = FALLING_WORDS[n++ % FALLING_WORDS.length];
        const x = 10 + ctx.rng() * (w - 110);
        const b = ctx.el('button', `position:absolute;left:${x}px;top:0;min-width:88px;min-height:44px;padding:0 8px;border:0;border-radius:4px;background:${COLS[word.effect]};color:#17132c;font-weight:700;font-size:15px;pointer-events:auto;touch-action:manipulation;will-change:transform;`, word.word, 'over');
        b.type = 'button';
        const dur = ctx.gentle ? 9000 : 6000;
        const a = ctx.anim(b, [{ transform: 'translateY(-50px)' }, { transform: `translateY(${h + 10}px)` }], { duration: dur, easing: 'linear', fill: 'forwards' });
        a.onfinish = () => b.remove();
        b.addEventListener('pointerdown', (e) => {
          e.preventDefault();
          const r = b.getBoundingClientRect(), cx = r.left + r.width / 2, cy = r.top + r.height / 2;
          a.cancel();
          b.remove();
          burst(ctx, cx, cy, { color: COLS[word.effect], n: 14, reach: 70 });
          pop(ctx, cx, cy - 26, word.label, { color: 'var(--stamp)', size: 20, ms: 1000 });
          sfx(ctx, 'sfx/chime1.mp3', 0.5);
          if (word.effect === 'crit') { ctx.pay(ctx.rates().click * 10, 'trigger', { x: cx, y: cy }); ctx.emit('lucky', { x: cx, y: cy, auto: true }); }
          else if (word.effect === 'speed') fastUntil = now + 12000;
          else if (word.effect === 'mute') ctx.audio.muteFor(6000);
          ctx.say(word.line);
        });
      };
      ctx.after(fresh(ctx) ? 300 : 6000, fall);
      ctx.every(11000, fall);
      return { tick(dt, t) { now = t; } };
    },
  },

  /* Voice Reactions: a small round voice by the button, a caption bubble, a recorded clip or silence. */
  opinions: {
    mount(ctx) {
      const orb = ctx.el('div', 'position:absolute;left:10px;width:36px;height:36px;border-radius:50%;background:radial-gradient(circle at 35% 30%,#b9a4ff,#5a3fb5);pointer-events:none;', null, 'floaters');
      const place = () => { orb.style.top = `${btnCenter(ctx).y - 18}px`; };
      place();
      ctx.every(2000, place);
      let quietUntil = 0, now = 0;
      const speak = (bank) => {
        if (now < quietUntil) return;
        quietUntil = now + 5000;
        const line = pick(ctx, bank);
        const r = orb.getBoundingClientRect();
        const b = spawn(ctx, `left:${r.left + 14}px;top:${r.top - 44}px;padding:6px 12px;background:#fff;color:#2a1a5a;border:2px solid #5a3fb5;border-radius:12px;font-weight:700;font-size:16px;white-space:nowrap;`, line.caption);
        play(ctx, b, [{ opacity: 0 }, { opacity: 1, offset: 0.15 }, { opacity: 1, offset: 0.85 }, { opacity: 0 }], { duration: 1500 });
        if (line.clip) ctx.voice(line.clip, { gain: 0.8 });
        squish(ctx, orb, 1.4);
      };
      ctx.on('lucky', () => speak(OPINIONS.crit));
      ctx.on('buy', () => speak(OPINIONS.buy));
      ctx.on('cornered', () => speak(OPINIONS.corner));
      ctx.on('golden', () => speak(OPINIONS.golden));
      if (fresh(ctx)) ctx.after(300, () => speak(OPINIONS.buy));
      return { tick(dt, t) { now = t; } };
    },
  },

  /* Listening Test: a clip, then three words. Right pays twelve seconds. Skipping costs nothing. */
  listen: {
    mount(ctx) {
      let n = ctx.store.get('n') || 0, open = false;
      const ask = () => {
        if (open) return;
        open = true;
        const q = LISTENING_TEST.words[(n * 5) % LISTENING_TEST.words.length];
        n += 1;
        ctx.store.set('n', n);
        ctx.voice(q.clip, { gain: 1 });
        const { w } = ctx.bounds();
        const panel = ctx.el('div', `position:absolute;left:50%;bottom:12px;width:min(380px,${w - 24}px);transform:translateX(-50%);padding:8px;background:var(--panel);color:var(--ink);border:2px solid var(--stamp);border-radius:8px;box-shadow:0 3px 10px rgba(0,0,0,.3);pointer-events:auto;font-family:var(--font);`, null, 'over');
        const head = ctx.el('div', 'display:flex;align-items:center;gap:8px;font-weight:700;margin-bottom:6px;', null, panel);
        const face = ctx.el('div', 'position:relative;flex:none;width:34px;height:34px;border-radius:50%;background:#ff69b4;border:2px solid #d4488f;', null, head);
        for (const x of [8, 18]) ctx.el('i', `position:absolute;left:${x}px;top:10px;width:4px;height:7px;background:#222;border-radius:50%;`, null, face);
        ctx.el('span', '', `${LISTEN_NAME}: ${pick(ctx, LISTENING_TEST.ask)}`, head);
        if (!ctx.gentle) ctx.anim(panel, [{ translate: '0 140px' }, { translate: '0 0' }], { duration: 300, easing: 'ease-out' });
        const row = ctx.el('div', 'display:flex;gap:6px;', null, panel);
        const opts = [q.word, ...q.decoys];
        const start = Math.floor(ctx.rng() * 3);
        const order = [0, 1, 2].map((i) => opts[(i + start) % 3]);
        let stop = () => {};
        const finish = () => { open = false; stop(); panel.remove(); };
        for (const word of order) {
          const b = ctx.el('button', 'flex:1 1 0;min-height:44px;background:var(--btn);color:var(--btn-ink,inherit);border:1px solid var(--line);touch-action:manipulation;', word, row);
          b.type = 'button';
          b.addEventListener('click', () => {
            const r = b.getBoundingClientRect();
            if (word === q.word) {
              ctx.pay(worth(ctx, 12), 'listen', { x: r.left + r.width / 2, y: r.top });
              ctx.say(pick(ctx, LISTENING_TEST.right));
              sfx(ctx, 'sfx/chime3.mp3', 0.6);
            } else {
              ctx.say(pick(ctx, LISTENING_TEST.wrong).replace('{word}', q.word));
              sfx(ctx, 'sfx/ui_denied.mp3', 0.4);
            }
            ctx.emit('answer', { right: word === q.word });
            finish();
          });
        }
        stop = ctx.after(10000, () => { ctx.say(pick(ctx, LISTENING_TEST.skipped)); finish(); });
      };
      ctx.after(fresh(ctx) ? 600 : 30000, ask);
      ctx.every(75000, ask);
      return {};
    },
  },

  /* Toy Link: follows the earning rate up to a cap, in the app, on a toy the player connected there. On the web, a sign. */
  toy: {
    mount(ctx) {
      const app = ctx.hosted && ctx.caps.toys;
      const b = chip(ctx, `Toy: ${APP_ONLY.toy.tag}`, () => ctx.toast(app ? APP_ONLY.toy.app : APP_ONLY.toy.web));
      if (fresh(ctx)) { squish(ctx, b, 1.3); ctx.toast(app ? APP_ONLY.toy.app : APP_ONLY.toy.web); }
      if (app) {
        let taps = ctx.state.taps;
        ctx.every(1500, () => {
          const rate = (ctx.state.taps - taps) / 1.5;
          taps = ctx.state.taps;
          const level = Math.min(0.6, rate / 10);
          if (level > 0.05) ctx.host('haptic', { level, ms: 1200, tag: 'toy' });
        });
      }
      return {};
    },
  },

  /* Friend Remote: preset buttons and no words, pressed by the player on the player's own run. No friend wire exists, and
   * the card says so; nothing here pretends one is connected. */
  remote: {
    mount(ctx) {
      const doc = docOf(ctx);
      const act = (p) => {
        const el = ctx.button.el;
        if (p.key === 'poke') squish(ctx, el, 1.5);
        else if (p.key === 'spin') { if (!ctx.gentle) ctx.anim(el, [{ rotate: '0deg' }, { rotate: '360deg' }], { duration: 700 }); }
        else if (p.key === 'pink') { setPalette(ctx, 'pink'); ctx.emit('palette', { name: 'pink' }); }
        else if (p.key === 'tap') ctx.button.press();
        else if (p.key === 'hush') ctx.audio.muteFor(3000);
        ctx.toast(REMOTE_SOLO.toasts[p.key]);
      };
      const open = () => {
        const node = doc.createElement('div');
        node.style.cssText = 'display:flex;flex-wrap:wrap;gap:6px;';
        for (const p of REMOTE.presets) {
          const b = doc.createElement('button');
          b.type = 'button';
          b.textContent = p.label;
          b.addEventListener('click', () => act(p));
          node.appendChild(b);
        }
        ctx.modal({ title: REMOTE_SOLO.title, body: REMOTE_SOLO.note, node });
      };
      const c = chip(ctx, REMOTE_SOLO.chip, open);
      if (fresh(ctx)) { squish(ctx, c, 1.3); open(); }
      return {};
    },
  },
};
