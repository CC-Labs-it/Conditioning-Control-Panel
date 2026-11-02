/* Fewer Ads: you paid to be left alone, which is its own kind of noise. Gold everywhere, thanks everywhere, and a membership
 * that keeps checking in. Everything fake here is in-game: no inputs, no cards, and the only money in it is the game's own. */

import { FAKE_RENEWAL, NEW_TERMS, SUPPORT_MENU, ENDINGS } from '../../../data/content/index.js';
import { nextAd, panel, meterRow, burst, ring, bar, btnCentre, counterRect, deck, fill, docOf, railsRoom } from './kit.js';
import { endingCard, endButton } from './sell.js';
import { ROUTE_COPY } from './copy.js';

const GOLD = '#c9a227', GOLD_INK = '#4a3500';
const FONT = 'Arial,Helvetica,sans-serif';

const goldChip = (ctx, parent, text) => ctx.el('b', `font:700 12px Georgia,serif;color:${GOLD_INK};background:linear-gradient(90deg,#f6dd7e,${GOLD},#f6dd7e);padding:1px 8px;border-radius:9px;`, text, parent);

export default {
  /* Joke Ads Gone: from here on, only ads for the real thing. */
  p_member: {
    mount(ctx) {
      ctx.ads.jokesOff();
      const chip = goldChip(ctx, meterRow(ctx), ROUTE_COPY.premiumChip);
      const c = btnCentre(ctx);
      if (!ctx.gentle) {
        ctx.anim(chip, [{ transform: 'scale(2.4)', opacity: 0 }, { transform: 'scale(1)', opacity: 1 }], { duration: 420, easing: 'ease-out' });
        burst(ctx, c.x, c.y - 60, GOLD, 12, 90);
      }
      return {};
    },
  },

  /* Gold Badge: a gold star by your score, and your taps come out gold. */
  p_badge: {
    mount(ctx) {
      const star = ctx.el('div', `position:absolute;width:30px;height:30px;line-height:30px;text-align:center;border-radius:50%;background:radial-gradient(circle at 35% 30%,#fff3b0,${GOLD});color:#7a5a10;font-size:17px;box-shadow:0 0 0 1px #8a6d10;`, '★');
      const place = () => { const r = counterRect(ctx); star.style.left = `${r.left + r.width / 2 + Math.min(r.width / 2, 170) + 6}px`; star.style.top = `${r.top + 8}px`; };
      place();
      ctx.every(600, place);
      if (!ctx.gentle) ctx.anim(star, [{ transform: 'scale(0) rotate(-90deg)' }, { transform: 'scale(1.3) rotate(0)', offset: 0.6 }, { transform: 'scale(1) rotate(0)' }], { duration: 600 });
      const r = counterRect(ctx);
      burst(ctx, r.right, r.top + 20, GOLD, 10, 60);
      let n = 0;
      ctx.on('tap', ({ gain, auto }) => {
        if (auto || ++n % 3) return;
        const b = btnCentre(ctx);
        ctx.float(b.x, b.top - 6, `+${ctx.fmt(gain)}`, `color:${GOLD};font:700 15px ${FONT};text-shadow:0 1px 1px #5a4300;`);
      });
      return {};
    },
  },

  /* Skip Button: a gold button sits on an ad and pays you for leaving it alone. Press it and the pay stops for a while. */
  p_skip: {
    mount(ctx) {
      const wide = railsRoom(ctx);
      const host = wide ? panel(ctx, { side: 'right', height: 120, css: 'pointer-events:auto;' }) : ctx.el('div', 'width:100%;margin-bottom:4px;', null, 'banner');
      const box = ctx.el('div', `position:relative;display:flex;align-items:center;gap:8px;min-height:48px;height:100%;padding:4px 8px;box-sizing:border-box;background:#2a2410;color:#fff;border:1px solid ${GOLD};font:12px/1.15 ${FONT};pointer-events:auto;`, null, host);
      const txt = ctx.el('div', 'flex:1 1 auto;min-width:0;overflow:hidden;', '', box);
      const btn = ctx.el('button', `flex:none;min-height:40px;padding:2px 14px;background:${GOLD};color:#fff;border:0;border-radius:6px;font:700 14px Georgia,serif;touch-action:manipulation;`, ROUTE_COPY.skip, box);
      btn.type = 'button';
      box.classList.add('routes-ad');
      const turn = () => { const rec = nextAd(ctx, 'banner'); txt.textContent = ctx.brand.text(rec.headline); };
      turn();
      if (!ctx.gentle) ctx.anim(btn, [{ transform: 'scale(1)' }, { transform: 'scale(1.08)' }], { duration: 900, iterations: Infinity, direction: 'alternate' });
      let until = 0;
      ctx.every(1600, () => {
        if (ctx.state.clock < until) return;
        const r = btn.getBoundingClientRect();
        const got = ctx.pay(ctx.rates().fps * 0.25, 'noskip');
        ctx.float(r.left + r.width / 2, r.top - 4, `+${ctx.fmt(got)}`, `color:${GOLD};font:700 13px ${FONT};`);
      });
      ctx.every(8000, turn);
      ctx.on('ads', turn);
      btn.addEventListener('click', () => {
        until = ctx.state.clock + 45000;
        box.style.opacity = '.3';
        btn.disabled = true;
        ctx.toast(ROUTE_COPY.skipGone);
        ctx.after(45000, () => { box.style.opacity = ''; btn.disabled = false; turn(); ctx.toast(ROUTE_COPY.skipBack); });
      });
      return {};
    },
  },

  /* Gold Shop: the rows go gold, one at a time. The prices do not change, and that is the joke. */
  p_gold: {
    mount(ctx) {
      ctx.css(`.row{background:linear-gradient(90deg,#f6dd7e,${GOLD},#f6dd7e) !important;color:${GOLD_INK} !important;border-color:#8a6d10 !important;font-family:Georgia,serif}
.row.short{background:linear-gradient(90deg,#efe4b8,#d9c98f,#efe4b8) !important}.row small,.row .cost i,#goal .tag{color:#5f4a10 !important}
#goal .bar{background:rgba(138,109,16,.35) !important}`);
      ctx.vars({ '--ready': '#f6dd7e' });
      [...ctx.parts.shop.querySelectorAll('.row')].forEach((r, i) => {
        if (ctx.gentle) return;
        ctx.anim(r, [{ transform: 'scale(1)' }, { transform: 'scale(1.03)' }, { transform: 'scale(1)' }], { duration: 300, delay: i * 90 });
        const q = r.getBoundingClientRect();
        ctx.after(i * 90, () => burst(ctx, q.right - 20, q.top + q.height / 2, GOLD, 6, 40));
      });
      return {};
    },
  },

  /* Fake Renewal: every two minutes you have renewed. The cancel link moves, five times, and then lets you. It changes nothing. */
  p_renew: {
    mount(ctx) {
      const doc = docOf(ctx), bodies = deck(FAKE_RENEWAL.bodies, ctx), caught = deck(FAKE_RENEWAL.caught, ctx);
      const SPOTS = [[42, 56], [6, 76], [66, 30], [22, 8], [60, 78], [34, 44]];
      const show = () => {
        const node = doc.createElement('div');
        const text = doc.createElement('p');
        text.textContent = ctx.brand.text(bodies());
        node.appendChild(text);
        const yard = doc.createElement('div');
        yard.style.cssText = 'position:relative;height:110px;border:1px dashed #8a867d;overflow:hidden;';
        node.appendChild(yard);
        const link = doc.createElement('button');
        link.type = 'button';
        link.style.cssText = 'position:absolute;min-height:36px;padding:2px 8px;background:none;border:0;color:#1a4fd0;text-decoration:underline;transition:left .18s,top .18s;touch-action:manipulation;';
        yard.appendChild(link);
        let moves = 0, h = null;
        const to = (n) => { const [x, y] = SPOTS[n % SPOTS.length]; link.style.left = `${x}%`; link.style.top = `${y}%`; link.textContent = FAKE_RENEWAL.cancel[Math.min(n, FAKE_RENEWAL.cancel.length - 1)]; };
        to(0);
        const run = () => { moves += 1; to(moves); };
        link.addEventListener('pointerenter', (e) => { if (moves < 5 && e.pointerType === 'mouse') run(); });
        link.addEventListener('pointerdown', (e) => {
          if (moves < 5) { e.preventDefault(); run(); return; }
          text.textContent = ctx.brand.text(caught());
          yard.remove();
          const c = btnCentre(ctx), r = ctx.rates();
          ctx.pay(r.fps * 5, 'cancel', { x: c.x, y: c.y });
          ctx.after(1800, () => h?.close());
        });
        h = ctx.modal({ title: FAKE_RENEWAL.title, node, buttons: [{ label: FAKE_RENEWAL.close }] });
      };
      if (!ctx.restored) ctx.after(1500, show);
      ctx.every(120000, show);
      return {};
    },
  },

  /* Support Menu: EMI answers the phone. No option helps, and Hang up always works. */
  p_line: {
    mount(ctx) {
      const doc = docOf(ctx), hold = deck(SUPPORT_MENU.hold, ctx);
      const show = () => {
        const node = doc.createElement('div');
        const top = doc.createElement('div');
        top.style.cssText = 'display:flex;gap:10px;align-items:flex-start;';
        const face = doc.createElement('div');
        face.style.cssText = 'flex:none;width:56px;height:56px;border-radius:50%;background:linear-gradient(#ff69b4 0 36%,#ffe0d2 36%);border:2px solid #d4488f;position:relative;';
        [13, 31].forEach((x) => { const e = doc.createElement('div'); e.style.cssText = `position:absolute;left:${x}px;top:25px;width:7px;height:10px;border-radius:50%;background:#2a0f22;`; face.appendChild(e); });
        const say = doc.createElement('p');
        say.style.cssText = 'margin:0;min-height:56px;';
        top.append(face, say);
        node.appendChild(top);
        const pad = doc.createElement('div');
        pad.style.cssText = 'display:grid;grid-template-columns:1fr;gap:4px;margin-top:8px;';
        node.appendChild(pad);
        const note = doc.createElement('div');
        note.style.cssText = 'margin-top:6px;font-style:italic;color:#555;font-size:14px;min-height:18px;';
        node.appendChild(note);
        let calls = 0;
        const go = (id) => {
          const n = SUPPORT_MENU.nodes[id];
          say.textContent = ctx.brand.text(n.say);
          pad.textContent = '';
          for (const o of n.options) {
            const b = doc.createElement('button');
            b.type = 'button';
            b.style.cssText = 'text-align:left;min-height:40px;';
            b.textContent = `${o.key}. ${ctx.brand.text(o.label)}`;
            b.addEventListener('click', () => {
              calls += 1;
              ctx.audio.play('assets/audio/sfx/ui_click.mp3', { bus: 'sfx', gain: 0.4 });
              note.textContent = fill(ctx.brand.text(hold()), { n: 3 + calls * 2 });
              go(o.next);
            });
            pad.appendChild(b);
          }
        };
        go(SUPPORT_MENU.start);
        ctx.modal({ title: SUPPORT_MENU.title, node, buttons: [{ label: SUPPORT_MENU.hangUp }] });
      };
      if (!ctx.restored) ctx.after(1500, show);
      ctx.every(200000, show);
      return {};
    },
  },

  /* Remove Ads: a gold button. Everything goes quiet for a minute, then new terms arrive and it all comes back. */
  p_remove: {
    mount(ctx) {
      const row = ctx.el('div', 'display:flex;align-items:center;gap:6px;', null, meterRow(ctx));
      const btn = ctx.el('button', `min-height:30px;padding:0 12px;background:${GOLD};color:#fff;border:0;border-radius:6px;font:700 13px Georgia,serif;pointer-events:auto;touch-action:manipulation;`, NEW_TERMS.button, row);
      btn.type = 'button';
      const m = bar(ctx, row, 'width:44px;height:7px;', GOLD);
      const terms = deck(NEW_TERMS.terms, ctx);
      let sheet = null, live = false;
      const backOn = () => { sheet?.remove(); sheet = null; };
      btn.addEventListener('click', () => {
        if (live) return;
        live = true;
        btn.disabled = true;
        btn.style.opacity = '.5';
        sheet = ctx.css('#l-banner,.routes-ad{visibility:hidden}');
        ctx.toast(NEW_TERMS.gone);
        const c = btnCentre(ctx);
        ring(ctx, c.x, c.y, 260, GOLD, 700);
        m.set(1);
        ctx.after(30, () => m.set(0, 60000));
        ctx.after(60000, () => {
          backOn();
          ctx.modal({
            title: NEW_TERMS.title,
            body: ctx.brand.text(`${terms()} ${terms()}`),
            buttons: [{ label: NEW_TERMS.close, onPress() { live = false; btn.disabled = false; btn.style.opacity = ''; } }],
          });
        });
      });
      return { off() { backOn(); } };
    },
  },

  /* Ending: Lifetime Plan. A gold sheet rises and thanks you, for ever, and the button under it goes to the real membership. */
  p_life: {
    mount() { return {}; },
    ending(ctx) {
      const e = ENDINGS.p_life;
      return endingCard(ctx, {
        build(sheet, finish) {
          sheet.style.background = 'linear-gradient(160deg,#f6dd7e,#c9a227 60%,#a07f14)';
          sheet.style.color = '#3e2c00';
          ctx.anim(sheet, ctx.gentle ? [{ opacity: 0 }, { opacity: 1 }] : [{ transform: 'translateY(100%)' }, { transform: 'none' }], { duration: 1100, easing: 'ease-out', fill: 'both' });
          ctx.el('div', 'font:700 min(26vw,140px)/1 Georgia,serif;', '∞', sheet);
          ctx.el('div', 'font:italic 700 34px Georgia,serif;', ROUTE_COPY.thanks, sheet);
          ctx.el('div', 'font-size:16px;', ROUTE_COPY.forEver, sheet);
          ctx.el('div', 'font-size:16px;max-width:520px;margin-top:6px;', e.result, sheet);
          const row = ctx.el('div', 'display:flex;flex-wrap:wrap;gap:10px;justify-content:center;margin-top:12px;', null, sheet);
          endButton(ctx, row, e.link.label, 'background:#2a0f22;color:#fff;', () => ctx.openLink(e.link.url));
          endButton(ctx, row, ROUTE_COPY.next, 'background:#fff6c8;color:#3e2c00;', finish);
          [1300, 2600, 3900].forEach((ms) => ctx.after(ms, () => burst(ctx, ctx.bounds().w / 2, ctx.bounds().h * 0.3, '#fff6c8', 16, 170)));
        },
      });
    },
  },
};
