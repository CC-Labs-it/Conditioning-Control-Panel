/* ============================================================================
 * ui/unlocks/prestige/boosts.js - Renewal Sky boost lanes: Pay, Idle Perks, Active Play, Lucky Breaks
 * (brief: handoff-1109/TC-PRESTIGE-WIRE-BRIEF.md; star texts in data/prestige.js, the `plain` line is the spec).
 *
 * The numbers live in the pure helpers below, so tests and the hooks inside other features read the same thing.
 * Each helper takes `p` = ctx.prestige (`has(id)`, `dust`); a missing p reads as no stars.
 *
 * Stacking: an `x` star multiplies, the `+%` stars of one lane add up first. Pay at full: 1.25 x 2.7 x 2 = 6.75.
 *
 * Rate stars go through ctx.mod with `post: true`: they are the player's own permanent multipliers, so they should
 * fold AFTER the MOD_*_KNEE soft cap (MOD_SLOPE 0.2 would eat them) and still pay away time. Until sim/economy.js reads
 * `post` (patch in the lane report) they fold with the steady mods, weaker but correct in sign.
 * Away stars use two mod fields the economy does not read yet either: `away(rate)` and `awayCap(ms)` (same patch).
 *
 * Hooks in other features (one marked line each) import from here: prize-wheel.js (wheelDraw, prizeMult),
 * side.js (goldGapMult, goldStay, frenzyMult, prizeMult, boxHoldMs, autopilotShare), toy-box.js (peekRate, prizeMult),
 * trunk.js (critMult).
 * ==========================================================================*/

import { CONFIG } from '../../../data/config.js';
import { SECTORS } from '../../../sim/prize-wheel.js';

const has = (p, id) => !!(p && typeof p.has === 'function' && p.has(id));
const sumPct = (p, list) => list.reduce((a, [id, v]) => a + (has(p, id) ? v : 0), 0);
const num = (v) => (Number.isFinite(v) ? v : 0);

/* ---------- Pay ---------- */
export const PAY_PCT = Object.freeze([['pay_2', 0.1], ['pay_3', 0.1], ['pay_5', 0.15], ['pay_6', 0.15], ['pay_8', 0.2], ['pay_9', 0.2]]);
/** Compound: +0.5% per unspent Stardust, up to +50%. */
export const compoundPct = (p) => (has(p, 'pay_4') ? Math.min(0.5, 0.005 * Math.max(0, Math.floor(num(p.dust)))) : 0);
/** Interest: +1% a minute of saving, up to +30%. */
export const interestPct = (p, saveMs) => (has(p, 'pay_7') ? Math.min(0.3, 0.01 * Math.max(0, num(saveMs)) / 60000) : 0);
export function payMult(p, saveMs = 0) {
  const pct = sumPct(p, PAY_PCT) + compoundPct(p) + interestPct(p, saveMs);
  return (has(p, 'pay_1') ? 1.25 : 1) * (1 + pct) * (has(p, 'pay_10') ? 2 : 1);
}

/* ---------- Idle Perks ---------- */
export const IDLE_PCT = Object.freeze([['idle_3', 0.1], ['idle_5', 0.15], ['idle_8', 0.2]]);
export const idleMult = (p) => (has(p, 'idle_1') ? 1.25 : 1) * (1 + sumPct(p, IDLE_PCT));
/** Snooze +25%, Long Nap +50%: on the away rate only. */
export const awayMult = (p) => 1 + (has(p, 'idle_2') ? 0.25 : 0) + (has(p, 'idle_9') ? 0.5 : 0);
/** Night Shift: one absence pays up to 16 h instead of 8. */
export const awayCapMs = (p) => (has(p, 'idle_4') ? 2 * CONFIG.AWAY_CAP_MS : CONFIG.AWAY_CAP_MS);
/** Autopilot: a toy left alone still pays this share of its reward. */
export const autopilotShare = (p) => (has(p, 'idle_6') ? 0.5 : 0);
export const LET_GO_MS = 20000, LET_GO_MULT = 3, SLEEPWALK_MS = 30000;

/** Sleepwalk's pick: the cheapest plain row the bank covers. Forks and endings never buy themselves. */
export function cheapestAffordable(rows, priceOf, funds) {
  let best = null, bestCost = Infinity;
  for (const it of rows || []) {
    if (!it || it.kind !== 'item') continue;
    const c = priceOf(it.id);
    if (Number.isFinite(c) && c <= funds && c < bestCost) { best = it; bestCost = c; }
  }
  return best;
}

/* ---------- Active Play ---------- */
export const ACTIVE_PCT = Object.freeze([['active_2', 0.1], ['active_5', 0.15], ['active_8', 0.2]]);
export const tapMult = (p) => (has(p, 'active_1') ? 1.25 : 1) * (1 + sumPct(p, ACTIVE_PCT));
/** Crit Chance: Lucky Taps happen 5% more often. */
export const critMult = (p) => (has(p, 'active_3') ? 1.05 : 1);
/** Golden Habit: 25% more often = the gap between golds / 1.25. */
export const goldGapMult = (p) => (has(p, 'active_4') ? 1 / 1.25 : 1);
/** Long Gold: on screen 50% longer = its path runs at 1 / 1.5. */
export const goldStay = (p) => (has(p, 'active_9') ? 1.5 : 1);
/** Frenzy Fingers: Frenzy lasts twice as long. */
export const frenzyMult = (p) => (has(p, 'active_10') ? 2 : 1);
export const COMBO_GAP_MS = 1000, COMBO_STEP = 0.02, COMBO_MAX = 0.5;
export const comboBonus = (streak) => Math.min(COMBO_MAX, Math.max(0, Math.floor(num(streak))) * COMBO_STEP);
export const COUNT_FROM = 10, COUNT_BEAT_MS = 1000, COUNT_MIN_S = 150, COUNT_MAX_S = 300;
/** A count is perfect when every number got exactly one tap on its beat. */
export function countVerdict(hits, extra) { return { perfect: hits >= COUNT_FROM && extra === 0, hits, extra }; }

/* ---------- Lucky Breaks ---------- */
export const LUCK_PCT = Object.freeze([['luck_3', 0.1], ['luck_6', 0.15], ['luck_9', 0.2]]);
export const prizeMult = (p) => (has(p, 'luck_1') ? 1.25 : 1) * (1 + sumPct(p, LUCK_PCT));
/** Peek +10%, Peek Again +15%: the closed Toy Box counts down this much faster. */
export const peekRate = (p) => 1 + (has(p, 'luck_2') ? 0.1 : 0) + (has(p, 'luck_5') ? 0.15 : 0);
/** Hold On: the Mystery Box waits 2 s longer. */
export const boxHoldMs = (p) => (has(p, 'luck_8') ? 2000 : 0);

const span = (id) => { const s = SECTORS.find((x) => x.id === id); return [s.start / 360, s.end / 360]; };
export const wheelMultAt = (r) => (SECTORS.find((s) => r * 360 < s.end) || SECTORS[SECTORS.length - 1]).mult;
/** No Nothing: the same draw squeezed past the Nothing wedge, so the other odds keep their proportions. */
export function skipNothing(r) {
  const [a, b] = span('nothing'), w = b - a;
  const s = Math.min(1 - Number.EPSILON, Math.max(0, num(r))) * (1 - w);
  return s >= a ? s + w : s;
}
export const JACKPOT_KEY = 'prestigeJackpot';
/** The random number the Prize Wheel lands on. Jackpot: the first spin of a run is the top wedge (kept in the wheel's
 *  own run store under JACKPOT_KEY, so a new run gets it again). Second Spin: two draws, the better wedge wins. */
export function wheelDraw(p, rng, store = null) {
  if (has(p, 'luck_10') && store && !store.get(JACKPOT_KEY)) {
    store.set(JACKPOT_KEY, true);
    const [a, b] = span('jackpot');
    return a + (b - a) * (0.25 + 0.5 * rng());
  }
  const one = () => (has(p, 'luck_7') ? skipNothing(rng()) : rng());
  let r = one();
  if (has(p, 'luck_4')) { const r2 = one(); if (wheelMultAt(r2) > wheelMultAt(r)) r = r2; }
  return r;
}

/* ---------- shared bits for the entries ---------- */
const sound = (ctx, file, gain) => { if (ctx.has('sound') && ctx.switches.isOn('sound')) ctx.audio.play(`assets/audio/${file}`, { bus: 'sfx', gain }); };
const calm = (ctx) => ctx.gentle || ctx.reduced;
const PAPER = 'background:#fffaf0;color:#2a2230;border:2px solid #2a2230;border-radius:14px 10px 15px 9px / 9px 14px 10px 15px;font-family:var(--font, inherit);';
/* One lamp top-left: the shadow sits down-right and never moves with the piece (the pieces never rotate either). */
const SHADOW = 'box-shadow:3px 4px 0 rgba(42,34,48,.22);';

function chip(ctx, css) {
  const el = ctx.el('div', `position:absolute;${PAPER}${SHADOW}padding:3px 10px;font-size:13px;line-height:1.2;white-space:nowrap;opacity:0;pointer-events:none;${css}`, null, 'meters');
  let on = false, breath = null;
  return {
    el,
    get on() { return on; },
    show(text) {
      if (text != null) { const t = ctx.brand.text(text); if (el.textContent !== t) el.textContent = t; }
      if (on) return;
      on = true;
      ctx.anim(el, [{ opacity: 0 }, { opacity: 1 }], { duration: 600, fill: 'forwards', easing: 'ease-out' });
      if (!calm(ctx)) breath = ctx.anim(el, [{ opacity: 1 }, { opacity: 0.72 }, { opacity: 1 }], { duration: 4000, delay: 600, iterations: Infinity });
    },
    hide() {
      if (!on) return;
      on = false;
      breath?.cancel(); breath = null;
      ctx.anim(el, [{ opacity: 1 }, { opacity: 0 }], { duration: 400, fill: 'forwards' });
    },
  };
}

/* ---------- entries ---------- */
export default {
  /* Pay: one multiplier on everything a run earns. Interest grows while the next goal row is out of reach. */
  pay: {
    mount(ctx) {
      let saveMs = 0;
      if (has(ctx.prestige, 'pay_7')) {
        const saving = () => {
          const g = typeof ctx.shop === 'function' ? ctx.shop(1)?.goal : null;
          if (!g || typeof ctx.price !== 'function') return true;  // no shop door on ctx: time since the last buy
          return ctx.price(g.id) > ctx.state.fuzz;
        };
        ctx.on('buy', () => { saveMs = 0; });
        ctx.every(1000, () => { if (saving()) saveMs = Math.min(saveMs + 1000, 30 * 60000); });
      }
      const m = () => payMult(ctx.prestige, saveMs);
      ctx.mod({ post: true, fps: (v) => v * m(), click: (v) => v * m(), wall: (v) => v * m() });
      return {};
    },
  },

  /* Idle Perks: per-second pay, away pay, Let Go, Sleepwalk. Autopilot lives in the toy hooks (autopilotShare). */
  idle: {
    mount(ctx) {
      const p = ctx.prestige;
      ctx.mod({ post: true, fps: (v) => v * idleMult(p) });
      ctx.mod({ away: (r) => r * awayMult(p), awayCap: (ms) => Math.max(ms, awayCapMs(p)) });

      if (has(p, 'idle_10')) {
        /* Let Go: 20 s of game time with no input and per-second pay goes x3 until the next press, key or wheel. */
        let still = 0, letGo = false;
        const cue = chip(ctx, 'left:50%;top:6px;transform:translateX(-50%);');
        const touch = () => { still = 0; if (letGo) { letGo = false; cue.hide(); } };
        const doc = ctx.button.el.ownerDocument;
        const EVENTS = ['pointerdown', 'keydown', 'wheel'];
        for (const ev of EVENTS) doc.addEventListener(ev, touch, { capture: true, passive: true });
        ctx.cleanup(() => { for (const ev of EVENTS) doc.removeEventListener(ev, touch, { capture: true }); });
        ctx.mod({ timed: true, fps: (v) => (letGo ? v * LET_GO_MULT : v) });
        ctx.frame((dt) => {
          if (letGo) return;
          still += dt * 1000;
          if (still >= LET_GO_MS) { letGo = true; cue.show(`Letting go: per second x${LET_GO_MULT}`); }
        });
      }

      if (has(p, 'idle_7')) {
        /* Sleepwalk: every 30 s the cheapest row the bank covers buys itself. Needs the shop door on ctx (Core patch). */
        ctx.every(SLEEPWALK_MS, () => {
          if (typeof ctx.shop !== 'function' || typeof ctx.buy !== 'function' || typeof ctx.price !== 'function') return;
          const w = ctx.shop(12);
          const it = cheapestAffordable([w?.goal, ...(w?.rows || [])], (id) => ctx.price(id), ctx.state.fuzz);
          if (it && ctx.buy(it.id)?.ok) ctx.toast(`Sleepwalk bought ${it.name}.`);
        });
      }
      return {};
    },
  },

  /* Active Play: tap pay, Combo, Countdown. Crit and the Golden Button stars are hooks (critMult, gold*). */
  active: {
    mount(ctx) {
      const p = ctx.prestige;
      let now = 0, combo = null;
      ctx.mod({ post: true, click: (v) => v * tapMult(p) });

      if (has(p, 'active_6')) {
        /* Combo: manual taps less than a second apart build a streak, +2% a tap up to +50%. */
        let streak = 0, lastTap = -Infinity;
        const cue = chip(ctx, 'left:50%;bottom:4px;transform:translateX(-50%);');
        const live = (t) => t - lastTap <= COMBO_GAP_MS;
        ctx.mod({ timed: true, click: (v, s, t) => (live(t) ? v * (1 + comboBonus(streak)) : v) });
        ctx.on('tap', ({ auto }) => {
          if (auto) return;
          streak = live(now) ? streak + 1 : 1;
          lastTap = now;
          const b = comboBonus(streak);
          if (b > 0) cue.show(`Streak +${Math.round(b * 100)}%`);
        });
        combo = () => { if (streak && !live(now)) { streak = 0; cue.hide(); } };
      }

      if (has(p, 'active_7')) mountCountdown(ctx);

      return { tick(dt, t) { now = t; combo?.(); } };
    },
  },

  /* Lucky Breaks: everything is in the hooks (prizeMult, peekRate, boxHoldMs, wheelDraw). Nothing to draw. */
  luck: { mount() { return {}; } },
};

/* Countdown: now and then a paper card counts 10 to 1, one number a second (1 Hz; fades only under Gentle).
 * One tap per number while it shows. All ten and no extra tap pays a full minute of per-second income. */
export function mountCountdown(ctx, firstMs = null) {
  ctx.css(`.fz-count{position:absolute;left:18px;top:34%;width:116px;padding:8px 10px 10px;${PAPER}${SHADOW}text-align:center;pointer-events:auto;touch-action:manipulation;user-select:none;cursor:pointer;}
.fz-count small{display:block;font-size:12px;opacity:.75;}
.fz-count b{display:block;font-size:54px;line-height:1;font-weight:800;color:var(--accent-deep, #c2185b);text-shadow:1px 1px 0 rgba(42,34,48,.25);}
.fz-count i{display:block;font-style:normal;font-size:11px;letter-spacing:2px;min-height:14px;}`);
  let card = null;
  const later = () => (COUNT_MIN_S + ctx.rng() * (COUNT_MAX_S - COUNT_MIN_S)) * 1000;
  const schedule = () => ctx.after(later(), start);

  function start() {
    if (card) return schedule();
    card = ctx.el('div', '', null, 'over');
    card.className = 'fz-count';
    ctx.el('small', '', 'Count with me', card);
    const big = ctx.el('b', '', String(COUNT_FROM), card);
    const marks = ctx.el('i', '', '', card);
    let n = COUNT_FROM, hit = false, hits = 0, extra = 0, open = true;
    ctx.anim(card, [{ opacity: 0 }, { opacity: 1 }], { duration: calm(ctx) ? 400 : 250, fill: 'forwards' });
    card.addEventListener('pointerdown', (e) => {
      e.preventDefault();
      if (!open || ctx.paused) return;
      if (hit) { extra += 1; marks.textContent += 'x'; return; }
      hit = true; hits += 1; marks.textContent += '.';
      sound(ctx, 'sfx/count_tick.mp3', 0.12);
      if (!calm(ctx)) ctx.anim(big, [{ transform: 'scale(1.18)' }, { transform: 'none' }], { duration: 220, easing: 'ease-out' });
    });
    const finish = () => {
      open = false;
      const v = countVerdict(hits, extra);
      const r = card.getBoundingClientRect();
      if (v.perfect) {
        const rt = ctx.rates();  // a minute of income, with the same floor every other prize uses (side.js worth)
        const got = ctx.pay(Math.max(rt.fps, rt.click * 2, 2) * 60, 'countdown', { x: r.left + r.width / 2, y: r.top });
        ctx.toast(`Perfect count. +${ctx.fmt(got)}`);
        sound(ctx, 'sfx/rank_settle.mp3', 0.3);
      } else {
        ctx.toast(hits ? `${hits} of ${COUNT_FROM}. Close.` : 'The count went by.');
      }
      const me = card; card = null;
      const a = ctx.anim(me, [{ opacity: 1 }, { opacity: 0 }], { duration: 400, fill: 'forwards' });
      a.onfinish = a.oncancel = () => me.remove();
      schedule();
    };
    const beat = () => {
      if (n <= 1) return finish();
      n -= 1; hit = false;
      big.textContent = String(n);
      ctx.anim(big, calm(ctx) ? [{ opacity: 0.4 }, { opacity: 1 }] : [{ transform: 'translateY(-6px)', opacity: 0.5 }, { transform: 'none', opacity: 1 }], { duration: calm(ctx) ? 300 : 180 });
      ctx.after(COUNT_BEAT_MS, beat);
    };
    ctx.after(COUNT_BEAT_MS, beat);
  }
  if (firstMs != null) ctx.after(firstMs, start); else schedule();
}
