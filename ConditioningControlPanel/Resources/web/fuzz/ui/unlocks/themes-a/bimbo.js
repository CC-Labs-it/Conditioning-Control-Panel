/* Bimbo: hot pink and gloss, fewer and fewer words. The signature: the game gets dumber as you buy. A fake IQ drops with
 * every purchase, taps pay more the lower it goes, and the shop's words wear away until only pictures are left. */

import { BY_ID } from '../../../data/items.js';
import { THEMES, ENDINGS } from '../../../data/content/index.js';
import { mountPodcast } from '../podcast-player.js';
import { bag, geo, q, sheet, done, frame, sideFrames, startTheme, shopWords, modChip, pick, gentleSwitch } from './kit.js';
import { endingShow } from './endings.js';
import { COPY } from './copy.js';

const R = THEMES.bimbo.rules;
const NICHES = ['pink', 'heels', 'gloss', 'curls', 'lashes'];

/** Pure, from the run alone, so the tap modifier can read it and a reload lands on the same number. */
export function iqOf(s) {
  const from = s.order.indexOf('bi_iq');
  if (from < 0) return 100;
  if ((s.owned.bi_pics || 0) > 0) return 0;
  let v = 100;
  for (let i = from + 1; i < s.order.length; i++) v -= BY_ID[s.order[i]]?.lane === 'bimbo' ? 10 : 6;
  return Math.max(0, v);
}

const VARS = {
  '--page': '#ffcde6', '--ink': '#8a0f5c', '--ink-soft': '#b0277f', '--ink-faint': '#c9559d', '--btn': '#ff2f92',
  '--panel': '#fff0f7', '--panel-short': '#ffd9ec', '--line': '#ff8ac2', '--fill': '#ffa9d4', '--stamp': '#ff2f92',
  '--font': '"Fredoka","Segoe UI",system-ui,sans-serif',
};
const CSS = `@font-face{font-family:"Fredoka";src:url("assets/fonts/fredoka-latin.woff2") format("woff2");font-weight:300 700;font-display:swap;}
#btn{border:0;border-radius:28px;color:#fff;font-weight:600;background:linear-gradient(#ff8ac2,#ff2f92);box-shadow:0 3px 0 #c9207a;}
#btn.down{border-style:none;box-shadow:0 1px 0 #c9207a;transform:translateY(2px) scale(.97);}
#counter{color:#ff2f92;font-weight:600;}
.row{border-radius:14px;}
#goal .row{border-color:#ff2f92;}
#goal .row.ready{animation-name:bi-ready;}
@keyframes bi-ready{50%{background:#fff;}}
.toast{border-radius:14px;background:#ff2f92;color:#fff;}`;

/** A pink pop from the button, hearts, one word. */
function moment(ctx) {
  ctx.audio.play('assets/audio/sfx/golden_pop.mp3', { bus: 'sfx', gain: 0.8 });
  ctx.voice('blank-and-happy');
  const g = geo(ctx);
  const s = sheet(ctx, 'background:#ff2f92;display:grid;place-items:center;color:#fff;font-size:clamp(60px,18vw,150px);font-weight:600;');
  const word = ctx.el('div', '', COPY.bimbo.moment, s);
  if (ctx.gentle) { done(ctx.anim(s, [{ opacity: 0 }, { opacity: 1, offset: 0.3 }, { opacity: 0 }], { duration: 1100 }), s); return; }
  const R0 = Math.hypot(g.w, g.h);
  /* Fast in, a held beat to read the word, out. Easing per step: one global curve swallows the hold. */
  done(ctx.anim(s, [
    { clipPath: `circle(0px at ${g.cx}px ${g.cy}px)`, opacity: 1, offset: 0, easing: 'cubic-bezier(.2,.9,.3,1)' },
    { clipPath: `circle(${R0}px at ${g.cx}px ${g.cy}px)`, opacity: 1, offset: 0.2 },
    { clipPath: `circle(${R0}px at ${g.cx}px ${g.cy}px)`, opacity: 1, offset: 0.62, easing: 'ease-in' },
    { clipPath: `circle(${R0}px at ${g.cx}px ${g.cy}px)`, opacity: 0, offset: 1 },
  ], { duration: 1200 }), s);
  ctx.anim(word, [{ transform: 'scale(.2) rotate(-14deg)', offset: 0, easing: 'cubic-bezier(.2,1.7,.4,1)' }, { transform: 'scale(1.15) rotate(3deg)', offset: 0.22 }, { transform: 'scale(1) rotate(0deg)', offset: 0.36 }, { transform: 'scale(1) rotate(0deg)', offset: 0.62 }, { transform: 'scale(1.6) rotate(0deg)', offset: 1 }], { duration: 1200 });
  for (let i = 0; i < 14; i++) {
    const h = ctx.el('div', `position:absolute;left:${g.cx}px;top:${g.cy}px;color:#fff;font-size:${16 + ctx.rng() * 26}px;`, '♡', 'over');
    const a = ctx.rng() * Math.PI * 2, d = 120 + ctx.rng() * Math.max(g.w, g.h) * 0.45;
    done(ctx.anim(h, [{ transform: 'translate(0,0) scale(.4)', opacity: 1 }, { transform: `translate(${Math.cos(a) * d}px,${Math.sin(a) * d}px) scale(1.2)`, opacity: 0 }], { duration: 1000 + ctx.rng() * 500, easing: 'cubic-bezier(.1,.8,.3,1)' }), h);
  }
}

/** Longest match first, whole words, the capital kept. */
function swapper(ctx) {
  const cur = ctx.brand.text('{cur}');
  /* The words the shop says most go first, so the first swap is one you can see. Matching stays longest first. */
  const FIRST = ['button', cur, 'spiral', 'pictures', 'choose', 'second', 'earn', 'increase', 'bonus', 'effect'];
  const rank = (w) => { const i = FIRST.indexOf(w); return i < 0 ? FIRST.length : i; };
  const rows = R.swaps.map(([a, b]) => ({ word: ctx.brand.text(a), to: b })).sort((x, y) => rank(x.word) - rank(y.word));
  rows.forEach((r, i) => {
    r.turn = i;
    r.re = new RegExp(`(^|[^\\p{L}])(${r.word.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')})(?![\\p{L}])`, 'giu');
  });
  const byLength = [...rows].sort((x, y) => y.word.length - x.word.length);
  return (text, upTo) => {
    let t = text;
    for (const r of byLength) {
      if (r.turn >= upTo) continue;
      t = t.replace(r.re, (m, pre, w, at) => {
        /* "A second button" is not a tick. */
        if (r.word === 'second' && /\ba\s$/i.test(t.slice(0, at + pre.length))) return m;
        /* The currency is a name, not a sentence start. */
        const cap = r.word === cur ? at === 0 && !pre : w[0] === w[0].toUpperCase();
        return pre + (cap ? r.to[0].toUpperCase() + r.to.slice(1) : r.to);
      });
    }
    return t;
  };
}

export default {
  bi_head: {
    mount(ctx) {
      const theme = startTheme(ctx, { theme: 'bimbo', vars: VARS, css: CSS, moment });
      sideFrames(ctx, ['pink', 'heels', 'gloss', 'lashes'], 'border-radius:18px;border:3px solid #fff;box-shadow:0 2px 0 #ff8ac2;');
      modChip(ctx, 'bimbo');
      return { off() { theme.off(); } };
    },
  },

  /* IQ Meter: every buy takes some, and lower pays more per tap. */
  bi_iq: {
    mount(ctx) {
      const b = bag(ctx);
      ctx.mod({ click: (v, s) => v * (1 + 2 * (100 - iqOf(s)) / 100) });
      const box = ctx.el('div', 'display:flex;flex-direction:column;align-items:center;width:min(280px,80vw);margin-top:2px;', null, 'meters');
      const top = ctx.el('div', 'display:flex;gap:8px;align-items:baseline;font-weight:600;color:var(--ink);', null, box);
      const num = ctx.el('span', 'font-size:17px;font-variant-numeric:tabular-nums;', null, top);
      const note = ctx.el('span', 'font-size:13px;font-weight:400;color:var(--ink-soft);', null, top);
      const track = ctx.el('div', 'position:relative;width:100%;height:8px;border-radius:4px;background:rgba(255,255,255,.75);overflow:hidden;', null, box);
      const fill = ctx.el('div', 'position:absolute;inset:0;border-radius:4px;background:#ff2f92;transform-origin:0 50%;transition:transform .35s cubic-bezier(.2,1.2,.4,1);', null, track);
      let shown = -1;
      const paint = () => {
        const iq = iqOf(ctx.state);
        if (iq === shown) return;
        const was = shown;
        shown = iq;
        b.iq = iq;
        num.textContent = iq === 0 ? 'IQ' : `IQ ${iq}`;
        note.textContent = R.iq[Math.min(R.iq.length - 1, Math.floor((100 - iq) / (100 / (R.iq.length - 1))))];
        fill.style.transform = `scaleX(${(iq / 100).toFixed(2)})`;
        b.onIq?.forEach((f) => f(iq));
        if (was > iq) {
          const r = num.getBoundingClientRect();
          ctx.float(r.right + 26, r.top, `-${was - iq}`, 'color:#ff2f92;font-size:18px;font-weight:600;');
          if (!ctx.gentle) ctx.anim(num, [{ transform: 'scale(1)' }, { transform: 'scale(1.35) rotate(-4deg)' }, { transform: 'scale(1)' }], 320);
        }
      };
      b.onIq = b.onIq || new Set();
      paint();
      ctx.on('buy', paint);
      return {};
    },
  },

  /* Bimbo Pictures: they fall, top to bottom. */
  bi_feed: {
    mount(ctx) {
      const live = [];
      let col = 0;
      const spawn = (lead = 0) => {
        if (live.length >= 7) return;
        const { w, h } = ctx.bounds();
        const narrow = w < 760;
        const tw = narrow ? 104 : 168, th = tw * 1.2;
        const cols = narrow ? 3 : Math.max(3, Math.floor(w / (tw + 20)));
        col = (col + 1 + Math.floor(ctx.rng() * 2)) % cols;
        const x = (w / cols) * col + (w / cols - tw) / 2;
        const f = frame(ctx, `position:absolute;left:${x}px;top:0;width:${tw}px;height:${th}px;border-radius:16px;opacity:.6;will-change:transform;`, pick(ctx, NICHES));
        live.push(f);
        const a = ctx.anim(f.el, [{ transform: `translateY(${-th}px) rotate(-2deg)` }, { transform: `translateY(${h}px) rotate(2deg)` }], { duration: 12000, easing: 'linear' });
        if (lead) a.currentTime = lead;
        a.finished?.then(() => { f.el.remove(); const n = live.indexOf(f); if (n >= 0) live.splice(n, 1); }, () => {});
      };
      ctx.on('pictures', () => { for (const f of live) f.next(); });
      gentleSwitch(ctx, () => {
        [0, 2000, 4000, 6000, 8000].forEach((t) => spawn(t));
        const stop = ctx.every(2000, () => spawn());
        return () => { stop(); for (const f of [...live]) f.el.remove(); live.length = 0; };
      }, () => {
        const list = [0, 1, 2].map((i) => frame(ctx, `position:absolute;left:${4 + i * 33}%;top:6px;width:29%;max-width:190px;height:${Math.min(geo(ctx).stageBottom - 8, 200)}px;border-radius:16px;opacity:.55;`, NICHES[i]));
        live.push(...list);
        let k = 0;
        const stop = ctx.every(8000, () => list[k++ % 3].next());
        return () => { stop(); for (const f of list) f.el.remove(); live.length = 0; };
      });
      return {};
    },
  },

  /* Lips Button: glossy lips, and taps make a tiny kiss. */
  bi_lips: {
    mount(ctx) {
      ctx.button.skin('bi-lips');
      ctx.css(`#btn[data-skin=bi-lips]{width:132px;height:84px;font-size:0;background:radial-gradient(circle at 34% 26%,#fff 0 6%,#ff9cce 24%,#ff2f92);border-radius:42px;box-shadow:0 3px 0 #c9207a;}
#btn[data-skin=bi-lips]::after{content:'👄';font-size:60px;line-height:84px;font-family:"Segoe UI Emoji","Apple Color Emoji","Noto Color Emoji",sans-serif;}`);
      let n = 0;
      ctx.on('tap', (e) => {
        const g = bag(ctx).lipsAt || (bag(ctx).lipsAt = geo(ctx));
        const x = e.x ?? g.cx, y = e.y ?? g.cy;
        ctx.float(x + (ctx.rng() - 0.5) * 70, y - 16, '💋', 'font-size:20px;');
        if (n++ % 4 === 0) { ctx.float(x + 44, y - 40, COPY.bimbo.kiss, 'color:#ff2f92;font-style:italic;font-weight:600;'); ctx.audio.play('assets/audio/sfx/chip_pop.mp3', { bus: 'sfx', gain: 0.5 }); }
      });
      return { off() { bag(ctx).lipsAt = null; } };
    },
  },

  /* Simpler Words: the shop's words get easier as the meter drops. At the bottom the descriptions give up. */
  bi_words: {
    mount(ctx) {
      const b = bag(ctx), swap = swapper(ctx);
      const lines = THEMES.bimbo.shopLines;
      const handle = shopWords(ctx, (text, node) => {
        const iq = b.iq ?? 100, upTo = Math.ceil(((100 - iq) / 60) * R.swaps.length);
        if (iq <= 24 && node.tagName === 'SMALL') return lines[Math.abs([...text].reduce((h, c) => h * 31 + c.charCodeAt(0) | 0, 7)) % lines.length];
        return swap(text, upTo);
      });
      const refresh = () => {
        handle.refresh();
        if (ctx.gentle) return;
        for (const r of ctx.world.querySelectorAll('#rows .row small,#goal .row small')) ctx.anim(r, [{ opacity: 0.1, transform: 'scale(.97)' }, { opacity: 1, transform: 'scale(1)' }], 350);
      };
      b.onIq?.add(refresh);
      ctx.after(0, refresh);
      return { off() { b.onIq?.delete(refresh); handle.off(); } };
    },
  },

  /* Vague Counter: no more numbers. */
  bi_nums: {
    mount(ctx) {
      const STEPS = [1e3, 1e4, 5e4, 2e5, 5e5, 1e6, 3e6];
      const count = q(ctx, '#count'), cur = q(ctx, '#cur'), box = q(ctx, '#counter');
      const word = ctx.el('span', '', '', box);
      count.hidden = true;
      if (cur) cur.hidden = true;
      let shown = -1;
      const paint = () => {
        const f = ctx.state.fuzz;
        let i = 0;
        while (i < STEPS.length && f >= STEPS[i]) i += 1;
        if (i === shown) return;
        shown = i;
        word.textContent = R.vague[Math.min(i, R.vague.length - 1)];
        if (!ctx.gentle) ctx.anim(word, [{ transform: 'scale(.86)' }, { transform: 'scale(1.06)' }, { transform: 'scale(1)' }], 260);
      };
      paint();
      ctx.every(200, paint);
      return { off() { count.hidden = false; if (cur) cur.hidden = false; } };
    },
  },

  bi_pod: { mount: (ctx) => mountPodcast(ctx, { theme: 'bimbo', show: 'True Bimbo', accent: '#ff2f92' }) },

  /* Sparkly Surfaces: gloss on every row and a sheen across the page, with little stars landing on what is there. */
  bi_gloss: {
    mount(ctx) {
      ctx.css(`.row{background:linear-gradient(#fff,#ffd9ec);box-shadow:inset 0 1px 0 #fff,0 1px 0 #ff8ac2;}
.row.short{background:linear-gradient(#ffeaf4,#ffc9e2);}`);
      gentleSwitch(ctx, () => {
        const sheen = ctx.el('div', 'position:absolute;top:0;bottom:0;left:0;width:120px;background:linear-gradient(90deg,transparent,rgba(255,255,255,.55),transparent);will-change:transform;', null, 'over');
        const { w } = ctx.bounds();
        ctx.anim(sheen, [{ transform: `translateX(-200px) skewX(-20deg)` }, { transform: `translateX(${w + 100}px) skewX(-20deg)` }], { duration: 3600, iterations: Infinity, easing: 'ease-in-out' });
        return () => sheen.remove();
      });
      const rects = () => {
        const out = [];
        for (const r of ctx.world.querySelectorAll('.row,#btn,#counter')) { const b = r.getBoundingClientRect(); if (b.width) out.push(b); }
        return out;
      };
      let spots = rects();
      ctx.every(2000, () => { spots = rects(); });
      const pool = [];
      for (let i = 0; i < 12; i++) pool.push(ctx.el('div', 'position:absolute;left:0;top:0;color:#fff;font-size:16px;opacity:0;text-shadow:0 0 3px #ff2f92;will-change:transform,opacity;', '✦', 'over'));
      let k = 0;
      ctx.every(ctx.gentle ? 900 : 340, () => {
        if (!spots.length) return;
        const r = spots[Math.floor(ctx.rng() * spots.length)], e = pool[k++ % pool.length];
        const x = r.left + ctx.rng() * r.width - 8, y = r.top + ctx.rng() * r.height - 8, sz = 0.7 + ctx.rng() * 0.8;
        ctx.anim(e, ctx.gentle
          ? [{ transform: `translate(${x}px,${y}px) scale(${sz})`, opacity: 0 }, { transform: `translate(${x}px,${y}px) scale(${sz})`, opacity: 0.9 }, { transform: `translate(${x}px,${y}px) scale(${sz})`, opacity: 0 }]
          : [{ transform: `translate(${x}px,${y}px) scale(0)`, opacity: 0 }, { transform: `translate(${x}px,${y}px) scale(${sz}) rotate(40deg)`, opacity: 1, offset: 0.4 }, { transform: `translate(${x}px,${y}px) scale(0) rotate(90deg)`, opacity: 0 }], { duration: ctx.gentle ? 1600 : 800 });
      });
      return {};
    },
  },

  /* Giggles: lucky taps crit, and a recorded giggle plays. No clip yet = no sound; the crit still lands. */
  bi_giggle: {
    mount(ctx) {
      let since = 0;
      ctx.on('tap', (e) => {
        if (e.auto) return;
        since += 1;
        if (ctx.rng() > 0.05 + since * 0.03) return;
        since = 0;
        const g = geo(ctx);
        const x = e.x ?? g.cx, y = e.y ?? g.cy;
        const gain = ctx.pay(Math.max(ctx.rates().click * 10, ctx.rates().fps * 2), 'bi_giggle', { x, y: y - 20 });
        ctx.float(x, y - 54, COPY.bimbo.giggle(ctx.fmt(gain)), 'color:#ff2f92;font-size:22px;font-style:italic;font-weight:600;');
        ctx.audio.play(`assets/audio/themes/bimbo/giggle${1 + Math.floor(ctx.rng() * 3)}.mp3`, { bus: 'voice', gain: 0.8 });
        ctx.audio.play('assets/audio/sfx/chain_pop.mp3', { bus: 'sfx', gain: 0.5 });
        if (!ctx.gentle) {
          const ring = ctx.el('div', `position:absolute;left:${x - 30}px;top:${y - 30}px;width:60px;height:60px;border-radius:50%;border:3px solid #ff2f92;`, null, 'floaters');
          done(ctx.anim(ring, [{ transform: 'scale(.4)', opacity: 1 }, { transform: 'scale(2.2)', opacity: 0 }], { duration: 480, easing: 'ease-out' }), ring);
        }
      });
      return {};
    },
  },

  /* Icons Only Shop: at zero IQ the words are gone. A row still dims when it costs too much; the goal still fills. */
  bi_pics: {
    mount(ctx) {
      const shop = q(ctx, '#shop');
      ctx.css(`#shop.ta-icons .row b,#shop.ta-icons .row small,#shop.ta-icons .row .cost,#shop.ta-icons .row .tag,#shop.ta-icons #more{display:none;}
#shop.ta-icons .row{grid-template-columns:1fr;justify-items:center;min-height:56px;}
#shop.ta-icons #goal .row{min-height:64px;}
#shop.ta-icons .row .ico{font-size:34px;}
#shop.ta-icons .row img.ico{width:40px;height:40px;}
#shop.ta-icons #rows{grid-template-columns:repeat(auto-fit,minmax(84px,1fr));}
#shop.ta-icons .row.short .ico{opacity:.5;}`);
      const handle = shopWords(ctx, (text, node) => {
        const row = node.closest('.row');
        if (row && node.tagName === 'B' && !row.getAttribute('aria-label')) row.setAttribute('aria-label', text);
        return text;
      });
      shop?.classList.add('ta-icons');
      return { off() { shop?.classList.remove('ta-icons'); handle.off(); } };
    },
  },

  bi_end: {
    mount() { return {}; },
    ending(ctx) { return endingShow(ctx, ENDINGS.bi_end, 'bimbo'); },
  },
};
