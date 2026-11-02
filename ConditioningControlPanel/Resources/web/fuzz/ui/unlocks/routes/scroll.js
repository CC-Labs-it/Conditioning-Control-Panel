/* Swipe Feed: a thumb game. Every swipe up is a tap, and all the rows after the first change what a swipe is worth.
 * The feed lives in a rail when there is room; on a phone it sits beside the button and opens full screen on a press. */

import { FEED_LINES } from '../../../data/content/index.js';
import { panel, deck, burst, ring, railsRoom, fill, CLIP_PICTURES } from './kit.js';
import { ROUTE_COPY } from './copy.js';

/* The one feed. d_scroll builds it; the later rows hang off it. Module state is reset when d_scroll goes. */
let feed = null;
const hooks = { swipe: [], doubleTap: [], pull: [], tile: [] };

const KINDS = ['pretty', 'close'];

function makeFeed(ctx) {
  const shell = panel(ctx, { side: 'left', height: Math.min(ctx.bounds().h - 120, 520), css: 'pointer-events:auto;' });
  const root = ctx.el('div', 'position:absolute;inset:0;background:#111;border:2px solid #222;border-radius:12px;overflow:hidden;touch-action:none;user-select:none;-webkit-user-select:none;cursor:grab;pointer-events:auto;', null, shell);
  shell.style.position = shell.style.position || 'relative';
  const col = ctx.el('div', 'position:absolute;left:0;right:0;top:0;transition:transform .28s ease-out;will-change:transform;', null, root);
  const hud = ctx.el('div', 'position:absolute;inset:0;pointer-events:none;z-index:5;', null, root);
  const hint = ctx.el('div', 'position:absolute;left:0;right:0;bottom:12px;text-align:center;font:700 13px Arial,Helvetica,sans-serif;color:#fff;text-shadow:0 1px 3px #000;', ROUTE_COPY.feedSwipe, hud);
  hint.className = 'routes-hint';

  const f = { root, shell, col, hud, tiles: [], index: 0, h: 0, swipes: 0, fullscreen: false, sheetClose: null };
  ctx.css('.routes-small .routes-big{display:none !important}.routes-full .routes-fy{top:48px !important;right:8px}.routes-full .routes-endbox{bottom:72px !important}.routes-full .routes-hint{bottom:auto !important;top:58%}');
  const sizeMode = () => { root.classList.toggle('routes-small', !railsRoom(ctx) && !f.fullscreen); hint.textContent = (!railsRoom(ctx) && !f.fullscreen) ? ROUTE_COPY.feedPress : ROUTE_COPY.feedSwipe; };
  sizeMode();
  f.sizeMode = sizeMode;

  async function fillTile(t) {
    const kind = hooks.pick ? hooks.pick() : KINDS[0];
    t.kind = kind;
    t.clip = hooks.clip ? hooks.clip() : false;
    t.seenAt = 0;
    t.liked = false;
    const pic = await ctx.pictures.next(t.clip && CLIP_PICTURES ? { niche: kind, kind: 'clip' } : { niche: kind });
    if (!t.el.isConnected) return;
    t.el.style.background = pic ? `center/cover no-repeat url("${pic.url}") #222` : 'repeating-linear-gradient(45deg,#2a2a33 0 10px,#222 10px 20px)';
    t.credit.textContent = pic?.credit || '';
    t.deco.textContent = '';
    for (const fn of hooks.tile) fn(t);
  }
  function addTile() {
    const el = ctx.el('div', `position:absolute;left:3px;right:3px;border-radius:9px;overflow:hidden;background:#222;`, null, col);
    const credit = ctx.el('div', 'position:absolute;right:4px;bottom:2px;font:10px Arial,Helvetica,sans-serif;color:#fff;text-shadow:0 1px 2px #000;', '', el);
    const deco = ctx.el('div', 'position:absolute;inset:0;pointer-events:none;', null, el);
    const t = { el, credit, deco, n: f.tiles.length ? f.tiles[f.tiles.length - 1].n + 1 : 0 };
    f.tiles.push(t);
    fillTile(t);
    if (f.tiles.length > 5) f.tiles.shift().el.remove();
    return t;
  }
  function place() {
    f.h = root.clientHeight || 100;
    for (const t of f.tiles) { t.el.style.top = `${t.n * (f.h + 4)}px`; t.el.style.height = `${f.h - 4}px`; }
    col.style.transform = `translateY(${-f.index * (f.h + 4)}px)`;
  }
  f.place = place;
  f.current = () => f.tiles.find((t) => t.n === f.index) || f.tiles[0];

  addTile(); addTile();
  ctx.after(30, place);
  ctx.every(900, () => { if (root.clientHeight !== f.h) { place(); f.sizeMode?.(); } });

  function swipe(at) {
    const was = f.current();
    if (was) for (const fn of hooks.leave || []) fn(was);
    f.index += 1;
    addTile();
    place();
    if (railsRoom(ctx) || f.fullscreen) hint.style.opacity = '0';
    f.swipes += 1;
    const gain = ctx.button.press();
    const r = root.getBoundingClientRect();
    const p = at || { x: r.left + r.width / 2, y: r.top + r.height * 0.3 };
    if (gain) ctx.float(p.x, p.y, `+${ctx.fmt(gain)}`, 'color:#ffd24d;font-weight:700;font-size:18px;');
    ctx.audio.play('assets/audio/sfx/chip_pop.mp3', { bus: 'sfx', gain: 0.3 });
    for (const fn of hooks.swipe) fn(p);
  }
  f.swipe = swipe;

  /* gestures: drag up = swipe, drag down = pull, two quick presses = like, wheel = swipe. */
  let down = null, lastTapAt = 0;
  root.addEventListener('pointerdown', (e) => {
    down = { x: e.clientX, y: e.clientY, t: performance.now(), id: e.pointerId };
    try { root.setPointerCapture(e.pointerId); } catch { /* fine */ }
    col.style.transition = 'none';
  });
  root.addEventListener('pointermove', (e) => {
    if (!down) return;
    const dy = e.clientY - down.y;
    col.style.transform = `translateY(${-f.index * (f.h + 4) + Math.max(-90, Math.min(70, dy * (dy > 0 ? 0.5 : 1)))}px)`;
    for (const fn of hooks.drag || []) fn(dy);
  });
  const up = (e) => {
    if (!down) return;
    const dy = e.clientY - down.y, dx = e.clientX - down.x, d = down;
    down = null;
    col.style.transition = 'transform .28s ease-out';
    if (dy < -28) { swipe({ x: e.clientX, y: e.clientY }); return; }
    place();
    if (dy > 46) { for (const fn of hooks.pull) fn({ x: e.clientX, y: e.clientY }); return; }
    for (const fn of hooks.drag || []) fn(0);
    if (Math.abs(dy) < 12 && Math.abs(dx) < 12 && performance.now() - d.t < 350) {
      const now = performance.now();
      if (now - lastTapAt < 340) { lastTapAt = 0; for (const fn of hooks.doubleTap) fn({ x: e.clientX, y: e.clientY }); return; }
      lastTapAt = now;
      if (!railsRoom(ctx) && !f.fullscreen) open();
    }
  };
  root.addEventListener('pointerup', up);
  root.addEventListener('pointercancel', () => { down = null; col.style.transition = 'transform .28s ease-out'; place(); });
  let wheelAt = 0;
  root.addEventListener('wheel', (e) => {
    e.preventDefault();
    const now = performance.now();
    if (e.deltaY > 0 && now - wheelAt > 260) { wheelAt = now; swipe(); }
  }, { passive: false });

  /* Full screen on a phone: same feed, moved into the over layer, with its own way back. Wake Up stays above it. */
  const back = ctx.el('button', 'position:absolute;left:50%;bottom:12px;transform:translateX(-50%);min-height:44px;padding:4px 18px;background:#fff;color:#000;border:0;border-radius:22px;font:700 15px Arial,Helvetica,sans-serif;pointer-events:auto;z-index:8;touch-action:manipulation;display:none;', ROUTE_COPY.feedBack, root);
  back.type = 'button';
  back.addEventListener('pointerdown', (e) => { e.preventDefault(); e.stopPropagation(); close(); });
  const count = ctx.el('div', 'position:absolute;left:10px;top:10px;font:700 18px Arial,Helvetica,sans-serif;color:#fff;text-shadow:0 1px 3px #000;display:none;z-index:6;', '', hud);
  function open() {
    f.fullscreen = true;
    root.style.cssText += ';position:fixed;inset:0;border-radius:0;border:0;';
    ctx.layer('over').appendChild(root);
    back.style.display = count.style.display = 'block';
    hint.style.opacity = '1';
    root.classList.add('routes-full');
    sizeMode();
    ctx.after(40, place);
  }
  function close() {
    f.fullscreen = false;
    root.style.position = 'absolute'; root.style.borderRadius = '12px'; root.style.border = '2px solid #222';
    shell.appendChild(root);
    back.style.display = count.style.display = 'none';
    root.classList.remove('routes-full');
    sizeMode();
    ctx.after(40, place);
  }
  ctx.every(200, () => { if (f.fullscreen) count.textContent = ctx.fmt(ctx.state.fuzz); });
  ctx.on('routes:corner', ({ on }) => { if (!f.fullscreen) shell.style.opacity = on ? '.15' : ''; });
  f.open = open;
  return f;
}

const resetHooks = () => { hooks.swipe = []; hooks.doubleTap = []; hooks.pull = []; hooks.tile = []; hooks.pick = null; hooks.clip = null; hooks.leave = []; hooks.drag = []; };
const listen = (ctx, name, fn) => { (hooks[name] ||= []).push(fn); void ctx; };

export default {
  /* Swipe Feed: drag up, or scroll a wheel, and the next picture comes. Each one is a tap. */
  d_scroll: {
    mount(ctx) {
      resetHooks();
      feed = makeFeed(ctx);
      if (!ctx.gentle) ctx.anim(feed.shell, [{ transform: 'translateY(30px)', opacity: 0 }, { transform: 'none', opacity: 1 }], { duration: 300, easing: 'ease-out' });
      if (!railsRoom(ctx)) ctx.toast(ROUTE_COPY.feedOpen);
      return { off() { feed = null; resetHooks(); } };
    },
  },

  /* Double-Tap Like: two quick presses on a picture, once a picture. A heart and a lump. */
  d_like: {
    mount(ctx) {
      const line = deck(FEED_LINES.liked, ctx);
      listen(ctx, 'doubleTap', (p) => {
        const t = feed?.current();
        if (!t || t.liked) return;
        t.liked = true;
        ctx.float(p.x, p.y - 20, '♥', 'color:#ff3b7a;font-size:54px;');
        burst(ctx, p.x, p.y, '#ff3b7a', 10, 70);
        const r = ctx.rates();
        ctx.pay(Math.max(r.click * 5, r.fps * 2), 'like', p);
        if (ctx.rng() < 0.35) ctx.toast(line());
        ctx.audio.play('assets/audio/sfx/chain_pop.mp3', { bus: 'sfx', gain: 0.4 });
      });
      return {};
    },
  },

  /* Video Clips: one tile in three is a muted clip with a bar, and staying for the bar pays. Real clips come later; the picture pans. */
  d_clips: {
    mount(ctx) {
      hooks.clip = () => ctx.rng() < 0.34;
      listen(ctx, 'tile', (t) => {
        if (!t.clip) return;
        ctx.el('div', 'position:absolute;left:50%;top:50%;margin:-18px 0 0 -12px;width:0;height:0;border-left:26px solid rgba(255,255,255,.8);border-top:18px solid transparent;border-bottom:18px solid transparent;', null, t.deco);
        ctx.el('div', 'position:absolute;right:6px;top:6px;font:700 11px Arial,Helvetica,sans-serif;color:#fff;text-shadow:0 1px 2px #000;', ROUTE_COPY.muted, t.deco);
        t.bar = ctx.el('div', 'position:absolute;left:0;right:0;bottom:0;height:5px;background:#ff69b4;transform-origin:0 50%;transform:scaleX(0);', null, t.deco);
        t.watch = 0;
      });
      return {
        tick(dt) {
          const t = feed?.current();
          if (!t?.clip || !t.bar || t.watched) return;
          t.watch += dt;
          t.bar.style.transform = `scaleX(${Math.min(1, t.watch / 3)})`;
          if (t.watch >= 3) {
            t.watched = true;
            const r = feed.root.getBoundingClientRect(), rate = ctx.rates();
            ctx.pay(Math.max(rate.click * 3, rate.fps * 2), 'clip', { x: r.left + r.width / 2, y: r.top + r.height / 2 });
          }
        },
      };
    },
  },

  /* For You Feed: stay on a kind of picture and you get more of that kind, and a meter owns up to it. */
  d_foryou: {
    mount(ctx) {
      const lines = deck(FEED_LINES.forYou, ctx);
      const weights = { [KINDS[0]]: 1, [KINDS[1]]: 1 };
      hooks.pick = () => (ctx.rng() < weights[KINDS[0]] / (weights[KINDS[0]] + weights[KINDS[1]]) ? KINDS[0] : KINDS[1]);
      const box = ctx.el('div', 'position:absolute;left:8px;right:8px;top:8px;display:flex;flex-direction:column;gap:3px;', null, feed.hud);
      box.className = 'routes-big routes-fy';
      const label = ctx.el('div', 'font:11px Arial,Helvetica,sans-serif;color:#f0a8cc;text-shadow:0 1px 2px #000;', lines(), box);
      const mk = (c) => { const o = ctx.el('div', 'height:6px;border-radius:3px;background:rgba(255,255,255,.2);overflow:hidden;', null, box); return ctx.el('div', `height:100%;background:${c};transform-origin:0 50%;transition:transform .4s;`, null, o); };
      const a = mk('#c23a83'), b = mk('#2f8fd0');
      const paint = () => { const s = weights[KINDS[0]] + weights[KINDS[1]]; a.style.transform = `scaleX(${weights[KINDS[0]] / s})`; b.style.transform = `scaleX(${weights[KINDS[1]] / s})`; };
      paint();
      let sat = 0;
      return {
        tick(dt) {
          sat += dt;
          const t = feed?.current();
          if (!t) return;
          if (sat >= 1.4 && !t.lingered) {
            t.lingered = true;
            weights[t.kind] += 1;
            paint();
            label.textContent = lines();
            const r = feed.root.getBoundingClientRect();
            ring(ctx, r.left + r.width / 2, r.top + r.height / 2, 120, '#fff', 500, 'over');
          }
          if (t !== feed.lastSeen) { feed.lastSeen = t; sat = 0; }
        },
      };
    },
  },

  /* Swipe Streak: keep going with under two seconds between swipes and each swipe pays more, up to x4. */
  d_streak: {
    mount(ctx) {
      const words = deck(FEED_LINES.streak, ctx), lost = deck(FEED_LINES.streakLost, ctx);
      const COL = ['#ffb36b', '#ff9a3c', '#ff6a2c', '#ff3b2c'];
      const x = ctx.el('div', 'position:absolute;left:0;right:0;top:36%;text-align:center;font:800 26px/1 Arial,Helvetica,sans-serif;color:#ffb36b;text-shadow:0 2px 6px #000;transition:font-size .2s;opacity:0;', 'x1', feed.hud);
      let n = 0, last = 0, cancel = null;
      listen(ctx, 'swipe', (p) => {
        const t = ctx.state.clock;
        n = t - last < 2000 ? n + 1 : 1;
        last = t;
        const mul = Math.min(4, 1 + Math.floor(n / 3));
        if (mul > 1) {
          const r = ctx.rates();
          ctx.pay(r.click * (mul - 1), 'streak', p);
          x.textContent = `x${mul}`;
          x.style.fontSize = `${20 + mul * 13}px`;
          x.style.color = COL[mul - 1];
          x.style.opacity = '1';
          if (n === 12) { burst(ctx, p.x, p.y, '#ff6a2c', 14, 100); ctx.toast(fill(words(), { n })); ctx.audio.play('assets/audio/sfx/streak_milestone.mp3', { bus: 'sfx', gain: 0.5 }); }
        }
        cancel?.();
        cancel = ctx.after(2000, () => {
          if (n >= 6) ctx.toast(lost());
          n = 0;
          x.style.opacity = '0';
        });
      });
      return {};
    },
  },

  /* Pull to Refresh: pull the feed down. Usually nothing. One in five pays big, so you will keep pulling. */
  d_refresh: {
    mount(ctx) {
      const miss = deck(FEED_LINES.refreshMiss, ctx), hit = deck(FEED_LINES.refreshHit, ctx);
      const sp = ctx.el('div', 'position:absolute;left:50%;top:10px;margin-left:-11px;width:22px;height:22px;border:3px solid #fff;border-top-color:transparent;border-radius:50%;opacity:0;', null, feed.hud);
      const tag = ctx.el('div', 'position:absolute;left:0;right:0;top:38px;text-align:center;font:11px Arial,Helvetica,sans-serif;color:#ddd;opacity:0;', FEED_LINES.pull, feed.hud);
      let spin = null, busy = false;
      if (!ctx.gentle) spin = ctx.anim(sp, [{ transform: 'rotate(0)' }, { transform: 'rotate(360deg)' }], { duration: 900, iterations: Infinity, easing: 'linear' });
      void spin;
      listen(ctx, 'drag', (dy) => { sp.style.opacity = String(Math.max(0, Math.min(1, dy / 46))); tag.style.opacity = sp.style.opacity; });
      listen(ctx, 'pull', (p) => {
        if (busy) return;
        busy = true;
        sp.style.opacity = '1';
        tag.style.opacity = '0';
        ctx.after(700, () => {
          busy = false;
          sp.style.opacity = '0';
          if (ctx.rng() < 0.2) {
            const r = ctx.rates();
            ctx.pay(Math.max(r.click * 40, r.fps * 12), 'refresh', p);
            burst(ctx, p.x, p.y, '#ffd24d', 16, 110);
            ctx.toast(hit());
            ctx.audio.play('assets/audio/sfx/golden_pop.mp3', { bus: 'sfx', gain: 0.6 });
          } else {
            ctx.float(p.x, p.y - 20, '+1', 'color:#aaa;font-size:12px;');
            ctx.pay(1, 'refresh');
            ctx.toast(miss());
          }
        });
      });
      return {};
    },
  },

  /* Fake Progress Bar: every swipe halves what is left of the feed, so it is always nearly done, and it never is. */
  d_end: {
    mount(ctx) {
      const near = deck(FEED_LINES.endNear, ctx);
      const box = ctx.el('div', 'position:absolute;left:8px;right:8px;bottom:30px;text-align:center;font:800 22px/1 Arial,Helvetica,sans-serif;color:#fff;text-shadow:0 1px 3px #000;', '', feed.hud);
      box.className = 'routes-big routes-endbox';
      const pct = ctx.el('div', '', '0%', box);
      const o = ctx.el('div', 'height:9px;margin-top:5px;border-radius:5px;background:rgba(255,255,255,.25);overflow:hidden;', null, box);
      const fillEl = ctx.el('div', 'height:100%;background:#ffd24d;transform-origin:0 50%;transform:scaleX(0);transition:transform .3s;', null, o);
      ctx.el('div', 'font:11px Arial,Helvetica,sans-serif;margin-top:3px;color:#ccc;', FEED_LINES.endTag, box);
      let p = ctx.store.get('p') || 0, n = 0;
      const paint = () => { pct.textContent = `${Math.min(99, Math.floor(p * 100))}%`; fillEl.style.transform = `scaleX(${Math.min(0.99, p)})`; };
      paint();
      listen(ctx, 'swipe', () => {
        p += (1 - p) * 0.25;
        if (p > 0.992) p = 0.992;
        ctx.store.set('p', p);
        paint();
        if (++n % 5 === 0) ctx.toast(near());
      });
      return {};
    },
  },
};
