/* ui/unlocks/themes-b/locked.js - lane locked, shown as Chastity. Cold steel and brass. The mechanic: a timer that only climbs, and a
 * button to beg with. All the time here is pretend; nothing touches a real timer. */

import { THEMES } from '../../../data/content/themes.js';
import { FAKE_KEYHOLDER } from '../../../data/content/fakes.js';
import { ENDINGS } from '../../../data/content/endings.js';
import { mountPodcast } from '../podcast-player.js';
import { COPY } from './copy.js';
import { buttonCentre, docOf, endingStage, endingWords, fresh, gallery, gutter, pick, ring, rowsOf, sfx, wide } from './kit.js';
import { THEME_TASTES } from '../theme-skins.js';

const R = THEMES.locked.rules;
const BRASS = '#e3b341', STEEL = '#9fb3c6', BAD = '#ff7a6e', GOOD = '#7fe0a8';

const pad = (n) => String(Math.floor(n)).padStart(2, '0');
function clockText(s) {
  const d = Math.floor(s / 86400), h = Math.floor(s / 3600) % 24, m = Math.floor(s / 60) % 60;
  return `${d ? `${d}d ` : ''}${pad(h)}:${pad(m)}:${pad(s % 60)}`;
}
const minutesText = (m) => {
  const a = Math.abs(m), sign = m < 0 ? '-' : '+';
  return a >= 1440 ? `${sign}${Math.round(a / 1440)}d` : a >= 60 ? `${sign}${Math.round(a / 60)}h` : `${sign}${a}m`;
};

/** A padlock drawn with boxes. */
function padlock(ctx, parent, size, color, css = '') {
  const el = ctx.el('div', `position:relative;width:${size}px;height:${size * 1.15}px;pointer-events:none;${css}`, null, parent);
  const shackle = ctx.el('div', `position:absolute;left:${size * 0.2}px;top:0;box-sizing:border-box;width:${size * 0.6}px;height:${size * 0.7}px;border:${size * 0.12}px solid ${color};border-bottom:0;border-radius:${size * 0.3}px ${size * 0.3}px 0 0;`, null, el);
  ctx.el('div', `position:absolute;left:0;top:${size * 0.45}px;width:${size}px;height:${size * 0.7}px;background:${color};border-radius:${size * 0.1}px;`, null, el);
  ctx.el('div', `position:absolute;left:${size * 0.42}px;top:${size * 0.66}px;width:${size * 0.16}px;height:${size * 0.26}px;border-radius:${size * 0.08}px;background:#1d2935;`, null, el);
  el.shackle = shackle;
  return el;
}

export const lockedPick = {
  /* The pick: a huge padlock drops onto the page and closes. */
  arrive(ctx) {
    const b = ctx.bounds(), size = Math.min(220, b.w * 0.5);
    const hold = ctx.el('div', `position:absolute;left:50%;top:32%;margin-left:${-size / 2}px;pointer-events:none;`, null, 'over');
    const lock = padlock(ctx, hold, size, BRASS);
    const shut = ctx.gentle ? [{ opacity: 1 }, { opacity: 1 }] : [{ transform: `translateY(${-size * 0.28}px)` }, { transform: `translateY(${-size * 0.28}px)`, offset: 0.55 }, { transform: 'none', offset: 0.68 }, { transform: 'none' }];
    ctx.anim(lock.shackle, shut, { duration: 1100 });
    ctx.anim(hold, ctx.gentle ? [{ opacity: 0 }, { opacity: 1 }, { opacity: 0 }] : [{ transform: 'translateY(-60vh)' }, { transform: 'none', offset: 0.32, easing: 'cubic-bezier(.3,1.5,.5,1)' }, { transform: 'none', opacity: 1, offset: 0.8 }, { opacity: 0 }], { duration: 1500, fill: 'forwards' });
    ctx.after(700, () => { sfx(ctx, 'snap'); ctx.float(b.w / 2, b.h * 0.32 + size * 1.2, COPY.locked.click, `color:${STEEL};font-size:22px;`); });
    ctx.after(1560, () => hold.remove());
  },
};

/** A lock on a picture tile. */
function lockTile(ctx) {
  return (t, w) => {
    const s = Math.round(w * 0.3);
    padlock(ctx, t.el, s, BRASS, `position:absolute;left:${(w - s) / 2}px;top:40%;`);
  };
}

function wheelNode(ctx) {
  const doc = docOf(ctx), size = 188;
  const node = doc.createElement('div');
  node.style.cssText = `display:flex;flex-direction:column;align-items:center;gap:6px;color:var(--ink);font:13px var(--font);`;
  const face = doc.createElement('div');
  const stops = R.wheel.map((_, i) => `${i % 2 ? '#3a4a5a' : BRASS} ${i * 45}deg ${(i + 1) * 45}deg`).join(',');
  face.style.cssText = `position:relative;width:${size}px;height:${size}px;border-radius:50%;border:3px solid ${STEEL};background:conic-gradient(${stops});box-sizing:border-box;`;
  R.wheel.forEach((seg, i) => {
    const l = doc.createElement('div');
    l.textContent = seg.label.replace(', for now', '').replace(' minutes', 'm').replace(' hours', 'h').replace(' hour', 'h').replace(' days', 'd').replace(' day', 'd').replace(' week', 'w');
    l.style.cssText = `position:absolute;left:50%;top:50%;width:60px;margin:-8px 0 0 -30px;text-align:center;font:700 11px var(--font);color:${i % 2 ? '#fff' : '#201600'};transform:rotate(${i * 45 + 22.5}deg) translateY(-62px);`;
    face.appendChild(l);
  });
  const hold = doc.createElement('div');
  hold.style.cssText = 'position:relative;padding-top:10px;';
  const peg = doc.createElement('div');
  peg.style.cssText = 'position:absolute;left:50%;top:0;margin-left:-8px;width:0;height:0;border-left:8px solid transparent;border-right:8px solid transparent;border-top:14px solid #fff;z-index:1;filter:drop-shadow(0 0 1px #000);';
  hold.append(peg, face);
  const label = doc.createElement('div');
  label.style.cssText = 'font-weight:700;min-height:18px;';
  const spin = doc.createElement('button');
  spin.type = 'button';
  spin.className = 'tb-chip';
  spin.textContent = COPY.locked.spin;
  const note = doc.createElement('small');
  note.textContent = R.wheelNote;
  note.style.cssText = 'text-align:center;opacity:.8;';
  node.append(hold, label, spin, note);
  let rot = 0, busy = false;
  const COOL = 12000;
  spin.addEventListener('click', () => {
    if (busy) return;
    busy = true;
    spin.disabled = true;
    const i = Math.floor(ctx.rng() * R.wheel.length), seg = R.wheel[i];
    const next = rot - (rot % 360) + 1440 - (i * 45 + 22.5);
    ctx.anim(face, ctx.gentle ? [{ transform: `rotate(${next}deg)` }, { transform: `rotate(${next}deg)` }] : [{ transform: `rotate(${rot}deg)` }, { transform: `rotate(${next}deg)` }], { duration: ctx.gentle ? 300 : 2400, easing: 'cubic-bezier(.15,.7,.2,1)', fill: 'forwards' });
    rot = next;
    ctx.after(ctx.gentle ? 300 : 2400, () => {
      label.textContent = seg.label;
      ctx.emit('lo:add', { minutes: seg.minutes });
      const r = spin.getBoundingClientRect();
      ctx.pay(ctx.rates().fps * (seg.minutes > 0 ? 8 : 3), 'wheel', { x: r.left + r.width / 2, y: r.top });
      sfx(ctx, seg.minutes > 0 ? 'thud' : 'chime1', 0.7);
      ctx.after(COOL, () => { busy = false; spin.disabled = false; });
    });
  });
  return node;
}

export default {
  lo_head: {
    mount(ctx) {
      const g = gallery(ctx, { niche: 'chastity', mode: 'swap', decorate: lockTile(ctx) });
      if (ctx.has('lo_feed')) g.hide();
      ctx.on('buy', (e) => { if (e.item.id === 'lo_feed') g.hide(); });
      return {};
    },
  },

  /* The timer: big, brass, and it only climbs. Earnings climb with it. */
  lo_timer: {
    mount(ctx) {
      if (ctx.store.get('start') === undefined) ctx.store.set('start', ctx.state.clock);
      let extra = ctx.store.get('extra') || 0, shown = null;
      const climbed = () => (ctx.state.clock - ctx.store.get('start')) / 1000 * 60;
      const seconds = () => Math.max(60, climbed() + extra);
      const base = ctx.item.fps;
      const bonus = () => base * Math.min(6, seconds() / 3600 / 5);
      ctx.mod({ fps: (v) => v + bonus() });
      const box = ctx.el('div', 'display:flex;flex-direction:column;align-items:center;line-height:1.1;', null, 'meters');
      ctx.el('div', 'font:11px var(--font);color:var(--ink-faint);', R.timerLabel, box);
      const big = ctx.el('div', `font:700 26px Consolas,"Courier New",monospace;color:${BRASS};font-variant-numeric:tabular-nums;`, '', box);
      const rate = ctx.el('div', 'font:12px var(--font);color:var(--ink-soft);', '', box);
      if (fresh(ctx)) ctx.toast(R.timerNote);
      const paint = () => { big.textContent = clockText(shown ?? seconds()); rate.textContent = COPY.locked.lockRate(ctx.fmt(bonus())); };
      paint();
      ctx.every(ctx.gentle ? 1000 : 250, paint);
      ctx.on('lo:add', ({ minutes }) => {
        // Time off can reach the floor but never bank below it, or the timer would sit still for an hour afterwards.
        extra = Math.max(60 - climbed(), extra + minutes * 60);
        ctx.store.set('extra', extra);
        paint();
        if (minutes) {
          const r = big.getBoundingClientRect();
          ctx.float(r.left + r.width / 2, r.top - 6, minutesText(minutes), `color:${minutes > 0 ? BAD : GOOD};font-size:17px;font-weight:700;`);
          if (!ctx.gentle) ctx.anim(big, [{ transform: 'scale(1)' }, { transform: 'scale(1.14)' }, { transform: 'scale(1)' }], 260);
        }
      });
      ctx.on('lo:show', ({ seconds: s }) => { shown = s; paint(); });
      ctx.on('lo:ask', (q) => { q.seconds = seconds(); });
      return {};
    },
  },

  /* Beg: usually nothing. A pity count makes sure it is never nothing for long. */
  lo_beg: {
    mount(ctx) {
      const wrap = ctx.parts.buttonWrap;
      const beg = ctx.el('button', `position:absolute;right:calc(100% + 12px);top:50%;width:80px;height:46px;margin-top:-23px;background:${BRASS};color:#2a1e00;border:0;border-radius:23px;font:700 16px var(--font);touch-action:manipulation;pointer-events:auto;box-shadow:0 2px 0 #8a6a16;`, COPY.locked.beg, wrap);
      beg.type = 'button';
      beg.dataset.tbBeg = '1';
      let since = 0, coolUntil = 0, window_ = false;
      ctx.on('lo:window', ({ on }) => { window_ = on; });
      ctx.on('lo:refused', ({ on }) => {
        if (!on || ctx.gentle) return;
        ctx.anim(beg, [{ transform: 'scale(1)' }, { transform: 'scale(1.18)' }, { transform: 'scale(1)' }], { duration: 700, iterations: 3 });
      });
      beg.addEventListener('click', () => {
        const r = beg.getBoundingClientRect(), x = r.left + r.width / 2, y = r.top;
        const clock = ctx.state.clock;
        since += 1;
        ctx.emit('lo:beg', {});
        if (!ctx.gentle) ctx.anim(beg, [{ transform: 'translateY(0)' }, { transform: 'translateY(2px) scale(.96)' }, { transform: 'translateY(0)' }], 120);
        const rates = ctx.rates();
        if (window_) {
          window_ = false;
          ctx.pay(rates.fps * 14 + rates.click * 20, 'almost', { x, y });
          ctx.float(x, y - 22, COPY.locked.hoped, `color:${BRASS};font-size:18px;font-weight:700;`);
          ring(ctx, BRASS);
          sfx(ctx, 'chime2');
          return;
        }
        const chance = clock < coolUntil ? 0 : since >= 8 ? 1 : 0.14;
        if (ctx.rng() < chance) {
          since = 0;
          coolUntil = clock + 2500;
          ctx.pay(rates.fps * 6 + rates.click * 12, 'beg', { x, y });
          ctx.float(x, y - 22, pick(ctx, R.begPays), `color:${BRASS};font-size:18px;font-weight:700;`);
          ring(ctx, BRASS);
          sfx(ctx, 'dling');
        } else {
          ctx.float(x, y - 16, pick(ctx, R.begNothing), `color:${STEEL};`);
          sfx(ctx, 'ui_denied', 0.4);
        }
      });
      return {};
    },
  },

  /* The keyholder texts. Time goes on or comes off. Buying something can set them off. */
  lo_key: {
    mount(ctx) {
      const msgs = FAKE_KEYHOLDER.messages;
      let busy = false;
      const show = (m) => {
        if (busy) return;
        busy = true;
        const roomy = wide(ctx);
        const css = roomy
          ? `left:12px;top:150px;width:${Math.min(gutter(ctx) - 24, 230)}px;`
          : 'left:10px;right:10px;top:6px;';
        const e = ctx.el('div', `position:absolute;${css}box-sizing:border-box;padding:8px 12px;background:#2c3c4c;color:#e4ecf3;border:1px solid ${STEEL};border-radius:14px 14px 14px 3px;font:14px/1.25 var(--font);pointer-events:none;box-shadow:0 3px 10px rgba(0,0,0,.4);`, null, 'over');
        ctx.el('div', `font:600 11px var(--font);color:${BRASS};`, FAKE_KEYHOLDER.name, e);
        ctx.el('div', '', m.text, e);
        if (fresh(ctx, 'noted')) ctx.el('div', 'font:11px var(--font);opacity:.75;margin-top:3px;', FAKE_KEYHOLDER.note, e);
        ctx.anim(e, ctx.gentle ? [{ opacity: 0 }, { opacity: 1, offset: 0.1 }, { opacity: 1, offset: 0.9 }, { opacity: 0 }] : [{ opacity: 0, transform: 'translateX(40px)' }, { opacity: 1, transform: 'none', offset: 0.08 }, { opacity: 1, offset: 0.9 }, { opacity: 0 }], { duration: 5200, fill: 'forwards' });
        ctx.after(600, () => { ctx.emit('lo:add', { minutes: m.minutes }); sfx(ctx, 'chip_pop', 0.5); });
        ctx.after(5300, () => { e.remove(); busy = false; });
      };
      ctx.every(21000, () => show(pick(ctx, msgs)));
      ctx.on('buy', () => { if (ctx.rng() < 0.3) show(msgs[6]); });
      return {};
    },
  },

  /* Pictures you may look at. Press one to be told so. */
  lo_feed: {
    mount(ctx) {
      const g = gallery(ctx, { niche: 'chastity', mode: 'scroll', parent: gutter(ctx) >= 110 ? 'floaters' : 'back', decorate: lockTile(ctx) });
      for (const t of g.tiles) {
        if (gutter(ctx) < 110) break;
        t.el.style.pointerEvents = 'auto';
        t.el.addEventListener('click', (ev) => {
          ctx.float(ev.clientX, ev.clientY - 20, COPY.locked.lookOnly, `color:${BRASS};font-weight:700;`);
          sfx(ctx, 'ui_denied', 0.4);
          const img = t.el.querySelector('img');
          if (!ctx.gentle && img) ctx.anim(img, [{ transform: 'translateX(0)' }, { transform: 'translateX(-4px)' }, { transform: 'translateX(4px)' }, { transform: 'translateX(0)' }], 300);
        });
      }
      return {};
    },
  },

  /* A padlock for a button. It rocks when tapped. It will not open. */
  lo_pad: {
    mount(ctx) {
      ctx.css(THEME_TASTES.locked.css);
      ctx.button.skin('padlock');
      const btn = ctx.button.el;
      let up = 0;
      ctx.on('tap', () => {
        if (ctx.gentle) return;
        btn.style.setProperty('--shackle', '-5px');
        clearTimeout(up);
        up = setTimeout(() => btn.style.removeProperty('--shackle'), 110);
        ctx.anim(btn, [{ transform: 'rotate(0)' }, { transform: 'rotate(-4deg)' }, { transform: 'rotate(3deg)' }, { transform: 'rotate(0)' }], 240);
      });
      return { off() { clearTimeout(up); btn.style.removeProperty('--shackle'); } };
    },
  },

  /* Some buys get a NO stamp. One beg lifts it; twenty seconds lift it anyway. The engine lets a row through after 3 refusals. */
  lo_refuse: {
    mount(ctx) {
      let refused = false, liftAt = 0, tries = 0, stamp = null;
      const lift = () => {
        if (!refused) return;
        refused = false;
        ctx.emit('lo:refused', { on: false });
        if (stamp) { const s = stamp; stamp = null; ctx.anim(s, [{ opacity: 1 }, { opacity: 0 }], { duration: 300, fill: 'forwards' }); ctx.after(320, () => s.remove()); }
      };
      const stampOn = (id) => {
        const row = rowsOf(ctx).find((r) => r.dataset.id === id);
        if (!row) return;
        const r = row.getBoundingClientRect();
        stamp = ctx.el('div', `position:absolute;left:${r.left + r.width / 2 - 46}px;top:${r.top + r.height / 2 - 26}px;width:92px;height:52px;display:grid;place-items:center;border:4px solid #ff5a4a;border-radius:6px;color:#ff5a4a;background:rgba(29,41,53,.86);font:800 28px var(--font);transform:rotate(-12deg);pointer-events:none;`, COPY.locked.no, 'over');
        if (!ctx.gentle) ctx.anim(stamp, [{ transform: 'rotate(-12deg) scale(1.7)', opacity: 0 }, { transform: 'rotate(-12deg) scale(1)', opacity: 1 }], 200);
      };
      ctx.guardBuy((item) => {
        if (!refused) {
          tries += 1;
          if (tries % 3 !== 0) return null;
          refused = true;
          liftAt = ctx.state.clock + 20000;
          stampOn(item.id);
          sfx(ctx, 'snap');
          ctx.emit('lo:refused', { on: true });
        } else sfx(ctx, 'ui_denied');
        return { refuse: pick(ctx, R.refused) };
      });
      ctx.on('lo:beg', lift);
      ctx.on('buy', lift);
      ctx.every(500, () => { if (refused && ctx.state.clock >= liftAt) lift(); });
      return {};
    },
  },

  /* Now and then the timer falls to nearly nothing. Beg while it is down and it pays. Then back up it goes. */
  lo_tease: {
    mount(ctx) {
      let t = -1, from = 0, isOpen = false;
      const start = () => {
        const q = {};
        ctx.emit('lo:ask', q);
        from = q.seconds || 3600;
        t = 0;
        ctx.say(pick(ctx, R.tease));
      };
      ctx.after(9000, start);
      ctx.every(70000, start);
      return {
        tick(dt) {
          if (t < 0) return;
          t += dt;
          if (t < 3.4) ctx.emit('lo:show', { seconds: 3 + (from - 3) * Math.pow(1 - t / 3.4, 2) });
          else if (t < 5.2) {
            if (!isOpen) { isOpen = true; ctx.emit('lo:window', { on: true }); ctx.emit('lo:show', { seconds: 3 }); const c = buttonCentre(ctx); ctx.float(c.x, c.y - 50, COPY.locked.soClose, `color:${GOOD};font-size:18px;font-weight:700;`); sfx(ctx, 'heartbeat', 0.6); }
          } else {
            isOpen = false;
            t = -1;
            ctx.emit('lo:window', { on: false });
            ctx.emit('lo:show', { seconds: null });
            ctx.emit('lo:add', { minutes: 12 });
            ctx.toast(pick(ctx, R.tease));
          }
        },
      };
    },
  },

  lo_pod: { mount(ctx) { return mountPodcast(ctx, { theme: 'locked', show: 'The Keyholder Report' }); } },

  /* The wheel. It moves pretend time and pays either way. On a phone it opens from a chip. */
  lo_wheel: {
    mount(ctx) {
      if (wide(ctx)) {
        const card = ctx.el('div', 'pointer-events:auto;', null, ctx.layer('side'));
        card.className = 'tb-card';
        card.appendChild(wheelNode(ctx));
        return {};
      }
      const wrap = ctx.parts.buttonWrap;
      const chip = ctx.el('button', 'position:absolute;left:calc(100% + 12px);top:50%;width:68px;height:46px;margin-top:-23px;background:var(--panel);color:var(--ink);border:1px solid var(--line);border-radius:23px;font:700 14px var(--font);touch-action:manipulation;pointer-events:auto;', COPY.locked.wheel, wrap);
      chip.type = 'button';
      const node = wheelNode(ctx);
      // The card keeps its plain grey on every theme, so the wheel's words go dark inside it.
      node.style.color = '#000';
      node.querySelector('button').style.color = '#000';
      chip.addEventListener('click', () => ctx.modal({ title: ctx.item.name, node, buttons: [{ label: 'Close' }] }));
      return {};
    },
  },

  /* The key goes up, comes down, a very small splash. */
  lo_end: {
    mount() { return {}; },
    ending(ctx) {
      return endingStage(ctx, 'background:linear-gradient(#f7c08a,#a98bb0 52%,#5e7aa6);color:#fff;--font:"Segoe UI",system-ui,sans-serif;', 15000, (root) => {
        const b = ctx.bounds(), sea = b.h * 0.62;
        ctx.el('div', `position:absolute;left:0;right:0;top:${sea}px;bottom:0;background:linear-gradient(#35598a,#16304f);`, null, root);
        const key = ctx.el('div', `position:absolute;left:0;top:0;font:700 44px var(--font);color:#ffe7a0;`, null, root);
        key.textContent = '\u{1F5DD}️';
        const rip = ctx.el('div', `position:absolute;left:${b.w * 0.74 - 60}px;top:${sea - 10}px;width:120px;height:22px;box-sizing:border-box;border:2px solid #fff;border-radius:50%;opacity:0;`, null, root);
        const dur = 3000;
        let t = 0, hit = false;
        ctx.frame((dt) => {
          t += dt * 1000;
          const p = Math.min(1, t / dur);
          const x = b.w * 0.14 + b.w * 0.6 * p, y = sea - 30 + 30 * p - b.h * 0.44 * Math.sin(p * Math.PI);
          key.style.transform = `translate(${x - 22}px,${y - 22}px) rotate(${p * 720}deg)`;
          key.style.opacity = p < 1 ? 1 : 0;
          if (p >= 1 && !hit) {
            hit = true;
            ctx.anim(rip, [{ opacity: 0.9, transform: 'scale(.3)' }, { opacity: 0, transform: 'scale(2.8)' }], { duration: 2000, fill: 'forwards' });
            sfx(ctx, 'chip_pop', 0.7);
          }
        });
        endingWords(ctx, root, ENDINGS.lo_end, 'bottom:8%;');
      });
    },
  },
};
