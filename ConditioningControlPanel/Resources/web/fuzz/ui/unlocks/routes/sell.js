/* More Ads: you sold the page, and it pays. Ads pile up everywhere, the money follows them, and the jokes get closer to the
 * real thing until the last row turns the whole screen into one. Only a real card ever links out, on the player's own press. */

import { SPONSORED_THOUGHTS, NATIVE_LINES, EMI_AD_READS, ENDINGS } from '../../../data/content/index.js';
import { nextAd, pressAd, panel, deck, railsRoom, burst, ring, claim, release, fill } from './kit.js';
import { ROUTE_COPY } from './copy.js';

const FONT = 'Arial,Helvetica,sans-serif';
const CHIP = `font:700 10px ${FONT};background:#f0c24b;color:#000;padding:0 4px;border-radius:2px;flex:none;`;
const NOPE = ROUTE_COPY.adNope, AD = ROUTE_COPY.adChip;

/** One ad drawn small. Pressing a joke does nothing but a line; pressing a real card opens it, once paid. */
export function adCard(ctx, parent, rec, css = '') {
  const real = !!rec.ccp;
  const card = ctx.el('div', `display:flex;align-items:center;gap:8px;min-height:44px;padding:4px 8px;box-sizing:border-box;background:${real ? '#2a0f22' : '#fffbe4'};color:${real ? '#fff' : '#111'};border:1px solid ${real ? '#ff69b4' : '#b9a23e'};font:13px/1.15 ${FONT};pointer-events:auto;${css}`, null, parent);
  ctx.el('span', CHIP, AD, card);
  const text = ctx.el('div', 'flex:1 1 auto;min-width:0;overflow:hidden;', null, card);
  ctx.el('b', 'display:block;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;', rec.headline, text);
  ctx.el('small', 'display:block;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;opacity:.8;font-size:11px;', rec.sub, text);
  const cta = ctx.el('button', `flex:none;min-height:36px;padding:2px 8px;font:700 12px ${FONT};background:${real ? '#ff69b4' : '#e8d98a'};color:${real ? '#2a0f22' : '#111'};border:1px solid #0006;touch-action:manipulation;`, rec.cta, card);
  cta.type = 'button';
  cta.addEventListener('click', () => {
    const r = cta.getBoundingClientRect(), at = { x: r.left, y: r.top };
    if (pressAd(ctx, rec, at)) return;
    ctx.float(at.x, at.y - 6, NOPE[Math.floor(ctx.rng() * NOPE.length)], `font:12px ${FONT};`);
  });
  return card;
}

/** The ad panel that sits low on the page, shared by a few rows. Never takes a press outside its own button. */
function lowPanel(ctx, height) {
  const { w, h } = ctx.bounds(), pw = Math.min(300, w - 24);
  return ctx.el('div', `position:absolute;left:${(w - pw) / 2}px;top:${h - height - 8}px;width:${pw}px;height:${height}px;background:#111;color:#fff;border:2px solid #000;box-shadow:0 4px 14px rgba(0,0,0,.5);overflow:hidden;font:13px/1.2 ${FONT};`, null, 'over');
}

/* Ending shows, shared with the Fewer Ads lane. The credits card comes after. */
export function endingCard(ctx, { build, ms = 16000 }) {
  return new Promise((done) => {
    const sheet = ctx.el('div', 'position:absolute;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:10px;padding:24px;text-align:center;overflow:hidden;', null, 'ending');
    const finish = () => done();
    build(sheet, finish);
    ctx.after(ms, finish);
  });
}
export function endButton(ctx, parent, label, css, onPress) {
  const b = ctx.el('button', `min-height:48px;padding:6px 20px;border:0;border-radius:8px;font:700 16px ${FONT};touch-action:manipulation;pointer-events:auto;${css}`, label, parent);
  b.type = 'button';
  b.addEventListener('click', onPress);
  return b;
}

export default {
  /* Second Banner: one more strip of ads, turning over, and each new ad is a little more pay. */
  o_second: {
    mount(ctx) {
      const host = ctx.el('div', 'width:100%;margin-bottom:4px;', null, 'banner');
      let card = null;
      const turn = () => {
        const rec = nextAd(ctx, 'banner');
        card?.remove();
        card = adCard(ctx, host, rec);
        ctx.anim(card, ctx.gentle ? [{ opacity: 0 }, { opacity: 1 }] : [{ transform: 'translateX(40px)', opacity: 0 }, { transform: 'none', opacity: 1 }], { duration: 300 });
        const r = host.getBoundingClientRect();
        ctx.pay(ctx.rates().fps * 0.5, 'banner2', { x: r.left + r.width / 2, y: r.top + 10 });
      };
      turn();
      ctx.every(11000, turn);
      ctx.on('ads', turn);
      return {};
    },
  },

  /* Video Ads: three small muted clips. One of the three is real, and it moves around. */
  o_video: {
    mount(ctx) {
      const wide = railsRoom(ctx);
      const host = wide
        ? panel(ctx, { side: 'right', height: 300, css: 'display:flex;flex-direction:column;gap:4px;pointer-events:auto;' })
        : ctx.el('div', 'display:flex;gap:4px;width:100%;margin-bottom:4px;', null, 'banner');
      const TINTS = ['#7a5a2f', '#2f6f8f', '#6a3d7a'];
      const tiles = [0, 1, 2].map((i) => {
        const t = ctx.el('button', `position:relative;flex:1 1 0;min-width:0;min-height:62px;padding:0;overflow:hidden;background:${TINTS[i]};border:1px solid #000;color:#fff;font:700 12px/1.1 ${FONT};pointer-events:auto;touch-action:manipulation;`, null, host);
        t.type = 'button';
        ctx.el('span', `position:absolute;left:3px;top:3px;${CHIP}`, AD, t);
        const name = ctx.el('span', 'position:absolute;left:4px;right:4px;bottom:9px;text-align:center;text-shadow:0 1px 2px #000;', '', t);
        ctx.el('span', 'position:absolute;left:50%;top:26%;margin-left:-8px;width:0;height:0;border-left:16px solid rgba(255,255,255,.75);border-top:10px solid transparent;border-bottom:10px solid transparent;', null, t);
        const prog = ctx.el('span', 'position:absolute;left:0;right:0;bottom:0;height:4px;background:#ff69b4;transform-origin:0 50%;', null, t);
        ctx.anim(prog, [{ transform: 'scaleX(0)' }, { transform: 'scaleX(1)' }], { duration: 2400 + i * 700, iterations: Infinity, easing: 'linear' });
        const rec = { now: null };
        t.addEventListener('click', () => {
          const r = t.getBoundingClientRect();
          if (!pressAd(ctx, rec.now, { x: r.left, y: r.top })) ctx.float(r.left + 10, r.top, NOPE[0], `font:12px ${FONT};`);
        });
        return { t, name, rec };
      });
      let real = 0;
      const turn = () => {
        real = (real + 1) % 3;
        tiles.forEach((x, i) => {
          let rec = nextAd(ctx, 'banner');
          for (let n = 0; n < 40 && !!rec.ccp !== (i === real); n++) rec = nextAd(ctx, 'banner');
          x.rec.now = rec;
          x.name.textContent = ctx.brand.text(rec.cta ? (rec.ccp ? ROUTE_COPY.realBrand : rec.headline) : '');
          x.t.style.outline = rec.ccp ? '2px solid #ff69b4' : '';
        });
      };
      turn();
      ctx.every(6000, turn);
      ctx.on('ads', turn);
      return {};
    },
  },

  /* Pop-up Ads: a loud little window. The X runs away once, and closing it pays two seconds. */
  o_popup: {
    mount(ctx) {
      const open = new Set();
      const pop = () => {
        if (open.size >= 2) return;
        const rec = nextAd(ctx, 'popup'), { w, h } = ctx.bounds(), pw = Math.min(270, w - 30), ph = 170;
        const clear = (ctx.parts.shop.querySelector('.row')?.getBoundingClientRect().bottom ?? 260) + 6;
        const x = 10 + ctx.rng() * (w - pw - 20), y = clear + ctx.rng() * Math.max(0, h - ph - 140 - clear);
        const win = ctx.el('div', `position:absolute;left:${x}px;top:${y}px;width:${pw}px;height:${ph}px;background:${rec.ccp ? '#2a0f22' : '#fffbe4'};color:${rec.ccp ? '#fff' : '#111'};border:3px solid #000;border-radius:10px;box-shadow:5px 5px 0 rgba(0,0,0,.45);font:14px/1.25 ${FONT};pointer-events:auto;overflow:hidden;transform:rotate(${(ctx.rng() * 4 - 2).toFixed(1)}deg);`, null, 'over');
        open.add(win);
        ctx.el('div', 'height:32px;background:repeating-linear-gradient(135deg,#ff69b4 0 12px,#ffd24d 12px 24px);border-bottom:3px solid #000;', null, win);
        ctx.el('div', `position:absolute;left:6px;top:8px;${CHIP}`, AD, win);
        const body = ctx.el('div', 'padding:6px 10px;', null, win);
        ctx.el('b', 'display:block;font-size:16px;', rec.headline, body);
        ctx.el('div', 'font-size:12px;opacity:.85;margin-top:2px;max-height:45px;overflow:hidden;', rec.sub, body);
        const cta = ctx.el('button', `position:absolute;right:8px;bottom:8px;min-height:30px;padding:2px 10px;font:700 13px ${FONT};background:#ff69b4;color:#111;border:2px solid #000;border-radius:6px;touch-action:manipulation;`, rec.cta, win);
        cta.type = 'button';
        const shut = (paid, at) => {
          if (!open.delete(win)) return;
          if (paid) { const r = ctx.rates(); ctx.pay(Math.max(r.fps, r.click) * 2, 'popup', at); }
          ctx.anim(win, ctx.gentle ? [{ opacity: 1 }, { opacity: 0 }] : [{ transform: 'scale(1)', opacity: 1 }, { transform: 'scale(.5)', opacity: 0 }], { duration: 160, fill: 'both' });
          ctx.after(170, () => win.remove());
        };
        cta.addEventListener('click', () => {
          const r = cta.getBoundingClientRect();
          if (pressAd(ctx, rec, { x: r.left, y: r.top })) shut(false);
          else ctx.float(r.left, r.top - 8, NOPE[2], `font:12px ${FONT};color:#111;`);
        });
        const X = ctx.el('button', `position:absolute;right:2px;top:1px;width:44px;height:30px;padding:0;font:800 16px ${FONT};background:#fff;color:#000;border:2px solid #000;border-radius:6px;transition:left .15s,top .15s,right .15s;touch-action:manipulation;`, 'X', win);
        X.type = 'button';
        let moved = false;
        const dodge = () => { moved = true; X.style.right = 'auto'; X.style.left = '8px'; X.style.top = `${ph - 42}px`; };
        X.addEventListener('pointerenter', (e) => { if (!moved && e.pointerType === 'mouse') dodge(); });
        X.addEventListener('pointerdown', (e) => {
          e.preventDefault();
          if (!moved) { dodge(); return; }
          shut(true, { x: e.clientX, y: e.clientY });
        });
        if (!ctx.gentle) ctx.anim(win, [{ transform: 'scale(.6)' }, { transform: 'scale(1)' }], { duration: 180, easing: 'ease-out', composite: 'add' });
        ctx.audio.play('assets/audio/sfx/ui_denied.mp3', { bus: 'sfx', gain: 0.35 });
        ctx.after(30000, () => shut(false));
      };
      ctx.after(800, pop);
      ctx.every(35000, pop);
      return {};
    },
  },

  /* Sponsored Thoughts: ad reads slip in among the thoughts on the ticker. */
  o_sponsor: {
    mount(ctx) {
      const next = deck(SPONSORED_THOUGHTS, ctx);
      const read = () => ctx.say(`[${AD}] ${next()}`);
      read();
      ctx.every(18000, read);
      return {};
    },
  },

  /* Skip Countdown: waiting it out pays well, skipping pays almost nothing. You can skip. You will think about it. */
  o_skip: {
    mount(ctx) {
      let live = null;
      const play = () => {
        if (live || !claim(ctx, 'skip', 8000, 'low')) return;
        const rec = nextAd(ctx, 'banner');
        const p = lowPanel(ctx, 118);
        live = p;
        ctx.el('span', `position:absolute;left:6px;top:6px;${CHIP}`, AD, p);
        ctx.el('b', 'position:absolute;left:8px;right:8px;top:26px;font-size:16px;', rec.headline, p);
        const sub = ctx.el('div', 'position:absolute;left:8px;right:8px;top:48px;font-size:12px;opacity:.8;height:30px;overflow:hidden;', rec.sub, p);
        void sub;
        let left = 5;
        const skip = ctx.el('button', `position:absolute;right:6px;bottom:6px;min-height:36px;padding:2px 12px;background:rgba(255,255,255,.14);color:#fff;border:1px solid #fff;font:700 13px ${FONT};pointer-events:auto;touch-action:manipulation;`, fill(ROUTE_COPY.skipIn, { n: left }), p);
        skip.type = 'button';
        let stop = null;
        const close = (pay, big, at) => {
          stop?.();
          if (live !== p) return;
          live = null;
          release('skip', 'low');
          const r = ctx.rates();
          if (pay) ctx.pay(big ? Math.max(r.fps, r.click) * 8 : Math.max(1, r.click), 'skip', at);
          if (big) ctx.toast(ROUTE_COPY.paidWait);
          ctx.anim(p, [{ opacity: 1 }, { opacity: 0 }], { duration: 250, fill: 'both' });
          ctx.after(260, () => p.remove());
        };
        skip.addEventListener('pointerdown', (e) => { e.preventDefault(); close(true, false, { x: e.clientX, y: e.clientY }); });
        stop = ctx.every(1000, () => {
          left -= 1;
          skip.textContent = left > 0 ? fill(ROUTE_COPY.skipIn, { n: left }) : ROUTE_COPY.skipDone;
          if (left <= 0) { const r = skip.getBoundingClientRect(); close(true, true, { x: r.left, y: r.top }); }
        });
      };
      ctx.after(1500, play);
      ctx.every(60000, play);
      return {};
    },
  },

  /* Disguised Ads: the shop's own descriptions start recommending things. */
  o_native: {
    mount(ctx) {
      const next = deck(NATIVE_LINES, ctx), touched = new Map();
      ctx.css('.routes-native{background:#fffbe4 !important}.routes-native,.routes-native b,.routes-native small,.routes-native i{color:#222 !important}');
      const undo = () => { for (const [el, text] of touched) { el.textContent = text; el.closest('.row')?.classList.remove('routes-native'); } touched.clear(); };
      const turn = () => {
        undo();
        const rows = [...ctx.parts.shop.querySelectorAll('#rows .row small')];
        if (!rows.length) return;
        const el = rows[Math.floor(ctx.rng() * rows.length)];
        touched.set(el, el.textContent);
        el.textContent = `${ctx.brand.text(next())} (${AD})`;
        el.closest('.row')?.classList.add('routes-native');
        ctx.anim(el, [{ opacity: 0 }, { opacity: 1 }], { duration: 350 });
      };
      turn();
      ctx.every(3000, turn);
      return { off() { undo(); } };
    },
  },

  /* Sponsored Mascot: EMI pops up and reads the ad copy she was given. She has not rehearsed. */
  o_emi: {
    mount(ctx) {
      const reads = deck(EMI_AD_READS, ctx);
      const read = () => {
        const beats0 = 4;
        if (!claim(ctx, 'emi', beats0 * 1250 + 900, 'low')) { ctx.after(5000, read); return; }
        const beats = reads(), { w, h } = ctx.bounds();
        const box = ctx.el('div', `position:absolute;right:8px;top:${h - 66}px;height:58px;width:${Math.min(290, w - 16)}px;`, null, 'over');
        const head = ctx.el('div', 'position:absolute;right:0;top:0;width:58px;height:58px;border-radius:50%;background:#ffe0d2;border:2px solid #d4488f;box-sizing:border-box;overflow:hidden;', null, box);
        ctx.el('div', 'position:absolute;left:-2px;top:-2px;width:60px;height:21px;background:#ff69b4;', null, head);
        [13, 32].forEach((x) => ctx.el('div', `position:absolute;left:${x}px;top:27px;width:7px;height:10px;border-radius:50%;background:#2a0f22;`, null, head));
        const mouth = ctx.el('div', 'position:absolute;left:21px;top:41px;width:12px;height:5px;border-radius:0 0 8px 8px;background:#d4488f;', null, head);
        const bubble = ctx.el('div', `position:absolute;right:66px;top:12px;max-width:${Math.min(220, w - 90)}px;padding:4px 9px;background:#fff;color:#111;border:1px solid #333;border-radius:9px;font:700 16px ${FONT};white-space:nowrap;`, '', box);
        ctx.anim(box, ctx.gentle ? [{ opacity: 0 }, { opacity: 1 }] : [{ transform: 'translateY(80px)' }, { transform: 'none' }], { duration: 280, easing: 'ease-out' });
        beats.forEach((t, i) => ctx.after(i * 1250, () => {
          bubble.textContent = t;
          if (!ctx.gentle) ctx.anim(mouth, [{ height: '5px' }, { height: '9px' }, { height: '5px' }], { duration: 300 });
        }));
        ctx.after(beats.length * 1250 + 500, () => {
          ctx.anim(box, [{ opacity: 1 }, { opacity: 0 }], { duration: 280, fill: 'both' });
          ctx.after(300, () => { box.remove(); release('emi', 'low'); });
        });
      };
      ctx.after(1000, read);
      ctx.every(24000, read);
      return {};
    },
  },

  /* Ending: Living Ad. The last ad grows to fill the screen, with your score as the price. */
  o_out: {
    mount() { return {}; },
    ending(ctx) {
      const e = ENDINGS.o_out, score = ctx.fmt(ctx.state.total);
      return endingCard(ctx, {
        build(sheet, finish) {
          sheet.style.background = '#2a0f22';
          sheet.style.color = '#fff';
          ctx.anim(sheet, ctx.gentle ? [{ opacity: 0 }, { opacity: 1 }] : [{ clipPath: 'inset(84% 30% 8% 30%)' }, { clipPath: 'inset(0 0 0 0)' }], { duration: 1200, easing: 'ease-in-out', fill: 'both' });
          ctx.el('span', `${CHIP}font-size:14px;padding:2px 8px;`, AD, sheet);
          ctx.el('div', `font:800 min(22vw,120px)/1 ${FONT};color:#ff69b4;`, 'CCP', sheet);
          ctx.el('div', 'font-size:18px;max-width:520px;', e.result, sheet);
          ctx.el('div', 'font-size:14px;opacity:.8;margin-top:8px;', e.priceLabel, sheet);
          ctx.el('div', `font:700 min(9vw,36px) ${FONT};`, fill(e.price, { score }), sheet);
          const row = ctx.el('div', 'display:flex;flex-wrap:wrap;gap:10px;justify-content:center;margin-top:12px;', null, sheet);
          endButton(ctx, row, e.link.label, 'background:#ff69b4;color:#2a0f22;', () => ctx.openLink(e.link.url));
          endButton(ctx, row, ROUTE_COPY.next, 'background:#fff;color:#111;', finish);
          ctx.after(1300, () => { burst(ctx, ctx.bounds().w / 2, ctx.bounds().h * 0.3, '#ff69b4', 20, 160); ring(ctx, ctx.bounds().w / 2, ctx.bounds().h * 0.3, 300, '#ff69b4', 800); });
        },
      });
    },
  },
};
