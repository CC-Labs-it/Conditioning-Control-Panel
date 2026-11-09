/* ui/unlocks/extras/emi-unlocks.js - the EMI lane: arrival, pets, chatter, hats, jealousy, the tiny ones, naps, calls,
 * bad gaming and new management. Lines are in data/content/emi.js and fakes.js. */

import { EMI_BOSS_LINES, EMI_BUY_ANY, EMI_BUY_ITEM, EMI_BUY_LANE, EMI_HATS, EMI_HELLO, EMI_IDLE, EMI_JEALOUS, EMI_MINI, EMI_NAP, EMI_PET, EMI_PLAY, EMI_RENAMES, EMI_RENAME_TEMPLATES } from '../../../data/content/emi.js';
import { FAKE_CALL } from '../../../data/content/fakes.js';
import { BY_ID } from '../../../data/items.js';
import { EMI_COPY } from './copy.js';
import { drawFace, getEmi, onEmiTap } from './emi.js';
import { FONT, cardUp, h, pick, picker, slot } from './kit.js';
import { frameRect } from '../../perf.js';

const CALL_MS = 5000;

const HAT_EMOJI = { top: '🎩', party: '🎉', duck: '🦆', globe: '🔮' };
const mmss = (ms) => { const s = Math.floor(ms / 1000); return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`; };

/** Comment on a buy. Item line first, then the lane, then anything. */
export function buyLine(ctx, item, any) {
  if (EMI_BUY_ITEM[item.id]) return EMI_BUY_ITEM[item.id];
  const lane = EMI_BUY_LANE[item.lane];
  return lane && ctx.rng() < 0.6 ? pick(ctx, lane) : any();
}

/** She walks in from the left. */
export function arrive(ctx, e) {
  if (ctx.gentle) { ctx.anim(e.el, [{ opacity: 0 }, { opacity: 1 }], { duration: 400 }); return; }
  const p = e.home();
  ctx.anim(e.el, [{ transform: `translate(${-e.size - 10}px,${p.y}px)` }, { transform: `translate(${p.x + 8}px,${p.y}px)`, offset: 0.8 }, { transform: `translate(${p.x}px,${p.y}px)` }], { duration: 520, easing: 'ease-out' });
}

export const EMI_UNLOCKS = {
  /* EMI moves into the corner and talks. */
  em_head: {
    mount(ctx) {
      const e = getEmi(ctx);
      e.adopt(ctx);
      const fresh = !ctx.store.get('met');
      if (fresh) {
        ctx.store.set('met', 1);
        arrive(ctx, e);
        ctx.after(500, () => { e.mood('glee', 1400); e.say(pick(ctx, EMI_HELLO)); });
      }
      const idle = picker(ctx, EMI_IDLE);
      ctx.every(19000, () => { if (e.pos.s === 1 && !e.napping) e.say(idle(), 3600); });
      const t = onEmiTap(ctx, e, () => {
        if (ctx.has('em_pet') || e.napping || e.blocking) return;
        e.wiggle();
        e.say(idle());
      });
      return { tick() { e.tick(); }, off() { t.off(); } };
    },
  },

  /* The ladder: 3rd pet glee, 4th annoyed, 5th rage. */
  em_pet: {
    mount(ctx) {
      const e = getEmi(ctx);
      let n = 0, lastPet = -1e9, sulk = false;
      const hint = h('button', 'order:-1;', EMI_PET.button, e.controls);
      hint.className = 'xe-b';
      hint.type = 'button';
      const petted = (x, y) => {
        hint.remove();
        const now = ctx.state.clock;
        if (now - lastPet > 8000) n = 0;
        lastPet = now;
        n += 1;
        const stage = EMI_PET.stages[Math.min(n, 5) - 1];
        e.mood(stage.mood, stage.mood === 'rage' ? 4000 : 2200);
        e.say(pick(ctx, stage.lines), 2200);
        e.lastTouch = now;
        ctx.float(x, y - 14, n >= 5 ? '💢' : n === 4 ? '💧' : '💗', 'font-size:22px;');
        if (n >= 5) {
          ctx.audio.play('assets/audio/sfx/thud.mp3', { bus: 'sfx', gain: 0.7 });
          if (!ctx.gentle) ctx.anim(e.el, [{ translate: '-5px 0' }, { translate: '5px 0' }, { translate: '-4px 0' }, { translate: '0 0' }], { duration: 400 });
          sulk = true;
          n = 0;
          ctx.after(7000, () => { if (sulk) { sulk = false; e.say(pick(ctx, EMI_PET.calm)); } });
        } else {
          sulk = false;
          e.wiggle();
          ctx.audio.play('assets/audio/pop/Pop2.mp3', { bus: 'sfx', gain: 0.5 });
        }
      };
      const t = onEmiTap(ctx, e, (ev) => { if (!e.napping && !e.blocking) petted(ev.clientX, ev.clientY); });
      hint.addEventListener('click', () => {
        const r = e.el.getBoundingClientRect();
        petted(r.left + r.width / 2, r.top);
      });
      return { off() { t.off(); hint.remove(); } };
    },
  },

  /* Comments on whatever you buy. */
  em_chat: {
    mount(ctx) {
      const e = getEmi(ctx);
      const any = picker(ctx, EMI_BUY_ANY);
      ctx.on('buy', ({ item }) => { if (item.id !== 'em_chat') ctx.after(260, () => { e.say(buyLine(ctx, item, any), 3200); e.mood('glee', 900); }); });
      return {};
    },
  },

  /* Top hat, party hat, duck, snow globe. One press changes it, and it stays on. */
  em_hat: {
    mount(ctx) {
      const e = getEmi(ctx);
      const btn = h('button', '', EMI_COPY.hatButton, e.controls);
      btn.className = 'xe-b';
      btn.type = 'button';
      const wear = (i, say) => {
        const hat = EMI_HATS[i];
        ctx.store.set('i', i);
        e.hat.textContent = HAT_EMOJI[hat.id];
        ctx.anim(e.hat, [{ transform: 'translateY(-16px)', opacity: 0 }, { transform: 'none', opacity: 1 }], { duration: 260, easing: 'ease-out' });
        if (say) { e.say(hat.line); e.mood('glee', 900); }
      };
      wear(ctx.store.get('i') ?? 0, !ctx.store.get('worn'));
      ctx.store.set('worn', 1);
      btn.addEventListener('click', () => { wear(((ctx.store.get('i') ?? 0) + 1) % EMI_HATS.length, true); e.lastTouch = ctx.state.clock; });
      return { off() { btn.remove(); e.hat.textContent = ''; } };
    },
  },

  /* Ignored for a minute, she stands on a shop row until someone says hello. */
  em_jeal: {
    mount(ctx) {
      const e = getEmi(ctx);
      const IGNORE_MS = 60000, WARN_MS = 40000;
      const label = h('div', '', '', e.el);
      label.className = 'xe-t';
      label.hidden = true;
      const warn = picker(ctx, EMI_JEALOUS.warning), block = picker(ctx, EMI_JEALOUS.blocking);
      e.blocking = false;
      e.lastTouch = ctx.state.clock;
      const standDown = () => {
        e.blocking = false;
        e.controls.style.display = '';
        e.goHome(420);
        e.mood('happy');
        e.say(pick(ctx, EMI_JEALOUS.moved));
        e.lastTouch = ctx.state.clock;
        label.hidden = true;
      };
      const t = onEmiTap(ctx, e, () => { if (e.blocking) standDown(); });
      let warned = false;
      ctx.every(500, () => {
        if (e.held || e.asking || e.blocking || e.napping || cardUp()) return;
        const gone = ctx.state.clock - e.lastTouch;
        label.hidden = gone < WARN_MS;
        if (!label.hidden) label.textContent = EMI_JEALOUS.timer.replace('{n}', mmss(gone));
        if (gone < WARN_MS) warned = false;
        else if (!warned) { warned = true; e.say(warn()); }
        if (gone < IGNORE_MS) return;
        const row = ctx.parts.shop.querySelector('#rows .row');
        if (!row) return;
        const r = row.getBoundingClientRect();
        const s = Math.min(1.8, (r.height + 18) / e.size);
        e.blocking = true;
        e.controls.style.display = 'none';
        label.hidden = true;
        e.mood('rage');
        e.moveTo(r.left + r.width * 0.55, r.top - 9, s, 500);
        e.say(block(), 4000);
        ctx.audio.play('assets/audio/sfx/thud.mp3', { bus: 'sfx', gain: 0.6 });
      });
      return { off() { t.off(); label.remove(); e.blocking = false; } };
    },
  },

  /* Three small ones walk the floor and now and then run up and press the button. */
  em_mini: {
    mount(ctx) {
      const size = 30;
      const line = picker(ctx, EMI_MINI);
      const minis = [0, 1, 2].map((i) => {
        const el = ctx.el('img', `position:absolute;left:0;top:0;width:${size}px;height:${size}px;image-rendering:pixelated;will-change:transform;`, null, 'floaters');
        el.src = 'assets/img/emi/body.png';
        el.alt = '';
        const m = { el, x: 40 + i * 90, y: 0, tx: 0, ty: 0, state: 'walk', wait: 0, dir: i % 2 ? -1 : 1 };
        return m;
      });
      const floorY = () => ctx.parts.buttonWrap.getBoundingClientRect().bottom + 22 - size;
      for (const m of minis) { m.y = floorY(); m.x = Math.min(ctx.bounds().w - size, m.x); }
      let turn = 0;
      ctx.every(3400, () => {
        const m = minis[turn++ % minis.length];
        const b = frameRect(ctx.button.el);
        m.tx = b.left + ctx.rng() * (b.width - size);
        m.ty = b.top + b.height - size + 6;
        m.state = 'run';
      });
      ctx.every(47000, () => ctx.say(line()));
      if (!ctx.store.get('said')) { ctx.store.set('said', 1); ctx.after(400, () => ctx.say(line())); }
      return {
        tick(dt) {
          const w = ctx.bounds().w, speed = ctx.gentle ? 40 : 90;
          for (const m of minis) {
            if (m.state === 'run') {
              const dx = m.tx - m.x, dy = m.ty - m.y, d = Math.hypot(dx, dy);
              if (d < 4) {
                const gain = ctx.button.press();
                if (gain) ctx.float(m.x + size / 2, m.y - 8, `+${ctx.fmt(gain)}`, 'color:var(--stamp);');
                m.state = 'back';
              } else { const st = Math.min(d, speed * 2 * dt); m.x += (dx / d) * st; m.y += (dy / d) * st; }
            } else if (m.state === 'back') {
              const fy = floorY();
              if (Math.abs(m.y - fy) < 3) { m.y = fy; m.state = 'walk'; } else m.y += Math.sign(fy - m.y) * speed * dt;
            } else {
              m.x += m.dir * speed * 0.3 * dt;
              if (m.x < 2) { m.x = 2; m.dir = 1; } else if (m.x > w - size - 2) { m.x = w - size - 2; m.dir = -1; }
            }
            m.el.style.transform = `translate(${m.x}px,${m.y}px) scaleX(${m.dir})`;
          }
        },
      };
    },
  },

  /* She falls asleep on the counter. A tap wakes her and she pays you to go away. */
  em_nap: {
    mount(ctx) {
      const e = getEmi(ctx);
      const asleep = picker(ctx, EMI_NAP.asleep), woken = picker(ctx, EMI_NAP.woken);
      let wake = null, snore = null;
      const stop = () => { e.napping = false; e.controls.style.display = ''; snore?.(); wake?.(); snore = wake = null; };
      const lie = () => {
        if (e.held || e.asking || e.napping || e.blocking || cardUp()) return;
        const c = ctx.parts.counter?.querySelector('#count')?.getBoundingClientRect();
        if (!c) return;
        e.napping = true;
        e.controls.style.display = 'none';
        /* Curled up to the left of the number, with room for it to grow, so it stays readable. */
        const s = Math.min(1, 46 / e.size);
        const y = c.bottom - e.size * s - 4;
        e.moveTo(Math.max(2, c.left - e.size * s - 26), y, s, 600);
        e.mood('sleep');
        e.say(asleep(), 2600);
        snore = ctx.every(1600, () => {
          const r = e.el.getBoundingClientRect();
          ctx.float(r.right - 4, r.top + 4, 'z', 'color:var(--ink-soft);font-size:17px;');
        });
        wake = ctx.after(22000, () => { stop(); e.goHome(500); e.mood('happy'); e.lastTouch = ctx.state.clock; });
      };
      const t = onEmiTap(ctx, e, () => {
        if (!e.napping) return;
        stop();
        const r = e.el.getBoundingClientRect();
        ctx.pay(ctx.rates().fps * 20, 'em_nap', { x: r.left + r.width / 2, y: r.top });
        ctx.audio.play('assets/audio/sfx/golden_pop.mp3', { bus: 'sfx', gain: 0.7 });
        e.mood('rage', 1600);
        e.say(woken(), 2400);
        if (!ctx.gentle) ctx.anim(e.el, [{ translate: '0 -10px' }, { translate: '0 0' }], { duration: 240, easing: 'ease-out' });
        ctx.after(900, () => e.goHome(500));
      });
      ctx.after(ctx.store.get('did') ? 45000 : 2500, lie);
      ctx.store.set('did', 1);
      ctx.every(85000, lie);
      return { off() { t.off(); stop(); } };
    },
  },

  /* A call from EMI fills the screen. It is plainly EMI, it has one button, and it gives up by itself after a few seconds:
   * ignoring it costs nothing and never holds the page. */
  em_call: {
    mount(ctx) {
      getEmi(ctx);
      const lineOf = picker(ctx, FAKE_CALL.lines);
      const hung = picker(ctx, FAKE_CALL.afterHangUp);
      const host = ctx.el('div', 'position:absolute;inset:0;z-index:20;display:none;', null, 'over');
      const panel = h('div', `position:absolute;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:14px;padding:24px 16px;background:#1b1626;color:#fff;font:600 17px/1.25 ${FONT};text-align:center;pointer-events:auto;`, null, host);
      const who = h('div', 'font-size:24px;', FAKE_CALL.caller, panel);
      const big = Math.min(230, Math.round(ctx.bounds().w * 0.5));
      const ring = h('div', `position:relative;width:${big}px;height:${big}px;font-size:${big / 36}px;background:radial-gradient(circle,#46305c,#1b1626 70%);`, null, panel);
      const pic = h('img', 'position:absolute;inset:0;width:100%;height:100%;image-rendering:pixelated;', null, ring);
      pic.src = 'assets/img/emi/body-pet.png';
      pic.alt = 'EMI';
      drawFace(ring, 'glee');
      const words = h('div', 'min-height:46px;max-width:420px;font-weight:500;', '', panel);
      const end = h('button', `min-width:150px;min-height:58px;border-radius:29px;background:#e23b4a;color:#fff;border:0;font:700 18px ${FONT};touch-action:manipulation;`, FAKE_CALL.hangUp, panel);
      end.type = 'button';
      let cancels = [], up = false, pulse = null;
      const close = (rude) => {
        if (!up) return;
        up = false;
        for (const c of cancels) c();
        cancels = [];
        pulse?.cancel();
        const out = ctx.anim(host, [{ opacity: 1 }, { opacity: 0 }], { duration: 180 });
        out.onfinish = () => { host.style.display = 'none'; };
        if (rude) ctx.toast(hung());
      };
      const ringNow = () => {
        if (up || cardUp()) return false;
        up = true;
        host.style.display = 'block';
        who.textContent = FAKE_CALL.caller;
        words.textContent = lineOf();
        ctx.anim(host, [{ opacity: 0 }, { opacity: 1 }], { duration: 220 });
        ctx.audio.play('assets/audio/sfx/chime2.mp3', { bus: 'sfx', gain: 0.7 });
        if (!ctx.gentle) pulse = ctx.anim(ring, [{ transform: 'scale(1)' }, { transform: 'scale(1.08)' }], { duration: 700, iterations: Infinity, direction: 'alternate', easing: 'ease-in-out' });
        cancels = [ctx.every(3200, () => { words.textContent = lineOf(); }), ctx.after(CALL_MS, () => close(false))];
        return true;
      };
      end.addEventListener('click', () => close(true));
      ctx.after(ctx.store.get('rang') ? 90000 : 1500, ringNow);
      ctx.store.set('rang', 1);
      ctx.every(170000, ringNow);
      return { ringNow, off() { close(false); } };
    },
  },

  /* EMI plays a side game by herself, badly, and gets paid for the hits. */
  em_play: {
    mount(ctx) {
      const e = getEmi(ctx);
      const { el, wide } = slot(ctx, `position:relative;width:${wide0(ctx) ? 170 : 104}px;height:${wide0(ctx) ? 128 : 66}px;background:#111a2e;border:2px solid #333;overflow:hidden;`);
      const W = wide ? 166 : 100, H = wide ? 124 : 62;
      const head = h('div', `position:absolute;left:4px;top:2px;color:#ff9ed2;font:600 11px ${FONT};white-space:nowrap;`, '', el);
      const ball = h('div', 'position:absolute;left:0;top:0;width:8px;height:8px;border-radius:50%;background:#ffe36b;will-change:transform;', null, el);
      const padW = wide ? 36 : 24;
      const pad = h('div', `position:absolute;left:0;bottom:4px;width:${padW}px;height:6px;background:#ff69b4;will-change:transform;`, null, el);
      let x = W / 2, y = 20, vx = wide ? 80 : 52, vy = wide ? 70 : 46, px = W / 2;
      const missLine = picker(ctx, EMI_PLAY.miss), hitLine = picker(ctx, EMI_PLAY.hit);
      const show = () => { head.textContent = EMI_PLAY.label.replace('{n}', ctx.fmt(ctx.store.get('miss') || 0)); };
      show();
      if (!ctx.store.get('grab')) { ctx.store.set('grab', 1); ctx.after(300, () => e.say(pick(ctx, EMI_PLAY.grab))); }
      let quiet = 0, wander = 0;
      return {
        tick(dt) {
          const k = ctx.gentle ? 0.5 : 1;
          x += vx * dt * k; y += vy * dt * k;
          if (x < 0) { x = 0; vx = Math.abs(vx); } else if (x > W - 8) { x = W - 8; vx = -Math.abs(vx); }
          if (y < 0) { y = 0; vy = Math.abs(vy); }
          /* She aims at where the ball was, late, and sometimes at nothing. */
          wander -= dt;
          if (wander <= 0) wander = 0.9 + ctx.rng() * 1.2;
          const aim = wander > 1.2 ? W / 2 + Math.sin(ctx.state.clock / 300) * W * 0.4 : x;
          px += Math.sign(aim - px) * Math.min(Math.abs(aim - px), 55 * dt);
          px = Math.max(padW / 2, Math.min(W - padW / 2, px));
          const floor = H - 18;
          if (y >= floor && vy > 0) {
            vy = -Math.abs(vy);
            y = floor;
            const hit = Math.abs(x + 4 - px) < padW / 2 + 3;
            quiet -= 1;
            if (hit) {
              ctx.pay(ctx.rates().fps, 'em_play');
              if (quiet <= 0) { e.say(hitLine(), 2000); e.mood('glee', 800); quiet = 6; }
            } else {
              ctx.store.set('miss', (ctx.store.get('miss') || 0) + 1);
              show();
              if (quiet <= 0) { e.say(missLine(), 2000); e.mood('annoyed', 900); quiet = 6; }
            }
          }
          ball.style.transform = `translate(${x}px,${y}px)`;
          pad.style.transform = `translateX(${px - padW / 2}px)`;
        },
      };
    },
  },

  /* New management: she renames shop rows, one at a time, and the names stick for this run. */
  em_boss: {
    mount(ctx) {
      const e = getEmi(ctx);
      const line = picker(ctx, EMI_BOSS_LINES);
      const names = () => ctx.store.get('n') || {};
      const nameOf = (id) => {
        const it = BY_ID[id];
        return EMI_RENAMES[id] || pick(ctx, EMI_RENAME_TEMPLATES).replaceAll('{name}', it?.name || id);
      };
      /* The shop builds rows with their catalog name, so a kept name goes back on each time a row appears. */
      const apply = () => {
        const kept = names();
        for (const row of ctx.parts.shop.querySelectorAll('#rows .row, #goal .row')) {
          const n = kept[row.dataset.id];
          const b = n && row.querySelector('b');
          if (b && b.textContent !== n) b.textContent = ctx.brand.text(n);
        }
      };
      const renameOne = () => {
        if (cardUp()) return;
        const kept = names();
        const rows = [...ctx.parts.shop.querySelectorAll('#rows .row, #goal .row')];
        const row = rows.find((r) => !kept[r.dataset.id]);
        if (!row) return;
        kept[row.dataset.id] = nameOf(row.dataset.id);
        ctx.store.set('n', kept);
        apply();
        row.style.background = '#ffdcee';
        ctx.anim(row, [{ transform: 'translateX(6px)' }, { transform: 'none' }], { duration: 280, easing: 'ease-out' });
        ctx.after(900, () => { row.style.background = ''; });
        if (ctx.rng() < 0.4) { e.say(line(), 2600); e.mood('glee', 800); }
      };
      ctx.every(240, apply);
      ctx.every(6000, renameOne);
      if (!ctx.store.get('crown')) { ctx.store.set('crown', 1); ctx.after(500, () => { e.say(EMI_BOSS_LINES[0]); renameOne(); }); }
      apply();
      return {};
    },
  },
};

function wide0() { return true; }   // a dock slot has the room on every screen
