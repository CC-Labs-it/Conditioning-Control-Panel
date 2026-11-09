/* ui/unlocks/basics/side.js - Side Toys part one: Golden Button, Screen Time, Levels, Mystery Boxes, Prize Wheel, Phone Buzz,
 * Fake Inbox, Fake Live Chat. Anything tappable lands in `over` with at least a 44 px target and always leaves by itself. */

import { APP_ONLY, BOXES, FAKE_CHAT, FAKE_INBOX, GOLDEN, LEVELS, SCREEN_TIME } from '../../../data/content/index.js';
import { baseCss, burst, btnCenter, chip, docOf, fresh, pop, sfx, squish } from './fx.js';
import { XP_PER_SECOND, XP_PER_TAP, bankOf, collect, xpNeed } from '../../../sim/levels.js';
import { mountPrizeWheel } from './prize-wheel.js';
import { journeyChapter } from '../../../sim/journey-effects.js';
import { createCollectionFeel } from './collect-feel.js';
import { createGoldFx } from './gold-fx.js';
import { createBoxFx } from './box-fx.js';
import { CONFIG } from '../../../data/config.js';
import { autopilotShare, boxHoldMs, frenzyMult, goldGapMult, goldStay, prizeMult } from '../prestige/boosts.js';  // prestige hooks (Renewal Sky, boosts lane)
import { sunriseStay } from '../../../sim/prestige-endings.js';  // prestige hook (Renewal Sky, ending stars)

const pick = (ctx, list) => list[Math.floor(ctx.rng() * list.length)];

/* Golden Button prizes (owner 2026-11-07): a lump that scales with the run, Frenzy (x7 on the button), Deep (x5 per-second
 * pay) or a free next buy. The two buffs are timed: they skip the multiplier knees and multiply with any other buff. */
const GOLD_FRENZY = 7, GOLD_FRENZY_MS = 20000, GOLD_DEEP = 5, GOLD_DEEP_MS = 40000;
const GOLD_WEIGHT = { lump: 35, frenzy: 35, deep: 20, free: 10 };

/** A reward worth a look at any point of the run: seconds of income, with a floor from what taps pay. */
const worth = (ctx, seconds) => { const r = ctx.rates(); return Math.max(r.fps, r.click * 2, 2) * seconds; };

export default {
  /* Golden Button: drifts past every 2 to 5 minutes, only for a player who pressed something in the last minute (owner
   * 2026-11-07: staying pays). One of four weighted prizes. The look and feel live in gold-fx.js. */
  gold: {
    mount(ctx) {
      let frenzyUntil = 0, deepUntil = 0, free = !!ctx.store.get('free'), now = 0, g = null, path = null, gy = 0, gt = 0, wait = 0, nextAt = fresh(ctx) ? 2500 : 25000;
      let lastPress = 0;
      const doc = docOf(ctx);
      const fx = createGoldFx(ctx, { frenzy: GOLD_FRENZY, deep: GOLD_DEEP });
      const present = () => { lastPress = now; };
      doc.addEventListener('pointerdown', present, true);
      doc.addEventListener('keydown', present, true);
      ctx.cleanup(() => { doc.removeEventListener('pointerdown', present, true); doc.removeEventListener('keydown', present, true); });
      const here = () => now - lastPress < CONFIG.GOLD_PRESENT_S * 1000;
      const gap = () => (CONFIG.GOLD_MIN_S + ctx.rng() * (CONFIG.GOLD_MAX_S - CONFIG.GOLD_MIN_S)) * 1000 * goldGapMult(ctx.prestige);  // prestige hook: Golden Habit (active_4)
      ctx.mod({ timed: true, click: (v, s, t) => (t < frenzyUntil ? v * GOLD_FRENZY : v) });
      ctx.mod({ timed: true, fps: (v, s, t) => (t < deepUntil ? v * GOLD_DEEP : v) });
      ctx.mod({ price: (v, s, t, item) => (free && item.kind === 'item' ? 0 : v) });
      ctx.on('buy', ({ item }) => { if (free && item.kind === 'item') { free = false; ctx.store.set('free', false); } });
      ctx.on('tap', ({ x, y, auto }) => { if (!auto && now < frenzyUntil && Number.isFinite(x)) fx.tapSpark(x, y, GOLD_FRENZY, now); });

      const prize = (x, y) => {
        const chapter = ctx.state.store.journey ? journeyChapter(ctx.state) : null;
        const couponUsed = chapter !== null && (free || ctx.store.get('couponChapter') === chapter);
        const pool = couponUsed ? GOLDEN.prizes.filter(p => p.key !== 'free') : GOLDEN.prizes;
        let roll = ctx.rng() * pool.reduce((a, q) => a + (GOLD_WEIGHT[q.key] || 1), 0), p = pool[0];
        for (const q of pool) { roll -= GOLD_WEIGHT[q.key] || 1; if (roll <= 0) { p = q; break; } }
        const key = p.key;
        let text = p.label;
        if (key === 'lump') {
          const n = ctx.pay(Math.min(worth(ctx, 600), ctx.state.fuzz * 0.15 + worth(ctx, 45)), 'golden', { x, y });
          text = p.label.replace('{n}', ctx.fmt(n));
        } else if (key === 'frenzy') {
          frenzyUntil = Math.max(frenzyUntil, now) + GOLD_FRENZY_MS * frenzyMult(ctx.prestige);  // prestige hook: Frenzy Fingers (active_10)
        } else if (key === 'deep') {
          deepUntil = Math.max(deepUntil, now) + GOLD_DEEP_MS;
        } else {
          free = true;
          ctx.store.set('free', true);
          if (chapter !== null) ctx.store.set('couponChapter', chapter);
          text = 'Paid in full';
        }
        ctx.emit('golden-caught', { key });   // the Living Ad card counts these
        fx.reveal(x, y, key, text, () => { if (key === 'free') fx.ticket(x, y); });
        ctx.say(p.line);
      };

      const launch = () => {
        const { w, h } = ctx.bounds();
        gy = 90 + ctx.rng() * (h * 0.42); gt = 0; wait = 1;
        path = fx.route(w, h, gy);
        fx.glint(path);
        g = fx.makeButton();
        g.style.opacity = '0';
        g.style.transform = `translate(${path.start.x}px,${path.start.y}px)`;
        const me = g;
        me.addEventListener('pointerdown', (e) => {
          e.preventDefault();
          if (g !== me || wait > 0) return;
          const r = me.getBoundingClientRect(), x = r.left + r.width / 2, y = r.top + r.height / 2;
          g = null;
          nextAt = gap();
          fx.catchHit(me, x, y);
          ctx.after(260, () => me.remove());
          burst(ctx, x, y, { color: '#e7b416', n: 22, reach: 110, size: 8, ms: 800 });
          sfx(ctx, 'sfx/golden_pop.mp3', 0.7);
          ctx.emit('golden', { x, y });
          prize(x, y);
        });
      };
      /* Dev door (window.fuzz exists only where the debug door is on): launch now, or start a buff, to judge the feel. */
      const view = doc.defaultView;
      if (view?.fuzz) {
        view.fuzz.gold = { launch: () => { if (!g) launch(); }, buff: (key, ms) => { if (key === 'frenzy') frenzyUntil = now + (ms || GOLD_FRENZY_MS); if (key === 'deep') deepUntil = now + (ms || GOLD_DEEP_MS); } };
        ctx.cleanup(() => { if (view.fuzz) delete view.fuzz.gold; });
      }
      /* Ghost Run Replay (ui/unlocks/prestige/ghost.js, star ghost_10): a Golden Button the ghost caught comes round now, if none is up. */
      ctx.on('ghost-gold', () => { if (!g && !ctx.state.ended) launch(); });

      return {
        tick(dt, t) {
          now = t;
          if (nextAt > 0) { nextAt -= dt * 1000; if (nextAt <= 0 && !g && here()) launch(); }
          if (g) {
            if (wait > 0) {
              wait -= dt;
              if (wait <= 0) ctx.anim(g, [{ opacity: 0 }, { opacity: 1 }], { duration: ctx.gentle || ctx.reduced ? 900 : 300, fill: 'forwards' });
            } else {
              /* The star falls, rises or crosses (gold-fx.js picks the path); it ends on screen. */
              gt += dt / (goldStay(ctx.prestige) * sunriseStay(ctx.prestige));  // prestige hooks: Long Gold (active_9), Sunrise ending (go_end)
              const p = path.at(gt);
              fx.place(g, p.x, p.y, gt, p.left);
              fx.motes(p.x, p.y, now, path.trail);
              if (p.done) {
                /* Missed: it winks at the edge and goes up in a puff. */
                fx.miss(g, p.x, p.y);
                g = null;
                ctx.say(pick(ctx, GOLDEN.missed));
                nextAt = gap();
              }
            }
          } else if (nextAt <= 0) {
            /* Away: no gold. Look again every few seconds until the player is back. */
            if (here()) launch(); else nextAt = 5000;
          }
          const deepLeft = Math.max(0, deepUntil - now);
          fx.frame(now, { frenzyLeft: Math.max(0, frenzyUntil - now), frenzyMs: GOLD_FRENZY_MS * frenzyMult(ctx.prestige), deepLeft, deepMs: GOLD_DEEP_MS, perSecond: deepLeft > 0 ? ctx.rates().fps : 0, fmt: ctx.fmt });
        },
      };
    },
  },

  /* Screen Time: the wellness feature. A card on a wide rail, a chip that opens it anywhere. */
  screen: {
    mount(ctx) {
      baseCss(ctx);
      const ROWS = [['taps', 'var(--accent)'], ['strays', 'var(--accent-2)'], ['ads', 'color-mix(in srgb,var(--accent) 55%,var(--ink))'], ['idle', 'var(--accent-soft)'], ['other', 'var(--ink-faint)']];
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
          const track = ctx.el('span', 'flex:1 1 auto;height:12px;background:color-mix(in srgb,var(--ink) 12%,transparent);', null, r);
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
      const host = ctx.dock({ edge: 'left', width: 230 });
      const paper = ctx.el('div', 'padding:7px;background:var(--panel);color:var(--ink);border-radius:6px;', null, host);
      paper.dataset.dockSurface = '';
      const card = build(paper);
      ctx.every(2000, () => card.draw());
      ctx.every(20000, () => { card.tip.textContent = pick(ctx, SCREEN_TIME.tips); });
      return {};
    },
  },

  /* Levels: an XP bar under the counter. XP never stops: whole levels bank up while the bar keeps filling, and one press
   * pays them all at once, with a party the size of what was left waiting. */
  levels: {
    mount(ctx) {
      ctx.css('@keyframes fz-ready { 50% { box-shadow: 0 0 0 3px var(--niche); } } .fz-lvl-ready { animation: fz-ready 1.2s ease-in-out infinite; } body.gentle .fz-lvl-ready { animation: none; }');
      let lvl = ctx.store.get('lvl') || 1, xp = ctx.store.get('xp') || 0, shownBank = -1, lastFill = 0;
      const row = ctx.el('div', 'order:4;display:flex;align-items:center;gap:6px;margin-top:3px;padding:0 6px;background:color-mix(in srgb,var(--panel) 82%,transparent);color:var(--ink);font-size:13px;pointer-events:auto;', null, ctx.layer('meters'));
      const label = ctx.el('span', 'flex:none;', '', row);
      const track = ctx.el('span', 'display:block;width:110px;height:10px;background:var(--panel);border:1px solid var(--line);border-radius:5px;overflow:hidden;', null, row);
      const fill = ctx.el('i', 'display:block;height:100%;width:0;background:linear-gradient(90deg,var(--accent),var(--accent-2));transition:width .25s linear;', null, track);
      const b = ctx.el('button', 'min-height:30px;padding:1px 8px;font-size:12px;background:var(--btn);color:var(--btn-ink,inherit);border:1px solid var(--niche);touch-action:manipulation;visibility:hidden;white-space:nowrap;', LEVELS.collect, row);
      b.type = 'button';
      b.dataset.collectLevels = '';
      const feeling = createCollectionFeel(ctx);
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
        if (ctx.paused) return;
        const got = collect(lvl, xp);
        if (!got.levels) return;
        const r = b.getBoundingClientRect(), x = r.left + r.width / 2, y = r.top;
        /* Consume the bank before notifying listeners. Even a re-entrant press cannot pay it twice. */
        lvl = got.lvl; xp = got.xp;
        ctx.store.set('lvl', lvl); ctx.store.set('xp', xp);
        const amount = ctx.pay(worth(ctx, got.seconds), 'levels');
        feeling.show({ levels: got.levels, amount, x, y });
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

  /* Mystery Boxes: fall in, wait six seconds, leave. Sparkle hooks in through the `box` event. The look and feel live in
   * box-fx.js (owner 2026-11-08): a crayon gift box that lands, idles, counts down on its paper tag, pops or poofs. */
  boxes: {
    mount(ctx) {
      let live = 0;
      const fx = createBoxFx(ctx);
      const drop = () => {
        if (live >= 3) return;
        const { w, h } = ctx.bounds();
        const x = 20 + ctx.rng() * (w - 110), y = 80 + ctx.rng() * (h * 0.5);
        live += 1;
        const box = fx.make(x, y, pick(ctx, BOXES.labels));
        const landMs = box.arrive();
        const life = 6000 + boxHoldMs(ctx.prestige);  // prestige hook: Hold On (luck_8)
        box.countdown(life);
        box.idle(landMs, life);
        let gone = false;
        if (!(ctx.gentle || ctx.reduced)) ctx.after(landMs, () => { if (!gone) sfx(ctx, 'sfx/thud.mp3', 0.15); });
        const leave = () => { gone = true; live -= 1; };
        const stop = ctx.after(life, () => {
          if (gone) return; leave(); box.expire(); ctx.say(pick(ctx, BOXES.expired));
          if (autopilotShare(ctx.prestige)) ctx.pay(worth(ctx, 12) * prizeMult(ctx.prestige) * autopilotShare(ctx.prestige), 'boxes', box.center);  // prestige hook: Autopilot (idle_6)
        });
        box.el.addEventListener('pointerdown', (e) => {
          e.preventDefault();
          if (gone) return;
          const { x: cx, y: cy } = box.center;
          stop();
          leave();
          const mouth = box.open();
          ctx.pay(worth(ctx, 12) * prizeMult(ctx.prestige), 'boxes', mouth);  // prestige hook: Lucky/Charm (luck_1/3/6/9)
          burst(ctx, mouth.x, mouth.y, { color: 'var(--accent)', n: 16, reach: 80 });
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

  wheel: { mount: mountPrizeWheel },

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
        for (const [dx, t] of [[-c.w / 2 - 22, '(('], [c.w / 2 + 22, '))']]) pop(ctx, c.x + dx, c.y, t, { color: 'var(--stamp)', size: 30, ms: 700 });
      };
      ctx.on('lucky', shake);
      if (fresh(ctx)) { shake(); ctx.toast(can ? APP_ONLY.buzz.web : APP_ONLY.buzz.off); }
      return {};
    },
  },

  /* Fake Inbox: notices from nobody, in one tab-controlled tray. They close by themselves or on a tap. */
  inbox: {
    mount(ctx) {
      let shown = 0, n = ctx.store.get('n') || 0;
      const host = ctx.dock({ edge: 'right', width: 230 });
      ctx.css(".fz-inbox:empty::before{content:'Nothing new.';font-size:12px;color:var(--ink-soft);}");
      const tray = ctx.el('div', 'display:flex;flex-direction:column;gap:3px;padding:5px;background:var(--panel);border-radius:6px;', null, host);
      tray.className = 'fz-inbox'; tray.dataset.dockSurface = '';
      const post = () => {
        const { w } = ctx.bounds();
        const cap = w < 760 ? 1 : 2;
        if (shown >= cap) return;
        const m = FAKE_INBOX[(n * 7) % FAKE_INBOX.length];
        n += 1;
        ctx.store.set('n', n);
        shown += 1;
        host.dock?.setReady(true, 'New message');
        const spot = 'position:relative;display:block;flex:none;width:100%;';
        const box = ctx.el('button', `${spot}min-height:44px;padding:5px 8px;text-align:left;background:#fff;color:#111;border:1px solid #333;border-left:5px solid var(--niche);border-radius:4px;box-shadow:0 2px 6px rgba(0,0,0,.3);font-size:12px;line-height:1.15;pointer-events:auto;touch-action:manipulation;`, null, tray);
        box.type = 'button';
        ctx.el('b', 'display:block;font-size:13px;', m.from, box);
        ctx.el('span', '', m.text, box);
        ctx.anim(box, ctx.gentle ? [{ opacity: 0 }, { opacity: 1 }] : [{ transform: 'translateX(260px)' }, { transform: 'none' }], { duration: 300, easing: 'ease-out' });
        sfx(ctx, 'sfx/snap.mp3', 0.3);
        let gone = false;
        const close = () => {
          if (gone) return;
          gone = true;
          shown -= 1; host.dock?.setReady(shown > 0, 'New message');
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
      const css = 'display:flex;flex-direction:column;justify-content:flex-end;gap:3px;max-height:150px;padding:6px;background:#17132c;color:#fff;border-radius:4px;overflow:hidden;font:500 12px/1.2 var(--game-bubble-font,"Segoe UI",system-ui,sans-serif);';
      const box = ctx.el('div', css, null, ctx.dock({ edge: 'right' }));
      const COLS = ['#ff9ccf', '#8fd4ff', '#ffe08a', '#a6f0b0'];
      const cap = 12;
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

