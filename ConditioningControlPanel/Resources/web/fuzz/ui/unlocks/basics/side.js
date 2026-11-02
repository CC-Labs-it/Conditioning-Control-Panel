/* ui/unlocks/basics/side.js - Side Toys part one: Golden Button, Screen Time, Levels, Mystery Boxes, Prize Wheel, Phone Buzz,
 * Fake Inbox, Fake Live Chat. Anything tappable lands in `over` with at least a 44 px target and always leaves by itself. */

import { APP_ONLY, BOXES, FAKE_CHAT, FAKE_INBOX, GOLDEN, LEVELS, SCREEN_TIME, WHEEL } from '../../../data/content/index.js';
import { baseCss, burst, btnCenter, chip, docOf, fresh, pop, sfx, squish } from './fx.js';
import { XP_PER_SECOND, XP_PER_TAP, bankOf, collect, xpNeed } from '../../../sim/levels.js';

const pick = (ctx, list) => list[Math.floor(ctx.rng() * list.length)];

/** A reward worth a look at any point of the run: seconds of income, with a floor from what taps pay. */
const worth = (ctx, seconds) => { const r = ctx.rates(); return Math.max(r.fps, r.click * 2, 2) * seconds; };

export default {
  /* Golden Button: drifts past now and then. One of three prizes. */
  gold: {
    mount(ctx) {
      let frenzyUntil = 0, free = !!ctx.store.get('free'), now = 0, g = null, gx = 0, gy = 0, nextAt = fresh(ctx) ? 2500 : 25000;
      ctx.mod({ click: (v, s, t) => (t < frenzyUntil ? v * 7 : v) });
      ctx.mod({ price: (v, s, t, item) => (free && item.kind === 'item' ? 0 : v) });
      const bar = ctx.el('div', 'order:3;height:6px;width:150px;margin-top:3px;background:#e7b416;transform-origin:0 50%;display:none;', null, ctx.layer('meters'));
      ctx.on('buy', ({ item }) => { if (free && item.kind === 'item') { free = false; ctx.store.set('free', false); } });

      const prize = (x, y) => {
        const key = pick(ctx, GOLDEN.prizes).key;
        const p = GOLDEN.prizes.find((q) => q.key === key);
        if (key === 'lump') {
          const n = ctx.pay(worth(ctx, 45), 'golden', { x, y });
          pop(ctx, x, y - 30, p.label.replace('{n}', ctx.fmt(n)), { color: '#b8860b', size: 34, ms: 1500 });
        } else if (key === 'frenzy') {
          frenzyUntil = now + 12000;
          pop(ctx, x, y - 30, p.label, { color: '#b8860b', size: 44, ms: 1500 });
          bar.style.display = 'block';
        } else {
          free = true;
          ctx.store.set('free', true);
          pop(ctx, x, y - 30, p.label, { color: '#b8860b', size: 34, ms: 1500 });
        }
        ctx.say(p.line);
      };

      const launch = () => {
        const { w, h } = ctx.bounds();
        gx = -70; gy = 90 + ctx.rng() * (h * 0.45);
        g = ctx.el('button', 'position:absolute;left:0;top:0;width:76px;height:44px;border:1px solid #8a6500;border-radius:22px;pointer-events:auto;touch-action:manipulation;background:linear-gradient(135deg,#fff3b0,#e7b416 55%,#b8860b);box-shadow:0 2px 10px rgba(180,130,0,.65);will-change:transform;', null, 'over');
        g.type = 'button';
        g.setAttribute('aria-label', 'Golden Button');
        const me = g;
        const end = () => { if (g === me) g = null; me.remove(); };
        me.addEventListener('pointerdown', (e) => {
          e.preventDefault();
          if (g !== me) return;
          const r = me.getBoundingClientRect(), x = r.left + r.width / 2, y = r.top + r.height / 2;
          end();
          burst(ctx, x, y, { color: '#e7b416', n: 22, reach: 110, size: 8, ms: 800 });
          sfx(ctx, 'sfx/golden_pop.mp3', 0.7);
          ctx.emit('golden', { x, y });
          prize(x, y);
        });
        me.fzEnd = () => { end(); ctx.say(pick(ctx, GOLDEN.missed)); };
        me.fzW = w;
      };

      return {
        tick(dt, t) {
          now = t;
          if (nextAt > 0) { nextAt -= dt * 1000; if (nextAt <= 0 && !g) launch(); }
          if (g) {
            gx += dt * (ctx.gentle ? 45 : 85);
            g.style.transform = `translate(${gx}px,${gy}px)`;
            if (gx > g.fzW + 10) { g.fzEnd(); nextAt = 30000 + ctx.rng() * 35000; }
          } else if (nextAt <= 0) nextAt = 30000 + ctx.rng() * 35000;
          if (now < frenzyUntil) bar.style.transform = `scaleX(${((frenzyUntil - now) / 12000).toFixed(3)})`;
          else if (bar.style.display === 'block') bar.style.display = 'none';
        },
      };
    },
  },

  /* Screen Time: the wellness feature. A card on a wide rail, a chip that opens it anywhere. */
  screen: {
    mount(ctx) {
      baseCss(ctx);
      const ROWS = [['taps', '#ff69b4'], ['strays', '#7b5cff'], ['ads', '#16a596'], ['idle', '#f0a43a'], ['other', '#999']];
      const sort = (src) => {
        const out = { taps: 0, strays: 0, ads: 0, idle: 0, other: 0 };
        for (const [k, v] of Object.entries(src)) {
          if (k === 'tap') out.taps += v; else if (k === 'wall') out.strays += v; else if (k === 'ad') out.ads += v; else if (k === 'fps') out.idle += v; else if (k !== 'dev') out.other += v;
        }
        return out;
      };
      const build = (host) => {
        const head = ctx.el('div', 'font-weight:700;margin-bottom:4px;', '', host);
        const bars = ROWS.map(([key, col]) => {
          const r = ctx.el('div', 'display:flex;align-items:center;gap:6px;font-size:12px;margin:3px 0;', null, host);
          ctx.el('span', 'flex:none;width:76px;', SCREEN_TIME.sources[key], r);
          const track = ctx.el('span', 'flex:1 1 auto;height:12px;background:rgba(0,0,0,.08);', null, r);
          const fill = ctx.el('i', `display:block;height:100%;width:0;background:${col};transition:width .9s ease-out;`, null, track);
          return { key, fill };
        });
        const tip = ctx.el('div', 'font-size:12px;margin-top:6px;opacity:.8;', pick(ctx, SCREEN_TIME.tips), host);
        const draw = () => {
          head.textContent = SCREEN_TIME.today.replace('{min}', String(Math.max(1, Math.round(ctx.state.clock / 60000))));
          const s = sort(ctx.state.sources), top = Math.max(1, ...Object.values(s));
          for (const b of bars) b.fill.style.width = `${(s[b.key] / top * 100).toFixed(1)}%`;
        };
        draw();
        return { draw, tip };
      };
      const rail = ctx.layer('side', 'left');
      const wide = () => rail.clientWidth >= 170;
      let card = null;
      const place = () => {
        if (card && !wide()) { card.box.remove(); card = null; }
        if (!card && wide()) {
          const box = ctx.el('div', 'padding:8px;background:var(--panel);color:var(--ink);border:1px solid var(--line);border-radius:6px;font-family:var(--font);', null, rail);
          card = { box, ...build(box) };
          if (fresh(ctx)) ctx.anim(box, [{ transform: 'translateX(-40px)', opacity: 0 }, { transform: 'none', opacity: 1 }], { duration: 350, easing: 'ease-out' });
        }
      };
      place();
      ctx.every(2000, () => { place(); card?.draw(); open.style.display = wide() ? 'none' : ''; });
      ctx.every(20000, () => { if (card) card.tip.textContent = pick(ctx, SCREEN_TIME.tips); });
      const open = chip(ctx, SCREEN_TIME.title, () => {
        const node = docOf(ctx).createElement('div');
        const m = build(node);
        ctx.modal({ title: SCREEN_TIME.title, node });
        m.draw();
      });
      open.style.display = wide() ? 'none' : '';
      return {};
    },
  },

  /* Levels: an XP bar under the counter. XP never stops: whole levels bank up while the bar keeps filling, and one press
   * pays them all at once, with a party the size of what was left waiting. */
  levels: {
    mount(ctx) {
      ctx.css('@keyframes fz-ready { 50% { box-shadow: 0 0 0 3px var(--niche); } } .fz-lvl-ready { animation: fz-ready 1.2s ease-in-out infinite; } body.gentle .fz-lvl-ready { animation: none; }');
      let lvl = ctx.store.get('lvl') || 1, xp = ctx.store.get('xp') || 0, shownBank = -1, lastFill = 0;
      const row = ctx.el('div', 'order:4;display:flex;align-items:center;gap:6px;margin-top:3px;font-size:13px;pointer-events:auto;', null, ctx.layer('meters'));
      const label = ctx.el('span', 'flex:none;', '', row);
      const track = ctx.el('span', 'display:block;width:110px;height:10px;background:var(--panel);border:1px solid var(--line);border-radius:5px;overflow:hidden;', null, row);
      const fill = ctx.el('i', 'display:block;height:100%;width:0;background:linear-gradient(90deg,var(--niche),color-mix(in srgb,var(--niche),var(--niche-2)) 45%,var(--niche-2));transition:width .25s linear;', null, track);
      const b = ctx.el('button', 'min-height:30px;padding:1px 8px;font-size:12px;background:var(--btn);color:var(--btn-ink,inherit);border:1px solid var(--niche);touch-action:manipulation;visibility:hidden;white-space:nowrap;', LEVELS.collect, row);
      b.type = 'button';
      const draw = () => {
        const now = bankOf(lvl, xp);
        label.textContent = LEVELS.label.replace('{n}', String(now.lvl));
        /* The bar wraps when a level banks: jump back, never drain. */
        fill.style.transition = now.fill < lastFill ? 'none' : 'width .25s linear';
        lastFill = now.fill;
        fill.style.width = `${(now.fill * 100).toFixed(1)}%`;
        if (now.banked === shownBank) return;
        const rose = shownBank >= 0 && now.banked > shownBank;
        shownBank = now.banked;
        b.textContent = `${LEVELS.collect} ${now.banked}`;
        b.style.visibility = now.banked ? 'visible' : 'hidden';
        b.classList.toggle('fz-lvl-ready', now.banked > 0);
        if (rose) squish(ctx, b, 1.2);
      };
      const press = () => {
        const got = collect(lvl, xp);
        if (!got.levels) return;
        const r = b.getBoundingClientRect(), x = r.left + r.width / 2, y = r.top;
        const k = got.levels, size = Math.min(1, (k - 1) / 5);   // one level is a small party, six or more the biggest
        ctx.pay(worth(ctx, got.seconds), 'levels', { x: r.left, y: r.top });
        lvl = got.lvl; xp = got.xp;
        ctx.store.set('lvl', lvl); ctx.store.set('xp', xp);
        burst(ctx, x, y, { color: 'var(--niche)', n: 14 + Math.round(26 * size), reach: 80 + 110 * size });
        if (k > 1) {
          burst(ctx, x, y, { color: 'var(--niche-2)', n: 8 + Math.round(18 * size), reach: 50 + 150 * size, size: 5 });
          pop(ctx, x, y - 26, LEVELS.label.replace('{n}', String(lvl)), { color: 'var(--niche)', size: 20 + Math.round(18 * size), ms: 1100 });
          squish(ctx, ctx.parts.counter, 0.5 + size * 0.5);
        }
        sfx(ctx, k >= 3 ? 'sfx/pb_fanfare.mp3' : 'sfx/lvup.mp3', 0.6);
        ctx.say(pick(ctx, LEVELS.up));
        draw();
      };
      b.addEventListener('click', press);
      ctx.on('tap', () => { xp += XP_PER_TAP; draw(); });
      ctx.every(1000, () => { xp += XP_PER_SECOND; ctx.store.set('xp', xp); draw(); });
      if (fresh(ctx)) xp = xpNeed(lvl) * 0.8;
      draw();
      return {};
    },
  },

  /* Mystery Boxes: fall in, wait six seconds, leave. Sparkle hooks in through the `box` event. */
  boxes: {
    mount(ctx) {
      let live = 0;
      const drop = () => {
        if (live >= 3) return;
        const { w, h } = ctx.bounds();
        const x = 20 + ctx.rng() * (w - 110), y = 80 + ctx.rng() * (h * 0.5);
        live += 1;
        const box = ctx.el('button', `position:absolute;left:${x}px;top:${y}px;width:68px;height:62px;padding:0;border:1px solid #a02a6a;border-radius:4px;background:linear-gradient(90deg,#ff69b4 42%,#ffe08a 42% 58%,#ff69b4 58%);color:#2a0f22;font-size:11px;font-weight:700;pointer-events:auto;touch-action:manipulation;`, pick(ctx, BOXES.labels), 'over');
        box.type = 'button';
        const bar = ctx.el('i', 'position:absolute;left:0;right:0;bottom:-6px;height:4px;background:var(--stamp);transform-origin:0 50%;', null, box);
        if (!ctx.gentle) ctx.anim(box, [{ transform: 'translateY(-240px)', opacity: 0 }, { transform: 'none', opacity: 1, offset: 0.6 }, { transform: 'translateY(-9px)', offset: 0.8 }, { transform: 'none' }], { duration: 520, easing: 'ease-in' });
        ctx.anim(bar, [{ transform: 'scaleX(1)' }, { transform: 'scaleX(0)' }], { duration: 6000, easing: 'linear', fill: 'forwards' });
        let gone = false;
        const leave = () => { gone = true; live -= 1; box.remove(); };
        const stop = ctx.after(6000, () => { if (gone) return; leave(); ctx.say(pick(ctx, BOXES.expired)); });
        box.addEventListener('pointerdown', (e) => {
          e.preventDefault();
          if (gone) return;
          const cx = x + 34, cy = y + 30;
          stop();
          leave();
          ctx.pay(worth(ctx, 12), 'boxes', { x: cx, y: cy });
          burst(ctx, cx, cy, { color: '#ff69b4', n: 16, reach: 80 });
          sfx(ctx, 'sfx/chain_pop.mp3', 0.6);
          ctx.say(pick(ctx, BOXES.opened));
          ctx.emit('box', { x: cx, y: cy });
        });
      };
      if (fresh(ctx)) ctx.after(500, drop);
      ctx.every(26000, drop);
      return {};
    },
  },

  /* Prize Wheel: free every 90 s, paid only in the game's money. A chip counts down; the wheel opens on a card. */
  wheel: {
    mount(ctx) {
      const WAIT = 90;
      const labels = ['A bit', 'Some', 'More', 'Most', 'Some', 'A bit'], mult = [0.5, 1, 2, 4, 1, 0.5];
      const stops = ['#ff9ccf', '#c9b6ff', '#ffe08a', '#ff69b4', '#9ee6dd', '#d9d4c8'];
      let wait = fresh(ctx) ? 0 : (ctx.store.get('wait') ?? 0);
      const c = chip(ctx, '', () => { if (wait <= 0) open(); else ctx.toast(WHEEL.wait.replace('{n}', String(Math.ceil(wait)))); });
      const draw = () => { c.textContent = wait > 0 ? `${WHEEL.spin} ${Math.ceil(wait)} s` : WHEEL.spin; c.style.fontWeight = wait > 0 ? '' : '700'; };
      const open = () => {
        const node = docOf(ctx).createElement('div');
        node.style.cssText = 'position:relative;display:grid;place-items:center;height:230px;';
        const grad = stops.map((s, i) => `${s} ${i * 60}deg ${(i + 1) * 60}deg`).join(',');
        const w = docOf(ctx).createElement('div');
        w.style.cssText = `position:relative;width:200px;height:200px;border:3px solid #333;border-radius:50%;background:conic-gradient(${grad});`;
        labels.forEach((t, i) => {
          const a = (i * 60 + 30) * Math.PI / 180, l = docOf(ctx).createElement('span');
          l.textContent = t;
          l.style.cssText = `position:absolute;left:${100 + Math.sin(a) * 68 - 20}px;top:${100 - Math.cos(a) * 68 - 10}px;width:40px;text-align:center;font-weight:700;font-size:14px;color:#111;`;
          w.appendChild(l);
        });
        const tick = docOf(ctx).createElement('i');
        tick.style.cssText = 'position:absolute;left:50%;top:4px;margin-left:-8px;border:8px solid transparent;border-top:16px solid #111;z-index:2;';
        node.append(w, tick);
        const i = Math.floor(ctx.rng() * 6);
        const spun = { done: false };
        const m = ctx.modal({
          title: WHEEL.spin,
          body: WHEEL.note,
          node,
          buttons: [{
            label: WHEEL.spin,
            keepOpen: true,
            onPress: () => {
              if (spun.done) return;
              spun.done = true;
              wait = WAIT;
              ctx.store.set('wait', wait);
              draw();
              const end = 1080 - (i * 60 + 30);
              const land = () => {
                const n = worth(ctx, 30) * mult[i];
                ctx.pay(n, 'wheel', btnCenter(ctx));
                ctx.say(pick(ctx, WHEEL.results));
                sfx(ctx, mult[i] >= 2 ? 'sfx/pb_fanfare.mp3' : 'sfx/rank_settle.mp3', 0.5);
                ctx.after(900, () => m.close());
              };
              if (ctx.gentle) { w.style.transform = `rotate(${end}deg)`; ctx.after(400, land); return; }
              const a = w.animate([{ transform: 'rotate(0)' }, { transform: `rotate(${end}deg)` }], { duration: 2200, easing: 'cubic-bezier(.15,.7,.2,1)', fill: 'forwards' });
              a.finished?.then(land, () => {});
            },
          }, { label: 'Close' }],
        });
      };
      ctx.every(1000, () => { if (wait > 0) { wait -= 1; ctx.store.set('wait', wait); draw(); } });
      draw();
      if (fresh(ctx)) squish(ctx, c, 1.3);
      return {};
    },
  },

  /* Phone Buzz: a real buzz on lucky taps where the browser can, and the same shake on screen everywhere. */
  buzz: {
    mount(ctx) {
      const nav = docOf(ctx).defaultView.navigator;
      const can = typeof nav.vibrate === 'function';
      const shake = () => {
        const el = ctx.button.el;
        if (can) { try { nav.vibrate(70); } catch { /* the browser said no */ } }
        if (ctx.hosted) ctx.host('haptic', { level: 0.4, ms: 150, tag: 'lucky' });
        if (ctx.gentle) return;
        ctx.anim(el, [{ rotate: '-3deg' }, { rotate: '3deg' }, { rotate: '0deg' }], { duration: 340, iterations: 1, easing: 'linear' });
        const c = btnCenter(ctx);
        for (const [dx, t] of [[-c.w / 2 - 22, '(('], [c.w / 2 + 22, '))']]) pop(ctx, c.x + dx, c.y, t, { color: '#7b5cff', size: 30, ms: 700 });
      };
      ctx.on('lucky', shake);
      if (fresh(ctx)) { shake(); ctx.toast(can ? APP_ONLY.buzz.web : APP_ONLY.buzz.off); }
      return {};
    },
  },

  /* Fake Inbox: notices from nobody. One at a time on a phone, two on a desktop. They close by themselves or on a tap. */
  inbox: {
    mount(ctx) {
      let shown = 0, n = ctx.store.get('n') || 0;
      const post = () => {
        const { w } = ctx.bounds();
        const cap = w < 760 ? 1 : 2;
        if (shown >= cap) return;
        const m = FAKE_INBOX[(n * 7) % FAKE_INBOX.length];
        n += 1;
        ctx.store.set('n', n);
        shown += 1;
        const width = Math.min(230, w - 130);
        const box = ctx.el('button', `position:absolute;top:${52 + (shown - 1) * 66}px;right:8px;width:${width}px;min-height:44px;padding:5px 8px;text-align:left;background:#fff;color:#111;border:1px solid #333;border-left:5px solid #ff69b4;border-radius:4px;box-shadow:0 2px 6px rgba(0,0,0,.3);font-size:12px;line-height:1.15;pointer-events:auto;touch-action:manipulation;`, null, 'over');
        box.type = 'button';
        ctx.el('b', 'display:block;font-size:13px;', m.from, box);
        ctx.el('span', '', m.text, box);
        ctx.anim(box, ctx.gentle ? [{ opacity: 0 }, { opacity: 1 }] : [{ transform: 'translateX(260px)' }, { transform: 'none' }], { duration: 300, easing: 'ease-out' });
        sfx(ctx, 'sfx/snap.mp3', 0.3);
        let gone = false;
        const close = () => {
          if (gone) return;
          gone = true;
          shown -= 1;
          const a = ctx.anim(box, [{ opacity: 1 }, { opacity: 0 }], { duration: 250 });
          a.onfinish = a.oncancel = () => box.remove();
        };
        box.addEventListener('click', close);
        ctx.after(6500, close);
      };
      ctx.after(fresh(ctx) ? 300 : 9000, post);
      ctx.every(17000, post);
      return {};
    },
  },

  /* Fake Live Chat: preset lines from nobody, reacting to what happens. A rail on a desktop, a two line strip on a phone. */
  fakechat: {
    mount(ctx) {
      const rail = ctx.layer('side', 'right');
      const wide = rail.clientWidth >= 170;
      const parent = wide ? rail : ctx.layer('banner');
      const css = wide
        ? 'display:flex;flex-direction:column;justify-content:flex-end;gap:3px;height:min(46vh,340px);padding:6px;background:#17132c;color:#fff;border-radius:6px;overflow:hidden;font:500 12px/1.2 "Segoe UI",system-ui,sans-serif;'
        : 'display:flex;flex-direction:column;justify-content:flex-end;gap:1px;height:36px;margin:3px 0;padding:2px 8px;background:#17132c;color:#fff;border-radius:4px;overflow:hidden;font:500 12px/1.2 "Segoe UI",system-ui,sans-serif;';
      const box = ctx.el('div', css, null, parent);
      const COLS = ['#ff9ccf', '#8fd4ff', '#ffe08a', '#a6f0b0'];
      const cap = wide ? 20 : 3;
      let n = 0;
      const say = (list) => {
        const d = docOf(ctx).createElement('div');
        d.style.cssText = 'white-space:nowrap;overflow:hidden;text-overflow:ellipsis;flex:none;';
        const b = docOf(ctx).createElement('b');
        b.style.color = COLS[n % 4];
        b.textContent = FAKE_CHAT.names[Math.floor(ctx.rng() * FAKE_CHAT.names.length)];
        d.append(b, ` ${ctx.brand.text(pick(ctx, list))}`);
        box.appendChild(d);
        n += 1;
        while (box.children.length > cap) box.firstChild.remove();
      };
      let lastTap = ctx.state.taps, still = 0;
      ctx.on('buy', () => { say(FAKE_CHAT.buy); say(FAKE_CHAT.buy); });
      ctx.on('lucky', () => say(FAKE_CHAT.lucky));
      ctx.on('ad', () => say(FAKE_CHAT.ad));
      ctx.every(2800, () => {
        const t = ctx.state.taps;
        still = t === lastTap ? still + 1 : 0;
        lastTap = t;
        say(still > 4 ? FAKE_CHAT.idleHands : FAKE_CHAT.idle);
      });
      for (let i = 0; i < 5; i++) say(FAKE_CHAT.idle);
      return {};
    },
  },
};

