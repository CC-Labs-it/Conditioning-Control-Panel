import { mountConfetti } from './confetti.js';
import { openButtonShop, buttonShopCss, buttonShopReady, grandfatherFaces, shopDoor, SIZE_ITEMS } from '../../button-shop.js';
import { writeScore } from '../../emi-score.js';
import { mountGameWave } from '../../live-wave.js';
import { mountCursorTrail } from './cursor-trail.js';
import { FACE_CSS, grandfatherCopies } from '../../bouncing-art.js';
import { freshParts, takeToasts, toastLine, SHOP_NEW_CSS } from '../../shop-gates.js';
/* ui/unlocks/basics/look.js - the look lane: what the player buys in the first ten minutes. Every entry lands visibly
 * within a second of the press. Button skins keep a rectangle to press, so any skin stays completable. */

import { TAB_TITLES } from '../../../data/content/index.js';
import { whole } from '../../../sim/format.js';
import { buttonImpulse, contactSparks } from './tap-feel.js';
import { baseCss, burst, btnCenter, chip, docOf, fresh, play, pop, PALETTES, setPalette, sfx, spawn, squish, tools } from './fx.js';

/* ---- button wardrobe: latest skin wins, the chip goes back to any owned one ---- */
import { SKIN_ITEMS, SKIN_NAMES, wardrobeSkins } from '../../button-faces.js';

function wardrobe(ctx) {
  const el = ctx.button.el;
  const showcase = el.ownerDocument.documentElement.hasAttribute('data-combined-preview');
  const options = () => wardrobeSkins(ctx.state,showcase).map(s => [s,SKIN_NAMES[s]]);
  ctx.css(buttonShopCss);
  ctx.css(FACE_CSS);
  /* Human Check was free before the shop sold it: a save wearing it keeps it. */
  const kept = grandfatherFaces({ choice: ctx.store.get('choice'), faces: ctx.store.get('faces') });
  if (kept) ctx.store.set('faces', kept);
  /* Bouncing-copy looks were free before they were sold (owner 2026-11-08): a save that picked one keeps it. */
  const keptCopies = grandfatherCopies({ bouncer: ctx.store.get('bouncer'), emiPose: ctx.store.get('emiPose'), copySize: ctx.store.get('copySize'), copyOwned: ctx.store.get('copyOwned') });
  if (keptCopies) ctx.store.set('copyOwned', keptCopies);
  /* Big and Tiny were style choices before they became sizes: an old choice moves to the size pick. */
  if (ctx.store.get('choice') === 'big' || ctx.store.get('choice') === 'tiny') { ctx.store.set('size', ctx.store.get('choice')); ctx.store.set('choice', null); }
  /* A size row bought from the shop wears its size once; the Size switch in the shop changes it after. */
  ctx.on('button-size', ({ size, bought }) => { if (bought) ctx.store.set('size', size); });
  let dialog = null;
  function open(tab = 'Styles') {
    if (dialog && !dialog.closed) return;
    dialog = openButtonShop(ctx,{
      tab, skinOptions:options,
      chooseSkin(id) {
        ctx.store.set('choice',id);
        ctx.emit('wardrobe-equipped',{skin:id});
        squish(ctx,el); bumpZone(ctx);
      },
      onClose() { dialog = null; },
    });
  }
  /* Owner 2026-11-07: the two shops are two icon doors, a button and a cursor, with a dot when something there is affordable. */
  ctx.css(SHOP_NEW_CSS);
  const b = shopDoor(ctx,'button',() => { open(); label(); });
  ctx.on('open-button-shop',({tab} = {}) => open(['Wave','Bouncing copies'].includes(tab) ? tab : 'Styles'));
  const c = shopDoor(ctx,'cursor',() => { ctx.emit('open-cursor-shop'); label(); });
  for (const door of [b, c]) { const t = ctx.el('span', '', 'NEW', door); t.className = 'fz-door-new'; t.setAttribute('aria-hidden', 'true'); }
  const doc = el.ownerDocument;
  /* Owner 2026-11-08: a shop part that just opened lights its door NEW (apart from the red ready dot) and says so once. */
  const seen = () => { let r = ctx.store.get('shopSeen'); if (!r) { r = {}; ctx.store.set('shopSeen', r); } return r; };
  const news = (door, shop) => {
    const fresh = door.hidden ? [] : freshParts(ctx.state, shop, seen());
    door.toggleAttribute('data-new', fresh.length > 0);
    door.setAttribute('aria-label', (shop === 'cursor' ? 'Cursor Shop' : 'Button Shop') + (fresh.length ? ', something new inside' : ''));
    if (!fresh.length) return;
    const told = takeToasts(ctx.state, shop, seen());
    /* After the buy's own line has had its moment. */
    if (told.length) ctx.after(3000, () => ctx.toast(toastLine(shop, told)));
  };
  const label = () => {
    b.toggleAttribute('data-ready', buttonShopReady(ctx, options().map(([s]) => s)));
    c.hidden = !(ctx.has('cursor') || ctx.has('trail'));
    c.toggleAttribute('data-ready', doc.documentElement.dataset.cursorShopReady === '1');
    news(b, 'button'); news(c, 'cursor');
  };
  ctx.on('buy', ({item,also = []}) => {
    /* A new look row takes the button; a size row only resizes it and leaves the chosen style on. */
    if ([item,...also].some(it => SKIN_ITEMS[it.id] && !SIZE_ITEMS[it.id])) ctx.store.set('choice',null);
  });
  ctx.every(1000, label);
  label();
}

/** A skin that changes the button zone changes how many shop rows fit: let the shop measure again. */
function bumpZone(ctx) {
  ctx.after(450, () => docOf(ctx).defaultView.dispatchEvent(new Event('resize')));
}

/** The skin arrives with a squash and a little party, sized to the buy. */
function wear(ctx, name, css) {
  baseCss(ctx);
  if (css) ctx.css(css);
  ctx.button.skin(name);
  if (fresh(ctx)) {
    const c = btnCenter(ctx);
    squish(ctx, ctx.button.el);
    burst(ctx, c.x, c.y, { n: 12, reach: 70 });
    sfx(ctx, 'sfx/ui_equip.mp3', 0.5);
  }
}

/* ---- a page wipe from the button ---- */
function wipe(ctx, color, then) {
  if (ctx.gentle || !fresh(ctx)) { then(); return; }
  const c = btnCenter(ctx), { w, h } = ctx.bounds();
  const r = Math.hypot(Math.max(c.x, w - c.x), Math.max(c.y, h - c.y));
  const d = spawn(ctx, `inset:0;background:${color};`, null, 'back');
  const a = play(ctx, d, [{ clipPath: `circle(0px at ${c.x}px ${c.y}px)` }, { clipPath: `circle(${r}px at ${c.x}px ${c.y}px)` }], { duration: 800, easing: 'ease-out', fill: 'forwards' });
  a.finished?.then(then, then);
}

export default {
  /* Round Corners: the first change the button ever gets. */
  rounded: {
    mount(ctx) {
      wear(ctx, 'round', '#btn[data-skin=round] { border-radius: 16px; }');
      ctx.on('tap', ({ auto }) => {
        if (!auto) buttonImpulse(ctx);
      });
      return {};
    },
  },

  /* Disguised Tab: the real tab title goes boring, and a little tab flies up so the buy is seen on screen too. */
  title: {
    mount(ctx) {
      const doc = docOf(ctx), was = doc.title;
      const icon = ctx.el('link', '', null, doc.head);
      icon.rel = 'icon';
      icon.href = 'data:image/svg+xml,' + encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16"><rect width="16" height="16" rx="2" fill="#1d7a45"/><path d="M4 4l8 8M12 4l-8 8" stroke="#fff" stroke-width="2"/></svg>');
      let i = ctx.store.get('i') || 0;
      /* Inside the site (app.cclabs.app/tc) this page is a frame, so its own title and icon never reach the browser
         tab: the host is asked to wear them (CONTRACT `tab`; icon is a name from the host's fixed set, never a URL). */
      const set = () => { doc.title = TAB_TITLES[i % TAB_TITLES.length]; ctx.store.set('i', i); if (ctx.hosted) ctx.host('tab', { title: doc.title, icon: 'sheet' }); };
      set();
      ctx.every(60000, () => { i += 1; set(); });
      if (fresh(ctx)) {
        const tab = spawn(ctx, 'left:50%;top:6px;margin-left:-110px;width:220px;padding:6px 10px;background:#d4d0c8;color:#000;border:1px solid #6e6a62;border-radius:7px 7px 0 0;font:600 14px "Segoe UI",system-ui,sans-serif;white-space:nowrap;overflow:hidden;', '');
        const sq = docOf(ctx).createElement('i');
        sq.style.cssText = 'display:inline-block;width:12px;height:12px;background:#1d7a45;border-radius:2px;margin-right:6px;vertical-align:-1px;';
        tab.append(sq, TAB_TITLES[0]);
        play(ctx, tab, [{ transform: 'translateY(-44px)' }, { transform: 'translateY(0)', offset: 0.12 }, { transform: 'translateY(0)', offset: 0.82 }, { transform: 'translateY(-44px)' }], { duration: 2200, easing: 'ease-in-out' });
      }
      // Off: the page's own title and icon come back (the disguise link goes, the original favicon stays).
      return { off() { doc.title = was; icon.remove(); if (ctx.hosted) ctx.host('tab', { title: null, icon: null }); } };
    },
  },

  /* Floating Numbers */
  float: {
    mount(ctx) {
      const shoot = (gain, x, y) => {
        const size = Math.min(30, 15 + Math.log10(Math.max(1, gain)) * 3);
        ctx.float(x + (ctx.rng() - 0.5) * 60, y - 18, `+${ctx.fmt(gain)}`, `color:var(--stamp);font-weight:700;font-size:${size}px;`);
      };
      ctx.on('tap', ({ gain, x, y }) => {
        if (x == null) { const c = btnCenter(ctx); shoot(gain, c.x, c.y); } else shoot(gain, x, y);
      });
      if (fresh(ctx)) { const c = btnCenter(ctx); for (let i = 0; i < 3; i++) ctx.after(i * 90, () => shoot(ctx.rates().click, c.x, c.y)); }
      return {};
    },
  },

  /* Colour: the grey wipes away from the button. */
  colour: {
    mount(ctx) {
      baseCss(ctx);
      ctx.stage.register('wash');   // the page is the theme: never dimmed, but its buy gets the solo
      ctx.on('tap', (event) => contactSparks(ctx, event));
      ctx.css(`
body { transition: background-color .6s, color .6s; }
#btn { background: var(--btn-fill); border: 2px solid var(--btn-edge); box-shadow: 0 3px 0 var(--btn-edge); font-weight: 600; }
#btn.down { border-style: solid; box-shadow: none; transform: translateY(3px) scale(.98); }
.row { border-radius: 6px; }
#goal .row { border-color: var(--btn-edge); }
`);
      if (ctx.pick('f5')) {
        /* The grey is long gone: bought after a theme, Colour gives the page more of the theme, in the same wipe. */
        const rich = { '--niche-wash-now': '30%' };
        wipe(ctx, 'color-mix(in srgb, var(--mix) 26%, var(--page))', () => ctx.vars(rich));
      } else {
        const { '--page': page, ...rest } = PALETTES.pink;
        ctx.vars(rest);
        wipe(ctx, page, () => { if (!ctx.pick('f5')) ctx.vars({ '--page': page }); });
        /* Bought before the theme: the pick comes along and takes the page, so the richer wash waits for it. */
        ctx.on('fork', ({ fork }) => { if (fork === 'f5') ctx.vars({ '--niche-wash-now': '30%' }); });
      }
      if (fresh(ctx)) { sfx(ctx, 'sfx/reveal_chime.mp3', 0.6); squish(ctx, ctx.button.el); }
      return {};
    },
  },

  /* Wall Pop: every wall gets a small burst away from the wall, and a soft pop. */
  wallpop: {
    mount(ctx) {
      const pops = ['pop/Pop.mp3', 'pop/Pop2.mp3', 'pop/Pop3.mp3'];
      let lastPop = -Infinity;
      const blast = (x, y, n = 8) => {
        const { w, h } = ctx.bounds();
        const hit = [x < 50 ? 0 : null, x > w - 50 ? Math.PI : null, y < 30 ? Math.PI / 2 : null, y > h - 30 ? -Math.PI / 2 : null].find((a) => a != null);
        const a = hit ?? 0;
        burst(ctx, Math.max(0, Math.min(w, x)), Math.max(0, Math.min(h, y)), { n, reach: 58, size: 8, from: a - 1.3, to: a + 1.3 });
      };
      ctx.on('wall', ({ x, y, corner }) => {
        blast(x, y);
        const clock = ctx.state.clock;
        if (clock - lastPop > (corner ? 450 : 1100)) { lastPop = clock; sfx(ctx, pops[Math.floor(clock / 1100) % pops.length], corner ? .16 : .1); }
      });
      /* Two copies touching after a spiral (ui/unlocks/basics.js): a small ring of bits and the plain wall pop, same cooldown. */
      ctx.on('stray:bump', ({ x, y }) => {
        burst(ctx, x, y, { n: 6, reach: 40, size: 6, from: 0, to: Math.PI * 2 });
        const clock = ctx.state.clock;
        if (clock - lastPop > 1100) { lastPop = clock; sfx(ctx, pops[Math.floor(clock / 1100) % pops.length], .1); }
      });
      if (fresh(ctx)) { const { w, h } = ctx.bounds(); blast(0, h * 0.4, 12); blast(w, h * 0.6, 12); blast(w * 0.5, 0, 12); }
      return {};
    },
  },

  /* Nicer Font: loads first, so the swap is one soft change and not a flash of fallback text. */
  font: {
    mount(ctx) {
      const doc = docOf(ctx);
      let mounted = true;
      const swap = () => {
        if (!mounted) return;
        doc.documentElement.setAttribute('data-bubble-type', '');
        if (fresh(ctx)) for (const part of [ctx.parts.shop, ctx.parts.stage]) ctx.anim(part, [{ opacity: 0.2 }, { opacity: 1 }], { duration: 400 });
      };
      ctx.cleanup(() => {
        mounted = false;
        doc.documentElement.removeAttribute('data-bubble-type');
      });
      if (doc.fonts?.load) doc.fonts.load('700 16px ShopRound').then(swap, swap);
      else swap();
      return {};
    },
  },

  confetti: { mount: mountConfetti },

  /* Bubble Button: the button and its copies. A bubble pops when pressed. The wardrobe chip lives here: swap back any time. */
  bubble: {
    mount(ctx) {
      wear(ctx, 'bubble');
      wardrobe(ctx);
      ctx.frame(mountGameWave(ctx,true));
      ctx.on('tap', ({ x, y, auto }) => {
        if (ctx.button.el.dataset.skin !== 'bubble') return;
        if (!auto) sfx(ctx, `pop/Pop${['', '2', '3'][Math.floor(ctx.state.clock / 300) % 3]}.mp3`, .16);
        const c = x == null ? btnCenter(ctx) : { x, y };
        burst(ctx, c.x, c.y, { color: 'url("assets/img/bubble.png") center/139% no-repeat', n: 12, reach: 52, size: 8 });
      });
      return {};
    },
  },

  /* Pink Cursor: a pink arrow, and a ring wherever anyone presses, finger or mouse. */
  cursor: {
    mount(ctx) {
      const win = docOf(ctx).defaultView;
      const ring = (x, y) => {
        const d = spawn(ctx, `width:44px;height:44px;margin:-22px 0 0 -22px;box-sizing:border-box;border:3px solid var(--accent);border-radius:50%;left:${x}px;top:${y}px;`);
        play(ctx, d, [{ transform: 'scale(.3)', opacity: 1 }, { transform: 'scale(1.1)', opacity: 0 }], { duration: ctx.gentle ? 400 : 480, easing: 'ease-out' });
      };
      const onDown = (e) => { if (!ctx.paused && ctx.state.store.cursorKit?.rings !== false) ring(e.clientX, e.clientY); };
      win.addEventListener('pointerdown', onDown, true);
      if (fresh(ctx)) { const c = btnCenter(ctx); ring(c.x, c.y); ctx.after(180, () => ring(c.x, c.y)); }
      return { off() { win.removeEventListener('pointerdown', onDown, true); } };
    },
  },

  /* Rolling Counter: odometer digits over the real count, which stays in place for Gentle. */
  counter: {
    mount(ctx) {
      const doc = docOf(ctx);
      ctx.css(`
#count { display: none; }
body.gentle #count { display: inline; }
body.tb-hidecount #count:not([hidden]) { display: inline-block; }
body.gentle #fz-roll, body.tb-hidecount #fz-roll, #counter:has(#count[hidden]) #fz-roll { display:none; }
#fz-roll { display:inline-flex; align-items:baseline; white-space:nowrap; }
#fz-roll > .d { display:inline-block; min-width:.62em; text-align:center; }
`);
      const counter = ctx.parts.counter;
      const roll = ctx.el('span', '', null, counter);
      roll.id = 'fz-roll';
      counter.insertBefore(roll, counter.querySelector('#count'));
      const columns = [];
      let shown = '';
      const draw = () => {
        const text = whole(ctx.state.fuzz);
        if (text === shown) return;
        const resized = text.length !== shown.length;
        const time = doc.defaultView.performance.now();
        if (resized) {
          for (const column of columns) column.motion?.cancel();
          columns.length = 0;
          const fragment = doc.createDocumentFragment();
          for (let i = 0; i < text.length; i++) {
            const el = doc.createElement('span');
            fragment.appendChild(el);
            columns.push({ el, changedAt: time, motion: null });
          }
          roll.replaceChildren(fragment);
        }
        for (let i = 0; i < text.length; i++) {
          const ch = text[i], previous = shown[i];
          if (!resized && ch === previous) continue;
          const column = columns[i];
          column.motion?.cancel();
          column.motion = null;
          const isDigit = ch >= '0' && ch <= '9';
          column.el.className = isDigit ? 'd' : 'c';
          // One live glyph per place. Only a steady, adjacent lower digit rolls in.
          writeScore(column.el, ch, i === text.length-1 && counter.hasAttribute('data-et-score'));
          if (i > 0 && !resized && isDigit && Math.abs(Number(ch) - Number(previous)) === 1 &&
              time - column.changedAt >= 180 && !ctx.gentle && !ctx.reduced) {
            const dy = ch > previous ? '.18em' : '-.18em';
            column.motion = ctx.anim(column.el, [{ transform: `translateY(${dy})` }, { transform: 'translateY(0)' }],
              { duration: 125, easing: 'ease-out' });
          }
          column.changedAt = time;
        }
        shown = text;
      };
      draw();
      ctx.every(70, draw);
      return {};
    },
  },

  /* Fine dust and small sparkles share one bounded canvas pool. */
  trail: { mount: mountCursorTrail },
  starburst: { mount: mountCursorTrail },
  word_comet: { mount: mountCursorTrail },

  /* Page Colour: four pots. Buying it moves off pink at once, so the pots explain themselves. */
  paint: {
    mount(ctx) {
      baseCss(ctx);
      ctx.stage.register('wash');
      ctx.css(`.fz-pot { width: 34px; min-height: 34px; padding: 0; border: 0; background: transparent; touch-action: manipulation; pointer-events: auto; } .fz-pot i { display: block; width: 24px; height: 24px; margin: auto; border-radius: 50%; border: 2px solid #fff; box-shadow: 0 0 0 1px rgba(0,0,0,.5); transition: scale .15s; } .fz-pot[aria-pressed=true] i { scale: 1.25; }`);
      const row = ctx.el('span', 'display:inline-flex;gap:0;', null, tools(ctx));
      const dots = {};
      const paint = (name, by = true) => {
        setPalette(ctx, name);
        ctx.store.set('name', name);
        for (const [k, d] of Object.entries(dots)) d.setAttribute('aria-pressed', String(k === name));
        if (by) ctx.emit('page-colour', { name });
      };
      /* After a pick a fifth pot goes back to the theme's own page. */
      const pots = [['pink', '#ff69b4'], ['violet', '#7b5cff'], ['teal', '#16a596'], ['black', '#111']];
      if (ctx.pick('f5')) pots.unshift(['theme', 'var(--btn-fill)']);
      for (const [name, col] of pots) {
        const b = ctx.el('button', '', null, row);
        b.type = 'button';
        b.className = 'fz-pot';
        b.setAttribute('aria-label', `Page colour ${name}`);
        const i = docOf(ctx).createElement('i');
        i.style.background = col;
        b.appendChild(i);
        b.addEventListener('click', () => { paint(name); squish(ctx, b); sfx(ctx, 'sfx/ui_equip.mp3', 0.4); });
        dots[name] = b;
        if (fresh(ctx)) ctx.anim(b, [{ scale: 0 }, { scale: 1.3, offset: 0.7 }, { scale: 1 }], { duration: 320, delay: 80 * Object.keys(dots).length, fill: 'backwards' });
      }
      const pick = ctx.store.get('name');
      paint(pick || (ctx.pick('f5') === 'trance' ? 'teal' : 'violet'), false);
      /* A theme picked after the pots dresses the page: the player's pot goes back on top of it. */
      ctx.on('fork', ({ fork }) => { if (fork === 'f5') ctx.after(0, () => paint(ctx.store.get('name') || 'violet', false)); });
      return {};
    },
  },

  /* Dark Mode: goes on when bought, and a switch stays for changing your mind. A colour pot switches it back off. */
  dark: {
    mount(ctx) {
      baseCss(ctx);
      let on = ctx.store.get('on') ?? true, base = null;
      const set = (v) => {
        on = v;
        ctx.store.set('on', on);
        b.textContent = on ? 'Dark: on' : 'Dark: off';
      };
      const b = chip(ctx, '', () => {
        set(!on);
        if (on) setPalette(ctx, 'dark'); else setPalette(ctx, base || (ctx.pick('f5') ? 'theme' : 'pink'));
        sfx(ctx, 'sfx/ui_equip.mp3', 0.4);
      });
      ctx.on('page-colour', ({ name }) => { base = name; if (on) set(false); });
      set(on);
      ctx.after(0, () => { if (on) setPalette(ctx, 'dark'); });
      return {};
    },
  },

  /* Corner Bonus: glowing corners, a big payout. The bouncer snaps to a true corner when it comes close. */
  corner: {
    mount(ctx) {
      const glow = (css, at) => ctx.el('div', `position:absolute;${css};width:90px;height:90px;background:radial-gradient(circle at ${at},rgba(240,194,75,.75),transparent 70%);pointer-events:none;`, null, 'wash');
      const spots = [glow('left:0;top:0', '0 0'), glow('right:0;top:0', '100% 0'), glow('left:0;bottom:0', '0 100%'), glow('right:0;bottom:0', '100% 100%')];
      if (!ctx.gentle) for (const s of spots) ctx.anim(s, [{ opacity: 0.45 }, { opacity: 1 }], { duration: 1600, iterations: Infinity, direction: 'alternate', easing: 'ease-in-out' });
      const party = (x, y, paid) => {
        const { w, h } = ctx.bounds();
        const cx = x < w / 2 ? 0 : w, cy = y < h / 2 ? 0 : h;
        const aim = Math.atan2(h / 2 - cy, w / 2 - cx);
        burst(ctx, cx, cy, { color: '#f0c24b', n: 24, reach: 150, size: 9, ms: 900, from: aim - 1, to: aim + 1 });
        if (paid) pop(ctx, Math.max(70, Math.min(w - 70, cx)), Math.max(60, Math.min(h - 60, cy)), `+${ctx.fmt(paid)}`, { color: '#c79400', size: 44, ms: 1300 });
      };
      ctx.on('wall', ({ corner, x, y }) => {
        if (!corner) return;
        const got = ctx.pay(ctx.rates().wall * 9, 'corner', { x, y });
        ctx.emit('cornered', { x, y });
        party(x, y, got);
        sfx(ctx, 'sfx/pb_fanfare.mp3', 0.5);
      });
      if (fresh(ctx)) { const { w } = ctx.bounds(); party(0, 0); ctx.after(220, () => party(w, 0)); }
      return {};
    },
  },
};
