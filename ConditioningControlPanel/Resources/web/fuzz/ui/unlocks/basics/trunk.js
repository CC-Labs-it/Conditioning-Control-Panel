/* ui/unlocks/basics/trunk.js - the main path: forks stamp, Lucky Taps, the ticker, achievements, the ad banner,
 * glossy icons, spiral overlay, page tilt, the fake desktop, the warp and the Drop. */

import { moan } from '../moan.js';
import { ACHIEVEMENTS, DROP_WORDS, FAKE_DESKTOP, LUCKY_WORDS, THEMES, THEME_IDS, thoughtsFor } from '../../../data/content/index.js';
import { FORKS } from '../../../data/items.js';
import { SECOND_THEME } from './copy.js';
import { THEME_PALETTES } from '../../palette.js';
import { burst, btnCenter, chip, docOf, fresh, play, pop, sfx, spawn, spiralCanvas, squish } from './fx.js';
import { THEME_TASTES } from '../theme-skins.js';

/* A fork lands like a stamp: the choice crosses the middle of the screen. */
const stampFork = {
  mount(ctx) {
    if (!fresh(ctx)) return {};
    const lane = ctx.pick(ctx.item.id);
    const c = btnCenter(ctx);
    const label = FORKS[ctx.item.id]?.opts?.[lane]?.name || ctx.item.name;
    pop(ctx, ctx.bounds().w / 2, c.y, label, { size: 58, ms: 1400 });
    burst(ctx, c.x, c.y, { n: 24, reach: 190, size: 9, ms: 900 });
    sfx(ctx, 'sfx/unlock_card.mp3', 0.6);
    return {};
  },
};

const stageOf = (s) => (s.clock > 18 * 60000 ? 'late' : s.clock > 8 * 60000 ? 'mid' : 'early');

/* ---- achievements: conditions read from state and events, kept in the store ---- */
function achieved(st, seen) {
  return ACHIEVEMENTS.filter((a) => {
    if (seen.has(a.id)) return false;
    const [what, n] = a.when.split(':');
    const need = Number(n);
    switch (what) {
      case 'taps': return st.taps >= need;
      case 'buys': return st.buys >= need;
      case 'total': return st.total >= need;
      case 'minutes': return st.clock >= need * 60000;
      case 'forks': return Object.keys(st.picks).length >= need;
      default: return (st.tally[what] || 0) >= need;
    }
  });
}

export default {
  f1: stampFork, f2: stampFork, f3: stampFork, f4: stampFork, f5: stampFork,

  /* Lucky Taps: about one in twenty-five, never more than forty dry. Word on the button, what it paid, and a `lucky` event for others. */
  softw: {
    mount(ctx) {
      const el = ctx.button.el;
      let dry = 0, unhide = () => {};
      const word = (c) => {
        unhide();
        el.style.color = 'transparent';
        unhide = ctx.after(850, () => { el.style.color = ''; });
        pop(ctx, c.x, c.y, LUCKY_WORDS[Math.floor(ctx.rng() * LUCKY_WORDS.length)], { color: 'var(--ink)', size: 28, ms: 900 });
      };
      ctx.on('tap', ({ x, y, auto }) => {
        dry += 1;
        if (ctx.rng() > 0.04 && dry < 40) return;
        dry = 0;
        const c = btnCenter(ctx);
        const at = x == null ? c : { x, y };
        const got = ctx.pay(ctx.rates().click * 7, 'lucky', at);
        word(c);
        ctx.float(c.x + c.w / 2 + 24, c.y - 12, `+${ctx.fmt(got)}`, 'color:var(--stamp);font-size:22px;font-weight:700;');
        if (!auto) squish(ctx, el, 0.8);
        sfx(ctx, 'sfx/chip_pop.mp3', 0.45);
        if (!auto) moan(ctx);
        ctx.emit('lucky', { x: at.x, y: at.y, auto });
      });
      if (fresh(ctx)) word(btnCenter(ctx));
      return {};
    },
  },

  /* Scrolling Thoughts: a strip at the top that never stops losing its place. */
  ticker: {
    mount(ctx) {
      ctx.css(`#l-ticker { left: 0; right: 96px; top: 10px; height: 24px; line-height: 24px; background: var(--panel); border-top: 1px solid var(--line); border-bottom: 1px solid var(--line); -webkit-mask-image: linear-gradient(to right, transparent 0, #000 12%); mask-image: linear-gradient(to right, transparent 0, #000 12%); }`);
      let n = ctx.store.get('n') || 0;
      const next = () => {
        const st = ctx.state;
        const list = thoughtsFor({ stage: stageOf(st), routes: Object.values(st.picks), themes: [st.picks.f5].filter(Boolean) });
        // A shuffled walk through the list, stable for the run, so nothing repeats soon.
        ctx.say(list[(n * 37 + (st.seed % 97)) % list.length]);
        n += 1;
        ctx.store.set('n', n);
      };
      next();
      ctx.every(9000, next);
      return {};
    },
  },

  /* Achievements: stars fly into a ten cell meter. Each full meter is +10% of everything. */
  depth: {
    mount(ctx) {
      const earned = new Set(ctx.store.get('earned') || []);
      const tally = ctx.store.get('tally') || {};
      const note = (k) => { tally[k] = (tally[k] || 0) + 1; ctx.store.set('tally', tally); };
      const row = ctx.el('div', 'order:2;display:flex;gap:3px;margin-top:3px;', null, ctx.layer('meters'));
      const cells = Array.from({ length: 10 }, () => ctx.el('i', 'display:block;width:15px;height:9px;background:var(--panel-short);border:1px solid var(--line);transition:background .25s;', null, row));
      const paint = () => { const on = earned.size % 10; cells.forEach((c, i) => { c.style.background = i < on ? 'var(--stamp)' : 'var(--panel-short)'; }); };
      const mult = () => 1 + Math.floor(earned.size / 10) * 0.1;
      ctx.mod({ fps: (v) => v * mult(), click: (v) => v * mult() });
      paint();

      const queue = [];
      let showing = false;
      const card = (a) => {
        showing = true;
        const c = spawn(ctx, 'left:max(8px,calc(50vw - 165px));top:0;width:min(330px,calc(100vw - 108px));padding:4px 10px;text-align:center;background:var(--toast-bg);color:var(--toast-ink);border-radius:6px;font-size:15px;line-height:1.2;box-shadow:0 3px 10px rgba(0,0,0,.35);', `★ ${a.name}`);
        const sub = docOf(ctx).createElement('div');
        sub.style.cssText = 'font-size:13px;opacity:.8;';
        sub.textContent = ctx.brand.text(a.line);
        c.appendChild(sub);
        const y = -38;
        const an = play(ctx, c, [{ transform: `translateY(${y - 40}px)`, opacity: 0 }, { transform: `translateY(${y + 40}px)`, opacity: 1, offset: 0.1 }, { transform: `translateY(${y + 40}px)`, opacity: 1, offset: 0.9 }, { transform: `translateY(${y + 26}px)`, opacity: 0 }], { duration: 2300, easing: 'ease-out' });
        const done = () => { showing = false; pump(); };
        an.finished?.then(done, done);
      };
      const pump = () => {
        if (showing || !queue.length) return;
        const a = queue.shift();
        card(a);
        earned.add(a.id);
        ctx.store.set('earned', [...earned]);
        const r = row.getBoundingClientRect();
        ctx.float(r.left + r.width / 2, r.top, '★', 'color:var(--stamp);font-size:20px;');
        sfx(ctx, 'sfx/chime2.mp3', 0.4);
        paint();
        if (earned.size % 10 === 0) {
          sfx(ctx, 'sfx/rank_up.mp3', 0.6);
          pop(ctx, r.left + r.width / 2, r.bottom + 24, '+10%', { size: 30, ms: 1400 });
          burst(ctx, r.left + r.width / 2, r.top, { n: 18, reach: 90 });
        }
      };
      ctx.on('wall', ({ corner }) => { if (corner) note('corner'); });
      ctx.on('golden', () => note('golden'));
      ctx.on('resume', () => note('resume'));
      ctx.on('ad', (d) => { note('adclick'); if (d?.ccp) note('ccpclick'); });
      ctx.on('answer', () => note('answers'));
      ctx.on('refused', () => note('refusals'));
      let idle = 0, lastTaps = ctx.state.taps;
      const check = () => {
        const st = ctx.state;
        if (st.taps !== lastTaps) { lastTaps = st.taps; idle = 0; }
        else { idle += 1; if (idle >= 30 && !tally.rest) { tally.rest = 30; } if (idle >= 120) tally.rest = 120; }
        for (const a of achieved({ taps: st.taps, buys: st.buys, total: st.total, clock: st.clock, picks: st.picks, tally }, earned)) {
          if (!queue.includes(a)) queue.push(a);
        }
        pump();
      };
      check();
      ctx.every(1000, check);
      return {};
    },
  },

  /* Ad Banner: rotates jokes and real cards. One payment per ad, for the player's own press. A real card links to the site. */
  banner: {
    mount(ctx) {
      ctx.css(`
.fz-ad { display: flex; align-items: center; gap: 8px; width: 100%; min-height: 46px; margin: 4px 0; padding: 4px 10px; background: #fffbe4; color: #16120c; border: 1px solid #000; text-align: left; font: 600 14px/1.15 "Segoe UI", system-ui, sans-serif; pointer-events: auto; touch-action: manipulation; }
.fz-ad u { flex: none; padding: 1px 4px; background: #f0c24b; color: #000; font-size: 10px; text-decoration: none; }
.fz-ad .t { flex: 1 1 auto; min-width: 0; }
.fz-ad .t small { display: block; font-weight: 400; font-size: 12px; opacity: .85; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.fz-ad .c { flex: none; padding: 4px 8px; background: #000; color: #fff; font-size: 12px; white-space: nowrap; }
.fz-ad.ccp { background: #2a0f22; color: #fff; }
.fz-ad.ccp .c { background: var(--accent); color: var(--niche-ink); }
.fz-ad.scam { background: #fff; color: #b00020; } .fz-ad.wellness { background: #e8f6ee; color: #114d33; } .fz-ad.pharma { background: #e7f1fb; color: #123a66; } .fz-ad.desperate { background: #ffe4dc; color: #6a1b00; } .fz-ad.corporate { background: #eceff3; color: #1d2733; } .fz-ad.local { background: #fff0f7; color: #7a0f4a; }
`);
      const slot = ctx.el('button', 'display:none;', null, ctx.layer('banner'));
      slot.type = 'button';
      let ad = null, paid = false;
      const show = () => {
        ad = ctx.ads.next({ kind: 'banner' });
        paid = false;
        const doc = docOf(ctx);
        slot.className = `fz-ad ${ad.ccp ? 'ccp' : ad.tone || ''}`;
        slot.style.display = '';
        slot.replaceChildren();
        const u = doc.createElement('u'); u.textContent = 'AD';
        const t = doc.createElement('span'); t.className = 't'; t.textContent = ctx.brand.text(ad.headline || ad.text || '');
        const s = doc.createElement('small'); s.textContent = ctx.brand.text(ad.sub || ''); t.appendChild(s);
        const c = doc.createElement('span'); c.className = 'c'; c.textContent = ctx.brand.text(ad.cta || 'Look');
        slot.append(u, t, c);
        ctx.anim(slot, ctx.gentle ? [{ opacity: 0 }, { opacity: 1 }] : [{ transform: 'translateY(30px)', opacity: 0 }, { transform: 'none', opacity: 1 }], { duration: 350, easing: 'ease-out' });
      };
      slot.addEventListener('click', (e) => {
        if (!ad) return;
        if (!paid) {
          paid = true;
          ctx.pay(ctx.rates().fps * ctx.config.AD_CLICK_PAYS_SECONDS, 'ad', { x: e.clientX, y: e.clientY });
          sfx(ctx, 'sfx/dling.mp3', 0.5);
        }
        ctx.emit('ad', { ccp: !!ad.ccp, id: ad.id });
        if (ad.url) ctx.openLink(ad.url);
      });
      show();
      ctx.every(13000, show);
      ctx.on('ads', ({ jokesOff }) => { if (jokesOff && ad && !ad.ccp) show(); });
      return {};
    },
  },

  /* Glossy Icons: the same icons, in a shiny tile. Art comes later behind ui/icons.js; the tile stays. */
  glossy: {
    mount(ctx) {
      ctx.css(`.row .ico { display: grid; place-items: center; width: 30px; height: 30px; font-size: 19px; border-radius: 8px; background: radial-gradient(circle at 32% 24%, #fff 0 12%, color-mix(in srgb, var(--hue-a, #ff69b4) 60%, #fff) 34%, color-mix(in srgb, var(--hue-b, #ff4fa8) 78%, #000) 92%); box-shadow: 0 2px 5px rgba(0,0,0,.4), inset 0 -2px 4px rgba(0,0,0,.25); }`);
      if (fresh(ctx) && !ctx.gentle) {
        ctx.parts.shop.querySelectorAll('.row .ico').forEach((ic, i) => ctx.anim(ic, [{ transform: 'rotateY(90deg)' }, { transform: 'rotateY(0)' }], { duration: 360, delay: i * 80, fill: 'backwards' }));
      }
      return {};
    },
  },

  /* Second Theme: pick one other theme at the card. Its button and its button words come along, and a small frame shows
   * pictures asked for under that theme's name. The chip goes back to the card until a pick is made. */
  taste2: {
    mount(ctx) {
      let frame = null;
      const wearIt = (id) => {
        const t = THEME_TASTES[id];
        if (!t) return;
        ctx.css(t.css);
        ctx.button.skin(t.skin);
      };
      const showPicture = async (id) => {
        if (!frame) return;
        const niches = THEME_TASTES[id]?.niches || [id];
        const pic = await ctx.pictures.next({ niche: niches[Math.floor(ctx.rng() * niches.length)] });
        if (!frame) return;
        frame.img.style.display = pic ? 'block' : 'none';
        if (pic) { frame.img.src = pic.url; ctx.anim(frame.img, [{ opacity: 0 }, { opacity: 1 }], { duration: 500 }); }
        frame.credit.textContent = pic?.credit || '';
      };
      const buildFrame = (id) => {
        const box = ctx.el('div', 'position:relative;width:100%;max-width:190px;height:150px;margin:0 auto;border:2px dashed var(--line);background:var(--panel);overflow:hidden;', null, ctx.dock({ edge: 'left', title: THEMES[id].label }));
        const img = ctx.el('img', 'display:none;width:100%;height:100%;object-fit:cover;', null, box);
        img.alt = '';
        const credit = ctx.el('span', 'position:absolute;right:3px;bottom:2px;font-size:10px;color:#fff;text-shadow:0 1px 2px #000;', '', box);
        frame = { box, img, credit };
        showPicture(id);
      };
      let ask = null;
      const apply = (id, arriving) => {
        ctx.store.set('theme', id);
        ctx.emit('taste2', { theme: id });
        wearIt(id);
        buildFrame(id);
        ask?.remove();
        if (arriving) {
          ctx.toast(SECOND_THEME.chosen.replace('{theme}', THEMES[id].label));
          const c = btnCenter(ctx);
          squish(ctx, ctx.button.el, 1.3);
          burst(ctx, c.x, c.y, { color: THEME_PALETTES[id]?.a, n: 18, reach: 110, size: 8 });
          sfx(ctx, 'sfx/ui_unlock.mp3', 0.5);
        }
      };
      const openCard = () => {
        const others = THEME_IDS.filter((id) => id !== ctx.pick('f5'));
        ctx.modal({ noDefault: true, title: SECOND_THEME.title, body: SECOND_THEME.ask, buttons: others.map((id) => ({ label: THEMES[id].label, onPress: () => apply(id, true) })) });
      };
      const chosen = ctx.store.get('theme');
      if (chosen) apply(chosen, false);
      else {
        ask = chip(ctx, SECOND_THEME.chip, openCard);
        if (fresh(ctx)) openCard();
      }
      ctx.every(14000, () => { const id = ctx.store.get('theme'); if (id) showPicture(id); });
      ctx.on('pictures', () => { const id = ctx.store.get('theme'); if (id) showPicture(id); });
      ctx.on('lucky', () => {
        const id = ctx.store.get('theme');
        const list = id ? THEMES[id]?.buttonWords || [] : [];
        if (!list.length) return;
        const c = btnCenter(ctx);
        pop(ctx, c.x, c.y - c.h, list[Math.floor(ctx.rng() * list.length)], { size: 20, ms: 900 });
      });
      return { off() { frame = null; } };
    },
  },

  /* Spiral Overlay: faint, over everything, ignores the pointer. One still frame of the Loom, turned by the compositor. */
  overlay: {
    mount(ctx) {
      const sp = spiralCanvas(ctx, 'position:absolute;left:50%;top:50%;width:150vmax;height:150vmax;margin:-75vmax 0 0 -75vmax;opacity:0;pointer-events:none;', 'over', { px: 640, turnMs: 18000 });
      ctx.anim(sp.el, [{ opacity: 0 }, { opacity: 0.16 }], { duration: fresh(ctx) ? 1200 : 1, fill: 'forwards', easing: 'ease-in' });
      return { off: sp.off };
    },
  },

  /* Page Tilt: the whole world leans, slowly. Wake Up and the cards stay outside it. Gentle: no lean at all. */
  takeover: {
    mount(ctx) {
      if (ctx.gentle) return {};
      const start = fresh(ctx) ? 0 : 1;
      // 84 steps of about 0.05 degrees: the eye cannot tell, and the whole page only recomposites a dozen times a second.
      const a = ctx.anim(ctx.world, [{ transform: 'rotate(-2.2deg)', easing: 'ease-in-out' }, { transform: 'rotate(2.2deg)' }], { duration: 7000, iterations: Infinity, direction: 'alternate', easing: 'steps(84, jump-none)', delay: start });
      ctx.every(1000, () => { if (ctx.gentle) a.cancel(); });
      return {};
    },
  },

  /* Wallpaper: a picture of a desktop behind the page, and a card that says plainly it is one. The real wallpaper is in the app. */
  wallpaper: {
    mount(ctx) {
      const desk = ctx.el('div', 'position:absolute;inset:0;overflow:hidden;background:var(--page);', null, 'back');
      const wall = spiralCanvas(ctx, 'position:absolute;left:50%;top:50%;width:150vmax;height:150vmax;margin:-75vmax 0 0 -75vmax;opacity:.24;', desk, { px: 512, turnMs: 30000 });
      /* One page-wide spiral at a time: a second one under the first costs as much as anything on the page and shows as stripes. */
      const stepAside = () => { if (ctx.has('e_spiral') || ctx.has('a_spiral') || ctx.pick('f5') === 'trance') wall.el.style.display = 'none'; };
      stepAside();
      ctx.on('buy', stepAside);
      const icons = ctx.el('div', 'position:absolute;left:10px;bottom:34px;display:flex;flex-direction:column;gap:12px;', null, desk);
      for (const name of FAKE_DESKTOP.icons) {
        const i = ctx.el('div', 'width:62px;text-align:center;font-size:11px;color:var(--ink);', null, icons);
        ctx.el('div', 'width:30px;height:24px;margin:0 auto 2px;background:#fff;border:1px solid #333;border-radius:3px;', null, i);
        i.append(name);
      }
      ctx.el('div', 'position:absolute;left:0;right:0;bottom:0;height:22px;background:#1b2230;', null, desk);
      if (fresh(ctx)) {
        ctx.anim(desk, [{ clipPath: 'inset(0 100% 0 0)' }, { clipPath: 'inset(0 0 0 0)' }], { duration: 900, easing: 'ease-in-out' });
        ctx.after(1100, () => ctx.modal({
          title: FAKE_DESKTOP.title,
          body: FAKE_DESKTOP.body,
          buttons: [
            { label: FAKE_DESKTOP.button, onPress: () => ctx.openLink('https://cclabs.app') },
            { label: FAKE_DESKTOP.close },
          ],
        }));
      }
      return { off: wall.off };
    },
  },

  /* Warp Tunnel: frames keep falling away behind the page, toward a point behind the shop, and two of them carry a frozen
   * snapshot of the counter and the button. The snapshot is only those two: a copy of the live meters was half of all the
   * nodes in a late run and sat right under the real ones. Decoration, no input. Gentle: no warp at all. */
  tunnel: {
    mount(ctx) {
      const COPIES = 3, SNAPSHOTS = 2, LOOP = 6000;
      const hole = ctx.el('div', 'position:absolute;inset:0;overflow:hidden;', null, 'back');
      const frames = [];
      for (let i = 0; i < COPIES; i++) {
        const f = ctx.el('div', 'position:absolute;inset:0;border:3px solid var(--stamp);overflow:hidden;will-change:transform,opacity;transform-origin:50% 74%;', null, hole);
        frames.push(f);
        if (!ctx.gentle) {
          ctx.anim(f, [{ transform: 'scale(.92)', opacity: 0 }, { transform: 'scale(.66)', opacity: 0.6, offset: 0.2 }, { transform: 'scale(.4)', opacity: 0.7, offset: 0.4 }, { transform: 'scale(.22)', opacity: 0.8, offset: 0.6 }, { transform: 'scale(.1)', opacity: 0.8, offset: 0.8 }, { transform: 'scale(.04)', opacity: 0 }], { duration: LOOP, iterations: Infinity, easing: 'linear', delay: -i * (LOOP / COPIES) });
        } else {
          f.style.transform = `scale(${0.8 - i * 0.16})`;
        }
      }
      const refresh = () => {
        const copy = ctx.parts.stage.cloneNode(false);
        copy.removeAttribute('id');
        const snap = (el) => { const c = el.cloneNode(true); c.removeAttribute('id'); c.querySelectorAll('[id]').forEach((n) => n.removeAttribute('id')); copy.appendChild(c); };
        snap(ctx.parts.counter);
        snap(ctx.button.el);
        copy.style.cssText = 'position:absolute;left:50%;top:40%;width:360px;max-width:100%;transform:translateX(-50%);opacity:.45;pointer-events:none;display:flex;flex-direction:column;align-items:center;gap:8px;';
        for (const f of frames.slice(0, SNAPSHOTS)) f.replaceChildren(copy.cloneNode(true));
      };
      refresh();
      ctx.every(30000, refresh);
      // Gentle can be switched on mid-run: the copies stop where they are.
      ctx.every(1000, () => { if (ctx.gentle) for (const f of frames) for (const a of f.getAnimations()) a.pause(); });
      if (fresh(ctx) && !ctx.gentle) ctx.anim(ctx.parts.shop, [{ transform: 'scale(1.06)', opacity: 0.6 }, { transform: 'none', opacity: 1 }], { duration: 800, easing: 'ease-out' });
      return {};
    },
  },

  /* Drop: its show between the fade and the credits. One word at a time through the pink-white. */
  drop: {
    mount() { return {}; },
    ending(ctx) {
      return new Promise((done) => {
        const { w, h } = ctx.bounds();
        const list = DROP_WORDS.filter((_, i) => i < 2 || i % 3 === 0 || i === DROP_WORDS.length - 1);
        const GAP = 1500;
        list.forEach((d, i) => {
          ctx.after(500 + i * GAP, () => {
            pop(ctx, w / 2, h * 0.45, d.word, { color: 'color-mix(in srgb, var(--accent) 62%, #000)', size: 46, ms: GAP - 100, weight: 600, parent: 'ending' });
            if (d.clip) ctx.voice(d.clip, { gain: 0.7 });
          });
        });
        ctx.after(500 + list.length * GAP + 400, done);
      });
    },
  },
};

